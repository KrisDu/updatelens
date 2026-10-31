using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace UpdateLens.Win.Services;

/// <summary>Light/dark theme. All theme brushes live in App.xaml and are referenced
/// via DynamicResource, so Apply() swaps them live with no restart.</summary>
public static class ThemeService
{
    public static bool IsDark { get; private set; }

    private static readonly (string Key, string Light, string Dark)[] Brushes = new[]
    {
        ("AccentHoverBrush", "#1975CC", "#4DA3E6"),
        ("AccentPressedBrush", "#005A9E", "#2E7CC2"),
        ("AppBgBrush", "#F3F3F3", "#202020"),
        ("HeaderBgBrush", "#FFFFFF", "#2C2C2C"),
        ("CardBgBrush", "#FFFFFF", "#303030"),
        ("TextFgBrush", "#1B1A19", "#F5F5F5"),
        ("MutedBrush", "#616161", "#A6A6A6"),
        ("HairlineBrush", "#EDEBE9", "#3A3A3A"),
        ("InputBgBrush", "#F3F3F3", "#333333"),
        ("InputBorderBrush", "#E1DFDD", "#4A4A4A"),
        ("AvatarBgBrush", "#E5F1FB", "#0E3A5C"),
        ("PillBlueBg", "#E5F1FB", "#0F3B5E"),
        ("PillBlueFg", "#0067C0", "#60CDFF"),
        ("PillGreenBg", "#E6F4EA", "#103B22"),
        ("PillGreenFg", "#107C10", "#7BD88A"),
        ("PillRedBg", "#FDE7E9", "#4B1F1F"),
        ("PillRedFg", "#C42B1C", "#FF9D8A"),
        ("PillGrayBg", "#F0F0F0", "#3A3A3A"),
        ("PillGrayFg", "#616161", "#C7C7C7"),
    };

    public static void Init()
    {
        var mode = AppSettings.Load().Theme;
        bool dark = mode switch
        {
            "dark" => true,
            "light" => false,
            _ => !SystemUsesLight(),
        };
        Apply(dark);
    }

    public static void SetDark(bool dark)
    {
        Apply(dark);
        var s = AppSettings.Load();
        s.Theme = dark ? "dark" : "light";
        s.Save();
    }

    public static void SetMode(string mode)
    {
        var s = AppSettings.Load();
        s.Theme = mode;
        s.Save();
        Init();
    }

    public static string CurrentMode() => AppSettings.Load().Theme;

    private static void Apply(bool dark)
    {
        IsDark = dark;
        var res = System.Windows.Application.Current?.Resources;
        if (res is null) return;
        foreach (var (key, light, darkHex) in Brushes)
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dark ? darkHex : light);
                if (res[key] is SolidColorBrush brush && !brush.IsFrozen)
                    brush.Color = c;
                else
                    res[key] = new SolidColorBrush(c);
            }
            catch { }
        }
    }

    private static bool SystemUsesLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i != 0;
        }
        catch { }
        return true;
    }
}
