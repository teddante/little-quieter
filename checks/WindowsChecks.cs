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
        window.ToggleListening();
        Assert(!window.IsListening && Preferences.Load(path).DeviceId == "missing-test-mic", "an unavailable microphone cannot open audio or overwrite the chosen endpoint");
        Assert(window.FormBorderStyle == FormBorderStyle.Sizable && window.MaximizeBox, "main window supports resizing and maximizing");
        var meter = Descendants(window).OfType<LevelMeter>().Single();
        _ = meter.Handle;
        meter.Level = 72;
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
            window.ApplySchedule(DateTime.Now);
            window.ToggleListening();
            Assert(!window.IsListening && Descendants(window).OfType<Button>().Any(button => button.Text == "Back up and use new settings"),
                "background startup with damaged preferences offers recovery and never starts audio");
            Assert(Descendants(window).OfType<Label>().Any(label => label.Text == "SETTINGS RECOVERY · MICROPHONE OFF") &&
                !Descendants(window).OfType<CheckBox>().Single(control => control.Text == "Listen on a daily schedule").Enabled,
                "recovery status explains the pause and blocks schedule changes until recovery");
            window.Close();
            Assert(File.ReadAllText(path) == "original broken settings", "window initialization and shutdown preserve the damaged original");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void InstallationChecks()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-install-check-" + Guid.NewGuid().ToString("N"));
        string keyPath = @"Software\LittleQuieter.InstallChecks\" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "source.exe");
        string installedFolder = Path.Combine(folder, "installed");
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
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    }

    private static void CheckPreviewOutputChanges()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-preview-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0, ShowReminder = false }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            try
            {
                InvokeMonitor(window, "PreviewAlarm");
                var alarm = (AlarmTone)typeof(MainWindow).GetField("alarm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Assert(alarm.IsReady && !window.IsListening, "paused preview prepares only a zero-volume render stream");
                var output = Descendants(window).OfType<ComboBox>().Single(control => control.AccessibleName == "Alarm output. Only changes Little Quieter");
                var missing = new AudioDevice { Id = "missing-preview-output", Name = "Missing preview output", Unavailable = true };
                output.Items.Add(missing);
                output.SelectedItem = missing;
                Assert(!alarm.IsReady && !alarm.IsActive, "changing the preview output immediately closes the old endpoint and cancels the preview");
                InvokeMonitor(window, "PreviewAlarm");
                Assert(!alarm.IsReady && alarm.Fault == null &&
                    Descendants(window).OfType<Label>().Any(label => label.Text.StartsWith("Tone unavailable.")),
                    "previewing an unavailable new output reports failure instead of reusing the previous endpoint");
            }
            finally { window.Close(); }
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
                    Assert(!window.IsListening && !HasActiveSession(DataFlow.Capture) && !HasActiveSession(DataFlow.Render),
                        "an update preserves a manual pause with scheduling " + (scheduled ? "on" : "off"));
                    window.ToggleListening();
                    Assert(window.IsListening && Installation.IsCapturing(Environment.ProcessId),
                        "manual listening still resumes after a paused update at zero test volume");
                    window.ToggleListening();
                    if (scheduled)
                    {
                        window.ApplySchedule(now.AddDays(1));
                        Assert(window.IsListening, "the next scheduled period resumes after a pause preserved through update");
                    }
                }
                finally { window.Close(); }
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
            finally { next.Close(); }
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void PreviewWindow(string imagePath)
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
            window.ClientSize = new Size(650, 1050);
            window.PerformLayout();
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
            bitmap.Save(imagePath);
            window.Close();
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
