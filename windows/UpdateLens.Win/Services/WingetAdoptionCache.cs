using System.IO;
using System.Text.Json;

namespace UpdateLens.Win.Services;

/// <summary>Persists winget search adoption so each Manual app is searched at most ~1x/day.</summary>
public sealed class WingetAdoptionCache
{
    private record Entry(string WingetId, string LatestVersion, long CheckedUnix, bool Found);
    private readonly string _path;
    private Dictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);

    public WingetAdoptionCache()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateLens");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "winget-map.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            _map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json)
                ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { _map = new(StringComparer.OrdinalIgnoreCase); }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_map)); }
        catch { }
    }

    public bool TryGetFresh(string key, TimeSpan ttl, out string wingetId, out string latest)
    {
        wingetId = ""; latest = "";
        if (_map.TryGetValue(key, out var e)
            && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - e.CheckedUnix < ttl.TotalSeconds
            && e.Found)
        { wingetId = e.WingetId; latest = e.LatestVersion; return true; }
        return false;
    }

    public bool IsFreshMiss(string key, TimeSpan ttl)
        => _map.TryGetValue(key, out var e)
           && !e.Found
           && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - e.CheckedUnix < ttl.TotalSeconds;

    public void StoreHit(string key, string wingetId, string latest)
    {
        _map[key] = new Entry(wingetId, latest, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), true);
        Save();
    }

    public void StoreMiss(string key)
    {
        _map[key] = new Entry("", "", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), false);
        Save();
    }
}
