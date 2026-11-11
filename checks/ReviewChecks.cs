using System.Reflection;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace LittleQuieter;

internal static partial class Checks
{
    private static void ReminderChecks()
    {
        var state = new ReminderState();
        Assert(state.Update(true, false, 0), "a loud frame can show the visual reminder immediately");
        bool stable = true;
        for (int i = 1; i <= 20; i++)
        {
            stable &= state.Update(false, false, i * 0.02);
            stable &= state.Update(true, false, i * 0.02 + 0.01);
        }
        Assert(stable, "brief quiet gaps do not repeatedly hide and reopen the reminder");
        state.Dismiss();
        Assert(!state.Update(true, false, 1) && !state.Update(false, false, 1.1) && !state.Update(true, false, 1.2),
            "dismissal survives brief threshold crossings within one warning episode");
        state.Update(false, false, 2);
        Assert(!state.Update(false, false, 2.6) && state.Update(true, false, 3), "sustained quiet rearms a dismissed reminder");
        state.Update(false, false, 4);
        Assert(state.Update(false, false, 4.49) && !state.Update(false, false, 4.51), "the reminder closes after half a second of quiet");
        state.Dismiss();
        Assert(state.Update(false, true, 5), "an explicit preview can show a previously dismissed reminder");
        state.Reset();
        Assert(!state.Update(false, false, 5.1), "stop and recovery can reset the reminder immediately");
    }

    private static void StartupChecks()
    {
        const string executable = @"C:\Program Files\Little Quieter.exe";
        Assert(StartupRegistration.Matches(StartupRegistration.Command(executable.ToUpperInvariant()), executable),
            "startup requests identify the current executable without case sensitivity");
        Assert(!StartupRegistration.Matches(null, executable) && !StartupRegistration.Matches("arbitrary command", executable) &&
            !StartupRegistration.Matches(StartupRegistration.Command(executable).ToUpperInvariant(), executable) &&
            !StartupRegistration.Matches(StartupRegistration.Command(@"C:\old\Little Quieter.exe"), executable),
            "missing, invalid and other-copy startup entries are not presented as requests for this copy");
    }

    private static void InstallationConcurrencyChecks(string folder, string installedFolder, string shortcut, string key)
    {
        string firstSource = Path.Combine(folder, "first-update.exe");
        string secondSource = Path.Combine(folder, "second-update.exe");
        string program = Path.Combine(installedFolder, "Little Quieter.exe");
        File.WriteAllText(firstSource, "first serialized update");
        File.WriteAllText(secondSource, "second serialized update");
        byte[] before = File.ReadAllBytes(program);
        using var staged = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Task.Run(() => Installation.Install(firstSource, installedFolder, shortcut, key, () =>
        {
            staged.Set();
            if (!release.Wait(5000))
                throw new TimeoutException("The concurrent installation check did not release staging.");
        }));
        bool secondReachedReplacement = false, rejected = false;
        try
        {
            if (!staged.Wait(5000))
                throw new TimeoutException("The first installation did not stage.");
            try
            {
                Installation.Install(secondSource, installedFolder, shortcut, key, () => secondReachedReplacement = true);
            }
            catch (IOException error) when (error.Message.Contains("in progress")) { rejected = true; }
            Assert(rejected && !secondReachedReplacement && File.ReadAllBytes(program).SequenceEqual(before),
                "a concurrent installer is rejected before taking replacement ownership or changing files");
        }
        finally
        {
            release.Set();
            first.GetAwaiter().GetResult();
        }
        Installation.Install(secondSource, installedFolder, shortcut, key);
        Assert(File.ReadAllText(program) == "second serialized update" && File.ReadAllText(program + ".previous") == "first serialized update",
            "retrying after installation finishes preserves the successful update and its rollback copy");
    }

