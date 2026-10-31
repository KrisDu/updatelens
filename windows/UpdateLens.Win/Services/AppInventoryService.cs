using System.Text.RegularExpressions;
using UpdateLens.Win.Models;

namespace UpdateLens.Win.Services;

/// <summary>Merges registry inventory with `winget list` + `winget upgrade`.</summary>
public sealed class AppInventoryService
{
    private readonly RegistryScanner _registry = new();
    private readonly WingetService _winget = new();

    public bool WingetAvailable { get; private set; }
    public int PromotedCount { get; private set; }

    public async Task<List<AppEntry>> RefreshAsync()
    {
        var reg = await Task.Run(() => _registry.Scan());
        WingetAvailable = _winget.IsAvailable();
        PromotedCount = 0;

        List<WingetRow> installed = new();
        Dictionary<string, (string Installed, string Available)> upgrades = new(StringComparer.OrdinalIgnoreCase);
        if (WingetAvailable)
        {
            try
            {
                // 2 calls total (list + upgrade). List covers up-to-date apps,
                // upgrade covers --include-unknown extras.
                installed = await _winget.GetInstalledAsync();
                upgrades = await _winget.GetUpgradesAsync();
                // Hide drivers / runtimes / system components — user apps only.
                installed = installed.Where(w => !AppFilter.IsSystemWingetRow(w.Name, w.Id)).ToList();
                foreach (var sysId in upgrades.Keys.Where(k => AppFilter.IsSystemWingetRow(k, k)).ToList())
                    upgrades.Remove(sysId);
            }
            catch { }
        }

        var byId = installed
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var wingetByNorm = installed
            .Where(r => WingetService.IsRealWingetId(r.Id))
            .GroupBy(r => Norm(r.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var results = new List<AppEntry>(reg);
        var usedWingetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // (1) Fuzzy-match registry -> winget list. Promote to real winget Id when found.
        foreach (var r in results)
        {
            var n = Norm(r.Name);
            WingetRow? hit = null;
            if (wingetByNorm.TryGetValue(n, out var exact)) hit = exact;
            else
            {
                // contains-match: "calibre 64bit" -> "calibre", "Git" token match
                foreach (var kv in wingetByNorm)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    if (n.Contains(kv.Key) || kv.Key.Contains(n))
                    { hit = kv.Value; break; }
                }
                // last resort: id token appears in name, e.g. id Git.Git -> "git"
                if (hit is null)
                {
                    foreach (var w in installed.Where(w => WingetService.IsRealWingetId(w.Id)))
                    {
                        var token = w.Id.Split('.')[^1].ToLowerInvariant();
                        if (token.Length >= 3 && n.Contains(token))
                        { hit = w; break; }
                    }
                }
            }

            if (hit != null)
            {
                r.Id = hit.Id;
                r.Source = AppSource.Winget;
                if (!string.IsNullOrWhiteSpace(hit.Installed))
                    r.VersionInstalled = hit.Installed;
                usedWingetIds.Add(hit.Id);
                PromotedCount++;
            }
        }

        // Apply upgrade info to promoted + existing winget rows
        foreach (var r in results.Where(r => r.Source == AppSource.Winget))
        {
            if (upgrades.TryGetValue(r.Id, out var up) && !string.IsNullOrWhiteSpace(up.Available))
            {
                r.VersionAvailable = up.Available;
                r.State = ShouldIgnore(r, up.Available) ? UpdateState.Ignored : UpdateState.UpdateAvailable;
            }
            else
            {
                // Check list's own Available column
                if (byId.TryGetValue(r.Id, out var row) && !string.IsNullOrWhiteSpace(row.Available))
                {
                    r.VersionAvailable = row.Available;
                    r.State = ShouldIgnore(r, row.Available) ? UpdateState.Ignored : UpdateState.UpdateAvailable;
                }
                else r.State = UpdateState.UpToDate;
            }
        }

        // Add winget-only rows (installed via winget, no registry entry) + upgrade-only rows
        foreach (var w in installed.Where(w => WingetService.IsRealWingetId(w.Id) && !usedWingetIds.Contains(w.Id)))
        {
            if (results.Any(r => string.Equals(r.Id, w.Id, StringComparison.OrdinalIgnoreCase))) continue;
            bool hasUp = !string.IsNullOrWhiteSpace(w.Available)
                || (upgrades.TryGetValue(w.Id, out var u) && !string.IsNullOrWhiteSpace(u.Available));
            string avail = !string.IsNullOrWhiteSpace(w.Available) ? w.Available
                : hasUp && upgrades.TryGetValue(w.Id, out var uu) ? uu.Available : "";
            results.Add(new AppEntry
            {
                Id = w.Id,
                Name = w.Name,
                VersionInstalled = w.Installed,
                VersionAvailable = avail,
                Source = AppSource.Winget,
                State = hasUp ? UpdateState.UpdateAvailable : UpdateState.UpToDate,
            });
        }
        foreach (var kv in upgrades)
        {
            if (results.Any(r => string.Equals(r.Id, kv.Key, StringComparison.OrdinalIgnoreCase))) continue;
            results.Add(new AppEntry
            {
                Id = kv.Key,
                Name = kv.Key,
                VersionInstalled = kv.Value.Installed,
                VersionAvailable = kv.Value.Available,
                Source = AppSource.Winget,
                State = UpdateState.UpdateAvailable,
            });
        }

        foreach (var r in results)
        {
            if (r.State == UpdateState.Unknown)
                r.State = r.Source == AppSource.Winget ? UpdateState.UpToDate : UpdateState.Manual;
            if (!string.IsNullOrWhiteSpace(r.VersionAvailable) && !string.IsNullOrWhiteSpace(r.IgnoredVersion)
                && string.Equals(r.VersionAvailable, r.IgnoredVersion, StringComparison.OrdinalIgnoreCase))
                r.State = UpdateState.Ignored;
        }

        return results.OrderByDescending(a => a.State == UpdateState.UpdateAvailable)
                      .ThenBy(a => a.Name).ToList();
    }

    internal static string Norm(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.ToLowerInvariant();
        t = Regex.Replace(t, @"\b(x64|x86|64[-\s]?bit|32[-\s]?bit|en-us|for windows|desktop)\b", " ");
        t = Regex.Replace(t, @"[^a-z0-9]+", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }

    private static bool ShouldIgnore(AppEntry app, string available)
        => !string.IsNullOrWhiteSpace(app.IgnoredVersion)
           && string.Equals(app.IgnoredVersion, available, StringComparison.OrdinalIgnoreCase);
}
