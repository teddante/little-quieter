using System;

namespace LittleQuieter
{
    // Audio-thread decision path. The callback only changes the prepared tone's
    // atomic gate: it never opens a device, blocks on playback, or touches UI.
    public sealed class AlarmGate
    {
        private readonly object sync = new object();
        private readonly Action<bool> setOutput;
        private double threshold = -28, lastTime, previewUntil;
        private bool enabled = true, suspended, active, outputActive;
        public AlarmGate(Action<bool> output) { setOutput = output; }
        public bool Active { get { lock (sync) return active; } }
        public void Configure(double thresholdDb, bool soundEnabled)
        {
            lock (sync) { threshold = thresholdDb; enabled = soundEnabled; Apply(AudioInput.Now); }
        }
        public void Suspend(bool value)
        {
            lock (sync) { suspended = value; active = false; previewUntil = 0; Apply(AudioInput.Now); }
        }
        public void Update(SoundFrame frame)
        {
            lock (sync)
            {
                lastTime = frame.Time;
                active = !suspended && double.IsFinite(frame.Db) && frame.Db >= threshold;
                Apply(frame.Time);
            }
        }
        public void Preview(double now)
        {
            lock (sync) { previewUntil = now + 2; Apply(now); }
        }
        public void Refresh(double now)
        {
            lock (sync)
            {
                if (now - lastTime > 0.1) active = false;
                Apply(now);
            }
        }
        public void Reset()
        {
            lock (sync) { active = false; lastTime = previewUntil = 0; SetOutput(false); }
        }
        private void Apply(double now) { SetOutput(enabled && !suspended && (active || previewUntil > now)); }
        private void SetOutput(bool value)
        {
            if (value == outputActive) return;
            outputActive = value;
            setOutput(value);
        }
    }
}
