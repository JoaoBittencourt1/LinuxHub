using System.Windows.Input;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Common.Mvvm;
using LinuxHub.Features.InstallWizard.Services;

namespace LinuxHub.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Orquestra as três etapas do wizard (ISO, alvo, conta), exige confirmação
    /// destrutiva explícita e gera o <c>install.conf</c> ao confirmar. Ver
    /// specs/install-wizard/spec.md.
    /// </summary>
    public class InstallWizardViewModel : ObservableObject
    {
        private readonly InstallerConfigBuilder _configBuilder;
        private readonly IInstallerConfigWriter _configWriter;
        private readonly IDiskPartitioningService _diskPartitioning;
        private readonly IAutoinstallPreparationService _autoinstallPreparation;
        private readonly IBootStagingService _bootStaging;
        private ConfirmationViewModel? _pendingConfirmation;
        private string? _installStatus;

        public InstallWizardViewModel(
            IsoAcquisitionViewModel iso,
            TargetSelectionViewModel target,
            AccountViewModel account,
            InstallerConfigBuilder configBuilder,
            IInstallerConfigWriter configWriter,
            IDiskPartitioningService diskPartitioning,
            IAutoinstallPreparationService autoinstallPreparation,
            IBootStagingService bootStaging)
        {
            Iso = iso ?? throw new ArgumentNullException(nameof(iso));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Account = account ?? throw new ArgumentNullException(nameof(account));
            _configBuilder = configBuilder ?? throw new ArgumentNullException(nameof(configBuilder));
            _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));
            _diskPartitioning = diskPartitioning ?? throw new ArgumentNullException(nameof(diskPartitioning));
            _autoinstallPreparation = autoinstallPreparation ?? throw new ArgumentNullException(nameof(autoinstallPreparation));
            _bootStaging = bootStaging ?? throw new ArgumentNullException(nameof(bootStaging));

            Iso.Notify += (title, message, isError) => Notify?.Invoke(title, message, isError);

            // A conta (usuário/senha/hostname) só existe pro fluxo de autoinstall — quando ele
            // não roda, é o instalador nativo da própria ISO que vai perguntar isso.
            Iso.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsoAcquisitionViewModel.IsAutoinstallActive))
                    OnPropertyChanged(nameof(IsAccountStepVisible));
            };

            InstallCommand = new RelayCommand(BeginInstall);
        }

        public IsoAcquisitionViewModel Iso { get; }
        public TargetSelectionViewModel Target { get; }
        public AccountViewModel Account { get; }

        public ICommand InstallCommand { get; }

        public bool IsAccountStepVisible => Iso.IsAutoinstallActive;

        /// <summary>Não nulo entre o clique em "Instalar" e a confirmação/cancelamento
        /// do usuário — a instalação de fato só ocorre depois de confirmada.</summary>
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

        /// <summary>Etapa em andamento, ou <c>null</c> fora de uma instalação. Encolher a
        /// partição e preparar o boot bloqueiam por dezenas de segundos (processo elevado +
        /// UAC + I/O de disco); sem esse estado a janela ficava congelada e sem resposta, o
        /// que numa operação destrutiva se lê como travamento — daí a tela de progresso.</summary>
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

        /// <summary>Nem confirmando nem instalando: só aí o botão "Instalar" aparece.</summary>
        public bool IsIdle => !IsConfirming && !IsInstalling;

        public event Action<string, string, bool>? Notify;

        /// <summary>
        /// Chamado pela View depois de se inscrever em <see cref="Notify"/> — avisos que
        /// dependem de estado calculado no construtor (ex.: UEFI) não podem ser disparados
        /// do próprio construtor, porque ainda não há ninguém ouvindo o evento nesse ponto.
        /// </summary>
        public void RaiseStartupWarnings()
        {
            if (!Target.IsUefi)
            {
                var loc = LocalizationManager.Instance;
                Notify?.Invoke(loc["Wizard_UefiWarningTitle"], loc["Wizard_UefiWarningMessage"], false);
            }
        }

        private void BeginInstall()
        {
            var loc = LocalizationManager.Instance;

            try
            {
                if (Iso.DisplayedDistro is not { } distro)
                    throw new InvalidOperationException(loc["Wizard_NoDistroSelected"]);

                if (string.IsNullOrWhiteSpace(Iso.ResolvedIsoPath))
                    throw new InvalidOperationException(loc["Wizard_NoIsoSelected"]);

                if (Iso.IsAutoinstallActive)
                {
                    if (string.IsNullOrWhiteSpace(Account.Username)
                        || string.IsNullOrWhiteSpace(Account.Password)
                        || string.IsNullOrWhiteSpace(Account.ConfirmPassword)
                        || string.IsNullOrWhiteSpace(Account.Hostname))
                    {
                        throw new InvalidOperationException(loc["Wizard_AccountIncompleteMessage"]);
                    }

                    if (Account.Password != Account.ConfirmPassword)
                        throw new InvalidOperationException(loc["Wizard_PasswordMismatchMessage"]);
                }

                // Barra aqui, e não no shrink: uma partição sem espaço livre suficiente é um
                // alvo inviável de saída, e deixar passar significava gravar install.conf e
                // preparar o boot antes do Windows recusar o encolhimento.
                if (Target.IsDualBootMode && Target.PartitionSpaceError is { } spaceError)
                    throw new InvalidOperationException(spaceError);

                bool isReplace = Target.IsReplaceMode;

                string summary = isReplace
                    ? loc.Format(
                        Target.IsReplacingSystemDisk ? "Wizard_ConfirmReplaceSystemDiskSummary" : "Wizard_ConfirmReplaceSummary",
                        Target.SelectedDisk?.ToString() ?? string.Empty)
                    : loc.Format(
                        "Wizard_ConfirmShrinkSummary",
                        Target.SelectedPartition?.ToString() ?? string.Empty,
                        (int)Target.LinuxPartitionSizeGb);

                var confirmation = new ConfirmationViewModel(
                    summary,
                    requiresTypedConfirmation: isReplace,
                    confirmationWord: loc["Wizard_ConfirmReplaceWord"]);

                // Fire-and-forget deliberado: ExecuteInstallAsync trata os próprios erros e
                // publica tudo via Notify/InstallStatus — não há nada pra aguardar aqui, e o
                // handler de um evento síncrono não pode ser await-ado sem virar async void.
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
        /// Roda o trabalho pesado fora da thread de UI para que a tela de progresso de fato
        /// desenhe. As etapas são publicadas por <see cref="IProgress{T}"/>, que devolve cada
        /// atualização ao contexto de UI capturado aqui — escrever em
        /// <see cref="InstallStatus"/> direto da thread de trabalho dependeria do marshalling
        /// implícito do binding engine, que não vale pra tudo (comandos, coleções).
        /// </summary>
        private async Task ExecuteInstallAsync(DistroInfo distro)
        {
            var loc = LocalizationManager.Instance;
            var progress = new Progress<string>(step => InstallStatus = step);

            // Capturado aqui, antes do trabalho pesado: é o que decide qual mensagem final
            // aparece, e o toggle não deve mudar no meio de uma instalação já em andamento.
            bool autoinstallActive = Iso.IsAutoinstallActive;

            // Some o cartão de confirmação e abre a tela de progresso no mesmo passo: os dois
            // são excludentes, e deixar o botão "Confirmar" clicável durante a instalação
            // permitiria disparar um segundo shrink por cima do primeiro.
            PendingConfirmation = null;
            InstallStatus = loc["Wizard_InstallStepPreparing"];

            string? error = null;

            try
            {
                await Task.Run(() => RunInstall(distro, progress));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                // Fecha a tela de progresso ANTES de notificar: o aviso é um MessageBox modal
                // que só sai no clique do usuário, e um spinner girando atrás dele passaria a
                // impressão de que ainda há operação de disco em andamento.
                InstallStatus = null;
            }

            // Sem autoinstall, quem termina a instalação de fato é o usuário dentro do
            // instalador nativo da ISO — dizer "não precisa fazer nada" nesse caso seria
            // enganoso, então a mensagem de sucesso muda conforme o modo usado.
            string successMessageKey = autoinstallActive
                ? "Wizard_InstallSuccessMessage"
                : "Wizard_InstallSuccessMessageManual";

            Notify?.Invoke(
                error is null ? loc["Wizard_InstallSuccessTitle"] : loc["Wizard_InstallErrorTitle"],
                error ?? loc[successMessageKey],
                error is not null);
        }

        /// <summary>
        /// As três etapas de disco, na thread de trabalho. Não captura exceção nenhuma: quem
        /// reporta é <see cref="ExecuteInstallAsync"/>, num ponto só — engolir aqui deixaria
        /// a instalação seguir para o boot-staging com o disco em estado desconhecido.
        /// </summary>
        private void RunInstall(DistroInfo distro, IProgress<string> progress)
        {
            var loc = LocalizationManager.Instance;

            if (Target.IsDualBootMode && Target.SelectedPartition is { } partition)
            {
                progress.Report(loc["Wizard_InstallStepShrinking"]);

                _diskPartitioning.ShrinkPartition(
                    partition.DiskIndex,
                    partition.PartitionIndex,
                    (int)Target.LinuxPartitionSizeGb);
            }

            int targetDiskIndex = Target.IsReplaceMode
                ? Target.SelectedDisk!.Index
                : Target.SelectedPartition!.DiskIndex;

            // Distro sem autoinstall validado (ou usuário desligou o toggle): só prepara o
            // boot até o instalador nativo da ISO — sem install.conf, sem semente de
            // cloud-init, o resto da instalação fica por conta do usuário dentro dele.
            if (Iso.IsAutoinstallActive)
            {
                progress.Report(loc["Wizard_InstallStepWritingConfig"]);

                var request = new BuildInstallerConfigRequest(
                    Distro: distro,
                    IsoPath: Iso.ResolvedIsoPath!,
                    IsUefi: Target.IsUefi,
                    Mode: Target.Mode,
                    TargetDiskIndex: Target.IsReplaceMode ? Target.SelectedDisk?.Index : Target.SelectedPartition?.DiskIndex,
                    TargetPartitionIndex: Target.IsDualBootMode ? Target.SelectedPartition?.PartitionIndex : null,
                    LinuxPartitionSizeGb: (int)Target.LinuxPartitionSizeGb,
                    Username: Account.Username,
                    Password: Account.Password,
                    Hostname: Account.Hostname);

                var config = _configBuilder.Build(request);
                _configWriter.Save(config);

                // Precisa vir depois do encolhimento e antes do boot-staging: o autoinstall
                // descreve o disco no estado em que o instalador vai encontrá-lo, e o parâmetro
                // `autoinstall` no GRUB só pode ser ligado depois que a semente existe de fato.
                _autoinstallPreparation.Prepare(config, targetDiskIndex);
            }

            progress.Report(loc["Wizard_InstallStepStagingBoot"]);

            _bootStaging.InstallStagingBootloader(new BootStagingRequest(
                DistroName: distro.Name,
                IsoPath: Iso.ResolvedIsoPath!,
                IsUefi: Target.IsUefi,
                TargetDiskIndex: targetDiskIndex,
                EnableAutoinstall: Iso.IsAutoinstallActive));
        }
    }
}
