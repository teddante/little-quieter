using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LittleQuieter
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool background = Array.IndexOf(args, "--background") >= 0;
            bool exit = Array.IndexOf(args, "--exit") >= 0;
            bool first;
            using (var instance = new Mutex(true, "Local\\LittleQuieter.Desktop.v1", out first))
            {
                if (!first)
                {
                    if (background && !exit) return;
                    try { using (var signal = EventWaitHandle.OpenExisting(exit ? "Local\\LittleQuieter.Exit.v1" : "Local\\LittleQuieter.Show.v1")) signal.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("Little Quieter is already running. Open it from the system tray.", "Little Quieter"); }
                    return;
                }
                if (exit) return;
                ApplicationConfiguration.Initialize();
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                { MessageBox.Show("Something went wrong. Stop and restart Little Quieter.\n\n" + e.Exception.Message, "Little Quieter"); };
                using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LittleQuieter.Show.v1"))
                using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LittleQuieter.Exit.v1"))
                {
                    using var main = new MainWindow(background, showSignal, exitSignal);
                    // A hidden top-level handle receives Windows shutdown messages too.
                    var handle = main.Handle;
                    Application.Run(main);
                }
            }
        }
    }

    internal sealed class LevelMeter : Control
    {
        public double Level;
        public int Limit = 65;
        public LevelMeter() { DoubleBuffered = true; AccessibleName = "Live microphone level with warning limit"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int segments = 48;
            float width = (Width - (segments - 1) * 3) / (float)segments;
            for (int i = 0; i < segments; i++)
            {
                double position = (i + 1) * 100.0 / segments;
                Color color = SystemInformation.HighContrast ? SystemColors.Highlight : position < Limit ? MainWindow.Mint : MainWindow.Amber;
                if (position > Level) color = SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(49, 62, 76);
                using (var brush = new SolidBrush(color)) g.FillRectangle(brush, i * (width + 3), 3, width, Height - 6);
            }
            using (var pen = new Pen(SystemInformation.HighContrast ? SystemColors.WindowText : Color.White, 2))
            {
                float x = (float)Math.Min(Width - 2, Math.Max(1, Width * Limit / 100.0));
                g.DrawLine(pen, x, 0, x, Height);
            }
        }
    }

    internal sealed class ReminderWindow : Form
    {
        private readonly Label message;
        private readonly System.Windows.Forms.Timer hideTimer;
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
            dismiss.Click += delegate { Hide(); };
            Controls.AddRange(new Control[] { heading, message, dismiss });
            hideTimer = new System.Windows.Forms.Timer { Interval = 6000 };
            hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); };
            MainWindow.ApplyContrast(this);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } // NOACTIVATE, TOOLWINDOW
        }
        public void Remind(Form parent, string text, bool persistent = false)
        {
            message.Text = text;
            Rectangle area = Screen.FromControl(parent).WorkingArea;
            Location = new Point(area.Right - Width - 24, area.Top + 24);
            Show();
            hideTimer.Stop(); if (!persistent) hideTimer.Start();
        }
        protected override void Dispose(bool disposing) { if (disposing) hideTimer.Dispose(); base.Dispose(disposing); }
    }

}
