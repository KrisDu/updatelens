using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using UpdateLens.Win.Models;
using UpdateLens.Win.Services;
using UpdateLens.Win.Views;

namespace UpdateLens.Win.ViewModels;

public sealed class AppRow : INotifyPropertyChanged
{
    public AppEntry Entry { get; }
    public AppRow(AppEntry entry) { Entry = entry; }

    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string Vendor => Entry.Vendor;
    public AppSource Source => Entry.Source;
    public string? VendorUrl => Entry.VendorUrl;
    public bool HasVendorUrl => !string.IsNullOrWhiteSpace(Entry.VendorUrl);

    public string VersionInstalled => Entry.VersionInstalled;
    public string? VersionAvailable
    {
        get => Entry.VersionAvailable;
        set { Entry.VersionAvailable = value; OnPropertyChanged(); }
    }

    public string? IconPath
    {
        get => Entry.IconPath;
        set { Entry.IconPath = value; OnPropertyChanged(); }
    }

    public string? LastLog
    {
        get => Entry.LastLog;
        set { Entry.LastLog = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasFailureLog)); }
    }

    public bool HasFailureLog => State == UpdateState.Failed && !string.IsNullOrWhiteSpace(Entry.LastLog);

    public string Initials
    {
        get
        {
            var words = Name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            if (words.Length == 1)
                return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
            return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        }
    }

    public string StatusTone => State switch
    {
        UpdateState.UpdateAvailable or UpdateState.Updating => "Blue",
        UpdateState.UpToDate or UpdateState.Updated => "Green",
        UpdateState.Failed => "Red",
        _ => "Gray",
    };

    public UpdateState State
    {
        get => Entry.State;
        set { Entry.State = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(CanUpdate)); OnPropertyChanged(nameof(StatusTone)); OnPropertyChanged(nameof(HasFailureLog)); }
    }

    public string StatusText => State switch
    {
        UpdateState.UpdateAvailable => $"Update: {VersionInstalled} → {VersionAvailable}",
        UpdateState.UpToDate => "Up to date",
        UpdateState.Manual => HasVendorUrl ? "Manual — vendor site available" : "Manual / not managed",
        UpdateState.Ignored => $"Skipped {Entry.IgnoredVersion}",
        UpdateState.Updating => "Updating…",
        UpdateState.Failed => "Failed — see log",
        UpdateState.Updated => "Updated",
        _ => VersionInstalled,
    };

    public bool CanUpdate => State == UpdateState.UpdateAvailable || State == UpdateState.Failed;

    /// <summary>Call after Entry.VersionInstalled changes in place.</summary>
    public void RefreshVersions()
    {
        OnPropertyChanged(nameof(VersionInstalled));
        OnPropertyChanged(nameof(StatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AppInventoryService _inventory = new();
    private readonly WingetService _winget = new();
    private readonly IgnoreStore _ignores = new();
    private CancellationTokenSource? _cts;
    private bool _checkToastShown;

    public ObservableCollection<AppRow> Apps { get; } = new();
    public ObservableCollection<AppRow> Filtered { get; } = new();

    private string _search = "";
    public string Search
    {
        get => _search;
        set { _search = value; OnPropertyChanged(); ApplyFilter(); }
    }

    private string _filter = "All";
    public string Filter
    {
        get => _filter;
        set { _filter = value; OnPropertyChanged(); ApplyFilter(); }
    }
    public List<string> Filters { get; } = new() { "All", "Updates Available", "Up to date", "Manual" };

    private string _status = "Click Refresh to scan.";
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    private bool _busy;
    public bool Busy { get => _busy; set { _busy = value; OnPropertyChanged(); } }

    private int _doneCount;
    public int DoneCount { get => _doneCount; set { _doneCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressValue)); OnPropertyChanged(nameof(ProgressText)); } }

    private int _totalCount;
    public int TotalCount { get => _totalCount; set { _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressValue)); OnPropertyChanged(nameof(ProgressText)); } }

    public double ProgressValue => TotalCount == 0 ? 0 : (double)DoneCount / TotalCount * 100;
    public string ProgressText => TotalCount == 0 ? "" : $"{DoneCount}/{TotalCount}";

    private string _lastChecked = "Never";
    public string LastChecked { get => _lastChecked; set { _lastChecked = value; OnPropertyChanged(); } }

    public bool IsDark
    {
        get => ThemeService.IsDark;
        set { ThemeService.SetDark(value); OnPropertyChanged(); }
    }

    public int UpdateCount => Apps.Count(a => a.State == UpdateState.UpdateAvailable);

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand UpdateAllCommand { get; }
    public AsyncRelayCommand UpdateOneCommand { get; }
    public RelayCommand SkipCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenVendorCommand { get; }
    public RelayCommand ShowLogCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public AsyncRelayCommand ResetSkipsCommand { get; }

    public MainViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        UpdateAllCommand = new AsyncRelayCommand(_ => UpdateAllAsync(), _ => Apps.Any(a => a.State == UpdateState.UpdateAvailable));
        UpdateOneCommand = new AsyncRelayCommand(p => UpdateOneAsync(p as AppRow));
        SkipCommand = new RelayCommand(p =>
        {
            if (p is not AppRow r) return;
            if (!string.IsNullOrWhiteSpace(r.VersionAvailable))
            {
                // Versioned skip: persists across restarts.
                _ignores.Ignore(r.Id, r.VersionAvailable);
                r.Entry.IgnoredVersion = r.VersionAvailable;
                r.State = UpdateState.Ignored;
                Status = $"Skipped {r.Name} {r.VersionAvailable}. Reset in Settings.";
            }
            else
            {
                // Manual row: hide it persistently.
                _ignores.Hide(AppInventoryService.Norm(r.Name));
                Apps.Remove(r);
                Status = $"Hidden {r.Name}. Reset in Settings to show it again.";
            }
            RefreshCounts();
            ApplyFilter();
        });
        CancelCommand = new RelayCommand(_ => { try { _cts?.Cancel(); } catch { } });
        OpenVendorCommand = new RelayCommand(p =>
        {
            if (p is AppRow r && !string.IsNullOrWhiteSpace(r.VendorUrl))
            {
                try { Process.Start(new ProcessStartInfo(r.VendorUrl.Trim()) { UseShellExecute = true }); }
                catch (Exception ex) { System.Windows.MessageBox.Show("Could not open vendor site:\n" + ex.Message, "UpdateLens", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }, p => p is AppRow r && r.HasVendorUrl);
        ShowLogCommand = new RelayCommand(p =>
        {
            if (p is AppRow r && !string.IsNullOrWhiteSpace(r.LastLog))
                System.Windows.MessageBox.Show(Trim(r.LastLog, 3000), $"{r.Name} — update log", MessageBoxButton.OK, MessageBoxImage.Information);
        }, p => p is AppRow r && r.HasFailureLog);
        SettingsCommand = new RelayCommand(p =>
        {
            var dlg = new SettingsWindow();
            if (p is Window owner) dlg.Owner = owner;
            if (dlg.ShowDialog() == true) _ = RefreshAsync();
            OnPropertyChanged(nameof(IsDark)); // theme may have changed in Settings
        });
        ResetSkipsCommand = new AsyncRelayCommand(_ =>
        {
            _ignores.Reset();
            Status = "Cleared all skips and hidden apps.";
            return RefreshAsync();
        });
    }

    public async Task RefreshAsync()
    {
        if (Busy) return;
        Busy = true; DoneCount = 0; TotalCount = 0;
        try
        {
            // Instant: show the last scan while the live one runs.
            if (!_checkToastShown && Apps.Count == 0)
            {
                var cached = await Task.Run(() => ScanCache.Load());
                if (cached is { Apps.Count: > 0 })
                {
                    AppInventoryService.ApplyStores(cached.Value.Apps, _ignores);
                    foreach (var e in cached.Value.Apps) Apps.Add(new AppRow(e));
                    LastChecked = cached.Value.When.ToString("HH:mm:ss") + " (cached)";
                    Status = $"Showing last scan — refreshing…";
                    RefreshCounts();
                    ApplyFilter();
                    _ = LoadIconsAsync();
                }
            }
            Status = "Scanning apps + matching winget catalog (first run slower)…";
            var list = await _inventory.RefreshAsync(_ignores);
            ScanCache.Save(list);
            Apps.Clear();
            foreach (var e in list) Apps.Add(new AppRow(e));
            LastChecked = DateTime.Now.ToString("HH:mm:ss");
            if (!_inventory.WingetAvailable)
                Status = $"Found {Apps.Count} apps. winget not found — updates limited to manual. Install WinGet / App Installer.";
            else
                Status = $"Found {Apps.Count} apps, {UpdateCount} with updates ({_inventory.PromotedCount} matched, {_inventory.AdoptedCount} via search).";
            RefreshCounts();
            ApplyFilter();
            if (App.CheckMode && !_checkToastShown)
            {
                _checkToastShown = true;
                if (UpdateCount > 0)
                    NotifyService.Balloon("UpdateLens", $"{UpdateCount} app updates available.", System.Windows.Forms.ToolTipIcon.Info);
            }
            // Icons load in the background so the list shows fast.
            _ = LoadIconsAsync();
        }
        catch (Exception ex) { Status = "Scan failed: " + ex.Message; }
        finally { Busy = false; }
    }

    private async Task LoadIconsAsync()
    {
        try
        {
            var pending = Apps.Where(a => a.IconPath == null && !string.IsNullOrWhiteSpace(a.Entry.ExePath)).ToList();
            var filled = new List<(AppRow Row, string Path)>();
            foreach (var row in pending)
            {
                var icon = await IconService.GetIconAsync(row.Entry.ExePath);
                if (icon != null) filled.Add((row, icon));
            }
            foreach (var (row, path) in filled) row.IconPath = path;
        }
        catch { }
    }

    private async Task UpdateOneAsync(AppRow? row)
    {
        if (row is null || !row.CanUpdate) return;
        _cts = new CancellationTokenSource();
        Busy = true; DoneCount = 0; TotalCount = 1;
        try
        {
            row.State = UpdateState.Updating;
            Status = $"Updating {row.Name}…";
            var before = row.VersionInstalled;
            var (ok, code, log) = await _winget.UpgradeAsync(row.Id, _cts.Token);
            row.LastLog = log;
            if (!ok)
            {
                row.State = UpdateState.Failed;
                Status = $"{row.Name} failed (exit {code}). Open Log for details.";
                NotifyService.Balloon("Update failed", $"{row.Name} (exit {code}).", System.Windows.Forms.ToolTipIcon.Error);
            }
            else
            {
                var after = await _winget.GetInstalledVersionAsync(row.Id);
                if (after != null && VersionHelper.IsNewer(after, before))
                {
                    row.Entry.VersionInstalled = after;
                    row.VersionAvailable = null;
                    row.State = UpdateState.Updated;
                    Status = $"{row.Name} updated to {after}.";
                    NotifyService.Balloon("Update successful", $"{row.Name} {before} → {after}.", System.Windows.Forms.ToolTipIcon.Info);
                }
                else if (after != null && after == before)
                {
                    row.State = UpdateState.Failed;
                    Status = $"{row.Name}: installer finished but version is still {before}.";
                    NotifyService.Balloon("Update unverified", $"{row.Name} still reports {before}.", System.Windows.Forms.ToolTipIcon.Warning);
                }
                else
                {
                    row.State = UpdateState.Updated;
                    Status = $"{row.Name} updated.";
                    NotifyService.Balloon("Update successful", $"{row.Name} updated.", System.Windows.Forms.ToolTipIcon.Info);
                }
                row.RefreshVersions();
            }
        }
        finally { Busy = false; DoneCount = 0; TotalCount = 0; }
        RefreshCounts();
        ApplyFilter();
    }

    private async Task UpdateAllAsync()
    {
        var pending = Apps.Where(a => a.State == UpdateState.UpdateAvailable).ToList();
        if (pending.Count == 0) return;
        var res = System.Windows.MessageBox.Show($"Update {pending.Count} apps sequentially?", "Update All",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        _cts = new CancellationTokenSource();
        Busy = true; DoneCount = 0; TotalCount = pending.Count;
        int okCount = 0, failCount = 0;
        try
        {
            foreach (var row in pending)
            {
                if (_cts.IsCancellationRequested) break;
                row.State = UpdateState.Updating;
                Status = $"Updating {row.Name} ({DoneCount + 1}/{pending.Count})…";
                var before = row.VersionInstalled;
                var (ok, code, log) = await _winget.UpgradeAsync(row.Id, _cts.Token);
                row.LastLog = log;
                if (!ok)
                {
                    row.State = UpdateState.Failed;
                    failCount++;
                }
                else
                {
                    var after = await _winget.GetInstalledVersionAsync(row.Id);
                    if (after != null && !VersionHelper.IsNewer(after, before) && after == before)
                    {
                        row.State = UpdateState.Failed;
                        failCount++;
                    }
                    else
                    {
                        if (after != null) row.Entry.VersionInstalled = after;
                        row.VersionAvailable = null;
                        row.State = UpdateState.Updated;
                        row.RefreshVersions();
                        okCount++;
                    }
                }
                DoneCount++;
                RefreshCounts();
            }
        }
        finally { Busy = false; DoneCount = 0; TotalCount = 0; }
        Status = _cts.IsCancellationRequested
            ? $"Cancelled: {okCount} updated, {failCount} failed."
            : $"Done: {okCount} updated, {failCount} failed.";
        NotifyService.Balloon("UpdateLens",
            $"{okCount} updated, {failCount} failed.",
            failCount == 0 ? System.Windows.Forms.ToolTipIcon.Info : System.Windows.Forms.ToolTipIcon.Warning);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<AppRow> q = Apps;
        if (!string.IsNullOrWhiteSpace(Search))
            q = q.Where(a => a.Name.Contains(Search, StringComparison.OrdinalIgnoreCase));
        q = Filter switch
        {
            "Updates Available" => q.Where(a => a.State == UpdateState.UpdateAvailable),
            "Up to date" => q.Where(a => a.State == UpdateState.UpToDate || a.State == UpdateState.Updated),
            "Manual" => q.Where(a => a.State == UpdateState.Manual || a.State == UpdateState.Unknown),
            _ => q,
        };
        Filtered.Clear();
        foreach (var r in q) Filtered.Add(r);
        OnPropertyChanged(nameof(UpdateCount));
        UpdateAllCommand.RaiseCanExecuteChanged();
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(UpdateCount));
        UpdateAllCommand.RaiseCanExecuteChanged();
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[^max..];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
