# UpdateLens

<img src="windows/UpdateLens.Win/Assets/logo.png" alt="UpdateLens logo" width="96"/>

Desktop app that lists installed software and updates it — individually or all at once.

- `windows/UpdateLens.Win` — C# + XAML (WPF, .NET 8), WinGet-backed, light + dark mode
- `macos/` — SwiftUI client (planned)

## What it does

- **Sees everything you installed** — registry apps merged with the winget catalog.
  Drivers, runtimes, SDKs and system components are filtered out; only real user apps show.
- **Update one or Update all** — per-row Update for anything with a newer version,
  or one click to update everything sequentially with a live progress bar.
- **Uninstalls too** — per-row Uninstall for winget-managed apps and anything with
  a registered uninstaller (quiet strings preferred, MSI made silent). Confirms first,
  elevates only the uninstaller itself, rescans to confirm it's gone.
- **Verifies every update** — re-reads the installed version after each upgrade and
  reports success (or "still on the old version") in a taskbar notification.
- **Adopts winget for manual installs** — apps you installed from the web (VS Code, VLC,
  OBS…) are matched to the winget catalog by name so they update in-app.
- **Manual apps get a way out** — vendor Site buttons with per-app guidance
  (Adobe CC, Office update path, Logitech software) for what winget can't touch.
- **Remembers your choices** — skipped versions and hidden apps persist across restarts.
- **Stays out of the way** — start-with-Windows and a daily background check from Settings,
  instant startup from a cached scan, cancelable operations with per-app logs on failure.

## Windows dev

Prereqs: .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`), Windows 10 1809+ with WinGet.

```powershell
cd windows/UpdateLens.Win
dotnet build -c Release
dotnet publish -c Release -o publish
./publish/UpdateLens.exe
```

See `windows/README.md` for architecture and service notes.

## How updates work

1. `winget list` + `winget upgrade` identify what's installed and what's outdated.
2. Registry-only apps are fuzzy-matched to the catalog; leftovers are looked up via
   `winget search` (exact name match only, results cached — misses are retried later).
3. Updates run as `winget upgrade --id … --silent`, one at a time, then verified
   against the newly installed version.

Only HTTPS package sources are used and nothing executes without your click.
All state stays on your machine under `%LocalAppData%\UpdateLens\`
(scan cache, icon cache, skips, settings, crash log) — no accounts, no telemetry.

## Roadmap

- Portable release + submission to `winget-pkgs` (repo must go public first)
- In-app self-update via GitHub Releases
- MS Store app updates, MSIX packaging
- macOS (SwiftUI) client
