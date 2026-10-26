using NAudio.CoreAudioApi;

namespace LittleQuieter;

internal sealed partial class MainWindow : Form
{
    private readonly string settingsPath;
    private readonly Preferences preferences;
    private readonly AlarmTone alarm = new AlarmTone();
    private readonly AlarmGate gate;
    private readonly DailySchedule dailySchedule = new DailySchedule();
    private readonly System.Threading.Timer watchdog;
    private readonly ReminderWindow reminder = new ReminderWindow();
    private readonly List<double> calibration = new List<double>();
    private readonly EventWaitHandle showSignal, exitSignal;
    private readonly bool background;
    private readonly bool pauseAfterUpdate;
    private AudioInput? input;
    private AudioDeviceChanges? deviceChanges;
    private int deviceVersion;
    private bool showRequested, initialized, changingStartup, soundFailed;
    private bool calibrating, beforeCalibration, closing, previousActive, previousPreview;
    private double lastFrameTime, calibrationStart, calibrationEnd, saveAt, lastDb = -100;

    private int StartMinute => (int)scheduleStart.Value.TimeOfDay.TotalMinutes;
    private int EndMinute => (int)scheduleEnd.Value.TimeOfDay.TotalMinutes;
    internal bool IsListening => input != null;

    private bool ScheduleAllowsListening(DateTime localNow)
    {
        return !preferences.NeedsRecovery && (!scheduleEnabled.Checked || DailySchedule.ActivePeriodStart(StartMinute, EndMinute, localNow).HasValue);
    }
    private void ChangeSchedule()
    {
        ApplySchedule(DateTime.Now);
        UpdateScheduleControls(DateTime.Now);
        if (!scheduleEnabled.Checked && input == null)
            footer.Text = "Schedule off. Start listening whenever you need it.";
        QueueSave();
    }
    internal void ApplySchedule(DateTime localNow)
    {
        if (preferences.NeedsRecovery)
            return;
        bool? decision = dailySchedule.Update(scheduleEnabled.Checked, StartMinute, EndMinute, localNow);
        if (decision == true)
            StartListening(true);
        else if (decision == false)
            StopListening("Outside your scheduled hours. Your microphone and alarm output are off.");
        if (decision.HasValue)
            UpdateScheduleControls(localNow);
    }
    private void UpdateScheduleControls(DateTime localNow)
    {
        if (preferences.NeedsRecovery)
        {
            start.Enabled = trayToggle.Enabled = learn.Enabled = test.Enabled = trayTest.Enabled = false;
            start.Text = trayToggle.Text = "Recover settings to listen";
            scheduleCaption.Text = "Recover settings before changing the listening schedule.";
            stateCaption.Text = "SETTINGS RECOVERY · MICROPHONE OFF";
            tray.Text = "Little Quieter — settings need recovery";
            return;
        }
        bool allowed = ScheduleAllowsListening(localNow);
        start.Enabled = trayToggle.Enabled = learn.Enabled = allowed;
        test.Enabled = trayTest.Enabled = allowed && !calibrating;
        start.Text = trayToggle.Text = !allowed ? "Waiting for schedule" : input == null ? "Start listening" : "Stop listening";
        if (!scheduleEnabled.Checked)
            scheduleCaption.Text = "Choose your hours, then enable the schedule. It is currently off.";
        else if (StartMinute == EndMinute)
            scheduleCaption.Text = "Choose different start and end times. The microphone stays off.";
        else if (!allowed)
            scheduleCaption.Text = "Waiting until " + scheduleStart.Text + ". Keep the app running in the tray.";
        else
            scheduleCaption.Text = input == null ? "Paused until the next scheduled period, or start listening now." : "Active until " + scheduleEnd.Text + ". Stop pauses until the next period.";
        if (!allowed && input == null)
        {
            stateCaption.Text = "SCHEDULED · MICROPHONE OFF";
            tray.Text = "Little Quieter — waiting for schedule";
        }
        else if (input == null)
        {
            stateCaption.Text = "PAUSED · MICROPHONE OFF";
            tray.Text = "Little Quieter — paused";
        }
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
    private void ChangeStartup()
    {
        if (changingStartup)
            return;
        try
        {
            StartupRegistration.SetEnabled(runAtStartup.Checked, Application.ExecutablePath);
            footer.Text = runAtStartup.Checked ? "Startup enabled. It will listen in the tray when you sign in." : "Startup disabled.";
        }
        catch (Exception error)
        {
            changingStartup = true;
            runAtStartup.Checked = !runAtStartup.Checked;
            changingStartup = false;
            footer.Text = "Couldn't change startup: " + error.Message;
        }
    }
    internal void LoadDevices()
    {
        if (input != null)
            return;
        var selectedInput = microphones.SelectedItem as AudioDevice;
        string? inputId = selectedInput == null ? preferences.DeviceId : selectedInput.Id;
        string inputName = selectedInput?.Name ?? preferences.DeviceName;
        var selectedOutput = outputs.SelectedItem as AudioDevice;
        string? outputId = selectedOutput == null ? preferences.OutputDeviceId : selectedOutput.Id;
        string outputName = selectedOutput?.Name ?? preferences.OutputDeviceName;
        microphones.Items.Clear();
        outputs.Items.Clear();
        try
        {
            var captureDevices = AudioDevice.List(DataFlow.Capture);
            microphones.Items.AddRange(captureDevices.ToArray());
            SelectDevice(microphones, AudioDevice.Select(captureDevices, inputId, inputName, AudioDevice.DefaultMicrophoneName));
            var renderDevices = AudioDevice.List(DataFlow.Render);
            outputs.Items.AddRange(renderDevices.ToArray());
            SelectDevice(outputs, AudioDevice.Select(renderDevices, outputId, outputName, AudioDevice.DefaultOutputName));
        }
        catch (Exception error) { footer.Text = "Couldn't list audio devices: " + error.Message; }
    }
    private static void SelectDevice(ComboBox list, AudioDevice device)
    {
        if (!list.Items.Contains(device))
            list.Items.Add(device);
        list.SelectedItem = device;
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
        if (!ScheduleAllowsListening(DateTime.Now))
            return false;
        if (input != null)
            return true;
        if (microphones.SelectedItem is not AudioDevice device)
        {
            footer.Text = "Connect a microphone, then click Refresh.";
            return false;
        }
        if (device.Unavailable)
        {
            footer.Text = "Your selected microphone is unavailable. Reconnect it and Refresh, or choose another microphone.";
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
            deviceChanges = new AudioDeviceChanges(() => gate.Suspend(true), device.Id == null, (outputs.SelectedItem as AudioDevice)?.Id == null);
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
        UpdateScheduleControls(DateTime.Now);
        return true;
    }
    internal void ToggleListening()
    {
        if (input == null)
            StartListening();
        else
            StopListening(scheduleEnabled.Checked ? "Paused until the next scheduled period, or start listening to resume now." : "Paused. Your microphone is off.");
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
        UpdateScheduleControls(DateTime.Now);
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
        stateCaption.ForeColor = SystemColors.WindowText;
        levelCaption.Text = "—";
        liveCaption.Text = "Start listening to see your microphone level.";
        meter.Level = 0;
        meter.Invalidate();
        lastDb = -100;
        footer.Text = text;
        UpdateScheduleControls(DateTime.Now);
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
            if (background && !pauseAfterUpdate && !preferences.IsNew && !preferences.NeedsRecovery && !scheduleEnabled.Checked)
                StartListening(true);
        }
        ApplySchedule(DateTime.Now);
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
            stateCaption.ForeColor = SystemColors.WindowText;
            liveCaption.Text = "Talk at a comfortable indoor volume.";
        }
        else
        {
            stateCaption.Text = active ? "ABOVE YOUR LIMIT · QUIETEN DOWN" : "LISTENING · WITHIN YOUR LIMIT";
            stateCaption.ForeColor = SystemColors.WindowText;
            liveCaption.Text = clipped ? "Input is clipping. Lower mic gain or move it back." : meter.Level < 3 ? "Very little input. Check your mic if you're speaking." : "Your limit is " + limit.Value + ". Tone follows your microphone level.";
        }
    }
    private void PreviewAlarm()
    {
        if (calibrating || !ScheduleAllowsListening(DateTime.Now))
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
    private void ChangeOutput()
    {
        if (input == null)
        {
            gate.Reset();
            alarm.Dispose();
            watchdog.Change(Timeout.Infinite, Timeout.Infinite);
            UpdateReminder(false, false);
        }
        QueueSave();
    }
    private void UpdateReminder(bool active, bool previewing)
    {
        if (!showReminder.Checked || (!active && !previewing))
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
    private void RecoverPreferences(Control notice)
    {
        try
        {
            string backup = preferences.Recover(settingsPath);
            notice.Hide();
            SetSettingsEnabled(true);
            footer.Text = "Original settings backed up to " + backup + ". Choose your microphone before starting.";
            UpdateScheduleControls(DateTime.Now);
        }
        catch (Exception error) { MessageBox.Show(this, "Your original settings were kept. " + error.Message, "Settings recovery"); }
    }
    private void SetSettingsEnabled(bool enabled)
    {
        microphones.Enabled = outputs.Enabled = refresh.Enabled = limit.Enabled = playTone.Enabled =
            showReminder.Enabled = toneVolume.Enabled = scheduleEnabled.Enabled = scheduleStart.Enabled = scheduleEnd.Enabled = enabled;
    }
    private void SavePreferences(bool report)
    {
        if (preferences.NeedsRecovery)
            return;
        preferences.Limit = limit.Value;
        preferences.PlayTone = playTone.Checked;
        preferences.ToneVolume = (int)toneVolume.Value;
        preferences.ScheduleEnabled = scheduleEnabled.Checked;
        preferences.ScheduleStartMinute = StartMinute;
        preferences.ScheduleEndMinute = EndMinute;
        preferences.ShowReminder = showReminder.Checked;
        if (microphones.SelectedItem is AudioDevice microphone)
        {
            preferences.DeviceId = microphone.Id;
            preferences.DeviceName = microphone.Name;
        }
        if (outputs.SelectedItem is AudioDevice output)
        {
            preferences.OutputDeviceId = output.Id;
            preferences.OutputDeviceName = output.Name;
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
        reminder.Dispose();
        tray.Visible = false;
        tray.Dispose();
        SavePreferences(true);
        timer.Dispose();
    }
}
