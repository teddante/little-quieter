using System.Xml.Serialization;

namespace LittleQuieter;

public sealed class Preferences
{
    public int Limit = Levels.DefaultLimit;
    public bool PlayTone = true;
    public int ToneVolume = 35;
    public string DeviceName = AudioDevice.DefaultMicrophoneName;
    public string? OutputDeviceId;
    public string OutputDeviceName = AudioDevice.DefaultOutputName;

    public void Validate()
    {
        Limit = Math.Clamp(Limit, Levels.MinimumLimit, Levels.MaximumLimit);
        ToneVolume = Math.Clamp(ToneVolume, 0, 100);
        if (string.IsNullOrWhiteSpace(DeviceName))
            DeviceName = AudioDevice.DefaultMicrophoneName;
        if (string.IsNullOrWhiteSpace(OutputDeviceName))
            OutputDeviceName = AudioDevice.DefaultOutputName;
    }

    public static Preferences Load(string path)
    {
        try
        {
            using (var input = File.OpenRead(path))
            {
                var settings = new XmlSerializer(typeof(Preferences)).Deserialize(input) as Preferences ?? new Preferences();
                settings.Validate();
                return settings;
            }
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
            new XmlSerializer(typeof(Preferences)).Serialize(output, this);
        if (File.Exists(path))
            File.Replace(temporary, path, null);
        else
            File.Move(temporary, path);
    }
}
