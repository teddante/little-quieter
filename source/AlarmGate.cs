namespace LittleQuieter;

// Audio-thread decision path. The callback only changes the prepared tone's
// atomic gate: it never opens a device, blocks on playback, or touches UI.
internal sealed class AlarmGate
{
    public const double PreviewSeconds = 2;
    public const int WatchdogMilliseconds = 20;
    private const double StaleInputSeconds = 0.1;
    private readonly object sync = new object();
    private readonly Action<bool> setOutput;
    private double threshold = Levels.ToDb(Levels.DefaultLimit), lastTime, previewUntil;
    private bool enabled = true, suspended, active, outputActive;
    public AlarmGate(Action<bool> output)
    {
        setOutput = output;
    }
    public bool Active
    {
        get
        {
            lock (sync)
                return active;
        }
    }
    public void Configure(double thresholdDb, bool soundEnabled)
    {
        lock (sync)
        {
            threshold = thresholdDb;
            enabled = soundEnabled;
            Apply(AudioClock.Now);
        }
    }
    public void Suspend(bool value)
    {
        lock (sync)
        {
            suspended = value;
            active = false;
            previewUntil = 0;
            Apply(AudioClock.Now);
        }
    }
    public void Update(SoundFrame frame)
    {
        lock (sync)
        {
            bool valid = double.IsFinite(frame.Db) && double.IsFinite(frame.Time);
            lastTime = valid ? frame.Time : 0;
            active = !suspended && valid && frame.Db >= threshold;
            Apply(frame.Time);
        }
    }
    public void Preview(double now)
    {
        lock (sync)
        {
            previewUntil = now + PreviewSeconds;
            Apply(now);
        }
    }
    public bool IsPreviewing(double now)
    {
        lock (sync)
            return !suspended && previewUntil > now;
    }
    public void Refresh(double now)
    {
        lock (sync)
        {
            if (now - lastTime > StaleInputSeconds)
                active = false;
            Apply(now);
        }
    }
    public void Reset()
    {
        lock (sync)
        {
            active = false;
            lastTime = previewUntil = 0;
            SetOutput(false);
        }
    }
    private void Apply(double now)
    {
        SetOutput(enabled && !suspended && (active || previewUntil > now));
    }
    private void SetOutput(bool value)
    {
        if (value == outputActive)
            return;
        outputActive = value;
        setOutput(value);
    }
}
