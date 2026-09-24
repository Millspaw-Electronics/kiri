# KiRI for Windows

A native Windows version of KiRI: a C# (.NET 8) port of the `bin/kiri` generator and a desktop app that shows the viewer (`assets/`) in a WebView2 window. It replaces the bash version on Windows; the bash version is still the one to use on Linux and macOS.

## Projects

| Folder | Output | What it is |
|---|---|---|
| `Kiri.Core` | library | The generator: finds `git.exe` and `kicad-cli.exe`, lists commits, exports each commit's KiCad files, plots them and assembles the viewer. |
| `Kiri.Cli` | `kiri-cli.exe` | Command-line front end with the bash version's main options (`-g`, `-r`, `-d`, `-a`, `-t`, `-n`, `-o`, `-u`, `-x`). |
| `Kiri.App` | `KiRI.exe` | WinForms desktop app. `ui/` holds its start page; the viewer and generated files are served to WebView2 through virtual host names, so no local web server runs. |
| `installer` | `KiRI-Setup-<version>.exe` | Inno Setup script. Installs per user (no admin rights) by default. |

## How the generator works

For each run the generator:

1. Finds the `.kicad_pro`, the repository root and the project's folder inside the repository.
2. Lists commits with one `git log --name-only --relative` call over all local and remote branches, keeping the commits that change a `.kicad_sch` or `.kicad_pcb` in the project folder. Uncommitted changes become the `_local_` commit.
3. For each commit not already plotted, streams `git archive <hash>:<project folder>` and keeps only the KiCad files (`.kicad_pro`, `.kicad_sch`, `.kicad_pcb`, `.kicad_wks`, `.kicad_dru`, library tables). Git LFS pointers are resolved with `git lfs smudge`.
4. Reads the sheet hierarchy, layer table and title blocks with a small S-expression parser (`SExpr.cs`), and expands KiCad text variables from the `.kicad_pro`.
5. Runs one `kicad-cli sch export svg` and one `kicad-cli pcb export svg --mode-multi` per commit, several commits at a time, and renames the output to what the viewer expects.
6. Copies the viewer into `web/` and fills in the commit, page and layer lists and the page header.

The output layout is the same as the bash version's (`<hash>/_KIRI_/sch/*.svg`, `pcb/layer-NN.svg`, `sch_sheets`, `pcb_layers`, `web/index.html`), so both versions share `assets/`. Finished commits are marked with `_KIRI_/.done` and reused; `_local_` is always plotted again. The default output folder is `%LOCALAPPDATA%\kiri\<project>-<id>`.

A full build of a 4-commit, 29-layer KiCad 10 project takes about 5 seconds.

## Building

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`).

```powershell
dotnet build windows\Kiri.sln
windows\Kiri.App\bin\Debug\net8.0-windows\KiRI.exe [folder or .kicad_pro]
windows\Kiri.Cli\bin\Debug\net8.0\kiri-cli.exe --help
```

Set `KIRI_DEVTOOLS=1` before starting `KiRI.exe` to enable the WebView2 developer tools (F12) and context menu.

To build the installer, also install Inno Setup 6 (`winget install JRSoftware.InnoSetup`), then run:

```powershell
windows\build.ps1 -Version 1.0.0
```

It publishes both programs self-contained for x64, so users don't need .NET, and writes `windows\dist\KiRI-Setup-1.0.0.exe` (about 50 MB). The installer isn't code-signed, so Windows SmartScreen warns about it the first time it runs.

## Testing

There are no automated tests yet. To check a change, build a project with `kiri-cli` and compare its output with the bash version's (see "Testing" in `docs/PORT_PLAN.md`). Compare the files the viewer reads (`sch_sheets`, `pcb_layers`, the SVG file names and `web/index.html`), not the SVGs byte for byte, since `kicad-cli` puts a date in each one.
