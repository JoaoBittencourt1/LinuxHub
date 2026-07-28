using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Os hashes esperados foram gerados por <c>openssl passwd -6 -salt &lt;salt&gt; &lt;senha&gt;</c>
    /// num Ubuntu real (WSL), não escritos à mão nem tirados de documentação — a saída deste
    /// algoritmo é impossível de conferir por leitura, e um erro de um bit produz um hash que
    /// parece perfeitamente válido e simplesmente não deixa o usuário logar no sistema
    /// instalado. Se algum destes falhar, o bug está no <see cref="Sha512Crypt"/>, não no vetor.
    /// </summary>
    public class Sha512CryptTests
    {
        [Theory]
        [InlineData("Hello world!", "saltstring",
            "$6$saltstring$svn8UoSVapNtMuq1ukKS4tPQd8iKwSMHWjl/O817G3uBnIFNjnQJuesI68u4OTLiBFdcbYEdFCoEOfaS35inz1")]
        [InlineData("linuxhub", "abcdefghijklmnop",
            "$6$abcdefghijklmnop$zD.aVfOby4QY3jq5toBjfWeSgwmLqKARLs7Vup6khwKiyvYBRXnhkr4ZhWkw1SIzbVX2xUCNlGOfcCQ0QN21m0")]
        [InlineData("a", "xyz",
            "$6$xyz$QdWnVhA9Jebjwi2.aD6Ly.KW69PwO5EGuibLjRJJD0W6WnB3HQvkGo583.VFK9X0vAjnP9YImpHqvDtuj2M.s/")]
        [InlineData("senha com espaco e acento: ção", "Ab9CdEfGh",
            "$6$Ab9CdEfGh$0Me2dAbGKEtiLw5WGi57PPKdOQ2FWmRLt/EpLNNcw.cMUz.mNd1aGrsPxuh52Ll9XlXOvZcK3uIGq/A2bkP5y1")]
        public void Hash_MatchesOpenSslReference(string password, string salt, string expected)
        {
            Assert.Equal(expected, Sha512Crypt.Hash(password, salt));
        }

        [Fact]
        public void Hash_TruncatesSaltAtSixteenCharacters()
        {
            // crypt(3) ignora o que passa de 16 — se não truncássemos, o hash gravado no
            // autoinstall usaria um salt que o sistema instalado nunca reproduz na hora do login.
            string longSalt = Sha512Crypt.Hash("linuxhub", "abcdefghijklmnopQQQQ");
            string exactSalt = Sha512Crypt.Hash("linuxhub", "abcdefghijklmnop");

            Assert.Equal(exactSalt, longSalt);
        }

        [Fact]
        public void GenerateSalt_ProducesSixteenCharactersFromTheCryptAlphabet()
        {
            const string alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            for (int attempt = 0; attempt < 50; attempt++)
            {
                string salt = Sha512Crypt.GenerateSalt();

                Assert.Equal(Sha512Crypt.MaxSaltLength, salt.Length);
                Assert.All(salt, character => Assert.Contains(character, alphabet));
            }
        }

        [Fact]
        public void GenerateSalt_DoesNotRepeat()
        {
            var salts = new HashSet<string>();
            for (int attempt = 0; attempt < 50; attempt++)
                salts.Add(Sha512Crypt.GenerateSalt());

            Assert.Equal(50, salts.Count);
        }

        [Fact]
        public void Hash_AlwaysCarriesTheSixDollarPrefixAndTheSaltBack()
        {
            string salt = Sha512Crypt.GenerateSalt();
            string hash = Sha512Crypt.Hash("qualquer", salt);

            Assert.StartsWith($"$6${salt}$", hash);

            // 86 caracteres de digest é o comprimento fixo do $6$; um a mais ou a menos
            // significa erro na permutação do base64.
            Assert.Equal(86, hash[$"$6${salt}$".Length..].Length);
        }
    }
}
