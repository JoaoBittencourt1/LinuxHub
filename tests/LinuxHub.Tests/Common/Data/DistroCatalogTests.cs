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
    }
}
