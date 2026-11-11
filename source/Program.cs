namespace LittleQuieter;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        if (args.Contains("--install") || string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "Little Quieter Setup", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Installation.Run(args.Contains("--no-launch"));
                return 0;
            }
            catch (Exception error)
            {
                if (!args.Contains("--no-launch"))
                    MessageBox.Show(error.Message, "Little Quieter installation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        bool background = args.Contains("--background");
        bool exit = args.Contains("--exit");
        using var instance = new Mutex(true, "Local\\LittleQuieter.Desktop.v1", out bool first);
        if (!first)
        {
            if (background && !exit)
                return 0;
            try
            {
                using var signal = EventWaitHandle.OpenExisting(exit ? "Local\\LittleQuieter.Exit.v1" : "Local\\LittleQuieter.Show.v1");
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("Little Quieter is already running. Open it from the system tray.", "Little Quieter"); }
            return 0;
        }
        if (exit)
            return 0;
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LittleQuieter.Show.v1");
        using var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LittleQuieter.Exit.v1");
        using var main = new MainWindow(background, showSignal, exitSignal, pausedForUpdateAt: Installation.ReadPauseTime(args));
        bool handlingError = false;
        Application.ThreadException += async (_, e) =>
        {
            if (handlingError)
                return;
            handlingError = true;
            try
            {
                try
                {
                    await main.PauseAfterError();
                }
                catch { } // Pausing silences and detaches devices before waiting for cleanup.
                if (!main.IsDisposed)
                    MessageBox.Show(main, "Listening has been paused after an error.\n\n" + e.Exception.Message, "Little Quieter");
            }
            finally { handlingError = false; }
        };
        // A hidden top-level handle receives Windows shutdown messages too.
        _ = main.Handle;
        Application.Run(main);
        return 0;
    }
}
