using System.IO;
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

        private sealed class FakeArtifactVerifier : IArtifactVerifier
        {
            public Task<ArtifactVerificationResult> VerifyFileAsync(
                string filePath, string expectedSha256, long expectedSizeBytes,
                IProgress<double>? progress, CancellationToken cancellationToken) =>
                Task.FromResult(ArtifactVerificationResult.Verified());
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

            /// <summary>Quando definido, o passo de preparação de disco lança — é como os
            /// testes exercitam o caminho de falha do ledger.</summary>
            public Exception? Failure { get; set; }

            public void ShrinkPartition(
                int diskIndex, int partitionIndex, long bytesToFree, int newPartitionsPlanned)
            {
                if (Failure is not null)
                    throw Failure;

                ShrunkBytes = bytesToFree;
                NewPartitionsPlanned = newPartitionsPlanned;
            }

            public void EnsureUnallocatedSpace(
                int diskIndex, long requiredBytes, int newPartitionsPlanned)
            {
                if (Failure is not null)
                    throw Failure;

                NewPartitionsPlanned = newPartitionsPlanned;
            }
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

        /// <summary>
        /// own-linux-installer: o provider real procura a ISO da mídia live em
        /// <c>%ProgramData%</c>. Nenhum teste pode depender de um arquivo de 384 MB existir na
        /// máquina — o resultado dependeria de a máquina ter rodado o build da mídia.
        /// </summary>
        private sealed class FakeLiveMediaProvider : ILiveMediaProvider
        {
            public string GetIsoPath() => @"C:\ProgramData\LinuxHub\LiveMedia\linuxhub-live.iso";
        }

        /// <summary>
        /// own-linux-installer: o registro precisa resolver o mecanismo que o catálogo declara
        /// para o Ubuntu. Registrar só um mecanismo acoplava a suíte à declaração do catálogo:
        /// trocar a declaração fazia 11 testes falharem por "mecanismo não registrado", que não
        /// é o que nenhum deles se propõe a verificar.
        /// </summary>
        private sealed class FakeOwnLiveInstallerPreparer : IUnattendedInstallPreparer
        {
            public UnattendedInstallMechanism Mechanism => UnattendedInstallMechanism.OwnLiveInstaller;

            public UnattendedPreparationResult Prepare(
                InstallerConfig config, int diskIndex, StagingPartition? staging) =>
                new(SeedPartitionNumber: 0,
                    BootParameters: new UnattendedBootParameters(
                        IsUnattended: true, KernelParameters: string.Empty, ExtraInitrdGrubPath: null));
        }

        /// <summary>Como o de raiz, o service real ELEVA (cria partição, formata, monta a ISO).
        /// Nenhum teste pode tocar o concreto.</summary>
        private sealed class FakeLiveMediaStagingService : ILiveMediaStagingService
        {
            public int CreateCalls { get; private set; }
            public string? CopiedFrom { get; private set; }

            public long RequiredBytesFor(long liveMediaIsoSizeBytes) =>
                liveMediaIsoSizeBytes + 512L * 1024 * 1024;

            public LiveMediaStagingPartition Create(int diskIndex, long liveMediaIsoSizeBytes)
            {
                CreateCalls++;
                return new(diskIndex, 5, "BBBBBBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF",
                    OffsetBytes: 290L * 1024 * 1024 * 1024, SizeBytes: 1024L * 1024 * 1024);
            }

            public void CopyLiveFiles(LiveMediaStagingPartition partition, string liveMediaIsoWindowsPath) =>
                CopiedFrom = liveMediaIsoWindowsPath;
        }

        /// <summary>own-linux-installer: o service real ELEVA para criar partição no disco —
        /// nenhum teste pode tocar o concreto.</summary>
        private sealed class FakeLinuxRootPartitionService : ILinuxRootPartitionService
        {
            public int CreateCalls { get; private set; }

            public LinuxRootPartition Create(int diskIndex)
            {
                CreateCalls++;
                return new(diskIndex, 6, "CCCCCCCC-DDDD-EEEE-FFFF-000000000000",
                    OffsetBytes: 300L * 1024 * 1024 * 1024, SizeBytes: 50L * 1024 * 1024 * 1024);
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
                return new(diskIndex, 9, "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE", OffsetBytes: 250L * 1024 * 1024 * 1024);
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
            public bool EncryptionQueryFailed { get; set; }

            public bool IsSecureBootEnabled() => SecureBoot;

            public VolumeEncryptionState GetVolumeEncryptionState(char driveLetter) =>
                EncryptionQueryFailed
                    ? new VolumeEncryptionState("QueryFailed", 0, -1, QuerySucceeded: false)
                    : BitLocker
                        ? new VolumeEncryptionState("FullyEncrypted", 100, 1, QuerySucceeded: true)
                        : new VolumeEncryptionState("FullyDecrypted", 0, 0, QuerySucceeded: true);
        }

        private sealed class FakeDiskLayoutProvider : IDiskLayoutProvider
        {
            public DiskLayout GetLayout(int diskIndex) => new(
                Index: diskIndex,
                SerialNumber: "TESTDISK",
                Model: "Fake",
                SizeBytes: 512L * 1024 * 1024 * 1024,
                IsGpt: true,
                IsLargestDisk: true,
                IsSmallestDisk: true,
                Partitions:
                [
                    new PartitionLayout(
                        Number: 1,
                        OffsetBytes: 1024 * 1024,
                        SizeBytes: 100 * 1024 * 1024,
                        GptType: "{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}",
                        IsEfiSystemPartition: true,
                        Guid: "{11111111-1111-1111-1111-111111111111}"),
                    new PartitionLayout(
                        Number: 2,
                        OffsetBytes: 101 * 1024 * 1024,
                        SizeBytes: 200L * 1024 * 1024 * 1024,
                        GptType: "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}",
                        IsEfiSystemPartition: false,
                        Guid: "{22222222-2222-2222-2222-222222222222}"),
                    new PartitionLayout(
                        Number: 3,
                        OffsetBytes: 400L * 1024 * 1024 * 1024,
                        SizeBytes: 1L * 1024 * 1024 * 1024,
                        GptType: "{de94bba4-06d1-4d40-a16a-bfd50179d6ac}",
                        IsEfiSystemPartition: false,
                        Guid: "{33333333-3333-3333-3333-333333333333}"),
                ],
                Guid: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                UniqueId: "USBSTOR\\TEST",
                LogicalSectorSizeBytes: 512);
        }

        private sealed class FakePlanPublisher : IInstallationPlanPublisher
        {
            public InstallationPlan? Current { get; private set; }
            public string? PublishedPath { get; private set; }
            public int PublishCalls { get; private set; }
            public int StagingIdentityUpdates { get; private set; }

            public string Publish(InstallationPlan plan, string password)
            {
                InstallationPlanValidator.Validate(plan);
                string? directory = Path.GetDirectoryName(plan.Account.PasswordWindowsPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(plan.Account.PasswordWindowsPath, password);

                Current = plan;
                PublishedPath = @"C:\ProgramData\LinuxHub\Transactions\" + plan.PlanId + @"\installation-plan.json";
                PublishCalls++;
                return PublishedPath;
            }

            public InstallationPlan ReadValidated(string path) =>
                Current ?? throw new InvalidOperationException("No plan.");

            public void UpdateStagingIdentity(int number, long offsetBytes, string partitionUuid)
            {
                if (Current is null)
                    throw new InvalidOperationException("No plan.");
                Current.Disk.Installer.Number = number;
                Current.Disk.Installer.OffsetBytes = offsetBytes;
                Current.Disk.Installer.PartitionUuid = partitionUuid;
                InstallationPlanValidator.Validate(Current);
                StagingIdentityUpdates++;
            }

            public void Clear()
            {
                Current = null;
                PublishedPath = null;
            }
        }

        private sealed class InMemoryLedgerFactory : IInstallationExecutionLedgerFactory
        {
            /// <summary>Último ledger criado, para o teste inspecionar o estado final.</summary>
            public IInstallationExecutionLedger? Last { get; private set; }

            public IInstallationExecutionLedger Create(string planId, string statePath) =>
                Last = new InMemoryLedger(InstallationStateMachine.Create(planId));

            public IInstallationExecutionLedger Open(string statePath) =>
                throw new NotSupportedException();

            private sealed class InMemoryLedger : IInstallationExecutionLedger
            {
                private readonly InstallationStateMachine _machine;

                public InMemoryLedger(InstallationStateMachine machine) => _machine = machine;

                public string StatePath => "memory";
                public InstallationExecutionState State => _machine.State;

                public void StartStep(string step) => _machine.StartStep(step);
                public void SkipOptionalStep(string step) => _machine.SkipOptionalStep(step);
                public void CompleteStep(string step) => _machine.CompleteStep(step);
                public void SetProgress(string stage, int overallPercent, int? detailPercent = null) =>
                    _machine.SetProgress(stage, overallPercent, detailPercent);
                public void Fail(string code, string message, string component) =>
                    _machine.Fail(code, message, component);
                public void MarkSucceeded() => _machine.MarkSucceeded();
                public void BeginRollback() => _machine.BeginRollback();
                public IReadOnlyList<string> GetCompensationCandidates() => _machine.GetCompensationCandidates();
                public void CompleteCompensation(string step) => _machine.CompleteCompensation(step);
                public void CompleteRollback() => _machine.CompleteRollback();
                public void MarkRollbackIncomplete(string code, string message) =>
                    _machine.MarkRollbackIncomplete(code, message);
                public string? GetNextPendingArmedStepId() => _machine.GetNextPendingArmedStepId();
            }
        }

        private static InstallWizardViewModel BuildViewModel(
            IBootStagingService bootStaging,
            IBootSecurityService? bootSecurity = null,
            IIsoFileInfoProvider? isoFileInfo = null,
            FakeStagingPartitionService? staging = null,
            FakeDiskPartitioningService? partitioning = null,
            FakePartitionInventoryService? partitions = null,
            FakeInstallerConfigWriter? configWriter = null,
            FakePlanPublisher? planPublisher = null,
            IInterruptedTransactionProbe? interruptedProbe = null,
            ICompatibilityFactsProbe? compatibilityFacts = null,
            InMemoryLedgerFactory? ledgerFactory = null)
        {
            var iso = new IsoAcquisitionViewModel(new FakeIsoDownloadService(), new FakeDistroDetectionService(), new FakeDownloadedIsoRepository(), new FakeArtifactVerifier());
            var target = new TargetSelectionViewModel(
                new FakeDiskInventoryService(),
                partitions ?? new FakePartitionInventoryService(),
                new FakeFirmwareService());

            var stagingService = staging ?? new FakeStagingPartitionService();
            var isoInfo = isoFileInfo ?? new FakeIsoFileInfoProvider();
            var flowRunner = new InstallationFlowRunner(
                new FakeDiskLayoutProvider(),
                planPublisher ?? new FakePlanPublisher(),
                ledgerFactory ?? new InMemoryLedgerFactory(),
                partitioning ?? new FakeDiskPartitioningService(),
                stagingService,
                isoInfo,
                new InstallerConfigBuilder(new FakeEspLocatorService()),
                configWriter ?? new FakeInstallerConfigWriter(),
                new UnattendedInstallPreparerRegistry(
                    [new FakeUnattendedInstallPreparer(), new FakeOwnLiveInstallerPreparer()]),
                bootStaging,
                new FakeLinuxRootPartitionService(),
                new FakeLiveMediaStagingService());

            var vm = new InstallWizardViewModel(
                iso,
                target,
                new AccountViewModel { Username = "joao", Password = "123", ConfirmPassword = "123", Hostname = "pc" },
                new RegionalSettingsViewModel(new FakeSystemInfoProvider()),
                bootSecurity ?? new FakeBootSecurityService(),
                stagingService,
                isoInfo,
                flowRunner,
                interruptedTransactionProbe: interruptedProbe ?? new FakeInterruptedTransactionProbe(),
                compatibilityFacts: compatibilityFacts ?? FakeCompatibilityFactsProbe.Compatible(),
                liveMediaProvider: new FakeLiveMediaProvider());

            // ResolvedIsoPath só é preenchido pelo download ou pela seleção manual; o wizard
            // recusa instalar sem ele, então o teste passa pelo caminho de download falso.
            iso.DownloadIsoCommand.Execute(null);

            // Dual-boot é o padrão do wizard; estes testes cobrem o fluxo de substituição, que
            // não depende de haver partição elegível na máquina de teste.
            vm.Target.Mode = InstallMode.Replace;

            return vm;
        }

        private sealed class FakeInterruptedTransactionProbe : IInterruptedTransactionProbe
        {
            public InterruptedTransactionInfo? Info { get; set; }
            public InterruptedTransactionInfo? FindBlockingTransaction(string systemDrive) => Info;
        }

        private sealed class FakeCompatibilityFactsProbe : ICompatibilityFactsProbe
        {
            public CompatibilityFacts? Facts { get; set; }
            public Exception? Failure { get; set; }
            public int ReadCalls { get; private set; }
            public int? LastDiskNumber { get; private set; }

            /// <summary>Máquina que passa em todas as regras — o padrão dos testes que não
            /// estão exercitando o preflight.</summary>
            public static FakeCompatibilityFactsProbe Compatible() => new()
            {
                Facts = new CompatibilityFacts
                {
                    TopologyDeterminate = true,
                    EncryptionQuerySucceeded = true,
                    EncryptionConversionStatus = "FullyDecrypted",
                    EncryptionPercentComplete = 0,
                    EncryptionProtectionStatus = 0,
                    BootNextProbeResult = "ok",
                },
            };

            public CompatibilityFacts Read(int diskNumber, char systemDriveLetter)
            {
                ReadCalls++;
                LastDiskNumber = diskNumber;

                if (Failure is not null)
                    throw Failure;

                return Facts ?? new CompatibilityFacts();
            }
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

        /// <summary>O usuário pediu para ver o que está sendo feito durante a instalação: um
        /// log acumulado, não só a última linha sobrescrevendo a anterior. Cada passo relatado
        /// tem que virar uma entrada nova em <see cref="InstallWizardViewModel.InstallLog"/>,
        /// preservando as anteriores.</summary>
        [Fact]
        public async Task Confirming_AccumulatesEachStepInTheInstallLog()
        {
            var bootStaging = new BlockingBootStagingService();
            var vm = BuildViewModel(bootStaging);

            await ConfirmAndWaitForInstallStartAsync(vm, bootStaging);

            // Pelo menos o passo de preparação inicial e o passo em que a instalação está
            // bloqueada (BlockingBootStagingService segura no boot staging) já têm que estar
            // no log, como linhas distintas — não uma sobrescrevendo a outra.
            Assert.True(vm.InstallLog.Count >= 2);
            Assert.NotEqual(vm.InstallLog[0], vm.InstallLog[^1]);

            bootStaging.Release();
            await WaitUntilIdleAsync(vm);
        }

        /// <summary>Uma nova tentativa depois de um erro não pode misturar o histórico da
        /// tentativa anterior com o da nova — confundiria o que de fato está acontecendo agora.</summary>
        [Fact]
        public async Task Confirming_ClearsThePreviousInstallLogOnANewAttempt()
        {
            var bootStaging = new BlockingBootStagingService();
            var vm = BuildViewModel(bootStaging);

            await ConfirmAndWaitForInstallStartAsync(vm, bootStaging);
            int firstAttemptLogCount = vm.InstallLog.Count;
            Assert.True(firstAttemptLogCount > 0);

            bootStaging.Release();
            await WaitUntilIdleAsync(vm);

            await ConfirmAndWaitForInstallStartAsync(vm, bootStaging);

            // O log da segunda tentativa não pode ser maior ou igual ao dobro do primeiro por
            // acaso — o teste real é que ele não CRESCEU sobre o anterior sem limpar.
            Assert.True(vm.InstallLog.Count <= firstAttemptLogCount);

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
                        PartitionIndex = 2,
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

        /// <summary>
        /// O Ubuntu declara subiquity, cuja ISO já embute o GNOME — oferecer a escolha ali
        /// prometeria um controle que o usuário não tem. A regra é do MECANISMO, não da
        /// identidade da distro: é o que faz uma distro nova passar a oferecer (ou não) a opção
        /// sem tocar na lógica da interface.
        /// </summary>
        [Fact]
        public void DesktopEnvironmentStep_HiddenWhenTheMechanismDoesNotSupportTheChoice()
        {
            var vm = BuildViewModel(new CapturingBootStagingService());

            Assert.Equal(UnattendedInstallMechanism.Subiquity, vm.Iso.ActiveMechanism);
            Assert.False(vm.IsDesktopEnvironmentStepVisible);
        }

        /// <summary>E o valor escolhido não chega ao instalador quando o seletor nem apareceu —
        /// senão o wizard gravaria uma decisão que o usuário nunca viu.</summary>
        [Fact]
        public async Task DesktopEnvironment_IsNotWrittenWhenTheStepIsHidden()
        {
            var configWriter = new FakeInstallerConfigWriter();
            var vm = BuildViewModel(new CapturingBootStagingService(), configWriter: configWriter);

            vm.Regional.DesktopEnvironment = "Hyprland";

            await ReplaceInstallAsync(vm);

            Assert.Equal(string.Empty, configWriter.Saved!.DesktopEnvironment);
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

        /// <summary>
        /// Um passo que falha tem que ser registrado como falha. O ledger é gravado em disco e
        /// sobrevive ao processo: deixado em `running`, o InterruptedTransactionProbe encontra
        /// uma transação não resolvida em toda abertura seguinte e bloqueia qualquer nova
        /// instalação — para sempre, porque nada apaga esse estado. Uma instalação que falha
        /// uma vez inutilizaria o app.
        /// </summary>
        [Fact]
        public async Task FailedStep_MarksTheLedgerFailedInsteadOfLeavingItRunning()
        {
            var ledgers = new InMemoryLedgerFactory();
            var partitioning = new FakeDiskPartitioningService
            {
                Failure = new InvalidOperationException("shrink falhou"),
            };
            var vm = BuildViewModel(
                new BlockingBootStagingService(),
                partitioning: partitioning,
                ledgerFactory: ledgers);

            vm.InstallCommand.Execute(null);
            vm.PendingConfirmation!.ConfirmCommand.Execute(null);
            await WaitUntilIdleAsync(vm);

            Assert.NotNull(ledgers.Last);
            Assert.Equal(InstallationStatus.Failed, ledgers.Last!.State.Status);
            Assert.NotNull(ledgers.Last.State.Failure);
        }

        [Fact]
        public void CompatibilityRejection_BlocksMutatingInstall()
        {
            var vm = BuildViewModel(new BlockingBootStagingService());
            vm.ApplyCompatibilityFacts(new CompatibilityFacts
            {
                DiskIsDynamic = true,
                TopologyDeterminate = true,
                EncryptionQuerySucceeded = true,
                EncryptionConversionStatus = "FullyDecrypted",
                EncryptionPercentComplete = 0,
                EncryptionProtectionStatus = 0,
            });

            Assert.False(vm.CanStartMutatingInstall);
            Assert.False(vm.InstallCommand.CanExecute(null));
            vm.InstallCommand.Execute(null);
            Assert.Null(vm.PendingConfirmation);
        }

        /// <summary>
        /// O preflight tem que RODAR contra a máquina, não só existir. Toda a máquina de regras
        /// (disco dinâmico, Storage Spaces, RAID/VMD, BitLocker) já estava pronta e testada, mas
        /// nada em produção executava o script: o gate ficava permanentemente aberto e a
        /// instalação destrutiva seguia numa topologia recusada. É o padrão que constitution
        /// §7.1 chama de gate desarmado em runtime.
        /// </summary>
        [Fact]
        public void BeginInstall_RunsThePreflightAgainstTheTargetDisk()
        {
            var probe = FakeCompatibilityFactsProbe.Compatible();
            var vm = BuildViewModel(new BlockingBootStagingService(), compatibilityFacts: probe);

            vm.InstallCommand.Execute(null);

            Assert.Equal(1, probe.ReadCalls);
            Assert.NotNull(vm.PendingConfirmation);
        }

        [Fact]
        public void BeginInstall_RejectedTopology_NeverReachesConfirmation()
        {
            var probe = new FakeCompatibilityFactsProbe
            {
                Facts = new CompatibilityFacts
                {
                    DiskIsDynamic = true,
                    TopologyDeterminate = true,
                    EncryptionQuerySucceeded = true,
                    EncryptionConversionStatus = "FullyDecrypted",
                    EncryptionPercentComplete = 0,
                    EncryptionProtectionStatus = 0,
                    BootNextProbeResult = "ok",
                },
            };
            var vm = BuildViewModel(new BlockingBootStagingService(), compatibilityFacts: probe);

            vm.InstallCommand.Execute(null);

            Assert.Null(vm.PendingConfirmation);
            Assert.True(vm.LastCompatibilityReport!.HasRejection);
        }

        /// <summary>
        /// Uma topologia que não pôde ser lida é indistinguível de uma incompatível. §6.1 manda
        /// parar em vez de assumir o caso provável — liberar aqui seria transformar uma falha de
        /// consulta em permissão para reparticionar.
        /// </summary>
        [Fact]
        public void BeginInstall_PreflightQueryFails_BlocksInsteadOfAssuming()
        {
            var probe = new FakeCompatibilityFactsProbe
            {
                Failure = new InvalidOperationException("script falhou"),
            };
            var vm = BuildViewModel(new BlockingBootStagingService(), compatibilityFacts: probe);

            vm.InstallCommand.Execute(null);

            Assert.Null(vm.PendingConfirmation);
        }

        [Fact]
        public void CompatibilityWarning_AllowsAdvance()
        {
            var vm = BuildViewModel(new BlockingBootStagingService());
            vm.ApplyCompatibilityFacts(new CompatibilityFacts
            {
                TopologyDeterminate = true,
                EncryptionQuerySucceeded = true,
                EncryptionConversionStatus = "FullyDecrypted",
                EncryptionPercentComplete = 0,
                EncryptionProtectionStatus = 0,
                BootNextProbeResult = "skipped",
            });

            Assert.True(vm.CanStartMutatingInstall);
            Assert.True(vm.InstallCommand.CanExecute(null));
            Assert.NotEmpty(vm.LastCompatibilityReport!.Warnings);
        }

        [Fact]
        public void PendingTransaction_BlocksNewInstall()
        {
            var probe = new FakeInterruptedTransactionProbe
            {
                Info = new InterruptedTransactionInfo(
                    new string('c', 32),
                    "state.json",
                    "failed",
                    "ROLLBACK_INCOMPLETE",
                    "geometry"),
            };
            var vm = BuildViewModel(new BlockingBootStagingService(), interruptedProbe: probe);
            vm.RaiseStartupWarnings();

            Assert.True(vm.HasBlockingTransaction);
            Assert.False(vm.CanStartMutatingInstall);
            Assert.False(vm.InstallCommand.CanExecute(null));
        }
    }
}
