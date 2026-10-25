using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LittleQuieter;

internal sealed class AudioDevice
{
    public const string DefaultMicrophoneName = "Windows default microphone";
    public const string DefaultOutputName = "Windows default output";
    public string? Id;
    public required string Name;
    public override string ToString()
    {
        return Name;
    }
    public static List<AudioDevice> List(DataFlow flow)
    {
        var result = new List<AudioDevice> { new AudioDevice { Name = flow == DataFlow.Capture ? DefaultMicrophoneName : DefaultOutputName } };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            using (device)
                result.Add(new AudioDevice { Id = device.ID, Name = device.FriendlyName });
        return result;
    }
}
internal readonly record struct SoundFrame(double Db, double Time, bool Clipped);

internal sealed class AudioInput : IDisposable
{
    private const int WindowsPerSummary = 10;
    private const int MaximumQueuedSummaries = 20;
    private readonly ConcurrentQueue<SoundFrame> frames = new ConcurrentQueue<SoundFrame>();
    private WasapiRecorder? recorder;
    private MMDevice? endpoint;
    private MicrophoneAnalyzer? analyzer;
    private volatile Exception? fault;
    private volatile bool disposed;
    private int displayWindows;
    private bool displayClipped;
    public event Action<SoundFrame>? LevelAvailable;
    public event Action? StreamStopped;
    public Exception? Fault => fault;
    public bool LowLatency => recorder != null && recorder.LowLatencyActive;
    public int EngineMilliseconds => recorder == null ? 0 : recorder.LatencyMilliseconds;
    public WaveFormat? Format => recorder?.WaveFormat;
    public double AnalysisMilliseconds => analyzer == null ? 0 : analyzer.WindowMilliseconds;
    public bool TryRead(out SoundFrame frame)
    {
        return frames.TryDequeue(out frame);
    }
    public void Start(string? deviceId)
    {
        if (recorder != null || disposed)
            throw new InvalidOperationException("This input has already been used.");
        try
        {
            Task.Run(() =>
            {
                using var enumerator = new MMDeviceEnumerator();
                endpoint = deviceId == null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console) : enumerator.GetDevice(deviceId);
                var capture = new WasapiRecorderBuilder().WithDevice(endpoint).WithSharedMode().WithEventSync()
                    .WithBufferLength(10).WithLowLatency().WithMmcssThreadPriority("Audio").Build();
                recorder = capture;
                var analysis = new MicrophoneAnalyzer(capture.WaveFormat);
                analyzer = analysis;
                analysis.LevelAvailable += frame =>
                {
                    LevelAvailable?.Invoke(frame); // Direct gate control precedes optional UI summaries.
                    displayClipped |= frame.Clipped;
                    if (++displayWindows < WindowsPerSummary)
                        return; // 20 summaries/second, not 200 gate decisions.
                    frames.Enqueue(frame with
                    {
                        Clipped = displayClipped
                    });
                    displayWindows = 0;
                    displayClipped = false;
                    while (frames.Count > MaximumQueuedSummaries)
                        frames.TryDequeue(out _);
                };
                capture.DataAvailable += (buffer, flags, _, _) =>
                {
                    if (disposed)
                        return;
                    if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0)
                        analysis.Reset();
                    analysis.Process(buffer, AudioClock.Now);
                };
                capture.RecordingStopped += (_, e) =>
                {
                    if (!disposed)
                        fault = e.Exception ?? new InvalidOperationException("The microphone stream stopped.");
                    StreamStopped?.Invoke();
                };
                capture.StartRecording();
            }).GetAwaiter().GetResult();
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            recorder?.Dispose();
        }
        finally
        {
            recorder = null;
            var device = endpoint;
            endpoint = null;
            device?.Dispose();
            StreamStopped?.Invoke();
        }
    }
}
