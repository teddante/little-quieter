namespace LittleQuieter;

// WinForms 10 applies native control themes when creating handles. These small
// adapters expose the supported protected API for refreshing existing controls.
internal interface IThemeControl
{
    void RefreshTheme();
}

internal sealed class ThemeButton : Button, IThemeControl
{
    void IThemeControl.RefreshTheme() => RecreateHandle();
}

internal sealed class ThemeCheckBox : CheckBox, IThemeControl
{
    void IThemeControl.RefreshTheme() => RecreateHandle();
}

internal sealed class ThemeComboBox : ComboBox, IThemeControl
{
    void IThemeControl.RefreshTheme() => RecreateHandle();
}

internal sealed class ThemeTrackBar : TrackBar, IThemeControl
{
    void IThemeControl.RefreshTheme() => RecreateHandle();
}

internal sealed class ThemeNumericUpDown : NumericUpDown, IThemeControl
{
    void IThemeControl.RefreshTheme() => RecreateHandle();
}

internal sealed class ThemeDateTimePicker : DateTimePicker, IThemeControl
{
    void IThemeControl.RefreshTheme() => RecreateHandle();
}
