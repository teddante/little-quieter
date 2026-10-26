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

    public static AudioDevice Select(List<AudioDevice> devices, string? id, string name, string defaultName)
    {
        if (id != null)
            return devices.Find(device => device.Id == id) ?? new AudioDevice { Id = id, Name = name, Unavailable = true };
        if (name == defaultName)
            return devices[0];
        // Migrate old name-only preferences only when the match is unambiguous.
        var matches = devices.Where(device => device.Id != null && device.Name == name).ToArray();
        return matches.Length == 1 ? matches[0] : new AudioDevice { Name = name, Unavailable = true };
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

internal sealed class AudioDeviceChanges : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
    private readonly MMDeviceNotificationClient notifications;
    private int version;
    public int Version => Volatile.Read(ref version);

    internal static bool AffectsSelection(DataFlow flow, Role role, bool defaultInput, bool defaultOutput)
    {
        return role == Role.Console && (flow == DataFlow.Capture && defaultInput || flow == DataFlow.Render && defaultOutput);
    }

    public AudioDeviceChanges(Action silence, bool defaultInput, bool defaultOutput)
    {
        notifications = enumerator.CreateNotificationClient(false);
        notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (!AffectsSelection(e.Flow, e.Role, defaultInput, defaultOutput))
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
