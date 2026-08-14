namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// One entry in the ordered installation step catalog — the sole authority over
    /// progress (installation-state spec). Presentation labels live elsewhere.
    /// </summary>
    public sealed record InstallationStepDefinition(
        string Id,
        bool Required,
        bool Compensatable,
        bool Armed);

    /// <summary>
    /// Ordered catalog of installation steps. own-linux-installer task 9.1: os quatro passos
    /// live/target reservados pelo change anterior (D13.2) agora Armed=true — é esta mudança
    /// que os liga. Task 9.2 acrescenta <c>target.installation-verified</c> (D12). O mecanismo
    /// em si continua indisponível ao usuário até a fase 11 (validação em VM) passar — não por
    /// <see cref="InstallationStepDefinition.Armed"/>, mas por nenhuma distro declarar
    /// <see cref="LinuxHub.Common.Models.UnattendedInstallMechanism.OwnLiveInstaller"/> no
    /// catálogo ainda (§7.1).
    /// </summary>
    public static class InstallationStepCatalog
    {
        public static IReadOnlyList<InstallationStepDefinition> All { get; } =
        [
            new(Models.InstallationStepIds.WindowsPlanPublished, Required: true, Compensatable: false, Armed: true),
            new(Models.InstallationStepIds.WindowsDiskPrepared, Required: true, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.WindowsStagingPrepared, Required: false, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.WindowsInstallerConfigWritten, Required: false, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.WindowsTemporaryBootPrepared, Required: true, Compensatable: true, Armed: true),
            // Task 9.4 (D5): extração e configuração compensáveis por reformatar a partição
            // alvo (ela pertence só a esta transação); só o bootloader toca algo preexistente
            // (a ESP do Windows) e por isso não é compensável.
            new(Models.InstallationStepIds.LiveIsoMounted, Required: true, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.LiveDistributionExtracted, Required: true, Compensatable: true, Armed: true),
            // Antes de target.system-configured, e não por gosto: a cadeia assinada vem do
            // pool/ da ISO, então este passo precisa do artefato ainda montado — e o volume do
            // Windows, de onde a ISO é lida, é solto logo depois da extração.
            //
            // Compensável: desfazer é remover pacotes de um sistema que ainda não é o de
            // ninguém — a partição alvo já é nossa desde o mkfs. Nada fora dela foi tocado
            // ainda; a ESP só entra no passo target.bootloader-installed.
            new(Models.InstallationStepIds.TargetBootPackagesInstalled, Required: true, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.TargetSystemConfigured, Required: true, Compensatable: true, Armed: true),
            new(Models.InstallationStepIds.TargetBootloaderInstalled, Required: true, Compensatable: false, Armed: true),
            new(Models.InstallationStepIds.TargetInstallationVerified, Required: true, Compensatable: false, Armed: true),
        ];

        public static InstallationStepDefinition Get(string stepId) =>
            All.FirstOrDefault(s => string.Equals(s.Id, stepId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown installation step '{stepId}'.", nameof(stepId));

        public static int IndexOf(string stepId)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Id, stepId, StringComparison.Ordinal))
                    return i;
            }

            throw new ArgumentException($"Unknown installation step '{stepId}'.", nameof(stepId));
        }

        public static string PhaseOf(string stepId)
        {
            int dot = stepId.IndexOf('.');
            if (dot <= 0)
                throw new ArgumentException($"Step '{stepId}' has no phase prefix.", nameof(stepId));
            return stepId[..dot];
        }
    }
}
