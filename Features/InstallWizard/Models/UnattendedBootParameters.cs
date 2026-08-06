namespace LinuxHub.Features.InstallWizard.Models
{
    /// <summary>
    /// O que uma instalação desatendida precisa acrescentar à entrada de boot, já resolvido
    /// pelo preparer do mecanismo em uso. Existe para que o gerador do grub.cfg continue
    /// sendo texto puro: ele recebe isto pronto em vez de saber que subiquity usa
    /// <c>autoinstall</c> e Ubiquity usa <c>automatic-ubiquity</c> (design.md, D4).
    ///
    /// <paramref name="KernelParameters"/> vai ANTES do separador <c>---</c> da linha de
    /// kernel: o que vem depois dele é destinado ao sistema instalado, não ao instalador.
    /// Não deve conter espaços dentro de um valor — o casper lê o cmdline com
    /// <c>for x in $(cat /proc/cmdline)</c>, que quebra em espaços.
    ///
    /// <paramref name="ExtraInitrdGrubPath"/> é um arquivo adicional a passar no comando
    /// <c>initrd</c> do GRUB (caminho relativo à raiz do volume localizado por
    /// <c>search</c>, não caminho do Windows). O kernel concatena todos os cpio informados
    /// num initramfs só, que é como o preseed do Ubiquity chega ao <c>/preseed.cfg</c> que o
    /// casper procura. <c>null</c> quando o mecanismo não precisa de nada extra.
    /// </summary>
    public sealed record UnattendedBootParameters(
        bool IsUnattended,
        string KernelParameters,
        string? ExtraInitrdGrubPath)
    {
        /// <summary>Instalação conduzida pelo usuário no instalador nativo: nenhum parâmetro
        /// de automação, nenhum initrd extra.</summary>
        public static UnattendedBootParameters Interactive { get; } =
            new(IsUnattended: false, KernelParameters: string.Empty, ExtraInitrdGrubPath: null);
    }
}
