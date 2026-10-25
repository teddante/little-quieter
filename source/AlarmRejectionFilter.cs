using NAudio.Dsp;

namespace LittleQuieter;

// Remove the alarm's narrow frequency band before measuring the mic, so a speaker
// echo cannot normally hold the alarm on. Used during calibration as well.
internal sealed class AlarmRejectionFilter
{
    private readonly BiQuadFilter first, second;
    public AlarmRejectionFilter(int sampleRate = 48000)
    {
        first = BiQuadFilter.NotchFilter(sampleRate, AlarmTone.FrequencyHz, 8);
        second = BiQuadFilter.NotchFilter(sampleRate, AlarmTone.FrequencyHz, 8);
    }
    public void Reset()
    {
        first.ResetState();
        second.ResetState();
    }
    public float Transform(float sample)
    {
        return second.Transform(first.Transform(sample));
    }
}
