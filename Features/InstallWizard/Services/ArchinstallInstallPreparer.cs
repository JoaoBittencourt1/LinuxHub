using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Instalação desatendida via <c>archinstall</c>: um script gravado ao lado da ISO, que a
    /// sessão live executa sozinha e que monta lá o JSON de configuração final.
    ///
    /// Ao contrário dos outros dois preparers, este NÃO cria partição nenhuma do lado do
    /// Windows — quem particiona é o <c>archinstall</c>, a partir do espaço livre que o wizard
    /// já abriu. Daí não haver partição semente a declarar.
    /// </summary>
    public sealed class ArchinstallInstallPreparer : IUnattendedInstallPreparer
    {
        /// <summary>
        /// Sem isto o archiso copia o <c>airootfs.sfs</c> para a RAM e DESMONTA
        /// <c>/run/archiso/img_dev</c> ainda no initramfs — o script sumiria antes do login e o
        /// <c>.automated_script.sh</c> falharia calado, porque ele só executa se o <c>cp</c>
        /// devolver 0. O default é <c>auto</c>, que vira <c>y</c> em qualquer máquina com RAM
        /// sobrando, então este parâmetro é a diferença entre automatizar e não fazer nada.
        /// </summary>
        internal const string KeepHostPartitionMountedParameter = "copytoram=n";

        private readonly IDiskLayoutProvider _diskLayoutProvider;
        private readonly IArchinstallScriptWriter _scriptWriter;

        public ArchinstallInstallPreparer(
            IDiskLayoutProvider diskLayoutProvider,
            IArchinstallScriptWriter scriptWriter)
        {
            _diskLayoutProvider = diskLayoutProvider ?? throw new ArgumentNullException(nameof(diskLayoutProvider));
            _scriptWriter = scriptWriter ?? throw new ArgumentNullException(nameof(scriptWriter));
        }

        public UnattendedInstallMechanism Mechanism => UnattendedInstallMechanism.Archinstall;

        public UnattendedPreparationResult Prepare(
            InstallerConfig config, int diskIndex, StagingPartition? staging)
        {
            ArgumentNullException.ThrowIfNull(config);

            EnsureSupportedScenario(config, staging);

            DiskLayout layout = _diskLayoutProvider.GetLayout(diskIndex);

            PartitionLayout esp = layout.EfiSystemPartition
                ?? throw new InvalidOperationException(
                    "O disco alvo não tem EFI System Partition. A instalação automática do Arch " +
                    "reaproveita a ESP existente e não cria uma nova.");

            if (string.IsNullOrWhiteSpace(esp.Guid))
            {
                // Sem PARTUUID não há como nomear o alvo, e adivinhar o caminho é exatamente o
                // que não pode acontecer (§6.1). Recusar aqui deixa a máquina intacta.
                throw new InvalidOperationException(
                    "A partição EFI do disco alvo não tem um identificador estável (PARTUUID), " +
                    "necessário para localizá-la com segurança durante a instalação.");
            }

            // O salt é sorteado a cada instalação: duas máquinas com a mesma senha não devem
            // produzir o mesmo hash no /etc/shadow.
            string passwordHash = Sha512Crypt.Hash(config.Password, Sha512Crypt.GenerateSalt());

            string json = ArchinstallConfigBuilder.Build(config, layout, passwordHash);
            string script = ArchinstallScriptBuilder.Build(json, esp.Guid);

            string scriptPathOnHostVolume = _scriptWriter.Write(script, config.IsoPath);

            string kernelParameters =
                $"script={ArchisoIsoBootEntryBuilder.HostPartitionMountPoint}{scriptPathOnHostVolume} " +
                KeepHostPartitionMountedParameter;

            return new UnattendedPreparationResult(
                // Este mecanismo não cria partição semente: quem monta a configuração é o
                // script, dentro da sessão live. Zero não é um número de partição válido, e é
                // assim que "nenhuma" fica dito.
                SeedPartitionNumber: 0,
                BootParameters: new UnattendedBootParameters(
                    IsUnattended: true,
                    KernelParameters: kernelParameters,
                    ExtraInitrdGrubPath: null));
        }

        /// <summary>
        /// Os dois cenários que este mecanismo ainda não sabe fazer com segurança. Recusar é
        /// deliberado: automação incompleta é preferível a automação insegura (§6.1), e o
        /// usuário continua com o instalador nativo do Arch nesses casos.
        /// </summary>
        private static void EnsureSupportedScenario(InstallerConfig config, StagingPartition? staging)
        {
            if (staging is not null)
            {
                // No substituir, a ISO mora na staging — e `copytoram=n` mantém essa partição
                // presa durante toda a instalação, no mesmo disco que o archinstall vai
                // reparticionar. Pior: o `umount_all_existing` dele desmonta todas as partições
                // do disco alvo antes de particionar, o que inclui a que sustenta a raiz da
                // sessão live. Enquanto isso não for exercitado em VM, o modo não é oferecido.
                throw new InvalidOperationException(
                    "A instalação automática do Arch ainda não cobre o modo substituir — nesse " +
                    "modo a ISO fica numa partição do próprio disco que seria reparticionado. " +
                    "Use o modo dual-boot, ou desligue a instalação automática para instalar " +
                    "pelo instalador do Arch.");
            }

            if (!string.Equals(config.BootMode, "uefi", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "A instalação automática do Arch cobre apenas máquinas UEFI — ela reaproveita " +
                    "a partição EFI existente, que não existe num boot legado (BIOS).");
            }
        }
    }
}
