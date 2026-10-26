using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace LittleQuieter;

internal static class Installation
{
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LittleQuieter";
    private const string PauseArgument = "--paused-after-update=";
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LittleQuieter");
    internal static string ProgramPath => Path.Combine(Folder, "Little Quieter.exe");
    internal static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Little Quieter.lnk");

    public static void Run(bool noLaunch)
    {
        string source = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the running executable.");
        bool updating = File.Exists(ProgramPath);
        using var running = FindRunning(ProgramPath);
        bool resume = running != null, stopped = false, wasListening = false;
        DateTime pausedAt = DateTime.Now;
        Version? oldVersion = null;
        if (updating)
            Version.TryParse(FileVersionInfo.GetVersionInfo(ProgramPath).FileVersion, out oldVersion);
        try
        {
            Install(source, Folder, ShortcutPath, UninstallKey, () =>
            {
                if (running == null || running.HasExited)
                {
                    resume = false;
                    return;
                }
                wasListening = IsCapturing(running.Id);
                pausedAt = DateTime.Now;
                using var exit = EventWaitHandle.OpenExisting("Local\\LittleQuieter.Exit.v1");
                exit.Set();
                if (!running.WaitForExit(5000))
                    throw new IOException("Close Little Quieter from its tray menu, then try again.");
                stopped = true;
            });
        }
        catch (Exception error)
        {
            // Older versions cannot honor a paused restart. Leave those stopped rather than open the mic.
            if (error is not AggregateException && !noLaunch && stopped && (wasListening || oldVersion != null && oldVersion >= new Version(1, 5, 1)))
                Launch(wasListening, pausedAt);
            throw;
        }
        if (!noLaunch && (!updating || resume))
            Launch(wasListening, updating ? pausedAt : null);
    }

    private static Process? FindRunning(string program)
    {
        foreach (var process in Process.GetProcessesByName("Little Quieter"))
        {
            if (process.Id != Environment.ProcessId &&
                string.Equals(process.MainModule?.FileName, program, StringComparison.OrdinalIgnoreCase))
                return process;
            process.Dispose();
        }
        return null;
    }

