# KiCad Revision Inspector (KiRI)

> **This is Millspaw Electronics' fork of [leoheck/kiri](https://github.com/leoheck/kiri).**
> All credit for KiRI goes to Leandro Heck and its other contributors. The original README follows the fork notes below.

## About this fork

Millspaw Electronics uses KiRI on KiCad 10 projects that are stored on Windows and checked out with Git for Windows, with KiRI running in WSL2 Ubuntu. That setup hit problems the upstream version doesn't handle. We fixed them in KiRI itself rather than keep a separate patch script, so the fixes are versioned and every install gets them. The fork changes:

- **CRLF line endings in the working copy.** KiRI converts Windows (CRLF) line endings in the uncommitted working copy (`_local_`) before parsing it, but only for files that `file` reports as *ASCII* text. KiCad 10 board files often contain UTF-8 characters, so they were skipped. The layer parser then read the whole board file, including embedded images, as a list of layers: about 340,000 “layers” on one project, and KiRI appeared to hang. The fork converts any text file with CRLF endings.
- **Commits from all branches.** Upstream lists only the commits on the checked-out branch. The fork lists commits from every local and remote branch by default. Set `KIRI_BRANCHES` to narrow the list, for example `KIRI_BRANCHES="main my-feature" kiri ...`. Any revisions `git log` accepts work.
- **KiCad text variables in the page header.** The web page header shows the schematic and PCB title, revision and date copied straight from the title block. Projects that use KiCad text variables, such as `${PROJECT_TITLE}`, showed the variable names. The fork fills in the values from the project's `.kicad_pro`, including variables that contain other variables. It also strips the stray carriage return that CRLF files left at the end of each value.
- **Quieter output on the Windows drive.** Running on a project under `/mnt/c` in WSL, upstream printed a harmless `tar: … Cannot utime` or `sed: preserving permissions` warning for nearly every file. The fork no longer triggers or shows them.
- **Install scripts** (`install_kiri.sh`, `install_dependencies.sh`, `INSTALL.md`) download from this fork instead of upstream.
- **`kiri-headless`** runs `kiri -k -S -p 8080`: plot layouts with `kicad-cli`, start the web server without opening a browser, and keep the same address. It passes any other options through, for example `kiri-headless -r board.kicad_pro`.

To pull in upstream changes:

```bash
git remote add upstream https://github.com/leoheck/kiri.git   # once
git fetch upstream
git merge upstream/main
```

Millspaw's full WSL2 setup procedure is in `Kiri_Setup_Guide.md` in the Millspaw-Electronics/KiCad-Shared repository.

---

KiRI started as a script to experiment having a visual diff tool for KiCad projects including schematics and layouts.
After some time, it became an interesting and it is still being updated.

Currently, KiRi supports KiCad >= 5.

Internally it uses existing tools to generate svg images of schematics and layouts to be compared.

In this way, when exporting schematics, if:

- KiCad >= 7 is installed, the `kicad-cli` is used.
- KiCad 6 is installed (which does not have `kicad-cli` available), schematics are exported using [xdotool](https://github.com/jordansissel/xdotool) on Linux/Windows and [cliclick](https://github.com/BlueM/cliclick) on macOS, using the GUI. This method is far from the ideal and it is not recommended.
- KiCad 5 is installed or if the projects is based on KiCad 5, [plotkicadsch/plotgitsch](https://github.com/jnavila/plotkicadsch) are used to export the schematics.

However, when exporting the layout layers:

- [Kicad-Diff](https://github.com/Gasman2014/KiCad-Diff) is used for all supported KiCad versions using `pcbnew` library available in python. It is also possible to use `kicad-cli` to export the layout layers however this process is slower than using Kicad-Diff since the other exports all the layers at once and `kicad-cli` can only do one layer at a time.


## KiRI Installation

Check the Installation instructions, [here](INSTALL.md).


## Using KiRI

KiRI can be launched with the following command, anywhere, inside or outside of the repository of the project.

```bash
kiri [OPTIONS] [KICAD_PROJECT_FILE]
```

`KICAD_PROJECT_FILE` can be passed, but it can also be omitted. If running from inside the project's repository, it will use the `.pro` or `.kicad_pro` available. If both are present (which is not good), it will ask your choice. The same happens is running outside of the repository without passing the `KICAD_PROJECT_FILE`.


## Command line options (aka Help)

Command line flags can be seen using the `-h` flag
```bash
kiri -h
```

### Archiving generated files

There is a possibility to archive generated files (check the help above).

To visualize generated files it is not necessary to have KiRI installed. You just have to unpack the generated package and then execute the web-server script (`./kiri-server`) inside of the folder, as shown below:

```bash
tar -xvzf kiri-2021.11.18-16h39.tgz
cd kiri
./kiri-server .
```

# KiCad Integration

It is possible to integrate KiRI with PCBNew by adding a button to its toolbar with the following command:

```bash
# Create folder if it does not exist
mkdir -p "~/.kicad/scripting/plugins"

# Copy the plugin there
cd ./kiri
cp -r "./kicad/plugin/kiri_v6/" "~/.kicad/scripting/plugins/kiri"
```

# KiRI Screenshots

Browsing the schematic view walking through and comparing each page of the schematics, individually.

<p align="center">
    <img src="misc/kiri_sch.png" width="820" alt="Schematic View">
</p>

Browsing the layout view walking through and comparing each layer of the layout, individually.

<p align="center">
    <img src="misc/kiri_pcb.png" width="820" alt="Layout View">
</p>

Here is the comparison of the schematics when the project is updated from using KiCad 5 (`.sch`) to KiCad 6 (`.kicad_sch`).

<p align="center">
    <img src="misc/kicad_sch_v6.png" width="820" alt="Layout View">
</p>

Shortcuts are a really good way of walking through the commits, pages and layers quickly. Check the available shortcuts by hitting the shortcut `i`.

<p align="center">
    <img src="misc/shortcuts.png" width="820" alt="Layout View">
</p>

A quick and old demo on the Youtube.

<p align="center">
<a href="https://youtu.be/zpssGsvCgi0" target="_blank">
    <img src="https://img.youtube.com/vi/zpssGsvCgi0/maxresdefault.jpg" alt="KiCad Revision Inspector Demo" width="820">
</a>
</p>

---

<p align="center">
Are you enjoying using this tool, feel free to pay me a beer :). Cheers!
</p>

<p align="center">
    <a href="https://www.paypal.com/donate/?hosted_button_id=EPV73V7C5N4CJ"><img src="misc/donate_btn.gif"></a>
</p>
