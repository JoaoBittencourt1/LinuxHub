using System.Collections.Generic;
using LinuxHub.Common.Data;
using LinuxHub.Common.Models;
using Xunit;

namespace LinuxHub.Tests.Common.Data
{
    /// <summary>
    /// A detecção por nome de arquivo decide mais do que o rótulo na tela: é o
    /// <see cref="DistroInfo.UnattendedInstall"/> da distro casada que libera (ou não) a
    /// instalação automática no wizard, e com qual mecanismo.
    /// </summary>
    public class DistroCatalogTests
    {
        /// <summary>
        /// "xubuntu"/"kubuntu" contêm "ubuntu", e o casamento por substring pegava a primeira
        /// entrada do catálogo — o Ubuntu, a única distro com autoinstall validado. Uma ISO de
        /// Xubuntu selecionada manualmente aparecia como Ubuntu e ganhava o toggle de
        /// instalação automática, que nunca foi testado nela.
        /// </summary>
        [Theory]
        [InlineData("xubuntu-25.10-desktop-amd64.iso", "xubuntu")]
        [InlineData("kubuntu-24.04-desktop-amd64.iso", "kubuntu")]
        [InlineData("ubuntu-24.04.4-desktop-amd64.iso", "ubuntu")]
        public void FindByIsoFileName_PrefersTheMostSpecificDistro(string fileName, string expectedId)
        {
            var distro = DistroCatalog.FindByIsoFileName(fileName);

            Assert.NotNull(distro);
            Assert.Equal(expectedId, distro!.Id);
        }

        [Fact]
        public void FindByIsoFileName_UnknownName_ReturnsNull() =>
            Assert.Null(DistroCatalog.FindByIsoFileName("slackware-15.0-install-dvd.iso"));

        /// <summary>Só quem foi validado de ponta a ponta declara mecanismo — é o que sustenta
        /// o toggle de instalação automática aparecer nessas distros e em mais nenhuma. O teste
        /// fixa o mecanismo de cada uma, e não só "tem ou não tem": declarar o mecanismo errado
        /// gera um preseed para quem espera autoinstall (ou vice-versa), o que só apareceria
        /// num boot real.
        ///
        /// O Mint não está aqui por decisão, não por pendência: o teste em VM de 2026-08-10
        /// mostrou que o dual-boot dele não tem automação segura possível — a chave que liga o
        /// modo automático é a mesma que arma o disco inteiro. Ver o comentário no
        /// DistroCatalog.</summary>
        [Fact]
        public void UnattendedInstall_IsClaimedOnlyByValidatedBuilds()
        {
            var declared = DistroCatalog.All
                .Where(distro => distro.SupportsUnattendedInstall)
                .ToDictionary(distro => distro.Id, distro => distro.UnattendedInstall);

            Assert.Equal(
                new Dictionary<string, UnattendedInstallMechanism>
                {
                    ["ubuntu"] = UnattendedInstallMechanism.Subiquity,
                    ["arch"] = UnattendedInstallMechanism.Archinstall,
                },
                declared);
        }

        /// <summary>O padrão de uma entrada nova é "sem mecanismo": esquecer de declarar não
        /// pode virar uma promessa de automação nunca testada.</summary>
        [Fact]
        public void UnattendedInstall_DefaultsToNone() =>
            Assert.Equal(UnattendedInstallMechanism.None, new DistroInfo().UnattendedInstall);

        /// <summary>É o que decide se a UI mostra o aviso vermelho e o ícone de risco. Marcar
        /// uma distro como testada sem ter testado remove esses dois avisos de uma vez.</summary>
        [Fact]
        public void IsTested_IsClaimedOnlyByDistrosActuallyExercised()
        {
            string[] tested = DistroCatalog.All
                .Where(distro => distro.IsTested)
                .Select(distro => distro.Id)
                .ToArray();

            Assert.Equal(new[] { "ubuntu", "mint", "arch" }, tested);
        }

        /// <summary>O padrão de uma entrada nova é "não testada": esquecer de declarar deixa
        /// os avisos LIGADOS, que é o lado seguro do erro.</summary>
        [Fact]
        public void IsTested_DefaultsToFalse() => Assert.False(new DistroInfo().IsTested);

        /// <summary>Mecanismo declarado sem hash de referência não pode virar automação sobre um
        /// artefato que o app não consegue verificar — mesmo risco de fundo do §6.1 da
        /// constitution, agora sobre a integridade do arquivo em vez do particionamento.</summary>
        [Fact]
        public void SupportsUnattendedInstall_RequiresAVerifiableArtifact()
        {
            var distro = new DistroInfo { UnattendedInstall = UnattendedInstallMechanism.Subiquity };

            Assert.False(distro.HasVerifiableArtifact);
            Assert.False(distro.SupportsUnattendedInstall);

            distro.Sha256 = "a".PadLeft(64, '0');
            distro.SizeBytes = 1;

            Assert.True(distro.HasVerifiableArtifact);
            Assert.True(distro.SupportsUnattendedInstall);
        }

        [Theory]
        [InlineData("", 0, false)]
        [InlineData("somehash", 0, false)]
        [InlineData("", 100, false)]
        [InlineData("somehash", 100, true)]
        public void HasVerifiableArtifact_RequiresBothHashAndPositiveSize(
            string sha256, long sizeBytes, bool expected)
        {
            var distro = new DistroInfo { Sha256 = sha256, SizeBytes = sizeBytes };

            Assert.Equal(expected, distro.HasVerifiableArtifact);
        }

