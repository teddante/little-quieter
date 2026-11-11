namespace LittleQuieter;

// Keep one reminder through brief quiet gaps; audio still follows each analysis frame.
internal sealed class ReminderState
{
    internal const double QuietSeconds = 0.5;
    private double quietSince = double.NaN;
    private bool visible, dismissed, wasPreviewing;

    public bool Update(bool active, bool previewing, double now)
    {
        if (active || previewing)
        {
            if (previewing && !wasPreviewing)
                dismissed = false;
            quietSince = double.NaN;
            visible = !dismissed;
        }
        else if (double.IsNaN(quietSince))
            quietSince = now;
        else if (now - quietSince >= QuietSeconds)
        {
            visible = dismissed = false;
        }
        wasPreviewing = previewing;
        return visible;
    }

    public void Dismiss()
    {
        dismissed = true;
        visible = false;
    }

    public void Reset()
    {
        visible = dismissed = wasPreviewing = false;
        quietSince = double.NaN;
    }
}
