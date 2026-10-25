using System.ComponentModel;

namespace LittleQuieter;

internal sealed class LevelMeter : ProgressBar
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Level
    {
        get => Value;
        set => Value = (int)Math.Round(Math.Clamp(value, 0, 100));
    }
    public int Limit = Levels.DefaultLimit;
    public LevelMeter()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleName = "Live microphone level with warning limit";
        AccessibleDescription = "Relative microphone level, from 0 to 100. The warning limit is adjustable below.";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        const int segments = 48;
        float width = (Width - (segments - 1) * 3) / (float)segments;
        bool highContrast = SystemInformation.HighContrast;
        using var belowLimit = new SolidBrush(SystemColors.Highlight);
        using var aboveLimit = new SolidBrush(highContrast ? SystemColors.Highlight : SystemColors.WindowText);
        using var inactive = new SolidBrush(SystemColors.ControlDark);
        for (int i = 0; i < segments; i++)
        {
            double position = (i + 1) * 100.0 / segments;
            Brush brush = position > Level ? inactive : position < Limit ? belowLimit : aboveLimit;
            g.FillRectangle(brush, i * (width + 3), 3, width, Height - 6);
        }
        using (var pen = new Pen(SystemColors.WindowText, 2))
        {
            float x = (float)Math.Min(Width - 2, Math.Max(1, Width * Limit / 100.0));
            g.DrawLine(pen, x, 0, x, Height);
        }
    }
}
