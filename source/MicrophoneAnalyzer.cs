using System;
using System.Buffers.Binary;
using NAudio.Wave;

namespace LittleQuieter
{
    public sealed class MicrophoneAnalyzer
    {
        private readonly WaveFormat format;
        private readonly bool floating;
        private readonly int bytesPerSample, windowFrames;
        private readonly AlarmRejectionFilter[] filters;
        private readonly double[] previousInput, previousOutput;
        private readonly double dcPole;
        private int count;
        private double squares;
        private bool clipped;
        public event Action<SoundFrame> LevelAvailable;
        public double WindowMilliseconds => windowFrames * 1000.0 / format.SampleRate;

        public MicrophoneAnalyzer(WaveFormat nativeFormat)
        {
            format = nativeFormat;
            floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            bool pcm = format.Encoding == WaveFormatEncoding.Pcm ||
                format is WaveFormatExtensible pcmExt && pcmExt.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71");
            if (!(floating && format.BitsPerSample == 32) && !(pcm && (format.BitsPerSample == 16 || format.BitsPerSample == 24 || format.BitsPerSample == 32)))
                throw new NotSupportedException("Unsupported microphone mix format: " + format);
            bytesPerSample = format.BitsPerSample / 8;
            windowFrames = Math.Max(1, format.SampleRate / 200); // 5 ms, independent of device packet size.
            filters = new AlarmRejectionFilter[format.Channels];
            previousInput = new double[format.Channels];
            previousOutput = new double[format.Channels];
            dcPole = Math.Exp(-2 * Math.PI * 20 / format.SampleRate);
            for (int channel = 0; channel < filters.Length; channel++)
                filters[channel] = new AlarmRejectionFilter(format.SampleRate);
        }
        public void Reset()
        {
            count = 0;
            squares = 0;
            clipped = false;
            Array.Clear(previousInput);
            Array.Clear(previousOutput);
            foreach (var filter in filters)
                filter.Reset();
        }
        public void Process(ReadOnlySpan<byte> buffer, double now)
        {
            for (int offset = 0; offset + format.BlockAlign <= buffer.Length; offset += format.BlockAlign)
            {
                for (int channel = 0; channel < format.Channels; channel++)
                {
                    var data = buffer.Slice(offset + channel * bytesPerSample, bytesPerSample);
                    double sample;
                    if (floating)
                        sample = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
                    else if (bytesPerSample == 2)
                        sample = BinaryPrimitives.ReadInt16LittleEndian(data) / 32768.0;
                    else if (bytesPerSample == 3)
                        sample = ((data[0] << 8) | (data[1] << 16) | (data[2] << 24)) / 2147483648.0;
                    else
                        sample = BinaryPrimitives.ReadInt32LittleEndian(data) / 2147483648.0;
                    if (!double.IsFinite(sample))
                        sample = 0;
                    clipped |= Math.Abs(sample) > 0.992;
                    double filtered = filters[channel].Transform((float)sample);
                    // A DC blocker preserves low voice frequencies in a short window;
                    // subtracting each 5 ms window's mean would remove much of a low voice.
                    double ac = filtered - previousInput[channel] + dcPole * previousOutput[channel];
                    previousInput[channel] = filtered;
                    previousOutput[channel] = ac;
                    squares += ac * ac;
                }
                if (++count < windowFrames)
                    continue;
                double db = Math.Max(-100, 10 * Math.Log10(Math.Max(1e-10, squares / (count * format.Channels))));
                LevelAvailable?.Invoke(new SoundFrame(db, count / (double)format.SampleRate, now, clipped));
                count = 0;
                squares = 0;
                clipped = false;
            }
        }
    }
}
