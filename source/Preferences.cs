using System.Xml.Serialization;

namespace LittleQuieter;

public sealed class Preferences
{
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Preferences));
    public int Limit = Levels.DefaultLimit;
    public bool PlayTone = true;
    public int ToneVolume = 35;
    public string DeviceName = AudioDevice.DefaultMicrophoneName;
    public string? OutputDeviceId;
    public string OutputDeviceName = AudioDevice.DefaultOutputName;
    public bool ScheduleEnabled;
    public int ScheduleStartMinute = 22 * 60;
    public int ScheduleEndMinute = 7 * 60;

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
            var settings = Serializer.Deserialize(input) as Preferences ?? new Preferences();
            settings.Validate();
            return settings;
        }
        catch (IOException) { return new Preferences(); }
        catch (UnauthorizedAccessException) { return new Preferences(); }
        catch (InvalidOperationException) { return new Preferences(); }
    }

    public void Save(string path)
    {
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
