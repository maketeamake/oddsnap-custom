# Contributing

Thank you for improving OddSnap Custom. This repository is an independently
maintained derivative of OddSnap; please report upstream-only issues to the
[original project](https://github.com/jasperdevs/odd-snap).

## Development environment

- Windows 10 or Windows 11
- The .NET SDK selected by `global.json`
- Visual Studio 2022 or another C# editor is optional

Restore the locked dependency graph before building:

```powershell
dotnet restore OddSnap.sln --locked-mode
```

## Required checks

Run the same checks as CI before opening a pull request:

```powershell
dotnet format whitespace OddSnap.sln --verify-no-changes --no-restore
dotnet format style OddSnap.sln --verify-no-changes --severity warn --no-restore
dotnet format analyzers OddSnap.sln --verify-no-changes --severity warn --no-restore
dotnet list OddSnap.sln package --vulnerable --include-transitive
dotnet build OddSnap.sln -c Release --no-restore -warnaserror
dotnet test src/OddSnap.Tests/OddSnap.Tests.csproj -c Release --no-build
```

New behavior should include focused tests where the logic can run without
opening windows, registering global hotkeys, capturing the screen, or touching
real user settings and history.

## Pull requests

- Keep changes focused and explain the user-visible behavior.
- Do not commit captures, local settings, logs, credentials, build output, or
  generated installers.
- Preserve compatibility with existing settings and history unless the pull
  request includes an explicit migration.
- Keep UI work responsive: disk, image processing, and network operations must
  not block the WPF dispatcher.
