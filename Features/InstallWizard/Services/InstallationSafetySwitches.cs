namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Runtime arming gates for destructive capabilities that exist in code but must stay
    /// unreachable until constitution §7.1 validation completes (phase 8 VM gate).
    /// </summary>
    public static class InstallationSafetySwitches
    {
        /// <summary>
        /// Recovery-agent registration and the compensation path. Off until phase 8 proves
        /// rollback on a real VM. Implementing ahead is allowed; reaching these paths is not.
        /// </summary>
        public const bool RecoveryAndCompensationArmed = false;

        /// <summary>
        /// Bypassa <c>VirtualOrIscsiDiskRule</c>'s virtual-disk rejection (task 6.2) para o
        /// disco de sistema de uma VM local (Hyper-V/VMware/VirtualBox). Existe porque a
        /// própria validação da fase 8 exige rodar em VM com snapshot — mas a regra recusa
        /// QUALQUER disco cujo <c>FriendlyName</c> contenha "Virtual", o que inclui o
        /// controlador SCSI sintético de toda VM. Sem este interruptor, a fase 8 nunca consegue
        /// passar do preflight para começar a validar.
        ///
        /// NÃO bypassa a recusa de iSCSI — armazenamento remoto numa máquina real é um caso
        /// diferente (disco de rede, não disco local de teste) e continua recusado sempre.
        ///
        /// FALSE em qualquer build distribuída. Ligar exige editar este arquivo e recompilar —
        /// nunca uma opção de UI, nunca uma variável de ambiente — porque a regra que ele
        /// desliga protege contra um caso não validado em campo (§6.1: "ler, nunca deduzir").
        /// Reverter para <c>false</c> antes de qualquer build que não seja exclusivamente para
        /// testar dentro de uma VM.
        /// </summary>
        public const bool AllowVirtualDiskForVmValidation = false;
    }
}
