using System.Diagnostics;
using System.Text.RegularExpressions;
using UpdateLens.Win.Models;

namespace UpdateLens.Win.Services;

public sealed record WingetRow(string Name, string Id, string Installed, string Available, string Source);

/// <summary>
/// WinGet wrapper. v1.29 has no --output json for list/search/upgrade,
/// so we parse the text tables. Single `winget list` call is the main source.
/// </summary>
public sealed class WingetService
{
    public bool IsAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("winget", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(8000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<string> RunAsync(string args, int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo("winget", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi };
        var outText = new System.Text.StringBuilder();
        object logLock = new();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (logLock) outText.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(); } catch { } }
        return outText.ToString();
    }

    public static bool IsRealWingetId(string? id)
        => !string.IsNullOrWhiteSpace(id)
           && !id.Contains('\\')
           && id.Contains('.');

    /// <summary>All installed packages known to winget (1 call). Available may be empty.</summary>
    public async Task<List<WingetRow>> GetInstalledAsync()
    {
        try
        {
            string t = await RunAsync("list --accept-source-agreements --disable-interactivity");
            return ParseListOrUpgradeTable(t);
        }
        catch { return new(); }
    }

    /// <summary>Subset with upgrades available (1 call).</summary>
    public async Task<Dictionary<string, (string Installed, string Available)>> GetUpgradesAsync()
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string t = await RunAsync("upgrade --include-unknown --accept-source-agreements --disable-interactivity");
            foreach (var r in ParseListOrUpgradeTable(t))
                if (!string.IsNullOrWhiteSpace(r.Available))
                    map[r.Id] = (r.Installed, r.Available);
        }
        catch { }
        return map;
    }

    /// <summary>Single on-demand lookup for a manual app. Returns top candidates.</summary>
    public async Task<List<WingetRow>> SearchAsync(string query, int count = 3)
    {
        try
        {
            string t = await RunAsync($"search \"{query}\" -n {count} --accept-source-agreements --disable-interactivity", 30_000);
            return ParseSearchTable(t);
        }
        catch { return new(); }
    }

    internal static List<WingetRow> ParseListOrUpgradeTable(string table)
    {
        var rows = new List<WingetRow>();
        if (string.IsNullOrWhiteSpace(table)) return rows;
        var lines = table.Split('\n').Select(l => l.TrimEnd()).ToList();
        int start = lines.FindIndex(l => l.TrimStart().StartsWith("---"));
        if (start < 0)
        {
            start = lines.FindIndex(l => l.Contains("Name") && l.Contains("Id") && l.Contains("Version"));
            if (start < 0) return rows;
        }
        for (int i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("No applicable", StringComparison.OrdinalIgnoreCase)) break;
            if (line.StartsWith("No installed", StringComparison.OrdinalIgnoreCase)) break;
            // Truncation notice line
            if (line.Contains("truncated", StringComparison.OrdinalIgnoreCase) && line.Length < 80) continue;
            var cols = Regex.Split(line, @"\s{2,}").Where(c => c.Length > 0).ToArray();
            // list/upgrade shapes:
            // 5: Name | Id | Installed | Available | Source
            // 4: Name | Id | Installed | Source (no update)
            // 3: Name | Id | Installed (ARP/MSIX, no source shown)
            if (cols.Length == 5)
                rows.Add(new WingetRow(cols[0].Trim(), cols[1].Trim(), cols[2].Trim(), cols[3].Trim(), cols[4].Trim()));
            else if (cols.Length == 4)
                rows.Add(new WingetRow(cols[0].Trim(), cols[1].Trim(), cols[2].Trim(), "", cols[3].Trim()));
            else if (cols.Length == 3)
                rows.Add(new WingetRow(cols[0].Trim(), cols[1].Trim(), cols[2].Trim(), "", ""));
        }
        return rows;
    }

    internal static List<WingetRow> ParseSearchTable(string table)
    {
        var rows = new List<WingetRow>();
        if (string.IsNullOrWhiteSpace(table)) return rows;
        var lines = table.Split('\n').Select(l => l.TrimEnd()).ToList();
        int start = lines.FindIndex(l => l.TrimStart().StartsWith("---"));
        if (start < 0) return rows;
        for (int i = start + 1; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("No package", StringComparison.OrdinalIgnoreCase)) break;
            var cols = Regex.Split(line, @"\s{2,}").Where(c => c.Length > 0).ToArray();
            // search shapes:
            // 5: Name | Id | Version | Match | Source
            // 4: Name | Id | Version | Source
            if (cols.Length == 5)
                rows.Add(new WingetRow(cols[0].Trim(), cols[1].Trim(), "", cols[2].Trim(), cols[4].Trim()));
            else if (cols.Length == 4)
                rows.Add(new WingetRow(cols[0].Trim(), cols[1].Trim(), "", cols[2].Trim(), cols[3].Trim()));
        }
        return rows.Where(r => IsRealWingetId(r.Id)).ToList();
    }

    /// <summary>Installed version for a single winget Id, or null when unknown.</summary>
    public async Task<string?> GetInstalledVersionAsync(string wingetId)
    {
        try
        {
            string t = await RunAsync($"list --id \"{wingetId}\" --accept-source-agreements --disable-interactivity", 30_000);
            var row = ParseListOrUpgradeTable(t)
                .FirstOrDefault(r => string.Equals(r.Id, wingetId, StringComparison.OrdinalIgnoreCase));
            var v = row?.Installed?.Trim();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    public async Task<(bool Ok, int ExitCode, string Log)> UpgradeAsync(string wingetId, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("winget",
            $"upgrade --id \"{wingetId}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = new Process { StartInfo = psi };
        var log = new System.Text.StringBuilder();
        object logLock = new();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (logLock) log.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (logLock) log.AppendLine(e.Data); };
        try
        {
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            await p.WaitForExitAsync(ct);
            return (p.ExitCode == 0, p.ExitCode, log.ToString());
        }
        catch (Exception ex)
        {
            return (false, -1, log + "\n" + ex.Message);
        }
    }
}
