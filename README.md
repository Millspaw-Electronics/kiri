# KiCad Revision Inspector (KiRI)

> **This is Millspaw Electronics' fork of [leoheck/kiri](https://github.com/leoheck/kiri).**
> All credit for KiRI goes to Leandro Heck and its other contributors. The original README follows the fork notes below.

## About this fork

Millspaw Electronics uses KiRI on KiCad 10 projects that are stored on Windows and checked out with Git for Windows, with KiRI running in WSL2 Ubuntu. That setup hit problems the upstream version doesn't handle. We fixed them in KiRI itself rather than keep a separate patch script, so the fixes are versioned and every install gets them. The fork changes:

- **KiCad 9 or newer only.** The fork plots schematics and layouts only with KiCad's `kicad-cli`, and drops upstream's support for older KiCad versions: `plotgitsch`/`plotkicadsch` for KiCad 5 projects (`.pro`/`.sch`), GUI scripting with `xdotool`/`cliclick` for KiCad 6, KiCad-Diff for layouts, the `svgo` optimisation step, and the KiCad 5 and 6 PCB editor plugins. The options that went with them (`-s`, `-6`, `-dp`, `-dk`) are gone; `-k` and `-R` are still accepted but do nothing.

- **CRLF line endings in the working copy.** KiRI converts Windows (CRLF) line endings in the uncommitted working copy (`_local_`) before parsing it, but only for files that `file` reports as *ASCII* text. KiCad 10 board files often contain UTF-8 characters, so they were skipped. The layer parser then read the whole board file, including embedded images, as a list of layers: about 340,000 “layers” on one project, and KiRI appeared to hang. The fork converts any text file with CRLF endings.
- **Commits from all branches.** Upstream lists only the commits on the checked-out branch. The fork lists commits from every local and remote branch by default. Set `KIRI_BRANCHES` to narrow the list, for example `KIRI_BRANCHES="main my-feature" kiri ...`. Any revisions `git log` accepts work.
- **KiCad text variables in the page header.** The web page header shows the schematic and PCB title, revision and date copied straight from the title block. Projects that use KiCad text variables, such as `${PROJECT_TITLE}`, showed the variable names. The fork fills in the values from the project's `.kicad_pro`, including variables that contain other variables. It also strips the stray carriage return that CRLF files left at the end of each value.
- **Light and dark backgrounds.** Upstream draws both views on a dark background, with unchanged lines in dim grey and the older commit at half opacity. The fork draws everything at full strength and adds a light/dark button (shortcut `t`). The defaults match KiCad: a light schematic and a dark layout. The choice is remembered per view.
- **Commit messages with quotes.** Upstream inserted commit messages into the page unescaped, so a message containing `"` broke the commit list. The fork escapes them.
- **Long titles** in the page header wrap instead of being cut off.
- **Quieter output on the Windows drive.** Running on a project under `/mnt/c` in WSL, upstream printed a harmless `tar: … Cannot utime` or `sed: preserving permissions` warning for nearly every file. The fork no longer triggers or shows them.
- **One installer.** `install_kiri.sh` replaces upstream's separate `install_dependencies.sh` and `install_kiri.sh`. It installs the system packages, KiCad 10 from KiCad's PPA on Ubuntu and KiRI, and sets up `~/.bashrc`. Python packages come from Ubuntu's own packages, since system-wide `pip` installs are blocked on current Ubuntu. The installer clones from this fork. See [INSTALL.md](INSTALL.md).
- **Faster layout plotting.** Upstream runs `kicad-cli` once per layer, and every run spends about 2 seconds starting KiCad and loading the board. The fork plots all layers of a commit in one `kicad-cli --mode-multi` run (KiCad 9 and later). On a 4-commit, 29-layer project a full build went from about 5 minutes to about 20 seconds.
- **Output outside the project.** Upstream writes its output to `.kiri` in the project folder, where it shows up as changes in Git. The fork writes to `~/.cache/kiri/<project>-<id>` (respecting `XDG_CACHE_HOME`). `-d .kiri` restores the old location. In WSL this also keeps the output off the slower Windows drive.
- **`kiri-headless`** runs `kiri -S -p 8080`: start the web server without opening a browser, and keep the same address. It passes any other options through, for example `kiri-headless -r board.kicad_pro`.

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

This fork of KiRI supports KiCad 9 and newer.

Internally it uses KiCad's `kicad-cli` to generate svg images of the schematics (every sheet) and the layout (one image per layer) of each revision, to be compared.


## KiRI Installation

Check the Installation instructions, [here](INSTALL.md).


## Using KiRI

KiRI can be launched with the following command, anywhere, inside or outside of the repository of the project.

```bash
kiri [OPTIONS] [KICAD_PROJECT_FILE]
```

`KICAD_PROJECT_FILE` (a `.kicad_pro` file) can be passed, but it can also be omitted. If it is omitted, KiRI uses the `.kicad_pro` in the current folder.


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

# KiRI Screenshots

Browsing the schematic view walking through and comparing each page of the schematics, individually.

<p align="center">
    <img src="misc/kiri_sch.png" width="820" alt="Schematic View">
</p>

Browsing the layout view walking through and comparing each layer of the layout, individually.

<p align="center">
    <img src="misc/kiri_pcb.png" width="820" alt="Layout View">
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
