using NAudio.CoreAudioApi;

namespace LittleQuieter;

internal sealed class MainWindow : Form
{
    internal static readonly Color Mint = Color.FromArgb(106, 226, 183);
    internal static readonly Color Amber = Color.FromArgb(255, 184, 104);
    private static readonly Color Ink = Color.FromArgb(16, 23, 31);
    private static readonly Color Card = Color.FromArgb(26, 37, 49);
    private static readonly Color Muted = Color.FromArgb(166, 184, 201);
    private readonly string settingsPath;
    private readonly Preferences preferences;
    private readonly AlarmTone alarm = new AlarmTone();
    private readonly AlarmGate gate;
    private readonly System.Threading.Timer watchdog;
    private readonly ReminderWindow reminder = new ReminderWindow();
    private readonly List<double> calibration = new List<double>();
    private readonly EventWaitHandle showSignal, exitSignal;
    private readonly bool background;
    private readonly ComboBox microphones, outputs;
    private readonly TrackBar limit;
    private readonly Label limitCaption, stateCaption, levelCaption, liveCaption, footer;
    private readonly LevelMeter meter;
    private readonly Button start, learn, refresh, test;
    private readonly NumericUpDown toneVolume;
    private readonly CheckBox playTone, runAtStartup;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayToggle;
    private readonly System.Windows.Forms.Timer timer;
    private AudioInput? input;
    private AudioDeviceChanges? deviceChanges;
    private int deviceVersion;
    private bool showRequested, initialized, changingStartup, soundFailed;
    private bool calibrating, beforeCalibration, closing, previousActive, previousPreview;
    private double lastFrameTime, calibrationStart, calibrationEnd, saveAt, lastDb = -100;

