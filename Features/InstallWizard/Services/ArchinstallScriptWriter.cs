using System.IO;
using System.Text;

namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IArchinstallScriptWriter
    {
        /// <summary>Grava o script e devolve o caminho dele DENTRO do volume que hospeda a ISO
        /// (ex.: <c>/ISOs/linuxhub-arch-install.sh</c>) — é esse caminho, e não o do Windows,
        /// que a sessão live enxerga sob
        /// <see cref="ArchisoIsoBootEntryBuilder.HostPartitionMountPoint"/>.</summary>
        string Write(string script, string isoWindowsPath);
    }

    /// <summary>
    /// Grava o script ao lado da ISO. O lugar não é livre: o archiso monta em
    /// <c>/run/archiso/img_dev</c> exatamente o volume de onde a ISO foi carregada, e é só
    /// por ele que um arquivo gravado do lado do Windows fica alcançável pela sessão live.
    /// Qualquer outro destino ficaria invisível lá.
    /// </summary>
    public sealed class ArchinstallScriptWriter : IArchinstallScriptWriter
    {
        public string Write(string script, string isoWindowsPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(script);
            ArgumentException.ThrowIfNullOrWhiteSpace(isoWindowsPath);

            string directory = Path.GetDirectoryName(isoWindowsPath)
                ?? throw new InvalidOperationException(
                    $"Não foi possível determinar a pasta da ISO a partir de '{isoWindowsPath}' " +
                    "para gravar o script de instalação ao lado dela.");

            string destination = Path.Combine(directory, ArchinstallScriptBuilder.FileName);

            // Sem BOM e com LF: o arquivo é executado por bash. Um BOM na primeira linha faz o
            // kernel não reconhecer o shebang, e o erro apareceria só depois do reboot.
            File.WriteAllText(destination, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            return GrubConfigBuilder.ToGrubPath(destination);
        }
    }
}
