using System.IO;
using System.Windows.Input;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Common.Mvvm;
using LinuxHub.Features.InstallWizard.Models;
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
        private readonly IUnattendedInstallPreparerRegistry _unattendedPreparers;
        private readonly IBootStagingService _bootStaging;
        private readonly IBootSecurityService _bootSecurity;
        private readonly IStagingPartitionService _stagingPartition;
        private readonly IIsoFileInfoProvider _isoFileInfo;
        private ConfirmationViewModel? _pendingConfirmation;
        private string? _installStatus;

        public InstallWizardViewModel(
            IsoAcquisitionViewModel iso,
            TargetSelectionViewModel target,
            AccountViewModel account,
            RegionalSettingsViewModel regional,
            InstallerConfigBuilder configBuilder,
            IInstallerConfigWriter configWriter,
            IDiskPartitioningService diskPartitioning,
            IUnattendedInstallPreparerRegistry unattendedPreparers,
            IBootStagingService bootStaging,
            IBootSecurityService bootSecurity,
            IStagingPartitionService stagingPartition,
            IIsoFileInfoProvider isoFileInfo)
        {
            Iso = iso ?? throw new ArgumentNullException(nameof(iso));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Account = account ?? throw new ArgumentNullException(nameof(account));
            Regional = regional ?? throw new ArgumentNullException(nameof(regional));
            _configBuilder = configBuilder ?? throw new ArgumentNullException(nameof(configBuilder));
            _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));
            _diskPartitioning = diskPartitioning ?? throw new ArgumentNullException(nameof(diskPartitioning));
            _unattendedPreparers = unattendedPreparers ?? throw new ArgumentNullException(nameof(unattendedPreparers));
            _bootStaging = bootStaging ?? throw new ArgumentNullException(nameof(bootStaging));
            _bootSecurity = bootSecurity ?? throw new ArgumentNullException(nameof(bootSecurity));
            _stagingPartition = stagingPartition ?? throw new ArgumentNullException(nameof(stagingPartition));
            _isoFileInfo = isoFileInfo ?? throw new ArgumentNullException(nameof(isoFileInfo));

            Iso.Notify += (title, message, isError) => Notify?.Invoke(title, message, isError);

            // A conta (usuário/senha/hostname) e as informações regionais só existem pro fluxo
            // de autoinstall — quando ele não roda, é o instalador nativo da própria ISO que
            // vai perguntar isso, e oferecer aqui uma escolha que seria ignorada prometeria ao
            // usuário um controle que ele não tem.
            Iso.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(IsoAcquisitionViewModel.IsAutoinstallActive))
                    return;

                OnPropertyChanged(nameof(IsAccountStepVisible));
                OnPropertyChanged(nameof(IsRegionalStepVisible));
            };

            InstallCommand = new RelayCommand(BeginInstall);
        }

        public IsoAcquisitionViewModel Iso { get; }
        public TargetSelectionViewModel Target { get; }
        public AccountViewModel Account { get; }
        public RegionalSettingsViewModel Regional { get; }

        public ICommand InstallCommand { get; }

        public bool IsAccountStepVisible => Iso.IsAutoinstallActive;
        public bool IsRegionalStepVisible => Iso.IsAutoinstallActive;

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

                // Sem alvo não há instalação: o dual-boot abre sem partição selecionada quando
                // a máquina não tem nenhuma elegível (ou a detecção falhou), e sem esta guarda
                // o clique em "Instalar" morria num NullReferenceException lá no RunInstall.
                if (Target.IsDualBootMode && Target.SelectedPartition is null)
                    throw new InvalidOperationException(loc["Wizard_NoTargetPartitionSelected"]);

                if (Target.IsReplaceMode && Target.SelectedDisk is null)
                    throw new InvalidOperationException(loc["Wizard_NoTargetDiskSelected"]);

                // Barra aqui, e não no shrink: uma partição sem espaço livre suficiente é um
                // alvo inviável de saída, e deixar passar significava gravar install.conf e
                // preparar o boot antes do Windows recusar o encolhimento.
                if (Target.IsDualBootMode && Target.PartitionSpaceError is { } spaceError)
                    throw new InvalidOperationException(spaceError);

                EnsureBootSecurityAllowsInstall(loc);
                EnsureDiskFitsThePreparation(loc);

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
        /// Secure Boot e BitLocker quebram o boot de staging de formas que o app não tem como
        /// contornar — o firmware recusa o <c>grubx64.efi</c> não assinado, e o GRUB não lê
        /// volume criptografado. Os dois só apareciam DEPOIS do reboot, numa tela preta, com o
        /// disco já encolhido e uma entrada de boot pendurada (erro real numa VM com BitLocker).
        /// Recusar aqui deixa a máquina exatamente como estava.
        ///
        /// A ordem não é acidental: Secure Boot sai do registro sem elevação, então checá-lo
        /// primeiro evita gastar um prompt de UAC numa instalação que já está condenada.
        /// </summary>
        private void EnsureBootSecurityAllowsInstall(LocalizationManager loc)
        {
            if (_bootSecurity.IsSecureBootEnabled())
                throw new InvalidOperationException(loc["Wizard_SecureBootBlockedMessage"]);

            // Dual-boot: o GRUB lê a ISO no volume do Windows — BitLocker nele é bloqueio duro.
            // Substituir: a ISO vai para a staging, mas o preparo ainda ENCOLHE este volume e
            // muda a cadeia de boot (pedido de chave de recuperação). Nos dois casos o volume
            // da ISO é o que importa, e hoje ela mora nele antes do preparo.
            if (Path.GetPathRoot(Iso.ResolvedIsoPath) is not { Length: > 0 } root)
                return;

            char driveLetter = root[0];
            if (_bootSecurity.IsVolumeBitLockerProtected(driveLetter))
                throw new InvalidOperationException(loc.Format("Wizard_BitLockerBlockedMessage", driveLetter));
        }

        /// <summary>
        /// O preparo consome espaço que o usuário não pediu. No modo substituir: a partição de
        /// staging (ISO + folga) e, com autoinstall, a semente. No dual-boot a ISO continua no
        /// volume do Windows — só a semente entra na conta. Descobrir que não cabe DEPOIS de
        /// encolher o Windows deixaria a máquina alterada por nada.
        ///
        /// A conta aqui é sobre o disco todo, não sobre o que o <c>Get-PartitionSupportedSize</c>
        /// permite encolher: esse número exige elevação e sai no script, imediatamente antes da
        /// escrita. Este é o filtro grosseiro, que pega o caso óbvio sem gastar um prompt de UAC.
        /// </summary>
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

        /// <summary>
        /// Staging só no substituir: o dual-boot já preserva a partição que hospeda a ISO, então
        /// copiá-la para uma partição dedicada era custo sem ganho (e o motivo desta mudança
        /// nasceu do <c>layout: direct</c> do substituir, não do dual-boot).
        /// </summary>
        private long PreparationOverheadBytes()
        {
            long overhead = Iso.IsAutoinstallActive ? CloudInitSeedWriter.RequiredBytes : 0;
            if (!Target.IsReplaceMode)
                return overhead;

            long isoSize = _isoFileInfo.GetSizeInBytes(Iso.ResolvedIsoPath!);
            return overhead + _stagingPartition.RequiredBytesFor(isoSize);
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

            int targetDiskIndex = Target.IsReplaceMode
                ? Target.SelectedDisk!.Index
                : Target.SelectedPartition!.DiskIndex;

            // Um shrink só — encadear resizes no mesmo volume dobraria tempo e risco
            // (design.md D4). Staging só entra no overhead do substituir.
            long overheadBytes = PreparationOverheadBytes();

            // Quantas partições o preparo ainda vai criar: a raiz do Linux sempre, a staging só
            // no substituir, a semente só com autoinstall. A tabela precisa comportar todas —
            // em MBR o teto é 4, e estourar no meio deixava o disco encolhido e a mensagem crua.
            int newPartitions = 1
                + (Target.IsReplaceMode ? 1 : 0)
                + (Iso.IsAutoinstallActive ? 1 : 0);

            if (Target.IsDualBootMode && Target.SelectedPartition is { } partition)
            {
                progress.Report(loc["Wizard_InstallStepShrinking"]);

                // O overhead do preparo (semente) é ADICIONAL ao que o usuário pediu no slider.
                _diskPartitioning.ShrinkPartition(
                    partition.DiskIndex,
                    partition.PartitionIndex,
                    (long)Target.LinuxPartitionSizeGb * 1024 * 1024 * 1024 + overheadBytes,
                    newPartitions);
            }
            else
            {
                // No modo substituir o disco cheio de Windows não tem onde acomodar a staging —
                // este é o único ponto que abre esse espaço.
                _diskPartitioning.EnsureUnallocatedSpace(
                    targetDiskIndex, overheadBytes, newPartitions);
            }

            // Staging só no substituir: no dual-boot o curtin já preserva a partição do Windows
            // que hospeda a ISO, então o GRUB continua achando-a com search --file no caminho
            // original. Copiar ~7 GB e abrir partição extra era desnecessário nesse modo.
            StagingPartition? staging = null;
            string isoPathForGrub = Iso.ResolvedIsoPath!;

            if (Target.IsReplaceMode)
            {
                progress.Report(loc["Wizard_InstallStepCopyingIso"]);

                long isoSize = _isoFileInfo.GetSizeInBytes(Iso.ResolvedIsoPath!);
                staging = _stagingPartition.Create(targetDiskIndex, isoSize);
                _stagingPartition.CopyIso(staging, Iso.ResolvedIsoPath!, progress);
                isoPathForGrub = StagingPartitionService.IsoGrubPath;
            }

            // Distro sem mecanismo validado (ou usuário desligou o toggle): só prepara o
            // boot até o instalador nativo da ISO — sem install.conf, sem configuração
            // desatendida, o resto da instalação fica por conta do usuário dentro dele.
            UnattendedBootParameters unattended = UnattendedBootParameters.Interactive;

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
                    Hostname: Account.Hostname,
                    Locale: Regional.Locale,
                    Keymap: Regional.Keymap,
                    Timezone: Regional.Timezone);

                var config = _configBuilder.Build(request);
                _configWriter.Save(config);

                // No substituir precisa vir depois da staging: a configuração desatendida
                // descreve o disco incluindo a partição de staging como preservada, senão o
                // instalador a trata como espaço livre e apaga a ISO que está usando pra rodar.
                unattended = _unattendedPreparers
                    .Resolve(Iso.ActiveMechanism)
                    .Prepare(config, targetDiskIndex, staging)
                    .BootParameters;
            }

            progress.Report(loc["Wizard_InstallStepStagingBoot"]);

            _bootStaging.InstallStagingBootloader(new BootStagingRequest(
                DistroName: distro.Name,
                IsoPath: isoPathForGrub,
                IsUefi: Target.IsUefi,
                TargetDiskIndex: targetDiskIndex,
                Unattended: unattended));
        }
    }
}
