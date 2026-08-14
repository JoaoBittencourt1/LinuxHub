namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Grava o initrd adicional (o cpio com o preseed) num lugar que o GRUB consiga ler no
    /// boot, e devolve o caminho no formato que o comando <c>initrd</c> espera.
    ///
    /// O destino não é livre: a entrada de boot resolve <c>$root</c> com
    /// <c>search --file $isofile</c>, então o cpio precisa estar no MESMO volume da ISO — os
    /// caminhos do comando <c>initrd</c> são relativos a esse <c>$root</c>. Por isso o
    /// destino muda com o modo de instalação: junto da ISO no volume do Windows (dual-boot) ou
    /// na raiz da partição de staging (substituir).
    /// </summary>
    public interface IUnattendedInitrdWriter
    {
        /// <summary>
        /// Devolve o caminho GRUB do arquivo gravado (ex.: <c>/linuxhub-preseed.cpio</c>).
        /// <paramref name="staging"/> é <c>null</c> no dual-boot, quando a ISO permanece no
        /// volume do Windows indicado por <paramref name="isoWindowsPath"/>.
        /// </summary>
        string Write(byte[] archive, string isoWindowsPath, StagingPartition? staging);
    }
}
