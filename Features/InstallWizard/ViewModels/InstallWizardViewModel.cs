using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using LinuxHub.Common.Data;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Common.Mvvm;
using LinuxHub.Features.InstallWizard.Services;

namespace LinuxHub.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Wizard presentation: confirmation, progress text, and startup warnings.
    /// Disk/install sequencing is directed through <see cref="IInstallationFlowRunner"/>
    /// and the execution ledger (D12) — this type does not own the step catalog.
    /// </summary>
    public class InstallWizardViewModel : ObservableObject
    {
        private readonly IBootSecurityService _bootSecurity;
        private readonly IStagingPartitionService _stagingPartition;
        private readonly IIsoFileInfoProvider _isoFileInfo;
        private readonly IInstallationFlowRunner _flowRunner;
        private readonly ICompatibilityPreflightRunner _compatibilityPreflight;
        private readonly IInterruptedTransactionProbe _interruptedTransactionProbe;
        private readonly ICompatibilityFactsProbe _compatibilityFacts;
        private ConfirmationViewModel? _pendingConfirmation;
        private string? _installStatus;
        private CompatibilityPreflightReport? _lastCompatibilityReport;
        private InterruptedTransactionInfo? _blockingTransaction;

        /// <summary>Resultado da resolução do catálogo remoto no startup (App.xaml.cs), definido
        /// antes de <see cref="RaiseStartupWarnings"/> rodar.</summary>
        public CatalogFetchOutcome? CatalogOutcome { get; set; }

        public InstallWizardViewModel(
            IsoAcquisitionViewModel iso,
            TargetSelectionViewModel target,
            AccountViewModel account,
            RegionalSettingsViewModel regional,
            IBootSecurityService bootSecurity,
            IStagingPartitionService stagingPartition,
            IIsoFileInfoProvider isoFileInfo,
            IInstallationFlowRunner flowRunner,
            ICompatibilityPreflightRunner? compatibilityPreflight = null,
            IInterruptedTransactionProbe? interruptedTransactionProbe = null,
            ICompatibilityFactsProbe? compatibilityFacts = null)
        {
            Iso = iso ?? throw new ArgumentNullException(nameof(iso));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Account = account ?? throw new ArgumentNullException(nameof(account));
            Regional = regional ?? throw new ArgumentNullException(nameof(regional));
            _bootSecurity = bootSecurity ?? throw new ArgumentNullException(nameof(bootSecurity));
            _stagingPartition = stagingPartition ?? throw new ArgumentNullException(nameof(stagingPartition));
            _isoFileInfo = isoFileInfo ?? throw new ArgumentNullException(nameof(isoFileInfo));
            _flowRunner = flowRunner ?? throw new ArgumentNullException(nameof(flowRunner));
            _compatibilityPreflight = compatibilityPreflight ?? CompatibilityPreflightRunner.CreateDefault();
            _interruptedTransactionProbe = interruptedTransactionProbe ?? new InterruptedTransactionProbe();
            _compatibilityFacts = compatibilityFacts ?? new CompatibilityFactsProbe();

            Iso.Notify += (title, message, isError) => Notify?.Invoke(title, message, isError);

            Iso.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(IsoAcquisitionViewModel.IsAutoinstallActive))
                    return;

                OnPropertyChanged(nameof(IsAccountStepVisible));
                OnPropertyChanged(nameof(IsRegionalStepVisible));
                OnPropertyChanged(nameof(IsDesktopEnvironmentStepVisible));
            };

            InstallCommand = new RelayCommand(BeginInstall, () => CanStartMutatingInstall);
        }

        public CompatibilityPreflightReport? LastCompatibilityReport
        {
            get => _lastCompatibilityReport;
            private set => SetProperty(ref _lastCompatibilityReport, value);
        }

        public InterruptedTransactionInfo? BlockingTransaction
        {
            get => _blockingTransaction;
            private set
            {
                if (!SetProperty(ref _blockingTransaction, value))
                    return;
                OnPropertyChanged(nameof(HasBlockingTransaction));
                OnPropertyChanged(nameof(CanStartMutatingInstall));
                (InstallCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public bool HasBlockingTransaction => BlockingTransaction is not null;

        /// <summary>
        /// False when a prior unresolved transaction exists or compatibility rejected the machine.
        /// </summary>
        public bool CanStartMutatingInstall =>
            !HasBlockingTransaction &&
            LastCompatibilityReport?.HasRejection != true;

        public IsoAcquisitionViewModel Iso { get; }
        public TargetSelectionViewModel Target { get; }
        public AccountViewModel Account { get; }
        public RegionalSettingsViewModel Regional { get; }

        public ICommand InstallCommand { get; }

        public bool IsAccountStepVisible => Iso.IsAutoinstallActive;
        public bool IsRegionalStepVisible => Iso.IsAutoinstallActive;

        public bool IsDesktopEnvironmentStepVisible =>
            Iso.ActiveMechanism.SupportsDesktopEnvironmentChoice();

        public ConfirmationViewModel? PendingConfirmation
        {
            get => _pendingConfirmation;
            private set
            {
                if (!SetProperty(ref _pendingConfirmation, value))
                    return;

                OnPropertyChanged(nameof(IsConfirming));
                OnPropertyChanged(nameof(IsIdle));
            }
        }

        public string? InstallStatus
        {
            get => _installStatus;
            private set
            {
                if (!SetProperty(ref _installStatus, value))
                    return;

                OnPropertyChanged(nameof(IsInstalling));
                OnPropertyChanged(nameof(IsIdle));
            }
        }

        public bool IsConfirming => PendingConfirmation is not null;
        public bool IsInstalling => InstallStatus is not null;
        public bool IsIdle => !IsConfirming && !IsInstalling;

        /// <summary>Histórico acumulado dos passos da instalação em andamento, um item por
        /// relato de progresso — nunca sobrescrito, ao contrário de <see cref="InstallStatus"/>
        /// (que continua sendo só o passo atual, para o texto em destaque). Limpo no início de
        /// cada tentativa, para que uma reinstalação depois de um erro não misture o histórico
        /// antigo com o novo.</summary>
        public ObservableCollection<string> InstallLog { get; } = new();

        public event Action<string, string, bool>? Notify;

        public void RaiseStartupWarnings()
        {
            var loc = LocalizationManager.Instance;

            BlockingTransaction = _interruptedTransactionProbe.FindBlockingTransaction(
                Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:");

            if (BlockingTransaction is not null)
            {
                Notify?.Invoke(
                    loc["Wizard_InterruptedTransactionTitle"],
                    loc.Format(
                        "Wizard_InterruptedTransactionMessage",
                        BlockingTransaction.Status,
                        BlockingTransaction.FailureMessage ?? BlockingTransaction.FailureCode ?? string.Empty),
                    true);
            }

            if (!Target.IsUefi)
                Notify?.Invoke(loc["Wizard_UefiWarningTitle"], loc["Wizard_UefiWarningMessage"], false);

            switch (CatalogOutcome)
            {
                case CatalogFetchOutcome.NetworkUnavailable
                    or CatalogFetchOutcome.SignatureInvalid
                    or CatalogFetchOutcome.MalformedDocument:
                    Notify?.Invoke(
                        loc["Wizard_CatalogFallbackTitle"],
                        loc["Wizard_CatalogFallbackMessage"],
                        false);
                    break;
            }
        }

        /// <summary>
        /// Applies preflight facts (from the versioned script or a test fixture). Rejection
        /// blocks mutating install; warnings notify but allow advance (task 6.7).
        /// </summary>
        public void ApplyCompatibilityReport(CompatibilityPreflightReport report)
        {
            ArgumentNullException.ThrowIfNull(report);
            LastCompatibilityReport = report;
            OnPropertyChanged(nameof(CanStartMutatingInstall));
            (InstallCommand as RelayCommand)?.RaiseCanExecuteChanged();

            var loc = LocalizationManager.Instance;
            foreach (CompatibilityFinding rejection in report.Rejections)
            {
                Notify?.Invoke(loc["Preflight_RejectTitle"], loc[rejection.MessageResourceKey], true);
            }

            foreach (CompatibilityFinding warning in report.Warnings)
            {
                Notify?.Invoke(loc["Preflight_WarnTitle"], loc[warning.MessageResourceKey], false);
            }
        }

        public void ApplyCompatibilityFacts(CompatibilityFacts facts) =>
            ApplyCompatibilityReport(_compatibilityPreflight.Evaluate(facts));

        private void BeginInstall()
        {
            var loc = LocalizationManager.Instance;

            try
            {
                if (!CanStartMutatingInstall)
                {
                    if (HasBlockingTransaction)
                    {
                        throw new InvalidOperationException(loc["Wizard_InterruptedTransactionBlocked"]);
                    }

                    throw new InvalidOperationException(loc["Preflight_RejectBlocked"]);
                }

                if (Iso.DisplayedDistro is not { } distro)
                    throw new InvalidOperationException(loc["Wizard_NoDistroSelectedMessage"]);

                if (string.IsNullOrWhiteSpace(Iso.ResolvedIsoPath))
                    throw new InvalidOperationException(loc["Wizard_NoIsoSelectedMessage"]);

                if (Iso.IsAutoinstallActive)
                {
                    if (string.IsNullOrWhiteSpace(Account.Username) ||
                        string.IsNullOrWhiteSpace(Account.Password) ||
                        string.IsNullOrWhiteSpace(Account.Hostname))
                    {
                        throw new InvalidOperationException(loc["Wizard_AccountIncompleteMessage"]);
                    }

                    if (Account.Password != Account.ConfirmPassword)
                        throw new InvalidOperationException(loc["Wizard_PasswordMismatchMessage"]);
                }

                if (Target.IsDualBootMode && Target.SelectedPartition is null)
                    throw new InvalidOperationException(loc["Wizard_NoTargetPartitionSelected"]);

                if (Target.IsReplaceMode && Target.SelectedDisk is null)
                    throw new InvalidOperationException(loc["Wizard_NoTargetDiskSelected"]);

                if (Target.IsDualBootMode && Target.PartitionSpaceError is { } spaceError)
                    throw new InvalidOperationException(spaceError);

                EnsureBootSecurityAllowsInstall(loc);
                EnsureCompatibilityAllowsInstall(loc);
                EnsureDiskFitsThePreparation(loc);

                bool isReplace = Target.IsReplaceMode;

                string summary = isReplace
                    ? loc.Format(
                        Target.IsReplacingSystemDisk
                            ? "Wizard_ConfirmReplaceSystemDiskSummary"
                            : "Wizard_ConfirmReplaceSummary",
                        Target.SelectedDisk?.ToString() ?? string.Empty)
                    : loc.Format(
                        "Wizard_ConfirmShrinkSummary",
                        Target.SelectedPartition?.ToString() ?? string.Empty,
                        (int)Target.LinuxPartitionSizeGb);

                var confirmation = new ConfirmationViewModel(
                    summary,
                    requiresTypedConfirmation: isReplace,
                    confirmationWord: loc["Wizard_ConfirmReplaceWord"]);

                confirmation.Confirmed += () => _ = ExecuteInstallAsync(distro);
                confirmation.Cancelled += () => PendingConfirmation = null;

                PendingConfirmation = confirmation;
            }
            catch (Exception ex)
            {
                Notify?.Invoke(loc["Wizard_InstallErrorTitle"], ex.Message, true);
            }
        }

        /// <summary>
        /// Roda o preflight contra o disco alvo e interrompe se ele recusar a máquina.
        ///
        /// Acontece aqui, e não em <see cref="RaiseStartupWarnings"/>, porque o script exige o
        /// número do disco: antes da escolha do alvo não há o que perguntar. E acontece ANTES
        /// da confirmação, porque o que ele protege é o caminho destrutivo — recusar depois de
        /// o usuário confirmar seria tarde.
        ///
        /// Falha ao consultar RECUSA, nunca libera: uma máquina cuja topologia não pôde ser
        /// lida é indistinguível de uma incompatível, e §6.1 manda parar em vez de assumir o
        /// caso provável.
        /// </summary>
        private void EnsureCompatibilityAllowsInstall(LocalizationManager loc)
        {
            int? diskNumber = Target.IsReplaceMode
                ? Target.SelectedDisk?.Index
                : Target.SelectedPartition?.DiskIndex;

            if (diskNumber is not { } disk)
                throw new InvalidOperationException(loc["Wizard_NoTargetDiskSelected"]);

            char systemDrive = Path.GetPathRoot(Environment.SystemDirectory) is { Length: > 0 } root
                ? root[0]
                : 'C';

            CompatibilityFacts facts;
            try
            {
                facts = _compatibilityFacts.Read(disk, systemDrive);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{loc["Preflight_RejectBlocked"]} ({ex.Message})", ex);
            }

            ApplyCompatibilityFacts(facts);

            if (LastCompatibilityReport?.HasRejection == true)
                throw new InvalidOperationException(loc["Preflight_RejectBlocked"]);
        }

        private void EnsureBootSecurityAllowsInstall(LocalizationManager loc)
        {
            if (_bootSecurity.IsSecureBootEnabled())
                throw new InvalidOperationException(loc["Wizard_SecureBootBlockedMessage"]);

            if (Path.GetPathRoot(Iso.ResolvedIsoPath) is not { Length: > 0 } root)
                return;

            char driveLetter = root[0];
            VolumeEncryptionState encryption = _bootSecurity.GetVolumeEncryptionState(driveLetter);
            if (!encryption.QuerySucceeded)
            {
                throw new InvalidOperationException(
                    loc.Format("Wizard_BitLockerQueryFailedMessage", driveLetter));
            }

            if (encryption.BlocksStagingBoot)
            {
                throw new InvalidOperationException(
                    loc.Format("Wizard_BitLockerBlockedMessage", driveLetter));
            }
        }

        private void EnsureDiskFitsThePreparation(LocalizationManager loc)
        {
            long required = PreparationOverheadBytes();

            long diskSize = Target.IsReplaceMode
                ? Target.SelectedDisk?.SizeBytes ?? 0
                : Target.SelectedPartition?.SizeBytes ?? 0;

            long requestedByUser = Target.IsDualBootMode
                ? (long)Target.LinuxPartitionSizeGb * 1024 * 1024 * 1024
                : 0;

            if (diskSize > 0 && required + requestedByUser > diskSize)
            {
                throw new InvalidOperationException(loc.Format(
                    "Wizard_NotEnoughSpaceForPreparationMessage",
                    Math.Round((required + requestedByUser) / (1024d * 1024 * 1024), 1),
                    Math.Round(diskSize / (1024d * 1024 * 1024), 1)));
            }
        }

        private long PreparationOverheadBytes()
        {
            long overhead = Iso.IsAutoinstallActive ? CloudInitSeedWriter.RequiredBytes : 0;
            if (!Target.IsReplaceMode)
                return overhead;

            long isoSize = _isoFileInfo.GetSizeInBytes(Iso.ResolvedIsoPath!);
            return overhead + _stagingPartition.RequiredBytesFor(isoSize);
        }

        /// <summary>Acrescenta uma linha ao log visível. <see cref="Progress{T}"/> volta na
        /// thread que o criou (a de UI, aqui), então mutar <see cref="InstallLog"/> direto é
        /// seguro — mesma garantia de que já dependia <see cref="InstallStatus"/>.</summary>
        private void AppendToInstallLog(string message) =>
            InstallLog.Add($"{DateTime.Now:HH:mm:ss}  {message}");

        private async Task ExecuteInstallAsync(DistroInfo distro)
        {
            var loc = LocalizationManager.Instance;
            var progress = new Progress<string>(step =>
            {
                InstallStatus = step;
                AppendToInstallLog(step);
            });
            bool autoinstallActive = Iso.IsAutoinstallActive;

            PendingConfirmation = null;
            InstallLog.Clear();
            InstallStatus = loc["Wizard_InstallStepPreparing"];
            AppendToInstallLog(InstallStatus);

            string? error = null;
            try
            {
                await Task.Run(() => _flowRunner.Run(BuildFlowRequest(distro), progress));
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppendToInstallLog(error);
            }
            finally
            {
                InstallStatus = null;
            }

            string successMessageKey = autoinstallActive
                ? "Wizard_InstallSuccessMessage"
                : "Wizard_InstallSuccessMessageManual";

            Notify?.Invoke(
                error is null ? loc["Wizard_InstallSuccessTitle"] : loc["Wizard_InstallErrorTitle"],
                error ?? loc[successMessageKey],
                error is not null);
        }

        private InstallationFlowRequest BuildFlowRequest(DistroInfo distro) =>
            new(
                Distro: distro,
                IsoPath: Iso.ResolvedIsoPath!,
                IsUefi: Target.IsUefi,
                IsReplaceMode: Target.IsReplaceMode,
                IsDualBootMode: Target.IsDualBootMode,
                IsAutoinstallActive: Iso.IsAutoinstallActive,
                ActiveMechanism: Iso.ActiveMechanism,
                LiveSession: distro.LiveSession,
                TargetDiskIndex: Target.IsReplaceMode
                    ? Target.SelectedDisk!.Index
                    : Target.SelectedPartition!.DiskIndex,
                DualBootPartitionIndex: Target.IsDualBootMode
                    ? Target.SelectedPartition?.PartitionIndex
                    : null,
                LinuxPartitionSizeGb: (int)Target.LinuxPartitionSizeGb,
                Username: Account.Username,
                Password: Account.Password,
                Hostname: Account.Hostname,
                Locale: Regional.Locale,
                Keymap: Regional.Keymap,
                Timezone: Regional.Timezone,
                DesktopEnvironment: IsDesktopEnvironmentStepVisible
                    ? Regional.DesktopEnvironment
                    : string.Empty);
    }
}
