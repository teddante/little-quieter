namespace LittleQuieter;

internal sealed partial class MainWindow
{
    private bool audioBusy, pausingAfterError, errorPaused;
    private int audioGeneration;
    private Task audioOperation = Task.CompletedTask;
    internal Task AudioIdle => audioOperation;

    // Only one transition owns the devices. Continuations return to the UI thread;
    // opening, closing and COM cleanup run on worker threads.
    private Task ChangeAudio(Func<Task> change)
    {
        if (closing || audioBusy || pausingAfterError)
            return audioOperation;
        audioBusy = true;
        UpdateListeningControls(DateTime.Now);
        return audioOperation = ExecuteAudioChange(change);
    }

    private async Task ExecuteAudioChange(Func<Task> change)
    {
        try
        {
            await change();
        }
        finally
        {
            audioBusy = false;
            if (!closing)
                UpdateListeningControls(DateTime.Now);
        }
    }

    private async Task<bool> PrepareOutput()
    {
        soundFailed = false;
        if (outputs.SelectedItem is not AudioDevice { Unavailable: false } device)
        {
            soundFailed = true;
            footer.Text = "Tone unavailable. Select an available output, or reconnect it and Refresh.";
            ConfigureGate();
            return false;
        }
        int volume = (int)toneVolume.Value;
        int generation = audioGeneration;
        gate.Suspend(true);
        try
        {
            await Task.Run(() => alarm.Start(volume, device.Id));
        }
        catch (Exception error)
        {
            soundFailed = true;
            if (!closing)
                footer.Text = "Tone unavailable. Check the output, then use Test tone. " + error.Message;
        }
        if (closing || generation != audioGeneration)
        {
            gate.Suspend(true);
            alarm.SetActive(false);
            return false;
        }
        ConfigureGate();
        if (!closing && input != null && !calibrating)
            gate.Suspend(false);
        return !soundFailed && !closing;
    }

    private async Task SynchronizeOutput()
    {
        bool needed = input != null && AutomaticTone || gate.IsPreviewing(AudioClock.Now);
        if (deviceChanges != null)
            deviceChanges.FollowDefaultOutput = needed && (outputs.SelectedItem as AudioDevice)?.Id == null;
        if (needed && (!alarm.IsReady || soundFailed))
            await PrepareOutput();
        else if (!needed && alarm.HasOutput)
            await ReleaseOutput();
    }

