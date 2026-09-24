# Installing KiRI

KiRI needs **KiCad 9 or newer**: it plots schematics and layouts with KiCad's `kicad-cli`.

One script installs KiRI's dependencies, KiCad (10.0 from KiCad's PPA on Ubuntu) and KiRI itself, and adds KiRI to your shell's `PATH`. Open a terminal and run:

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/Millspaw-Electronics/kiri/main/install_kiri.sh)"
```

It asks for your `sudo` password to install system packages. Run it again at any time to update KiRI. An existing installation is updated in place.

When it finishes, open a new terminal and check the installation with `kiri -v`.

> Windows users must use WSL2. See the [Windows preparation](#windows-preparation) section.

> macOS users must have `homebrew`.

## Options

Set these environment variables in front of the command to change what the installer does, for example `KIRI_BRANCH=my-branch bash -c "$(curl ...)"`.

| Variable | Default | Meaning |
|---|---|---|
| `KIRI_INSTALL_PATH` | `~/.local/share` | Folder to install into. KiRI goes in `${KIRI_INSTALL_PATH}/kiri`. |
| `KIRI_BRANCH` | `main` | Branch to install. |
| `KIRI_REPO` | `https://github.com/Millspaw-Electronics/kiri.git` | Repository to clone. |
| `INSTALL_KIRI_REMOTELLY` | unset | When running `./install_kiri.sh` from a checkout, set this to clone from `KIRI_REPO` instead of installing the checkout. |
| `KIRI_KICAD_PPA` | `ppa:kicad/kicad-10.0-releases` | KiCad PPA used on Ubuntu. Set it to an empty string to use the distribution's KiCad, which must be version 9 or newer. |
| `KIRI_SKIP_DEPENDENCIES` | unset | Set to `1` to skip installing system packages. |

The installer adds a block marked `# >>> kiri >>>` to `~/.bashrc` (and `~/.zshrc` if it exists) that sets `KIRI_HOME` and `PATH`. Re-running the installer updates that block rather than adding another.

## Windows Preparation

In PowerShell with administrator rights, install WSL2 with Ubuntu, then reboot:

```powershell
wsl --install -d Ubuntu
wsl --set-default-version 2
```

Then run the installer above in the Ubuntu terminal. KiRI plots with `kicad-cli`, so no X server is needed.

# Docker

Since KiRI involves a bunch of tools and some complex settings, there is a repo that shares a Docker image to provide simple use. This is a separate project, based on upstream KiRI, and can be found here [Kiri-Docker](https://github.com/leoheck/kiri-docker)
