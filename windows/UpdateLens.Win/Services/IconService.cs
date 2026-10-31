using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Drawing;

namespace UpdateLens.Win.Services;

/// <summary>Extracts exe icons to a PNG cache. Returns null when no usable icon exists.</summary>
public static class IconService
{
    private static string CacheDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UpdateLens", "icons");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Derives an exe path from DisplayIcon / UninstallString registry values.</summary>
    public static string? ResolveExe(string? displayIcon, string? uninstallString)
    {
        foreach (var raw in new[] { displayIcon, uninstallString })
        {
            var exe = ExtractExe(raw);
            if (exe != null && File.Exists(exe)) return exe;
        }
        return null;
    }

    private static string? ExtractExe(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Trim('"');
        // DisplayIcon often looks like "C:\...app.exe,0"
        var comma = s.LastIndexOf(',');
        if (comma > 0 && s.EndsWith(",0")) s = s[..comma].Trim().Trim('"');
        if (s.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase)) return null;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end > 1) s = s[1..end];
        }
        else if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // unquoted path with args: cut at ".exe"
            var i = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            s = s[..(i + 4)];
        }
        else return null;
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s : null;
    }

    public static Task<string?> GetIconAsync(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return Task.FromResult<string?>(null);
        return Task.Run<string?>(() =>
        {
            try
            {
                using var md5 = MD5.Create();
                var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(exePath.ToLowerInvariant())));
                var outPath = Path.Combine(CacheDir, hash + ".png");
                if (File.Exists(outPath)) return outPath;
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon is null) return null;
                using var bmp = icon.ToBitmap();
                bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                return outPath;
            }
            catch { return null; }
        });
    }
}
