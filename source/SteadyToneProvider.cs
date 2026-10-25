using System.Runtime.InteropServices;
using NAudio.Wave;

namespace LittleQuieter;

internal sealed class SteadyToneProvider : IWaveProvider
{
    public WaveFormat WaveFormat
    {
        get;
    }
    private readonly double phaseStep, gainStep;
    private double phase, currentGain;
    private volatile float gain;
    private volatile bool active;
    public bool Active => active;
    public SteadyToneProvider(int volume, int rate = 48000, int channels = 1)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        phaseStep = 2 * Math.PI * AlarmTone.FrequencyHz / rate;
        gainStep = 0.85 / Math.Max(1, rate * 0.0005); // Half-millisecond click suppression.
        SetVolume(volume);
    }
    public void SetVolume(int volume)
    {
        gain = 0.85f * Math.Clamp(volume, 0, 100) / 100f;
    }
    public void SetActive(bool value)
    {
        active = value;
    }
    public int Read(Span<byte> buffer)
    {
        int bytes = buffer.Length - buffer.Length % WaveFormat.BlockAlign;
        if (currentGain == 0 && (!active || gain == 0))
        {
            buffer.Slice(0, bytes).Clear();
            return bytes;
        }
        var samples = MemoryMarshal.Cast<byte, float>(buffer.Slice(0, bytes));
        for (int frame = 0; frame < samples.Length; frame += WaveFormat.Channels)
        {
            double target = active ? gain : 0;
            currentGain += Math.Clamp(target - currentGain, -gainStep, gainStep);
            float value = (float)(currentGain * Math.Sin(phase));
            for (int channel = 0; channel < WaveFormat.Channels; channel++)
                samples[frame + channel] = value;
            phase += phaseStep;
            if (phase >= 2 * Math.PI)
                phase -= 2 * Math.PI;
        }
        return bytes;
    }
}
