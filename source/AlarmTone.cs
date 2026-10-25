using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LittleQuieter;

internal sealed class AlarmTone : IDisposable
{
    private readonly Func<IWaveProvider, IWaveProvider>? measurementDecorator;
    public AlarmTone(Func<IWaveProvider, IWaveProvider>? decorator = null)
    {
        measurementDecorator = decorator;
    }
    private WasapiPlayer? player;
    private MMDevice? endpoint;
    private SteadyToneProvider? source;
    internal WasapiPlayer Player => player ?? throw new InvalidOperationException("The alarm output is not prepared.");
    private volatile Exception? fault;
    public Exception? Fault => fault;
    public const int FrequencyHz = 2500;
    public bool IsReady => player?.PlaybackState == PlaybackState.Playing;
    public bool IsActive => source?.Active == true;
    public bool LowLatency => player?.LowLatencyActive == true;
    public int EngineMilliseconds => player?.LatencyMilliseconds ?? 0;
    public WaveFormat? Format => source?.WaveFormat;
    public void Start(int volume, string? deviceId = null)
    {
        Dispose();
        fault = null;
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
                source = prepared;
                player = output;
                output.PlaybackStopped += (_, e) =>
                {
                    prepared.SetActive(false);
                    if (ReferenceEquals(player, output))
                        fault = e.Exception ?? new InvalidOperationException("The alarm output stream stopped.");
                };
                output.Init(measurementDecorator == null ? prepared : measurementDecorator(prepared));
                output.Play(); // Keep the engine ready; the provider emits silence until gated on.
            }).GetAwaiter().GetResult();
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    public void SetActive(bool value)
    {
        source?.SetActive(value);
    }
    public void SetVolume(int volume)
    {
        source?.SetVolume(volume);
    }
    public void Dispose()
    {
        source?.SetActive(false);
        var output = player;
        var device = endpoint;
        player = null; // Disposal is expected; its stopped event must not become a new fault.
        endpoint = null;
        source = null;
        try
        {
            output?.Dispose();
        }
        finally
        {
            device?.Dispose();
        }
    }
}
