namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// own-linux-installer task 10.2: resolve o caminho, no volume do Windows, da mídia live
    /// própria (design.md D0) que <see cref="BootStagingService"/> vai bootar para o mecanismo
    /// <see cref="Common.Models.UnattendedInstallMechanism.OwnLiveInstaller"/>.
    ///
    /// Não é um binário pequeno como os de <see cref="IGrubAssetProvider"/> (dezenas de KB,
    /// committados em <c>Assets/Grub/</c>): a mídia live é uma ISO de centenas de MB, gerada
    /// por <c>live-media/build/build-live-media.sh</c> fora do app. Ela NÃO é committada no
    /// repositório — segue o mesmo princípio que já vale para as ISOs de distro (baixadas,
    /// nunca embarcadas). A task 1.9 (integração ao catálogo assinado, hash verificável) ainda
    /// não está feita; esta interface só resolve ONDE ela deveria estar.
    /// </summary>
    public interface ILiveMediaProvider
    {
        /// <summary>Caminho absoluto da ISO da mídia live no volume do Windows. Lança se o
        /// arquivo não existir — nunca assume presença (§6.1).</summary>
        string GetIsoPath();
    }
}
