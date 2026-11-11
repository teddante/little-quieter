using System.Reflection;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace LittleQuieter;

internal static partial class Checks
{
    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child))
                yield return nested;
        }
    }

    private static void DeviceSelectionChecks()
    {
        var devices = new List<AudioDevice>
        {
            new AudioDevice { Name = AudioDevice.DefaultMicrophoneName },
            new AudioDevice { Id = "mic-one", Name = "USB microphone" },
            new AudioDevice { Id = "mic-two", Name = "USB microphone" },
            new AudioDevice { Id = "mic-three", Name = "Renamed microphone" }
        };
        Assert(AudioDevice.Select(devices, "mic-three", "Old microphone name", AudioDevice.DefaultMicrophoneName).Name == "Renamed microphone", "microphone identity survives a Windows device rename");
        Assert(AudioDevice.Select(devices, "disconnected", "USB microphone", AudioDevice.DefaultMicrophoneName) is { Id: "disconnected", Unavailable: true }, "missing device IDs never fall back to a matching name or default");
        Assert(AudioDevice.Select(devices, null, "USB microphone", AudioDevice.DefaultMicrophoneName).Unavailable, "ambiguous legacy microphone names require an explicit selection");
        Assert(AudioDevice.Select(devices, null, "Renamed microphone", AudioDevice.DefaultMicrophoneName).Id == "mic-three", "unique legacy microphone selections migrate to stable endpoint IDs");
        Assert(AudioDevice.Select(devices, null, "Disconnected legacy microphone", AudioDevice.DefaultMicrophoneName).Unavailable, "disconnected legacy microphone selections remain unavailable");
        Assert(AudioDevice.Select(devices, null, AudioDevice.DefaultMicrophoneName, AudioDevice.DefaultMicrophoneName) == devices[0], "an explicit Windows default choice continues following the default");
    }

    private static void CheckWindowImprovements(MainWindow window, string path)
    {
        var microphone = Descendants(window).OfType<ComboBox>().Single(control => control.AccessibleName == "Microphone");
        Assert(microphone.SelectedItem is AudioDevice { Id: "missing-test-mic", Unavailable: true }, "window preserves disconnected microphone selection");
        Finish(window.ToggleListening());
        Assert(!window.IsListening && Preferences.Load(path).DeviceId == "missing-test-mic", "an unavailable microphone cannot open audio or overwrite the chosen endpoint");
        Assert(window.FormBorderStyle == FormBorderStyle.FixedSingle && !window.MaximizeBox && window.MinimizeBox,
            "main window prevents manual resizing and maximizing while retaining minimize-to-tray");
        var meter = Descendants(window).OfType<ProgressBar>().Single();
        _ = meter.Handle;
        meter.Value = 72;
        Assert(meter.AccessibilityObject.Role == AccessibleRole.ProgressBar && meter.AccessibilityObject.Value?.Contains("72") == true,
            "assistive technology can read the live meter value and progress-bar role");
        var reminder = Descendants(window).OfType<CheckBox>().Single(control => control.Text == "Show visual reminder");
        reminder.Checked = false;
        using var popup = new ReminderWindow();
        var dismiss = Descendants(popup).OfType<Button>().Single();
        Assert(!popup.TopMost && popup.ShowInTaskbar && dismiss.TabStop && popup.CancelButton == dismiss, "reminder is not topmost and supports taskbar access, tab navigation and Escape");
        var close = typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var reason in new[] { CloseReason.WindowsShutDown, CloseReason.ApplicationExitCall, CloseReason.FormOwnerClosing, CloseReason.TaskManagerClosing })
        {
            var closing = new FormClosingEventArgs(reason, false);
            close.Invoke(popup, new object[] { closing });
            Assert(!closing.Cancel, "reminder allows closing for " + reason);
        }
        var userClosing = new FormClosingEventArgs(CloseReason.UserClosing, false);
        close.Invoke(popup, new object[] { userClosing });
        Assert(userClosing.Cancel, "closing a reminder from its window still dismisses it for reuse");
        window.ClientSize = new Size(420, 600);
        window.PerformLayout();
        Assert(Descendants(window).OfType<ComboBox>().All(control => control.Width <= 400), "device selectors fit a narrow window");
        Assert(Descendants(window).OfType<CheckBox>().All(control => control.Width <= 400), "long setting labels wrap in a narrow window");
    }

    private static void RecoveryWindowCheck()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-recovery-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.xml");
        File.WriteAllText(path, "original broken settings");
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(true, show, exit, path);
            _ = window.Handle;
            Finish(window.ApplySchedule(DateTime.Now));
            Finish(window.ToggleListening());
            Assert(!window.IsListening && Descendants(window).OfType<Button>().Any(button => button.Text == "Back up and use new settings"),
                "background startup with damaged preferences offers recovery and never starts audio");
            Assert(Descendants(window).OfType<Label>().Any(label => label.Text == "SETTINGS RECOVERY · MICROPHONE OFF") &&
                !Descendants(window).OfType<CheckBox>().Single(control => control.Text == "Listen on a daily schedule").Enabled,
                "recovery status explains the pause and blocks schedule changes until recovery");
            CloseWindow(window);
            Assert(File.ReadAllText(path) == "original broken settings", "window initialization and shutdown preserve the damaged original");
            using var recovered = new MainWindow(true, show, exit, path);
            _ = recovered.Handle;
            try
            {
                // An unavailable selection prevents this ordinary check from opening hardware,
                // even if startup incorrectly tries to listen after recovery.
                var microphone = Descendants(recovered).OfType<ComboBox>().Single(control => control.AccessibleName == "Microphone");
                var missing = new AudioDevice { Id = "missing-recovery-mic", Name = "Disconnected microphone", Unavailable = true };
                microphone.Items.Add(missing);
                microphone.SelectedItem = missing;
                var recover = Descendants(recovered).OfType<Button>().Single(button => button.Text == "Back up and use new settings");
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recover, new object[] { EventArgs.Empty });
                string backup = Directory.GetFiles(folder, "settings.xml.recovery-*").Single();
                Assert(File.ReadAllText(backup) == "original broken settings" && !Preferences.Load(path).NeedsRecovery &&
                    microphone.Enabled && Descendants(recovered).OfType<TrackBar>().Single().Enabled &&
                    Descendants(recovered).OfType<CheckBox>().Single(control => control.Text == "Listen on a daily schedule").Enabled,
                    "recovering settings preserves the original backup and restores settings controls");
                InvokeMonitor(recovered, "TickMonitor");
                Assert(!recovered.IsListening && Descendants(recovered).OfType<Label>().Any(label => label.Text.StartsWith("Original settings backed up to ")),
                    "recovering before the first background tick stays paused without attempting to open the microphone");
            }
            finally { CloseWindow(recovered); }
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(folder))
                File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void InstallationChecks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-install-check-" + Guid.NewGuid().ToString("N"));
        string keyPath = @"Software\LittleQuieter.InstallChecks\" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "source.exe");
        string localData = Path.Combine(folder, "local-é-音");
        string installedFolder = Path.Combine(localData, "Programs", "LittleQuieter");
        string shortcut = Path.Combine(folder, "Little Quieter.lnk");
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        object? startupBefore = run?.GetValue("LittleQuieter");
        try
        {
            File.WriteAllText(source, "test executable - never launched");
            string settings = Path.Combine(folder, "settings.xml");
            File.WriteAllText(settings, "existing personal settings");
            Installation.Install(source, installedFolder, shortcut, keyPath);
            string program = Path.Combine(installedFolder, "Little Quieter.exe");
            using var registration = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert(File.ReadAllText(program) == File.ReadAllText(source) && File.Exists(shortcut), "native installer copies the executable and creates a Start menu shortcut");
            Assert((string?)registration?.GetValue("DisplayName") == "Little Quieter" &&
                ((string?)registration.GetValue("UninstallString"))?.Contains("Uninstall.ps1") == true &&
                File.Exists(Path.Combine(installedFolder, "Uninstall.ps1")), "installation registers removal through Windows Installed apps");
            using (var resource = typeof(Installation).Assembly.GetManifestResourceStream("LittleQuieter.THIRD-PARTY-NOTICES.txt")!)
            using (var reader = new StreamReader(resource))
                Assert(File.ReadAllText(Path.Combine(installedFolder, "THIRD-PARTY-NOTICES.txt")) == reader.ReadToEnd(),
                    "installation includes embedded notices even without a companion file");
            object shellLink = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
            try
            {
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)shellLink).Load(shortcut, 0);
                var target = new System.Text.StringBuilder(260);
                var workingDirectory = new System.Text.StringBuilder(260);
                var link = (ShellShortcut.IShellLink)shellLink;
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
                Assert(target.ToString() == program && workingDirectory.ToString() == installedFolder,
                    "the native Shell shortcut preserves its Unicode target and working directory");
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shellLink); }
            var uninstallPreview = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            uninstallPreview.Environment["LOCALAPPDATA"] = localData;
            foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Restricted", "-Command",
                "& " + (string)registration!.GetValue("UninstallString")! + " -WhatIf" })
                uninstallPreview.ArgumentList.Add(argument);
            using (var preview = System.Diagnostics.Process.Start(uninstallPreview)!)
            {
                if (!preview.WaitForExit(10000))
                {
                    preview.Kill(true);
                    throw new TimeoutException("The isolated uninstall preview did not finish.");
                }
                string output = preview.StandardOutput.ReadToEnd();
                string error = preview.StandardError.ReadToEnd();
                Assert(preview.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) && error.Length == 0 && File.Exists(program),
                    "the registered uninstaller previews successfully under a Restricted parent policy without removing files");
            }
            File.WriteAllText(source, "updated test executable - never launched");
            Installation.Install(source, installedFolder, shortcut, keyPath);
            Assert(File.ReadAllText(program) == File.ReadAllText(source) && File.ReadAllText(program + ".previous") == "test executable - never launched", "updates replace the executable atomically and retain a rollback copy");
            Assert(File.ReadAllText(settings) == "existing personal settings" && Equals(startupBefore, run?.GetValue("LittleQuieter")), "native installation and updates preserve personal settings and startup registration");
            byte[] programBefore = File.ReadAllBytes(program);
            byte[] previousBefore = File.ReadAllBytes(program + ".previous");
            byte[] scriptBefore = File.ReadAllBytes(Path.Combine(installedFolder, "Uninstall.ps1"));
            byte[] shortcutBefore = File.ReadAllBytes(shortcut);
            string? versionBefore = (string?)registration?.GetValue("DisplayVersion");
            File.WriteAllText(source, "failed update - never launched");
            string blocked = Path.Combine(folder, "blocked-shortcut-directory");
            File.WriteAllText(blocked, "prevent shortcut directory creation after file replacement");
            bool failed = false;
            try
            {
                Installation.Install(source, installedFolder, Path.Combine(blocked, "Little Quieter.lnk"), keyPath);
            }
            catch (IOException) { failed = true; }
            Assert(failed && File.ReadAllBytes(program).SequenceEqual(programBefore) &&
                File.ReadAllBytes(program + ".previous").SequenceEqual(previousBefore), "a late installation failure restores both the executable and its previous rollback copy");
            Assert(File.ReadAllBytes(Path.Combine(installedFolder, "Uninstall.ps1")).SequenceEqual(scriptBefore) &&
                File.ReadAllBytes(shortcut).SequenceEqual(shortcutBefore) && (string?)registration?.GetValue("DisplayVersion") == versionBefore,
                "failed updates preserve the uninstaller, shortcut and existing Windows registration");
            string readOnlyScript = Path.Combine(installedFolder, "Uninstall.ps1");
            File.SetAttributes(readOnlyScript, FileAttributes.ReadOnly);
            failed = false;
            try
            {
                Installation.Install(source, installedFolder, shortcut, keyPath);
            }
            catch (UnauthorizedAccessException) { failed = true; }
            finally { File.SetAttributes(readOnlyScript, FileAttributes.Normal); }
            Assert(failed && File.ReadAllBytes(program).SequenceEqual(programBefore), "read-only existing files trigger rollback without breaking the installed executable");
            Assert(File.ReadAllText(settings) == "existing personal settings" && Equals(startupBefore, run?.GetValue("LittleQuieter")) &&
                !Directory.EnumerateDirectories(installedFolder, ".install-*").Any(), "failed updates leave settings and startup intact and clean up staging");
            bool stopRequested = false;
            try
            {
                Installation.Install(source + ".missing", installedFolder, shortcut, keyPath, () => stopRequested = true);
            }
            catch (FileNotFoundException) { }
            Assert(!stopRequested && File.ReadAllBytes(program).SequenceEqual(programBefore),
                "staging failures leave the installed app running and its executable untouched");
            InstallationConcurrencyChecks(folder, installedFolder, shortcut, keyPath);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            string resolved = Path.GetFullPath(folder);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(resolved), temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("LittleQuieter-install-check-", StringComparison.Ordinal))
                throw new IOException("Refusing unexpected installation-check cleanup.");
            Directory.Delete(resolved, true);
        }
    }

    private static void DefaultDeviceChangeChecks()
    {
        Assert(!AudioDeviceChanges.AffectsSelection(DataFlow.Capture, Role.Console, false, false) &&
            !AudioDeviceChanges.AffectsSelection(DataFlow.Render, Role.Console, false, false),
            "fixed input and output selections ignore all unrelated Windows default changes");
        Assert(AudioDeviceChanges.AffectsSelection(DataFlow.Capture, Role.Console, true, false) &&
            !AudioDeviceChanges.AffectsSelection(DataFlow.Render, Role.Console, true, false),
            "following the default microphone reacts only to capture changes");
        Assert(AudioDeviceChanges.AffectsSelection(DataFlow.Render, Role.Console, false, true) &&
            !AudioDeviceChanges.AffectsSelection(DataFlow.Capture, Role.Console, false, true),
            "following the default output reacts only to render changes");
        Assert(!AudioDeviceChanges.AffectsSelection(DataFlow.Capture, Role.Communications, true, true) &&
            !AudioDeviceChanges.AffectsSelection(DataFlow.Render, Role.Multimedia, true, true),
            "other Windows audio roles do not restart the app's console-role streams");
        var pausedAt = new DateTime(2026, 10, 26, 22, 0, 0, DateTimeKind.Local);
        Assert(Installation.ReadPauseTime(Installation.ResumeArguments(false, pausedAt)) == pausedAt &&
            Installation.ReadPauseTime(Installation.ResumeArguments(true, pausedAt)) == null,
            "update restart arguments preserve the exact pause time without altering normal background startup");
        Assert(Installation.ReadPauseTime(new[] { "--paused-after-update=0" }) is DateTime safePause &&
            Math.Abs((safePause - DateTime.Now).TotalSeconds) < 2, "invalid pause timestamps fail paused without calendar underflow");
    }

    private static void InvokeMonitor(MainWindow window, string method)
    {
        if (typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null) is Task operation)
            Finish(operation);
    }

    private static void Finish(Task operation)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!operation.IsCompleted)
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("An asynchronous UI check did not finish.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
        operation.GetAwaiter().GetResult();
        Application.DoEvents();
    }

    private static void CloseWindow(MainWindow window)
    {
        window.Close();
        Finish(window.AudioIdle);
    }

    private static void CheckDisposedWindow(bool listening)
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-dispose-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0, ShowReminder = false }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(true, show, exit, path);
            _ = window.Handle;
            var timer = (System.Windows.Forms.Timer)typeof(MainWindow).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var tray = (NotifyIcon)typeof(MainWindow).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var menu = tray.ContextMenuStrip!;
            var reminder = (ReminderWindow)typeof(MainWindow).GetField("reminder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            if (listening)
            {
                Finish(window.ToggleListening());
                Assert(window.IsListening, "the disposal check starts listening at zero volume");
            }
            window.Dispose();
            Finish(window.AudioIdle);
            Assert(!timer.Enabled && !tray.Visible && menu.IsDisposed && reminder.IsDisposed,
                "disposing a window directly releases its timer, tray menu and reminder");
            if (listening)
                Assert(SpinWait.SpinUntil(() => !AudioDevice.HasActiveSession(DataFlow.Capture) && !AudioDevice.HasActiveSession(DataFlow.Render), 1500),
                    "disposing a listening window releases both audio streams");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckShutdownSaveFailure()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-shutdown-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0 }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(true, show, exit, path);
            _ = window.Handle;
            Descendants(window).OfType<TrackBar>().Single().Value++;
            File.SetAttributes(path, FileAttributes.ReadOnly);
            var closing = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
            typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { closing });
            Assert(!closing.Cancel && Descendants(window).OfType<Label>().Any(label => label.Text.StartsWith("Couldn't save settings:")),
                "a settings write failure does not block Windows shutdown with a modal dialog");
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
            foreach (string file in Directory.EnumerateFiles(folder))
                File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void CheckPreviewOutputChanges()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-preview-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0, ShowReminder = false, ScheduleEnabled = true, ScheduleStartMinute = 0, ScheduleEndMinute = 0 }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            try
            {
                Assert(Descendants(window).OfType<Button>().Single(button => button.Text == "Test tone (2s)").Enabled,
                    "explicit tone testing remains available outside scheduled listening hours");
                InvokeMonitor(window, "PreviewAlarm");
                var alarm = (AlarmTone)typeof(MainWindow).GetField("alarm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Assert(alarm.IsReady && alarm.IsActive && !window.IsListening,
                    "an explicit preview gates the zero-volume test source even when automatic tones and scheduled listening are off");
                alarm.Player.Stop();
                Assert(SpinWait.SpinUntil(() => alarm.Fault != null, 1000), "a stopped preview reports an output fault");
                InvokeMonitor(window, "TickMonitor");
                Assert(Descendants(window).OfType<Label>().Any(label => label.Text.StartsWith("Tone output stopped.")),
                    "output failures are explained while the microphone is paused");
                InvokeMonitor(window, "PreviewAlarm");
                Assert(alarm.IsReady && alarm.Fault == null, "testing again prepares a fresh output after a preview failure");
                var output = Descendants(window).OfType<ComboBox>().Single(control => control.AccessibleName == "Alarm output. Only changes Little Quieter");
                var missing = new AudioDevice { Id = "missing-preview-output", Name = "Missing preview output", Unavailable = true };
                output.Items.Add(missing);
                output.SelectedItem = missing;
                Finish(window.AudioIdle);
                Assert(!alarm.IsReady && !alarm.IsActive, "changing the preview output immediately closes the old endpoint and cancels the preview");
                InvokeMonitor(window, "PreviewAlarm");
                Assert(!alarm.IsReady && alarm.Fault == null &&
                    Descendants(window).OfType<Label>().Any(label => label.Text.StartsWith("Tone unavailable.")),
                    "previewing an unavailable new output reports failure instead of reusing the previous endpoint");
            }
            finally { CloseWindow(window); }
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckUnavailableOutputs()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-output-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0, ShowReminder = false, OutputDeviceName = "Disconnected legacy output" }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            try
            {
                var output = Descendants(window).OfType<ComboBox>().Single(control => control.AccessibleName == "Alarm output. Only changes Little Quieter");
                var alarm = (AlarmTone)typeof(MainWindow).GetField("alarm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Assert(output.SelectedItem is AudioDevice { Id: null, Unavailable: true }, "a missing legacy output remains selected as unavailable");
                InvokeMonitor(window, "PreviewAlarm");
                Assert(!alarm.IsReady && Descendants(window).OfType<Label>().Any(label => label.Text.StartsWith("Tone unavailable.")),
                    "a missing legacy output cannot route a preview to the Windows default");
                Descendants(window).OfType<CheckBox>().Single(control => control.Text == "Play steady alarm tone").Checked = true;
                Finish(window.AudioIdle);
                Assert(!alarm.HasOutput && Descendants(window).OfType<Label>().Any(label => label.Text == "Automatic tone is off."),
                    "disabling automatic sound at zero volume clears an irrelevant output error without opening a stream");
                output.SelectedIndex = -1;
                Finish(window.AudioIdle);
                InvokeMonitor(window, "PreviewAlarm");
                Assert(!alarm.IsReady, "an empty output selection cannot implicitly open the Windows default");
            }
            finally { CloseWindow(window); }
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckCalibrationAudio()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-calibration-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { ToneVolume = 0, ShowReminder = false, Limit = 50 }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            try
            {
                foreach (bool listening in new[] { false, true })
                    foreach (string outcome in new[] { "cancel", "failure", "success", "device change" })
                    {
                        if (window.IsListening != listening)
                            Finish(window.ToggleListening());
                        var limit = Descendants(window).OfType<TrackBar>().Single();
                        limit.Value = 50;
                        InvokeMonitor(window, "ToggleCalibration");
                        Assert(window.IsListening && !limit.Enabled, "learning opens the microphone with alarms suspended");
                        if (outcome == "success")
                        {
                            var readings = (List<double>)typeof(MainWindow).GetField("calibration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                            for (int i = 0; i < 80; i++)
                                readings.Add(-40 + i % 20);
                        }
                        if (outcome == "cancel")
                            InvokeMonitor(window, "ToggleCalibration");
                        else
                        {
                            if (outcome == "device change")
                                typeof(MainWindow).GetField("deviceVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, -1);
                            else
                                typeof(MainWindow).GetField("calibrationEnd", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 0.0);
                            InvokeMonitor(window, "TickMonitor");
                        }
                        Assert(window.IsListening == listening && limit.Enabled && (outcome == "success" ? limit.Value > 50 : limit.Value == 50),
                            "learning " + outcome + " restores the previous " + (listening ? "listening" : "paused") + " state and applies only a successful limit");
                        if (!listening)
                            Assert(!AudioDevice.HasActiveSession(DataFlow.Capture) && !AudioDevice.HasActiveSession(DataFlow.Render) &&
                                Descendants(window).OfType<Label>().Any(label => label.Text == "PAUSED · MICROPHONE OFF"),
                                "finishing learning from paused releases both streams and keeps the paused status");
                    }
            }
            finally { CloseWindow(window); }
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckUpdatePauseAudio()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-update-pause-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        DateTime now = DateTime.Now;
        int minute = (int)now.TimeOfDay.TotalMinutes;
        try
        {
            foreach (bool scheduled in new[] { false, true })
            {
                new Preferences
                {
                    ToneVolume = 0,
                    ShowReminder = false,
                    ScheduleEnabled = scheduled,
                    ScheduleStartMinute = (minute + 1438) % 1440,
                    ScheduleEndMinute = (minute + 2) % 1440
                }.Save(path);
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var window = new MainWindow(true, show, exit, path, now);
                _ = window.Handle;
                try
                {
                    InvokeMonitor(window, "TickMonitor");
                    Assert(!window.IsListening && !AudioDevice.HasActiveSession(DataFlow.Capture) && !AudioDevice.HasActiveSession(DataFlow.Render),
                        "an update preserves a manual pause with scheduling " + (scheduled ? "on" : "off"));
                    Finish(window.ToggleListening());
                    Assert(window.IsListening && Installation.IsCapturing(Environment.ProcessId),
                        "manual listening still resumes after a paused update at zero test volume");
                    Finish(window.ToggleListening());
                    if (scheduled)
                    {
                        Finish(window.ApplySchedule(now.AddDays(1)));
                        Assert(window.IsListening, "the next scheduled period resumes after a pause preserved through update");
                    }
                }
                finally { CloseWindow(window); }
            }
            new Preferences
            {
                ToneVolume = 0,
                ShowReminder = false,
                ScheduleEnabled = true,
                ScheduleStartMinute = (minute + 1438) % 1440,
                ScheduleEndMinute = (minute + 2) % 1440
            }.Save(path);
            using var nextShow = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var nextExit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var next = new MainWindow(true, nextShow, nextExit, path, now.AddDays(-1));
            _ = next.Handle;
            try
            {
                InvokeMonitor(next, "TickMonitor");
                Assert(next.IsListening, "a pause from the previous daily period does not suppress today's scheduled listening");
            }
            finally { CloseWindow(next); }
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void PreviewWindow(string imagePath, bool settingsExpanded, bool narrow)
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-preview-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0 }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            // Create visible child controls for WM_PRINT while keeping the native window transparent,
            // disabled and out of the taskbar so the render cannot interrupt the user's work.
            window.Opacity = 0;
            window.Enabled = false;
            window.ShowInTaskbar = false;
            window.Show();
            window.Enabled = true;
            Application.DoEvents();
            if (settingsExpanded)
                Descendants(window).OfType<Button>().Single(button => button.Text == "Show devices, startup and schedule").PerformClick();
            Rectangle area = Screen.FromControl(window).WorkingArea;
            window.FitWindow(narrow ? new Rectangle(area.Location, new Size(Math.Min(460, area.Width), area.Height)) : area);
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
            bitmap.Save(imagePath);
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
