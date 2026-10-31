# UpdateLens.Win — Windows client (C# + XAML/WPF, .NET 8)

## Run

```powershell
cd windows/UpdateLens.Win
dotnet build -c Release
dotnet publish -c Release -o publish
./publish/UpdateLens.exe
```

`publish/`, `publish-test/`, `bin/`, `obj/` are git-ignored build output.

## Architecture

MVVM, no external NuGet packages (WPF + WinForms shared frameworks only).

| Area | Files | Notes |
|---|---|---|
| Scan | `Services/RegistryScanner.cs`, `Services/WingetService.cs`, `Services/AppInventoryService.cs` | Registry uninstall keys + `winget list`/`upgrade` (run in parallel). No `--output json` — v1.29 is table-only, so tables are parsed. |
| Matching | `AppInventoryService.Norm/SearchQuery/PickAdoption`, `Services/WingetAdoptionCache.cs` | Registry → `winget list` fuzzy match, then exact-name `winget search` adoption (max ~15 fresh lookups/scan, 24h hit / 7d miss cache). |
| Filtering | `Services/AppFilter.cs` | Hides runtimes, SDKs, drivers, Xaml/VCLibs, NVIDIA/Intel, KB updates. Allowlist keeps Edge, Terminal, WSL, VS Code. |
| Manual apps | `Services/VendorMap.cs` | Fallback vendor URLs + guidance for what winget can't own (Adobe CC, Office C2R path, Logitech). |
| State | `Services/IgnoreStore.cs`, `Services/ScanCache.cs` | Skips/hides in `skips.json`; last scan in `last-scan.json` for instant startup. `%LocalAppData%\UpdateLens\` also holds icon cache, winget map, crash log. |
| Icons | `Services/IconService.cs` | Extracts exe icons via DisplayIcon/UninstallString, caches PNGs by hash. |
| Notifications | `Services/NotifyService.cs` | WinForms balloon tips (no WinRT dependency). |
| Settings | `Services/SettingsService.cs`, `Views/SettingsWindow.*` | HKCU Run key (launch at login), `schtasks` daily check (`--check` auto-scan + toast). |
| Theming | `App.xaml` resources + `Services/ThemeService.cs` | Light/dark/system via DynamicResource swap. Note: all row text must set Foreground explicitly — ListViewItem defaults to black and breaks inheritance. Inline values beat trigger setters, so themed control states live in Styles. |
| Safety | `App.xaml.cs`, `ViewModels/RelayCommand.cs` | Global crash log + dialog (app stays open), async commands never crash the process, thread-safe winget output capture. |

## Conventions that bit us before

- `Run.Text` bindings default to TwoWay and crash on read-only props — always `Mode=OneWay`.
- `Content="{x}" StringFormat=…` on a Button is ignored — use `ContentStringFormat`.
- Never `Stop-Process -Name UpdateLens` on this machine — it kills the user's live app. Always stop by PID.
- Publish to `publish-test/` for checks; `publish/` is what the user runs (file lock while open).
