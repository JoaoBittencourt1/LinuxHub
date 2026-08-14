namespace LinuxHub.Common.Models
{
    /// <summary>
    /// Como a ISO de uma build monta a própria sessão live, que é o que decide a entrada de
    /// boot capaz de iniciá-la a partir de um arquivo no disco do Windows.
    ///
    /// Não é inferível da família da distro: Manjaro e EndeavourOS são "Arch" no catálogo e
    /// constroem o live de formas próprias. E não é a mesma coisa que
    /// <see cref="UnattendedInstallMechanism"/> — bootar a ISO e automatizar o instalador
    /// dentro dela são problemas separados, resolvidos por peças separadas. Uma distro pode
    /// bootar sem ter automação nenhuma; é, aliás, o estado da maioria do catálogo.
    /// </summary>
    public enum LiveSessionFamily
    {
        /// <summary>Ubuntu e derivadas: kernel e initrd em <c>/casper</c>, ISO localizada pelo
        /// parâmetro <c>iso-scan/filename</c>, laço montado pelo GRUB. É o padrão de toda
        /// entrada do catálogo, e a única família exercitada em boot real até aqui.</summary>
        Casper = 0,

        /// <summary>Arch Linux: kernel e initramfs em <c>/arch/boot/x86_64</c>, e quem monta o
        /// laço é o próprio initramfs, a partir de <c>img_dev</c> e <c>img_loop</c> — o GRUB
        /// só aponta onde a ISO está.</summary>
        Archiso,
    }
}
