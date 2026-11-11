using System.Runtime.InteropServices;

namespace LittleQuieter;

internal static class WindowTheme
{
    public static (SystemColorMode Mode, bool HighContrast) Current =>
        (Application.ColorMode == SystemColorMode.System ? Application.SystemColorMode : Application.ColorMode, SystemInformation.HighContrast);

    public static void RefreshControls(Control root)
    {
        if (root is IThemeControl themed)
            themed.RefreshTheme();
        foreach (Control child in root.Controls)
            RefreshControls(child);
        root.Invalidate();
    }

    public static void ApplyTitle(Form window)
    {
        if (!window.IsHandleCreated)
            return;
        var (mode, highContrast) = Current;
        int dark = !highContrast && mode == SystemColorMode.Dark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE is documented for Windows 11.
        _ = DwmSetWindowAttribute(window.Handle, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
