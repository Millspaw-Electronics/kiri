# Plan: porting KiRI's generator off bash

*Status: proposal, not started. Written September 2026 against this fork at the commit that added `--mode-multi` plotting and the `~/.cache/kiri` output folder.*

## Goal

Run KiRI natively on Windows, without WSL, and make it easy to package as a desktop app later. Keep the web viewer and everything it shows today.

Today KiRI needs WSL on Windows. That means a separate Ubuntu install, a second copy of KiCad inside it, a Git `safe.directory` setting for the Windows drive, and a terminal to run it. The viewer itself is already a portable web page. What ties KiRI to Linux is the generator: a ~2,900-line bash script that calls `git`, `kicad-cli` and many small Unix tools.

## Background and measurements

Measured on `EE_650-30X-3193-MEL` (4 commits, 1 schematic sheet and 29 PCB layers each) on WSL2 Ubuntu 26.04 with KiCad 10.0.6:

| Item | Time |
|---|---|
| One `kicad-cli pcb export svg` call (one layer) | 2.00 s |
| One `kicad-cli pcb export svg --mode-multi` call (all 29 layers) | 2.06 s |
| One `kicad-cli sch export svg` call | 0.58 s |
| KiRI's own bash work on a fully cached run | 3.5 s |
| Full build before `--mode-multi` (one `kicad-cli` call per layer) | ~5 min |
| Full build with `--mode-multi`, output on the Windows drive | 50 s |
| Full build with `--mode-multi`, output in `~/.cache/kiri` | 20 s |

Almost all of `kicad-cli`'s time is spent starting KiCad and loading the board, so plotting all layers in one call is nearly free. This fork already does that.

The obvious shortcut, running the existing bash script in Git Bash (MSYS2, which ships with Git for Windows), mostly works tool-wise. Git Bash has bash 5.3, GNU `sed`/`grep -P`/`awk`, `find`, `realpath` and `file`. But starting a process there is much slower. 300 small `echo | cut | sed` pipelines took **0.7 s in WSL and 14 s in Git Bash (about 21×)**. KiRI starts thousands of such processes, so its 3.5 s of bash work would take roughly a minute or more in Git Bash on every run. That is why this plan proposes porting the generator rather than just running it under Git Bash.

## Language choice

Nothing the generator does needs a particular language. It runs `git` and `kicad-cli`, reads KiCad's text files, and writes SVG and JSON. The two Python components KiRI currently uses are no longer needed: KiCad-Diff is replaced by `kicad-cli --mode-multi`, and `plotgitsch` only matters for KiCad 5.

| | Node | Python |
|---|---|---|
| Runtime on a Windows PC | Not installed by default. Bundle it with Electron, or ship a single `.exe` using Node's single-executable-application feature. | Already present: KiCad for Windows ships `C:\Program Files\KiCad\<version>\bin\python.exe`. |
| Desktop app | Electron | pywebview (uses the Edge WebView2 already on Windows) plus PyInstaller |
| Shares a language with the viewer | Yes | No |
| Command-line use | Needs Node, or the bundled `.exe` | Works with KiCad's Python |

**Recommendation:** use **Node** if an Electron app is the likely end state, so the generator and viewer share one language and nothing is rewritten later. Use Python if a command-line tool plus the browser is enough, because it needs no extra install. Decide this before starting Phase 1.

## Packaging options

| Option | Notes |
|---|---|
| Browser plus a Windows launcher (`.cmd` or shortcut) | Least work. Runs the generator, starts the server and opens the default browser. |
| Electron | Real app window, menus and a file picker. About 150 MB per install. Needs an installer, and code signing to avoid Windows SmartScreen warnings. |
| pywebview (Python) | Native window using the system's WebView2. Probably 20–40 MB as one `.exe`. |
| Tauri | Small app, but the backend is Rust: another rewrite. |
| KiCad plugin | KiRI ships PCB editor plugins for KiCad 5 and 6. KiCad is replacing that plugin API, so this needs checking against KiCad 10 before relying on it. |

## What the port must keep

Features of this fork that users rely on:

