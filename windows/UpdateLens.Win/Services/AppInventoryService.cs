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
    public int AdoptedCount { get; private set; }

    public async Task<List<AppEntry>> RefreshAsync(IgnoreStore? ignores = null)
    {
        var reg = await Task.Run(() => _registry.Scan());
        WingetAvailable = _winget.IsAvailable();
        PromotedCount = 0;
        AdoptedCount = 0;

        List<WingetRow> installed = new();
        Dictionary<string, (string Installed, string Available)> upgrades = new(StringComparer.OrdinalIgnoreCase);
        if (WingetAvailable)
        {
            try
            {
                // 2 independent calls, run together to halve scan time.
                var listTask = _winget.GetInstalledAsync();
                var upgradeTask = _winget.GetUpgradesAsync();
                await Task.WhenAll(listTask, upgradeTask);
                installed = await listTask;
                upgrades = await upgradeTask;
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

        // (2) Adopt Manual apps that exist in the winget catalog but were installed
        // outside winget (winget list shows them as ARP/* with no real Id).
        // Cached: at most ~1 search per app per day; misses cached 7 days.
        if (WingetAvailable)
        {
            var cache = new WingetAdoptionCache();
            int budget = 15;
            foreach (var r in results.Where(r => r.Source != AppSource.Winget))
            {
                string key = Norm(r.Name);
                if (string.IsNullOrEmpty(key)) continue;
                if (cache.TryGetFresh(key, TimeSpan.FromHours(24), out var cachedId, out var cachedLatest))
                {
                    AdoptAsWinget(r, cachedId, cachedLatest, usedWingetIds);
                    continue;
                }
                if (cache.IsFreshMiss(key, TimeSpan.FromDays(7))) continue;
                if (budget-- <= 0) continue;
                try
                {
                    var cands = await _winget.SearchAsync(SearchQuery(r.Name), 3);
                    var pick = PickAdoption(r.Name, cands);
                    if (pick != null)
                    {
                        cache.StoreHit(key, pick.Id, pick.Available);
                        AdoptAsWinget(r, pick.Id, pick.Available, usedWingetIds);
                    }
                    else cache.StoreMiss(key); // searched OK, catalog has no exact match
                }
                catch
                {
                    // Transient (lock/timeout/network): do NOT cache — retry next scan.
                }
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
                // Check list's own Available column; keep adoption's
                // provisional version when the list knows nothing about this Id.
                if (byId.TryGetValue(r.Id, out var row) && !string.IsNullOrWhiteSpace(row.Available))
                {
                    r.VersionAvailable = row.Available;
                    r.State = ShouldIgnore(r, row.Available) ? UpdateState.Ignored : UpdateState.UpdateAvailable;
                }
                else if (string.IsNullOrWhiteSpace(r.VersionAvailable))
                    r.State = UpdateState.UpToDate;
                else if (VersionHelper.IsNewer(r.VersionAvailable, r.VersionInstalled))
                    r.State = ShouldIgnore(r, r.VersionAvailable!) ? UpdateState.Ignored : UpdateState.UpdateAvailable;
                else
                    r.State = UpdateState.UpToDate;
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

        ApplyStores(results, ignores);

        return results.OrderByDescending(a => a.State == UpdateState.UpdateAvailable)
                      .ThenBy(a => a.Name).ToList();
    }

    /// <summary>Shared finalize so cached scans honor current skips/hides too.</summary>
    internal static void ApplyStores(List<AppEntry> results, IgnoreStore? ignores)
    {
        foreach (var r in results)
        {
            if (r.State == UpdateState.Unknown)
                r.State = r.Source == AppSource.Winget ? UpdateState.UpToDate : UpdateState.Manual;
            if ((!string.IsNullOrWhiteSpace(r.VersionAvailable) && !string.IsNullOrWhiteSpace(r.IgnoredVersion)
                && string.Equals(r.VersionAvailable, r.IgnoredVersion, StringComparison.OrdinalIgnoreCase))
                || (ignores != null && ignores.IsIgnored(r.Id, r.VersionAvailable)))
                r.State = UpdateState.Ignored;
            // Vendor fallback so Site works even when the registry key has no URL.
            if (r.State == UpdateState.Manual && string.IsNullOrWhiteSpace(r.VendorUrl)
                && VendorMap.TryGet(r.Name, out var vurl, out _))
                r.VendorUrl = vurl;
        }

        // Persisted hides (Manual rows the user dismissed).
        if (ignores != null)
            results.RemoveAll(r => r.State == UpdateState.Manual && ignores.IsHidden(Norm(r.Name)));
    }

    private void AdoptAsWinget(AppEntry r, string wingetId, string latest, HashSet<string> usedWingetIds)
    {
        r.Id = wingetId;
        r.Source = AppSource.Winget;
        usedWingetIds.Add(wingetId);
        AdoptedCount++;
        // Provisional: upgrade/list pass below refines it when authoritative data exists.
        if (!string.IsNullOrWhiteSpace(latest) && !string.Equals(latest, "Unknown", StringComparison.OrdinalIgnoreCase)
            && VersionHelper.IsNewer(latest, r.VersionInstalled))
        {
            r.VersionAvailable = latest;
            r.State = ShouldIgnore(r, latest) ? UpdateState.Ignored : UpdateState.UpdateAvailable;
        }
    }

    internal static string SearchQuery(string name)
    {
        var q = (name ?? "").Trim();
        foreach (var p in new[] { "Microsoft ", "Logitech ", "Logi ", "Adobe ", "VideoLAN " })
            if (q.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { q = q[p.Length..].Trim(); break; }
        q = Regex.Replace(q, @"\s*\(?(64-bit|x64|x86|64bit)\)?\s*$", "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(q) ? (name ?? "").Trim() : q;
    }

    /// <summary>Strict: adopt only on exact normalized-name match. Never guess.</summary>
    internal static WingetRow? PickAdoption(string registryName, List<WingetRow> candidates)
    {
        var want = Norm(registryName);
        if (string.IsNullOrEmpty(want)) return null;
        foreach (var c in candidates)
        {
            if (!WingetService.IsRealWingetId(c.Id)) continue;
            if (string.Equals(Norm(c.Name), want, StringComparison.Ordinal))
                return c;
        }
        return null;
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
