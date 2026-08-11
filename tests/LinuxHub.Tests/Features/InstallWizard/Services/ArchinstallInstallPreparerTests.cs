using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class ArchinstallInstallPreparerTests
    {
        private const long Gib = 1024L * 1024 * 1024;

        private sealed class FakeDiskLayoutProvider : IDiskLayoutProvider
        {
            public DiskLayout Layout { get; set; } = new(
                Index: 0,
                SerialNumber: "S1",
                Model: "NVMe",
                SizeBytes: 512 * Gib,
                IsGpt: true,
                IsLargestDisk: true,
                IsSmallestDisk: true,
                Partitions:
                [
                    new PartitionLayout(1, 1024 * 1024, 100L * 1024 * 1024, "", true,
                        Guid: "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"),
                    new PartitionLayout(2, 200L * 1024 * 1024, 300 * Gib, "", false,
                        Guid: "{11111111-2222-3333-4444-555555555555}"),
                ]);

            public DiskLayout GetLayout(int diskIndex) => Layout;
        }

        private sealed class CapturingScriptWriter : IArchinstallScriptWriter
        {
            public string? Script { get; private set; }

            public string Write(string script, string isoWindowsPath)
            {
                Script = script;
                return "/ISOs/" + ArchinstallScriptBuilder.FileName;
            }
        }

        private static InstallerConfig CreateConfig() => new()
        {
            Username = "joao",
            Password = "secret",
            Hostname = "pc",
            Locale = "pt_BR.UTF-8",
            Keymap = "br",
            Timezone = "America/Sao_Paulo",
            BootMode = "uefi",
            InstallMode = "dualboot",
            IsoPath = @"C:\ISOs\archlinux.iso",
        };

        private static (UnattendedPreparationResult Result, CapturingScriptWriter Writer) Prepare(
            InstallerConfig? config = null, StagingPartition? staging = null)
        {
            var writer = new CapturingScriptWriter();
            var preparer = new ArchinstallInstallPreparer(new FakeDiskLayoutProvider(), writer);

            return (preparer.Prepare(config ?? CreateConfig(), diskIndex: 0, staging), writer);
        }

        [Fact]
        public void DeclaresTheArchinstallMechanism() =>
            Assert.Equal(
                UnattendedInstallMechanism.Archinstall,
                new ArchinstallInstallPreparer(new FakeDiskLayoutProvider(), new CapturingScriptWriter()).Mechanism);

        /// <summary>
        /// O caminho do script é resolvido a partir de onde o archiso monta o volume que
        /// hospeda a ISO. Errar isso não dá erro visível: o <c>.automated_script.sh</c>
        /// simplesmente não acha o arquivo e a instalação vira interativa depois do reboot.
        /// </summary>
        [Fact]
        public void PointsTheBootParameterAtTheScriptInsideTheLiveSession()
        {
            var (result, _) = Prepare();

            Assert.True(result.BootParameters.IsUnattended);
            Assert.Contains(
                $"script={ArchisoIsoBootEntryBuilder.HostPartitionMountPoint}/ISOs/{ArchinstallScriptBuilder.FileName}",
                result.BootParameters.KernelParameters);
        }

        /// <summary>
        /// Sem <c>copytoram=n</c> o archiso copia o squashfs para a RAM e desmonta a partição
        /// que hospeda a ISO ainda no initramfs — o script sumiria antes do login. O default é
        /// <c>auto</c>, que vira <c>y</c> em qualquer máquina com RAM sobrando, então este
        /// parâmetro é a diferença entre automatizar e não fazer nada.
        /// </summary>
        [Fact]
        public void KeepsTheHostPartitionMounted() =>
            Assert.Contains("copytoram=n", Prepare().Result.BootParameters.KernelParameters);

        [Fact]
        public void ScriptResolvesTheEspByPartuuid_InLowercaseWithoutBraces()
        {
            var (_, writer) = Prepare();

            Assert.Contains(
                "ESP_PARTUUID='aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'",
                writer.Script);
        }

        /// <summary>Este mecanismo não cria partição semente — quem monta a configuração é o
        /// script, já dentro da sessão live.</summary>
        [Fact]
        public void CreatesNoSeedPartition() => Assert.Equal(0, Prepare().Result.SeedPartitionNumber);

        /// <summary>
        /// No modo substituir a ISO fica numa partição do próprio disco a ser reparticionado, e
        /// o <c>copytoram=n</c> a mantém presa durante toda a instalação. Enquanto isso não for
        /// exercitado em VM, recusar é o comportamento correto: automação incompleta é
        /// preferível a automação insegura (§6.1).
        /// </summary>
        [Fact]
        public void ReplaceMode_RefusesInsteadOfGuessing()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => Prepare(staging: new StagingPartition(0, 9, "{GUID}")));

            Assert.Contains("substituir", ex.Message);
        }

        /// <summary>Num boot legado não existe ESP para reaproveitar, e todo o desenho do
        /// bootloader parte dela.</summary>
        [Fact]
        public void BiosBoot_RefusesInsteadOfGuessing()
        {
            var config = CreateConfig();
            config.BootMode = "bios";

            Assert.Throws<InvalidOperationException>(() => Prepare(config));
        }

        /// <summary>Sem PARTUUID não há como nomear o alvo, e adivinhar o caminho do disco é
        /// exatamente o que não pode acontecer.</summary>
        [Fact]
        public void EspWithoutAStableIdentifier_RefusesInsteadOfGuessing()
        {
            var provider = new FakeDiskLayoutProvider();
            provider.Layout = provider.Layout with
            {
                Partitions = [new PartitionLayout(1, 1024 * 1024, 100L * 1024 * 1024, "", true, Guid: "")]
            };

            var preparer = new ArchinstallInstallPreparer(provider, new CapturingScriptWriter());

            Assert.Throws<InvalidOperationException>(
                () => preparer.Prepare(CreateConfig(), diskIndex: 0, staging: null));
        }
    }
}
