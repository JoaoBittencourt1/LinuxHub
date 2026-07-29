using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Costura as quatro peças do autoinstall na ordem certa. Existe como service (e não
    /// como mais um trecho de <c>InstallWizardViewModel.RunInstall</c>) porque a ordem tem
    /// uma dependência não-óbvia: a partição de semente precisa existir ANTES da leitura do
    /// layout, senão o storage config não a declara e o curtin a trata como espaço livre —
    /// apagando a própria configuração que está lendo.
    /// </summary>
    public sealed class AutoinstallPreparationService : IAutoinstallPreparationService
    {
        private readonly ICloudInitSeedWriter _seedWriter;
        private readonly IDiskLayoutProvider _diskLayoutProvider;

        public AutoinstallPreparationService(
            ICloudInitSeedWriter seedWriter,
            IDiskLayoutProvider diskLayoutProvider)
        {
            _seedWriter = seedWriter ?? throw new ArgumentNullException(nameof(seedWriter));
            _diskLayoutProvider = diskLayoutProvider ?? throw new ArgumentNullException(nameof(diskLayoutProvider));
        }

        public int Prepare(InstallerConfig config, int diskIndex, StagingPartition staging)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(staging);

            int seedPartitionNumber = _seedWriter.CreateSeedPartition(diskIndex);

            DiskLayout layout = _diskLayoutProvider.GetLayout(diskIndex);

            // O salt é sorteado a cada instalação: duas máquinas com a mesma senha não
            // devem produzir o mesmo hash no /etc/shadow.
            string passwordHash = Sha512Crypt.Hash(config.Password, Sha512Crypt.GenerateSalt());

            string userData = AutoinstallBuilder.BuildUserData(
                config, layout, passwordHash, seedPartitionNumber, staging);
            string metaData = AutoinstallBuilder.BuildMetaData($"linuxhub-{Guid.NewGuid():N}");

            _seedWriter.WriteSeedFiles(diskIndex, seedPartitionNumber, userData, metaData);

            return seedPartitionNumber;
        }
    }
}
