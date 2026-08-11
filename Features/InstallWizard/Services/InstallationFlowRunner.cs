using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed record InstallationFlowRequest(
        DistroInfo Distro,
        string IsoPath,
        bool IsUefi,
        bool IsReplaceMode,
        bool IsDualBootMode,
        bool IsAutoinstallActive,
        UnattendedInstallMechanism ActiveMechanism,
        LiveSessionFamily LiveSession,
        int TargetDiskIndex,
        int? DualBootPartitionIndex,
        int LinuxPartitionSizeGb,
        string Username,
        string Password,
        string Hostname,
        string Locale,
        string Keymap,
        string Timezone,
        string DesktopEnvironment);

    public interface IInstallationFlowRunner
    {
        void Run(InstallationFlowRequest request, IProgress<string> progress);
    }

    /// <summary>
    /// Executes the armed Windows installation steps, directing
    /// <see cref="IInstallationExecutionLedger"/> for every transition (D12).
    /// Keeps orchestrating I/O out of the ViewModel.
    /// </summary>
    public sealed class InstallationFlowRunner : IInstallationFlowRunner
    {
        private readonly IDiskLayoutProvider _diskLayoutProvider;
        private readonly IInstallationPlanPublisher _planPublisher;
        private readonly IInstallationExecutionLedgerFactory _ledgerFactory;
        private readonly IDiskPartitioningService _diskPartitioning;
        private readonly IStagingPartitionService _stagingPartition;
        private readonly IIsoFileInfoProvider _isoFileInfo;
        private readonly InstallerConfigBuilder _configBuilder;
        private readonly IInstallerConfigWriter _configWriter;
        private readonly IUnattendedInstallPreparerRegistry _unattendedPreparers;
        private readonly IBootStagingService _bootStaging;

        public InstallationFlowRunner(
            IDiskLayoutProvider diskLayoutProvider,
            IInstallationPlanPublisher planPublisher,
            IInstallationExecutionLedgerFactory ledgerFactory,
            IDiskPartitioningService diskPartitioning,
            IStagingPartitionService stagingPartition,
            IIsoFileInfoProvider isoFileInfo,
            InstallerConfigBuilder configBuilder,
            IInstallerConfigWriter configWriter,
            IUnattendedInstallPreparerRegistry unattendedPreparers,
            IBootStagingService bootStaging)
        {
            _diskLayoutProvider = diskLayoutProvider ?? throw new ArgumentNullException(nameof(diskLayoutProvider));
            _planPublisher = planPublisher ?? throw new ArgumentNullException(nameof(planPublisher));
            _ledgerFactory = ledgerFactory ?? throw new ArgumentNullException(nameof(ledgerFactory));
            _diskPartitioning = diskPartitioning ?? throw new ArgumentNullException(nameof(diskPartitioning));
            _stagingPartition = stagingPartition ?? throw new ArgumentNullException(nameof(stagingPartition));
            _isoFileInfo = isoFileInfo ?? throw new ArgumentNullException(nameof(isoFileInfo));
            _configBuilder = configBuilder ?? throw new ArgumentNullException(nameof(configBuilder));
            _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));
            _unattendedPreparers = unattendedPreparers ?? throw new ArgumentNullException(nameof(unattendedPreparers));
            _bootStaging = bootStaging ?? throw new ArgumentNullException(nameof(bootStaging));
        }

        public void Run(InstallationFlowRequest request, IProgress<string> progress)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(progress);

            var loc = LocalizationManager.Instance;
            long isoSize = _isoFileInfo.GetSizeInBytes(request.IsoPath);
            long stagingSizeBytes = request.IsReplaceMode
                ? _stagingPartition.RequiredBytesFor(isoSize)
                : 0;

            InstallationPlan plan = BuildPlan(request, isoSize, stagingSizeBytes);
            string statePath = InstallationTransactionPaths.GetStatePath(
                plan.Disk.SystemDrive, plan.PlanId);
            IInstallationExecutionLedger ledger = _ledgerFactory.Create(plan.PlanId, statePath);

            RunStep(ledger, InstallationStepIds.WindowsPlanPublished, progress, loc, () =>
            {
                string password = request.IsAutoinstallActive ? request.Password : string.Empty;
                _planPublisher.Publish(plan, password);
            });

            long overheadBytes = PreparationOverheadBytes(request, isoSize);
            int newPartitions = 1
                + (request.IsReplaceMode ? 1 : 0)
                + (request.IsAutoinstallActive ? 1 : 0);

            RunStep(ledger, InstallationStepIds.WindowsDiskPrepared, progress, loc, () =>
            {
                if (request.IsDualBootMode && request.DualBootPartitionIndex is { } partitionIndex)
                {
                    _diskPartitioning.ShrinkPartition(
                        request.TargetDiskIndex,
                        partitionIndex,
                        (long)request.LinuxPartitionSizeGb * 1024 * 1024 * 1024 + overheadBytes,
                        newPartitions);
                }
                else
                {
                    _diskPartitioning.EnsureUnallocatedSpace(
                        request.TargetDiskIndex, overheadBytes, newPartitions);
                }
            });

            StagingPartition? staging = null;
            string isoPathForGrub = request.IsoPath;

            if (request.IsReplaceMode)
            {
                RunStep(ledger, InstallationStepIds.WindowsStagingPrepared, progress, loc, () =>
                {
                    staging = _stagingPartition.Create(request.TargetDiskIndex, isoSize);
                    _planPublisher.UpdateStagingIdentity(
                        staging.PartitionNumber, staging.OffsetBytes, staging.PartitionUuid);
                    _stagingPartition.CopyIso(staging, request.IsoPath, progress);
                    isoPathForGrub = StagingPartitionService.IsoGrubPath;
                });
            }
            else
            {
                ledger.SkipOptionalStep(InstallationStepIds.WindowsStagingPrepared);
            }

            UnattendedBootParameters unattended = UnattendedBootParameters.Interactive;

            if (request.IsAutoinstallActive)
            {
                RunStep(ledger, InstallationStepIds.WindowsInstallerConfigWritten, progress, loc, () =>
                {
                    int? efiIndex = request.IsUefi
                        ? _configBuilder.FindEfiPartitionIndex(request.TargetDiskIndex)
                        : null;
                    var config = InstallerConfigFromPlan.Derive(plan, efiIndex);
                    _configWriter.Save(config);
                    unattended = _unattendedPreparers
                        .Resolve(request.ActiveMechanism)
                        .Prepare(config, request.TargetDiskIndex, staging)
                        .BootParameters;
                });
            }
            else
            {
                ledger.SkipOptionalStep(InstallationStepIds.WindowsInstallerConfigWritten);
            }

            RunStep(ledger, InstallationStepIds.WindowsTemporaryBootPrepared, progress, loc, () =>
            {
                _bootStaging.InstallStagingBootloader(new BootStagingRequest(
                    DistroName: request.Distro.Name,
                    IsoPath: isoPathForGrub,
                    IsUefi: request.IsUefi,
                    TargetDiskIndex: request.TargetDiskIndex,
                    Unattended: unattended,
                    LiveSession: request.LiveSession,
                    IsoHostPartitionUuid: staging?.PartitionUuid));
            });

            ledger.MarkSucceeded();
        }

        private InstallationPlan BuildPlan(
            InstallationFlowRequest request,
            long isoSize,
            long stagingSizeBytes)
        {
            DiskLayout layout = _diskLayoutProvider.GetLayout(request.TargetDiskIndex);
            string systemDrive = InstallationTransactionPaths.NormalizeSystemDrive(
                Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");

            string isoSha256 = !string.IsNullOrWhiteSpace(request.Distro.Sha256)
                ? request.Distro.Sha256.ToLowerInvariant()
                : ArtifactHash.ComputeSha256(request.IsoPath);

            long catalogSize = request.Distro.SizeBytes > 0 ? request.Distro.SizeBytes : isoSize;

            return InstallationPlanFactory.Create(new BuildInstallationPlanRequest(
                Distro: request.Distro,
                IsoWindowsPath: request.IsoPath,
                IsoSizeBytes: catalogSize,
                IsoSha256: isoSha256,
                IsoUrl: request.Distro.DirectDownloadLink,
                IsUefi: request.IsUefi,
                Mode: request.IsReplaceMode ? InstallMode.Replace : InstallMode.DualBoot,
                Layout: layout,
                LogicalSectorSizeBytes: layout.LogicalSectorSizeBytes,
                SystemDrive: systemDrive,
                DiskUniqueId: InstallationPlanDiskIdentity.BuildUniqueId(layout),
                PartitionTableId: InstallationPlanDiskIdentity.BuildPartitionTableId(layout),
                WindowsPartition: InstallationPlanWindowsPartition.Resolve(
                    layout, request.DualBootPartitionIndex),
                BootPartition: InstallationPlanDiskIdentity.ResolveBootPartition(layout, request.IsUefi),
                RecoveryPartition: InstallationPlanDiskIdentity.ResolveRecoveryPartition(layout),
                FinalSizeGiB: request.IsDualBootMode ? request.LinuxPartitionSizeGb : 0,
                StagingSizeBytes: stagingSizeBytes,
                Username: request.IsAutoinstallActive ? request.Username : "linuxhub",
                Hostname: request.IsAutoinstallActive ? request.Hostname : "linuxhub",
                Locale: request.Locale,
                Keymap: request.Keymap,
                Timezone: request.Timezone,
                DesktopEnvironment: request.DesktopEnvironment));
        }

        private long PreparationOverheadBytes(InstallationFlowRequest request, long isoSize)
        {
            long overhead = request.IsAutoinstallActive ? CloudInitSeedWriter.RequiredBytes : 0;
            if (!request.IsReplaceMode)
                return overhead;

            return overhead + _stagingPartition.RequiredBytesFor(isoSize);
        }

        private static void RunStep(
            IInstallationExecutionLedger ledger,
            string stepId,
            IProgress<string> progress,
            LocalizationManager loc,
            Action action)
        {
            progress.Report(InstallationProgressCatalog.GetStatusText(stepId, loc));
            ledger.StartStep(stepId);
            ledger.SetProgress(
                stage: stepId.Replace('.', '-'),
                overallPercent: InstallationProgressCatalog.GetOverallPercent(stepId));
            action();
            ledger.CompleteStep(stepId);
        }
    }
}
