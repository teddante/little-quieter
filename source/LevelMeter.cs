using System.ComponentModel;

namespace LittleQuieter;

// A live meter needs immediate painting and a threshold cue, without progress animation.
// Keep ProgressBar's native range and accessibility provider.
internal sealed class LevelMeter : ProgressBar
{
    private int limit = Levels.DefaultLimit;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Limit
    {
        get => limit;
        set
        {
            limit = value;
            Invalidate();
        }
    }

    public LevelMeter()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "Live microphone level";
        AccessibleDescription = "Relative microphone level from 0 to 100, with a warning-limit marker.";
    }

    public void SetLevel(double level)
    {
        Value = (int)Math.Round(Math.Clamp(level, 0, 100));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SystemColors.Control);
        Rectangle area = ClientRectangle;
        area.Inflate(-2, -2);
        if (area.Width <= 0 || area.Height <= 0)
            return;
        using var fill = new SolidBrush(SystemColors.Highlight);
        using var marker = new Pen(SystemColors.WindowText, Math.Max(1, DeviceDpi / 96f));
        e.Graphics.FillRectangle(fill, area.Left, area.Top, area.Width * Value / 100f, area.Height);
        float x = area.Left + area.Width * Limit / 100f;
        e.Graphics.DrawLine(marker, x, 0, x, Height);
        e.Graphics.DrawRectangle(marker, 0, 0, Width - 1, Height - 1);
    }
}
