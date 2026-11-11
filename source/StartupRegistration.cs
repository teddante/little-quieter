using Microsoft.Win32;

namespace LittleQuieter;

internal static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "LittleQuieter";
    public static string Command(string executable) => $"\"{executable}\" --background";
    public static string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue(Name) as string;
    }
    public static bool Matches(string? command, string executable)
    {
        const string arguments = " --background";
        return command != null && command.EndsWith(arguments, StringComparison.Ordinal) &&
            string.Equals(command[..^arguments.Length], $"\"{executable}\"", StringComparison.OrdinalIgnoreCase);
    }
    public static void SetEnabled(bool enabled, string executable, string keyPath = Key)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (enabled)
            key.SetValue(Name, Command(executable), RegistryValueKind.String);
        else if (Matches(key.GetValue(Name) as string, executable))
            key.DeleteValue(Name, false);
    }
}
