using Microsoft.Win32;

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