    private static void CheckUnchangedWindowSettings()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-unchanged-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { DeviceId = "missing-unchanged-mic", DeviceName = "Missing mic", ToneVolume = 0 }.Save(path);
        DateTime stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            Assert(File.GetLastWriteTimeUtc(path) == stamp, "opening a window does not rewrite unchanged settings");
            window.LoadDevices();
            CloseWindow(window);
            Assert(File.GetLastWriteTimeUtc(path) == stamp && !File.Exists(path + ".tmp"),
                "refreshing unchanged devices and closing do not rewrite settings or leave temporary files");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckResponsiveAudioTransition()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-responsive-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0, ShowReminder = false, DeviceId = "missing-responsive-mic", DeviceName = "Missing mic" }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var transition = (Task)typeof(MainWindow).GetMethod("ChangeAudio", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, new object[] { (Func<Task>)(() => release.Task) })!;
            bool responded = false;
            window.BeginInvoke(() => responded = true);
            Finish(Task.Delay(30));
            Assert(responded && !transition.IsCompleted && !Descendants(window).OfType<Button>().Single(button => button.Text == "Test tone (2s)").Enabled,
                "a slow audio transition leaves UI messages responsive and blocks overlapping device actions");
            Task pause = window.PauseAfterError();
            var gate = (AlarmGate)typeof(MainWindow).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            gate.Update(new SoundFrame(0, AudioClock.Now, false));
            Assert(!gate.Active && !pause.IsCompleted, "error handling suspends gating immediately while a device operation is still pending");
            release.SetResult();
            Finish(pause);
            Assert(!window.IsListening && Descendants(window).OfType<Label>().Any(label => label.Text.StartsWith("Paused after an error.")),
                "error handling finishes in a paused state after the pending operation drains");
            var finishClosing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = (Task)typeof(MainWindow).GetMethod("ChangeAudio", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, new object[] { (Func<Task>)(() => finishClosing.Task) })!;
            window.Close();
            Assert(!window.IsDisposed && !window.AudioIdle.IsCompleted, "closing can wait asynchronously for a pending device transition");
            finishClosing.SetResult();
            Finish(window.AudioIdle);
            Assert(window.IsDisposed && !window.IsListening, "closing a pending transition drains cleanup without reopening or stranding the window");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckReminderStability()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-reminder-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0 }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            var reminder = (ReminderWindow)typeof(MainWindow).GetField("reminder", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            reminder.Opacity = 0;
            reminder.ShowInTaskbar = false;
            int appearances = 0;
            reminder.VisibleChanged += (_, _) => { if (reminder.Visible) appearances++; };
            var update = typeof(MainWindow).GetMethod("UpdateReminder", BindingFlags.NonPublic | BindingFlags.Instance)!;
            for (int i = 0; i < 10; i++)
            {
                update.Invoke(window, new object[] { true, false });
                update.Invoke(window, new object[] { false, false });
            }
            Assert(appearances == 1, "ten rapid threshold crossings produce one reminder-window appearance");
            InvokeMonitor(window, "DismissReminder");
            update.Invoke(window, new object[] { true, false });
            Assert(!reminder.Visible, "dismissing the reminder stays effective through the current warning episode");
            update.Invoke(window, new object[] { false, true });
            update.Invoke(window, new object[] { false, false });
            Assert(reminder.Visible && Descendants(reminder).OfType<Label>().Single().Text == "Test tone" && appearances == 2,
                "a quiet preview keeps its test label during the reminder's closing hold");
            update.Invoke(window, new object[] { true, false });
            Assert(reminder.Visible && Descendants(reminder).OfType<Label>().Single().Text == "Keep your voice down, please." && appearances == 2,
                "a preview ending during a loud episode restores the warning text without reopening the reminder");
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckLiveMeterAndTheme()
    {
        SystemColorMode previous = Application.ColorMode;
        try
        {
            using var form = new Form();
            _ = form.Handle;
            using var combo = new ThemeComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(200, 24) };
            form.Controls.Add(combo);
            combo.Items.Add("Theme check");
            combo.SelectedIndex = 0;
            _ = combo.Handle;
            using var comboImage = new Bitmap(200, 24);
            using var meter = new LevelMeter { Size = new Size(300, 24), Limit = 65 };
            _ = meter.Handle;
            using var high = new Bitmap(300, 24);
            using var low = new Bitmap(300, 24);
            meter.SetLevel(90);
            meter.DrawToBitmap(high, meter.ClientRectangle);
            meter.SetLevel(10);
            meter.DrawToBitmap(low, meter.ClientRectangle);
            Assert(high.GetPixel(240, 12).ToArgb() == SystemColors.Highlight.ToArgb() && low.GetPixel(240, 12).ToArgb() == SystemColors.Control.ToArgb(),
                "the live meter paints increasing and falling levels immediately without progress animation");
            Assert(Enumerable.Range(193, 5).Any(x => low.GetPixel(x, 12).ToArgb() == SystemColors.WindowText.ToArgb()) &&
                meter.AccessibilityObject.Value?.Contains("10") == true,
                "the live meter retains a visible threshold marker and its accessible value");
            Application.SetColorMode(SystemColorMode.Dark);
            WindowTheme.RefreshControls(form);
            WindowTheme.ApplyTitle(form);
            Finish(Task.Delay(30));
            Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(form.Handle, 20, out int darkTitle, sizeof(int)));
            meter.DrawToBitmap(low, meter.ClientRectangle);
            int dark = low.GetPixel(240, 12).ToArgb();
            combo.DrawToBitmap(comboImage, combo.ClientRectangle);
            int darkPicker = comboImage.GetPixel(120, 10).ToArgb();
            Application.SetColorMode(SystemColorMode.Classic);
            WindowTheme.RefreshControls(form);
            WindowTheme.ApplyTitle(form);
            Finish(Task.Delay(30));
            Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(form.Handle, 20, out int lightTitle, sizeof(int)));
            meter.DrawToBitmap(low, meter.ClientRectangle);
            combo.DrawToBitmap(comboImage, combo.ClientRectangle);
            Assert(dark != low.GetPixel(240, 12).ToArgb(), "existing live-meter controls repaint after process-local light/dark changes");
            Assert(darkTitle == 1 && lightTitle == 0, "existing window titles follow process-local light/dark changes");
            Assert(darkPicker != comboImage.GetPixel(120, 10).ToArgb(), "existing native device pickers update when the process colour mode changes");
        }
        finally { Application.SetColorMode(previous); }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    private static void CheckWindowThemeState()
    {
        SystemColorMode previous = Application.ColorMode;
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-theme-state-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { DeviceId = "missing-theme-mic", DeviceName = "Missing mic", PlayTone = false, ToneVolume = 0, Limit = 50, ShowReminder = false }.Save(path);
        DateTime stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            foreach (var control in Descendants(window))
                _ = control.Handle;
            foreach (var mode in new[] { SystemColorMode.Dark, SystemColorMode.Classic })
            {
                Application.SetColorMode(mode);
                InvokeMonitor(window, "ApplyTheme");
                Finish(window.AudioIdle);
            }
            Assert(!window.IsListening && Descendants(window).OfType<TrackBar>().Single().Value == 50 &&
                Descendants(window).OfType<NumericUpDown>().Single().Value == 0 &&
                Descendants(window).OfType<ComboBox>().Single(control => control.AccessibleName == "Microphone").SelectedItem is AudioDevice { Id: "missing-theme-mic" },
                "live theme refresh preserves device selection, levels and paused state");
            Assert(!(bool)typeof(MainWindow).GetField("preferencesDirty", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!,
                "recreating native theme handles does not act like a user settings change");
            CloseWindow(window);
            Assert(File.GetLastWriteTimeUtc(path) == stamp, "live theme refresh does not rewrite unchanged settings");
        }
        finally
        {
            Application.SetColorMode(previous);
            File.Delete(path);
            Directory.Delete(folder);
        }
    }

    private static void CheckVisualOnlyAudio()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-visual-audio-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        int minute = (int)DateTime.Now.TimeOfDay.TotalMinutes;
        new Preferences
        {
            PlayTone = false,
            ToneVolume = 0,
            ShowReminder = false,
            OutputDeviceId = "missing-visual-output",
            OutputDeviceName = "Missing output",
            ScheduleEnabled = true,
            ScheduleStartMinute = (minute + 1438) % 1440,
            ScheduleEndMinute = (minute + 2) % 1440
        }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            Finish(window.ToggleListening());
            Assert(window.IsListening && AudioDevice.HasActiveSession(DataFlow.Capture) && !AudioDevice.HasActiveSession(DataFlow.Render) &&
                !(bool)typeof(MainWindow).GetField("soundFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!,
                "visual-only listening does not open or report errors for an unused unavailable output");
            SystemColorMode previous = Application.ColorMode;
            try
            {
                foreach (var mode in new[] { SystemColorMode.Dark, SystemColorMode.Classic })
                {
                    Application.SetColorMode(mode);
                    InvokeMonitor(window, "ApplyTheme");
                    Finish(window.AudioIdle);
                }
                Assert(window.IsListening && AudioDevice.HasActiveSession(DataFlow.Capture) && !AudioDevice.HasActiveSession(DataFlow.Render),
                    "live theme changes preserve active capture without opening an output stream");
            }
            finally
            {
                Application.SetColorMode(previous);
                InvokeMonitor(window, "ApplyTheme");
            }
            Finish(window.PauseAfterError());
            Finish(window.ApplySchedule(DateTime.Now));
            Finish(window.ApplySchedule(DateTime.Now.AddDays(1)));
            Assert(!window.IsListening && !AudioDevice.HasActiveSession(DataFlow.Capture) && !AudioDevice.HasActiveSession(DataFlow.Render),
                "an unexpected UI error releases both streams and blocks scheduled restarts until the user resumes");
            Finish(window.ToggleListening());
            Assert(window.IsListening && !AudioDevice.HasActiveSession(DataFlow.Render), "the user can explicitly resume output-free listening after an error");
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckEarlyBackgroundPauseAudio()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-early-pause-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0, ShowReminder = false }.Save(path);
        try
        {
            foreach (bool learning in new[] { false, true })
            {
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
                using var window = new MainWindow(true, show, exit, path);
                _ = window.Handle;
                // Drive the first background tick explicitly, after the user's actions.
                ((System.Windows.Forms.Timer)typeof(MainWindow).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Stop();
                if (learning)
                    InvokeMonitor(window, "ToggleCalibration");
                else
                    Finish(window.ToggleListening());
                Assert(window.IsListening, "manual " + (learning ? "learning" : "listening") + " can start before the first background tick");
                if (learning)
                    InvokeMonitor(window, "ToggleCalibration");
                else
                    Finish(window.ToggleListening());
                InvokeMonitor(window, "TickMonitor");
                Assert(!window.IsListening && !AudioDevice.HasActiveSession(DataFlow.Capture),
                    (learning ? "cancelling learning" : "a manual pause") + " before the first background tick keeps the microphone off");
                CloseWindow(window);
            }
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
