using System.IO;
using LinuxHub.Common.Diagnostics;
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
        string DesktopEnvironment,
        string? OwnLiveMediaWindowsPath = null);

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
        private readonly ILinuxRootPartitionService _linuxRootPartition;
        private readonly ILiveMediaStagingService _liveMediaStaging;

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
            IBootStagingService bootStaging,
            ILinuxRootPartitionService linuxRootPartition,
            ILiveMediaStagingService liveMediaStaging)
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
            // Explícito de propósito, sem default concreto: este service ELEVA (cria partição
            // no disco). Um default silencioso faria qualquer chamador que esquecesse de
            // injetá-lo disparar elevação real — inclusive testes, que foi como isto apareceu.
            _linuxRootPartition = linuxRootPartition ?? throw new ArgumentNullException(nameof(linuxRootPartition));
            _liveMediaStaging = liveMediaStaging ?? throw new ArgumentNullException(nameof(liveMediaStaging));
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
            // No caminho próprio são duas partições novas (mídia live + raiz) e nenhuma
            // semente; nos demais, a raiz é criada pelo instalador da distro e a semente só
            // existe quando há autoinstall.
            int newPartitions = request.ActiveMechanism == UnattendedInstallMechanism.OwnLiveInstaller
                ? 2 + (request.IsReplaceMode ? 1 : 0)
                : 1 + (request.IsReplaceMode ? 1 : 0) + (request.IsAutoinstallActive ? 1 : 0);

            // Encolher e criar as partições são UM passo do registro, não dois: encolher sem
            // criar deixa o disco num estado intermediário que só este fluxo sabe interpretar.
            // Manter a criação fora do RunStep fazia uma falha aqui não ser registrada como
            // falha — o estado ficava eternamente em `running`, o probe de transação
            // interrompida bloqueava toda instalação seguinte, e o encolhimento já aplicado
            // (que subtrai do tamanho ATUAL a cada execução) seria repetido na próxima
            // tentativa, comendo o volume do Windows a cada ciclo. Bug real, encontrado ao
            // falhar a cópia da mídia live.
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

                // own-linux-installer: no caminho próprio o app prepara as duas partições que o
                // boot e a instalação precisam, nesta ordem — a da mídia live primeiro, com
                // tamanho fixo, e a raiz depois, ocupando o que sobrar.
                //
                // Nos demais mecanismos isto não roda: quem cria a raiz é o instalador nativo
                // da distro, a partir do espaço livre que o encolhimento acabou de abrir.
                if (request.ActiveMechanism != UnattendedInstallMechanism.OwnLiveInstaller)
                    return;

                // Os arquivos da mídia live vão para uma partição FAT32 e o GRUB carrega o
                // kernel direto dela. Deixar a ISO como arquivo e mandar o GRUB montá-la em
                // laço obrigava o live-boot a montar NTFS por FUSE dentro do initramfs, antes
                // de existir sistema — uma cadeia inteira de pontos de falha entre o firmware e
                // qualquer diagnóstico possível.
                if (string.IsNullOrWhiteSpace(request.OwnLiveMediaWindowsPath))
                {
                    throw new InvalidOperationException(
                        "O caminho da mídia live própria não foi informado — o preparer do " +
                        "mecanismo novo precisa entregá-lo.");
                }

                long liveMediaSize = _isoFileInfo.GetSizeInBytes(request.OwnLiveMediaWindowsPath);
                LiveMediaStagingPartition liveMedia =
                    _liveMediaStaging.Create(request.TargetDiskIndex, liveMediaSize);
                _liveMediaStaging.CopyLiveFiles(liveMedia, request.OwnLiveMediaWindowsPath);

                // A raiz vem depois, com o espaço restante. É a identidade dela que o
                // instalador live lê para saber onde escrever; sem ela ele pararia antes de
                // qualquer escrita, em vez de escolher um alvo sozinho.
                LinuxRootPartition root = _linuxRootPartition.Create(request.TargetDiskIndex);
                _planPublisher.UpdateStagingIdentity(
                    root.PartitionNumber, root.OffsetBytes, root.PartitionUuid, root.SizeBytes);
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
                    IsoHostPartitionUuid: staging?.PartitionUuid,
                    Mode: request.IsReplaceMode ? InstallMode.Replace : InstallMode.DualBoot,
                    Mechanism: request.ActiveMechanism,
                    OwnLiveMediaWindowsPath: request.OwnLiveMediaWindowsPath));
            });

            // A senha em claro só existe para atravessar o lado Windows: quem a consome é o
            // InstallerConfigFromPlan, e o que chega ao Linux é o hash já dentro do preseed /
            // autoinstall. Passado esse ponto ela é resíduo — e mora sob ProgramData, que é
            // legível por qualquer usuário da máquina. Some assim que deixa de ser necessária.
            //
            // Exceto para o mecanismo próprio (own-linux-installer, D1/task 7.1): ali o
            // segredo só é consumido DEPOIS do reboot, pelo instalador live que lê
            // InstallationTransactionPaths — apagar aqui apagaria antes de alguém ler.
            bool ownsLiveMediaTransaction = request.ActiveMechanism == UnattendedInstallMechanism.OwnLiveInstaller;
            if (!ownsLiveMediaTransaction)
            {
                DeleteAccountSecret(plan);
            }

            // own-linux-installer task 9.2/D12: com os passos live/target armados, a fase
            // Windows terminar não é mais a instalação terminar para este mecanismo — falta
            // tudo que só roda depois do reboot, até target.installation-verified. Marcar
            // sucesso aqui seria "rodamos os comandos", exatamente o que D12 existe para
            // impedir. Quem marca sucesso é o instalador live (run-installer.sh), espelhando
            // para o mesmo registro. Os dois caminhos preservados continuam terminando aqui,
            // como sempre.
            if (!ownsLiveMediaTransaction)
            {
                ledger.MarkSucceeded();
            }
        }

        /// <summary>
        /// Apaga o arquivo de senha em claro. Best-effort: não conseguir apagar não invalida
        /// uma instalação que já deu certo, mas fica no log — um resíduo que ninguém percebe
        /// é pior do que um que aparece no diagnóstico.
        /// </summary>
        private static void DeleteAccountSecret(InstallationPlan plan)
        {
            string path = plan.Account.PasswordWindowsPath;
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.Write(
                    "limpeza do segredo de conta",
                    $"Não foi possível apagar {path}: {ex.Message}");
            }
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
                DesktopEnvironment: request.DesktopEnvironment,
                // own-linux-installer: o mecanismo entra no plano porque o validador precisa
                // dele para decidir se disk.installer pode carregar identidade, e o instalador
                // live o usa para confirmar que o plano encontrado é dele.
                Mechanism: request.IsAutoinstallActive
                    ? request.ActiveMechanism
                    : UnattendedInstallMechanism.None));
        }

        private long PreparationOverheadBytes(InstallationFlowRequest request, long isoSize)
        {
            // O instalador próprio não usa partição semente (não há configuração de terceiro
            // para entregar — o plano publicado é o transporte, D1), mas precisa de espaço para
            // a partição FAT32 da mídia live. Sem reservar isto no encolhimento, ela não caberia
            // e a raiz ficaria menor do que o usuário pediu.
            if (request.ActiveMechanism == UnattendedInstallMechanism.OwnLiveInstaller)
            {
                long liveMediaSize = string.IsNullOrWhiteSpace(request.OwnLiveMediaWindowsPath)
                    ? 0
                    : _liveMediaStaging.RequiredBytesFor(
                        _isoFileInfo.GetSizeInBytes(request.OwnLiveMediaWindowsPath));

                return request.IsReplaceMode
                    ? liveMediaSize + _stagingPartition.RequiredBytesFor(isoSize)
                    : liveMediaSize;
            }

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

            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Sem isto o estado durável fica parado em `running` com este passo ativo: o
                // ledger é gravado em disco e sobrevive ao processo, então o
                // InterruptedTransactionProbe passa a encontrar uma transação não resolvida em
                // TODA abertura seguinte e bloqueia qualquer nova instalação — para sempre,
                // porque nada apaga esse estado. Uma falha de passo tem que ser registrada como
                // falha, não deixada em aberto.
                // `component` só aceita uma fase (windows/live/target/rollback), não o id do
                // passo — e o id já carrega a fase no prefixo ("windows.disk-prepared").
                // Passar o id inteiro faria o próprio tratador lançar e mascarar a exceção
                // original.
                ledger.Fail(
                    code: "STEP_FAILED",
                    message: $"{stepId}: {ex.Message}",
                    component: PhaseOf(stepId));
                throw;
            }

            ledger.CompleteStep(stepId);
        }

        /// <summary>
        /// Fase a partir do prefixo do id do passo. Um id sem prefixo conhecido cai em
        /// <see cref="InstallationPhase.Windows"/>: registrar a falha na fase errada é ruim,
        /// mas não registrar nenhuma deixaria a transação presa em `running`, que é pior.
        /// </summary>
        private static string PhaseOf(string stepId)
        {
            int separator = stepId.IndexOf('.');
            if (separator <= 0)
                return InstallationPhase.Windows;

            string prefix = stepId[..separator];
            return prefix switch
            {
                InstallationPhase.Windows => InstallationPhase.Windows,
                InstallationPhase.Live => InstallationPhase.Live,
                InstallationPhase.Target => InstallationPhase.Target,
                InstallationPhase.Rollback => InstallationPhase.Rollback,
                _ => InstallationPhase.Windows,
            };
        }
    }
}
