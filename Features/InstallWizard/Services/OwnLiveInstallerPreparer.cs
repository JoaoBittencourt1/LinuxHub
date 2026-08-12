using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// own-linux-installer task 10.1 (design.md D0/D1/D13): preparer do mecanismo próprio,
    /// resolvido pelo <see cref="IUnattendedInstallPreparerRegistry"/> existente — sem
    /// <c>if (distro.Id == ...)</c> nenhum (§2).
    ///
    /// Ao contrário dos outros três preparers, este não grava nenhuma configuração adicional:
    /// não há YAML de cloud-init, preseed debconf, nem script archinstall para gerar. O
    /// instalador live lê diretamente o plano publicado (D1) — que
    /// <see cref="InstallationFlowRunner"/> já publica em <see cref="InstallationTransactionPaths"/>
    /// para TODOS os mecanismos, no passo <c>windows.plan-published</c>, antes deste preparer
    /// rodar. Não há partição semente (D13: a live descobre o plano procurando por
    /// dispositivo, não por uma partição que o Windows aponta) nem parâmetro de kernel (a linha
    /// de boot da mídia live não carrega nenhum — <see cref="OwnLiveMediaBootEntryBuilder"/>).
    /// </summary>
    public sealed class OwnLiveInstallerPreparer : IUnattendedInstallPreparer
    {
        public UnattendedInstallMechanism Mechanism => UnattendedInstallMechanism.OwnLiveInstaller;

        public UnattendedPreparationResult Prepare(
            InstallerConfig config, int diskIndex, StagingPartition? staging)
        {
            ArgumentNullException.ThrowIfNull(config);

            // Mesma restrição de BootStagingService.InstallStagingBootloader (D16) — checar
            // aqui também é defesa em profundidade: se algum dia um chamador criar o preparer
            // fora do fluxo normal, ele já recusa cedo, antes de qualquer outro efeito colateral.
            if (!string.Equals(config.BootMode, "uefi", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "O instalador próprio (mídia live) é UEFI apenas (design.md D16). " +
                    "Em firmware BIOS legado, use o dual-boot manual ou o modo substituir.");
            }

            if (staging is not null)
            {
                // D16: este mecanismo serve exclusivamente o dual-boot desatendido. O modo
                // substituir permanece nos caminhos preservados — ver design.md, "Alternativa
                // descartada: levar também o modo substituir para a live própria".
                throw new InvalidOperationException(
                    "O instalador próprio (mídia live) cobre apenas o dual-boot desatendido. " +
                    "O modo substituir continua usando o caminho preservado.");
            }

            return new UnattendedPreparationResult(
                // Sem partição semente: o plano e o segredo já viajam por
                // InstallationTransactionPaths (D13), publicados antes deste preparer rodar.
                SeedPartitionNumber: 0,
                BootParameters: new UnattendedBootParameters(
                    IsUnattended: true,
                    KernelParameters: string.Empty,
                    ExtraInitrdGrubPath: null));
        }
    }
}
