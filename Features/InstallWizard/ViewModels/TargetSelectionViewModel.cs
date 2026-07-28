using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;
using LinuxHub.Common.Mvvm;
using LinuxHub.Features.InstallWizard.Services;

namespace LinuxHub.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Modo de instalação (substituir/dual-boot) e seleção de disco ou partição alvo.
    /// Ver specs/install-wizard/spec.md — "Selecionar o alvo da instalação".
    /// </summary>
    public class TargetSelectionViewModel : ObservableObject
    {
        private const double DefaultSliderMaximum = 500;
        private const double DefaultSliderMinimum = 20;
        private const double BytesPerGb = 1024d * 1024 * 1024;

        /// <summary>
        /// Folga que fica livre no Windows depois do shrink. O slider oferecia até o tamanho
        /// TOTAL da partição, então dava pra pedir 100 GB de uma partição com 36 GB livres —
        /// e o Windows recusava ("o tamanho de redução especificado é muito grande", erro
        /// real em teste), já que ele nunca encolhe além do espaço livre e ainda precisa de
        /// espaço pra atualizar, hibernar e paginar. O número autoritativo
        /// (<c>Get-PartitionSupportedSize</c>) exige elevação e por isso só é conferido no
        /// <see cref="Services.DiskPartitioningService"/>, na hora de encolher de fato.
        /// </summary>
        private const double WindowsFreeSpaceReserveGb = 10;

        private readonly IDiskInventoryService _diskInventory;
        private readonly IPartitionInventoryService _partitionInventory;

        private InstallMode _mode = InstallMode.Replace;
        private DiskInfo? _selectedDisk;
        private PartitionInfo? _selectedPartition;
        private double _linuxPartitionSizeGb = 100;
        private double _sliderMaximum = DefaultSliderMaximum;
        private string? _diskDetectionError;
        private string? _partitionSpaceError;

        public TargetSelectionViewModel(
            IDiskInventoryService diskInventory,
            IPartitionInventoryService partitionInventory,
            IFirmwareService firmwareService)
        {
            _diskInventory = diskInventory ?? throw new ArgumentNullException(nameof(diskInventory));
            _partitionInventory = partitionInventory ?? throw new ArgumentNullException(nameof(partitionInventory));

            if (firmwareService is null)
                throw new ArgumentNullException(nameof(firmwareService));

            IsUefi = firmwareService.IsUefi();

            ReloadDisks();
        }

        public bool IsUefi { get; }

        public InstallMode Mode
        {
            get => _mode;
            set
            {
                if (!SetProperty(ref _mode, value))
                    return;

                OnPropertyChanged(nameof(IsReplaceMode));
                OnPropertyChanged(nameof(IsDualBootMode));
                OnPropertyChanged(nameof(IsReplacingSystemDisk));

                if (value == InstallMode.Replace)
                    ReloadDisks();
                else
                    ReloadPartitions();
            }
        }

        public bool IsReplaceMode => Mode == InstallMode.Replace;
        public bool IsDualBootMode => Mode == InstallMode.DualBoot;

        public IReadOnlyList<DiskInfo> Disks { get; private set; } = Array.Empty<DiskInfo>();
        public IReadOnlyList<PartitionInfo> Partitions { get; private set; } = Array.Empty<PartitionInfo>();

        /// <summary>Mensagem quando <see cref="IDiskInventoryService.GetDisks"/> falha — nunca
        /// silenciada, conforme constitution §6; a lista de discos fica vazia até o próximo
        /// <see cref="ReloadDisks"/>.</summary>
        public string? DiskDetectionError
        {
            get => _diskDetectionError;
            private set
            {
                if (SetProperty(ref _diskDetectionError, value))
                    OnPropertyChanged(nameof(HasDiskDetectionError));
            }
        }

        public bool HasDiskDetectionError => DiskDetectionError is not null;

        /// <summary>Modo substituir no disco físico que hospeda o Windows em execução —
        /// permitido (o wipe real só acontece depois do reboot, via boot-staging + Linux
        /// live, quando o Windows não está mais rodando — ver design.md D2, revisado).
        /// Só informativo: liga um aviso mais forte na UI e na confirmação destrutiva,
        /// nunca bloqueia a seleção.</summary>
        public bool IsReplacingSystemDisk => IsReplaceMode && SelectedDisk?.IsSystemDisk == true;

        public DiskInfo? SelectedDisk
        {
            get => _selectedDisk;
            set
            {
                if (!SetProperty(ref _selectedDisk, value))
                    return;

                OnPropertyChanged(nameof(IsReplacingSystemDisk));
            }
        }

        public PartitionInfo? SelectedPartition
        {
            get => _selectedPartition;
            set
            {
                if (!SetProperty(ref _selectedPartition, value))
                    return;

                // Sem partição selecionada não há espaço a estimar nem alvo a reprovar —
                // acusar "0 GB livres" aqui seria um erro sobre uma partição que não existe.
                if (value is null)
                {
                    SliderMaximum = DefaultSliderMaximum;
                    PartitionSpaceError = null;
                    return;
                }

                double shrinkableGb = CalculateShrinkableGb(value);

                SliderMaximum = Math.Max(shrinkableGb, DefaultSliderMinimum);
                if (LinuxPartitionSizeGb > SliderMaximum)
                    LinuxPartitionSizeGb = SliderMaximum;

                PartitionSpaceError = shrinkableGb < DefaultSliderMinimum
                    ? LocalizationManager.Instance.Format(
                        "Wizard_PartitionTooFullMessage",
                        Math.Floor((value.FreeSpaceBytes ?? 0) / BytesPerGb),
                        DefaultSliderMinimum + WindowsFreeSpaceReserveGb)
                    : null;
            }
        }

        /// <summary>Mensagem quando a partição escolhida não tem espaço livre suficiente pro
        /// shrink mínimo. Diferente de <see cref="DiskDetectionError"/>, não é falha de
        /// detecção: é um alvo inviável, e o wizard bloqueia a instalação enquanto estiver
        /// setada (ver <c>InstallWizardViewModel.BeginInstall</c>) — sem isso o usuário só
        /// descobria depois de confirmar, no erro cru do Windows.</summary>
        public string? PartitionSpaceError
        {
            get => _partitionSpaceError;
            private set
            {
                if (SetProperty(ref _partitionSpaceError, value))
                    OnPropertyChanged(nameof(HasPartitionSpaceError));
            }
        }

        public bool HasPartitionSpaceError => PartitionSpaceError is not null;

        /// <summary>
        /// Quanto dá pra liberar na partição, em GB: o espaço livre menos
        /// <see cref="WindowsFreeSpaceReserveGb"/>.
        ///
        /// Espaço livre desconhecido devolve <c>0</c> (alvo inviável), e não o tamanho
        /// total da partição como antes. O fallback antigo partia de "sem volume
        /// associado, deixa a validação elevada do shrink decidir" — mas essa validação
        /// não existe para um filesystem que o Windows não reconhece:
        /// <c>Get-PartitionSupportedSize</c> devolve <c>SizeMin</c> = 1 MiB numa ext4
        /// (medido em disco real), então o slider ia até o tamanho inteiro da partição e
        /// o <c>Resize-Partition</c> truncava o filesystem. O filtro de NTFS em
        /// <c>PartitionInventoryService</c> já impede que uma partição assim chegue até
        /// aqui; isto é a segunda barreira.
        /// </summary>
        private static double CalculateShrinkableGb(PartitionInfo partition) =>
            partition.FreeSpaceBytes is { } freeBytes
                ? Math.Floor(freeBytes / BytesPerGb - WindowsFreeSpaceReserveGb)
                : 0;

        public double SliderMinimum => DefaultSliderMinimum;

        public double SliderMaximum
        {
            get => _sliderMaximum;
            private set => SetProperty(ref _sliderMaximum, value);
        }

        public double LinuxPartitionSizeGb
        {
            get => _linuxPartitionSizeGb;
            set => SetProperty(ref _linuxPartitionSizeGb, value);
        }

        private void ReloadDisks()
        {
            try
            {
                Disks = _diskInventory.GetDisks();
                DiskDetectionError = null;
            }
            catch (Exception ex)
            {
                Disks = Array.Empty<DiskInfo>();
                DiskDetectionError = LocalizationManager.Instance.Format("Wizard_DiskDetectionErrorMessage", ex.Message);
            }

            OnPropertyChanged(nameof(Disks));
            SelectedDisk = Disks.Count > 0 ? Disks[0] : null;
        }

        private void ReloadPartitions()
        {
            Partitions = _partitionInventory.GetEligiblePartitions();
            OnPropertyChanged(nameof(Partitions));
            SelectedPartition = Partitions.Count > 0 ? Partitions[0] : null;
        }
    }
}
