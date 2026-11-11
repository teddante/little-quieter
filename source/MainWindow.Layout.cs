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
    private readonly LinkLabel startupSettings;
    private readonly DateTimePicker scheduleStart, scheduleEnd;
    private readonly Label scheduleCaption;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayToggle, trayTest;
    private readonly System.Windows.Forms.Timer timer;
    private readonly Panel scroll;
    private readonly TableLayoutPanel content;
    private readonly FlowLayoutPanel primaryActions;
    private FormWindowState restoredWindowState = FormWindowState.Normal;

    public MainWindow(bool startInBackground, EventWaitHandle show, EventWaitHandle exit, string? preferencesPath = null, DateTime? pausedForUpdateAt = null)
    {
        SuspendLayout();
        background = startInBackground;
        showSignal = show;
        exitSignal = exit;
        gate = new AlarmGate(alarm.SetActive);
        watchdog = new System.Threading.Timer(_ => gate.Refresh(AudioClock.Now), null, Timeout.Infinite, Timeout.Infinite);
        settingsPath = preferencesPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleQuieter", "settings.xml");
        preferences = Preferences.Load(settingsPath);
        appliedVolume = preferences.ToneVolume;
        preferencesSaved = !preferences.IsNew && !preferences.NeedsRecovery;
        showRequested = preferences.IsNew || preferences.NeedsRecovery;
        Text = "Little Quieter";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = SystemFonts.MessageBoxFont;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 540);
        MinimumSize = new Size(460, 520);
        StartPosition = FormStartPosition.CenterScreen;
        scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
        content = Column();
        scroll.Controls.Add(content);
        Controls.Add(scroll);
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
        meter = new LevelMeter
        {
            Dock = DockStyle.Top,
            Height = 24,
            Limit = preferences.Limit
        };
        AddRow(card, stateCaption);
        AddRow(card, levelCaption);
        AddRow(card, liveCaption);
        AddRow(card, meter);
        AddRow(content, card);
        var settings = Column();
        bool settingsExpanded = preferences.IsNew || preferences.NeedsRecovery || preferences.ScheduleEnabled;
        var settingsToggle = ActionButton(settingsExpanded ? "Hide devices, startup and schedule" : "Show devices, startup and schedule");
        settings.Visible = settingsExpanded;
        settingsToggle.Click += (_, _) =>
        {
            settingsExpanded = !settingsExpanded;
            settings.Visible = settingsExpanded;
            settingsToggle.Text = settingsExpanded ? "Hide devices, startup and schedule" : "Show devices, startup and schedule";
            FitWindow(true);
        };
        AddRow(settings, Caption("Microphone", true));
        microphones = DevicePicker("Microphone");
        AddRow(settings, microphones);
        AddRow(settings, Caption("Alarm output", true));
        outputs = DevicePicker("Alarm output. Only changes Little Quieter");
        AddRow(settings, outputs);
        refresh = ActionButton("&Refresh devices");
        refresh.Click += (_, _) => LoadDevices();
        AddRow(settings, refresh);
        limitCaption = Caption("", true);
        AddRow(content, limitCaption);
        limit = new ThemeTrackBar { Dock = DockStyle.Top, Minimum = Levels.MinimumLimit, Maximum = Levels.MaximumLimit, Value = preferences.Limit, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 5, AccessibleName = "Loudness limit. Lower warns sooner" };
        AddRow(content, limit);
        AddRow(content, Caption("Lower warns sooner. Higher allows a louder voice."));
        start = ActionButton("Start listening");
        start.Click += async (_, _) => await ToggleListening();
        learn = ActionButton("Learn my normal voice");
        learn.Click += async (_, _) => await ToggleCalibration();
        test = ActionButton("Test tone (2s)");
        test.Click += async (_, _) => await PreviewAlarm();
        primaryActions = Actions(start, learn, test);
        AddRow(content, primaryActions);
        playTone = Option("Play steady alarm tone", preferences.PlayTone);
        showReminder = Option("Show visual reminder", preferences.ShowReminder);
        toneVolume = new ThemeNumericUpDown { Width = 80, Minimum = 0, Maximum = 100, Value = preferences.ToneVolume, Increment = 5, AccessibleName = "Alarm tone volume percent" };
        var volumeControls = Actions(Caption("Volume (%)"), toneVolume);
        volumeControls.WrapContents = false;
        AddRow(content, Actions(playTone, volumeControls));
        AddRow(content, showReminder);
        runAtStartup = Option("Request startup with Windows", false);
        startupSettings = new LinkLabel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 5, 0, 5),
            LinkColor = SystemColors.WindowText,
            VisitedLinkColor = SystemColors.WindowText,
            ActiveLinkColor = SystemColors.WindowText
        };
        startupSettings.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
        RefreshStartup();
        AddRow(settings, runAtStartup);
        AddRow(settings, startupSettings);
        scheduleEnabled = Option("Listen on a daily schedule", preferences.ScheduleEnabled);
        scheduleStart = TimePicker("Daily start time", preferences.ScheduleStartMinute);
        scheduleEnd = TimePicker("Daily end time", preferences.ScheduleEndMinute);
        AddRow(settings, scheduleEnabled);
        AddRow(settings, Actions(Caption("From"), scheduleStart, Caption("to"), scheduleEnd));
        AddRow(settings, Caption("Local time · every day · overnight ranges supported"));
        scheduleCaption = Caption("");
        AddRow(settings, scheduleCaption);
        AddRow(content, settingsToggle);
        AddRow(content, settings);
        footer = Caption(preferences.IsNew ? "Choose your microphone, learn your voice, then start listening." : "Ready.");
        AddRow(content, footer);
        AddRow(content, Caption("Minimize to keep listening · Close exits · No recordings"));
        runAtStartup.CheckedChanged += (_, _) => ChangeStartup();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Little Quieter", null, (_, _) => RestoreWindow());
        trayToggle = new ToolStripMenuItem("Start listening", null, async (_, _) => await ToggleListening());
        menu.Items.Add(trayToggle);
        trayTest = (ToolStripMenuItem)menu.Items.Add("Test tone (2s)", null, async (_, _) => await PreviewAlarm());
        menu.Items.Add("Dismiss reminder", null, (_, _) => DismissReminder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());
        tray = new NotifyIcon { Text = "Little Quieter — paused", Icon = Icon, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                Hide();
            else
                restoredWindowState = WindowState;
        };
        FormClosing += async (_, e) =>
        {
            if (closing)
                return;
            bool waitForAudio = e.CloseReason == CloseReason.UserClosing;
            e.Cancel = waitForAudio;
            Shutdown(waitForAudio);
            if (waitForAudio)
            {
                try
                {
                    await AudioIdle;
                }
                catch (Exception error) { MessageBox.Show("Audio stopped. Device cleanup reported: " + error.Message, "Little Quieter"); }
                Close();
            }
        };
        Shown += (_, _) => FitWindow(true);
        DpiChanged += (_, _) => BeginInvoke(() => { if (!closing) FitWindow(false); });
        timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += async (_, _) => await TickMonitor();
        timer.Start();
        LoadDevices();
        UpdateLimit();
        limit.ValueChanged += (_, _) => { if (!refreshingTheme) { UpdateLimit(); ConfigureGate(); QueueSave(); } };
        playTone.CheckedChanged += async (_, _) => await ChangeToneSettings();
        reminder.Dismissed += () => reminderState.Dismiss();
        showReminder.CheckedChanged += (_, _) =>
        {
            if (refreshingTheme)
                return;
            reminderState.Reset();
            UpdateReminder(gate.Active, gate.IsPreviewing(AudioClock.Now));
            QueueSave();
        };
        toneVolume.ValueChanged += async (_, _) => await ChangeToneSettings();
        microphones.SelectedIndexChanged += (_, _) => { if (!loadingDevices && !refreshingTheme) QueueSave(); };
        outputs.SelectedIndexChanged += async (_, _) => await ChangeOutput();
        scheduleEnabled.CheckedChanged += async (_, _) => await ChangeSchedule();
        scheduleStart.ValueChanged += async (_, _) => await ChangeSchedule();
        scheduleEnd.ValueChanged += async (_, _) => await ChangeSchedule();
        ConfigureGate();
        DateTime launchTime = DateTime.Now;
        pauseAfterUpdate = pausedForUpdateAt.HasValue && (!scheduleEnabled.Checked ||
            DailySchedule.ActivePeriodStart(StartMinute, EndMinute, pausedForUpdateAt.Value) == DailySchedule.ActivePeriodStart(StartMinute, EndMinute, launchTime));
        if (pauseAfterUpdate)
            dailySchedule.Update(scheduleEnabled.Checked, StartMinute, EndMinute, launchTime);
        UpdateListeningControls(DateTime.Now);
        ResumeLayout(true);
    }

    internal void FitWindow(bool fitContent, Rectangle? workingArea = null)
    {
        Rectangle area = workingArea ?? Screen.FromControl(this).WorkingArea;
        int width = Math.Max(LogicalToDeviceUnits(444), primaryActions.PreferredSize.Width + scroll.Padding.Horizontal + SystemInformation.VerticalScrollBarWidth);
        Size minimum = SizeFromClientSize(new Size(width, LogicalToDeviceUnits(480)));
        MinimumSize = new Size(Math.Min(minimum.Width, area.Width), Math.Min(minimum.Height, area.Height));
        if (WindowState != FormWindowState.Normal)
            return;
        Width = Math.Clamp(Width, MinimumSize.Width, area.Width);
        if (fitContent)
        {
            scroll.AutoScrollPosition = Point.Empty;
            content.PerformLayout();
            int height = SizeFromClientSize(new Size(ClientSize.Width,
                content.GetPreferredSize(new Size(content.Width, 0)).Height + scroll.Padding.Vertical)).Height;
            Height = Math.Clamp(height, MinimumSize.Height, area.Height);
        }
        else
            Height = Math.Clamp(Height, MinimumSize.Height, area.Height);
        Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height));
    }
    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(value && (!background || showRequested));
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.ApplyTitle(this);
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
        return new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 5, 0, 5), Font = bold || scale != 1 ? new Font(Font.FontFamily, Font.Size * scale, bold ? FontStyle.Bold : FontStyle.Regular) : Font };
    }
    private static Button ActionButton(string text) => new ThemeButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 4, 8, 4) };
    private static CheckBox Option(string text, bool value) => new ThemeCheckBox { Text = text, AutoSize = true, Checked = value, AccessibleName = text, Margin = new Padding(0, 5, 0, 5) };
    private static ComboBox DevicePicker(string name) => new ThemeComboBox { Dock = DockStyle.Top, DropDownWidth = 550, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name };
    private static FlowLayoutPanel Actions(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty };
        foreach (var control in controls)
        {
            control.Dock = DockStyle.None;
            control.Anchor = AnchorStyles.Left;
        }
        flow.Controls.AddRange(controls);
        return flow;
    }
    private static DateTimePicker TimePicker(string name, int minute) => new ThemeDateTimePicker { Width = 100, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Today.AddMinutes(minute), AccessibleName = name };
}
