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
    ///
    /// Desde a mudança do modelo transacional de segurança (D9/D14): esta lista literal é o <b>fallback
    /// embarcado</b>, não necessariamente o que a UI exibe. <see cref="All"/> começa
    /// igual a <see cref="Fallback"/> e pode ser substituída, uma vez, por
    /// <c>CatalogProvider</c> em <c>App.xaml.cs</c> — antes de qualquer ViewModel ser
    /// construído — quando um catálogo remoto assinado é obtido e verificado com
    /// sucesso. Consumidores continuam lendo <see cref="All"/> sem saber a origem.
    /// </summary>
    public static class DistroCatalog
    {
        // Declarado ANTES de All de propósito: inicializadores de campo estático rodam na
        // ordem do código-fonte, e All = Fallback só captura a lista real se Fallback já
        // tiver sido inicializado quando essa linha executar.

        /// <summary>O catálogo compilado no executável, nunca sobrescrito em runtime. Existe
        /// separado de <see cref="All"/> para dois usos: <c>CatalogProvider</c> precisa dele
        /// como fonte dos assets locais (imagens) ao mesclar um documento remoto (D14: pack://
        /// nunca vem do documento remoto), e o teste de paridade de forma (D9) precisa comparar
        /// contra ele especificamente, não contra o que estiver em <see cref="All"/> no momento.</summary>
        public static IReadOnlyList<DistroInfo> Fallback { get; } = new List<DistroInfo>
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
                // Único mecanismo desatendido declarado no catálogo. Subiquity é o instalador
                // nativo do Ubuntu 23.04+: o app prepara o disco e o boot, entrega um
                // `autoinstall:` como user-data de cloud-init, e o instalador da própria distro
                // faz a instalação depois do reboot.
                //
                // As demais distros ficam em None por decisão, não por esquecimento: declarar um
                // mecanismo é prometer automação validada de ponta a ponta, e o padrão de uma
                // entrada nova é não prometer (ver UnattendedInstall_DefaultsToNone).
                UnattendedInstall = UnattendedInstallMechanism.Subiquity,
                // De releases.ubuntu.com/24.04/SHA256SUMS, obtido em 2026-08-11.
                Sha256 = "3a4c9877b483ab46d7c3fbe165a0db275e1ae3cfe56a5657e5a47c2f99a99d1e",
                SizeBytes = 6655619072,
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
                // O boot pela staging foi exercitado (foi ele que expôs o initrd.lz), mas a
                // instalação em si nunca foi validada de ponta a ponta aqui. "Testado" para o
                // usuário significa que instalar funciona, não que a ISO inicia — e o Mint é
                // justamente a distro onde essa diferença custou a ESP de alguém.
                IsTested = false,
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
                // De mirrors.edge.kernel.org/linuxmint/stable/22.3/sha256sum.txt, obtido em
                // 2026-08-11 — o mirror direto (portalidea) não publica checksum próprio.
                Sha256 = "a081ab202cfda17f6924128dbd2de8b63518ac0531bcfe3f1a1b88097c459bd4",
                SizeBytes = 3091660800,
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
                // De mirror.umd.edu/zorin/18/SHA256SUMS.txt, obtido em 2026-08-11.
                Sha256 = "934df8549b44f8d72db8348fddd1b08f99cdd46cfee8660206fa9ec814ace69d",
                SizeBytes = 3787948032,
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
                // De iso.pop-os.org/24.04/amd64/generic/22/SHA256SUMS, obtido em 2026-08-11.
                Sha256 = "ca85fa8515770153dbe254ab93d406a53f2abdf10068da75b8d49018e92a5a51",
                SizeBytes = 3077963776,
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
                // Do CHECKSUM assinado ao lado da própria ISO, obtido em 2026-08-11.
                Sha256 = "181fe3e265fb5850c929f5afb7bdca91bb433b570ef39ece4a7076187435fdab",
                SizeBytes = 3265843200,
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
                //
                // Sem Sha256/SizeBytes de propósito: o arquivo por trás deste link não é o
                // Kubuntu (é o instalador do Pop!_OS acima), então não existe hash de "Kubuntu"
                // correto para declarar aqui — declarar o hash do Pop!_OS sob o Id do Kubuntu
                // legitimaria a troca como se fosse a distro certa.
                //
                // IsEnabled = false (2026-08-11): oculta do
                // catálogo e do seletor de download até o link real ser corrigido. Reabilitar
                // exige trocar DirectDownloadLink pelo instalador certo do Kubuntu e preencher
                // Sha256/SizeBytes a partir da fonte oficial dele.
                IsEnabled = false,
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
                // De cdimage.ubuntu.com/xubuntu/releases/25.10/release/SHA256SUMS (fonte
                // canônica; o mirror direto ftp.ussg.iu.edu não publica checksum próprio),
                // obtido em 2026-08-11.
                Sha256 = "f6a82db0a34731fd64f4a0ed15b653ab531b058a988830d164b1a0d1c00ba0a6",
                SizeBytes = 4721451008,
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
                // Do .sha256 publicado ao lado da própria ISO em download.manjaro.org, obtido
                // em 2026-08-11.
                Sha256 = "d1b66b5a8638174ae8c745a1310df6e9991c217cfc76aa5f8dd933aa356acce0",
                SizeBytes = 5028161536,
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
                // O boot pela staging chegou à sessão live em VM (2026-08-11), o que também
                // provou o ntfs3 no initramfs. Ainda assim fica FALSE: as tasks 8.2 a 8.6 do
                // change regional-settings-and-arch-autoinstall (dry-run com a configuração
                // real, instalação completa em VM com a ESP do Windows intacta, ambiente
                // gráfico subindo sozinho) não passaram. Bootar o live e instalar com
                // segurança são coisas diferentes, e é a segunda que o usuário vê.
                IsTested = false,
                // Sem mecanismo declarado até 8.2–8.6 passarem (§7.1). O gerador do script,
                // o preparer e os testes continuam no código, prontos — é a declaração aqui
                // que decide se o caminho é alcançável, e ela é a última coisa a mudar.
                UnattendedInstall = UnattendedInstallMechanism.None,
                // De mirror.adectra.com/archlinux/iso/2026.08.01/sha256sums.txt, obtido em
                // 2026-08-11. Esta entrada expira (ver comentário acima) — revalidar hash e
                // tamanho junto de Version/DirectDownloadLink a cada release do checklist.
                Sha256 = "4e82dced1c4fd3e498b22a853f8db2a4d262d32b97e7e07d97390d9e425ffe5e",
                SizeBytes = 1597014016,
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
                // Sem Sha256/SizeBytes de propósito: a fonte oficial do EndeavourOS só publica
                // checksum em SHA-512 para esta ISO (ver *.sha512sum ao lado do arquivo),
                // nunca em SHA-256. Calcular um SHA-256 a partir do arquivo baixado provaria
                // só consistência consigo mesmo, não que o arquivo é o que deveria ser — a
                // mesma razão pela qual a task 1.10 proíbe hash calculado localmente.
                //
                // IsEnabled = false (2026-08-11): oculta até a
                // distro publicar SHA-256, ou até este catálogo suportar SHA-512 como
                // alternativa (fora de escopo desta mudança).
                IsEnabled = false,
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
                // 2025.4 (a versão anterior aqui) já era 404 em produção quando este hash foi
                // levantado em 2026-08-11 — mesma classe de bug documentada no comentário do
                // Arch acima: o mirror do Kali também não guarda releases antigas
                // indefinidamente. Bump para a última disponível na origem no momento da
                // verificação de artefato (mudança do modelo transacional de segurança, task 1.10).
                Version = "2026.2",
                CreatedYear = "2013",
                BeginnerRating = 1,
                // De cdimage.kali.org/kali-2026.2/SHA256SUMS, obtido em 2026-08-11.
                Sha256 = "6dbefacc95e3b556c19c48e8bae39b8b505e2d3a1aba0bfb7ab62b036c3d2ba3",
                SizeBytes = 4802531328,
                // IsEnabled = false (decisão do usuário, 2026-08-11): despriorizada por ora
                // junto com Kubuntu/EndeavourOS enquanto o foco fica nas distros já 100% ok
                // (hash publicado pela fonte oficial e link vivo). Hash e link já corrigidos
                // acima — reabilitar é só trocar o flag de volta quando fizer sentido.
                IsEnabled = false,
                ImagePath = "pack://application:,,,/Assets/Images/kali.png",
                DownloadLink = "https://www.kali.org/get-kali/",
                DirectDownloadLink = "https://cdimage.kali.org/kali-2026.2/kali-linux-2026.2-installer-amd64.iso",
                CarouselImages = new[]
                {
                    "pack://application:,,,/Assets/Images/Kali/kali1.jpg",
                    "pack://application:,,,/Assets/Images/Kali/kali2.jpg"
                }
            },
        };

        /// <summary>Catálogo efetivo: o que a UI mostra. Padrão é <see cref="Fallback"/>;
        /// <c>CatalogProvider</c> é o único autorizado a substituir (internal set), sempre
        /// antes da primeira ViewModel ser construída — trocar depois não teria efeito nas
        /// ViewModels já construídas, que capturam a lista uma vez no próprio construtor.</summary>
        public static IReadOnlyList<DistroInfo> All { get; internal set; } = Fallback;

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
