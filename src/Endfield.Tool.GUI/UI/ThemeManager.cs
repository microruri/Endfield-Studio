namespace Endfield.Tool.GUI.UI;

public static class ThemeManager
{
    public static GuiTheme CurrentTheme { get; private set; } = GuiTheme.System;

    public static ThemePalette GetPalette(GuiTheme theme)
    {
        var resolvedTheme = ResolveTheme(theme);

        return resolvedTheme switch
        {
            GuiTheme.Dark => new ThemePalette(
                FormBack: Color.FromArgb(32, 32, 32),
                Surface: Color.FromArgb(45, 45, 45),
                SurfaceAlt: Color.FromArgb(54, 54, 54),
                Border: Color.FromArgb(78, 78, 78),
                PrimaryText: Color.FromArgb(232, 232, 232),
                SecondaryText: Color.FromArgb(176, 176, 176),
                Accent: Color.FromArgb(67, 121, 242)),

            _ => new ThemePalette(
                FormBack: Color.FromArgb(246, 247, 249),
                Surface: Color.White,
                SurfaceAlt: Color.FromArgb(250, 250, 250),
                Border: Color.FromArgb(214, 217, 224),
                PrimaryText: Color.FromArgb(29, 34, 40),
                SecondaryText: Color.FromArgb(88, 95, 107),
                Accent: Color.FromArgb(44, 96, 220))
        };
    }

    public static GuiTheme ResolveTheme(GuiTheme theme)
    {
        return theme == GuiTheme.System
            ? (IsDarkSystemTheme() ? GuiTheme.Dark : GuiTheme.Light)
            : theme;
    }

    public static void ApplyAppTheme(GuiTheme theme)
    {
        CurrentTheme = theme;

#if NET9_0_OR_GREATER
#pragma warning disable WFO5001
        try
        {
            if (theme == GuiTheme.System)
            {
                Application.SetColorMode(SystemColorMode.System);
                return;
            }

            if (theme == GuiTheme.Dark)
            {
                Application.SetColorMode(SystemColorMode.Dark);
                return;
            }

            Application.SetColorMode(SystemColorMode.Classic);
        }
        catch
        {
            // Ignore and fallback to manual palette rendering only.
        }
#pragma warning restore WFO5001
#endif
    }

    private static bool IsDarkSystemTheme()
    {
        try
        {
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int intValue)
                return intValue == 0;
        }
        catch
        {
            // Ignore registry errors.
        }

        return false;
    }
}
