namespace LittleQuieter;

internal sealed class ReminderWindow : Form
{
    private readonly Label message;
    public ReminderWindow()
    {
        Text = "Little Quieter reminder";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(380, 150);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        message = new Label { AutoSize = true, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) };
        var dismiss = new Button { Text = "&Dismiss", AutoSize = true, Anchor = AnchorStyles.Right, AccessibleName = "Dismiss reminder", DialogResult = DialogResult.Cancel };
        dismiss.Click += (_, _) => Hide();
        CancelButton = dismiss;
        AcceptButton = dismiss;
        layout.Controls.Add(message, 0, 0);
        layout.Controls.Add(dismiss, 0, 1);
        Controls.Add(layout);
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }
    // Avoid stealing focus, while remaining reachable through the taskbar and Alt+Tab.
    protected override bool ShowWithoutActivation => true;
    public void Remind(Form parent, string text)
    {
        message.Text = text;
        Rectangle area = Screen.FromControl(parent).WorkingArea;
        Location = new Point(Math.Max(area.Left, area.Right - Width - 24), area.Top + 24);
        Show();
    }
}
