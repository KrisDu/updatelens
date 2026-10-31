using Microsoft.Win32;
using UpdateLens.Win.Models;

namespace UpdateLens.Win.Services;

/// <summary>Reads classic Win32 uninstall entries from registry.</summary>
public sealed class RegistryScanner
{
    private static readonly (RegistryHive Hive, string SubKey)[] Locations = new[]
    {
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    };

    public List<AppEntry> Scan()
    {
        var results = new List<AppEntry>();
        foreach (var (hive, subKey) in Locations)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var key = baseKey.OpenSubKey(subKey);
                if (key is null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(name);
                        if (sub is null) continue;
                        var displayName = sub.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        var publisher = sub.GetValue("Publisher") as string;
                        var releaseType = sub.GetValue("ReleaseType") as string;
                        var parentKey = sub.GetValue("ParentKeyName") as string;
                        var sysComp = sub.GetValue("SystemComponent") as int?;
                        if ((sysComp) == 1) continue;
                        if (string.Equals(releaseType, "Security Update", StringComparison.OrdinalIgnoreCase)) continue;
                        if (AppFilter.IsSystemRegistryEntry(displayName, publisher, sysComp, releaseType, parentKey)) continue;

                        var displayIcon = sub.GetValue("DisplayIcon") as string;
                        var uninstall = sub.GetValue("UninstallString") as string;
                        results.Add(new AppEntry
                        {
                            Id = $"registry:{name}",
                            Name = displayName.Trim(),
                            VersionInstalled = (sub.GetValue("DisplayVersion") as string ?? "").Trim(),
                            Vendor = (publisher ?? "").Trim(),
                            ExePath = IconService.ResolveExe(displayIcon, uninstall),
                            VendorUrl = (sub.GetValue("HelpLink") as string ?? sub.GetValue("URLInfoAbout") as string)?.Trim(),
                            Source = AppSource.System,
                            State = UpdateState.Unknown,
                        });
                    }
                    catch { /* skip bad key */ }
                }
            }
            catch { /* skip hive */ }
        }
        // De-dupe by name+version, keep first
        return results
            .GroupBy(a => (a.Name.ToLowerInvariant(), a.VersionInstalled))
            .Select(g => g.First())
            .OrderBy(a => a.Name)
            .ToList();
    }
}
