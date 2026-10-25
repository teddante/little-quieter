using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace LittleQuieter;

internal static class Installation
{
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LittleQuieter";
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LittleQuieter");
    internal static string ProgramPath => Path.Combine(Folder, "Little Quieter.exe");
    internal static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Little Quieter.lnk");

    public static void Run(bool noLaunch)
    {
        string source = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running executable.");
        bool updating = File.Exists(ProgramPath);
        Install(source, Folder, ShortcutPath, UninstallKey);
        if (!noLaunch)
        {
            var launch = new ProcessStartInfo(ProgramPath) { UseShellExecute = true };
            if (updating)
                launch.ArgumentList.Add("--background");
            Process.Start(launch);
        }
    }

    // Explicit destinations allow isolated integration checks without touching the installed app.
    internal static void Install(string source, string folder, string shortcutPath, string uninstallKey)
    {
        string program = Path.Combine(folder, "Little Quieter.exe");
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(program), StringComparison.OrdinalIgnoreCase))
        {
            foreach (var process in Process.GetProcessesByName("Little Quieter"))
            {
                using (process)
                {
                    if (!string.Equals(process.MainModule?.FileName, program, StringComparison.OrdinalIgnoreCase))
                        continue;
                    using var exit = EventWaitHandle.OpenExisting("Local\\LittleQuieter.Exit.v1");
                    exit.Set();
                    if (!process.WaitForExit(5000))
                        throw new IOException("Close Little Quieter from its tray menu, then try again.");
                }
            }
            Directory.CreateDirectory(folder);
            string staged = program + ".new";
            try
            {
                File.Copy(source, staged, true);
                using (var original = File.OpenRead(source))
                using (var copy = File.OpenRead(staged))
                    if (!SHA256.HashData(original).SequenceEqual(SHA256.HashData(copy)))
                        throw new IOException("The installation copy did not match the source.");
                if (File.Exists(program))
                    File.Replace(staged, program, program + ".previous");
                else
                    File.Move(staged, program);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        using (var script = Assembly.GetExecutingAssembly().GetManifestResourceStream("LittleQuieter.Uninstall.ps1")!)
        using (var output = File.Create(Path.Combine(folder, "Uninstall.ps1")))
            script.CopyTo(output);
        string notices = Path.Combine(Path.GetDirectoryName(source)!, "THIRD-PARTY-NOTICES.txt");
        string installedNotices = Path.Combine(folder, "THIRD-PARTY-NOTICES.txt");
        if (File.Exists(notices) && !string.Equals(notices, installedNotices, StringComparison.OrdinalIgnoreCase))
            File.Copy(notices, installedNotices, true);
        CreateShortcut(shortcutPath, program, folder);
        using var key = Registry.CurrentUser.CreateSubKey(uninstallKey);
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        key.SetValue("DisplayName", "Little Quieter");
        key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
        key.SetValue("DisplayIcon", program);
        key.SetValue("InstallLocation", folder);
        key.SetValue("UninstallString", $"\"{powershell}\" -NoProfile -WindowStyle Hidden -File \"{Path.Combine(folder, "Uninstall.ps1")}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("URLInfoAbout", "https://github.com/teddante/little-quieter");
        // Installation never writes settings or the Run/StartupApproved keys.
    }

    private static void CreateShortcut(string path, string program, string folder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
        object? shortcut = null;
        try
        {
            dynamic automation = shell;
            shortcut = automation.CreateShortcut(path);
            dynamic link = shortcut;
            link.TargetPath = program;
            link.WorkingDirectory = folder;
            link.Description = "Open Little Quieter microphone volume monitor";
            link.Save();
        }
        finally
        {
            if (shortcut != null)
                Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
