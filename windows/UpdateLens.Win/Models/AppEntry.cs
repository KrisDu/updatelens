namespace UpdateLens.Win.Models;

public enum AppSource
{
    Winget,
    Store,
    System,
    Unknown
}

public enum UpdateState
{
    Unknown,
    UpToDate,
    UpdateAvailable,
    Manual,      // installed but not managed by winget/store
    Ignored,
    Updating,
    Failed,
    Updated
}

public sealed class AppEntry
{
    public string Id { get; set; } = string.Empty;          // winget Id or registry key / appx name
    public string Name { get; set; } = string.Empty;
    public string VersionInstalled { get; set; } = string.Empty;
    public string? VersionAvailable { get; set; }
    public string Vendor { get; set; } = string.Empty;
    public AppSource Source { get; set; } = AppSource.Unknown;
    public UpdateState State { get; set; } = UpdateState.Unknown;
    public string? VendorUrl { get; set; }
    public string? IgnoredVersion { get; set; }
    public string? ExePath { get; set; }      // local exe for icon extraction (registry apps)
    public string? IconPath { get; set; }     // cached png, filled post-scan
    public string? LastLog { get; set; }      // last update attempt log

    public bool CanUpdate => State == UpdateState.UpdateAvailable;
}
