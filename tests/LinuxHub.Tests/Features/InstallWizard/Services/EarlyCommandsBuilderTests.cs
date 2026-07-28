using System.Linq;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// O script gerado aqui é a única coisa que resolve, em tempo de execução no Linux, qual
    /// disco físico o Windows escolheu — se o marcador que ele procura ou o valor que ele
    /// substitui divergirem do que <see cref="AutoinstallStorageBuilder"/> gera, o `match:`
    /// nunca é corrigido e o curtin aplica o storage no disco errado, silenciosamente.
    /// </summary>
    public class EarlyCommandsBuilderTests
    {
        [Fact]
        public void ForPartuuid_FindsThePartitionThenItsParentDisk()
        {
            string script = EarlyCommandsBuilder.BuildForPartuuid(
                "{6A1E2C3D-1111-2222-3333-444455556666}", indentSpaces: 4);

            // Sem chaves e em minúsculas: é como o `blkid` do Linux espera/reporta o PARTUUID.
            Assert.Contains("blkid -t PARTUUID=\"6a1e2c3d-1111-2222-3333-444455556666\" -o device", script);
            Assert.Contains("lsblk -no pkname", script);
        }

        [Fact]
        public void ForMbrSignature_FindsTheDiskDirectlyWithoutAParentLookup()
        {
            // A assinatura MBR é do disco inteiro, não de uma partição — diferente do PARTUUID,
            // não precisa de um segundo passo para achar o disco pai.
            string script = EarlyCommandsBuilder.BuildForMbrSignature("1A2B3C4D", indentSpaces: 4);

            Assert.Contains("blkid -t PTUUID=\"1a2b3c4d\" -o device", script);
            Assert.DoesNotContain("lsblk -no pkname", script);
        }

        [Fact]
        public void Build_NeverReadsSerialAsASecondaryLookup()
        {
            // Regressão de uma instalação real: o disco foi resolvido certo (o sed rodou e
            // substituiu o marcador), mas `match: serial:` ainda deu "matched no disk" — o
            // `lsblk -dno serial` e o probe interno do subiquity nem sempre derivam o serial do
            // mesmo jeito. A correção é nunca reintroduzir essa segunda leitura: usar
            // diretamente o `$disk` já resolvido, sem reinterpretar através de outra
            // ferramenta.
            string script = EarlyCommandsBuilder.BuildForPartuuid("guid-de-teste", indentSpaces: 4);

            Assert.DoesNotContain("serial", script);
        }

        [Fact]
        public void Build_SubstitutesExactlyTheMarkerThatAutoinstallStorageBuilderEmits()
        {
            // Mitigação do risco registrado em design.md (D3): se o marcador divergir entre os
            // dois lados, o sed não encontra nada e o match: continua com o placeholder — o
            // curtin morreria com "matched no disk" silenciosamente, sem avisar por quê.
            string script = EarlyCommandsBuilder.BuildForPartuuid("guid-de-teste", indentSpaces: 4);

            Assert.Contains(
                $"sed -i \"s|{EarlyCommandsBuilder.DiskPathPlaceholder}|$disk|\" /autoinstall.yaml",
                script);
        }

        [Fact]
        public void Build_UsesAPipeDelimiterInSedBecauseTheSubstitutedValueIsAPath()
        {
            // $disk é algo como "/dev/sda" — um delimitador `/` no sed quebraria o comando.
            string script = EarlyCommandsBuilder.BuildForPartuuid("guid-de-teste", indentSpaces: 4);

            Assert.Contains("sed -i \"s|", script);
            Assert.DoesNotContain("sed -i \"s/", script);
        }

        [Fact]
        public void Build_StopsOnTheFirstFailingCommand()
        {
            // Sem `set -e`, um `blkid`/`lsblk` que falhar deixaria $disk vazio e o sed
            // continuaria rodando sobre um match: errado, sem avisar ninguém.
            string script = EarlyCommandsBuilder.BuildForPartuuid("guid-de-teste", indentSpaces: 4);

            Assert.Contains("set -e", script);
        }

        [Fact]
        public void Build_IndentsAsALiteralBlockScalarUnderTheDashItem()
        {
            string script = EarlyCommandsBuilder.BuildForPartuuid("guid-de-teste", indentSpaces: 4);

            string[] lines = script.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            Assert.Equal("    - |", lines[0]);
            Assert.All(lines.Skip(1), line => Assert.StartsWith("      ", line));
        }
    }
}
