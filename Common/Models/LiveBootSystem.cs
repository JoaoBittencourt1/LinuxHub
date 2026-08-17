namespace LinuxHub.Common.Models
{
    /// <summary>
    /// Como a ISO monta o sistema live. É isto, e não a família da distro, que
    /// determina o caminho do kernel e os parâmetros de boot — Linux Mint é
    /// "Debian" na mesma coluna que o Kali, e mesmo assim um usa casper e o outro
    /// usa live-boot, com caminhos totalmente diferentes.
    ///
    /// O valor padrão é <see cref="Unsupported"/> de propósito: uma distro nova
    /// entra no catálogo sem receita de boot, e a falha tem que ser explícita na
    /// hora de gerar o grub.cfg. O bug que originou este tipo foi exatamente o
    /// oposto — o gerador assumia casper para todo mundo, e a instalação do Arch
    /// só descobria isso depois do disco já ter sido reparticionado, na tela do
    /// GRUB procurando um /casper/vmlinuz que nunca existiu naquela ISO.
    /// </summary>
    public enum LiveBootSystem
    {
        /// <summary>Sem receita de boot validada. Gerar um grub.cfg para esta distro
        /// é um erro, não um palpite.</summary>
        Unsupported = 0,

        /// <summary>Ubuntu e derivadas: <c>/casper/vmlinuz</c>, <c>boot=casper</c>.</summary>
        Casper,

        /// <summary>Arch: <c>/arch/boot/x86_64/vmlinuz-linux</c>, com a ISO em loopback
        /// endereçada por <c>img_dev</c>/<c>img_loop</c>.</summary>
        Archiso,
    }
}
