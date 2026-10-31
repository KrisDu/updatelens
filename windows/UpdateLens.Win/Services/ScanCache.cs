using System.IO;
using System.Text.Json;
using UpdateLens.Win.Models;

namespace UpdateLens.Win.Services;

/// <summary>Last full scan on disk so the list shows instantly at startup.</summary>
public sealed class ScanCache
{
    private record Entry(string Id, string Name, string VersionInstalled, string? VersionAvailable,
        string Vendor, int Source, int State, string? VendorUrl, string? ExePath);
    private record Payload(long CheckedUnix, List<Entry> Apps);

    private static string Path
    {
        get
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateLens");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "last-scan.json");
        }
    }

    public static void Save(List<AppEntry> apps)
    {
        try
        {
            var payload = new Payload(DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                apps.Select(a => new Entry(a.Id, a.Name, a.VersionInstalled, a.VersionAvailable,
                    a.Vendor, (int)a.Source, (int)a.State, a.VendorUrl, a.ExePath)).ToList());
            File.WriteAllText(Path, JsonSerializer.Serialize(payload));
        }
        catch { }
    }

    public static (List<AppEntry> Apps, DateTime When)? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(Path));
            if (payload is null || payload.Apps.Count == 0) return null;
            var apps = payload.Apps.Select(e => new AppEntry
            {
                Id = e.Id, Name = e.Name, VersionInstalled = e.VersionInstalled,
                VersionAvailable = e.VersionAvailable, Vendor = e.Vendor,
                Source = (AppSource)e.Source, State = (UpdateState)e.State,
                VendorUrl = e.VendorUrl, ExePath = e.ExePath,
            }).ToList();
            var when = DateTimeOffset.FromUnixTimeSeconds(payload.CheckedUnix).LocalDateTime;
            return (apps, when);
        }
        catch { return null; }
    }
}
