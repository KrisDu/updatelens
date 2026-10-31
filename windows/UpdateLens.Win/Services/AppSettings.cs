using System.IO;
using System.Text.Json;

namespace UpdateLens.Win.Services;

/// <summary>Theme choice + window bounds. Lives in LocalAppData\UpdateLens\settings.json.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "system"; // system | light | dark
    public double WinWidth { get; set; }
    public double WinHeight { get; set; }
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;

    private static string Path
    {
        get
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateLens");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path));
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(this)); }
        catch { }
    }
}
