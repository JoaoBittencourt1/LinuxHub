using LinuxHub.Common.Data;
using Xunit;

namespace LinuxHub.Tests.Common.Data
{
    /// <summary>
    /// A detecção por nome de arquivo decide mais do que o rótulo na tela: é o
    /// <see cref="LinuxHub.Common.Models.DistroInfo.SupportsAutoinstall"/> da distro casada que
    /// libera (ou não) a instalação automática no wizard.
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

        /// <summary>Só o Ubuntu foi validado de ponta a ponta — é o que sustenta o toggle de
        /// instalação automática aparecer nele e em mais nenhuma distro do catálogo.</summary>
        [Fact]
        public void Autoinstall_IsClaimedByUbuntuOnly()
        {
            string[] withAutoinstall = DistroCatalog.All
                .Where(distro => distro.SupportsAutoinstall)
                .Select(distro => distro.Id)
                .ToArray();

            Assert.Equal(new[] { "ubuntu" }, withAutoinstall);
        }
    }
}
