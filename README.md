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

- **Check the Arch Linux catalog entry before every release.** The Arch mirrors
  keep only the last ~3 monthly ISOs, so `DistroCatalog`'s pinned Arch build
  stops existing after a few months and the direct download turns into a 404 —
  something the catalog cannot detect on its own. Ubuntu and Mint keep their
  images online for years and do not need this.
- Confirm the pinned Arch build is still published, and if it is not, bump both
  `Version` and `DirectDownloadLink` to a build that is. The generic
  `iso/latest/` address is deliberately not used: see the comment on the Arch
  entry in `Common/Data/DistroCatalog.cs`.

## Contribution

- The project is open for contributions!

- Feel free to open issues or submit pull requests.
 
## License

This project currently does not have a defined license. Use and contributions are at the user's own risk.