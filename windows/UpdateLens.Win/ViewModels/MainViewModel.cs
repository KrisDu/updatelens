using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using UpdateLens.Win.Models;
using UpdateLens.Win.Services;

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

    public UpdateState State
    {
        get => Entry.State;
        set { Entry.State = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(CanUpdate)); }
    }

    public string StatusText => State switch
    {
        UpdateState.UpdateAvailable => $"Update: {VersionInstalled} → {VersionAvailable}",
        UpdateState.UpToDate => "Up to date",
        UpdateState.Manual => HasVendorUrl ? "Manual — vendor site available" : "Manual / not managed",
        UpdateState.Ignored => $"Skipped {Entry.IgnoredVersion}",
        UpdateState.Updating => "Updating…",
        UpdateState.Failed => "Failed — retry?",
        UpdateState.Updated => "Updated",
        _ => VersionInstalled,
    };

    public bool CanUpdate => State == UpdateState.UpdateAvailable || State == UpdateState.Failed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AppInventoryService _inventory = new();
    private readonly WingetService _winget = new();
    private CancellationTokenSource? _cts;

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

    private string _lastChecked = "Never";
    public string LastChecked { get => _lastChecked; set { _lastChecked = value; OnPropertyChanged(); } }

    public int UpdateCount => Apps.Count(a => a.State == UpdateState.UpdateAvailable);

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand UpdateAllCommand { get; }
    public AsyncRelayCommand UpdateOneCommand { get; }
    public RelayCommand SkipCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenVendorCommand { get; }

    public MainViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        UpdateAllCommand = new AsyncRelayCommand(_ => UpdateAllAsync(), _ => Apps.Any(a => a.State == UpdateState.UpdateAvailable));
        UpdateOneCommand = new AsyncRelayCommand(p => UpdateOneAsync(p as AppRow));
        SkipCommand = new RelayCommand(p =>
        {
            if (p is AppRow r && !string.IsNullOrWhiteSpace(r.VersionAvailable))
            {
                r.Entry.IgnoredVersion = r.VersionAvailable;
                r.State = UpdateState.Ignored;
                RefreshCounts();
                ApplyFilter();
            }
        });
        CancelCommand = new RelayCommand(_ => { try { _cts?.Cancel(); } catch { } });
        OpenVendorCommand = new RelayCommand(p =>
        {
            if (p is AppRow r && !string.IsNullOrWhiteSpace(r.VendorUrl))
            {
                try { Process.Start(new ProcessStartInfo(r.VendorUrl.Trim()) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show("Could not open vendor site:\n" + ex.Message, "UpdateLens", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }, p => p is AppRow r && r.HasVendorUrl);
    }

    public async Task RefreshAsync()
    {
        if (Busy) return;
        Busy = true; Status = "Scanning installed apps + winget upgrades…";
        try
        {
            var list = await _inventory.RefreshAsync();
            Apps.Clear();
            foreach (var e in list) Apps.Add(new AppRow(e));
            LastChecked = DateTime.Now.ToString("HH:mm:ss");
            if (!_inventory.WingetAvailable)
                Status = $"Found {Apps.Count} apps. winget not found — updates limited to manual. Install WinGet / App Installer.";
            else
                Status = $"Found {Apps.Count} apps, {UpdateCount} with updates ({_inventory.PromotedCount} matched to winget).";
            RefreshCounts();
            ApplyFilter();
        }
        catch (Exception ex) { Status = "Scan failed: " + ex.Message; }
        finally { Busy = false; }
    }

    private async Task UpdateOneAsync(AppRow? row)
    {
        if (row is null || !row.CanUpdate) return;
        _cts = new CancellationTokenSource();
        row.State = UpdateState.Updating;
        Status = $"Updating {row.Name}…";
        var (ok, code, log) = await _winget.UpgradeAsync(row.Id, _cts.Token);
        if (ok) { row.State = UpdateState.Updated; Status = $"{row.Name} updated."; }
        else
        {
            row.State = UpdateState.Failed;
            Status = $"{row.Name} failed (exit {code}).";
            MessageBox.Show($"{row.Name} failed.\nExit {code}\n\n{Trim(log, 1500)}", "Update failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshCounts();
        ApplyFilter();
    }

    private async Task UpdateAllAsync()
    {
        var pending = Apps.Where(a => a.State == UpdateState.UpdateAvailable).ToList();
        if (pending.Count == 0) return;
        var res = MessageBox.Show($"Update {pending.Count} apps sequentially?", "Update All",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        _cts = new CancellationTokenSource();
        int okCount = 0, failCount = 0;
        foreach (var row in pending)
        {
            if (_cts.IsCancellationRequested) break;
            row.State = UpdateState.Updating;
            Status = $"Updating {row.Name} ({okCount + failCount + 1}/{pending.Count})…";
            var (ok, _, _) = await _winget.UpgradeAsync(row.Id, _cts.Token);
            row.State = ok ? UpdateState.Updated : UpdateState.Failed;
            if (ok) okCount++; else failCount++;
            RefreshCounts();
        }
        Status = $"Done: {okCount} updated, {failCount} failed.";
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
