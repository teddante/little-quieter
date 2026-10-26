namespace LittleQuieter;

internal sealed partial class MainWindow
{
    private readonly ComboBox microphones, outputs;
    private readonly TrackBar limit;
    private readonly Label limitCaption, stateCaption, levelCaption, liveCaption, footer;
    private readonly LevelMeter meter;
    private readonly Button start, learn, refresh, test;
    private readonly NumericUpDown toneVolume;
    private readonly CheckBox playTone, runAtStartup, showReminder, scheduleEnabled;
    private readonly DateTimePicker scheduleStart, scheduleEnd;
    private readonly Label scheduleCaption;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayToggle, trayTest;
    private readonly System.Windows.Forms.Timer timer;

    public MainWindow(bool startInBackground, EventWaitHandle show, EventWaitHandle exit, string? preferencesPath = null, DateTime? pausedForUpdateAt = null)
    {
        background = startInBackground;
        showSignal = show;
        exitSignal = exit;
        gate = new AlarmGate(alarm.SetActive);
        watchdog = new System.Threading.Timer(_ => gate.Refresh(AudioClock.Now), null, Timeout.Infinite, Timeout.Infinite);
        settingsPath = preferencesPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleQuieter", "settings.xml");
        preferences = Preferences.Load(settingsPath);
        showRequested = preferences.IsNew || preferences.NeedsRecovery;
        Text = "Little Quieter";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = SystemFonts.MessageBoxFont;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(650, 800);
        MinimumSize = new Size(400, 400);
        StartPosition = FormStartPosition.CenterScreen;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
        var content = Column();
        scroll.Controls.Add(content);
        Controls.Add(scroll);
        AddRow(content, Caption("Little Quieter", true, 1.8f));
        AddRow(content, Caption("Keep your voice within your chosen limit."));
        var recovery = Column();
        recovery.Visible = preferences.NeedsRecovery;
        AddRow(recovery, Caption("Your settings could not be read. Listening is paused and the original file is preserved. Repair settings.xml and restart, or back it up and use new settings."));
        var recover = ActionButton("Back up and use new settings");
        recover.Click += (_, _) => RecoverPreferences(recovery);
        AddRow(recovery, recover);
        AddRow(content, recovery);
        var card = Column();
        card.Padding = new Padding(12);
        card.BorderStyle = BorderStyle.FixedSingle;
        stateCaption = Caption("PAUSED · MICROPHONE OFF", true);
        levelCaption = Caption("—", true, 2);
        liveCaption = Caption("Start listening to see your microphone level.");
        meter = new LevelMeter { Dock = DockStyle.Top, Height = 24, Limit = preferences.Limit };
        AddRow(card, stateCaption);
        AddRow(card, levelCaption);
        AddRow(card, liveCaption);
        AddRow(card, meter);
        AddRow(content, card);
        AddRow(content, Caption("Microphone", true));
        microphones = DevicePicker("Microphone");
        AddRow(content, microphones);
        AddRow(content, Caption("Alarm output (only Little Quieter)", true));
        outputs = DevicePicker("Alarm output. Only changes Little Quieter");
        AddRow(content, outputs);
        refresh = ActionButton("&Refresh devices");
        refresh.Click += (_, _) => LoadDevices();
        AddRow(content, refresh);
        limitCaption = Caption("", true);
        AddRow(content, limitCaption);
        limit = new TrackBar { Dock = DockStyle.Top, Minimum = Levels.MinimumLimit, Maximum = Levels.MaximumLimit, Value = preferences.Limit, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 5, AccessibleName = "Loudness limit. Lower warns sooner" };
        AddRow(content, limit);
        AddRow(content, Caption("Lower warns sooner. Higher allows a louder voice."));
        start = ActionButton("Start listening");
        start.Click += (_, _) => ToggleListening();
        learn = ActionButton("Learn my normal voice");
        learn.Click += (_, _) => ToggleCalibration();
        test = ActionButton("Test tone (2s)");
        test.Click += (_, _) => PreviewAlarm();
        AddRow(content, Actions(start, learn, test));
        playTone = Option("Play steady alarm tone", preferences.PlayTone);
        showReminder = Option("Show visual reminder", preferences.ShowReminder);
        AddRow(content, playTone);
        AddRow(content, showReminder);
        toneVolume = new NumericUpDown { Width = 80, Minimum = 0, Maximum = 100, Value = preferences.ToneVolume, Increment = 5, AccessibleName = "Alarm tone volume percent" };
        AddRow(content, Actions(Caption("Alarm volume (%)"), toneVolume));
        runAtStartup = Option("Start with Windows and listen in the background", false);
        try
        {
            runAtStartup.Checked = StartupRegistration.Enabled;
        }
        catch (System.Security.SecurityException) { }
        AddRow(content, runAtStartup);
        scheduleEnabled = Option("Listen on a daily schedule", preferences.ScheduleEnabled);
        scheduleStart = TimePicker("Daily start time", preferences.ScheduleStartMinute);
        scheduleEnd = TimePicker("Daily end time", preferences.ScheduleEndMinute);
        AddRow(content, scheduleEnabled);
        AddRow(content, Actions(Caption("From"), scheduleStart, Caption("to"), scheduleEnd));
        AddRow(content, Caption("Local time · every day · overnight ranges supported"));
        scheduleCaption = Caption("");
        AddRow(content, scheduleCaption);
        footer = Caption(preferences.IsNew ? "Choose your microphone, learn your voice, then start listening. Startup is your choice." : "Tone starts above your limit and stops when you quieten down.");
        AddRow(content, footer);
        AddRow(content, Caption("Local only · no recordings · hears other room sounds too\nMinimize to keep listening in the tray. Close × exits and turns the mic off."));
        runAtStartup.CheckedChanged += (_, _) => ChangeStartup();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Little Quieter", null, (_, _) => RestoreWindow());
        trayToggle = new ToolStripMenuItem("Start listening", null, (_, _) => ToggleListening());
        menu.Items.Add(trayToggle);
        trayTest = (ToolStripMenuItem)menu.Items.Add("Test tone (2s)", null, (_, _) => PreviewAlarm());
        menu.Items.Add("Dismiss reminder", null, (_, _) => reminder.Hide());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());
        tray = new NotifyIcon { Text = "Little Quieter — paused", Icon = Icon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, _) => Shutdown();
        Shown += (_, _) =>
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
            Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        };
        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => TickMonitor();
        timer.Start();
        LoadDevices();
        UpdateLimit();
        limit.ValueChanged += (_, _) => { UpdateLimit(); ConfigureGate(); QueueSave(); };
        playTone.CheckedChanged += (_, _) => { ConfigureGate(); QueueSave(); };
        showReminder.CheckedChanged += (_, _) =>
        {
            previousActive = previousPreview = false;
            UpdateReminder(gate.Active, gate.IsPreviewing(AudioClock.Now));
            QueueSave();
        };
        toneVolume.ValueChanged += (_, _) => { alarm.SetVolume((int)toneVolume.Value); ConfigureGate(); QueueSave(); };
        microphones.SelectedIndexChanged += (_, _) => QueueSave();
        outputs.SelectedIndexChanged += (_, _) => ChangeOutput();
        scheduleEnabled.CheckedChanged += (_, _) => ChangeSchedule();
        scheduleStart.ValueChanged += (_, _) => ChangeSchedule();
        scheduleEnd.ValueChanged += (_, _) => ChangeSchedule();
        ConfigureGate();
        if (preferences.NeedsRecovery)
            SetSettingsEnabled(false);
        DateTime launchTime = DateTime.Now;
        pauseAfterUpdate = pausedForUpdateAt.HasValue && (!scheduleEnabled.Checked ||
            DailySchedule.ActivePeriodStart(StartMinute, EndMinute, pausedForUpdateAt.Value) == DailySchedule.ActivePeriodStart(StartMinute, EndMinute, launchTime));
        if (pauseAfterUpdate)
            dailySchedule.Update(scheduleEnabled.Checked, StartMinute, EndMinute, launchTime);
        UpdateScheduleControls(DateTime.Now);
        SavePreferences(false);
    }
    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(value && (!background || showRequested));
    }
    private static TableLayoutPanel Column()
    {
        var column = new TableLayoutPanel { ColumnCount = 1, RowCount = 0, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4) };
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        column.SizeChanged += (_, _) =>
        {
            foreach (Control child in column.Controls)
                if (child is Label or CheckBox)
                    child.MaximumSize = new Size(Math.Max(100, column.ClientSize.Width - column.Padding.Horizontal - child.Margin.Horizontal), 0);
        };
        return column;
    }
    private static void AddRow(TableLayoutPanel column, Control control)
    {
        int row = column.RowCount++;
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        column.Controls.Add(control, 0, row);
    }
    private Label Caption(string text, bool bold = false, float scale = 1)
    {
        return new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 5, 0, 5), Font = new Font(Font.FontFamily, Font.Size * scale, bold ? FontStyle.Bold : FontStyle.Regular) };
    }
    private static Button ActionButton(string text) => new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 4, 8, 4) };
    private static CheckBox Option(string text, bool value) => new CheckBox { Text = text, AutoSize = true, Checked = value, AccessibleName = text, Margin = new Padding(0, 5, 0, 5) };
    private static ComboBox DevicePicker(string name) => new ComboBox { Dock = DockStyle.Top, DropDownWidth = 550, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name };
    private static FlowLayoutPanel Actions(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        return flow;
    }
    private static DateTimePicker TimePicker(string name, int minute) => new DateTimePicker { Width = 100, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Today.AddMinutes(minute), AccessibleName = name };
}
