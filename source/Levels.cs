namespace LittleQuieter;

internal static class Levels
{
    public const int MinimumLimit = 15;
    public const int MaximumLimit = 95;
    public const int DefaultLimit = 65;
    public static double ToMeter(double db)
    {
        return Math.Clamp((db + 80) * 1.25, 0, 100);
    }
    public static double ToDb(double meter)
    {
        return meter * 0.8 - 80;
    }

    public static bool TryCalibrate(List<double> readings, out int limit)
    {
        limit = DefaultLimit;
        if (readings.Count < 30)
            return false;
        var sorted = new List<double>(readings);
        sorted.Sort();
        double low = sorted[(int)((sorted.Count - 1) * 0.15)];
        double high = sorted[(int)((sorted.Count - 1) * 0.85)];
        // Require some variation and a usable input, rather than calibrating silence/a fan.
        if (!double.IsFinite(low) || !double.IsFinite(high) || high < -60 || high > -7 || high - low < 3)
            return false;
        limit = Math.Clamp((int)Math.Round(ToMeter(high + 5)), MinimumLimit, MaximumLimit);
        return true;
    }
}
