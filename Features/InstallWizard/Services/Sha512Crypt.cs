using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Implementação do <c>crypt(3)</c> SHA-512 (prefixo <c>$6$</c>), o formato que o
    /// <c>/etc/shadow</c> e o campo <c>identity.password</c> do autoinstall do Ubuntu
    /// esperam.
    ///
    /// Existe porque o .NET não expõe <c>crypt(3)</c> e o Windows não tem glibc. A
    /// tentativa anterior (<c>CryptoHelper.GenerateSha512Hash</c>, removido) gerava um
    /// digest SHA-512 hex puro e não um hash crypt(3) — login quebrado. A saída dali foi
    /// mandar a senha em texto puro no <c>install.conf</c> e hashear no lado Linux com
    /// <c>chpasswd</c>; isso não se aplica ao autoinstall, cujo YAML é consumido pelo
    /// subiquity antes de existir qualquer sistema instalado onde rodar <c>chpasswd</c>.
    /// Daí implementar o algoritmo aqui, em vez de trafegar a senha em claro.
    ///
    /// Algoritmo conforme a especificação de Ulrich Drepper (a mesma que a glibc
    /// implementa). A saída é verificada contra <c>openssl passwd -6</c> em
    /// <c>Sha512CryptTests</c> — não confie em revisão visual para este arquivo.
    /// </summary>
    public static class Sha512Crypt
    {
        /// <summary>Alfabeto do base64 do crypt(3) — NÃO é o base64 padrão (RFC 4648),
        /// nem na tabela de caracteres nem na ordem dos bits.</summary>
        private const string Base64Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        private const int DefaultRounds = 5000;
        private const int DigestLength = 64;

        public const int MaxSaltLength = 16;

        public static string Hash(string password, string salt)
        {
            ArgumentNullException.ThrowIfNull(password);
            ArgumentException.ThrowIfNullOrEmpty(salt);

            if (salt.Length > MaxSaltLength)
                salt = salt[..MaxSaltLength];

            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] saltBytes = Encoding.UTF8.GetBytes(salt);

            byte[] digestB = Sha512(passwordBytes, saltBytes, passwordBytes);

            using var digestAInput = new MemoryStream();
            Write(digestAInput, passwordBytes);
            Write(digestAInput, saltBytes);
            Write(digestAInput, Repeat(digestB, passwordBytes.Length));

            // Percorre os bits do comprimento da senha do menos para o mais significativo,
            // alternando entre o digest B e a senha — é isto que impede que senhas de
            // tamanhos diferentes convirjam para a mesma sequência de entrada.
            for (int bits = passwordBytes.Length; bits > 0; bits >>= 1)
                Write(digestAInput, (bits & 1) != 0 ? digestB : passwordBytes);

            byte[] digestA = Sha512(digestAInput.ToArray());

            byte[] passwordSequence = SequenceFromRepeatedDigest(passwordBytes, passwordBytes.Length, passwordBytes.Length);

            // 16 + A[0], onde A[0] é o primeiro byte do digest A — não do B. É o único
            // ponto do algoritmo em que o número de repetições depende de um digest já
            // calculado, e trocar A por B aqui produz um hash bem-formado e errado.
            byte[] saltSequence = SequenceFromRepeatedDigest(saltBytes, 16 + digestA[0], saltBytes.Length);

            byte[] result = digestA;
            for (int round = 0; round < DefaultRounds; round++)
            {
                using var roundInput = new MemoryStream();

                Write(roundInput, (round & 1) != 0 ? passwordSequence : result);
                if (round % 3 != 0) Write(roundInput, saltSequence);
                if (round % 7 != 0) Write(roundInput, passwordSequence);
                Write(roundInput, (round & 1) != 0 ? result : passwordSequence);

                result = Sha512(roundInput.ToArray());
            }

            return $"$6${salt}${EncodeBase64(result)}";
        }

        /// <summary>
        /// Salt aleatório de 16 caracteres do alfabeto do crypt(3). O alfabeto tem 64
        /// caracteres e 256 é múltiplo exato de 64, então o <c>%</c> aqui não introduz
        /// viés — cada caractere é equiprovável.
        /// </summary>
        public static string GenerateSalt()
        {
            byte[] raw = RandomNumberGenerator.GetBytes(MaxSaltLength);

            var salt = new StringBuilder(MaxSaltLength);
            foreach (byte value in raw)
                salt.Append(Base64Alphabet[value % Base64Alphabet.Length]);

            return salt.ToString();
        }

        /// <summary>
        /// Hash de <paramref name="source"/> repetido <paramref name="repetitions"/> vezes,
        /// truncado/estendido para <paramref name="length"/> bytes — as sequências P e S da
        /// especificação, que derivam material de tamanho igual ao da entrada original.
        /// </summary>
        private static byte[] SequenceFromRepeatedDigest(byte[] source, int repetitions, int length)
        {
            using var input = new MemoryStream();
            for (int i = 0; i < repetitions; i++)
                Write(input, source);

            return Repeat(Sha512(input.ToArray()), length);
        }

        /// <summary>Estende ciclicamente <paramref name="digest"/> até
        /// <paramref name="length"/> bytes.</summary>
        private static byte[] Repeat(byte[] digest, int length)
        {
            byte[] output = new byte[length];
            for (int i = 0; i < length; i++)
                output[i] = digest[i % digest.Length];

            return output;
        }

        private static void Write(MemoryStream stream, byte[] data) => stream.Write(data, 0, data.Length);

        private static byte[] Sha512(params byte[][] parts)
        {
            using var input = new MemoryStream();
            foreach (byte[] part in parts)
                Write(input, part);

            return SHA512.HashData(input.ToArray());
        }

        /// <summary>
        /// Base64 do crypt(3): os 64 bytes do digest são lidos em 21 trios embaralhados
        /// (bytes 0/21/42, depois 22/43/1, …) mais o byte 63 sozinho, e cada trio vira 4
        /// caracteres emitidos do bit menos significativo para o mais significativo.
        /// A permutação dos trios é <c>(k*22) % 63</c> e os dois offsets seguintes,
        /// +21 e +42, também módulo 63.
        /// </summary>
        private static string EncodeBase64(byte[] digest)
        {
            var encoded = new StringBuilder(86);

            for (int group = 0; group < 21; group++)
            {
                int first = group * 22 % 63;
                AppendBase64(encoded, digest[first], digest[(first + 21) % 63], digest[(first + 42) % 63], 4);
            }

            AppendBase64(encoded, 0, 0, digest[63], 2);

            return encoded.ToString();
        }

        private static void AppendBase64(StringBuilder target, byte high, byte middle, byte low, int characters)
        {
            int window = (high << 16) | (middle << 8) | low;

            for (int i = 0; i < characters; i++)
            {
                target.Append(Base64Alphabet[window & 0x3F]);
                window >>= 6;
            }
        }
    }
}
