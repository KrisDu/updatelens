# UpdateLens

Desktop app that lists installed software and updates it — individually or all at once.

- `windows/UpdateLens.Win` — C# + XAML (WPF, .NET 8), WinGet-backed
- `macos/` — SwiftUI client (planned)

## Windows dev
```powershell
cd windows/UpdateLens.Win
dotnet build -c Release
dotnet run -c Release
```
