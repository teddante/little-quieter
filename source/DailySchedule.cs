namespace LittleQuieter;

internal sealed class DailySchedule
{
    private bool initialized;
    private int start, end;
    private DateTime? period;

    // A decision is returned only when the settings or active period change.
    // This lets a manual stop or a device failure stay paused until the next period.
    public bool? Update(bool enabled, int startMinute, int endMinute, DateTime localNow)
    {
        if (!enabled)
        {
            initialized = false;
            return null;
        }
        DateTime? current = ActivePeriodStart(startMinute, endMinute, localNow);
        if (initialized && start == startMinute && end == endMinute && period == current)
            return null;
        initialized = true;
        start = startMinute;
        end = endMinute;
        period = current;
        return current.HasValue;
    }

    public static DateTime? ActivePeriodStart(int startMinute, int endMinute, DateTime localNow)
    {
        if (startMinute is < 0 or >= 1440 || endMinute is < 0 or >= 1440 || startMinute == endMinute)
            return null;
        DateTime beginning = localNow.Date.AddMinutes(startMinute);
        DateTime ending = localNow.Date.AddMinutes(endMinute);
        if (startMinute > endMinute)
        {
            if (localNow < ending)
                beginning = beginning.AddDays(-1);
            else
                ending = ending.AddDays(1);
        }
        return localNow >= beginning && localNow < ending ? beginning : null;
    }
}
