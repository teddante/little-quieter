using System.Reflection;
using System.Runtime.InteropServices;

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
            Size compactSize = window.Size;
            Assert(window.MinimumSize == compactSize && window.MaximumSize == compactSize,
                "the compact window locks its size to the fitted content");
            Assert((GetWindowLongPtr(window.Handle, -16).ToInt64() & (0x00040000L | 0x00010000L)) == 0,
                "native window styles disable border resizing and maximization");
            window.Size = new Size(2000, 1800);
            Assert(window.Size == compactSize, "an oversized window request is capped at the fitted content size");
            window.Size = new Size(100, 100);
            Assert(window.Size == compactSize, "an undersized window request cannot hide the main controls");
            toggle.PerformClick();
            window.PerformLayout();
            Rectangle workArea = Screen.FromControl(window).WorkingArea;
            Assert(window.Height >= compactSize.Height && workArea.Contains(window.Bounds) &&
                (!scroll.VerticalScroll.Visible || window.Height == workArea.Height),
                "expanding settings fits the available screen height and scrolls when constrained");
            Size expandedSize = window.Size;
            Assert(window.MinimumSize == expandedSize && window.MaximumSize == expandedSize,
                "expanding settings updates both size limits to the new content");
            toggle.PerformClick();
            Assert(window.Size == compactSize && !scroll.VerticalScroll.Visible,
                "collapsing settings restores the compact height without a stale scrollbar");
            var primary = Descendants(window).OfType<Button>().Where(button => button.Text is "Start listening" or "Learn my normal voice" or "Test tone (2s)");
            Assert(primary.All(button => scroll.ClientRectangle.Contains(scroll.RectangleToClient(button.RectangleToScreen(button.ClientRectangle)))),
                "the fitted window keeps all main action buttons in view");
            var smallScreen = new Rectangle(100, 150, 360, 300);
            window.Location = new Point(-5000, -5000);
            window.FitWindow(smallScreen);
            Assert(smallScreen.Contains(window.Bounds) && scroll.VerticalScroll.Visible,
                "a small work area limits the fitted window and keeps it reachable");
            var last = Descendants(window).OfType<Label>().Single(label => label.Text.StartsWith("Minimize to keep listening"));
            toggle.PerformClick();
            window.FitWindow(smallScreen);
            scroll.ScrollControlIntoView(last);
            Assert(scroll.ClientRectangle.Contains(scroll.RectangleToClient(last.RectangleToScreen(last.ClientRectangle))),
                "scrolling a small window can reach the final settings and footer");
            window.FitWindow();
            Assert(window.Size == expandedSize,
                "returning to a larger screen restores the natural content size");
            using var largeFont = new Font(window.Font.FontFamily, window.Font.Size * 1.5f);
            window.Font = largeFont;
            window.FitWindow();
            Assert(Screen.FromControl(window).WorkingArea.Contains(window.Bounds) && !scroll.HorizontalScroll.Visible && primary
                .All(button => button.Parent!.ClientRectangle.Contains(button.Bounds)),
                "larger control text fits without clipping actions or extending beyond the screen");
            window.Enabled = false;
            Size beforeMinimizing = window.Size;
            window.WindowState = FormWindowState.Minimized;
            InvokeMonitor(window, "RestoreWindow");
            Assert(window.WindowState == FormWindowState.Normal && window.Size == beforeMinimizing,
                "restoring from the tray keeps the window fitted to its content");
            typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toggle, new object[] { EventArgs.Empty });
            Assert(window.MinimumSize == window.Size && window.MaximumSize == window.Size,
                "toggling settings after a tray restore retains the fixed size limits");
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

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
            Assert(Descendants(window).OfType<Label>().Any(label => label.Text == "Testing the selected output for two seconds."),
                "preview status follows a committed change from zero to nonzero volume");
            volume.Value = 0;
            Finish(window.AudioIdle);
            Assert(Descendants(window).OfType<Label>().Any(label => label.Text == "Test is silent: alarm volume is 0%."),
                "preview status reports a committed change back to silent volume");
            CloseWindow(window);
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
