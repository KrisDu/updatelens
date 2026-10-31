using System;

namespace UpdateLens.Win.Services;

/// <summary>Best-effort version comparison for Win32 display versions.</summary>
public static class VersionHelper
{
    public static int Compare(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return 0;
        if (string.IsNullOrWhiteSpace(a)) return -1;
        if (string.IsNullOrWhiteSpace(b)) return 1;

        // Strip build metadata / suffixes like "-beta", "+rev"
        var cleanA = a.Trim().Split(new[] { '-', '+' }, 2)[0];
        var cleanB = b.Trim().Split(new[] { '-', '+' }, 2)[0];

        var partsA = cleanA.Split('.');
        var partsB = cleanB.Split('.');
        int len = Math.Max(partsA.Length, partsB.Length);
        for (int i = 0; i < len; i++)
        {
            long x = i < partsA.Length && long.TryParse(partsA[i], out var px) ? px : 0;
            long y = i < partsB.Length && long.TryParse(partsB[i], out var py) ? py : 0;
            if (x != y) return x.CompareTo(y);
        }
        return string.Compare(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNewer(string? available, string? installed)
        => Compare(available, installed) > 0;
}
