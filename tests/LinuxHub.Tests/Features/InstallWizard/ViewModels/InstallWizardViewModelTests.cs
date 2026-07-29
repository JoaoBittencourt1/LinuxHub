using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using LinuxHub.Features.InstallWizard.ViewModels;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Cobre o estado da tela de progresso. Encolher a partição e preparar o boot bloqueiam
    /// por dezenas de segundos, então o wizard precisa sair do ar durante a instalação e
    /// voltar depois — inclusive quando o disco recusa a operação.
    /// </summary>
    public class InstallWizardViewModelTests
    {
        private sealed class FakeIsoDownloadService : IIsoDownloadService
        {
            public Task<string> DownloadAsync(
                DistroInfo distro, IProgress<IsoDownloadProgress> progress, CancellationToken cancellationToken) =>
                Task.FromResult(@"C:\isos\ubuntu.iso");
        }

        private sealed class FakeDistroDetectionService : IDistroDetectionService
        {
            public DistroDetectionResult Detect(string isoPath) =>
                new(new DistroInfo { Name = "Ubuntu" }, IsExpectedVersion: true);
        }

        private sealed class FakeDownloadedIsoRepository : IDownloadedIsoRepository
        {
            public IReadOnlyList<DownloadedIso> GetAll() => Array.Empty<DownloadedIso>();
        }

        private sealed class FakeDiskInventoryService : IDiskInventoryService
        {
            public IReadOnlyList<DiskInfo> GetDisks() => new[]
            {
                new DiskInfo { Index = 0, Model = "Disco", SizeBytes = 512L * 1024 * 1024 * 1024 }
            };
        }

        private sealed class FakePartitionInventoryService : IPartitionInventoryService
        {
            public IReadOnlyList<PartitionInfo> GetEligiblePartitions() => Array.Empty<PartitionInfo>();
        }

        private sealed class FakeFirmwareService : IFirmwareService
        {
            public bool IsUefi() => true;
        }

        private sealed class FakeInstallerConfigWriter : IInstallerConfigWriter
        {
            public void Save(InstallerConfig config) { }
        }

        private sealed class FakeSystemInfoProvider : ISystemInfoProvider
        {
            public string GetLocale() => "pt_BR.UTF-8";
            public string GetKeymap() => "br";
            public string GetTimezone() => "America/Sao_Paulo";
        }

        private sealed class FakeEspLocatorService : IEspLocatorService
        {
            public int? FindEfiSystemPartitionIndex(int diskIndex) => 1;
        }

        private sealed class FakeDiskPartitioningService : IDiskPartitioningService
        {
            public void ShrinkPartition(int diskIndex, int partitionIndex, int sizeInGb) { }
        }

        private sealed class FakeAutoinstallPreparationService : IAutoinstallPreparationService
        {
            public int Prepare(InstallerConfig config, int diskIndex) => 5;
        }

        /// <summary>Segura o boot-staging até o teste liberar, para inspecionar o wizard com a
        /// instalação em andamento — é o único ponto em que a tela de progresso está no ar.</summary>
        private sealed class BlockingBootStagingService : IBootStagingService
        {
            private readonly TaskCompletionSource _release = new();

            public TaskCompletionSource Started { get; } = new();
            public Exception? FailWith { get; set; }

            public void Release() => _release.TrySetResult();

            public void InstallStagingBootloader(BootStagingRequest request)
            {
                Started.TrySetResult();
                _release.Task.GetAwaiter().GetResult();

                if (FailWith is not null)
                    throw FailWith;
            }
        }

        private static InstallWizardViewModel BuildViewModel(IBootStagingService bootStaging)
        {
            var iso = new IsoAcquisitionViewModel(new FakeIsoDownloadService(), new FakeDistroDetectionService(), new FakeDownloadedIsoRepository());
            var target = new TargetSelectionViewModel(
                new FakeDiskInventoryService(), new FakePartitionInventoryService(), new FakeFirmwareService());

            var vm = new InstallWizardViewModel(
                iso,
                target,
                new AccountViewModel { Username = "joao", Password = "123", ConfirmPassword = "123", Hostname = "pc" },
                new InstallerConfigBuilder(new FakeSystemInfoProvider(), new FakeEspLocatorService()),
                new FakeInstallerConfigWriter(),
                new FakeDiskPartitioningService(),
                new FakeAutoinstallPreparationService(),
                bootStaging);

            // ResolvedIsoPath só é preenchido pelo download ou pela seleção manual; o wizard
            // recusa instalar sem ele, então o teste passa pelo caminho de download falso.
            iso.DownloadIsoCommand.Execute(null);

            return vm;
        }

        private static async Task ConfirmAndWaitForInstallStartAsync(
            InstallWizardViewModel vm, BlockingBootStagingService bootStaging)
        {
            vm.InstallCommand.Execute(null);
            vm.PendingConfirmation!.ConfirmCommand.Execute(null);

            await bootStaging.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        private static async Task WaitUntilIdleAsync(InstallWizardViewModel vm)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (vm.IsInstalling && DateTime.UtcNow < deadline)
                await Task.Delay(10);
        }

        [Fact]
        public async Task Confirming_ShowsProgressAndHidesConfirmationAndInstallButton()
        {
            var bootStaging = new BlockingBootStagingService();
            var vm = BuildViewModel(bootStaging);

            await ConfirmAndWaitForInstallStartAsync(vm, bootStaging);

            Assert.True(vm.IsInstalling);
            Assert.False(vm.IsConfirming);
            Assert.False(vm.IsIdle);
            Assert.False(string.IsNullOrWhiteSpace(vm.InstallStatus));

            bootStaging.Release();
            await WaitUntilIdleAsync(vm);
        }

        [Fact]
        public async Task InstallFinished_ReturnsWizardToIdle()
        {
            var bootStaging = new BlockingBootStagingService();
            var vm = BuildViewModel(bootStaging);

            await ConfirmAndWaitForInstallStartAsync(vm, bootStaging);
            bootStaging.Release();
            await WaitUntilIdleAsync(vm);

            Assert.False(vm.IsInstalling);
            Assert.Null(vm.InstallStatus);
            Assert.True(vm.IsIdle);
        }

        [Fact]
        public async Task InstallFailure_StillClosesProgressScreen()
        {
            // Sem isso um erro deixaria o overlay travado por cima do wizard: o usuário veria
            // a mensagem de falha e uma tela de carregamento eterna atrás dela.
            var bootStaging = new BlockingBootStagingService
            {
                FailWith = new InvalidOperationException("bcdedit falhou")
            };
            var vm = BuildViewModel(bootStaging);

            string? errorMessage = null;
            bool progressStillOpenWhenNotified = true;
            vm.Notify += (_, message, isError) =>
            {
                if (!isError)
                    return;

                errorMessage = message;
                progressStillOpenWhenNotified = vm.IsInstalling;
            };

            await ConfirmAndWaitForInstallStartAsync(vm, bootStaging);
            bootStaging.Release();
            await WaitUntilIdleAsync(vm);

            Assert.False(vm.IsInstalling);
            Assert.True(vm.IsIdle);
            Assert.Equal("bcdedit falhou", errorMessage);

            // O aviso é um MessageBox modal: se o overlay ainda estivesse aberto, o spinner
            // ficaria girando atrás dele até o usuário clicar em OK.
            Assert.False(progressStillOpenWhenNotified);
        }
    }
}
