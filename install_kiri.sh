#!/bin/bash

# Install KiRI, its dependencies and KiCad, and set up the shell environment.
# KiRI needs KiCad 9 or newer.
#
# Run it straight from GitHub:
#
#   bash -c "$(curl -fsSL https://raw.githubusercontent.com/Millspaw-Electronics/kiri/main/install_kiri.sh)"
#
# or from a KiRI checkout (./install_kiri.sh) to install that checkout.
# It is safe to run again: an existing installation is updated in place.
#
# Environment variables:
#   KIRI_INSTALL_PATH       Folder to install into (default: ~/.local/share).
#                           KiRI goes in ${KIRI_INSTALL_PATH}/kiri.
#   KIRI_BRANCH             Branch to install (default: main).
#   KIRI_REPO               Repository to clone
#                           (default: https://github.com/Millspaw-Electronics/kiri.git).
#   INSTALL_KIRI_REMOTELLY  Clone KiRI from KIRI_REPO even when run from a checkout.
#   KIRI_KICAD_PPA          KiCad PPA used on Ubuntu (default: ppa:kicad/kicad-10.0-releases).
#                           Set it to an empty string to use the distribution's KiCad.
#   KIRI_SKIP_DEPENDENCIES  Set to 1 to skip installing system packages.

CI=$(tput setaf 3 2> /dev/null) # Color Info
CW=$(tput setaf 1 2> /dev/null) # Color Warning
CB=$(tput bold 2> /dev/null)    # Color Bold
CR=$(tput sgr0 2> /dev/null)    # Color Reset

KIRI_INSTALL_PATH="${KIRI_INSTALL_PATH:-${HOME}/.local/share}"
KIRI_BRANCH="${KIRI_BRANCH:-main}"
KIRI_REPO="${KIRI_REPO:-https://github.com/Millspaw-Electronics/kiri.git}"
KIRI_KICAD_PPA="${KIRI_KICAD_PPA-ppa:kicad/kicad-10.0-releases}"
KIRI_DIR="${KIRI_INSTALL_PATH}/kiri"

ctrl_c()
{
	exit 1
}

info()
{
	echo -e "\n${CI}${CB}${*}${CR}"
}

warning()
{
	echo -e "${CW}${CB}WARNING:${CR} ${*}" >&2
}

# =============================================
# Operating system detection
# =============================================

identify_linux_or_wsl()
{
	if grep -qEi "(Microsoft|WSL)" /proc/version &> /dev/null ; then
		echo "WSL"
	else
		echo "Linux"
	fi
}

identify_operating_system()
{
	case "${OSTYPE}" in
		bsd*)     echo "BSD"     ;;
		darwin*)  echo "macOS"   ;;
		linux*)   echo "$(identify_linux_or_wsl)" ;;
		msys*)    echo "Windows" ;;
		solaris*) echo "Solaris" ;;
		*)        echo "Unknown" ;;
	esac
}

identify_linux_distro()
{
	grep "^ID=" /etc/os-release | cut -d= -f2 | tr -d '"'
}

identify_linux_pkg_manager()
{
	distro_id="$(identify_linux_distro)"
	distro_id_like="$(grep "^ID_LIKE=" /etc/os-release | cut -d= -f2)"

	# Debian does not have "ID_LIKE"
	if [[ "${distro_id}" == "debian" ]] || [[ "${distro_id_like}" =~ "debian" ]]; then
		base_distro="debian"
	else
		base_distro="${distro_id}"
	fi

	case "${base_distro}" in
		"debian")   echo "apt"     ;;
		"fedora")   echo "dnf"     ;;
		"redhat")   echo "yum"     ;;
		"arch")     echo "pacman"  ;;
		"archarm")  echo "pacman"  ;;
		"manjaro")  echo "pacman"  ;;
		*)          echo "Unknown" ;;
	esac
}

linux_install_dependencies()
{
	pkg_manager="$(identify_linux_pkg_manager)"

	case "${pkg_manager}" in
		apt)
			linux_install_software_with_apt
			;;
		dnf)
			linux_install_software_with_dnf
			install_python_modules_with_pip
			;;
		pacman)
			linux_install_software_with_pacman
			install_python_modules_with_pip
			;;
		*)
			echo "Error: Unknown system"
			echo "Please, ask KiRI dev to adapt the dependencies installer"
			exit 1
			;;
	esac
}

# =============================================
# Linux apt-related stuff
# =============================================

