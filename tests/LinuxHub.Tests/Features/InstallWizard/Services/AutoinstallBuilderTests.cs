using System.Linq;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class AutoinstallBuilderTests
    {
        private const long Gib = 1024L * 1024 * 1024;
        private const string SampleHash = "$6$abcdefghijklmnop$zD.aVfOby4QY3jq5toBjfWeSgwmLqKARLs7Vup6khwKiyvYBRXnhkr4ZhWkw1SIzbVX2xUCNlGOfcCQ0QN21m0";
        private const int SeedPartitionNumber = 3;

        /// <summary>A partição que hospeda a ISO no modo substituir. No dual-boot a ISO fica
        /// no volume do Windows e este parâmetro fica nulo.</summary>
        private static readonly StagingPartition Staging = new(0, 5, "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

        private static string BuildDualBootUserData(
            InstallerConfig? config = null, DiskLayout? disk = null) =>
            AutoinstallBuilder.BuildUserData(
                config ?? BuildConfig(), disk ?? BuildDisk(), SampleHash, SeedPartitionNumber, staging: null);
        private static DiskLayout BuildDisk() => new(
            Index: 0,
            SerialNumber: "SERIAL123",
            Model: "NVMe de teste",
            SizeBytes: 400 * Gib,
            IsGpt: true,
            IsLargestDisk: true,
            IsSmallestDisk: true,
            Partitions: new[]
            {
                new PartitionLayout(1, 1024 * 1024, 200L * 1024 * 1024, "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}", true),
                new PartitionLayout(SeedPartitionNumber, 210L * 1024 * 1024, 200 * Gib, "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}", false)
            });

        private static InstallerConfig BuildConfig() => new()
        {
            Username = "joao",
            Hostname = "linuxhub-pc",
            Password = "segredo",
            Locale = "pt_BR.UTF-8",
            Keymap = "br",
            Timezone = "America/Sao_Paulo",
            BootMode = "uefi",
            InstallMode = "dualboot",
            SwapEnabled = true,
            SwapSizeGb = 8
        };

        [Fact]
        public void UserData_StartsWithTheCloudConfigHeader()
        {
            string yaml = BuildDualBootUserData();

            // O cloud-init só reconhece o arquivo se a PRIMEIRA linha for exatamente esta.
            Assert.StartsWith("#cloud-config\n", yaml);
            Assert.Contains("autoinstall:", yaml);
            Assert.Contains("version: 1", yaml);
        }

        [Fact]
        public void UserData_UsesUnixLineEndingsOnly()
        {
            string yaml = BuildDualBootUserData();

            // Mesma armadilha do grub.cfg: um '\r' sobrando entra no valor do YAML.
            Assert.DoesNotContain("\r", yaml);
        }

        [Fact]
        public void UserData_CarriesTheAccountAndSystemChoicesFromTheWizard()
        {
            string yaml = BuildDualBootUserData();

            Assert.Contains("username: 'joao'", yaml);
            Assert.Contains("hostname: 'linuxhub-pc'", yaml);
            Assert.Contains($"password: '{SampleHash}'", yaml);
            Assert.Contains("locale: 'pt_BR.UTF-8'", yaml);
            Assert.Contains("layout: 'br'", yaml);
            Assert.Contains("timezone: 'America/Sao_Paulo'", yaml);
        }

        [Fact]
        public void UserData_NeverContainsThePlainTextPassword()
        {
            string yaml = BuildDualBootUserData();

            Assert.DoesNotContain("segredo", yaml);
        }

        [Fact]
        public void UserData_DoesNotDeclareATopLevelSwapKey()
        {
            // Regressão: `swap:` no topo não existe no autoinstall. Numa instalação real o
            // subiquity respondeu "Unrecognized top-level key 'swap'" e avisou que versões
            // futuras vão transformar isso em erro. Ele já cria o /swap.img sozinho.
            string yaml = BuildDualBootUserData();

            Assert.DoesNotContain("swap:", yaml);
        }

        [Fact]
        public void UserData_OnlyDeclaresKeysTheAutoinstallSchemaRecognizes()
        {
            string yaml = BuildDualBootUserData();

            string[] topLevelKeys = yaml
                .Split('\n')
                .Where(line => line.StartsWith("  ") && !line.StartsWith("   ") && line.Contains(':'))
                .Select(line => line.Trim().Split(':')[0])
                .ToArray();

            string[] recognized =
            {
                "version", "refresh-installer", "early-commands", "locale", "keyboard", "timezone",
                "identity", "ssh", "storage", "shutdown"
            };

            Assert.All(topLevelKeys, key => Assert.Contains(key, recognized));
        }

        [Fact]
        public void UserData_DisablesTheInstallerSelfUpdate()
        {
            // Numa máquina sem rede o auto-update trava o instalador numa tela de espera,
            // que é o contrário de desatendido.
            string yaml = BuildDualBootUserData();

            Assert.Contains("refresh-installer:", yaml);
            Assert.Contains("update: false", yaml);
        }

        [Fact]
        public void UserData_DoesNotDeclareInteractiveSections()
        {
            // Qualquer seção interativa declarada faz o instalador parar e esperar alguém.
            string yaml = BuildDualBootUserData();

            Assert.DoesNotContain("interactive-sections", yaml);
        }

        [Theory]
        [InlineData("Joao")]      // maiúscula
        [InlineData("joao silva")] // espaço
        [InlineData("1joao")]     // começa com dígito
        [InlineData("")]
        public void UserData_RefusesUsernamesThatLinuxWouldReject(string username)
        {
            InstallerConfig config = BuildConfig();
            config.Username = username;

            var error = Assert.Throws<InvalidOperationException>(
                () => AutoinstallBuilder.BuildUserData(config, BuildDisk(), SampleHash, SeedPartitionNumber, staging: null));

            Assert.Contains("nome de usuário", error.Message);
        }

        [Theory]
        [InlineData("meu pc")]
        [InlineData("-pc")]
        [InlineData("pc-")]
        [InlineData("")]
        public void UserData_RefusesInvalidHostnames(string hostname)
        {
            InstallerConfig config = BuildConfig();
            config.Hostname = hostname;

            var error = Assert.Throws<InvalidOperationException>(
                () => AutoinstallBuilder.BuildUserData(config, BuildDisk(), SampleHash, SeedPartitionNumber, staging: null));

            Assert.Contains("nome de máquina", error.Message);
        }

        [Fact]
        public void UserData_HasNoEarlyCommandsWhenTheSeedPartitionHasNoKnownPartuuid()
        {
            // BuildDisk() não atribui Guid à partição semente, então a identidade cai no
            // critério de tamanho — sem nada para resolver em tempo de execução.
            string yaml = BuildDualBootUserData();

            Assert.DoesNotContain("early-commands:", yaml);
        }

        [Fact]
        public void UserData_IncludesEarlyCommandsWhenTheSeedPartitionHasAKnownPartuuid()
        {
            DiskLayout disk = BuildDisk() with
            {
                Partitions = BuildDisk().Partitions
                    .Select(p => p.Number == SeedPartitionNumber
                        ? p with { Guid = "{6a1e2c3d-1111-2222-3333-444455556666}" }
                        : p)
                    .ToList()
            };

            string yaml = AutoinstallBuilder.BuildUserData(BuildConfig(), disk, SampleHash, SeedPartitionNumber, staging: null);

            Assert.Contains("early-commands:", yaml);
            Assert.Contains("blkid -t PARTUUID=\"6a1e2c3d-1111-2222-3333-444455556666\"", yaml);

            // early-commands precisa vir antes de storage: no texto gerado, já que é onde o
            // curtin lê o match: que ele reescreve.
            Assert.True(
                yaml.IndexOf("early-commands:", StringComparison.Ordinal) <
                yaml.IndexOf("storage:", StringComparison.Ordinal));
        }

        [Fact]
        public void MetaData_CarriesAnInstanceId()
        {
            // Sem meta-data ao lado, o cloud-init não reconhece a fonte NoCloud e ignora o
            // user-data inteiro.
            Assert.Equal("instance-id: linuxhub-abc\n", AutoinstallBuilder.BuildMetaData("linuxhub-abc"));
        }

        [Fact]
        public void UserData_ReplaceMode_RequiresStaging()
        {
            InstallerConfig config = BuildConfig();
            config.InstallMode = "replace";

            var error = Assert.Throws<InvalidOperationException>(
                () => AutoinstallBuilder.BuildUserData(
                    config, BuildDisk(), SampleHash, SeedPartitionNumber, staging: null));

            Assert.Contains("substituir", error.Message);
        }

        [Fact]
        public void UserData_DualBoot_DoesNotRequireStaging()
        {
            // Dual-boot preserva o volume do Windows onde a ISO mora — staging seria custo
            // sem ganho (o clear-holders do substituir é que originou a partição dedicada).
            string yaml = BuildDualBootUserData();

            Assert.DoesNotContain(Staging.PartitionUuid.ToLowerInvariant(), yaml);
        }
    }
}
