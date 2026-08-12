using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class OwnLiveMediaBootEntryBuilderTests
    {
        [Fact]
        public void Build_LoopsBackTheLiveMediaIso_AndBootsOwnKernel()
        {
            string cfg = OwnLiveMediaBootEntryBuilder.Build("/linuxhub-live.iso");

            Assert.Contains("loopback loop $isofile", cfg);
            Assert.Contains("(loop)/live/vmlinuz", cfg);
            Assert.Contains("(loop)/live/initrd.img", cfg);
            Assert.Contains("/linuxhub-live.iso", cfg);
            // D1: nenhum parâmetro de instalação desatendida na linha de boot — o instalador
            // live descobre tudo a partir do plano publicado no disco (D13), não da linha de kernel.
            Assert.DoesNotContain("autoinstall", cfg);
            Assert.DoesNotContain("automatic-ubiquity", cfg);
        }

        [Fact]
        public void Build_RejectsEmptyPath()
        {
            Assert.Throws<ArgumentException>(() => OwnLiveMediaBootEntryBuilder.Build(""));
        }
    }
}