    private async Task ReleaseOutput()
    {
        alarm.SetActive(false);
        if (deviceChanges != null)
            deviceChanges.FollowDefaultOutput = false;
        if (alarm.HasOutput)
            await Task.Run(alarm.Dispose);
        if (input == null && !closing)
            watchdog.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async Task<bool> StartListeningCore(bool silent = false, bool startSuspended = false)
    {
        if (!ScheduleAllowsListening(DateTime.Now) || closing)
            return false;
        if (input != null)
            return true;
        if (microphones.SelectedItem is not AudioDevice { Unavailable: false } device)
        {
            footer.Text = "Your selected microphone is unavailable. Reconnect it and Refresh, or choose another microphone.";
            return false;
        }
        int generation = audioGeneration;
        bool soundWanted = AutomaticTone && !startSuspended;
        bool defaultOutput = soundWanted && (outputs.SelectedItem as AudioDevice)?.Id == null;
        gate.Suspend(true);
        var candidate = new AudioInput();
        AudioDeviceChanges? changes = null;
        candidate.LevelAvailable += gate.Update;
        candidate.StreamStopped += gate.Reset;
        bool published = false;
        try
        {
            await Task.Run(() =>
            {
                changes = new AudioDeviceChanges(() => gate.Suspend(true), device.Id == null, defaultOutput);
                candidate.Start(device.Id);
            });
            if (closing || generation != audioGeneration || !ScheduleAllowsListening(DateTime.Now))
                return false;
            if (soundWanted)
            {
                if (!alarm.IsReady || soundFailed)
                    await PrepareOutput();
            }
            else
                await ReleaseOutput();
            if (closing || generation != audioGeneration || !ScheduleAllowsListening(DateTime.Now))
                return false;
            int openedDeviceVersion = changes!.Version;
            if (openedDeviceVersion != 0)
            {
                footer.Text = "An audio device changed while opening. Try starting again.";
                return false;
            }
            input = candidate;
            deviceChanges = changes;
            deviceVersion = openedDeviceVersion;
            published = true;
            lastFrameTime = AudioClock.Now;
            ConfigureGate();
            gate.Suspend(startSuspended);
            StartWatchdog();
            if (!soundWanted || !soundFailed)
                footer.Text = "Listening. Keep your microphone position steady.";
            return true;
        }
        catch (Exception error)
        {
            if (!closing)
            {
                footer.Text = "Couldn't open the microphone. Check its connection and Windows microphone access.";
                if (silent)
                {
                    tray.BalloonTipTitle = "Little Quieter: microphone unavailable";
                    tray.BalloonTipText = "Open Little Quieter to choose a microphone.";
                    tray.ShowBalloonTip(7000);
                }
                else
                    MessageBox.Show(this, error.Message, "Microphone unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return false;
        }
        finally
        {
            if (!published)
            {
                gate.Suspend(true);
                await Task.Run(() => DisposeAudio(candidate, changes));
            }
        }
    }

    internal Task ToggleListening() => ChangeAudio(async () =>
    {
        initialized = true; // A manual action takes precedence over deferred background startup.
        if (input == null)
        {
            errorPaused = false;
            dailySchedule.Update(scheduleEnabled.Checked, StartMinute, EndMinute, DateTime.Now);
            await StartListeningCore();
        }
        else
            await StopListeningCore(scheduleEnabled.Checked ? "Paused for this period. Start listening to resume." : "Paused. Your microphone is off.");
    });

    private void StartWatchdog() => watchdog.Change(AlarmGate.WatchdogMilliseconds, AlarmGate.WatchdogMilliseconds);

    private async Task StopListeningCore(string text)
    {
        gate.Suspend(true);
        var capture = input;
        var changes = deviceChanges;
        input = null;
        deviceChanges = null;
        calibrating = false;
        watchdog.Change(Timeout.Infinite, Timeout.Infinite);
        reminderState.Reset();
        reminder.Hide();
        meter.SetLevel(0);
        levelCaption.Text = "—";
        liveCaption.Text = "Start listening to see your microphone level.";
        lastDb = -100;
        footer.Text = text;
        if (capture != null || changes != null || alarm.HasOutput)
            await Task.Run(() => DisposeAudio(capture, changes));
    }

    private void DisposeAudio(AudioInput? capture, AudioDeviceChanges? changes)
    {
        try
        {
            changes?.Dispose();
        }
        finally
        {
            try
            {
                capture?.Dispose();
            }
            finally { alarm.Dispose(); }
        }
    }

    private Task ToggleCalibration() => ChangeAudio(async () =>
    {
        initialized = true;
        if (calibrating)
        {
            await FinishCalibration("Learning cancelled. Your previous limit is unchanged.");
            return;
        }
        beforeCalibration = input != null;
        errorPaused = false;
        dailySchedule.Update(scheduleEnabled.Checked, StartMinute, EndMinute, DateTime.Now);
        gate.Suspend(true);
        if (!beforeCalibration && !await StartListeningCore(startSuspended: true))
            return;
        calibrating = true;
        await ReleaseOutput();
        if (closing)
            return;
        reminderState.Reset();
        reminder.Hide();
        calibration.Clear();
        calibrationStart = AudioClock.Now + 2;
        calibrationEnd = calibrationStart + 8;
        footer.Text = "Get ready, then talk at a comfortable indoor volume for 8 seconds.";
    });

    private Task CompleteCalibration() => ChangeAudio(async () =>
    {
        string message;
        if (Levels.TryCalibrate(calibration, out int learned))
        {
            limit.Value = learned;
            message = "Learned your normal voice. Adjust the limit if needed.";
        }
        else
            message = "Couldn't learn a clear voice level. Try again or set the limit manually.";
        await FinishCalibration(message);
    });

    private async Task FinishCalibration(string message)
    {
        int generation = audioGeneration;
        if (!beforeCalibration)
            await StopListeningCore(message);
        else
        {
            calibrating = false;
            await SynchronizeOutput();
            if (!closing && generation == audioGeneration)
            {
                gate.Suspend(false);
                footer.Text = message;
            }
        }
    }

    private Task PreviewAlarm() => ChangeAudio(async () =>
    {
        if (calibrating || preferences.NeedsRecovery)
            return;
        int generation = audioGeneration;
        if ((!alarm.IsReady || soundFailed) && !await PrepareOutput())
            return;
        if (closing || generation != audioGeneration)
            return;
        gate.Suspend(false);
        ConfigureGate();
        gate.Preview(AudioClock.Now);
        if (deviceChanges != null)
            deviceChanges.FollowDefaultOutput = (outputs.SelectedItem as AudioDevice)?.Id == null;
        StartWatchdog();
        footer.Text = toneVolume.Value == 0 ? "Test is silent: alarm volume is 0%." : "Testing the selected output for two seconds.";
        UpdateReminder(gate.Active, true);
    });

    private Task ChangeOutput()
    {
        if (loadingDevices || refreshingTheme)
            return Task.CompletedTask;
        QueueSave();
        return ChangeAudio(async () =>
        {
            gate.CancelPreview();
            reminderState.Reset();
            reminder.Hide();
            if (input == null)
                await ReleaseOutput();
        });
    }

    private Task ChangeToneSettings()
    {
        if (refreshingTheme)
            return Task.CompletedTask;
        QueueSave();
        alarm.SetVolume((int)toneVolume.Value);
        if (!AutomaticTone && !gate.IsPreviewing(AudioClock.Now))
        {
            if (soundFailed)
                footer.Text = "Automatic tone is off.";
            soundFailed = false;
        }
        ConfigureGate();
        bool needed = input != null && AutomaticTone || gate.IsPreviewing(AudioClock.Now);
        return (needed ? !alarm.IsReady || soundFailed : alarm.HasOutput)
            ? ChangeAudio(SynchronizeOutput) : Task.CompletedTask;
    }

    internal async Task PauseAfterError()
    {
        pausingAfterError = errorPaused = true;
        audioGeneration++;
        gate.Suspend(true);
        alarm.SetActive(false);
        reminderState.Reset();
        reminder.Hide();
        initialized = true;
        dailySchedule.Update(scheduleEnabled.Checked, StartMinute, EndMinute, DateTime.Now);
        try
        {
            try
            {
                await audioOperation;
            }
            catch { } // Devices are detached and retried for disposal below.
            if (!closing)
            {
                audioBusy = true;
                await (audioOperation = ExecuteAudioChange(() => StopListeningCore("Paused after an error. Restart listening when ready.")));
            }
        }
        finally
        {
            pausingAfterError = false;
            if (!closing)
                UpdateListeningControls(DateTime.Now);
        }
    }

    private void Shutdown(bool report)
    {
        if (closing)
            return;
        closing = true;
        audioGeneration++;
        timer.Stop();
        gate.Suspend(true);
        alarm.SetActive(false);
        watchdog.Dispose();
        var capture = input;
        var changes = deviceChanges;
        input = null;
        deviceChanges = null;
        Task pending = audioOperation;
        audioOperation = Task.Run(async () =>
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch { }
            DisposeAudio(capture, changes);
        });
        reminder.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        SavePreferences(report);
        timer.Dispose();
        Enabled = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Shutdown(false);
        base.Dispose(disposing);
    }
}
