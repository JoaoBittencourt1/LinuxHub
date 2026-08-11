using System.IO;
using LinuxHub.Common.Appearance;
using Wpf.Ui.Appearance;
using Xunit;

namespace LinuxHub.Tests.Common.Appearance
{
    public class ThemeServiceTests
    {
        [Fact]
        public void ApplyPersistedOrDefault_UsesLightWhenNoPreferenceFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "linuxhub-theme-" + Guid.NewGuid().ToString("N"), "ui-theme.txt");
            var service = new ThemeService(path);

            service.ApplyPersistedOrDefault();

            Assert.Equal(ApplicationTheme.Light, service.Current);
            Assert.False(service.IsDark);
        }

        [Fact]
        public void Toggle_PersistsAndFlips()
        {
            string dir = Path.Combine(Path.GetTempPath(), "linuxhub-theme-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "ui-theme.txt");
            var service = new ThemeService(path);

            service.Apply(ApplicationTheme.Light);
            service.Toggle();

            Assert.Equal(ApplicationTheme.Dark, service.Current);
            Assert.Equal("Dark", File.ReadAllText(path).Trim());

            var reopened = new ThemeService(path);
            reopened.ApplyPersistedOrDefault();
            Assert.Equal(ApplicationTheme.Dark, reopened.Current);
        }
    }
}
