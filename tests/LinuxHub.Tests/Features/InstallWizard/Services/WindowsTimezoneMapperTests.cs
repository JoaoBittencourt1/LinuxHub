using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// O fuso era a constante <c>"America/Sao_Paulo"</c>: certa para quem mora em São Paulo,
    /// silenciosamente errada para todo o resto do mundo.
    /// </summary>
    public class WindowsTimezoneMapperTests
    {
        [Theory]
        [InlineData("E. South America Standard Time", "America/Sao_Paulo")]
        [InlineData("W. Europe Standard Time", "Europe/Berlin")]
        [InlineData("Tokyo Standard Time", "Asia/Tokyo")]
        [InlineData("Pacific Standard Time", "America/Los_Angeles")]
        public void KnownWindowsZone_IsConvertedToItsIanaId(string windowsId, string expectedIana)
        {
            var detected = WindowsTimezoneMapper.ToIanaTimezone(windowsId);

            Assert.Equal(expectedIana, detected.Value);
            Assert.False(detected.IsFallback);
        }

        /// <summary>O ponto de 1.3: sem correspondência, o valor devolvido é o padrão declarado
        /// E vem marcado como padrão — é a marcação que permite o wizard pedir revisão em vez
        /// de gravar um palpite calado.</summary>
        [Theory]
        [InlineData("Fuso Que Não Existe")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void UnmappedZone_FallsBackToTheDeclaredDefault_AndSaysSo(string? windowsId)
        {
            var detected = WindowsTimezoneMapper.ToIanaTimezone(windowsId);

            Assert.Equal(WindowsTimezoneMapper.FallbackTimezone, detected.Value);
            Assert.True(detected.IsFallback);
        }

        /// <summary>A lista do seletor precisa conter o que a detecção produz: um valor fora
        /// dela apareceria como campo em branco na tela, e o usuário não teria como saber o que
        /// seria gravado.</summary>
        [Fact]
        public void KnownTimezones_ContainTheDetectedZoneAndTheFallback()
        {
            var timezones = WindowsTimezoneMapper.KnownIanaTimezones();

            Assert.Contains(WindowsTimezoneMapper.FallbackTimezone, timezones);
            Assert.Contains(WindowsTimezoneMapper.ToIanaTimezone(TimeZoneInfo.Local.Id).Value, timezones);
            Assert.Equal(timezones.Distinct().Count(), timezones.Count);
        }
    }
}
