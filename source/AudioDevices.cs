using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

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
        using var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        foreach (var device in devices)
            using (device)
                result.Add(new AudioDevice { Id = device.ID, Name = device.FriendlyName });
        return result;
    }

    public static bool HasActiveSession(DataFlow flow, int? processId = null)
    {
        int targetProcessId = processId ?? Environment.ProcessId;
        using var enumerator = new MMDeviceEnumerator();
        using var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        foreach (var endpoint in devices)
            using (endpoint)
            {
                var sessions = endpoint.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.GetProcessID == targetProcessId && session.State == AudioSessionState.AudioSessionStateActive)
                        return true;
                }
            }
        return false;
    }
}

internal sealed class AudioDeviceChanges : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
    private readonly MMDeviceNotificationClient notifications;
    private int version;
    private volatile bool followDefaultOutput;
    public bool FollowDefaultOutput
    {
        get => followDefaultOutput;
        set => followDefaultOutput = value;
    }
    public int Version => Volatile.Read(ref version);

    internal static bool AffectsSelection(DataFlow flow, Role role, bool defaultInput, bool defaultOutput)
    {
        return role == Role.Console && (flow == DataFlow.Capture && defaultInput || flow == DataFlow.Render && defaultOutput);
    }

    public AudioDeviceChanges(Action silence, bool defaultInput, bool defaultOutput)
    {
        FollowDefaultOutput = defaultOutput;
        notifications = enumerator.CreateNotificationClient(false);
        notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (!AffectsSelection(e.Flow, e.Role, defaultInput, FollowDefaultOutput))
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
