using System.Reflection;
using Microsoft.Win32;

namespace LittleQuieter;

internal static partial class Checks
{
    private static void CheckStartupOwnership()
    {
        string path = @"Software\LittleQuieter.StartupChecks\" + Guid.NewGuid().ToString("N");
        const string current = @"C:\current\Little Quieter.exe";
        string other = StartupRegistration.Command(@"C:\other\Little Quieter.exe");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("LittleQuieter", other);
            StartupRegistration.SetEnabled(false, current, path);
            Assert(Equals(key.GetValue("LittleQuieter"), other), "disabling startup preserves an entry belonging to another copy");
            StartupRegistration.SetEnabled(true, current, path);
            Assert(StartupRegistration.Matches(key.GetValue("LittleQuieter") as string, current), "explicitly enabling startup registers the requested copy");
            StartupRegistration.SetEnabled(false, current.ToUpperInvariant(), path);
            Assert(key.GetValue("LittleQuieter") == null, "disabling startup removes this copy's entry regardless of path letter case");
            key.SetValue("LittleQuieter", new byte[] { 1, 2 }, RegistryValueKind.Binary);
            StartupRegistration.SetEnabled(false, current, path);
            Assert(key.GetValue("LittleQuieter") is byte[] bytes && bytes.SequenceEqual(new byte[] { 1, 2 }),
                "disabling startup preserves an unrecognized registration rather than deleting it");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    private static void CheckThemeDuringVolumeEdit()
    {
        SystemColorMode previous = Application.ColorMode;
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-theme-draft-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 35 }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            foreach (Control control in Descendants(window))
                _ = control.Handle;
            _ = window.Handle;
            var volume = Descendants(window).OfType<NumericUpDown>().Single();
            var edit = volume.Controls.OfType<TextBox>().Single();
            edit.Text = "0073";
            edit.Select(1, 2);
            foreach (var mode in new[] { SystemColorMode.Dark, SystemColorMode.Classic })
            {
                Application.SetColorMode(mode);
                InvokeMonitor(window, "ApplyTheme");
                Assert(edit.Text == "0073" && edit.SelectionStart == 1 && edit.SelectionLength == 2,
                    "switching to " + mode + " preserves unfinished volume text and selection");
            }
            CloseWindow(window);
        }
        finally
        {
            Application.SetColorMode(previous);
            File.Delete(path);
            Directory.Delete(folder);
        }
    }

    private static void CheckPreviewStatus()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-preview-status-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0, ShowReminder = false }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            var gate = (AlarmGate)typeof(MainWindow).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var footer = (Label)typeof(MainWindow).GetField("footer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            InvokeMonitor(window, "PreviewAlarm");
            typeof(AlarmGate).GetField("previewUntil", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(gate, AudioClock.Now - 1);
            gate.Refresh(AudioClock.Now);
            InvokeMonitor(window, "TickMonitor");
            Assert(footer.Text == "Test finished. Your microphone is off.", "an expired preview replaces its testing message with a completed status");
            InvokeMonitor(window, "PreviewAlarm");
            footer.Text = "A newer status message";
            typeof(AlarmGate).GetField("previewUntil", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(gate, AudioClock.Now - 1);
            gate.Refresh(AudioClock.Now);
            InvokeMonitor(window, "TickMonitor");
            Assert(footer.Text == "A newer status message", "preview completion preserves a more recent status message");
            InvokeMonitor(window, "PreviewAlarm");
            var output = Descendants(window).OfType<ComboBox>().Single(control => control.AccessibleName == "Alarm output. Only changes Little Quieter");
            var missing = new AudioDevice { Id = "missing-preview-status", Name = "Missing output", Unavailable = true };
            output.Items.Add(missing);
            output.SelectedItem = missing;
            Finish(window.AudioIdle);
            Assert(footer.Text.StartsWith("Test cancelled.") && !gate.IsPreviewing(AudioClock.Now) &&
                !AudioDevice.HasActiveSession(NAudio.CoreAudioApi.DataFlow.Render),
                "changing output reports a cancelled test and closes its stream");
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
