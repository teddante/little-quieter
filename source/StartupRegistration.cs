using Microsoft.Win32;

namespace LittleQuieter;

internal static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "LittleQuieter";
    public static string Command(string executable) => $"\"{executable}\" --background";
    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(Name) is string;
        }
    }
    public static void SetEnabled(bool enabled, string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled)
            key.SetValue(Name, Command(executable), RegistryValueKind.String);
        else
            key.DeleteValue(Name, false);
    }
}
