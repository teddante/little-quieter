namespace LittleQuieter;

internal sealed class LevelMeter : Control
{
    public double Level;
    public int Limit = Levels.DefaultLimit;
    public LevelMeter()
    {
        DoubleBuffered = true;
        AccessibleName = "Live microphone level with warning limit";
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        const int segments = 48;
        float width = (Width - (segments - 1) * 3) / (float)segments;
        bool highContrast = SystemInformation.HighContrast;
        using var belowLimit = new SolidBrush(highContrast ? SystemColors.Highlight : MainWindow.Mint);
        using var aboveLimit = new SolidBrush(highContrast ? SystemColors.Highlight : MainWindow.Amber);
        using var inactive = new SolidBrush(highContrast ? SystemColors.GrayText : Color.FromArgb(49, 62, 76));
        for (int i = 0; i < segments; i++)
        {
            double position = (i + 1) * 100.0 / segments;
            Brush brush = position > Level ? inactive : position < Limit ? belowLimit : aboveLimit;
            g.FillRectangle(brush, i * (width + 3), 3, width, Height - 6);
        }
        using (var pen = new Pen(highContrast ? SystemColors.WindowText : Color.White, 2))
        {
            float x = (float)Math.Min(Width - 2, Math.Max(1, Width * Limit / 100.0));
            g.DrawLine(pen, x, 0, x, Height);
        }
    }
}
