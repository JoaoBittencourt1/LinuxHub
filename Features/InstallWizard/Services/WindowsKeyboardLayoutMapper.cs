using System.Globalization;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Converte o identificador de layout de teclado do Windows (KLID — oito dígitos hex, ex.:
    /// <c>"00010416"</c>) no código de layout que o Linux usa (<c>"br"</c>).
    ///
    /// Não há equivalente do <see cref="WindowsTimezoneMapper"/> na BCL para teclado, então a
    /// tabela é mantida aqui. Ela é indexada pela PALAVRA BAIXA do KLID, que é o identificador
    /// de idioma; a palavra alta distingue variantes do mesmo idioma (ABNT × ABNT2 no
    /// português do Brasil, QWERTY × Dvorak × internacional no inglês dos EUA) e não muda o
    /// código de layout do X11 — <c>00000416</c> e <c>00010416</c> são ambos <c>br</c>.
    ///
    /// Classe pura: recebe o KLID como texto, não vai ao sistema (ver
    /// <see cref="SystemInfoProvider"/>).
    /// </summary>
    public static class WindowsKeyboardLayoutMapper
    {
        /// <summary>Padrão declarado para layout sem correspondência na tabela. Chega ao wizard
        /// marcado como padrão, para o usuário corrigir — é essa marcação que separa este
        /// comportamento do <c>"us"</c> chumbado que existia antes.</summary>
        public const string FallbackKeymap = "us";

        /// <summary>
        /// Identificador de idioma do Windows (palavra baixa do KLID) para código de layout do
        /// X11, que é o formato esperado tanto pelo <c>keyboard.layout</c> do subiquity quanto
        /// pelo <c>KEYMAP</c> do install.conf.
        /// </summary>
        private static readonly Dictionary<int, string> LayoutByLanguageId = new()
        {
            [0x0401] = "ara",   // árabe (Arábia Saudita)
            [0x0402] = "bg",    // búlgaro
            [0x0404] = "tw",    // chinês (Taiwan)
            [0x0405] = "cz",    // tcheco
            [0x0406] = "dk",    // dinamarquês
            [0x0407] = "de",    // alemão (Alemanha)
            [0x0408] = "gr",    // grego
            [0x0409] = "us",    // inglês (EUA)
            [0x040A] = "es",    // espanhol (Espanha, tradicional)
            [0x040B] = "fi",    // finlandês
            [0x040C] = "fr",    // francês (França)
            [0x040D] = "il",    // hebraico
            [0x040E] = "hu",    // húngaro
            [0x040F] = "is",    // islandês
            [0x0410] = "it",    // italiano (Itália)
            [0x0411] = "jp",    // japonês
            [0x0412] = "kr",    // coreano
            [0x0413] = "nl",    // holandês (Países Baixos)
            [0x0414] = "no",    // norueguês (bokmål)
            [0x0415] = "pl",    // polonês
            [0x0416] = "br",    // português (Brasil) — ABNT e ABNT2
            [0x0418] = "ro",    // romeno
            [0x0419] = "ru",    // russo
            [0x041A] = "hr",    // croata
            [0x041B] = "sk",    // eslovaco
            [0x041D] = "se",    // sueco (Suécia)
            [0x041E] = "th",    // tailandês
            [0x041F] = "tr",    // turco (Q e F)
            [0x0422] = "ua",    // ucraniano
            [0x0423] = "by",    // bielorrusso
            [0x0424] = "si",    // esloveno
            [0x0425] = "ee",    // estoniano
            [0x0426] = "lv",    // letão
            [0x0427] = "lt",    // lituano
            [0x0429] = "ir",    // persa
            [0x042A] = "vn",    // vietnamita
            [0x0437] = "ge",    // georgiano
            [0x0439] = "in",    // híndi
            [0x0804] = "cn",    // chinês (China)
            [0x0807] = "ch",    // alemão (Suíça)
            [0x0809] = "gb",    // inglês (Reino Unido)
            [0x080A] = "latam", // espanhol (México) — layout "América Latina"
            [0x080C] = "be",    // francês (Bélgica)
            [0x0810] = "ch",    // italiano (Suíça)
            [0x0813] = "be",    // holandês (Bélgica)
            [0x0816] = "pt",    // português (Portugal)
            [0x081A] = "rs",    // sérvio (latino)
            [0x081D] = "se",    // sueco (Finlândia)
            [0x0C07] = "de",    // alemão (Áustria)
            [0x0C09] = "us",    // inglês (Austrália)
            [0x0C0A] = "es",    // espanhol (Espanha, moderno)
            [0x0C0C] = "ca",    // francês (Canadá)
            [0x0C1A] = "rs",    // sérvio (cirílico)
            [0x1009] = "us",    // inglês (Canadá)
            [0x100C] = "ch",    // francês (Suíça)
        };

        public static DetectedRegionalSetting ToLinuxKeymap(string? keyboardLayoutId)
        {
            if (!TryReadLanguageId(keyboardLayoutId, out int languageId))
                return DetectedRegionalSetting.Fallback(FallbackKeymap);

            return LayoutByLanguageId.TryGetValue(languageId, out string? keymap)
                ? DetectedRegionalSetting.Detected(keymap)
                : DetectedRegionalSetting.Fallback(FallbackKeymap);
        }

        /// <summary>Os layouts oferecidos ao usuário para revisão — os mesmos que a tabela sabe
        /// produzir, para que o valor detectado esteja sempre entre as opções do seletor.</summary>
        public static IReadOnlyList<string> SupportedKeymaps { get; } = LayoutByLanguageId.Values
            .Append(FallbackKeymap)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        private static bool TryReadLanguageId(string? keyboardLayoutId, out int languageId)
        {
            languageId = 0;

            if (string.IsNullOrWhiteSpace(keyboardLayoutId))
                return false;

            if (!uint.TryParse(
                    keyboardLayoutId.Trim(),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out uint klid))
            {
                return false;
            }

            languageId = (int)(klid & 0xFFFF);
            return true;
        }
    }
}
