using System.Text.RegularExpressions;

namespace UpdateLens.Win.Services;

/// <summary>Keeps only plain user-installed apps. Hides runtimes, SDKs, drivers, KB updates.</summary>
public static class AppFilter
{
    private static readonly Regex[] RegistryHide = new[]
    {
        new Regex(@"visual c\+\+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"redistributable", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\.net\s(runtime|host|sdk|targeting|toolset|templates|apphost)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"asp\.net", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"targeting pack|standard targeting|desktop runtime|shared framework|workload|manifest-|mono toolchain|emscripten", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"webview2", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"windows (sdk|software development kit|application compatibility|desktop targeting)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"xaml|vclibs|native runtime|widgets.*runtime|windowsapp|winui", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"click-to-run|extensibility component|licensing component|compatibility database", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bnvidia\b|physx|nvcpl|nvdlisr|frameview|shadowplay|nvcontainer", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bintel\b|realtek|qualcomm|synaptics|elan|amd chipset", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"driver|chipset|firmware|\bbios\b|bluetooth|graphics.*\d+\.\d+|virtual audio|watchdog|telemetry client", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^KB\d+|security update|hotfix|service pack|update for (windows|microsoft)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"iis(\s|$)|iis express|sql server.*setup|web deploy", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private static readonly Regex[] WingetHide = new[]
    {
        new Regex(@"visual c\+\+|redistributable|vclibs", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\.net\s|\.net$|dotnet|apphost|targeting pack|shared framework|windows sdk|native runtime", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"webview2|xaml|winui|windowsapp|widgets.*runtime", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"nvidia|physx|intel\W.*driver|realtek|qualcomm|driver|firmware|\bbios\b|chipset", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // Never hide these even if a pattern above is broad (Edge, Terminal, WSL, dev tools users chose).
    private static readonly string[] WingetAllowIds = new[]
    {
        "Microsoft.Edge", "Microsoft.WindowsTerminal", "Microsoft.WSL",
        "Microsoft.VisualStudioCode", "Microsoft.OneDrive", "Microsoft.Office",
    };

    public static bool IsSystemRegistryEntry(string? name, string? publisher, int? systemComponent, string? releaseType, string? parentKeyName)
    {
        if (systemComponent == 1) return true;
        if (!string.IsNullOrWhiteSpace(parentKeyName)) return true;
        if (!string.IsNullOrWhiteSpace(releaseType) &&
            (releaseType.Contains("Security Update", StringComparison.OrdinalIgnoreCase)
             || releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase)
             || releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (string.IsNullOrWhiteSpace(name)) return true;
        var hay = $"{name} {publisher}";
        foreach (var rx in RegistryHide)
            if (rx.IsMatch(hay) && !IsAllowedUserApp(name)) return true;
        return false;
    }

    public static bool IsSystemWingetRow(string name, string id)
    {
        foreach (var allow in WingetAllowIds)
            if (string.Equals(id, allow, StringComparison.OrdinalIgnoreCase)) return false;
        var hay = $"{name} {id}";
        foreach (var rx in WingetHide)
            if (rx.IsMatch(hay)) return true;
        return false;
    }

    private static bool IsAllowedUserApp(string name)
    {
        // Keep the browser itself even though publisher is Microsoft; WebView2 is already hidden by its own name.
        if (name.Equals("Microsoft Edge", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("Office Professional", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