- **Commit list** from all local and remote branches (`git log --branches --remotes`), with `KIRI_BRANCHES` to narrow it, and branch labels on each branch's latest commit.
- **Uncommitted changes** shown as `_local_`, with CRLF line endings converted before parsing (KiCad 10 files often contain UTF-8, so don't rely on `file` reporting ASCII).
- **Schematics**: every sheet of a hierarchical schematic, with the page list. Today `kicad6_sheet_instances.py` works out the hierarchy, and KiRI renames `kicad-cli`'s per-sheet files, which are named after the sheet's title, not its file.
- **Layouts**: one SVG per layer with the board outline (Edge.Cuts) on every layer, plus the layer list with KiCad's layer numbers and display names.
- **Page header**: title, revision and date from the title blocks, with KiCad text variables (including nested ones) filled in from the `.kicad_pro`.
- **Viewer**: light/dark backgrounds with per-view defaults (light schematic, dark layout) and the `t` shortcut; full-contrast diff colours; HTML-escaped commit messages; wrapping titles; all existing keyboard shortcuts.
- **Caching**: commits that were already plotted are reused; only new commits and `_local_` are plotted. `-r` rebuilds everything.
- **Output location**: outside the project (`~/.cache/kiri/<project>-<id>` today; `%LOCALAPPDATA%\kiri\...` would be the Windows equivalent), overridable with `-d`.
- **Options** in use: `-g A..B` to compare two commits, `-r`, `-d`, the port, and not opening a browser (`kiri-headless`).
- **"Launch KiCad at this Rev"**: opens a commit's project in KiCad. On Windows this can finally open the Windows KiCad.
- **`-x` archive** of the generated site, viewable with only a web server.

Can be dropped: KiCad 5 and 6 support (`plotgitsch`, `xdotool`/`cliclick` GUI scripting), KiCad-Diff, the `svgo` optimisation step (optional today), and `svg_tweaks` (a manual viewBox fix behind a command-line option). The 3D-model step is already commented out.

## Proposed design

The generator becomes a small program with these parts:

1. **Project discovery**: find the `.kicad_pro`, the repository root and the nested project path, and the output folder.
2. **Commit list**: one `git log` call with a machine-readable format (for example fields separated by `%x00`), instead of the current `cut`/`sed` parsing of `|`-separated text.
3. **Checkout**: for each commit, write only the project's files (`.kicad_pro`, `.kicad_pcb`, every schematic sheet, and the project's `fp-lib-table`/`sym-lib-table` if plotting needs them) with `git show <hash>:<path>`, instead of unpacking the whole commit with `git archive | tar`. For `_local_`, copy the working files and normalise CRLF.
4. **Parsing**: read the layer table, sheet hierarchy and title block from the KiCad files, which are S-expressions. A small S-expression reader is enough; don't parse with line-based regular expressions.
5. **Plotting**: per commit, one `kicad-cli sch export svg` and one `kicad-cli pcb export svg --mode-multi --common-layers Edge.Cuts`, then rename the output to the viewer's layout. Run several commits in parallel, limited to the number of CPU cores. Detect `--mode-multi` support and fall back to one call per layer.
6. **Manifest**: write one `manifest.json` describing the commits, sheets, layers, titles and default view. The viewer loads it at start-up. This replaces the current approach of editing `index.html` and `kiri.js` with `sed`, and removes the need to HTML-escape values in shell.
7. **Server**: serve the output folder and handle "Launch KiCad at this Rev". Inside Electron the server isn't needed: the app can load files directly or through a custom protocol.

The viewer (`assets/`) stays mostly as it is. It needs a change to read `manifest.json`, and the placeholders (`[SCH_TITLE]` etc.) go away.

Tools to locate on Windows: `git.exe` (from Git for Windows, on `PATH` or through GitHub Desktop's bundled copy) and `kicad-cli.exe` (default `C:\Program Files\KiCad\<version>\bin\`, pick the newest). Allow overriding both.

## Phases

**Phase 0: done in this fork.** `--mode-multi` plotting, output folder outside the project, and the other fork fixes. These make the current bash version fast enough on WSL while the port is pending.

**Phase 1: generator parity (Linux/WSL first).**
Build the new generator and the manifest-reading viewer, and run it on the same machines as today.
*Done when:* on the reference projects (see Testing), the viewer shows the same commits, sheets, layers, titles and drawings as the bash version, and a full build is no slower than 20 s on `EE_650-30X-3193-MEL`.

**Phase 2: native Windows.**
Run the generator from PowerShell, using Windows `git.exe` and `kicad-cli.exe`, with Windows paths and `%LOCALAPPDATA%` output.
*Done when:* Phase 1's checks pass on Windows without WSL, including a project whose working copy has CRLF line endings.

**Phase 3: packaging.**
Pick from the packaging options above. Start with the launcher; move to Electron or pywebview if people want an app.
*Done when:* someone who has only Git for Windows (or GitHub Desktop) and KiCad installed can compare revisions without opening a terminal.

**Phase 4: extras** (optional). Rebuild automatically when project files change; "Launch KiCad at this Rev" opening the Windows KiCad; open-project dialog; recent projects.

## Testing

Keep a set of small reference repositories, and check each one in both the bash version and the port:

- `EE_650-30X-3193-MEL`: KiCad 10, 29 layers, text variables in the title block (including nested variables), a commit message with double quotes, a working copy with CRLF line endings and UTF-8 content.
- A project with a hierarchical schematic (several sheets, including a sheet used twice).
- A repository with two branches whose commits interleave.
- A project nested in a subfolder of its repository.
- A board with inner copper layers and user-renamed layers (display names that differ from the internal names, such as `F.SilkS` shown as `F.Silkscreen`).

For each, compare: the commit list; the page and layer lists; the page header; and the drawings. Compare SVGs by their drawing content, not byte-for-byte: `kicad-cli` puts a creation date in the file, the output order of elements can change, and `--mode-multi` numbers the title block's page "Id" per layer (e.g. `2/29` instead of `1/1`).

## Pitfalls found while fixing the bash version

- `--mode-multi` names each file after the layer's **display name** (the last quoted name on the layer's line in the `.kicad_pcb`, e.g. `(9 "F.Adhes" user "F.Adhesive")` gives `F_Adhesive`), with `.` replaced by `_`. Matching on the internal name silently loses layers.
- Git for Windows checks files out with CRLF line endings. Code that finds the end of a block by matching an exact line (like `\t)`) breaks on them.
- The Windows drive in WSL does not allow setting Linux timestamps or ownership, so `tar` and `sed -i` warn on every file. Keep generated files off it.
- Commit messages and author names end up in HTML; escape them. Messages can contain `|`, so don't use it as a field separator.
- Starting processes is expensive on Windows. Batch work into a few calls, and don't run a tool per line or per layer.

## Open questions

- Node or Python? (See Language choice.)
- Keep tracking upstream KiRI at all? A port makes this effectively a new tool built on KiRI's viewer. KiRI is MIT-licensed, so that's allowed, but the copyright notice and licence text must stay with the code.
- Where should the new tool live: in this fork, or a new repository?
- Is macOS or Linux support still wanted after the port, or only Windows?
