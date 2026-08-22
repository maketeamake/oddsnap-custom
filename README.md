# OddSnap Custom

OddSnap Custom is an independent, workflow-focused fork of
[OddSnap](https://github.com/jasperdevs/odd-snap), a free and open-source
screenshot tool for Windows.

This repository contains a substantially modified editor and capture workflow.
It is maintained independently by [maketeamake](https://github.com/maketeamake)
and is not affiliated with or endorsed by the upstream OddSnap project.

## What is different

- `F10` and `Alt+~` start region capture.
- A new capture is copied to the clipboard immediately and opened in the
  integrated Library editor.
- The Library combines capture history, date/application filters, a filmstrip,
  and non-destructive annotation editing.
- Arrow, pencil, highlight, text, shape, step, blur, crop, and cut-out tools are
  available from one toolbar.
- Tool colors and presets are independent and persist between uses.
- Text blocks can be edited, moved, resized, wrapped, and styled after creation.
- Existing annotations can be selected, moved, resized, recolored, deleted, and
  multi-selected with `Shift`.
- Crop and cut-out operations support undo/redo; `Shift` constrains arrows to a
  horizontal or vertical axis.
- `Enter` or `Ctrl+C` copies the current image and minimizes the Library.

The project also retains the broader capture, OCR, recording, upload, and
utility functionality inherited from OddSnap.

## Build and test

Requirements:

- Windows 10 or Windows 11
- The [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
  selected by `global.json`

```powershell
dotnet restore OddSnap.sln --locked-mode
dotnet format whitespace OddSnap.sln --verify-no-changes --no-restore
dotnet format style OddSnap.sln --verify-no-changes --severity warn --no-restore
dotnet format analyzers OddSnap.sln --verify-no-changes --severity warn --no-restore
dotnet build OddSnap.sln -c Release --no-restore
dotnet test src/OddSnap.Tests/OddSnap.Tests.csproj -c Release --no-build
```

To publish a self-contained x64 build:

```powershell
dotnet publish src/OddSnap/OddSnap.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

No installer or binary release is currently provided by this repository. The
source tree intentionally excludes generated builds, captures, personal files,
local configuration, marketing screenshots, and the upstream website bundle.
Runtime icons and provider logos required to build and use the application are
retained.

## Attribution and license

OddSnap Custom is based on OddSnap by JasperDevs. See [NOTICE.md](NOTICE.md) for
the upstream source and modification notice.

The complete work is distributed under the
[GNU General Public License v3.0 or later](LICENSE), as required by the upstream
license. Copyright in upstream code remains with its original contributors;
copyright in later modifications remains with their respective contributors.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the required quality checks and
[SECURITY.md](SECURITY.md) for private vulnerability reporting.
