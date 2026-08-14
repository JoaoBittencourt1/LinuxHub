using System.Text.Json;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// A configuração gerada é a única peça capaz de apagar o Windows. Os testes aqui travam o
    /// que separa "instalar ao lado" de "reparticionar o disco", e são lidos como JSON de
    /// verdade — comparar substring deixaria passar um documento sintaticamente quebrado, que
    /// só falharia depois do reboot.
    /// </summary>
    public class ArchinstallConfigBuilderTests
    {
        private const long Gib = 1024L * 1024 * 1024;

        /// <summary>Disco parecido com o real depois do shrink: ESP do Windows, Windows, e o
        /// vão livre que o wizard acabou de abrir no fim.</summary>
        private static DiskLayout CreateDisk() => new(
            Index: 0,
            SerialNumber: "S1",
            Model: "NVMe",
            SizeBytes: 512 * Gib,
            IsGpt: true,
            IsLargestDisk: true,
            IsSmallestDisk: true,
            Partitions:
            [
                new PartitionLayout(
                    Number: 1,
                    OffsetBytes: 1024 * 1024,
                    SizeBytes: 100L * 1024 * 1024,
                    GptType: "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}",
                    IsEfiSystemPartition: true,
                    Guid: "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"),
                new PartitionLayout(
                    Number: 2,
                    OffsetBytes: 200L * 1024 * 1024,
                    SizeBytes: 300 * Gib,
                    GptType: "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}",
                    IsEfiSystemPartition: false,
                    Guid: "{11111111-2222-3333-4444-555555555555}"),
            ]);

        private static InstallerConfig CreateConfig() => new()
        {
            Username = "joao",
            Hostname = "pc",
            Locale = "pt_BR.UTF-8",
            Keymap = "br",
            Timezone = "America/Sao_Paulo",
            BootMode = "uefi",
            InstallMode = "dualboot",
        };

        private static JsonElement BuildJson(InstallerConfig? config = null) =>
            JsonDocument.Parse(
                ArchinstallConfigBuilder.Build(config ?? CreateConfig(), CreateDisk(), "$6$salt$hash"))
                .RootElement;

        [Fact]
        public void GeneratesValidJson() => Assert.Equal(JsonValueKind.Object, BuildJson().ValueKind);

        /// <summary>
        /// As duas declarações que separam "instalar ao lado" de "apagar o Windows".
        /// <c>wipe: true</c> faria o archinstall recriar a tabela de partições inteira, e uma
        /// ESP sem <c>status: "existing"</c> seria formatada — levando junto os arquivos de
        /// boot do Windows. Foi essa classe de erro que custou a ESP do usuário em 2026-08-05.
        /// </summary>
        [Fact]
        public void NeverWipesTheDevice_AndKeepsTheWindowsEspAsExisting()
        {
            JsonElement device = BuildJson()
                .GetProperty("disk_config")
                .GetProperty("device_modifications")[0];

            Assert.False(device.GetProperty("wipe").GetBoolean());

            JsonElement esp = device.GetProperty("partitions")[0];
            Assert.Equal("existing", esp.GetProperty("status").GetString());

            // Sem dev_path o próprio archinstall recusa uma partição marcada como existente —
            // é ele que aponta qual partição é.
            Assert.Equal(ArchinstallConfigBuilder.EspPathPlaceholder, esp.GetProperty("dev_path").GetString());
        }

        /// <summary>Só a raiz é criada; nenhuma partição existente é declarada para apagar.</summary>
        [Fact]
        public void CreatesOnlyTheRootPartition()
        {
            JsonElement partitions = BuildJson()
                .GetProperty("disk_config")
                .GetProperty("device_modifications")[0]
                .GetProperty("partitions");

            string[] statuses = partitions.EnumerateArray()
                .Select(p => p.GetProperty("status").GetString()!)
                .ToArray();

            Assert.Equal(["existing", "create"], statuses);
            Assert.DoesNotContain("delete", statuses);
        }

        /// <summary>
        /// systemd-boot coloca kernel e initramfs DENTRO da ESP e precisa de centenas de MB —
        /// não cabe nos 100 MB que o Windows cria, e aumentar a ESP exigiria deslocar o próprio
        /// Windows. O GRUB usa poucos megabytes dela (design.md, decisão 4).
        /// </summary>
        [Fact]
        public void UsesGrub_NotSystemdBoot()
        {
            JsonElement bootloader = BuildJson().GetProperty("bootloader_config");

            Assert.Equal("Grub", bootloader.GetProperty("bootloader").GetString());
        }

        /// <summary>
        /// O default do archinstall para <c>removable</c> é TRUE, e com ele o GRUB vai para
        /// <c>\EFI\BOOT\BOOTX64.EFI</c> — o caminho de fallback do firmware, que numa máquina
        /// com Windows já está ocupado. Omitir a chave não é neutro, por isso o teste.
        /// </summary>
        [Fact]
        public void TurnsOffTheRemovableInstall()
        {
            Assert.False(BuildJson().GetProperty("bootloader_config").GetProperty("removable").GetBoolean());
        }

        /// <summary>
        /// A ESP vai em <c>/efi</c>, não em <c>/boot</c>: com ela em <c>/boot</c>, o kernel e os
        /// dois initramfs do Arch iriam para dentro dos 100 MB do Windows e não caberiam.
        /// </summary>
        [Fact]
        public void MountsTheEspOutsideBoot_SoTheKernelStaysOnRoot()
        {
            JsonElement partitions = BuildJson()
                .GetProperty("disk_config")
                .GetProperty("device_modifications")[0]
                .GetProperty("partitions");

            Assert.Equal("/efi", partitions[0].GetProperty("mountpoint").GetString());
            Assert.Equal("/", partitions[1].GetProperty("mountpoint").GetString());
        }

        /// <summary>O archinstall recusa partição a criar desalinhada ("Partition is
        /// misaligned"), e o erro só apareceria na sessão live.</summary>
        [Fact]
        public void AlignsTheNewPartitionToOneMebiByte()
        {
            JsonElement root = BuildJson()
                .GetProperty("disk_config")
                .GetProperty("device_modifications")[0]
                .GetProperty("partitions")[1];

            long start = root.GetProperty("start").GetProperty("value").GetInt64();
            long size = root.GetProperty("size").GetProperty("value").GetInt64();

            Assert.Equal(0, start % (1024 * 1024));
            Assert.Equal(0, size % (1024 * 1024));
            Assert.True(size > 0);
        }

        /// <summary>Os mesmos três campos da detecção regional, no formato que o archinstall
        /// espera — sem conversão pelo caminho.</summary>
        [Fact]
        public void CarriesTheRegionalSettings()
        {
            JsonElement json = BuildJson();
            JsonElement locale = json.GetProperty("locale_config");

            Assert.Equal("br", locale.GetProperty("kb_layout").GetString());
            Assert.Equal("pt_BR.UTF-8", locale.GetProperty("sys_lang").GetString());
            Assert.Equal("America/Sao_Paulo", json.GetProperty("timezone").GetString());
        }

        /// <summary>Instalar o ambiente sem o meio de iniciá-lo entrega uma máquina que liga
        /// num terminal — para o público deste app, indistinguível de uma falha.</summary>
        [Fact]
        public void DesktopEnvironment_ComesWithItsGreeter()
        {
            var config = CreateConfig();
            config.DesktopEnvironment = "GNOME";

            JsonElement profile = BuildJson(config).GetProperty("profile_config");

            Assert.Equal("Desktop", profile.GetProperty("profile").GetProperty("main").GetString());
            Assert.Equal("GNOME", profile.GetProperty("profile").GetProperty("details")[0].GetString());
            Assert.Equal("sddm", profile.GetProperty("greeter").GetString());
        }

        /// <summary>Sem ambiente escolhido o Arch é instalado limpo — o padrão da distro, e uma
        /// escolha legítima, não um estado por preencher.</summary>
        [Fact]
        public void WithoutDesktopEnvironment_EmitsNoProfile() =>
            Assert.False(BuildJson().TryGetProperty("profile_config", out _));

        /// <summary>A senha vai como hash, igual ao caminho do subiquity: o script fica gravado
        /// no disco do Windows, e texto puro ali seria legível por qualquer um.</summary>
        [Fact]
        public void UserPasswordGoesAsAHash()
        {
            JsonElement user = BuildJson().GetProperty("users")[0];

            Assert.Equal("joao", user.GetProperty("username").GetString());
            Assert.Equal("$6$salt$hash", user.GetProperty("enc_password").GetString());
            Assert.True(user.GetProperty("sudo").GetBoolean());
        }

        /// <summary>
        /// A pilha aberta funciona em qualquer GPU. Detectar a placa e escolher o driver
        /// "melhor" erraria para cima em alguns casos (o módulo Nvidia aberto só serve
        /// Turing+), e uma máquina que liga sem vídeo é um erro que só aparece depois do
        /// reboot.
        /// </summary>
        [Fact]
        public void GraphicsDriver_IsTheOpenSourceStack()
        {
            var config = CreateConfig();
            config.DesktopEnvironment = "GNOME";

            Assert.Equal(
                "All open-source",
                BuildJson(config).GetProperty("profile_config").GetProperty("gfx_driver").GetString());
        }

        /// <summary>Baixar pacotes de perto em vez de sortear um servidor do outro lado do
        /// mundo. A região sai do locale que o usuário já revisou, não de uma segunda leitura
        /// que poderia divergir do que ele viu.</summary>
        [Fact]
        public void MirrorRegion_FollowsTheChosenLocale()
        {
            JsonElement regions = BuildJson()
                .GetProperty("mirror_config")
                .GetProperty("mirror_regions");

            Assert.True(regions.TryGetProperty("Brazil", out _));
        }

        /// <summary>Sem mirror publicado no país, nenhuma região é declarada — e o archinstall
        /// usa a mirrorlist global que a ISO já traz. Chutar um país vizinho seria pior.</summary>
        [Fact]
        public void MirrorRegion_UnknownCountry_IsOmittedInsteadOfGuessed()
        {
            var config = CreateConfig();
            config.Locale = "en_ZW.UTF-8";

            Assert.False(BuildJson(config).TryGetProperty("mirror_config", out _));
        }

        /// <summary>No archinstall, "swap" é zram: comprime em RAM em vez de reservar disco —
        /// o comportamento certo para uma instalação que divide o disco com o Windows.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Swap_FollowsTheConfiguration(bool enabled)
        {
            var config = CreateConfig();
            config.SwapEnabled = enabled;

            Assert.Equal(enabled, BuildJson(config).GetProperty("swap").GetBoolean());
        }

        [Fact]
        public void InstallsTheStandardKernel()
        {
            JsonElement kernels = BuildJson().GetProperty("kernels");

            Assert.Equal("linux", Assert.Single(kernels.EnumerateArray()).GetString());
        }

        /// <summary>Sem ESP não há onde o GRUB se instalar, e criar uma exigiria mexer no
        /// particionamento do Windows. Parar aqui deixa a máquina intacta.</summary>
        [Fact]
        public void WithoutAnEsp_Refuses()
        {
            var diskWithoutEsp = CreateDisk() with
            {
                Partitions = [new PartitionLayout(1, 1024 * 1024, 300 * Gib, "", false, Guid: "{X}")]
            };

            Assert.Throws<InvalidOperationException>(
                () => ArchinstallConfigBuilder.Build(CreateConfig(), diskWithoutEsp, "$6$h"));
        }

        /// <summary>Espaço livre insuficiente é recusado antes de qualquer escrita, e não no
        /// meio da instalação — depois do ponto de não-retorno.</summary>
        [Fact]
        public void WithoutEnoughFreeSpace_Refuses()
        {
            var fullDisk = CreateDisk() with { SizeBytes = 302 * Gib };

            Assert.Throws<InvalidOperationException>(
                () => ArchinstallConfigBuilder.Build(CreateConfig(), fullDisk, "$6$h"));
        }
    }
}
