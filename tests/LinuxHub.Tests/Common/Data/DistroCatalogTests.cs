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

        /// <summary>Fixa qual mecanismo cada distro declara, e não só "tem ou não tem":
        /// declarar o mecanismo errado gera um preseed para quem espera autoinstall (ou
        /// vice-versa), o que só apareceria num boot real.
        ///
        /// ATENÇÃO — o Mint está aqui como habilitação para o teste em VM das tasks 6.3/6.4
        /// de openspec/changes/mint-ubiquity-autoinstall, NÃO como capacidade validada. Este
        /// teste deixou de ser a trava de §7.1 enquanto essa linha existir; a trava passou a
        /// ser o comentário no catálogo. Se 6.3/6.4 não passarem, Mint volta para None aqui e
        /// no catálogo.</summary>
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
                    ["mint"] = UnattendedInstallMechanism.UbiquityPreseed,
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

            Assert.Equal(new[] { "ubuntu", "mint" }, tested);
        }

        /// <summary>O padrão de uma entrada nova é "não testada": esquecer de declarar deixa
        /// os avisos LIGADOS, que é o lado seguro do erro.</summary>
        [Fact]
        public void IsTested_DefaultsToFalse() => Assert.False(new DistroInfo().IsTested);

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
