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
        /// num boot real.</summary>
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
                },
                declared);
        }

        /// <summary>O padrão de uma entrada nova é "sem mecanismo": esquecer de declarar não
        /// pode virar uma promessa de automação nunca testada.</summary>
        [Fact]
        public void UnattendedInstall_DefaultsToNone() =>
            Assert.Equal(UnattendedInstallMechanism.None, new DistroInfo().UnattendedInstall);
    }
}
