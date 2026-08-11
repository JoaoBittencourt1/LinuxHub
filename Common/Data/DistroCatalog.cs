using LinuxHub.Common.Models;

namespace LinuxHub.Common.Data
{
    /// <summary>
    /// Fonte única de verdade para as distros suportadas — usada tanto para exibição
    /// no catálogo quanto para detecção de distro a partir de um nome de arquivo ISO.
    /// Antes desta consolidação existiam duas listas divergentes (MainWindow e
    /// DistroDetector); ver design.md do change restructure-feature-based-mvvm.
    ///
    /// Texto voltado ao usuário (bio/mantenedor) não vive aqui como string literal —
    /// vive em Common/Localization/Strings*.resx, sob as chaves Distro_{Id}_Description
    /// e Distro_{Id}_Maintainer (ver DistroInfo.DescriptionKey/MaintainerKey e
    /// constitution.md, "Nenhuma string hardcoded").
    /// </summary>
    public static class DistroCatalog
    {
        public static IReadOnlyList<DistroInfo> All { get; } = new List<DistroInfo>
        {
            new()
            {
                Id = "ubuntu",
                Name = "Ubuntu",
                Family = "Debian",
                Version = "24.04.4",
                CreatedYear = "2004",
                BeginnerRating = 5,
                IsTested = true,
                UnattendedInstall = UnattendedInstallMechanism.Subiquity,
                ImagePath = "pack://application:,,,/Assets/Images/Ubuntu.png",
                DownloadLink = "https://ubuntu.com/download/desktop",
                DirectDownloadLink = "https://releases.ubuntu.com/24.04/ubuntu-24.04.4-desktop-amd64.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Ubuntu/ubuntu1.jpg",
                    "pack://application:,,,/Assets/Images/Ubuntu/ubuntu2.png"
                }
            },
            new()
            {
                Id = "mint",
                Name = "Linux Mint",
                Family = "Debian",
                Version = "22.3",
                CreatedYear = "2006",
                BeginnerRating = 5,
                // Boot pela staging testado de verdade (foi ele que expôs o initrd.lz).
                // A instalação desatendida é outra história — ver abaixo.
                IsTested = true,
                // Mint fica SEM instalação desatendida, e não por falta de tentativa: o teste
                // em VM de 2026-08-10 exercitou o caminho completo e mostrou que ele não tem
                // como ser fechado com segurança. O ubiquity consulta `partman-auto/method`
                // para entrar em modo automático — sem a chave, `auto_state` fica em None e
                // nada é automatizado; com ela, o partman reparticiona o disco inteiro se o
                // `init_automatically_partition` não casar. É a mesma chave para as duas
                // coisas, então "automatizar o dual-boot" e "arriscar o disco do usuário" são
                // inseparáveis aqui. Foi assim que a ESP do usuário se perdeu em 2026-08-05.
                //
                // constitution.md §6.1: automação incompleta é preferível a automação
                // insegura. O boot pela staging continua automático; a escolha da partição
                // fica com o usuário, na tela do próprio instalador.
                ImagePath = "pack://application:,,,/Assets/Images/mint.png",
                DownloadLink = "https://linuxmint.com/download.php",
                DirectDownloadLink = "https://mint.portalidea.com.br/iso/stable/22.3/linuxmint-22.3-cinnamon-64bit.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Mint/Mint1.png",
                    "pack://application:,,,/Assets/Images/Mint/mint2.png"
                }
            },
            new()
            {
                Id = "zorin",
                Name = "Zorin OS",
                Family = "Ubuntu",
                Version = "18",
                CreatedYear = "2008",
                BeginnerRating = 5,
                ImagePath = "pack://application:,,,/Assets/Images/zorin.png",
                DownloadLink = "https://zorin.com/os/download/",
                DirectDownloadLink = "https://mirror.umd.edu/zorin/18/Zorin-OS-18-Core-64-bit-r2.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Zorin/zorin1.png",
                    "pack://application:,,,/Assets/Images/Zorin/zorin2.jpg",
                    "pack://application:,,,/Assets/Images/Zorin/zorin3.jpg"
                }
            },
            new()
            {
                Id = "pop",
                Name = "Pop!_OS",
                Family = "Ubuntu",
                Version = "24.04",
                CreatedYear = "2017",
                BeginnerRating = 3,
                ImagePath = "pack://application:,,,/Assets/Images/popos.png",
                DownloadLink = "https://system76.com/pop/",
                DirectDownloadLink = "https://iso.pop-os.org/24.04/amd64/generic/22/pop-os_24.04_amd64_generic_22.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/PopOs/pop1.png",
                    "pack://application:,,,/Assets/Images/PopOs/pop2.png"
                }
            },
            new()
            {
                Id = "fedora",
                Name = "Fedora",
                Family = "Red Hat",
                Version = "43",
                CreatedYear = "2003",
                BeginnerRating = 3,
                ImagePath = "pack://application:,,,/Assets/Images/fedora.png",
                DownloadLink = "https://www.fedoraproject.org/pt-br/workstation/download",
                DirectDownloadLink = "https://download.fedoraproject.org/pub/fedora/linux/releases/43/KDE/x86_64/iso/Fedora-KDE-Desktop-Live-43-1.6.x86_64.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Fedora/fedora1.jpg",
                    "pack://application:,,,/Assets/Images/Fedora/fedora2.jpg",
                    "pack://application:,,,/Assets/Images/Fedora/fedora3.jpg"
                }
            },
            new()
            {
                Id = "kubuntu",
                Name = "Kubuntu",
                Family = "Ubuntu",
                Version = "24.04",
                CreatedYear = "2005",
                BeginnerRating = 4,
                ImagePath = "pack://application:,,,/Assets/Images/Kubuntu.png",
                DownloadLink = "https://kubuntu.org/archives/getkubuntu.html",
                // Link direto do Kubuntu está indisponível na origem; mantido como no código
                // original (aponta para o instalador do Pop!_OS) — correção de URL está fora
                // do escopo desta reorganização arquitetural.
                DirectDownloadLink = "https://iso.pop-os.org/24.04/amd64/generic/22/pop-os_24.04_amd64_generic_22.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Kubuntu/Kubuntu1.png"
                }
            },
            new()
            {
                Id = "xubuntu",
                Name = "Xubuntu",
                Family = "Ubuntu",
                Version = "25.10",
                CreatedYear = "2006",
                BeginnerRating = 4,
                ImagePath = "pack://application:,,,/Assets/Images/Xubuntu.png",
                DownloadLink = "https://xubuntu.org/download/",
                DirectDownloadLink = "https://ftp.ussg.iu.edu/linux/xubuntu/releases/25.10/release/xubuntu-25.10-desktop-amd64.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Xubuntu/xubuntu.png"
                }
            },
            new()
            {
                Id = "manjaro",
                Name = "Manjaro",
                Family = "Arch",
                Version = "25.0.10",
                CreatedYear = "2011",
                BeginnerRating = 3,
                ImagePath = "pack://application:,,,/Assets/Images/manjaro.png",
                DownloadLink = "https://manjaro.org/products/download/x86",
                DirectDownloadLink = "https://download.manjaro.org/xfce/25.0.10/manjaro-xfce-25.0.10-251013-linux612.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Manjaro/manjaro1.jpg",
                    "pack://application:,,,/Assets/Images/Manjaro/manjaro2.jpg"
                }
            },
            new()
            {
                Id = "arch",
                Name = "Arch Linux",
                Family = "Arch",
                // ESTA ENTRADA EXPIRA. O mirror do Arch guarda só as ~3 releases mensais mais
                // recentes, ao contrário de Ubuntu e Mint, cujas ISOs ficam anos no ar. A
                // 2026.01.01 declarada aqui antes já tinha saído do ar: era um 404 em produção,
                // e o catálogo não tem como perceber isso sozinho. A revisão desta entrada faz
                // parte do processo de release (ver README.md).
                //
                // O endereço estável `iso/latest/archlinux-x86_64.iso` resolveria o 404 e foi
                // descartado de propósito: no dia em que o Arch declarar mecanismo de instalação
                // desatendida, a versão validada e a versão entregue precisam ser a mesma, e o
                // schema do JSON do archinstall muda entre releases. `latest` trocaria uma falha
                // ruidosa (404) por uma silenciosa (config incompatível na máquina do usuário).
                Version = "2026.08.01",
                CreatedYear = "2002",
                BeginnerRating = 1,
                // A ISO do Arch não é casper: kernel e initramfs ficam em /arch/boot/x86_64, e
                // quem monta o laço da ISO é o initramfs (img_dev/img_loop), não o GRUB. Sem
                // esta declaração a entrada gerada seria a do casper, e a máquina reiniciaria
                // num GRUB que não acha o kernel.
                LiveSession = LiveSessionFamily.Archiso,
                ImagePath = "pack://application:,,,/Assets/Images/arch.png",
                DownloadLink = "https://archlinux.org/download/",
                DirectDownloadLink = "https://mirror.adectra.com/archlinux/iso/2026.08.01/archlinux-2026.08.01-x86_64.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Arch/arch1.png",
                    "pack://application:,,,/Assets/Images/Arch/arch2.png",
                    "pack://application:,,,/Assets/Images/Arch/arch3.png",
                    "pack://application:,,,/Assets/Images/Arch/arch4.png"
                }
            },
            new()
            {
                Id = "endeavour",
                Name = "EndeavourOS",
                Family = "Arch",
                Version = "2025.11.24",
                CreatedYear = "2019",
                BeginnerRating = 2,
                ImagePath = "pack://application:,,,/Assets/Images/endeavouros.png",
                DownloadLink = "https://endeavouros.com/",
                DirectDownloadLink = "https://mirrors.gigenet.com/endeavouros/iso/EndeavourOS_Ganymede-2025.11.24.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/End/end1.jpg",
                    "pack://application:,,,/Assets/Images/End/end2.png",
                    "pack://application:,,,/Assets/Images/End/end3.jpeg"
                }
            },
            new()
            {
                Id = "kali",
                Name = "Kali Linux",
                Family = "Debian",
                Version = "2025.4",
                CreatedYear = "2013",
                BeginnerRating = 1,
                ImagePath = "pack://application:,,,/Assets/Images/kali.png",
                DownloadLink = "https://www.kali.org/get-kali/",
                DirectDownloadLink = "https://cdimage.kali.org/kali-2025.4/kali-linux-2025.4-installer-amd64.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Kali/kali1.jpg",
                    "pack://application:,,,/Assets/Images/Kali/kali2.jpg"
                }
            },
        };

        /// <summary>
        /// Identifica a distro a partir do nome de um arquivo ISO, casando o Id de cada
        /// entrada do catálogo como substring do nome (case-insensitive). Retorna null
        /// quando nenhuma distro é reconhecida — quem chama decide o fallback.
        ///
        /// Entre vários Ids que casam, vence o mais específico (o mais longo): o nome
        /// <c>xubuntu-25.10-desktop-amd64.iso</c> contém "ubuntu", e antes quem ganhava era a
        /// primeira entrada da lista — o Ubuntu. Uma ISO de Xubuntu/Kubuntu era detectada como
        /// Ubuntu, o que arrastava junto o <see cref="DistroInfo.UnattendedInstall"/> dele e
        /// fazia o wizard oferecer instalação automática pra uma distro nunca validada.
        /// </summary>
        public static DistroInfo? FindByIsoFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            var lowered = fileName.ToLowerInvariant();
            return All
                .Where(distro => lowered.Contains(distro.Id))
                .OrderByDescending(distro => distro.Id.Length)
                .FirstOrDefault();
        }
    }
}
