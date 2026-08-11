using System.Linq;
using LinuxHub.Common.Data;
using LinuxHub.Features.Catalog.ViewModels;
using Xunit;

namespace LinuxHub.Tests.Features.Catalog.ViewModels
{
    public class CatalogViewModelTests
    {
        /// <summary>Uma entrada desabilitada (DistroInfo.IsEnabled) continua no catálogo de
        /// dados, mas não pode aparecer na grade de navegação — ela some da UI, não do código.</summary>
        [Fact]
        public void Distros_ExcludesDisabledEntries()
        {
            var vm = new CatalogViewModel();

            Assert.DoesNotContain(vm.Distros, distro => !distro.IsEnabled);
            Assert.True(DistroCatalog.All.Any(distro => !distro.IsEnabled), "fixture: catálogo precisa ter ao menos uma entrada desabilitada para este teste valer algo");
        }
    }
}
