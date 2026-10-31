# UpdateLens.Win — Windows MVP (C# + XAML/WPF)

Inventory + update via WinGet. Registry scan supplements manual list.

## Prereqs
- .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`)
- Windows 10 1809+ with WinGet (App Installer)

## Run
```powershell
cd windows/UpdateLens.Win
dotnet build -c Release
dotnet run -c Release
# or
dotnet publish -c Release -o publish
./publish/UpdateLens.exe
```

## What works
- Scan: HKLM/HKCU Uninstall + `winget upgrade --include-unknown`
- Filter/search, Update one, Update All (sequential), Skip version, Cancel
- Non-winget apps show as Manual

## Limitations (by design for MVP)
- Store apps redirect not yet implemented — shown as Manual
- Table fallback truncates long names; Id is authoritative
- Requires winget for actual updates

## Next
- MS Store updates via Store API
- SQLite cache for ignore list persistence
- MSIX packaging + Task Scheduler background check
