# VS Offline Setup Utility

A lightweight Windows Forms utility for downloading offline installation
files for Visual Studio 2019 / 2022 / 2026, and for cleaning up old-version
files left behind in an offline layout folder.

Built on **.NET Framework 4.8.1**, **WinForms**, with **zero NuGet
dependencies** — only in-box .NET Framework assemblies are referenced.

> This project supersedes [Visual-Studio-2026-Offline-Installer-Script](https://github.com/MEHDIMYADI/Visual-Studio-2026-Offline-Installer-Script).
> Please use this app instead of the old script.

## Features

- **Download tab**
  - Pick a Visual Studio edition (2019 / 2022 / 2026, Community / Professional / Enterprise).
  - Workload and component list is fetched live from Microsoft's official
    [`visualstudio-docs`](https://github.com/MicrosoftDocs/visualstudio-docs) repository, so it always
    reflects the current workload/component IDs.
  - Three-state checkbox tree: checking a workload implicitly includes its
    `Required` components; `Recommended` / `Optional` components are
    additionally included only when the matching global toggle is on.
    Independent components are only included when clicked directly.
  - If nothing is checked, no `--add` switch is generated at all, which
    makes the Visual Studio bootstrapper fall back to its full default
    layout (i.e. **all workload packages will be installed**) — same
    behavior as the original utility.
  - Generates the exact CLI command that will be run, and lets you review
    it before downloading.
  - Downloads the bootstrapper `.exe` and writes a `CliCommand.bat` next to
    it, then runs it.

- **Cleanup tab**
  - Scans an offline layout folder for module folders that are superseded
    by a newer version of the same module (folder names such as
    `Android.Manifest-10.0.100.36.1.2,version=36.1.2,machinearch=x64`).
    Architecture variants (e.g. `x64` vs `x86`) of the same version are
    correctly treated as separate, both required, folders — not
    duplicates.
  - Lets you review and delete the discovered old-version folders. If none
    are individually unchecked, all discovered folders are deleted (same
    behavior as the original utility, which had no per-item selection UI
    at all).
  - Optional "Run Visual Studio `--clean`" button that uses Visual Studio's
    own official cleanup mechanism against `Catalog.json` in the layout
    folder, if present.

- **Shared, persisted settings**
  - The offline layout folder path is shared between both tabs — pick it
    once, and it stays in sync.
  - Path, selected edition, language, and Recommended/Optional toggles are
    remembered between runs (`settings.txt` next to the `.exe`, plain
    `Key=Value` text — no external serialization library involved).
  - Both folder path boxes are plain editable text boxes, so you can also
    type or paste a path directly instead of using the folder picker.

## Requirements

- Windows with **.NET Framework 4.8.1** installed (comes preinstalled on
  current Windows 10/11 updates).
- An internet connection (for fetching the workload list and downloading
  setup files).

## Building

Open `VSOfflineTool.csproj` in Visual Studio 2019+ and build. There is
**no NuGet restore step** — the project only references standard .NET
Framework assemblies (`System.Net.Http`, `System.Windows.Forms`,
`System.Drawing`, etc.).

```
git clone https://github.com/MEHDIMYADI/VSOfflineTool.git
```

Then open the `.csproj` in Visual Studio and press **Build**.

## Project layout

| File | Purpose |
|---|---|
| `Program.cs` | Application entry point |
| `MainForm.cs` | The single WinForms window (Download + Cleanup tabs) |
| `Models.cs` | Plain data classes: `VsEdition`, `Workload`, `Component`, `VsModule`, dependency logic |
| `VsEditionCatalog.cs` | Static list of supported VS editions and their download/workload-doc URLs |
| `MarkdownWorkloadParser.cs` | Hand-written parser for Microsoft's workload/component Markdown tables |
| `CleanupHelper.cs` | Finds and deletes superseded module folders in an offline layout |
| `SettingsStore.cs` | Loads/saves persisted settings as plain text (no external library) |

## Disclaimer

This application is provided as is, without any warranty of any kind. It
is not affiliated with Microsoft or any other third party. No user data
is collected by this application.

## License

MIT — see [LICENSE](LICENSE).
