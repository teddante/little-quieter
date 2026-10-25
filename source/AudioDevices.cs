using NAudio.CoreAudioApi;

namespace LittleQuieter;

internal sealed class AudioDevice
{
    public const string DefaultMicrophoneName = "Windows default microphone";
    public const string DefaultOutputName = "Windows default output";
    public string? Id;
    public required string Name;
    public bool Unavailable;
    public override string ToString() => Unavailable ? Name + " (unavailable)" : Name;

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

internal sealed class AudioDeviceChanges : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
    private readonly MMDeviceNotificationClient notifications;
    private int version;
    public int Version => Volatile.Read(ref version);

    public AudioDeviceChanges(Action silence)
    {
        notifications = enumerator.CreateNotificationClient(false);
        notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (e.Role != Role.Console)
                return;
            silence();
            Interlocked.Increment(ref version);
        };
    }

    public void Dispose()
    {
        notifications.Dispose();
        enumerator.Dispose();
    }
}
