using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Instalação desatendida via Ubiquity (Linux Mint 22.x): preseed debconf empacotado num
    /// cpio que o GRUB carrega como initrd adicional, particionamento por receita
    /// <c>partman</c>.
    ///
    /// A partição semente continua sendo criada, mas por um motivo diferente do caminho
    /// subiquity: aqui ela não transporta nada — o preseed viaja no initrd —, ela existe só
    /// como âncora de identidade do disco. É a partição cujo PARTUUID o
    /// <c>preseed/early_command</c> procura em tempo de boot para descobrir em qual disco
    /// instalar, já que o caminho do disco no Linux não é previsível a partir do Windows.
    /// </summary>
    public sealed class UbiquityInstallPreparer : IUnattendedInstallPreparer
    {
        private readonly ICloudInitSeedWriter _seedWriter;
        private readonly IDiskLayoutProvider _diskLayoutProvider;
        private readonly IUnattendedInitrdWriter _initrdWriter;

        public UbiquityInstallPreparer(
            ICloudInitSeedWriter seedWriter,
            IDiskLayoutProvider diskLayoutProvider,
            IUnattendedInitrdWriter initrdWriter)
        {
            _seedWriter = seedWriter ?? throw new ArgumentNullException(nameof(seedWriter));
            _diskLayoutProvider = diskLayoutProvider ?? throw new ArgumentNullException(nameof(diskLayoutProvider));
            _initrdWriter = initrdWriter ?? throw new ArgumentNullException(nameof(initrdWriter));
        }

        public UnattendedInstallMechanism Mechanism => UnattendedInstallMechanism.UbiquityPreseed;

        public UnattendedPreparationResult Prepare(
            InstallerConfig config, int diskIndex, StagingPartition? staging)
        {
            ArgumentNullException.ThrowIfNull(config);

            int seedPartitionNumber = _seedWriter.CreateSeedPartition(diskIndex);

            // Depois de criar a semente, como no caminho subiquity: o layout precisa já conter
            // a partição para que o PARTUUID dela possa ser lido daqui.
            DiskLayout layout = _diskLayoutProvider.GetLayout(diskIndex);

            // Salt sorteado a cada instalação: duas máquinas com a mesma senha não devem
            // produzir o mesmo hash no /etc/shadow.
            string passwordHash = Sha512Crypt.Hash(config.Password, Sha512Crypt.GenerateSalt());

            string diskResolution =
                UbiquityPreseedBuilder.BuildDiskResolutionCommand(layout, seedPartitionNumber);

            bool isReplaceMode = staging is not null;

            string preseed = UbiquityPreseedBuilder.BuildPreseed(
                config, passwordHash, diskResolution, isReplaceMode);
            string recipe = isReplaceMode
                ? UbiquityPreseedBuilder.BuildReplaceRecipe(
                    isUefi: string.Equals(config.BootMode, "uefi", StringComparison.OrdinalIgnoreCase),
                    config.SwapEnabled,
                    config.SwapSizeGb)
                : UbiquityPreseedBuilder.BuildDualBootRecipe(config.SwapEnabled, config.SwapSizeGb);

            byte[] archive = CpioArchiveWriter.Build(
            [
                (UbiquityPreseedBuilder.PreseedFileName, preseed),
                (UbiquityPreseedBuilder.RecipeFileName, recipe),
            ]);

            string initrdGrubPath = _initrdWriter.Write(archive, config.IsoPath, staging);

            return new UnattendedPreparationResult(
                seedPartitionNumber,
                new UnattendedBootParameters(
                    IsUnattended: true,
                    KernelParameters: UbiquityPreseedBuilder.AutomaticParameter,
                    ExtraInitrdGrubPath: initrdGrubPath));
        }
    }
}
