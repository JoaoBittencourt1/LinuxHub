using System.Linq;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class ArchinstallScriptBuilderTests
    {
        private static string Build(string json = "{ \"hostname\": \"pc\" }") =>
            ArchinstallScriptBuilder.Build(json, "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}");

        /// <summary>Executado por bash: um CR no fim da linha do shebang, ou em qualquer
        /// comando, quebra o script de formas que só apareceriam depois do reboot.</summary>
        [Fact]
        public void UsesUnixLineEndingsAndAShebang()
        {
            string script = Build();

            Assert.StartsWith("#!/usr/bin/env bash\n", script);
            Assert.DoesNotContain("\r", script);
        }

        /// <summary>O PARTUUID é o identificador que vale nos dois lados. O Windows reporta com
        /// chaves e em maiúsculas; o <c>blkid</c> compara em minúsculas e sem chaves.</summary>
        [Fact]
        public void ResolvesTheEspByPartuuid()
        {
            string script = Build();

            Assert.Contains("ESP_PARTUUID='aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'", script);
            Assert.Contains("blkid -t PARTUUID=", script);
            Assert.Contains("lsblk -no pkname", script);
        }

        /// <summary>
        /// O ponto inteiro do desenho: um alvo que não resolve interrompe ANTES de chamar o
        /// instalador. O archinstall aceita sem questionar qualquer <c>/dev/...</c> que exista,
        /// inclusive o de outro disco — deixar passar seria repetir 2026-08-05 com outro nome.
        /// </summary>
        [Fact]
        public void UnresolvedTarget_ExitsBeforeCallingTheInstaller()
        {
            string[] lines = Build().Split('\n');

            int firstGuard = Array.FindIndex(lines, line => line.Contains("exit 1"));
            int firstInstall = Array.FindIndex(lines, line => line.TrimStart().StartsWith("archinstall "));

            Assert.True(firstGuard >= 0, "o script precisa ter uma guarda que sai sem instalar");
            Assert.True(firstGuard < firstInstall, "a guarda precisa vir antes de chamar o archinstall");
        }

        /// <summary>Portão barato antes do caro: o dry-run desserializa a configuração inteira e
        /// sai antes de qualquer operação de disco.</summary>
        [Fact]
        public void ValidatesWithDryRunBeforeInstallingForReal()
        {
            string[] lines = Build().Split('\n').Select(l => l.Trim()).ToArray();

            int dryRun = Array.FindIndex(lines, line => line.Contains("--dry-run"));
            int real = Array.FindLastIndex(lines, line => line.StartsWith("archinstall ") && !line.Contains("--dry-run"));

            Assert.True(dryRun >= 0);
            Assert.True(dryRun < real);
        }

        [Fact]
        public void RunsUnattended() => Assert.Contains("--silent", Build());

        /// <summary>O JSON entra num here-document com delimitador entre aspas simples: sem
        /// isso o shell expandiria <c>$</c> de dentro do hash da senha.</summary>
        [Fact]
        public void EmbedsTheConfigWithoutShellExpansion()
        {
            string script = ArchinstallScriptBuilder.Build(
                "{ \"enc_password\": \"$6$salt$hash\" }", "{A-B}");

            Assert.Contains("<<'LINUXHUB_ARCHINSTALL_CONFIG'", script);
            Assert.Contains("$6$salt$hash", script);
        }

        /// <summary>Os dois valores que só a sessão live conhece são substituídos antes do
        /// instalador ler o arquivo.</summary>
        [Fact]
        public void ReplacesTheRuntimeOnlyPlaceholders()
        {
            string script = Build();

            Assert.Contains($"s|{ArchinstallConfigBuilder.DiskPathPlaceholder}|$DISK_PATH|g", script);
            Assert.Contains($"s|{ArchinstallConfigBuilder.EspPathPlaceholder}|$ESP_PATH|g", script);
        }
    }
}
