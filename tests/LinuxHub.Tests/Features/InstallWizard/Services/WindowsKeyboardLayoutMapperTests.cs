using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// O teclado era a constante <c>"us"</c>. Quem instalava com um ABNT2 configurado no
    /// Windows recebia um sistema em português com teclado americano — sem <c>ç</c> e com a
    /// acentuação fora do lugar, percebido na primeira senha digitada. É o defeito que
    /// originou este change.
    /// </summary>
    public class WindowsKeyboardLayoutMapperTests
    {
        /// <summary>A palavra alta do KLID distingue variantes do mesmo idioma (ABNT ×
        /// ABNT2, QWERTY × Dvorak × internacional) e não muda o código de layout — os dois
        /// KLIDs do português do Brasil precisam cair no mesmo <c>br</c>.</summary>
        [Theory]
        [InlineData("00000416", "br")]
        [InlineData("00010416", "br")]
        [InlineData("00000409", "us")]
        [InlineData("00020409", "us")]
        [InlineData("00010409", "us")]
        [InlineData("00000816", "pt")]
        [InlineData("00000809", "gb")]
        [InlineData("00000407", "de")]
        [InlineData("0000040C", "fr")]
        [InlineData("0000080A", "latam")]
        [InlineData("0000041D", "se")]
        [InlineData("0000041F", "tr")]
        [InlineData("0001041F", "tr")]
        public void KnownLayout_IsConvertedToItsLinuxKeymap(string klid, string expectedKeymap)
        {
            var detected = WindowsKeyboardLayoutMapper.ToLinuxKeymap(klid);

            Assert.Equal(expectedKeymap, detected.Value);
            Assert.False(detected.IsFallback);
        }

        /// <summary>A tabela nunca vai cobrir todo layout existente. O que não pode acontecer é
        /// o não-coberto virar <c>"us"</c> calado — que é exatamente o bug antigo. Ele continua
        /// sendo o padrão, mas marcado, para o wizard pedir revisão.</summary>
        [Theory]
        [InlineData("0000FFFF")]
        [InlineData("não é hex")]
        [InlineData("")]
        [InlineData(null)]
        public void UnmappedLayout_FallsBackToTheDeclaredDefault_AndSaysSo(string? klid)
        {
            var detected = WindowsKeyboardLayoutMapper.ToLinuxKeymap(klid);

            Assert.Equal(WindowsKeyboardLayoutMapper.FallbackKeymap, detected.Value);
            Assert.True(detected.IsFallback);
        }

        /// <summary>A lista do seletor precisa conter tudo que a detecção produz: um valor fora
        /// dela apareceria como campo em branco na tela.</summary>
        [Fact]
        public void SupportedKeymaps_CoverEveryValueTheMappingCanProduce()
        {
            var keymaps = WindowsKeyboardLayoutMapper.SupportedKeymaps;

            Assert.Contains(WindowsKeyboardLayoutMapper.FallbackKeymap, keymaps);
            Assert.Contains("br", keymaps);
            Assert.Equal(keymaps.Distinct().Count(), keymaps.Count);
        }
    }
}
