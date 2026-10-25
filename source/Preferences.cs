using System.Xml.Serialization;

namespace LittleQuieter;

public sealed class Preferences
{
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Preferences));
    public int Limit = Levels.DefaultLimit;
    public bool PlayTone = true;
    public int ToneVolume = 35;
    public string? DeviceId;
    public string DeviceName = AudioDevice.DefaultMicrophoneName;
    public string? OutputDeviceId;
    public string OutputDeviceName = AudioDevice.DefaultOutputName;
    public bool ScheduleEnabled;
    public int ScheduleStartMinute = 22 * 60;
    public int ScheduleEndMinute = 7 * 60;
    public bool ShowReminder = true;

    internal bool IsNew
    {
        get; private set;
    }
    internal bool NeedsRecovery
    {
        get; private set;
    }

    public void Validate()
    {
        Limit = Math.Clamp(Limit, Levels.MinimumLimit, Levels.MaximumLimit);
        ToneVolume = Math.Clamp(ToneVolume, 0, 100);
        ScheduleStartMinute = Math.Clamp(ScheduleStartMinute, 0, 1439);
        ScheduleEndMinute = Math.Clamp(ScheduleEndMinute, 0, 1439);
        if (string.IsNullOrWhiteSpace(DeviceName))
            DeviceName = AudioDevice.DefaultMicrophoneName;
        if (string.IsNullOrWhiteSpace(OutputDeviceName))
            OutputDeviceName = AudioDevice.DefaultOutputName;
    }

    public static Preferences Load(string path)
    {
        try
        {
            using var input = File.OpenRead(path);
            var settings = Serializer.Deserialize(input) as Preferences ?? throw new InvalidOperationException("The settings file is empty.");
            settings.Validate();
            return settings;
        }
        catch (FileNotFoundException) { return new Preferences { IsNew = true }; }
        catch (DirectoryNotFoundException) { return new Preferences { IsNew = true }; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new Preferences { NeedsRecovery = true, PlayTone = false, ToneVolume = 0 };
        }
    }

    internal string Recover(string path)
    {
        string backup = path + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        // Do not replace an unreadable original if making its backup fails.
        File.Copy(path, backup);
        NeedsRecovery = false;
        try
        {
            Save(path);
        }
        catch { NeedsRecovery = true; throw; }
        return backup;
    }

    public void Save(string path)
    {
        if (NeedsRecovery)
            throw new InvalidOperationException("The original settings are preserved. Recover settings in Little Quieter before saving changes.");
        Validate();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string temporary = path + ".tmp";
        using (var output = File.Create(temporary))
            Serializer.Serialize(output, this);
        if (File.Exists(path))
            File.Replace(temporary, path, null);
        else
            File.Move(temporary, path);
    }
}
