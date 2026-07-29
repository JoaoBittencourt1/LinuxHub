using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Este é o código que roda como root no sistema do usuário e apaga partições. Os testes
    /// aqui cobrem as defesas, não a mecânica: o que importa é que ele se recuse a agir quando
    /// qualquer premissa não bate.
    /// </summary>
    public class PostInstallCleanupBuilderTests
    {
        private const string StagingUuid = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";
        private const string SeedUuid = "11111111-2222-3333-4444-555555555555";

        private static string Script() =>
            PostInstallCleanupBuilder.BuildCleanupScript(StagingUuid, SeedUuid);

        /// <summary>
        /// Índice de partição não serve: o instalador reescreve a tabela e os números mudam
        /// entre o momento em que o plano é montado e o primeiro boot.
        /// </summary>
        [Fact]
        public void Script_IdentifiesPartitionsByPartuuidOnly()
        {
            string script = Script();

            Assert.Contains("blkid -t PARTUUID=", script);
            Assert.Contains(StagingUuid.ToLowerInvariant(), script);
            Assert.Contains(SeedUuid.ToLowerInvariant(), script);
        }

        /// <summary>
        /// Rótulo E filesystem, os dois. Um só não basta: rótulo qualquer um copia, e tipo de
        /// filesystem é comum demais para identificar coisa nenhuma sozinho.
        /// </summary>
        [Fact]
        public void Script_ChecksBothLabelAndFilesystemBeforeDeleting()
        {
            string script = Script();

            Assert.Contains("-s LABEL", script);
            Assert.Contains("-s TYPE", script);
            Assert.Contains(StagingPartitionService.VolumeLabel, script);
            Assert.Contains(CloudInitSeedWriter.VolumeLabel, script);

            Assert.True(
                script.IndexOf("-s LABEL", StringComparison.Ordinal)
                    < script.IndexOf("sfdisk --delete", StringComparison.Ordinal),
                "a conferência precisa vir ANTES do delete, senão não protege nada.");
        }

        /// <summary>Partição montada significa que alguém depende dela — e a premissa inteira
        /// deste script é que ninguém mais precisa dessas duas.</summary>
        [Fact]
        public void Script_NeverDeletesAMountedPartition()
        {
            Assert.Contains("/proc/mounts", Script());
        }

        /// <summary>Rodar de novo a cada boot não mudaria o resultado e manteria um script que
        /// apaga partição instalado para sempre na máquina do usuário.</summary>
        [Fact]
        public void Script_DisablesItselfAndRemovesItsOwnFiles()
        {
            string script = Script();

            Assert.Contains("systemctl disable", script);
            Assert.Contains("rm -f", script);
        }

        /// <summary>
        /// O script viaja em base64 para o alvo. Inline exigiria escapar <c>$</c> duas vezes —
        /// uma para o YAML, outra para o shell — e cada nível de escape é uma chance de o
        /// script chegar corrompido. Num script que apaga partição, isso é inaceitável.
        /// </summary>
        [Fact]
        public void LateCommand_ShipsTheScriptAsBase64()
        {
            string command = PostInstallCleanupBuilder.BuildLateCommand(StagingUuid, SeedUuid);

            Assert.Contains("base64 -d", command);
            Assert.Contains("systemctl enable", command);
            Assert.DoesNotContain("#!/bin/sh", command);
        }

        [Fact]
        public void Script_UsesUnixLineEndings()
        {
            // O destino é um sistema Linux: um '\r' fantasma no shebang faz o kernel não achar
            // o interpretador, e o serviço falha em silêncio no primeiro boot.
            Assert.DoesNotContain("\r", Script());
            Assert.DoesNotContain("\r", PostInstallCleanupBuilder.BuildUnitFile());
        }

        [Fact]
        public void Build_RefusesEmptyIdentifiers()
        {
            Assert.Throws<ArgumentException>(
                () => PostInstallCleanupBuilder.Build("", SeedUuid, 4));
            Assert.Throws<ArgumentException>(
                () => PostInstallCleanupBuilder.Build(StagingUuid, "  ", 4));
        }
    }
}