    public MainWindow(bool startInBackground, EventWaitHandle show, EventWaitHandle exit)
    {
        background = startInBackground;
        showSignal = show;
        exitSignal = exit;
        gate = new AlarmGate(alarm.SetActive);
        watchdog = new System.Threading.Timer(_ => gate.Refresh(AudioClock.Now), null, Timeout.Infinite, Timeout.Infinite);
        settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleQuieter", "settings.xml");
        preferences = Preferences.Load(settingsPath);
        Text = "Little Quieter";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Segoe UI", 10);
        BackColor = Ink;
        ForeColor = Color.White;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(650, 704);
        AutoScroll = true;
        AutoScrollMinSize = ClientSize;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(Label("Little Quieter", 24, 20, 600, 43, 28, Color.White, true));
        Controls.Add(Label("Keep your voice within your chosen limit.", 26, 68, 600, 25, 11, Muted));
        var card = new Panel { BackColor = Card, Location = new Point(24, 110), Size = new Size(602, 156) };
        stateCaption = Label("PAUSED · MICROPHONE OFF", 20, 13, 563, 23, 10, Muted, true);
        levelCaption = Label("—", 18, 39, 85, 56, 32, Color.White, true);
        liveCaption = Label("Start listening to see your microphone level.", 112, 48, 470, 46, 11, Muted);
        meter = new LevelMeter { Location = new Point(20, 112), Size = new Size(562, 23), BackColor = Card, Limit = preferences.Limit };
        card.Controls.AddRange(new Control[] { stateCaption, levelCaption, liveCaption, meter });
        Controls.Add(card);
        Controls.Add(Label("MICROPHONE", 24, 284, 270, 22, 9, Muted, true));
        Controls.Add(Label("ALARM OUTPUT", 310, 284, 230, 22, 9, Muted, true));
        microphones = new ComboBox { Location = new Point(24, 309), Size = new Size(272, 29), DropDownWidth = 550, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Card, ForeColor = Color.White, AccessibleName = "Microphone" };
        outputs = new ComboBox { Location = new Point(310, 309), Size = new Size(230, 29), DropDownWidth = 550, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Card, ForeColor = Color.White, AccessibleName = "Alarm output. Only changes Little Quieter" };
        refresh = Button("Refresh", 550, 308, 76, 31);
        refresh.Click += delegate
        {
            LoadDevices();
        };
        Controls.AddRange(new Control[] { microphones, outputs, refresh });
        limitCaption = Label("", 24, 358, 600, 26, 11, Color.White, true);
        Controls.Add(limitCaption);
        limit = new TrackBar { Location = new Point(18, 388), Size = new Size(615, 36), AutoSize = false, Minimum = Levels.MinimumLimit, Maximum = Levels.MaximumLimit, Value = preferences.Limit, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 5, AccessibleName = "Loudness limit. Lower warns sooner" };
        Controls.Add(limit);
        Controls.Add(Label("Lower = warns sooner. Higher = allows a louder voice.", 24, 426, 600, 23, 10, Muted));
        start = Button("Start listening", 24, 468, 176, 43, true);
        start.Click += delegate
        {
            ToggleListening();
        };
        learn = Button("Learn my normal voice", 212, 468, 238, 43);
        learn.Click += delegate
        {
            ToggleCalibration();
        };
        test = Button("Test tone (2s)", 462, 468, 164, 43);
        test.Click += delegate
        {
            PreviewAlarm();
        };
        Controls.AddRange(new Control[] { start, learn, test });
        playTone = new CheckBox { Text = "Play steady alarm tone", Location = new Point(24, 534), Size = new Size(274, 27), Checked = preferences.PlayTone, AccessibleName = "Play steady alarm tone" };
        Controls.Add(playTone);
        Controls.Add(Label("Alarm volume", 362, 537, 113, 24, 10, Muted));
        toneVolume = new NumericUpDown { Location = new Point(476, 534), Size = new Size(70, 29), Minimum = 0, Maximum = 100, Value = preferences.ToneVolume, Increment = 5, BackColor = Card, ForeColor = Color.White, AccessibleName = "Alarm tone volume percent" };
        Controls.Add(toneVolume);
        Controls.Add(Label("%", 554, 537, 45, 24, 10, Muted));
        runAtStartup = new CheckBox { Text = "Start with Windows and listen in the background", Location = new Point(24, 578), Size = new Size(602, 27), AccessibleName = "Start with Windows and listen in the background" };
        try
        {
            runAtStartup.Checked = StartupRegistration.Enabled;
        }
        catch (System.Security.SecurityException) { }
        Controls.Add(runAtStartup);
        footer = Label("Tone starts above your limit and stops when you quieten down.", 24, 618, 602, 38, 10, Muted);
        Controls.Add(footer);
        Controls.Add(Label("Local only · no recordings · hears other room sounds too\nMinimize to keep listening in the tray. Close × exits and turns the mic off.", 24, 660, 602, 42, 9, Muted));
        runAtStartup.CheckedChanged += delegate
        {
            if (changingStartup)
                return;
            try
            {
                StartupRegistration.SetEnabled(runAtStartup.Checked, Application.ExecutablePath);
                footer.Text = runAtStartup.Checked ? "Startup enabled. It will listen in the tray when you sign in." : "Startup disabled.";
            }
            catch (Exception error) { changingStartup = true; runAtStartup.Checked = !runAtStartup.Checked; changingStartup = false; footer.Text = "Couldn't change startup: " + error.Message; }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Little Quieter", null, delegate
        {
            RestoreWindow();
        });
        trayToggle = new ToolStripMenuItem("Start listening", null, delegate
        {
            ToggleListening();
        });
        menu.Items.Add(trayToggle);
        menu.Items.Add("Test tone (2s)", null, delegate
        {
            PreviewAlarm();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate
        {
            Close();
        });
        tray = new NotifyIcon { Text = "Little Quieter — paused", Icon = Icon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += delegate
        {
            RestoreWindow();
        };
        Resize += delegate
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
        };
        FormClosing += delegate
        {
            Shutdown();
        };
        Shown += delegate
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height - 24)
            {
                Height = area.Height - 24;
                Top = area.Top + 12;
            }
        };
        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += delegate
        {
            TickMonitor();
        };
        timer.Start();
        LoadDevices();
        UpdateLimit();
        ApplyContrast(this);
        limit.ValueChanged += delegate
        {
            UpdateLimit();
            ConfigureGate();
            QueueSave();
        };
        playTone.CheckedChanged += delegate
        {
            ConfigureGate();
            QueueSave();
        };
        toneVolume.ValueChanged += delegate
        {
            alarm.SetVolume((int)toneVolume.Value);
            ConfigureGate();
            QueueSave();
        };
        microphones.SelectedIndexChanged += delegate
        {
            QueueSave();
        };
        outputs.SelectedIndexChanged += delegate
        {
            QueueSave();
        };
        ConfigureGate();
        SavePreferences(false); // Migrate away from obsolete speech/delay fields once at startup.
    }
    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(value && (!background || showRequested));
    }
    internal static void ApplyContrast(Control control)
    {
        if (!SystemInformation.HighContrast)
            return;
        control.BackColor = SystemColors.Window;
        control.ForeColor = SystemColors.WindowText;
        foreach (Control child in control.Controls)
            ApplyContrast(child);
    }
    private static Label Label(string text, int x, int y, int w, int h, float size, Color color, bool bold = false)
    {
        return new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color };
    }
    private static Button Button(string text, int x, int y, int w, int h, bool primary = false)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat, BackColor = primary ? Mint : Card, ForeColor = primary ? Ink : Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
    private void UpdateLimit()
    {
        limitCaption.Text = "Loudness limit: " + limit.Value + " / 100";
        meter.Limit = limit.Value;
        meter.Invalidate();
    }
    private void ConfigureGate()
    {
        gate.Configure(Levels.ToDb(limit.Value), playTone.Checked && toneVolume.Value > 0 && !soundFailed);
    }
    private void LoadDevices()
    {
        if (input != null)
            return;
        string previous = microphones.SelectedItem?.ToString() ?? preferences.DeviceName;
        string? outputId = outputs.SelectedItem is AudioDevice selectedOutput ? selectedOutput.Id : preferences.OutputDeviceId;
        microphones.Items.Clear();
        outputs.Items.Clear();
        try
        {
            foreach (var device in AudioDevice.List(DataFlow.Capture))
                microphones.Items.Add(device);
            microphones.SelectedIndex = 0;
            for (int i = 0; i < microphones.Items.Count; i++)
                if (microphones.Items[i] is AudioDevice microphone && microphone.Name == previous)
                {
                    microphones.SelectedIndex = i;
                    break;
                }
            foreach (var device in AudioDevice.List(DataFlow.Render))
                outputs.Items.Add(device);
            outputs.SelectedIndex = 0;
            for (int i = 0; i < outputs.Items.Count; i++)
                if (outputs.Items[i] is AudioDevice output && output.Id == outputId)
                {
                    outputs.SelectedIndex = i;
                    break;
                }
            if (outputId != null && outputs.SelectedIndex == 0)
            {
                // Preserve a disconnected explicit selection; don't silently move a loud alarm elsewhere.
                outputs.Items.Add(new AudioDevice { Id = outputId, Name = preferences.OutputDeviceName + " (unavailable)" });
                outputs.SelectedIndex = outputs.Items.Count - 1;
            }
        }
        catch (Exception error) { footer.Text = "Couldn't list audio devices: " + error.Message; }
    }
    private void PrepareOutput()
    {
        soundFailed = false;
        try
        {
            alarm.Start((int)toneVolume.Value, (outputs.SelectedItem as AudioDevice)?.Id);
        }
        catch (Exception error) { soundFailed = true; footer.Text = "Tone unavailable. Check sound output, then use Test tone. " + error.Message; }
        ConfigureGate();
    }
    private bool StartListening(bool silent = false, bool startSuspended = false)
    {
        if (input != null)
            return true;
        if (microphones.SelectedItem is not AudioDevice device)
        {
            footer.Text = "Connect a microphone, then click Refresh.";
            return false;
        }
        gate.Suspend(true);
        PrepareOutput();
        var candidate = new AudioInput();
        candidate.LevelAvailable += gate.Update;
        candidate.StreamStopped += gate.Reset;
        try
        {
            candidate.Start(device.Id);
            deviceChanges = new AudioDeviceChanges(() => gate.Suspend(true));
        }
        catch (Exception error)
        {
            candidate.Dispose();
            alarm.Dispose();
            footer.Text = "Couldn't open the microphone. Check its connection and Windows desktop-app microphone access.";
            if (silent)
            {
                tray.BalloonTipTitle = "Little Quieter: microphone unavailable";
                tray.BalloonTipText = "Open Little Quieter from the tray to choose a microphone.";
                tray.ShowBalloonTip(7000);
            }
            else
                MessageBox.Show(this, error.Message, "Microphone unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        input = candidate;
        deviceVersion = deviceChanges.Version;
        lastFrameTime = AudioClock.Now;
        gate.Suspend(startSuspended);
        StartWatchdog();
        start.Text = "Stop listening";
        trayToggle.Text = "Stop listening";
        microphones.Enabled = outputs.Enabled = refresh.Enabled = false;
        tray.Text = "Little Quieter — listening";
        if (!soundFailed)
            footer.Text = "Listening. Keep your mic position steady; the limit is a relative mic level.";
        return true;
    }
    private void ToggleListening()
    {
        if (input == null)
            StartListening();
        else
            StopListening("Paused. Your microphone is off.");
    }
    private void StartWatchdog()
    {
        watchdog.Change(AlarmGate.WatchdogMilliseconds, AlarmGate.WatchdogMilliseconds);
    }
    private void SetCalibration(bool value)
    {
        calibrating = value;
        gate.Suspend(value);
        learn.Text = value ? "Cancel learning" : "Learn my normal voice";
        limit.Enabled = test.Enabled = !value;
    }
    private void StopListening(string text)
    {
        gate.Suspend(true);
        watchdog.Change(Timeout.Infinite, Timeout.Infinite);
        deviceChanges?.Dispose();
        deviceChanges = null;
        input?.Dispose();
        input = null;
        alarm.Dispose();
        calibrating = false;
        previousActive = previousPreview = false;
        reminder.Hide();
        learn.Text = "Learn my normal voice";
        limit.Enabled = test.Enabled = true;
        start.Text = "Start listening";
        trayToggle.Text = "Start listening";
        microphones.Enabled = outputs.Enabled = refresh.Enabled = true;
        tray.Text = "Little Quieter — paused";
        stateCaption.Text = "PAUSED · MICROPHONE OFF";
        stateCaption.ForeColor = Muted;
        levelCaption.Text = "—";
        liveCaption.Text = "Start listening to see your microphone level.";
        meter.Level = 0;
        meter.Invalidate();
        lastDb = -100;
        footer.Text = text;
    }
    private void ToggleCalibration()
    {
        if (calibrating)
        {
            SetCalibration(false);
            if (!beforeCalibration)
                StopListening("Learning cancelled. Your previous limit is unchanged.");
            else
                footer.Text = "Learning cancelled. Your previous limit is unchanged.";
            return;
        }
        beforeCalibration = input != null;
        if (!StartListening(startSuspended: true))
            return;
        SetCalibration(true);
        reminder.Hide();
        calibration.Clear();
        calibrationStart = AudioClock.Now + 2;
        calibrationEnd = calibrationStart + 8;
        footer.Text = "Get ready, then talk at a comfortable indoor volume for 8 seconds.";
    }
    private void TickMonitor()
    {
        if (exitSignal.WaitOne(0))
        {
            Close();
            return;
        }
        if (showSignal.WaitOne(0))
            RestoreWindow();
        if (!initialized)
        {
            initialized = true;
            if (background)
                StartListening(true);
        }
        double now = AudioClock.Now;
        bool previewing = gate.IsPreviewing(now);
        timer.Interval = Visible || calibrating || gate.Active || previousActive || previewing ? 100 : 250;
        if (saveAt > 0 && now >= saveAt)
        {
            saveAt = 0;
            SavePreferences(false);
        }
        if (input == null)
        {
            if (!previewing && previousPreview)
            {
                gate.Reset();
                alarm.Dispose();
                watchdog.Change(Timeout.Infinite, Timeout.Infinite);
            }
            UpdateReminder(false, previewing);
            return;
        }
        if (deviceChanges == null || deviceChanges.Version != deviceVersion)
        {
            StopListening("Audio device changed.");
            LoadDevices();
            StartListening(true);
            return;
        }
        if (input.Fault != null)
        {
            string error = input.Fault.Message;
            StopListening("Microphone stopped. Reconnect it and click Refresh. " + error);
            return;
        }
        if (alarm.Fault != null && !soundFailed)
        {
            soundFailed = true;
            ConfigureGate();
            footer.Text = "Tone output stopped. Check sound output, then use Test tone to retry.";
        }
        bool changed = ReadInput(input, now, out bool clipped);
        if (now - lastFrameTime > 3)
        {
            StopListening("No audio is arriving. Check the microphone and restart listening.");
            return;
        }
        if (calibrating && now >= calibrationEnd)
            CompleteCalibration();
        bool active = gate.Active;
        UpdateReminder(active, previewing);
        if (Visible)
            UpdateLevelDisplay(now, active, changed, clipped);
    }
    private bool ReadInput(AudioInput source, double now, out bool clipped)
    {
        bool changed = false;
        clipped = false;
        while (source.TryRead(out SoundFrame frame))
        {
            lastFrameTime = frame.Time;
            if (now - frame.Time > 0.5)
                continue;
            lastDb = frame.Db;
            changed = true;
            clipped |= frame.Clipped;
            if (calibrating && frame.Time >= calibrationStart && frame.Time <= calibrationEnd)
                calibration.Add(frame.Db);
        }
        return changed;
    }
    private void CompleteCalibration()
    {
        if (Levels.TryCalibrate(calibration, out int learned))
        {
            limit.Value = learned;
            footer.Text = "Learned your normal voice. Adjust the limit if needed.";
        }
        else
            footer.Text = "Couldn't learn a clear voice level. Try again or set the limit manually.";
        SetCalibration(false);
    }
    private void UpdateLevelDisplay(double now, bool active, bool changed, bool clipped)
    {
        if (changed)
        {
            meter.Level = Levels.ToMeter(lastDb);
            meter.Invalidate();
            levelCaption.Text = ((int)Math.Round(meter.Level)).ToString();
        }
        if (calibrating)
        {
            stateCaption.Text = now < calibrationStart ? "LEARNING · GET READY" : "LEARNING · " + Math.Ceiling(calibrationEnd - now) + " SECONDS LEFT";
            stateCaption.ForeColor = Mint;
            liveCaption.Text = "Talk at a comfortable indoor volume.";
        }
        else
        {
            stateCaption.Text = active ? "ABOVE YOUR LIMIT · QUIETEN DOWN" : "LISTENING · WITHIN YOUR LIMIT";
            stateCaption.ForeColor = active ? Amber : Mint;
            liveCaption.Text = clipped ? "Input is clipping. Lower mic gain or move it back." : meter.Level < 3 ? "Very little input. Check your mic if you're speaking." : "Your limit is " + limit.Value + ". Tone follows your microphone level.";
        }
    }
    private void PreviewAlarm()
    {
        if (calibrating)
            return;
        if (!alarm.IsReady || soundFailed)
            PrepareOutput();
        if (soundFailed)
            return;
        gate.Suspend(false);
        ConfigureGate();
        double now = AudioClock.Now;
        gate.Preview(now);
        StartWatchdog();
        UpdateReminder(gate.Active, gate.IsPreviewing(now));
    }
    private void UpdateReminder(bool active, bool previewing)
    {
        if (!active && !previewing)
            reminder.Hide();
        else if (active != previousActive || previewing != previousPreview)
            reminder.Remind(this, previewing ? "Test tone · two seconds" : "Keep your voice down, please.");
        previousActive = active;
        previousPreview = previewing;
    }
    private void RestoreWindow()
    {
        showRequested = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }
    private void QueueSave()
    {
        saveAt = AudioClock.Now + 0.8;
    }
    private void SavePreferences(bool report)
    {
        preferences.Limit = limit.Value;
        preferences.PlayTone = playTone.Checked;
        preferences.ToneVolume = (int)toneVolume.Value;
        if (microphones.SelectedItem is AudioDevice microphone)
            preferences.DeviceName = microphone.Name;
        if (outputs.SelectedItem is AudioDevice output)
        {
            preferences.OutputDeviceId = output.Id;
            preferences.OutputDeviceName = output.Name.Replace(" (unavailable)", "");
        }
        try
        {
            preferences.Save(settingsPath);
        }
        catch (Exception error)
        {
            string message = "Couldn't save settings: " + error.Message;
            if (report)
                MessageBox.Show(message, "Little Quieter");
            else
                footer.Text = message;
        }
    }
    private void Shutdown()
    {
        if (closing)
            return;
        closing = true;
        timer.Stop();
        StopListening("Stopped.");
        watchdog.Dispose();
        reminder.Close();
        reminder.Dispose();
        tray.Visible = false;
        tray.Dispose();
        SavePreferences(true);
        timer.Dispose();
    }
}
