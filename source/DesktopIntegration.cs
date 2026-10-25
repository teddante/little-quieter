using System;
using System.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace LittleQuieter
{
    public static class StartupRegistration
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "LittleQuieter";
        public static string Command(string executable)
        {
            return "\"" + executable + "\" --background";
        }
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

    public sealed class AudioDeviceChanges : IDisposable
    {
        private readonly MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
        private readonly MMDeviceNotificationClient notifications;
        private int version;
        public int Version => Volatile.Read(ref version);
        public AudioDeviceChanges(Action silence)
        {
            notifications = enumerator.CreateNotificationClient(false);
            notifications.DefaultDeviceChanged += (_, e) =>
            {
                if (e.Role != Role.Console)
                    return;
                silence();
                Interlocked.Increment(ref version);
            };
        }
        public void Dispose()
        {
            notifications.Dispose();
            enumerator.Dispose();
        }
    }

}
