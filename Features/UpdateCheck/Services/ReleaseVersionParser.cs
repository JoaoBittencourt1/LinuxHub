namespace LinuxHub.Features.UpdateCheck.Services
{
    /// <summary>
    /// Converte a tag de uma release ("v1.2.4") em <see cref="Version"/> e decide se a
    /// versão em execução está atrás da publicada. Classe pura: sem rede, sem WPF, sem
    /// I/O — é aqui que mora a regra que pode dar errado, e por isso ela é testável
    /// sozinha (constitution §5).
    /// </summary>
    internal static class ReleaseVersionParser
    {
        /// <summary>Versões são comparadas por Major.Minor.Build; o quarto componente é ignorado.</summary>
        private const int ComparableComponents = 3;

        /// <summary>
        /// Lê uma tag no formato "vX.Y.Z". Devolve <c>false</c> em vez de inventar uma
        /// versão quando a tag não casa com o formato — um palpite aqui viraria um aviso
        /// de atualização errado, ou o silêncio de nunca avisar.
        /// </summary>
        public static bool TryParseTag(string? tag, out Version version)
        {
            version = new Version(0, 0, 0);

            if (string.IsNullOrWhiteSpace(tag))
                return false;

            string trimmed = tag.Trim();

            if (!trimmed.StartsWith('v'))
                return false;

            string[] parts = trimmed[1..].Split('.');

            if (parts.Length != ComparableComponents)
                return false;

            int[] numbers = new int[ComparableComponents];
            for (int i = 0; i < ComparableComponents; i++)
            {
                // int.TryParse aceitaria "+1" e sinal negativo; a tag tem que ser dígito puro,
                // senão "v1.-2.3" passaria e produziria uma versão sem sentido.
                if (!parts[i].All(char.IsAsciiDigit) || !int.TryParse(parts[i], out numbers[i]))
                    return false;
            }

            version = new Version(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        /// <summary>
        /// Normaliza para exatamente três componentes. <see cref="Version"/> compara quatro,
        /// e um componente ausente vale -1, não 0: sem isso, Version(1,2,4) — vinda da tag
        /// "v1.2.4" — fica MENOR que Version(1,2,4,0) — vinda do assembly da mesma versão —
        /// e a comparação passa a mentir. Ver decisão 3 do design.md.
        /// </summary>
        public static Version Normalize(Version version)
        {
            ArgumentNullException.ThrowIfNull(version);

            return new Version(
                version.Major,
                version.Minor,
                version.Build < 0 ? 0 : version.Build);
        }

        /// <summary>Verdadeiro quando a versão em execução está atrás da publicada.</summary>
        public static bool IsOutdated(Version running, Version latest)
        {
            ArgumentNullException.ThrowIfNull(running);
            ArgumentNullException.ThrowIfNull(latest);

            return Normalize(running) < Normalize(latest);
        }
    }
}
