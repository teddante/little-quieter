using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace LittleQuieter
{
    public static class Levels
    {
        public static double ToMeter(double db) { return Math.Max(0, Math.Min(100, (db + 80) * 1.25)); }
        public static double ToDb(double meter) { return meter * 0.8 - 80; }

        public static bool TryCalibrate(List<double> readings, out int limit)
        {
            limit = 65;
            if (readings.Count < 30) return false;
            var sorted = new List<double>(readings);
            sorted.Sort();
            double low = sorted[(int)((sorted.Count - 1) * 0.15)];
            double high = sorted[(int)((sorted.Count - 1) * 0.85)];
            // Require some variation and a usable input, rather than calibrating silence/a fan.
            if (high < -60 || high > -7 || high - low < 3) return false;
            limit = Math.Max(15, Math.Min(95, (int)Math.Round(ToMeter(high + 5))));
            return true;
        }
    }

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
            using (var output = File.Create(temporary)) new XmlSerializer(typeof(Preferences)).Serialize(output, this);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