apt_install()
{
	# Install all packages in one go, then retry one by one if that fails,
	# so a single unavailable package does not stop the others
	if ! sudo apt-get install -y "${@}"; then
		local package
		for package in "${@}"; do
			sudo apt-get install -y "${package}" || warning "Could not install ${package}"
		done
	fi
}

add_kicad_ppa()
{
	if [[ -z "${KIRI_KICAD_PPA}" ]]; then
		return
	fi

	if [[ "$(identify_linux_distro)" != "ubuntu" ]]; then
		warning "Not Ubuntu, so skipping ${KIRI_KICAD_PPA}. The distribution's KiCad will be used."
		return
	fi

	info "Adding KiCad package repository ${KIRI_KICAD_PPA}"
	apt_install software-properties-common
	sudo add-apt-repository -y "${KIRI_KICAD_PPA}" || warning "Could not add ${KIRI_KICAD_PPA}"
}

linux_install_software_with_apt()
{
	info "Installing system packages"

	# Update packages knowledge
	sudo apt-get update

	add_kicad_ppa
	sudo apt-get update

	# Base packages
	apt_install git curl coreutils dos2unix

	# Python, and wxPython for the project file picker (kiri-file-picker), from
	# Ubuntu's packages because system-wide pip installs are blocked on current
	# Ubuntu releases
	apt_install python3 python-is-python3 python3-wxgtk4.0

	# KiCad, including kicad-cli and the pcbnew Python module
	apt_install kicad
}

# =============================================
# Linux dnf-related stuff
# =============================================

linux_install_software_with_dnf()
{
	info "Installing system packages"

	# Update packages knowledge
	sudo dnf check-update

	sudo dnf install -y git
	sudo dnf install -y curl
	sudo dnf install -y python3-pip
	sudo dnf install -y kicad
	sudo dnf install -y dos2unix
}

# =============================================
# Linux pacman-related stuff
# =============================================

linux_install_software_with_pacman()
{
	info "Installing system packages"

	yes | sudo pacman -S git --needed
	yes | sudo pacman -S curl --needed
	yes | sudo pacman -S make --needed
	yes | sudo pacman -S patch --needed
	yes | sudo pacman -S python-pip --needed
	yes | sudo pacman -S kicad --needed
	yes | sudo pacman -S dos2unix --needed
}

# =============================================
# macOS Related stuff
# =============================================

macos_install_homebrew()
{
	# Install Homebrew
	if ! which brew &> /dev/null; then
		/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
	fi
}

macos_install_kicad()
{
	if [[ ! -d "/Applications/KiCad/KiCad.app" ]]; then
		brew install --cask kicad
	fi
}

macos_install_brew_modules()
{
	info "Installing Homebrew packages"

	sudo spctl --master-disable
	xcode-select --install

	# Base dependencies
	brew install git

	# KiRI dependencies
	brew install gsed
	brew install findutils
	brew install coreutils
	brew install wxpython
	brew install wxwidgets
	brew install dos2unix
}

# =============================================
# Python modules (non-apt systems)
# =============================================

install_python_modules_with_pip()
{
	info "Installing Python modules"

	if [[ -f "${KIRI_DIR}/python-requirements.txt" ]]; then
		pip3 install --user -r "${KIRI_DIR}/python-requirements.txt" || warning "Could not install the Python modules"
	else
		pip3 install --user -r https://raw.githubusercontent.com/Millspaw-Electronics/kiri/main/python-requirements.txt || warning "Could not install the Python modules"
	fi
}

# =============================================
# KiRI itself
# =============================================

script_checkout()
{
	# Print the KiRI checkout this script is running from, if any
	local script_dir
	script_dir="$(cd "$(dirname "${BASH_SOURCE[0]:-}")" 2> /dev/null && pwd -P)"
	if [[ -n "${script_dir}" ]] && [[ -f "${script_dir}/bin/kiri" ]]; then
		echo "${script_dir}"
	fi
}

