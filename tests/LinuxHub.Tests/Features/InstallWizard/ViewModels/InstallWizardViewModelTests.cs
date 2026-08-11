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
            public IReadOnlyList<PartitionInfo> Partitions { get; set; } = Array.Empty<PartitionInfo>();

            public IReadOnlyList<PartitionInfo> GetEligiblePartitions() => Partitions;
        }

        private sealed class FakeFirmwareService : IFirmwareService
        {
            public bool IsUefi() => true;
        }

        private sealed class FakeInstallerConfigWriter : IInstallerConfigWriter
        {
            public InstallerConfig? Saved { get; private set; }

            public void Save(InstallerConfig config) => Saved = config;
        }

        private sealed class FakeSystemInfoProvider : ISystemInfoProvider
        {
            public DetectedRegionalSetting GetLocale() => DetectedRegionalSetting.Detected("pt_BR.UTF-8");
            public DetectedRegionalSetting GetKeymap() => DetectedRegionalSetting.Detected("br");
            public DetectedRegionalSetting GetTimezone() => DetectedRegionalSetting.Detected("America/Sao_Paulo");
        }

        private sealed class FakeEspLocatorService : IEspLocatorService
        {
            public int? FindEfiSystemPartitionIndex(int diskIndex) => 1;
            public EfiSystemPartitionLocation? FindSystemEfiSystemPartition() =>
                new EfiSystemPartitionLocation(0, 1);
        }

        private sealed class FakeDiskPartitioningService : IDiskPartitioningService
        {
            public long? ShrunkBytes { get; private set; }
            public int? NewPartitionsPlanned { get; private set; }

            public void ShrinkPartition(
                int diskIndex, int partitionIndex, long bytesToFree, int newPartitionsPlanned)
            {
                ShrunkBytes = bytesToFree;
                NewPartitionsPlanned = newPartitionsPlanned;
            }

            public void EnsureUnallocatedSpace(
                int diskIndex, long requiredBytes, int newPartitionsPlanned) =>
                NewPartitionsPlanned = newPartitionsPlanned;
        }

        private sealed class FakeUnattendedInstallPreparer : IUnattendedInstallPreparer
        {
            public StagingPartition? ReceivedStaging { get; private set; }

            public UnattendedInstallMechanism Mechanism => UnattendedInstallMechanism.Subiquity;

            public UnattendedPreparationResult Prepare(
                InstallerConfig config, int diskIndex, StagingPartition? staging)
            {
                ReceivedStaging = staging;
                return new UnattendedPreparationResult(
                    SeedPartitionNumber: 5,
                    BootParameters: new UnattendedBootParameters(
                        IsUnattended: true,
                        KernelParameters: "autoinstall",
                        ExtraInitrdGrubPath: null));
            }
        }

        private sealed class FakeStagingPartitionService : IStagingPartitionService
        {
            public int CreateCalls { get; private set; }
            public string? CopiedFrom { get; private set; }

            public long RequiredBytesFor(long isoSizeInBytes) => isoSizeInBytes + 512L * 1024 * 1024;

            public StagingPartition Create(int diskIndex, long isoSizeInBytes)
            {
                CreateCalls++;
                return new(diskIndex, 9, "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
            }

            public void CopyIso(StagingPartition partition, string isoSourcePath, IProgress<string>? progress) =>
                CopiedFrom = isoSourcePath;
        }

        private sealed class CapturingBootStagingService : IBootStagingService
        {
            public BootStagingRequest? LastRequest { get; private set; }

            public void InstallStagingBootloader(BootStagingRequest request) => LastRequest = request;
        }

        private sealed class FakeIsoFileInfoProvider : IIsoFileInfoProvider
        {
            public long GetSizeInBytes(string isoPath) => 6_655_619_072;
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

        /// <summary>Máquina sem nenhuma das duas proteções — é o cenário em que a instalação
        /// pode prosseguir, e o padrão de todos os testes que não são sobre elas.</summary>
        private sealed class FakeBootSecurityService : IBootSecurityService
        {
            public bool SecureBoot { get; set; }
            public bool BitLocker { get; set; }

            public bool IsSecureBootEnabled() => SecureBoot;
            public bool IsVolumeBitLockerProtected(char driveLetter) => BitLocker;
        }

        private static InstallWizardViewModel BuildViewModel(
            IBootStagingService bootStaging,
            IBootSecurityService? bootSecurity = null,
            IIsoFileInfoProvider? isoFileInfo = null,
            FakeStagingPartitionService? staging = null,
            FakeDiskPartitioningService? partitioning = null,
            FakePartitionInventoryService? partitions = null,
            FakeInstallerConfigWriter? configWriter = null)
        {
            var iso = new IsoAcquisitionViewModel(new FakeIsoDownloadService(), new FakeDistroDetectionService(), new FakeDownloadedIsoRepository());
            var target = new TargetSelectionViewModel(
                new FakeDiskInventoryService(),
                partitions ?? new FakePartitionInventoryService(),
                new FakeFirmwareService());

            var vm = new InstallWizardViewModel(
                iso,
                target,
                new AccountViewModel { Username = "joao", Password = "123", ConfirmPassword = "123", Hostname = "pc" },
                new RegionalSettingsViewModel(new FakeSystemInfoProvider()),
                new InstallerConfigBuilder(new FakeEspLocatorService()),
                configWriter ?? new FakeInstallerConfigWriter(),
                partitioning ?? new FakeDiskPartitioningService(),
                new UnattendedInstallPreparerRegistry([new FakeUnattendedInstallPreparer()]),
                bootStaging,
                bootSecurity ?? new FakeBootSecurityService(),
                staging ?? new FakeStagingPartitionService(),
                isoFileInfo ?? new FakeIsoFileInfoProvider());

            // ResolvedIsoPath só é preenchido pelo download ou pela seleção manual; o wizard
            // recusa instalar sem ele, então o teste passa pelo caminho de download falso.
            iso.DownloadIsoCommand.Execute(null);

            // Dual-boot é o padrão do wizard; estes testes cobrem o fluxo de substituição, que
            // não depende de haver partição elegível na máquina de teste.
            vm.Target.Mode = InstallMode.Replace;

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

        /// <summary>
        /// O ponto inteiro da guarda: recusar ANTES de escrever. Numa VM com BitLocker o
        /// fluxo ia até o fim — encolhia o disco, criava a semente, registrava a entrada de
        /// boot — e só morria depois do reboot, numa tela preta do GRUB. Se o cartão de
        /// confirmação chegar a aparecer, a recusa veio tarde demais.
        /// </summary>
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void BootProtectionEnabled_RefusesBeforeTouchingTheDisk(bool secureBoot, bool bitLocker)
        {
            var bootStaging = new BlockingBootStagingService();
            var vm = BuildViewModel(
                bootStaging,
                new FakeBootSecurityService { SecureBoot = secureBoot, BitLocker = bitLocker });

            string? errorMessage = null;
            vm.Notify += (_, message, isError) =>
            {
                if (isError)
                    errorMessage = message;
            };

            vm.InstallCommand.Execute(null);

            Assert.Null(vm.PendingConfirmation);
            Assert.False(vm.IsInstalling);
            Assert.NotNull(errorMessage);
        }

        /// <summary>
        /// A mensagem é a única saída que o usuário tem — ela precisa dizer o que fazer, não
        /// só que falhou. Sem o passo a passo a recusa vira um beco sem saída.
        /// </summary>
        [Fact]
        public void SecureBootRefusal_ExplainsHowToTurnItOff()
        {
            var vm = BuildViewModel(
                new BlockingBootStagingService(),
                new FakeBootSecurityService { SecureBoot = true });

            string? errorMessage = null;
            vm.Notify += (_, message, isError) =>
            {
                if (isError)
                    errorMessage = message;
            };

            vm.InstallCommand.Execute(null);

            Assert.Contains("Secure Boot", errorMessage);
            Assert.Contains("UEFI", errorMessage);
        }

        /// <summary>
        /// A staging consome espaço que o usuário não pediu. Descobrir que não cabe DEPOIS de
        /// encolher o Windows deixaria a máquina alterada por nada — e no modo substituir, com
        /// a ESP já preparada para um boot que nunca vai acontecer.
        /// </summary>
        [Fact]
        public void NotEnoughSpaceForStaging_RefusesBeforeTouchingTheDisk()
        {
            var bootStaging = new BlockingBootStagingService();
            // ISO maior que o disco de teste: não há como acomodar staging nenhuma.
            var vm = BuildViewModel(bootStaging, isoFileInfo: new HugeIsoFileInfoProvider());

            string? errorMessage = null;
            vm.Notify += (_, message, isError) =>
            {
                if (isError)
                    errorMessage = message;
            };

            vm.InstallCommand.Execute(null);

            Assert.Null(vm.PendingConfirmation);
            Assert.False(vm.IsInstalling);
            Assert.NotNull(errorMessage);
        }

        private sealed class HugeIsoFileInfoProvider : IIsoFileInfoProvider
        {
            public long GetSizeInBytes(string isoPath) => 100L * 1024 * 1024 * 1024 * 1024;
        }

        /// <summary>A letra da unidade tem que chegar na mensagem: "rode manage-bde -off" sem
        /// dizer em qual unidade não ajuda ninguém.</summary>
        [Fact]
        public void BitLockerRefusal_NamesTheDriveToDecrypt()
        {
            var vm = BuildViewModel(
                new BlockingBootStagingService(),
                new FakeBootSecurityService { BitLocker = true });

            string? errorMessage = null;
            vm.Notify += (_, message, isError) =>
            {
                if (isError)
                    errorMessage = message;
            };

            vm.InstallCommand.Execute(null);

            Assert.Contains("manage-bde", errorMessage);
            Assert.Matches(@"[A-Za-z]:", errorMessage!);
        }

        /// <summary>
        /// Dual-boot já preserva a partição que hospeda a ISO — staging era custo sem ganho
        /// nesse modo. O GRUB continua achando a ISO no caminho original do Windows.
        /// </summary>
        [Fact]
        public async Task DualBoot_DoesNotCreateStagingPartition()
        {
            var bootStaging = new CapturingBootStagingService();
            var staging = new FakeStagingPartitionService();
            var partitions = new FakePartitionInventoryService
            {
                Partitions = new[]
                {
                    new PartitionInfo
                    {
                        DiskIndex = 0,
                        PartitionIndex = 3,
                        SizeBytes = 400L * 1024 * 1024 * 1024,
                        FreeSpaceBytes = 200L * 1024 * 1024 * 1024,
                        FileSystem = "NTFS"
                    }
                }
            };

            var vm = BuildViewModel(bootStaging, staging: staging, partitions: partitions);
            vm.Target.Mode = InstallMode.DualBoot;
            vm.Target.SelectedPartition = vm.Target.Partitions[0];
            vm.Target.LinuxPartitionSizeGb = 50;

            vm.InstallCommand.Execute(null);
            vm.PendingConfirmation!.ConfirmCommand.Execute(null);
            await WaitUntilIdleAsync(vm);

            Assert.Equal(0, staging.CreateCalls);
            Assert.Null(staging.CopiedFrom);
            Assert.Equal(@"C:\isos\ubuntu.iso", bootStaging.LastRequest!.IsoPath);
        }

        /// <summary>
        /// O usuário chega ao passo regional e segue sem mexer em nada: o que a tela mostrava
        /// é o que vai para a instalação. Antes deste change nem chegava a existir tela — o
        /// teclado era <c>"us"</c> e o fuso <c>"America/Sao_Paulo"</c>, escritos direto.
        /// </summary>
        [Fact]
        public async Task Install_WritesTheRegionalSettingsExactlyAsShown()
        {
            var configWriter = new FakeInstallerConfigWriter();
            var vm = BuildViewModel(new CapturingBootStagingService(), configWriter: configWriter);

            await ReplaceInstallAsync(vm);

            Assert.Equal(vm.Regional.Locale, configWriter.Saved!.Locale);
            Assert.Equal(vm.Regional.Keymap, configWriter.Saved.Keymap);
            Assert.Equal(vm.Regional.Timezone, configWriter.Saved.Timezone);
        }

        /// <summary>
        /// E quando ele corrige, é a correção que vale — um teclado físico diferente do
        /// configurado no Windows é caso comum, e a detecção pode simplesmente errar.
        /// </summary>
        [Fact]
        public async Task Install_CorrectedRegionalSettings_OverrideTheDetectedOnes()
        {
            var configWriter = new FakeInstallerConfigWriter();
            var vm = BuildViewModel(new CapturingBootStagingService(), configWriter: configWriter);

            vm.Regional.Locale = "de_DE.UTF-8";
            vm.Regional.Keymap = "de";
            vm.Regional.Timezone = "Europe/Berlin";

            await ReplaceInstallAsync(vm);

            Assert.Equal("de_DE.UTF-8", configWriter.Saved!.Locale);
            Assert.Equal("de", configWriter.Saved.Keymap);
            Assert.Equal("Europe/Berlin", configWriter.Saved.Timezone);
        }

        /// <summary>Idioma, teclado e fuso só são perguntados quando a instalação desatendida
        /// vai de fato rodar: sem ela quem pergunta é o instalador nativo da ISO, e oferecer
        /// uma escolha que seria ignorada promete um controle que o usuário não tem.</summary>
        [Fact]
        public void RegionalStep_FollowsWhetherTheUnattendedInstallWillRun()
        {
            var vm = BuildViewModel(new CapturingBootStagingService());

            Assert.True(vm.Iso.IsAutoinstallActive);
            Assert.True(vm.IsRegionalStepVisible);

            vm.Iso.UseAutoinstall = false;

            Assert.False(vm.IsRegionalStepVisible);
        }

        private static async Task ReplaceInstallAsync(InstallWizardViewModel vm)
        {
            vm.InstallCommand.Execute(null);
            // Modo substituir exige confirmação tipada.
            vm.PendingConfirmation!.TypedConfirmation = vm.PendingConfirmation.ConfirmationWord;
            vm.PendingConfirmation.ConfirmCommand.Execute(null);
            await WaitUntilIdleAsync(vm);
        }

        [Fact]
        public async Task Replace_CopiesIsoToStagingAndPointsGrubAtIt()
        {
            var bootStaging = new CapturingBootStagingService();
            var staging = new FakeStagingPartitionService();
            var vm = BuildViewModel(bootStaging, staging: staging);

            vm.InstallCommand.Execute(null);
            // Modo substituir exige confirmação tipada.
            vm.PendingConfirmation!.TypedConfirmation = vm.PendingConfirmation.ConfirmationWord;
            vm.PendingConfirmation.ConfirmCommand.Execute(null);
            await WaitUntilIdleAsync(vm);

            Assert.Equal(1, staging.CreateCalls);
            Assert.Equal(@"C:\isos\ubuntu.iso", staging.CopiedFrom);
            Assert.Equal(StagingPartitionService.IsoGrubPath, bootStaging.LastRequest!.IsoPath);
        }
    }
}
