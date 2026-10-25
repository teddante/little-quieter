namespace LittleQuieter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool background = args.Contains("--background");
        bool exit = args.Contains("--exit");
        using var instance = new Mutex(true, "Local\\LittleQuieter.Desktop.v1", out bool first);
        if (!first)
        {
            if (background && !exit)
                return;
            try
            {
                using var signal = EventWaitHandle.OpenExisting(exit ? "Local\\LittleQuieter.Exit.v1" : "Local\\LittleQuieter.Show.v1");
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("Little Quieter is already running. Open it from the system tray.", "Little Quieter"); }
            return;
        }
        if (exit)
            return;
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Something went wrong. Stop and restart Little Quieter.\n\n" + e.Exception.Message, "Little Quieter");
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LittleQuieter.Show.v1");
        using var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LittleQuieter.Exit.v1");
        using var main = new MainWindow(background, showSignal, exitSignal);
        // A hidden top-level handle receives Windows shutdown messages too.
        _ = main.Handle;
        Application.Run(main);
    }
}
