using System;
using System.IO;
using System.Xml.Serialization;

namespace LittleQuieter
{
    public sealed class Preferences
    {
        public int Limit = 65;
        public bool PlayTone = true;
        public int ToneVolume = 35;
        public string DeviceName = "Windows default microphone";
        public string OutputDeviceId;
        public string OutputDeviceName = "Windows default output";

        public void Validate()
        {
            Limit = Math.Max(15, Math.Min(95, Limit));
            ToneVolume = Math.Max(0, Math.Min(100, ToneVolume));
        }

        public static Preferences Load(string path)
        {
            try
            {
                using (var input = File.OpenRead(path))
                {
                    var settings = (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(input);
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
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var output = File.Create(temporary))
                new XmlSerializer(typeof(Preferences)).Serialize(output, this);
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
    }
}
