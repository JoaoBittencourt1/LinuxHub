using System;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class UnattendedInstallPreparerRegistryTests
    {
        private sealed class StubPreparer(UnattendedInstallMechanism mechanism) : IUnattendedInstallPreparer
        {
            public UnattendedInstallMechanism Mechanism { get; } = mechanism;

            public UnattendedPreparationResult Prepare(
                InstallerConfig config, int diskIndex, StagingPartition? staging) =>
                new(1, UnattendedBootParameters.Interactive);
        }

        [Fact]
        public void Resolve_ReturnsThePreparerOfTheDeclaredMechanism()
        {
            var subiquity = new StubPreparer(UnattendedInstallMechanism.Subiquity);
            var ubiquity = new StubPreparer(UnattendedInstallMechanism.UbiquityPreseed);
            var registry = new UnattendedInstallPreparerRegistry([subiquity, ubiquity]);

            Assert.Same(ubiquity, registry.Resolve(UnattendedInstallMechanism.UbiquityPreseed));
            Assert.Same(subiquity, registry.Resolve(UnattendedInstallMechanism.Subiquity));
        }

        /// <summary>Declarar um mecanismo sem implementação registrada tem que estourar aqui,
        /// do lado do Windows: passar batido viraria um boot que cai calado no instalador
        /// interativo depois do reboot, quando o usuário já não tem como intervir pelo app.</summary>
        [Fact]
        public void Resolve_UnregisteredMechanism_Throws()
        {
            var registry = new UnattendedInstallPreparerRegistry(
                [new StubPreparer(UnattendedInstallMechanism.Subiquity)]);

            var ex = Assert.Throws<InvalidOperationException>(
                () => registry.Resolve(UnattendedInstallMechanism.UbiquityPreseed));

            Assert.Contains("UbiquityPreseed", ex.Message);
        }

        /// <summary>`None` não é "escolha um padrão": é sinal de que o wizard chegou no caminho
        /// de automação sem ter oferecido automação nenhuma.</summary>
        [Fact]
        public void Resolve_None_Throws() =>
            Assert.Throws<InvalidOperationException>(
                () => new UnattendedInstallPreparerRegistry([]).Resolve(UnattendedInstallMechanism.None));
    }
}
