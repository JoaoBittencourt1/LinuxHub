using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Instalação desatendida via subiquity: <c>user-data</c> de cloud-init numa partição
    /// semente NoCloud, particionamento em formato curtin.
    ///
    /// Costura as quatro peças na ordem certa. Existe como service (e não como mais um trecho
    /// de <c>InstallWizardViewModel.RunInstall</c>) porque a ordem tem uma dependência
    /// não-óbvia: a partição de semente precisa existir ANTES da leitura do layout, senão o
    /// storage config não a declara e o curtin a trata como espaço livre — apagando a própria
    /// configuração que está lendo.
    /// </summary>
    public sealed class SubiquityInstallPreparer : IUnattendedInstallPreparer
    {
        /// <summary>
        /// O parâmetro que torna a instalação de fato desatendida: com o <c>user-data</c>
        /// presente na partição semente mas SEM ele, o subiquity acha o arquivo e ainda assim
        /// para numa tela pedindo confirmação — comportamento padrão de segurança dele, não
        /// um bug.
        /// </summary>
        internal const string AutoinstallParameter = "autoinstall";

        private readonly ICloudInitSeedWriter _seedWriter;
        private readonly IDiskLayoutProvider _diskLayoutProvider;

        public SubiquityInstallPreparer(
            ICloudInitSeedWriter seedWriter,
            IDiskLayoutProvider diskLayoutProvider)
        {
            _seedWriter = seedWriter ?? throw new ArgumentNullException(nameof(seedWriter));
            _diskLayoutProvider = diskLayoutProvider ?? throw new ArgumentNullException(nameof(diskLayoutProvider));
        }

        public UnattendedInstallMechanism Mechanism => UnattendedInstallMechanism.Subiquity;

        public UnattendedPreparationResult Prepare(
            InstallerConfig config, int diskIndex, StagingPartition? staging)
        {
            ArgumentNullException.ThrowIfNull(config);

            int seedPartitionNumber = _seedWriter.CreateSeedPartition(diskIndex);

            DiskLayout layout = _diskLayoutProvider.GetLayout(diskIndex);

            // O salt é sorteado a cada instalação: duas máquinas com a mesma senha não
            // devem produzir o mesmo hash no /etc/shadow.
            string passwordHash = Sha512Crypt.Hash(config.Password, Sha512Crypt.GenerateSalt());

            string userData = AutoinstallBuilder.BuildUserData(
                config, layout, passwordHash, seedPartitionNumber, staging);
            string metaData = AutoinstallBuilder.BuildMetaData($"linuxhub-{Guid.NewGuid():N}");

            _seedWriter.WriteSeedFiles(diskIndex, seedPartitionNumber, userData, metaData);

            return new UnattendedPreparationResult(
                seedPartitionNumber,
                new UnattendedBootParameters(
                    IsUnattended: true,
                    KernelParameters: AutoinstallParameter,
                    ExtraInitrdGrubPath: null));
        }
    }
}
