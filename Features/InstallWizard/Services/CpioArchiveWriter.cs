using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Escreve um arquivo cpio no formato SVR4 "newc" — o único que o kernel aceita para
    /// formar um initramfs. Lógica pura de bytes, sem I/O.
    ///
    /// Existe porque o preseed do Ubiquity viaja como um initrd adicional (design.md, D1): o
    /// GRUB entrega o cpio junto do initrd da ISO e o kernel concatena os dois num initramfs
    /// só, fazendo o <c>/preseed.cfg</c> aparecer na raiz — que é onde o
    /// <c>casper-bottom/24preseed</c> o procura. É o único transporte que não exige escrever
    /// dentro da ISO (read-only) nem depender de rede.
    ///
    /// O formato é simples o bastante para não justificar dependência externa: cabeçalho ASCII
    /// de 110 bytes (magic + 13 campos hexadecimais de 8 dígitos), nome terminado em NUL,
    /// dados, e um registro final <c>TRAILER!!!</c> — com nome e dados alinhados em 4 bytes.
    /// </summary>
    public static class CpioArchiveWriter
    {
        private const string Magic = "070701";
        private const string TrailerName = "TRAILER!!!";

        /// <summary>Arquivo regular (<c>S_IFREG</c>) com permissão 0644.</summary>
        private const int RegularFileMode = 0x8000 | 0x1A4;

        /// <summary>
        /// Empacota os arquivos na ordem dada. Os nomes são caminhos relativos à raiz do
        /// initramfs, sem barra inicial (<c>preseed.cfg</c>, não <c>/preseed.cfg</c>) — é
        /// assim que o kernel os desempacota em <c>/</c>.
        /// </summary>
        public static byte[] Build(IReadOnlyList<(string Name, string Content)> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);

            using var stream = new MemoryStream();

            int inode = 1;
            foreach (var (name, content) in entries)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                WriteEntry(stream, name, Encoding.UTF8.GetBytes(content), inode++, RegularFileMode);
            }

            // O trailer não é opcional: sem ele o kernel continua lendo o que vier depois no
            // arquivo concatenado como se fosse mais um cabeçalho.
            WriteEntry(stream, TrailerName, [], inode: 0, mode: 0);

            return stream.ToArray();
        }

        private static void WriteEntry(Stream stream, string name, byte[] data, int inode, int mode)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name + "\0");

            var header = new StringBuilder(110);
            header.Append(Magic);
            header.Append(Hex(inode));          // ino
            header.Append(Hex(mode));           // mode
            header.Append(Hex(0));              // uid  — root
            header.Append(Hex(0));              // gid  — root
            header.Append(Hex(1));              // nlink
            header.Append(Hex(0));              // mtime — 0 mantém a saída reprodutível
            header.Append(Hex(data.Length));    // filesize
            header.Append(Hex(0));              // devmajor
            header.Append(Hex(0));              // devminor
            header.Append(Hex(0));              // rdevmajor
            header.Append(Hex(0));              // rdevminor
            header.Append(Hex(nameBytes.Length)); // namesize — inclui o NUL
            header.Append(Hex(0));              // check — ignorado no newc

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());

            stream.Write(headerBytes);
            stream.Write(nameBytes);
            PadTo4(stream, headerBytes.Length + nameBytes.Length);

            stream.Write(data);
            PadTo4(stream, data.Length);
        }

        private static string Hex(int value) => value.ToString("X8");

        /// <summary>
        /// O alinhamento é contado a partir do início do arquivo, e como cada campo já começa
        /// alinhado, basta completar o tamanho do bloco recém-escrito.
        /// </summary>
        private static void PadTo4(Stream stream, int bytesWritten)
        {
            int padding = (4 - (bytesWritten % 4)) % 4;
            for (int i = 0; i < padding; i++)
                stream.WriteByte(0);
        }
    }
}