install_kiri()
{
	local source_dir
	source_dir="$(script_checkout)"

	mkdir -p "${KIRI_INSTALL_PATH}"

	if [[ -z "${INSTALL_KIRI_REMOTELLY}" ]] && [[ -n "${source_dir}" ]]; then

		if [[ "$(realpath "${source_dir}")" == "$(realpath -m "${KIRI_DIR}")" ]]; then
			info "Using the KiRI checkout in ${KIRI_DIR}"
			git -C "${KIRI_DIR}" submodule update --init --recursive || warning "Could not update the submodules"
		else
			info "Copying KiRI from ${source_dir} to ${KIRI_DIR}"
			rm -rf "${KIRI_DIR}"
			cp -rf "${source_dir}" "${KIRI_DIR}"
		fi

	elif [[ -d "${KIRI_DIR}/.git" ]]; then

		info "Updating KiRI in ${KIRI_DIR}"
		if ! git -C "${KIRI_DIR}" fetch origin \
			|| ! git -C "${KIRI_DIR}" checkout "${KIRI_BRANCH}" \
			|| ! git -C "${KIRI_DIR}" pull --ff-only; then
			warning "Could not update ${KIRI_DIR}, probably because of local changes. Keeping the current version."
		fi
		git -C "${KIRI_DIR}" submodule update --init --recursive || warning "Could not update the submodules"

	else

		if ! which git &> /dev/null; then
			echo "Error: Git is missing. Install it, or run this script without KIRI_SKIP_DEPENDENCIES." >&2
			exit 1
		fi

		info "Downloading KiRI from ${KIRI_REPO} (${KIRI_BRANCH})"
		rm -rf "${KIRI_DIR}"
		if ! git clone --recurse-submodules -j8 --branch "${KIRI_BRANCH}" "${KIRI_REPO}" "${KIRI_DIR}"; then
			echo "Error: Could not download KiRI from ${KIRI_REPO}" >&2
			exit 1
		fi
	fi

}

# =============================================
# Shell environment
# =============================================

setup_shell_environment()
{
	local rc_file
	local begin="# >>> kiri >>>"
	local end="# <<< kiri <<<"

	read -r -d '' ENV_SETUP <<-EOM
	${begin}
	# Added by the KiRI installer. Re-running the installer updates this block.
	export KIRI_HOME="${KIRI_DIR}"
	export PATH="\${KIRI_HOME}/bin:\${PATH}"
	${end}
	EOM

	info "Setting up the shell environment"

	for rc_file in "${HOME}/.bashrc" "${HOME}/.zshrc"; do

		# Always set up bash; only touch zsh if it is in use
		if [[ "${rc_file}" == *zshrc ]] && [[ ! -f "${rc_file}" ]]; then
			continue
		fi
		touch "${rc_file}"

		if grep -qF "${begin}" "${rc_file}"; then
			# Replace the existing block
			local tmp_file
			tmp_file="$(mktemp)"
			awk -v begin="${begin}" -v end="${end}" -v block="${ENV_SETUP}" '
				$0 == begin { print block; skip = 1; next }
				$0 == end   { skip = 0; next }
				!skip       { print }
			' "${rc_file}" > "${tmp_file}" && cat "${tmp_file}" > "${rc_file}"
			rm -f "${tmp_file}"
			echo "Updated the KiRI settings in ${rc_file}"
		elif grep -q "KIRI_HOME" "${rc_file}"; then
			echo "${rc_file} already sets KIRI_HOME, so it was left unchanged"
		else
			echo -e "\n${ENV_SETUP}" >> "${rc_file}"
			echo "Added the KiRI settings to ${rc_file}"
		fi
	done
}

# =============================================
# Main
# =============================================

show_initial_message()
{
	read -r -d '' INITIAL_MESSAGE <<-EOM
	${CI}${CB}Installing KiRI${CR}

	Repository:        ${KIRI_REPO} (${KIRI_BRANCH})
	Installation path: ${KIRI_DIR}

	Change them with the KIRI_REPO, KIRI_BRANCH and KIRI_INSTALL_PATH environment
	variables. See the top of this script for the other options.
	EOM

	echo -e "\n${INITIAL_MESSAGE}"
}

show_final_message()
{
	local kicad_version
	kicad_version="$(kicad-cli version 2> /dev/null)"

	read -r -d '' FINAL_MESSAGE <<-EOM
	${CI}${CB}KiRI is installed${CR}

	KiRI:      ${KIRI_DIR}
	kicad-cli: ${kicad_version:-not found}

	Open a new terminal, then run KiRI from a KiCad project folder, for example:
	    kiri-headless my_board.kicad_pro
	EOM

	echo -e "\n${FINAL_MESSAGE}\n"
}

main()
{
	trap ctrl_c INT

	show_initial_message

	if [[ "${KIRI_SKIP_DEPENDENCIES}" != "1" ]]; then
		operating_system="$(identify_operating_system)"

		case "${operating_system}" in
			"Linux"|"WSL")
				linux_install_dependencies
				;;

			"macOS")
				macos_install_homebrew
				macos_install_kicad
				macos_install_brew_modules
				install_python_modules_with_pip
				;;

			*)
				echo "Installer does not handle \"${operating_system}\" yet."
				exit 1
				;;
		esac
	fi

	install_kiri
	setup_shell_environment
	show_final_message
}

main "${@}"
