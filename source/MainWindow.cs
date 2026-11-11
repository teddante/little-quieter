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
    private readonly ReminderState reminderState = new ReminderState();
    private readonly List<double> calibration = new List<double>();
    private readonly EventWaitHandle showSignal, exitSignal;
    private readonly bool background, pauseAfterUpdate;
    private AudioInput? input;
    private AudioDeviceChanges? deviceChanges;
    private int deviceVersion;
    private int appliedVolume; // Reading NumericUpDown.Value would commit unfinished keyboard input.
    private bool showRequested, initialized, changingStartup, soundFailed, loadingDevices, refreshingTheme;
    private (SystemColorMode Mode, bool HighContrast) lastTheme = WindowTheme.Current;
    private bool calibrating, beforeCalibration, closing, ticking, preferencesDirty, preferencesSaved;
    private double lastFrameTime, calibrationStart, calibrationEnd, saveAt, lastDb = -100;

    private int StartMinute => (int)scheduleStart.Value.TimeOfDay.TotalMinutes;
    private int EndMinute => (int)scheduleEnd.Value.TimeOfDay.TotalMinutes;
    internal bool IsListening => input != null;
    private bool AutomaticTone => playTone.Checked && appliedVolume > 0 && !calibrating;

    private bool ScheduleAllowsListening(DateTime localNow) =>
        !preferences.NeedsRecovery && (!scheduleEnabled.Checked || DailySchedule.ActivePeriodStart(StartMinute, EndMinute, localNow).HasValue);

    private Task ChangeSchedule()
    {
        if (refreshingTheme)
            return Task.CompletedTask;
        QueueSave();
        return ApplySchedule(DateTime.Now);
    }

    internal async Task ApplySchedule(DateTime localNow)
    {
        if (preferences.NeedsRecovery || closing || audioBusy)
            return;
        if (errorPaused)
        {
            UpdateListeningControls(localNow);
            return;
        }
        bool? decision = dailySchedule.Update(scheduleEnabled.Checked, StartMinute, EndMinute, localNow);
        if (decision == true && input == null)
            await ChangeAudio(async () => { await StartListeningCore(true); });
        else if (decision == false && input != null)
            await ChangeAudio(() => StopListeningCore("Outside your scheduled hours. Your microphone is off."));
        UpdateListeningControls(localNow);
    }

    private void UpdateListeningControls(DateTime localNow)
    {
        bool editable = !preferences.NeedsRecovery && !closing && !audioBusy && !pausingAfterError;
        bool allowed = ScheduleAllowsListening(localNow);
        playTone.Enabled = showReminder.Enabled = toneVolume.Enabled = scheduleEnabled.Enabled =
            scheduleStart.Enabled = scheduleEnd.Enabled = runAtStartup.Enabled = editable;
        microphones.Enabled = outputs.Enabled = refresh.Enabled = editable && input == null;
        limit.Enabled = editable && !calibrating;
        learn.Text = calibrating ? "Cancel learning" : "Learn my normal voice";
        start.Enabled = trayToggle.Enabled = learn.Enabled = editable && allowed;
        test.Enabled = trayTest.Enabled = editable && !calibrating;
        if (preferences.NeedsRecovery)
        {
            start.Text = trayToggle.Text = "Recover settings to listen";
            scheduleCaption.Text = "Recover settings before changing the schedule.";
            stateCaption.Text = "SETTINGS RECOVERY · MICROPHONE OFF";
            tray.Text = "Little Quieter — settings need recovery";
            return;
        }
        start.Text = trayToggle.Text = audioBusy ? "Updating audio…" : !allowed ? "Waiting for schedule" : input == null ? "Start listening" : "Stop listening";
        if (!scheduleEnabled.Checked)
            scheduleCaption.Text = "Schedule off";
        else if (StartMinute == EndMinute)
            scheduleCaption.Text = "Choose different start and end times. The microphone stays off.";
        else if (!allowed)
            scheduleCaption.Text = "Starts at " + scheduleStart.Text + " local time. Keep the app running.";
        else
            scheduleCaption.Text = input == null ? "Paused for this period. Start listening to resume." : "Active until " + scheduleEnd.Text + ". Stop pauses this period.";
        if (input == null)
        {
            stateCaption.Text = errorPaused ? "PAUSED AFTER ERROR · MICROPHONE OFF" : allowed ? "PAUSED · MICROPHONE OFF" : "SCHEDULED · MICROPHONE OFF";
            tray.Text = errorPaused ? "Little Quieter — paused after error" : allowed ? "Little Quieter — paused" : "Little Quieter — waiting for schedule";
        }
        else
            tray.Text = "Little Quieter — listening";
    }

    private void UpdateLimit()
    {
        limitCaption.Text = "Loudness limit: " + limit.Value + " / 100";
        meter.Limit = limit.Value;
    }

    private void ConfigureGate() => gate.Configure(Levels.ToDb(limit.Value), AutomaticTone && !soundFailed);

    private void RefreshStartup()
    {
        changingStartup = true;
        try
        {
            string? command = StartupRegistration.ReadCommand();
            runAtStartup.Checked = StartupRegistration.Matches(command, Application.ExecutablePath);
            startupSettings.Text = command != null && !runAtStartup.Checked ? "Startup points to another copy — check Windows settings" : "Windows may override this request — check Startup apps";
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            startupSettings.Text = "Startup status unavailable — check Windows settings";
        }
        finally { changingStartup = false; }
    }

    private void ChangeStartup()
    {
        if (changingStartup || refreshingTheme)
            return;
        try
        {
            StartupRegistration.SetEnabled(runAtStartup.Checked, Application.ExecutablePath);
            footer.Text = runAtStartup.Checked ? "Startup requested for this copy. Check Windows Startup apps if it does not run after sign-in." : "Startup request removed.";
        }
        catch (Exception error) { footer.Text = "Couldn't change startup: " + error.Message; }
        RefreshStartup();
    }

    internal void LoadDevices()
    {
        if (input != null || closing)
            return;
        var selectedInput = microphones.SelectedItem as AudioDevice;
        string? inputId = selectedInput == null ? preferences.DeviceId : selectedInput.Id;
        string inputName = selectedInput?.Name ?? preferences.DeviceName;
        var selectedOutput = outputs.SelectedItem as AudioDevice;
        string? outputId = selectedOutput == null ? preferences.OutputDeviceId : selectedOutput.Id;
        string outputName = selectedOutput?.Name ?? preferences.OutputDeviceName;
        loadingDevices = true;
        try
        {
            var captureDevices = AudioDevice.List(DataFlow.Capture);
            var renderDevices = AudioDevice.List(DataFlow.Render);
            var capture = AudioDevice.Select(captureDevices, inputId, inputName, AudioDevice.DefaultMicrophoneName);
            var render = AudioDevice.Select(renderDevices, outputId, outputName, AudioDevice.DefaultOutputName);
            microphones.Items.Clear();
            outputs.Items.Clear();
            microphones.Items.AddRange(captureDevices.ToArray());
            SelectDevice(microphones, capture);
            outputs.Items.AddRange(renderDevices.ToArray());
            SelectDevice(outputs, render);
            if (capture.Id != inputId || capture.Name != inputName || render.Id != outputId || render.Name != outputName)
                QueueSave();
        }
        catch (Exception error) { footer.Text = "Couldn't list audio devices: " + error.Message; }
        finally { loadingDevices = false; }
    }

    private static void SelectDevice(ComboBox list, AudioDevice device)
    {
        if (!list.Items.Contains(device))
            list.Items.Add(device);
        list.SelectedItem = device;
    }

    private async Task TickMonitor()
    {
        if (ticking || closing)
            return;
        ticking = true;
        try
        {
            await MonitorCore();
        }
        finally { ticking = false; }
    }

    private async Task MonitorCore()
    {
        if (exitSignal.WaitOne(0))
        {
            Close();
            return;
        }
        if (showSignal.WaitOne(0))
            RestoreWindow();
        if (audioBusy)
            return;
        if (!initialized)
        {
            initialized = true;
            if (background && !pauseAfterUpdate && !preferences.IsNew && !preferences.NeedsRecovery && !scheduleEnabled.Checked)
                await ChangeAudio(async () => { await StartListeningCore(true); });
        }
        await ApplySchedule(DateTime.Now);
        if (closing)
            return;
        double now = AudioClock.Now;
        bool previewing = gate.IsPreviewing(now);
        if (!previewing)
            FinishPreviewStatus(input == null ? "Test finished. Your microphone is off." : "Test finished. Listening.");
        timer.Interval = Visible || calibrating || gate.Active || reminder.Visible || previewing ? 100 : 250;
        if (saveAt > 0 && now >= saveAt)
            SavePreferences(false);
        if (alarm.Fault != null && !soundFailed)
        {
            soundFailed = true;
            gate.CancelPreview();
            ConfigureGate();
            footer.Text = "Tone output stopped. Check the output, then use Test tone to retry.";
        }
        if (input == null)
        {
            if (!previewing && alarm.HasOutput)
                await ChangeAudio(ReleaseOutput);
            UpdateReminder(false, gate.IsPreviewing(AudioClock.Now));
            return;
        }
        if (deviceChanges == null || deviceChanges.Version != deviceVersion)
        {
            bool resume = !calibrating || beforeCalibration;
            await ChangeAudio(async () =>
            {
                await StopListeningCore("Audio device changed.");
                LoadDevices();
                if (resume && !closing)
                    await StartListeningCore(true);
            });
            return;
        }
        if (input.Fault != null)
        {
            string error = input.Fault.Message;
            await ChangeAudio(() => StopListeningCore("Microphone stopped. Reconnect it and Refresh. " + error));
            return;
        }
        if (!AutomaticTone && !previewing && alarm.HasOutput)
            await ChangeAudio(ReleaseOutput);
        if (closing || input == null)
            return;
        bool changed = ReadInput(input, now, out bool clipped);
        if (now - lastFrameTime > 3)
        {
            await ChangeAudio(() => StopListeningCore("No audio is arriving. Check the microphone and restart listening."));
            return;
        }
        if (calibrating && now >= calibrationEnd)
        {
            await CompleteCalibration();
            if (input == null || closing)
                return;
        }
        UpdateReminder(gate.Active, gate.IsPreviewing(AudioClock.Now));
        if (Visible)
            UpdateLevelDisplay(now, gate.Active, changed, clipped);
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

    private void UpdateLevelDisplay(double now, bool active, bool changed, bool clipped)
    {
        if (changed)
        {
            meter.SetLevel(Levels.ToMeter(lastDb));
            levelCaption.Text = meter.Value.ToString();
        }
        if (calibrating)
        {
            stateCaption.Text = now < calibrationStart ? "LEARNING · GET READY" : "LEARNING · " + Math.Ceiling(calibrationEnd - now) + " SECONDS LEFT";
            liveCaption.Text = "Talk at a comfortable indoor volume.";
        }
        else
        {
            stateCaption.Text = active ? "ABOVE YOUR LIMIT · QUIETEN DOWN" : "LISTENING · WITHIN YOUR LIMIT";
            liveCaption.Text = clipped ? "Input is clipping. Lower mic gain or move it back." : meter.Value < 3 ? "Very little input. Check your mic if you're speaking." : (!playTone.Checked || appliedVolume == 0) && !showReminder.Checked ? "Meter only · alerts are off" : "Limit " + limit.Value;
        }
    }

    private void UpdateReminder(bool active, bool previewing)
    {
        if (!showReminder.Checked)
            reminderState.Reset();
        if (showReminder.Checked && reminderState.Update(active, previewing, AudioClock.Now))
        {
            if (active || previewing)
                reminder.Remind(this, previewing ? "Test tone" : "Keep your voice down, please.");
        }
        else
            reminder.Hide();
    }

    private void DismissReminder()
    {
        reminderState.Dismiss();
        reminder.Hide();
    }

    private void RestoreWindow()
    {
        showRequested = true;
        Show();
        WindowState = restoredWindowState;
        FitWindow(false);
        Activate();
        RefreshStartup();
    }

    private void QueueSave()
    {
        preferencesDirty = true;
        saveAt = AudioClock.Now + 0.8;
    }

    private void RecoverPreferences(Control notice)
    {
        try
        {
            string backup = preferences.Recover(settingsPath);
            preferencesSaved = initialized = true;
            notice.Hide();
            footer.Text = "Original settings backed up to " + backup + ". Choose your microphone before starting.";
            UpdateListeningControls(DateTime.Now);
            FitWindow(true);
        }
        catch (Exception error) { MessageBox.Show(this, "Your original settings were kept. " + error.Message, "Settings recovery"); }
    }

    private void SavePreferences(bool report)
    {
        saveAt = 0;
        if (preferences.NeedsRecovery || !preferencesDirty && preferencesSaved)
            return;
        preferences.Limit = limit.Value;
        preferences.PlayTone = playTone.Checked;
        preferences.ToneVolume = appliedVolume;
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
            preferencesSaved = true;
            preferencesDirty = false;
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

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x001A && !closing && Application.ColorMode == SystemColorMode.System) // WM_SETTINGCHANGE
            BeginInvoke(RefreshSystemTheme);
    }

    private void RefreshSystemTheme()
    {
        if (!closing && Application.ColorMode == SystemColorMode.System)
        {
            Application.SetColorMode(SystemColorMode.System);
            ApplyTheme();
        }
    }

    private void ApplyTheme()
    {
        if (lastTheme != WindowTheme.Current)
        {
            refreshingTheme = true;
            try
            {
                WindowTheme.RefreshControls(this);
                WindowTheme.RefreshControls(reminder);
                lastTheme = WindowTheme.Current;
            }
            finally { refreshingTheme = false; }
        }
        WindowTheme.ApplyTitle(this);
        WindowTheme.ApplyTitle(reminder);
    }
}
