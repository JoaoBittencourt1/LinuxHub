using System.Windows;
using LinuxHub.Common.Theming;
using LinuxHub.Features.Catalog.ViewModels;
using LinuxHub.Features.InstallWizard.Services;
using LinuxHub.Features.InstallWizard.ViewModels;
using LinuxHub.Shell;
using Wpf.Ui.Appearance;

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

            ThemeManager.Instance.Apply(ApplicationTheme.Dark);

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
            var installerConfigBuilder = new InstallerConfigBuilder(systemInfoProvider, espLocatorService);

            ICloudInitSeedWriter cloudInitSeedWriter = new CloudInitSeedWriter();
            IDiskLayoutProvider diskLayoutProvider = new DiskLayoutProvider();
            IAutoinstallPreparationService autoinstallPreparationService =
                new AutoinstallPreparationService(cloudInitSeedWriter, diskLayoutProvider);

            IGrubAssetProvider grubAssetProvider = new GrubAssetProvider();
            IMbrBackupService mbrBackupService = new MbrBackupService();
            IBootConfigurationService bootConfigurationService = new BootConfigurationService();
            IBootStagingService bootStagingService = new BootStagingService(
                espLocatorService, grubAssetProvider, mbrBackupService, bootConfigurationService);
            IBootSecurityService bootSecurityService = new BootSecurityService();
            IIsoFileInfoProvider isoFileInfoProvider = new IsoFileInfoProvider();
            IStagingPartitionService stagingPartitionService = new StagingPartitionService(isoFileInfoProvider);

            var catalogViewModel = new CatalogViewModel();

            var isoAcquisitionViewModel = new IsoAcquisitionViewModel(isoDownloadService, distroDetectionService, downloadedIsoRepository);
            var targetSelectionViewModel = new TargetSelectionViewModel(diskInventoryService, partitionInventoryService, firmwareService);
            var accountViewModel = new AccountViewModel();
            var installWizardViewModel = new InstallWizardViewModel(
                isoAcquisitionViewModel,
                targetSelectionViewModel,
                accountViewModel,
                installerConfigBuilder,
                installerConfigWriter,
                diskPartitioningService,
                autoinstallPreparationService,
                bootStagingService,
                bootSecurityService,
                stagingPartitionService,
                isoFileInfoProvider);

            var mainWindow = new MainWindow(catalogViewModel, installWizardViewModel);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}
