using System.Drawing;
using System.Windows.Forms;

namespace LittleQuieter
{
    internal sealed class ReminderWindow : Form
    {
        private readonly Label message;
        public ReminderWindow()
        {
            Text = "Little Quieter reminder";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(55, 35, 23);
            ForeColor = Color.White;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 156);
            Font = new Font("Segoe UI", 11);
            var heading = new Label { Text = "A LITTLE QUIETER", ForeColor = MainWindow.Amber, Location = new Point(20, 17), Size = new Size(355, 24), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            message = new Label { Location = new Point(20, 51), Size = new Size(400, 83), Font = new Font("Segoe UI", 15, FontStyle.Bold) };
            var dismiss = new Button { Text = "×", Location = new Point(396, 7), Size = new Size(35, 32), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, TabStop = false, AccessibleName = "Dismiss reminder" };
            dismiss.FlatAppearance.BorderSize = 0;
            dismiss.Click += delegate
            {
                Hide();
            };
            Controls.AddRange(new Control[] { heading, message, dismiss });
            MainWindow.ApplyContrast(this);
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x08000000 | 0x80;
                return p;
            } // NOACTIVATE, TOOLWINDOW
        }
        public void Remind(Form parent, string text)
        {
            message.Text = text;
            Rectangle area = Screen.FromControl(parent).WorkingArea;
            Location = new Point(area.Right - Width - 24, area.Top + 24);
            Show();
        }
    }

}
