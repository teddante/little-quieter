using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LittleQuieter
{
    public static class StartupRegistration
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "LittleQuieter";
        public static string Command(string executable) { return "\"" + executable + "\" --background"; }
        public static bool Enabled
        {
            get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name) is string; }
        }
        public static void SetEnabled(bool enabled, string executable)
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            if (enabled) key.SetValue(Name, Command(executable), RegistryValueKind.String);
            else key.DeleteValue(Name, false);
        }
    }

    public sealed class AudioDeviceChanges : IDisposable
    {
        private readonly MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
        private readonly MMDeviceNotificationClient notifications;
        private int version;
        public int Version { get { return Volatile.Read(ref version); } }
        public AudioDeviceChanges(Action silence)
        {
            notifications = enumerator.CreateNotificationClient(false);
            notifications.DefaultDeviceChanged += (_, e) =>
            {
                if (e.Role != Role.Console) return;
                silence();
                Interlocked.Increment(ref version);
            };
        }
        public void Dispose() { notifications.Dispose(); enumerator.Dispose(); }
    }

    public sealed class SteadyToneProvider : IWaveProvider
    {
        public WaveFormat WaveFormat { get; }
        private readonly double phaseStep, gainStep;
        private double phase, currentGain;
        private volatile float gain;
        private volatile bool active;
        public bool Active { get { return active; } }
        public SteadyToneProvider(int volume, int rate = 48000, int channels = 1)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
            phaseStep = 2 * Math.PI * AlarmTone.FrequencyHz / rate;
            gainStep = 0.85 / Math.Max(1, rate * 0.0005); // Half-millisecond click suppression.
            SetVolume(volume);
        }
        public void SetVolume(int volume) { gain = 0.85f * Math.Clamp(volume, 0, 100) / 100f; }
        public void SetActive(bool value) { active = value; }
        public int Read(Span<byte> buffer)
        {
            int bytes = buffer.Length - buffer.Length % WaveFormat.BlockAlign;
            if (!active && currentGain == 0) { buffer.Slice(0, bytes).Clear(); return bytes; }
            var samples = MemoryMarshal.Cast<byte, float>(buffer.Slice(0, bytes));
            for (int frame = 0; frame < samples.Length; frame += WaveFormat.Channels)
            {
                double target = active ? gain : 0;
                currentGain += Math.Clamp(target - currentGain, -gainStep, gainStep);
                float value = (float)(currentGain * Math.Sin(phase));
                for (int channel = 0; channel < WaveFormat.Channels; channel++) samples[frame + channel] = value;
                phase += phaseStep;
                if (phase >= 2 * Math.PI) phase -= 2 * Math.PI;
            }
            return bytes;
        }
    }

    public sealed class AlarmTone : IDisposable
    {
        private readonly Func<IWaveProvider, IWaveProvider> measurementDecorator;
        public AlarmTone() { }
        internal AlarmTone(Func<IWaveProvider, IWaveProvider> decorator) { measurementDecorator = decorator; }
        private WasapiPlayer player;
        private MMDevice endpoint;
        private SteadyToneProvider source;
        private volatile Exception fault;
        public Exception Fault { get { return fault; } }
        public const int FrequencyHz = 2500;
        public bool IsReady { get { return player != null && player.PlaybackState == PlaybackState.Playing; } }
        public bool IsActive { get { return source != null && source.Active; } }
        public bool LowLatency { get { return player != null && player.LowLatencyActive; } }
        public int EngineMilliseconds { get { return player == null ? 0 : player.LatencyMilliseconds; } }
        public WaveFormat Format { get { return source?.WaveFormat; } }
        public void Start(int volume, string deviceId = null)
        {
            Dispose(); fault = null;
            try
            {
                Task.Run(() =>
                {
                    using var enumerator = new MMDeviceEnumerator();
                    endpoint = deviceId == null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console) : enumerator.GetDevice(deviceId);
                    using var client = endpoint.CreateAudioClient();
                    var mix = client.MixFormat;
                    var prepared = new SteadyToneProvider(volume, mix.SampleRate, mix.Channels);
                    var output = new WasapiPlayerBuilder().WithDevice(endpoint).WithSharedMode().WithEventSync()
                        .WithLatency(10).WithLowLatency().WithMmcssThreadPriority("Audio").Build();
                    source = prepared; player = output;
                    output.PlaybackStopped += (_, e) =>
                    {
                        prepared.SetActive(false);
                        if (ReferenceEquals(player, output) && e.Exception != null) fault = e.Exception;
                    };
                    output.Init(measurementDecorator == null ? prepared : measurementDecorator(prepared));
                    output.Play(); // Keep the engine ready; the provider emits silence until gated on.
                }).GetAwaiter().GetResult();
            }
            catch { Dispose(); throw; }
        }
        public void SetActive(bool value) { source?.SetActive(value); }
        public void SetVolume(int volume) { source?.SetVolume(volume); }
        public void Dispose()
        {
            source?.SetActive(false);
            if (player != null) { player.Dispose(); player = null; }
            endpoint?.Dispose(); endpoint = null; source = null;
        }
    }
}
