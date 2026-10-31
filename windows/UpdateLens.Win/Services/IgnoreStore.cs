using System.IO;
using System.Text.Json;

namespace UpdateLens.Win.Services;

/// <summary>Persists Skip (per-version) and Hide (manual rows) across restarts.</summary>
public sealed class IgnoreStore
{
    private record Data(Dictionary<string, string> Ignored, List<string> Hidden);
    private readonly string _path;
    private Data _data;

    public IgnoreStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateLens");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "skips.json");
        _data = Load();
    }

    private Data Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(_path))
                    ?? new Data(new(StringComparer.OrdinalIgnoreCase), new());
        }
        catch { }
        return new Data(new(StringComparer.OrdinalIgnoreCase), new());
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_data)); }
        catch { }
    }

    public bool IsIgnored(string id, string? version)
        => !string.IsNullOrWhiteSpace(version)
           && _data.Ignored.TryGetValue(id ?? "", out var v)
           && string.Equals(v, version, StringComparison.OrdinalIgnoreCase);

    public bool IsHidden(string normName) => _data.Hidden.Contains(normName);

    public void Ignore(string id, string version)
    {
        _data.Ignored[id ?? ""] = version;
        Save();
    }

    public void Hide(string normName)
    {
        if (!_data.Hidden.Contains(normName)) { _data.Hidden.Add(normName); Save(); }
    }

    public void Reset()
    {
        _data = new Data(new(StringComparer.OrdinalIgnoreCase), new());
        Save();
    }

    public int SkipCount => _data.Ignored.Count + _data.Hidden.Count;
}
