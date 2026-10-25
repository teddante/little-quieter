using System;
using System.Collections.Generic;

namespace LittleQuieter
{
    public static class Levels
    {
        public static double ToMeter(double db)
        {
            return Math.Max(0, Math.Min(100, (db + 80) * 1.25));
        }
        public static double ToDb(double meter)
        {
            return meter * 0.8 - 80;
        }

        public static bool TryCalibrate(List<double> readings, out int limit)
        {
            limit = 65;
            if (readings.Count < 30)
                return false;
            var sorted = new List<double>(readings);
            sorted.Sort();
            double low = sorted[(int)((sorted.Count - 1) * 0.15)];
            double high = sorted[(int)((sorted.Count - 1) * 0.85)];
            // Require some variation and a usable input, rather than calibrating silence/a fan.
            if (!double.IsFinite(low) || !double.IsFinite(high) || high < -60 || high > -7 || high - low < 3)
                return false;
            limit = Math.Max(15, Math.Min(95, (int)Math.Round(ToMeter(high + 5))));
            return true;
        }
    }

}
