using System.Windows;
using LinuxHub.Common.Appearance;
using LinuxHub.Common.Data;
using LinuxHub.Features.Catalog.ViewModels;
using LinuxHub.Features.InstallWizard.Services;
using LinuxHub.Features.InstallWizard.ViewModels;
using LinuxHub.Features.UpdateCheck.Services;
using LinuxHub.Shell;
using Wpf.Ui.Controls;

namespace LinuxHub
{
    /// <summary>
    /// Composition root manual: constrói os services concretos e injeta nas
    /// ViewModels via construtor. Sem container de DI — ver design.md do change
    /// restructure-feature-based-mvvm (não se justifica pelo tamanho do app).
    /// </summary>
    public partial class App : Application
    {
        // Limite duro pro fetch do catálogo no startup: sem servidor configurado (padrão hoje —
        // ver CatalogSourceConfig), a resolução de DNS do placeholder falha rápido; com um
        // servidor lento ou fora do ar, este timeout é o que garante que a janela principal
        // ainda abre em tempo razoável em vez de o app parecer travado.
        private static readonly TimeSpan CatalogFetchTimeout = TimeSpan.FromSeconds(5);

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            IThemeService themeService = new ThemeService();
            themeService.ApplyPersistedOrDefault();

            // Resolvido ANTES de qualquer ViewModel que leia DistroCatalog.All ser construída —
            // CatalogViewModel e IsoAcquisitionViewModel capturam a lista uma vez no próprio
            // construtor, então trocar DistroCatalog.All depois não teria efeito nelas.
            CatalogFetchOutcome catalogOutcome = await ResolveEffectiveCatalogAsync();

            IIsoDownloadService isoDownloadService = new IsoDownloadService();
            IDistroDetectionService distroDetectionService = new DistroDetectionService();
            IDownloadedIsoRepository downloadedIsoRepository = new DownloadedIsoRepository();
            IArtifactVerifier artifactVerifier = new ArtifactVerifier();
            IDiskInventoryService diskInventoryService = new DiskInventoryService();
            IPartitionInventoryService partitionInventoryService = new PartitionInventoryService();
            IFirmwareService firmwareService = new FirmwareService();
            IEspLocatorService espLocatorService = new EspLocatorService();
            IInstallationPlanPublisher installationPlanPublisher = new InstallationPlanPublisher();
            IInstallationPlanMutationGuard installationPlanMutationGuard =
                new InstallationPlanMutationGuard(installationPlanPublisher);
            IDiskPartitioningService diskPartitioningService =
                new DiskPartitioningService(installationPlanMutationGuard);
            ISystemInfoProvider systemInfoProvider = new SystemInfoProvider();
            IInstallerConfigWriter installerConfigWriter = new InstallerConfigWriter();
            var installerConfigBuilder = new InstallerConfigBuilder(espLocatorService);

            ICloudInitSeedWriter cloudInitSeedWriter = new CloudInitSeedWriter();
            IDiskLayoutProvider diskLayoutProvider = new DiskLayoutProvider();
            IUnattendedInitrdWriter unattendedInitrdWriter = new UnattendedInitrdWriter();
            IArchinstallScriptWriter archinstallScriptWriter = new ArchinstallScriptWriter();
            IUnattendedInstallPreparerRegistry unattendedPreparerRegistry =
                new UnattendedInstallPreparerRegistry(
                [
                    new SubiquityInstallPreparer(cloudInitSeedWriter, diskLayoutProvider),
                    new UbiquityInstallPreparer(
                        cloudInitSeedWriter, diskLayoutProvider, unattendedInitrdWriter),
                    new ArchinstallInstallPreparer(diskLayoutProvider, archinstallScriptWriter),
                    new OwnLiveInstallerPreparer(),
                ]);

            IGrubAssetProvider grubAssetProvider = new GrubAssetProvider();
            IMbrBackupService mbrBackupService = new MbrBackupService(installationPlanMutationGuard);
            IBootConfigurationService bootConfigurationService = new BootConfigurationService();
            IIsoBootEntryBuilderRegistry isoBootEntryBuilderRegistry =
                new IsoBootEntryBuilderRegistry(
                [
                    CasperIsoBootEntryBuilder.Instance,
                    ArchisoIsoBootEntryBuilder.Instance,
                ]);
            IIsoHostPartitionLocator isoHostPartitionLocator = new IsoHostPartitionLocator();
            IBootStagingService bootStagingService = new BootStagingService(
                espLocatorService, grubAssetProvider, mbrBackupService, bootConfigurationService,
                isoBootEntryBuilderRegistry, isoHostPartitionLocator, installationPlanMutationGuard);
            IBootSecurityService bootSecurityService = new BootSecurityService();
            IIsoFileInfoProvider isoFileInfoProvider = new IsoFileInfoProvider();
            IStagingPartitionService stagingPartitionService =
                new StagingPartitionService(isoFileInfoProvider, installationPlanMutationGuard);
            IInstallationExecutionLedgerFactory ledgerFactory = new InstallationExecutionLedgerFactory();
            IInstallationFlowRunner installationFlowRunner = new InstallationFlowRunner(
                diskLayoutProvider,
                installationPlanPublisher,
                ledgerFactory,
                diskPartitioningService,
                stagingPartitionService,
                isoFileInfoProvider,
                installerConfigBuilder,
                installerConfigWriter,
                unattendedPreparerRegistry,
                bootStagingService,
                new LinuxRootPartitionService(installationPlanMutationGuard));

