using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using LinuxHub.Features.InstallWizard.ViewModels;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.ViewModels
{
    /// <summary>
    /// Ver specs/install-wizard/spec.md — "Permitir revisar as informações regionais antes de
    /// instalar". Os dois cenários que importam são o usuário aceitar o detectado e o usuário
    /// corrigi-lo.
    /// </summary>
    public class RegionalSettingsViewModelTests
    {
        private sealed class StubSystemInfoProvider(
            DetectedRegionalSetting locale,
            DetectedRegionalSetting keymap,
            DetectedRegionalSetting timezone) : ISystemInfoProvider
        {
            public DetectedRegionalSetting GetLocale() => locale;
            public DetectedRegionalSetting GetKeymap() => keymap;
            public DetectedRegionalSetting GetTimezone() => timezone;
        }

        private static RegionalSettingsViewModel BuildViewModel(
            DetectedRegionalSetting? locale = null,
            DetectedRegionalSetting? keymap = null,
            DetectedRegionalSetting? timezone = null) =>
            new(new StubSystemInfoProvider(
                locale ?? DetectedRegionalSetting.Detected("pt_BR.UTF-8"),
                keymap ?? DetectedRegionalSetting.Detected("br"),
                timezone ?? DetectedRegionalSetting.Detected("America/Sao_Paulo")));

        [Fact]
        public void OpensWithWhatWasDetected()
        {
            var vm = BuildViewModel();

            Assert.Equal("pt_BR.UTF-8", vm.Locale);
            Assert.Equal("br", vm.Keymap);
            Assert.Equal("America/Sao_Paulo", vm.Timezone);
        }

        [Fact]
        public void CorrectedValue_ReplacesTheDetectedOne()
        {
            var vm = BuildViewModel();

            vm.Keymap = "us";

            Assert.Equal("us", vm.Keymap);
        }

        /// <summary>Detecção que caiu no padrão precisa se anunciar. Um padrão que não se
        /// anuncia é o defeito antigo com outro nome.</summary>
        [Fact]
        public void FallbackDetection_IsFlaggedForReview()
        {
            var vm = BuildViewModel(keymap: DetectedRegionalSetting.Fallback("us"));

            Assert.True(vm.HasUndetectedSetting);
            Assert.Equal("us", vm.Keymap);
        }

        [Fact]
        public void EverythingDetected_RaisesNoReviewFlag() =>
            Assert.False(BuildViewModel().HasUndetectedSetting);

        /// <summary>Um valor fora da lista de opções aparece como seletor em branco na tela —
        /// e o usuário instalaria sem nunca ver o que seria gravado.</summary>
        [Fact]
        public void DetectedValues_AreAmongTheOfferedOptions()
        {
            var vm = BuildViewModel();

            Assert.Contains(vm.AvailableLocales, option => option.Value == vm.Locale);
            Assert.Contains(vm.Keymap, vm.AvailableKeymaps);
            Assert.Contains(vm.Timezone, vm.AvailableTimezones);
        }
    }
}
