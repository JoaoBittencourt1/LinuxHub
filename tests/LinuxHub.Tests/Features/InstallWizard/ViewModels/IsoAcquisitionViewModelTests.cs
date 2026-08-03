using LinuxHub.Common.Data;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Services;
using LinuxHub.Features.InstallWizard.ViewModels;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Cobre a entrada vinda do catálogo ("instalar agora") e a visibilidade do toggle de
    /// instalação automática, que só existe para a distro validada de ponta a ponta.
    /// </summary>
    public class IsoAcquisitionViewModelTests
    {
        private sealed class FakeIsoDownloadService : IIsoDownloadService
        {
            public Task<string> DownloadAsync(
                DistroInfo distro, IProgress<IsoDownloadProgress> progress, CancellationToken cancellationToken) =>
                Task.FromResult($@"C:\isos\{distro.Id}.iso");
        }

        private sealed class FakeDistroDetectionService : IDistroDetectionService
        {
            public DistroDetectionResult Detect(string isoPath) =>
                new(new DistroInfo { Name = "Ubuntu" }, IsExpectedVersion: true);
        }

        private sealed class FakeDownloadedIsoRepository : IDownloadedIsoRepository
        {
            public IReadOnlyList<DownloadedIso> Isos { get; init; } = Array.Empty<DownloadedIso>();

            public IReadOnlyList<DownloadedIso> GetAll() => Isos;
        }

        private static DistroInfo Distro(string id) =>
            DistroCatalog.All.First(distro => distro.Id == id);

        private static IsoAcquisitionViewModel BuildViewModel(params DownloadedIso[] downloaded) =>
            new(new FakeIsoDownloadService(),
                new FakeDistroDetectionService(),
                new FakeDownloadedIsoRepository { Isos = downloaded });

        /// <summary>Com a ISO já em disco, "instalar agora" não pode mandar o usuário baixar
        /// vários GB de novo — a instalação já pode começar dali.</summary>
        [Fact]
        public void PrepareForDistro_WithIsoAlreadyDownloaded_SelectsItForInstall()
        {
            var mint = Distro("mint");
            var downloaded = new DownloadedIso(@"C:\isos\mint.iso", mint, DateTime.UtcNow);
            var vm = BuildViewModel(downloaded);

            vm.PrepareForDistro(mint);

            Assert.Same(downloaded, vm.SelectedDownloadedIso);
            Assert.Equal(@"C:\isos\mint.iso", vm.ResolvedIsoPath);
            Assert.True(vm.IsIsoReadyForInstall);
            Assert.Same(mint, vm.DisplayedDistro);
        }

        /// <summary>Sem ISO dessa distro em disco, o wizard abre no seletor de download já
        /// apontado pra ela — não na lista de outras ISOs já baixadas.</summary>
        [Fact]
        public void PrepareForDistro_WithoutIso_OpensTheDownloadPickerOnIt()
        {
            var fedora = Distro("fedora");
            var vm = BuildViewModel(new DownloadedIso(@"C:\isos\ubuntu.iso", Distro("ubuntu"), DateTime.UtcNow));

            vm.PrepareForDistro(fedora);

            Assert.Same(fedora, vm.SelectedDistro);
            Assert.Null(vm.SelectedDownloadedIso);
            Assert.False(vm.IsIsoReadyForInstall);
            Assert.True(vm.IsDistroPickerVisible);
            Assert.False(vm.IsDownloadedIsosVisible);
        }

        [Fact]
        public void PrepareForDistro_LeavesManualSelection()
        {
            var vm = BuildViewModel();
            vm.IsManualSelect = true;

            vm.PrepareForDistro(Distro("ubuntu"));

            Assert.False(vm.IsManualSelect);
        }

        /// <summary>O toggle de instalação automática só pode aparecer no Ubuntu: é a única
        /// distro cujo autoinstall foi validado de ponta a ponta.</summary>
        [Theory]
        [InlineData("ubuntu", true)]
        [InlineData("xubuntu", false)]
        [InlineData("mint", false)]
        [InlineData("arch", false)]
        public void AutoinstallToggle_IsVisibleForUbuntuOnly(string distroId, bool expected)
        {
            var vm = BuildViewModel();

            vm.PrepareForDistro(Distro(distroId));

            Assert.Equal(expected, vm.IsAutoinstallToggleVisible);
            Assert.Equal(expected, vm.IsAutoinstallActive);
        }
    }
}
