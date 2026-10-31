namespace UpdateLens.Win.Services;

/// <summary>Fallback vendor links + guidance for apps that can never be in winget.</summary>
public static class VendorMap
{
    private static readonly (string Match, string Url, string Guidance)[] Entries = new[]
    {
        ("action1", "https://www.action1.com/", "Updates from the Action1 console (managed device)."),
        ("adobe", "https://helpx.adobe.com/creative-cloud/help/update-apps.html", "Update via Creative Cloud Desktop > Updates."),
        ("logi download assistant", "https://www.logitech.com/software", "Helper owned by Logi Options+ / G Hub — update that app."),
        ("logi plugin service", "https://www.logitech.com/software", "Helper owned by Logi Options+ / G Hub — update that app."),
        ("logitech", "https://www.logitech.com/software", "Update via Logi Options+ / G Hub."),
        ("office", "https://aka.ms/office-update", "Office: File > Account > Update Options > Update Now."),
        ("teams meeting add-in", "https://www.microsoft.com/microsoft-teams/download-app", "Reinstalled/updated with Teams."),
    };

    public static bool TryGet(string? appName, out string url, out string guidance)
    {
        url = ""; guidance = "";
        if (string.IsNullOrWhiteSpace(appName)) return false;
        var n = appName.ToLowerInvariant();
        foreach (var (match, u, g) in Entries)
            if (n.Contains(match)) { url = u; guidance = g; return true; }
        return false;
    }
}
