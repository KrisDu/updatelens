using System.Diagnostics;
using Microsoft.Win32;

namespace UpdateLens.Win.Services;

/// <summary>Launch-at-login (HKCU Run) and daily background check (schtasks, user scope).</summary>
public static class SettingsService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "UpdateLens";
    private const string TaskName = "UpdateLens Daily Check";

    private static string ExePath
        => Environment.ProcessPath
           ?? System.Reflection.Assembly.GetExecutingAssembly().Location
           ?? "";

    public static bool GetLaunchAtLogin()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) != null;
        }
        catch { return false; }
    }

    public static void SetLaunchAtLogin(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (on) key.SetValue(RunValue, $"\"{ExePath}\" --check");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch { }
    }

    public static bool GetDailyCheck()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", $"/query /tn \"{TaskName}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static void SetDailyCheck(bool on)
    {
        try
        {
            string args = on
                ? $"/create /tn \"{TaskName}\" /tr \"\\\"{ExePath}\\\" --check\" /sc daily /st 09:30 /f"
                : $"/delete /tn \"{TaskName}\" /f";
            var psi = new ProcessStartInfo("schtasks", args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
        }
        catch { }
    }
}
