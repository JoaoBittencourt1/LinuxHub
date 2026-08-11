using System;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class IsoBootEntryBuilderRegistryTests
    {
        private sealed class StubBuilder(LiveSessionFamily family) : IIsoBootEntryBuilder
        {
            public LiveSessionFamily Family { get; } = family;

            public string Build(IsoBootEntryRequest request) => string.Empty;
        }

        [Fact]
        public void Resolve_ReturnsTheBuilderOfTheDeclaredFamily()
        {
            var casper = new StubBuilder(LiveSessionFamily.Casper);
            var archiso = new StubBuilder(LiveSessionFamily.Archiso);
            var registry = new IsoBootEntryBuilderRegistry([casper, archiso]);

            Assert.Same(archiso, registry.Resolve(LiveSessionFamily.Archiso));
            Assert.Same(casper, registry.Resolve(LiveSessionFamily.Casper));
        }

        /// <summary>Estourar aqui, do lado do Windows, é o resultado bom: passar batido geraria
        /// a entrada de outra família e o usuário só descobriria depois do reboot, numa tela
        /// preta, quando o app já não tem como intervir.</summary>
        [Fact]
        public void Resolve_UnregisteredFamily_Throws()
        {
            var registry = new IsoBootEntryBuilderRegistry([new StubBuilder(LiveSessionFamily.Casper)]);

            var ex = Assert.Throws<InvalidOperationException>(
                () => registry.Resolve(LiveSessionFamily.Archiso));

            Assert.Contains("Archiso", ex.Message);
        }

        /// <summary>A composição real da app precisa cobrir toda família que o catálogo pode
        /// declarar — uma família sem construtor registrado só apareceria no boot.</summary>
        [Fact]
        public void EveryFamilyDeclaredInTheCatalogHasABuilder()
        {
            var registry = new IsoBootEntryBuilderRegistry(
            [
                CasperIsoBootEntryBuilder.Instance,
                ArchisoIsoBootEntryBuilder.Instance,
            ]);

            foreach (LiveSessionFamily family in Enum.GetValues<LiveSessionFamily>())
                Assert.NotNull(registry.Resolve(family));
        }
    }
}
