namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Checa as duas proteções do Windows que impedem o boot de staging de funcionar.
    /// Nenhuma delas é contornável pelo app — as duas exigem ação do usuário fora dele —
    /// então o único uso legítimo é recusar a instalação antes de escrever qualquer coisa.
    /// </summary>
    public interface IBootSecurityService
    {
        /// <summary>
        /// Secure Boot ligado: o firmware recusa carregar o <c>grubx64.efi</c> do LinuxHub,
        /// que não é assinado por nenhuma CA que ele reconheça. Falha no firmware, antes de
        /// qualquer código nosso rodar.
        /// </summary>
        bool IsSecureBootEnabled();

        /// <summary>
        /// BitLocker ativo no volume que hospeda a ISO: o GRUB não tem suporte a BitLocker,
        /// então o <c>search --file</c> varre a partição e não enxerga arquivo nenhum.
        /// </summary>
        bool IsVolumeBitLockerProtected(char driveLetter);
    }
}
