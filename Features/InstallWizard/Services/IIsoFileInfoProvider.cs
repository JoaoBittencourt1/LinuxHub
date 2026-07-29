using System.IO;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Tamanho da ISO em disco. Existe como interface — em vez de <c>new FileInfo(...)</c>
    /// espalhado — porque dele saem duas decisões que precisam ser testáveis sem uma ISO de
    /// 6 GB no repositório: quanto espaço a partição de staging precisa, e se a cópia saiu
    /// íntegra.
    /// </summary>
    public interface IIsoFileInfoProvider
    {
        long GetSizeInBytes(string isoPath);
    }

    public sealed class IsoFileInfoProvider : IIsoFileInfoProvider
    {
        public long GetSizeInBytes(string isoPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);

            var file = new FileInfo(isoPath);
            if (!file.Exists)
            {
                throw new FileNotFoundException(
                    $"A ISO selecionada não foi encontrada em {isoPath}.", isoPath);
            }

            return file.Length;
        }
    }
}
