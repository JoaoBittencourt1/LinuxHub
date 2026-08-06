using System.Linq;
using LinuxHub.Common.Data;
using LinuxHub.Common.Models;
using LinuxHub.Features.Catalog.ViewModels;
using Xunit;

namespace LinuxHub.Tests.Features.Catalog.ViewModels
{
    public class DistroDetailViewModelTests
    {
        private static DistroInfo Distro(string id) =>
            DistroCatalog.All.First(distro => distro.Id == id);

        /// <summary>O aviso vermelho da tela de detalhe pendura nesta propriedade. Quem abre a
        /// tela está decidindo se instala, e a decisão muda se o app nunca foi exercitado
        /// naquela distro.</summary>
        [Theory]
        [InlineData("ubuntu", false)]
        [InlineData("mint", false)]
        [InlineData("fedora", true)]
        [InlineData("arch", true)]
        public void IsUntested_MirrorsTheCatalog(string distroId, bool expected) =>
            Assert.Equal(expected, new DistroDetailViewModel(Distro(distroId)).IsUntested);
    }
}
