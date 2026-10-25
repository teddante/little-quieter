using System.Diagnostics.CodeAnalysis;
using NAudio.Dsp;

namespace LittleQuieter;

// Remove the alarm's narrow frequency band before measuring the mic, so a speaker
// echo cannot normally hold the alarm on. Used during calibration as well.
internal sealed class AlarmRejectionFilter
{
    private BiQuadFilter first;
    private BiQuadFilter second;
    private readonly int rate;
    public AlarmRejectionFilter(int sampleRate = 48000)
    {
        rate = sampleRate;
        Reset();
    }
    [MemberNotNull(nameof(first), nameof(second))]
    public void Reset()
    {
        first = BiQuadFilter.NotchFilter(rate, AlarmTone.FrequencyHz, 8);
        second = BiQuadFilter.NotchFilter(rate, AlarmTone.FrequencyHz, 8);
    }
    public float Transform(float sample)
    {
        return second.Transform(first.Transform(sample));
    }
}
