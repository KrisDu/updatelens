using System.Diagnostics;

namespace UpdateLens.Win.Services;

/// <summary>Uninstalls winget-managed apps via CLI, registry apps via their
/// QuietUninstallString / UninstallString (elevated). Never guesses flags.</summary>
public static class UninstallService
{
    public static bool CanUninstall(Models.AppEntry e)
        => (e.Source == Models.AppSource.Winget && WingetService.IsRealWingetId(e.Id))
           || !string.IsNullOrWhiteSpace(e.QuietUninstallCommand)
           || !string.IsNullOrWhiteSpace(e.UninstallCommand);

    public static async Task<(bool Ok, int ExitCode, string Log)> UninstallAsync(
        Models.AppEntry entry, WingetService winget, CancellationToken ct)
    {
        if (entry.Source == Models.AppSource.Winget && WingetService.IsRealWingetId(entry.Id))
            return await winget.UninstallAsync(entry.Id, ct);

        string? cmd = entry.QuietUninstallCommand ?? entry.UninstallCommand;
        bool quiet = entry.QuietUninstallCommand != null;
        if (string.IsNullOrWhiteSpace(cmd))
            return (false, -1, "No uninstall command registered for this app.");

        var split = SplitCommand(cmd);
        if (split is null)
            return (false, -1, "Could not parse uninstall command: " + cmd);
        var (file, args) = split.Value;

        // MSI via the loud string: make it silent. Quiet string is used as-is.
        if (!quiet && IsMsiExec(file, args) && !args.Contains("/qn", StringComparison.OrdinalIgnoreCase))
            args += " /qn /norestart";

        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = true, // required for elevation
                Verb = "runas",
            };
            using var p = Process.Start(psi);
            if (p is null) return (false, -1, "Uninstaller did not start.");
            await p.WaitForExitAsync(ct);
            bool ok = p.ExitCode == 0 || p.ExitCode == 3010; // 3010 = success, reboot required
            return (ok, p.ExitCode, $"Ran: {file} {args}\nExit code: {p.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, 1223, "Cancelled at the elevation prompt.");
        }
        catch (Exception ex)
        {
            return (false, -1, ex.Message);
        }
    }

    private static bool IsMsiExec(string file, string args)
        => file.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase)
           || file.Equals("MsiExec.exe", StringComparison.OrdinalIgnoreCase)
           || args.TrimStart().StartsWith("/X", StringComparison.OrdinalIgnoreCase)
           || args.TrimStart().StartsWith("/I", StringComparison.OrdinalIgnoreCase);

    private static (string File, string Args)? SplitCommand(string cmd)
    {
        var s = cmd.Trim();
        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            if (end < 1) return null;
            return (s[1..end], s[(end + 1)..].Trim());
        }
        int space = s.IndexOf(' ');
        if (space < 0) return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? (s, "") : null;
        var file = s[..space];
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith("MsiExec.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file, "MsiExec.exe", StringComparison.OrdinalIgnoreCase))
            return (file, s[(space + 1)..]);
        return null;
    }
}
