# LinuxBit  ![ícone](LinuxBit/Assets/Icons/favicon(2).ico) 


LinuxBit is a Linux distro portal that allows users to explore different distributions, learn about their main features, strengths, and weaknesses, view images and videos, and even download and install distros directly through the software.
The project also includes a "universal" installer, which enables the installation of multiple distributions without the need for a USB drive. Depending on the distro, it is possible to perform a pre-configuration or predefined automated installation.
LinuxBit is ideal for new users who want to find their ideal distro, understand its pros and cons, and perform downloads and installations in a simplified way.

--- 

### IMPORTANT NOTICE:
 The project is under development, may still contain bugs, and can potentially compromise any user's computer who uses it, especially because it interacts directly with the system disk/storage.

*USE AT YOUR OWN RISK*

---

## How to use:

1. Clone the repository (HTTPS, SSH, or GitHub CLI):

``git clone https://github.com/JoaoBittencourt1/LinuxBit.git``

2. Locate the executable:

The LinuxBit.exe file is located at:
``\LinuxBit\bin\Debug\net10.0-windows``
When run, the project should start normally.

## For those who want to view the code:

- It is recommended to use Microsoft Visual Studio to compile and run the project.

- This makes code management, debugging, and modifications easier compared to simpler editors like Vim or VS Code.

## Features

- Explore all available distros.

- View summaries, strengths, and weaknesses of each distro.

- View images and videos of distributions.

- Download and install distros directly through the software.

- "Universal" installer without the need for a USB drive.

- Automated installation pre-configuration for some distros.

## Technologies

- C# / .NET 10

- Windows Forms / WPF graphical interface (depending on the version used)

- Support for downloading and installing Linux distros

## Release checklist

- **Check the Arch Linux and Kali catalog entries before every release.** Their
  mirrors don't keep old releases online indefinitely, unlike Ubuntu and Mint,
  so a pinned build silently turns into a 404 — something the catalog cannot
  detect on its own. Confirm the pinned build is still published, and if not,
  bump `Version` and `DirectDownloadLink` to one that is. The generic
  `iso/latest/` address is deliberately not used for Arch: see the comment on
  its entry in `Common/Data/DistroCatalog.cs`.
- **Every time `Version` or `DirectDownloadLink` changes for an entry with
  `Sha256`/`SizeBytes` set, refresh those two fields from the distro's official
  checksum file** — never by hashing a file already downloaded locally, which
  only proves self-consistency, not that the file is the right one. A stale
  hash for a bumped build makes every future download of that distro fail
  verification (`artifact-integrity` spec), not silently succeed unverified.
- Entries without `Sha256`/`SizeBytes` (currently Kubuntu — its direct link
  points at the wrong ISO at the source — and EndeavourOS — its official
  source only publishes SHA-512) have automatic download disabled by design.
  Revisit them if the underlying issue gets fixed upstream.

## Contribution

- The project is open for contributions!

- Feel free to open issues or submit pull requests.
 
## License

This project is distributed under the GNU General Public License v3.0. See
[`LICENSE`](LICENSE).

## Acknowledgments

The transactional safety model adopted in `adopt-redacted-safety-model`
(installation plan, state machine, verified rollback, recovery agent,
compatibility preflight, signed catalog) is modeled after
[REDACTED](https://github.com/ekimiateam/redacted) by Félix and Ekimia,
also GNU GPL-3.0. See that change's `design.md` for the specific decisions
adapted and why.