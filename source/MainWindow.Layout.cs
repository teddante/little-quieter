namespace LittleQuieter;

internal sealed partial class MainWindow
{
    internal static readonly Color Mint = Color.FromArgb(106, 226, 183);
    internal static readonly Color Amber = Color.FromArgb(255, 184, 104);
    private static readonly Color Ink = Color.FromArgb(16, 23, 31);
    private static readonly Color Card = Color.FromArgb(26, 37, 49);
    private static readonly Color Muted = Color.FromArgb(166, 184, 201);
    private readonly ComboBox microphones, outputs;
    private readonly TrackBar limit;
    private readonly Label limitCaption, stateCaption, levelCaption, liveCaption, footer;
    private readonly LevelMeter meter;
    private readonly Button start, learn, refresh, test;
    private readonly NumericUpDown toneVolume;
    private readonly CheckBox playTone, runAtStartup;
    private readonly CheckBox scheduleEnabled;
    private readonly DateTimePicker scheduleStart, scheduleEnd;
    private readonly Label scheduleCaption;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayToggle;
    private readonly ToolStripMenuItem trayTest;
    private readonly System.Windows.Forms.Timer timer;

    public MainWindow(bool startInBackground, EventWaitHandle show, EventWaitHandle exit, string? preferencesPath = null)
    {
        background = startInBackground;
        showSignal = show;
        exitSignal = exit;
        gate = new AlarmGate(alarm.SetActive);
        watchdog = new System.Threading.Timer(_ => gate.Refresh(AudioClock.Now), null, Timeout.Infinite, Timeout.Infinite);
        settingsPath = preferencesPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleQuieter", "settings.xml");
        preferences = Preferences.Load(settingsPath);
        Text = "Little Quieter";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Segoe UI", 10);
        BackColor = Ink;
        ForeColor = Color.White;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(650, 832);
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
        refresh.Click += (_, _) => LoadDevices();
        Controls.AddRange(new Control[] { microphones, outputs, refresh });
        limitCaption = Label("", 24, 358, 600, 26, 11, Color.White, true);
        Controls.Add(limitCaption);
        limit = new TrackBar { Location = new Point(18, 388), Size = new Size(615, 36), AutoSize = false, Minimum = Levels.MinimumLimit, Maximum = Levels.MaximumLimit, Value = preferences.Limit, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 5, AccessibleName = "Loudness limit. Lower warns sooner" };
        Controls.Add(limit);
        Controls.Add(Label("Lower = warns sooner. Higher = allows a louder voice.", 24, 426, 600, 23, 10, Muted));
        start = Button("Start listening", 24, 468, 176, 43, true);
        start.Click += (_, _) => ToggleListening();
        learn = Button("Learn my normal voice", 212, 468, 238, 43);
        learn.Click += (_, _) => ToggleCalibration();
        test = Button("Test tone (2s)", 462, 468, 164, 43);
        test.Click += (_, _) => PreviewAlarm();
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
        scheduleEnabled = new CheckBox { Text = "Listen on a daily schedule", Location = new Point(24, 620), Size = new Size(602, 27), Checked = preferences.ScheduleEnabled, AccessibleName = "Listen on a daily schedule" };
        scheduleStart = TimePicker("Daily start time", 75, preferences.ScheduleStartMinute);
        scheduleEnd = TimePicker("Daily end time", 220, preferences.ScheduleEndMinute);
        Controls.Add(Label("From", 24, 663, 47, 24, 10, Muted));
        Controls.Add(Label("to", 184, 663, 30, 24, 10, Muted));
        Controls.Add(Label("Local time · every day\nOvernight ranges supported", 327, 658, 299, 42, 9, Muted));
        scheduleCaption = Label("", 24, 701, 602, 38, 10, Muted);
        Controls.AddRange(new Control[] { scheduleEnabled, scheduleStart, scheduleEnd, scheduleCaption });
        footer = Label("Tone starts above your limit and stops when you quieten down.", 24, 744, 602, 38, 10, Muted);
        Controls.Add(footer);
        Controls.Add(Label("Local only · no recordings · hears other room sounds too\nMinimize to keep listening in the tray. Close × exits and turns the mic off.", 24, 786, 602, 42, 9, Muted));
        runAtStartup.CheckedChanged += (_, _) => ChangeStartup();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Little Quieter", null, (_, _) => RestoreWindow());
        trayToggle = new ToolStripMenuItem("Start listening", null, (_, _) => ToggleListening());
        menu.Items.Add(trayToggle);
        trayTest = (ToolStripMenuItem)menu.Items.Add("Test tone (2s)", null, (_, _) => PreviewAlarm());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());
        tray = new NotifyIcon { Text = "Little Quieter — paused", Icon = Icon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
        };
        FormClosing += (_, _) => Shutdown();
        Shown += (_, _) =>
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height - 24)
            {
                Height = area.Height - 24;
                Top = area.Top + 12;
            }
        };
        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => TickMonitor();
        timer.Start();
        LoadDevices();
        UpdateLimit();
        ApplyContrast(this);
        limit.ValueChanged += (_, _) =>
        {
            UpdateLimit();
            ConfigureGate();
            QueueSave();
        };
        playTone.CheckedChanged += (_, _) =>
        {
            ConfigureGate();
            QueueSave();
        };
        toneVolume.ValueChanged += (_, _) =>
        {
            alarm.SetVolume((int)toneVolume.Value);
            ConfigureGate();
            QueueSave();
        };
        microphones.SelectedIndexChanged += (_, _) => QueueSave();
        outputs.SelectedIndexChanged += (_, _) => QueueSave();
        scheduleEnabled.CheckedChanged += (_, _) => ChangeSchedule();
        scheduleStart.ValueChanged += (_, _) => ChangeSchedule();
        scheduleEnd.ValueChanged += (_, _) => ChangeSchedule();
        ConfigureGate();
        UpdateScheduleControls(DateTime.Now);
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
    private static DateTimePicker TimePicker(string name, int x, int minute)
    {
        return new DateTimePicker { Location = new Point(x, 660), Size = new Size(93, 29), Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Today.AddMinutes(minute), AccessibleName = name };
    }
}
