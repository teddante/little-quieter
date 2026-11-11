using System.Reflection;

namespace LittleQuieter;

internal static partial class Checks
{
    private static void CheckPendingVolumeEdit()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-volume-draft-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 35 }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            var volume = Descendants(window).OfType<NumericUpDown>().Single();
            var edit = volume.Controls.OfType<TextBox>().Single();
            Descendants(window).OfType<TrackBar>().Single().Value++;
            edit.Text = "0073";
            edit.SelectionStart = 2;
            typeof(MainWindow).GetMethod("SavePreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { false });
            Assert(edit.Text == "0073" && edit.SelectionStart == 2 && Preferences.Load(path).ToneVolume == 35,
                "saving another setting preserves an unfinished volume edit and its caret");
            typeof(NumericUpDown).GetMethod("ValidateEditText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(volume, null);
            Assert(volume.Value == 73, "committing the volume edit applies the complete percentage");
            edit.Text = "42";
            CloseWindow(window);
            Assert(Preferences.Load(path).ToneVolume == 42, "closing commits the final volume edit before saving settings");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckScheduleEditing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-schedule-edit-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        int minute = (int)DateTime.Now.TimeOfDay.TotalMinutes;
        new Preferences
        {
            PlayTone = false,
            ToneVolume = 0,
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
            ((System.Windows.Forms.Timer)typeof(MainWindow).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Stop();
            // A logical capture suffices: this check must not open hardware.
            var capture = new AudioInput();
            typeof(MainWindow).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, capture);
            var end = Descendants(window).OfType<DateTimePicker>().Single(picker => picker.AccessibleName == "Daily end time");
            int disabled = 0;
            end.EnabledChanged += (_, _) => { if (!end.Enabled) disabled++; };
            window.ActiveControl = end;
            end.Value = end.Value.AddMinutes(1);
            Finish(window.AudioIdle);
            Assert(window.IsListening && disabled == 0 && window.ActiveControl == end,
                "editing an active schedule keeps its time editor enabled when capture is already running");
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckCalibrationOutputFailure()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-learning-output-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { ToneVolume = 35, ShowReminder = false, OutputDeviceId = "missing-learning-output", OutputDeviceName = "Missing output" }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            Finish(window.ToggleListening());
            var volume = Descendants(window).OfType<NumericUpDown>().Single();
            var edit = volume.Controls.OfType<TextBox>().Single();
            edit.Text = "0073";
            edit.SelectionStart = 2;
            InvokeMonitor(window, "TickMonitor");
            Assert(edit.Text == "0073" && edit.SelectionStart == 2,
                "live monitoring does not validate or rewrite an unfinished volume edit");
            typeof(NumericUpDown).GetMethod("ValidateEditText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(volume, null);
            foreach (string outcome in new[] { "cancel", "failure", "success" })
            {
                InvokeMonitor(window, "ToggleCalibration");
                if (outcome == "cancel")
                    InvokeMonitor(window, "ToggleCalibration");
                else
                {
                    if (outcome == "success")
                    {
                        var readings = (List<double>)typeof(MainWindow).GetField("calibration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                        for (int i = 0; i < 80; i++)
                            readings.Add(-40 + i % 20);
                    }
                    InvokeMonitor(window, "CompleteCalibration");
                }
                string result = outcome == "cancel" ? "Learning cancelled." : outcome == "failure" ? "Couldn't learn" : "Learned your normal voice.";
                Assert(window.IsListening && Descendants(window).OfType<Label>().Any(label => label.Text.StartsWith(result) && label.Text.Contains("Tone unavailable.")) &&
                    !AudioDevice.HasActiveSession(NAudio.CoreAudioApi.DataFlow.Render),
                    "learning " + outcome + " retains the unavailable-output warning while restoring capture without sound");
            }
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