        /// <summary>Declarar a família errada não é erro visível no Windows — é uma máquina que
        /// reinicia e não acha o kernel. O teste fixa quem diverge do padrão; hoje só o Arch,
        /// cuja ISO é archiso e não casper.</summary>
        [Fact]
        public void LiveSession_IsDeclaredOnlyWhereTheIsoIsNotCasper()
        {
            var declared = DistroCatalog.All
                .Where(distro => distro.LiveSession != LiveSessionFamily.Casper)
                .ToDictionary(distro => distro.Id, distro => distro.LiveSession);

            Assert.Equal(
                new Dictionary<string, LiveSessionFamily>
                {
                    ["arch"] = LiveSessionFamily.Archiso,
                },
                declared);
        }

        /// <summary>O padrão é casper porque era a única receita que o app sabia gerar — toda
        /// entrada existente continua bootando como antes sem precisar declarar nada.</summary>
        [Fact]
        public void LiveSession_DefaultsToCasper() =>
            Assert.Equal(LiveSessionFamily.Casper, new DistroInfo().LiveSession);

        /// <summary>Testar o boot é pré-requisito de declarar mecanismo de instalação
        /// desatendida, nunca o contrário — o inverso significaria automatizar uma distro em
        /// que o app nunca chegou nem ao instalador.</summary>
        [Fact]
        public void UnattendedInstall_IsNeverClaimedByAnUntestedDistro() =>
            Assert.All(
                DistroCatalog.All.Where(distro => distro.SupportsUnattendedInstall),
                distro => Assert.True(distro.IsTested, $"'{distro.Id}' declara mecanismo sem estar testada"));

        /// <summary>Quem verifica hash/tamanho é a fonte oficial da distro, nunca um cálculo
        /// feito a partir de um arquivo já baixado localmente (adopt-redacted-safety-model,
        /// task 1.10) — o teste em si não valida a procedência, mas fixa a invariante estrutural
        /// que a torna possível de auditar: os dois campos vêm juntos ou nenhum dos dois vem.</summary>
        [Fact]
        public void ArtifactIdentity_Sha256AndSizeBytesAreDeclaredTogether() =>
            Assert.All(DistroCatalog.All, distro =>
            {
                bool hasHash = !string.IsNullOrWhiteSpace(distro.Sha256);
                bool hasSize = distro.SizeBytes > 0;
                Assert.True(
                    hasHash == hasSize,
                    $"'{distro.Id}' declara só um de Sha256/SizeBytes — os dois precisam vir juntos");
            });

        /// <summary>Uma entrada sem hash de referência (Kubuntu: link direto está errado na
        /// origem; EndeavourOS: a fonte oficial só publica SHA-512) não pode oferecer download
        /// automático nem instalação desatendida — <see cref="DistroInfo.HasVerifiableArtifact"/>
        /// é quem garante isso para os dois casos ao mesmo tempo.</summary>
        [Fact]
        public void ArtifactIdentity_EntriesWithoutAHashAreNotVerifiable()
        {
            string[] withoutHash = DistroCatalog.All
                .Where(distro => !distro.HasVerifiableArtifact)
                .Select(distro => distro.Id)
                .ToArray();

            Assert.Equal(new[] { "kubuntu", "endeavour" }, withoutHash);
        }

        /// <summary>O hash publicado pela Kali para esta build tem 64 caracteres hex — o mesmo
        /// formato que todo SHA-256 declarado no catálogo precisa ter. Um valor truncado ou com
        /// caracteres fora do alfabeto hex nunca bateria contra nada, silenciosamente.</summary>
        [Fact]
        public void ArtifactIdentity_EveryDeclaredHashIsSixtyFourHexCharacters() =>
            Assert.All(
                DistroCatalog.All.Where(distro => distro.HasVerifiableArtifact),
                distro =>
                {
                    Assert.Equal(64, distro.Sha256.Length);
                    Assert.Matches("^[0-9a-fA-F]{64}$", distro.Sha256);
                });

        /// <summary>Kali, Kubuntu e EndeavourOS ficam fora da navegação por ora (decisão de
        /// 2026-08-11: focar nas distros já 100% ok) sem sair do catálogo de dados — ver
        /// CatalogViewModel/IsoAcquisitionViewModel, que filtram por IsEnabled.</summary>
        [Fact]
        public void IsEnabled_TemporarilyExcludesTheEntriesStillBeingWorkedOn()
        {
            string[] disabled = DistroCatalog.All
                .Where(distro => !distro.IsEnabled)
                .Select(distro => distro.Id)
                .ToArray();

            Assert.Equal(new[] { "kubuntu", "endeavour", "kali" }, disabled);
        }

        [Fact]
        public void IsEnabled_DefaultsToTrue() => Assert.True(new DistroInfo().IsEnabled);

        /// <summary>Trava a ordem de inicialização estática entre <c>Fallback</c> e
        /// <c>All</c> — <c>All</c> precisa capturar a lista real do <c>Fallback</c>, não um
        /// valor nulo por rodar antes dele (bug de ordem de campo estático já pego uma vez
        /// nesta mudança). Só é um teste de verdade se nada neste processo já reatribuiu
        /// <c>DistroCatalog.All</c> antes dele rodar.</summary>
        [Fact]
        public void All_DefaultsToTheEmbeddedFallback() =>
            Assert.Same(DistroCatalog.Fallback, DistroCatalog.All);
    }
}
