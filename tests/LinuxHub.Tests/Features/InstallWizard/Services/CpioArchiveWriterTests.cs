using System;
using System.Collections.Generic;
using System.Text;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Os testes leem o arquivo de volta com um parser escrito aqui, e não comparam bytes
    /// contra um blob fixo: o que precisa estar certo é o que o kernel vai conseguir
    /// desempacotar, não uma sequência específica de bytes. Um golden file passaria mesmo com
    /// um cabeçalho que nenhum leitor de cpio aceita.
    /// </summary>
    public class CpioArchiveWriterTests
    {
        private sealed record CpioEntry(string Name, string Content);

        /// <summary>Parser mínimo do SVR4 "newc", espelhando o que o kernel faz ao montar o
        /// initramfs: cabeçalho de 110 bytes, nome com NUL, dados, tudo alinhado em 4.</summary>
        private static List<CpioEntry> Read(byte[] archive)
        {
            var entries = new List<CpioEntry>();
            int offset = 0;

            while (offset + 110 <= archive.Length)
            {
                string header = Encoding.ASCII.GetString(archive, offset, 110);
                Assert.StartsWith("070701", header);

                int Field(int index) =>
                    Convert.ToInt32(header.Substring(6 + (index * 8), 8), 16);

                int fileSize = Field(6);
                int nameSize = Field(11);

                int nameStart = offset + 110;
                string name = Encoding.ASCII.GetString(archive, nameStart, nameSize - 1);

                int dataStart = Align4(nameStart + nameSize);
                if (name == "TRAILER!!!")
                    break;

                entries.Add(new CpioEntry(
                    name, Encoding.UTF8.GetString(archive, dataStart, fileSize)));

                offset = Align4(dataStart + fileSize);
            }

            return entries;
        }

        private static int Align4(int value) => (value + 3) & ~3;

        [Fact]
        public void Build_RoundTripsNameAndContent()
        {
            byte[] archive = CpioArchiveWriter.Build(
                [("preseed.cfg", "d-i passwd/username string joao\n")]);

            var entries = Read(archive);

            var entry = Assert.Single(entries);
            Assert.Equal("preseed.cfg", entry.Name);
            Assert.Equal("d-i passwd/username string joao\n", entry.Content);
        }

        [Fact]
        public void Build_PreservesOrderOfMultipleFiles()
        {
            byte[] archive = CpioArchiveWriter.Build(
                [("preseed.cfg", "primeiro"), ("partman.recipe", "segundo")]);

            var entries = Read(archive);

            Assert.Equal(["preseed.cfg", "partman.recipe"], entries.ConvertAll(e => e.Name));
            Assert.Equal("segundo", entries[1].Content);
        }

        /// <summary>O kernel lê initramfs concatenados em sequência; um segmento que não termina
        /// num múltiplo de 4 desalinha o cabeçalho do segmento seguinte.</summary>
        [Theory]
        [InlineData("a")]
        [InlineData("ab")]
        [InlineData("abc")]
        [InlineData("abcd")]
        public void Build_AlwaysEndsOnA4ByteBoundary(string content)
        {
            byte[] archive = CpioArchiveWriter.Build([("preseed.cfg", content)]);

            Assert.Equal(0, archive.Length % 4);
        }

        /// <summary>Sem o trailer o kernel segue lendo o que vier depois no arquivo concatenado
        /// como se fosse mais um cabeçalho de entrada.</summary>
        [Fact]
        public void Build_EndsWithTrailerRecord()
        {
            byte[] archive = CpioArchiveWriter.Build([("preseed.cfg", "x")]);

            Assert.Contains("TRAILER!!!", Encoding.ASCII.GetString(archive));
        }

        [Fact]
        public void Build_RejectsEmptyName() =>
            Assert.ThrowsAny<ArgumentException>(
                () => CpioArchiveWriter.Build([(" ", "conteudo")]));
    }
}