    internal static bool IsCapturing(int processId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                using (endpoint)
                {
                    var sessions = endpoint.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                        if (sessions[i].GetProcessID == processId && sessions[i].State == AudioSessionState.AudioSessionStateActive)
                            return true;
                }
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Unknown state resumes paused. This query never opens or changes an audio stream.
        }
        return false;
    }

    internal static string[] ResumeArguments(bool wasListening, DateTime pausedAt)
    {
        return wasListening ? new[] { "--background" } :
            new[] { "--background", PauseArgument + pausedAt.Ticks.ToString(CultureInfo.InvariantCulture) };
    }

    internal static DateTime? ReadPauseTime(string[] args)
    {
        string? argument = args.FirstOrDefault(value => value.StartsWith(PauseArgument, StringComparison.Ordinal));
        if (argument == null)
            return null;
        if (long.TryParse(argument.AsSpan(PauseArgument.Length), CultureInfo.InvariantCulture, out long ticks) &&
            ticks >= DateTime.MinValue.AddDays(1).Ticks && ticks <= DateTime.MaxValue.AddDays(-1).Ticks)
            return new DateTime(ticks, DateTimeKind.Local);
        return DateTime.Now; // A malformed pause request must not silently start listening.
    }

    private static void Launch(bool wasListening, DateTime? pausedAt)
    {
        var launch = new ProcessStartInfo(ProgramPath) { UseShellExecute = true };
        if (pausedAt.HasValue)
            foreach (string argument in ResumeArguments(wasListening, pausedAt.Value))
                launch.ArgumentList.Add(argument);
        Process.Start(launch);
    }

    // Explicit destinations keep integration checks isolated from the installed app.
    internal static void Install(string source, string folder, string shortcutPath, string uninstallKey, Action? beforeReplace = null)
    {
        string program = Path.Combine(folder, "Little Quieter.exe");
        Directory.CreateDirectory(folder);
        string staging = Path.Combine(folder, ".install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var files = new List<(string Staged, string Target)>();
        var originals = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (object Value, RegistryValueKind Kind)>? previousRegistration = null;
        bool filesChanged = false, registryChanged = false;
        try
        {
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(program), StringComparison.OrdinalIgnoreCase))
            {
                string staged = Path.Combine(staging, "program");
                File.Copy(source, staged);
                using (var original = File.OpenRead(source))
                using (var copy = File.OpenRead(staged))
                    if (!SHA256.HashData(original).SequenceEqual(SHA256.HashData(copy)))
                        throw new IOException("The installation copy did not match the source.");
                files.Add((staged, program));
                originals.Add(program + ".previous", File.Exists(program + ".previous") ? File.ReadAllBytes(program + ".previous") : null);
            }
            string scriptPath = Path.Combine(staging, "uninstaller");
            using (var script = Assembly.GetExecutingAssembly().GetManifestResourceStream("LittleQuieter.Uninstall.ps1")!)
            using (var output = File.Create(scriptPath))
                script.CopyTo(output);
            files.Add((scriptPath, Path.Combine(folder, "Uninstall.ps1")));
            string notices = Path.Combine(Path.GetDirectoryName(source)!, "THIRD-PARTY-NOTICES.txt");
            if (File.Exists(notices))
            {
                string staged = Path.Combine(staging, "notices");
                File.Copy(notices, staged);
                files.Add((staged, Path.Combine(folder, "THIRD-PARTY-NOTICES.txt")));
            }
            string stagedShortcut = Path.Combine(staging, "shortcut.lnk");
            CreateShortcut(stagedShortcut, program, folder);
            files.Add((stagedShortcut, shortcutPath));
            foreach (var file in files)
                originals.Add(file.Target, File.Exists(file.Target) ? File.ReadAllBytes(file.Target) : null);
            previousRegistration = ReadRegistration(uninstallKey);
            beforeReplace?.Invoke(); // Do not stop a running app until staging and backups have succeeded.
            filesChanged = true;
            foreach (var file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                if (file.Target == program && File.Exists(program))
                    File.Replace(file.Staged, program, program + ".previous");
                else
                    File.Copy(file.Staged, file.Target, true);
            }
            using var key = Registry.CurrentUser.CreateSubKey(uninstallKey);
            registryChanged = true;
            WriteRegistration(key, program, folder);
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            if (filesChanged)
                foreach (var original in originals.Reverse())
                    try
                    {
                        RestoreFile(original.Key, original.Value);
                    }
                    catch (Exception restoreError) { failures.Add(restoreError); }
            if (registryChanged)
                try
                {
                    RestoreRegistration(uninstallKey, previousRegistration);
                }
                catch (Exception restoreError) { failures.Add(restoreError); }
            if (failures.Count > 1)
                throw new AggregateException("Installation failed and rollback could not restore every item. Keep the previous executable and check folder permissions.", failures);
            throw;
        }
        finally
        {
            string resolved = Path.GetFullPath(staging);
            if (!string.Equals(Path.GetDirectoryName(resolved), Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith(".install-", StringComparison.Ordinal))
                throw new IOException("Refusing unexpected installation cleanup.");
            try
            {
                Directory.Delete(resolved, true);
            }
            catch (IOException) { } // Retain staging if the filesystem prevents cleanup.
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void RestoreFile(string path, byte[]? original)
    {
        if (original == null)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        else if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(original))
            File.WriteAllBytes(path, original);
    }

    private static Dictionary<string, (object Value, RegistryValueKind Kind)>? ReadRegistration(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValueNames().ToDictionary(name => name,
            name => (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!, key.GetValueKind(name)));
    }

    private static void RestoreRegistration(string path, Dictionary<string, (object Value, RegistryValueKind Kind)>? previous)
    {
        if (previous == null)
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            foreach (string name in key.GetValueNames().Except(previous.Keys))
                key.DeleteValue(name);
            foreach (var value in previous)
                key.SetValue(value.Key, value.Value.Value, value.Value.Kind);
        }
    }

    private static void WriteRegistration(RegistryKey key, string program, string folder)
    {
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
