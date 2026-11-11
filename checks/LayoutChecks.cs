using System.Reflection;

namespace LittleQuieter;

internal static partial class Checks
{
    private static void CheckWindowSizing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-layout-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0, DeviceId = "missing-layout-mic", DeviceName = "Missing microphone" }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            window.Opacity = 0;
            window.Enabled = false;
            window.ShowInTaskbar = false;
            window.Show();
            window.Enabled = true;
            Application.DoEvents();
            var scroll = window.Controls.OfType<Panel>().Single();
            var toggle = Descendants(window).OfType<Button>().Single(button => button.Text == "Show devices, startup and schedule");
            int compactHeight = window.Height;
            toggle.PerformClick();
            window.PerformLayout();
            Rectangle workArea = Screen.FromControl(window).WorkingArea;
            Assert(window.Height >= compactHeight && workArea.Contains(window.Bounds) &&
                (!scroll.VerticalScroll.Visible || window.Height == workArea.Height),
                "expanding settings fits the available screen height and scrolls when constrained");
            toggle.PerformClick();
            Assert(window.Height == compactHeight && !scroll.VerticalScroll.Visible,
                "collapsing settings restores the compact height without a stale scrollbar");
            window.Size = window.MinimumSize;
            var primary = Descendants(window).OfType<Button>().Where(button => button.Text is "Start listening" or "Learn my normal voice" or "Test tone (2s)");
            Assert(primary.All(button => scroll.ClientRectangle.Contains(scroll.RectangleToClient(button.RectangleToScreen(button.ClientRectangle)))),
                "the minimum window size keeps all main action buttons in view");
            foreach (int width in new[] { 1000, 460, 800, 460 })
            {
                window.Width = width;
                window.PerformLayout();
                Assert(!scroll.HorizontalScroll.Visible && Descendants(window).OfType<FlowLayoutPanel>().Where(flow => flow.Visible)
                    .All(flow => flow.Controls.Cast<Control>().Where(control => control.Visible).All(control => flow.ClientRectangle.Contains(control.Bounds))),
                    "resizing to width " + width + " keeps grouped controls inside their rows");
            }
            var smallScreen = new Rectangle(100, 150, 360, 300);
            window.Location = new Point(-5000, -5000);
            window.FitWindow(false, smallScreen);
            Assert(smallScreen.Contains(window.Bounds) && scroll.VerticalScroll.Visible,
                "a small work area overrides the normal minimum and keeps the entire window reachable");
            var last = Descendants(window).OfType<Label>().Single(label => label.Text.StartsWith("Minimize to keep listening"));
            toggle.PerformClick();
            window.FitWindow(false, smallScreen);
            scroll.ScrollControlIntoView(last);
            Assert(scroll.ClientRectangle.Contains(scroll.RectangleToClient(last.RectangleToScreen(last.ClientRectangle))),
                "scrolling a small window can reach the final settings and footer");
            window.FitWindow(true);
            using var largeFont = new Font(window.Font.FontFamily, window.Font.Size * 1.5f);
            window.Font = largeFont;
            window.FitWindow(true);
            Assert(Screen.FromControl(window).WorkingArea.Contains(window.Bounds) && !scroll.HorizontalScroll.Visible && primary
                .All(button => button.Parent!.ClientRectangle.Contains(button.Bounds)),
                "larger control text fits without clipping actions or extending beyond the screen");
            window.Enabled = false;
            window.WindowState = FormWindowState.Maximized;
            window.WindowState = FormWindowState.Minimized;
            InvokeMonitor(window, "RestoreWindow");
            Assert(window.WindowState == FormWindowState.Maximized,
                "restoring from the tray preserves a maximized window");
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toggle, new object[] { EventArgs.Empty });
            Assert(window.WindowState == FormWindowState.Maximized,
                "toggling settings in a maximized window preserves its state");
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckVolumeEditing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-volume-edit-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { PlayTone = false, ToneVolume = 0 }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            var volume = Descendants(window).OfType<NumericUpDown>().Single();
            int disabled = 0;
            volume.EnabledChanged += (_, _) => { if (!volume.Enabled) disabled++; };
            window.ActiveControl = volume;
            for (int i = 0; i < 20; i++)
                volume.UpButton();
            Assert(volume.Value == 100 && disabled == 0 && window.ActiveControl == volume,
                "volume arrows reach 100 percent without disabling the editor or losing its active control");
            for (int i = 0; i < 20; i++)
                volume.DownButton();
            Assert(volume.Value == 0 && disabled == 0 && !window.IsListening,
                "volume arrows return to zero without opening audio or interrupting repeated clicks");
            volume.Text = "37";
            typeof(NumericUpDown).GetMethod("ValidateEditText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(volume, null);
            Assert(volume.Value == 37 && disabled == 0, "typing an exact volume percentage preserves the edit control");
            CloseWindow(window);
            Assert(Preferences.Load(path).ToneVolume == 37, "the exact edited percentage is saved");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    private static void CheckLiveVolumeEditing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-live-volume-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        new Preferences { ToneVolume = 0, ShowReminder = false }.Save(path);
        try
        {
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var exit = new EventWaitHandle(false, EventResetMode.AutoReset);
            using var window = new MainWindow(false, show, exit, path);
            _ = window.Handle;
            Finish(window.ToggleListening());
            InvokeMonitor(window, "PreviewAlarm");
            var alarm = (AlarmTone)typeof(MainWindow).GetField("alarm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var player = alarm.Player;
            player.IsMuted = true;
            Assert(player.IsMuted, "live volume editing mutes only the test output session before raising its volume");
            var volume = Descendants(window).OfType<NumericUpDown>().Single();
            int disabled = 0;
            volume.EnabledChanged += (_, _) => { if (!volume.Enabled) disabled++; };
            window.ActiveControl = volume;
            for (int i = 0; i < 20; i++)
                volume.UpButton();
            Assert(volume.Value == 100 && disabled == 0 && window.ActiveControl == volume && ReferenceEquals(player, alarm.Player) && player.IsMuted,
                "live volume arrows keep the prepared output, active editor and test-session mute intact");
            volume.Value = 0;
            Finish(window.AudioIdle);
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