            // Phase 5: recovery/compensation exist but stay unreachable until phase 8 (§7.1).
            // Not injected into InstallationFlowRunner — task 5.9.
            IRecoveryAgentRegistrar recoveryAgentRegistrar = new DisarmedRecoveryAgentRegistrar();
            ICompensationOrchestrator compensationOrchestrator = new DisarmedCompensationOrchestrator();
            if (recoveryAgentRegistrar.IsArmed || compensationOrchestrator.IsArmed)
                throw new InvalidOperationException("Recovery/compensation must stay disarmed until phase 8.");

            var catalogViewModel = new CatalogViewModel();

            var isoAcquisitionViewModel = new IsoAcquisitionViewModel(isoDownloadService, distroDetectionService, downloadedIsoRepository, artifactVerifier);
            var targetSelectionViewModel = new TargetSelectionViewModel(diskInventoryService, partitionInventoryService, firmwareService);
            var accountViewModel = new AccountViewModel();
            // Construído aqui, na thread de UI: o layout de teclado que o SystemInfoProvider lê
            // é o da thread que pergunta (ver o comentário lá).
            var regionalSettingsViewModel = new RegionalSettingsViewModel(systemInfoProvider);
            var installWizardViewModel = new InstallWizardViewModel(
                isoAcquisitionViewModel,
                targetSelectionViewModel,
                accountViewModel,
                regionalSettingsViewModel,
                bootSecurityService,
                stagingPartitionService,
                isoFileInfoProvider,
                installationFlowRunner,
                liveMediaProvider: new LiveMediaProvider());
            installWizardViewModel.CatalogOutcome = catalogOutcome;

            IUpdateCheckService updateCheckService = new GitHubUpdateCheckService();
            var updateNoticePresenter = new UpdateNoticePresenter(updateCheckService);

            var mainWindow = new MainWindow(catalogViewModel, installWizardViewModel, themeService);
            MainWindow = mainWindow;
            mainWindow.Show();

            // Depois do Show(), e sem esperar: a janela precisa estar visível e utilizável
            // mesmo que a rede esteja lenta ou não responda.
            updateNoticePresenter.CheckInBackground(mainWindow);
        }

        /// <summary>
        /// Busca e verifica o catálogo remoto assinado; em qualquer resultado que não seja
        /// verificado, <see cref="DistroCatalog.All"/> permanece no fallback embarcado (seu
        /// valor padrão) — nada aqui apaga dado nenhum, só tenta substituí-lo por algo mais
        /// novo. Uma configuração de URL malformada (<see cref="CatalogSourceConfig"/>) é
        /// tratada como o mesmo caso de "catálogo indisponível", não como uma falha fatal de
        /// inicialização: um operador com <c>LINUXHUB_CATALOG_BASE_URL</c> errado não pode
        /// impedir o app de abrir.
        /// </summary>
        private static async Task<CatalogFetchOutcome> ResolveEffectiveCatalogAsync()
        {
            try
            {
                ICatalogSourceConfig sourceConfig = new CatalogSourceConfig();
                using var signatureVerifier = new CatalogSignatureVerifier();
                ICatalogClient catalogClient = new CatalogClient(sourceConfig, signatureVerifier, DistroCatalog.Fallback);

                using var timeout = new CancellationTokenSource(CatalogFetchTimeout);
                var result = await catalogClient.FetchAsync(timeout.Token);

                if (result.IsVerified)
                    DistroCatalog.All = result.Distros!;

                return result.Outcome;
            }
            catch (ArgumentException)
            {
                // CatalogSourceConfig rejeitou LINUXHUB_CATALOG_BASE_URL (URL ausente-mas-inválida
                // — vazio já cai no placeholder, não aqui). DistroCatalog.All já está no fallback.
                return CatalogFetchOutcome.NetworkUnavailable;
            }
        }
    }
}
