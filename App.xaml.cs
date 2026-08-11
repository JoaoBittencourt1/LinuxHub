using System.Windows;
using LinuxHub.Features.Catalog.ViewModels;
using LinuxHub.Features.InstallWizard.Services;
using LinuxHub.Features.InstallWizard.ViewModels;
using LinuxHub.Features.UpdateCheck.Services;
using LinuxHub.Shell;
using Wpf.Ui.Appearance;
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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);

            IIsoDownloadService isoDownloadService = new IsoDownloadService();
            IDistroDetectionService distroDetectionService = new DistroDetectionService();
            IDownloadedIsoRepository downloadedIsoRepository = new DownloadedIsoRepository();
            IDiskInventoryService diskInventoryService = new DiskInventoryService();
            IPartitionInventoryService partitionInventoryService = new PartitionInventoryService();
            IFirmwareService firmwareService = new FirmwareService();
            IEspLocatorService espLocatorService = new EspLocatorService();
            IDiskPartitioningService diskPartitioningService = new DiskPartitioningService();
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
                ]);

            IGrubAssetProvider grubAssetProvider = new GrubAssetProvider();
            IMbrBackupService mbrBackupService = new MbrBackupService();
            IBootConfigurationService bootConfigurationService = new BootConfigurationService();
            IIsoBootEntryBuilderRegistry isoBootEntryBuilderRegistry =
                new IsoBootEntryBuilderRegistry(
                [
                    CasperIsoBootEntryBuilder.Instance,
                    ArchisoIsoBootEntryBuilder.Instance,
                ]);
            IBootStagingService bootStagingService = new BootStagingService(
                espLocatorService, grubAssetProvider, mbrBackupService, bootConfigurationService,
                isoBootEntryBuilderRegistry);
            IBootSecurityService bootSecurityService = new BootSecurityService();
            IIsoFileInfoProvider isoFileInfoProvider = new IsoFileInfoProvider();
            IStagingPartitionService stagingPartitionService = new StagingPartitionService(isoFileInfoProvider);

            var catalogViewModel = new CatalogViewModel();

            var isoAcquisitionViewModel = new IsoAcquisitionViewModel(isoDownloadService, distroDetectionService, downloadedIsoRepository);
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
                installerConfigBuilder,
                installerConfigWriter,
                diskPartitioningService,
                unattendedPreparerRegistry,
                bootStagingService,
                bootSecurityService,
                stagingPartitionService,
                isoFileInfoProvider);

            IUpdateCheckService updateCheckService = new GitHubUpdateCheckService();
            var updateNoticePresenter = new UpdateNoticePresenter(updateCheckService);

            var mainWindow = new MainWindow(catalogViewModel, installWizardViewModel);
            MainWindow = mainWindow;
            mainWindow.Show();

            // Depois do Show(), e sem esperar: a janela precisa estar visível e utilizável
            // mesmo que a rede esteja lenta ou não responda.
            updateNoticePresenter.CheckInBackground(mainWindow);
        }
    }
}
