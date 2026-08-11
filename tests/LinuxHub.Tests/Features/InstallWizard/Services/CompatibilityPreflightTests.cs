using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class CompatibilityPreflightTests
    {
        [Fact]
        public void DynamicDisk_IsRejected()
        {
            var report = CompatibilityPreflightRunner.CreateDefault().Evaluate(new CompatibilityFacts
            {
                DiskIsDynamic = true,
                TopologyDeterminate = true,
                EncryptionQuerySucceeded = true,
                EncryptionConversionStatus = "FullyDecrypted",
                EncryptionPercentComplete = 0,
                EncryptionProtectionStatus = 0,
            });

            Assert.True(report.HasRejection);
            Assert.Contains(report.Rejections, r => r.Code == "COMPAT_E_DYNAMIC_DISK");
        }

        [Fact]
        public void IndeterminateTopology_IsRejected()
        {
            var report = CompatibilityPreflightRunner.CreateDefault().Evaluate(new CompatibilityFacts
            {
                TopologyDeterminate = false,
                EncryptionQuerySucceeded = true,
                EncryptionConversionStatus = "FullyDecrypted",
                EncryptionPercentComplete = 0,
                EncryptionProtectionStatus = 0,
            });

            Assert.Contains(report.Rejections, r => r.Code == "COMPAT_E_TOPOLOGY_INDETERMINATE");
        }

        [Fact]
        public void EncryptionQueryFailed_IsRejectedNotTreatedAsClear()
        {
            var report = CompatibilityPreflightRunner.CreateDefault().Evaluate(new CompatibilityFacts
            {
                TopologyDeterminate = true,
                EncryptionQuerySucceeded = false,
            });

            Assert.Contains(report.Rejections, r => r.Code == "COMPAT_E_ENCRYPTION_UNREADABLE");
        }

        [Fact]
        public void SuspendedEncryption_IsRejected()
        {
            var report = CompatibilityPreflightRunner.CreateDefault().Evaluate(new CompatibilityFacts
            {
                TopologyDeterminate = true,
                EncryptionQuerySucceeded = true,
                EncryptionConversionStatus = "EncryptionPaused",
                EncryptionPercentComplete = 100,
                EncryptionProtectionStatus = 0,
            });

            Assert.Contains(report.Rejections, r => r.Code == "COMPAT_E_ENCRYPTION_ACTIVE");
        }

        [Fact]
        public void BootNextSkipped_IsWarningNotApproval()
        {
            var report = CompatibilityPreflightRunner.CreateDefault().Evaluate(new CompatibilityFacts
            {
                TopologyDeterminate = true,
                EncryptionQuerySucceeded = true,
                EncryptionConversionStatus = "FullyDecrypted",
                EncryptionPercentComplete = 0,
                EncryptionProtectionStatus = 0,
                BootNextProbeResult = "skipped",
            });

            Assert.False(report.HasRejection);
            Assert.Contains(report.Warnings, w => w.Code == "COMPAT_W_BOOTNEXT_SKIPPED");
        }

        [Fact]
        public void Rules_AreIndependentlyComposable()
        {
            var onlyUsb = new CompatibilityPreflightRunner([new UsbSystemDiskRule()]);
            var report = onlyUsb.Evaluate(new CompatibilityFacts
            {
                DiskIsSystem = true,
                DiskBusType = "USB",
            });

            Assert.Single(report.Findings);
            Assert.Equal("COMPAT_E_USB_SYSTEM_DISK", report.Findings[0].Code);
        }

        [Fact]
        public void FactsParser_ReadsScriptMarkers()
        {
            CompatibilityFacts facts = CompatibilityFactsParser.Parse("""
                FACT_DISK_BUS=USB
                FACT_DISK_IS_SYSTEM=true
                FACT_TOPOLOGY_DETERMINATE=true
                FACT_ENC_QUERY_OK=true
                FACT_ENC_CONVERSION=FullyDecrypted
                FACT_ENC_PERCENT=0
                FACT_ENC_PROTECTION=0
                FACT_BOOTNEXT_PROBE=skipped
                FACT_SHRINKABLE_BYTES=10737418240
                """);

            Assert.Equal("USB", facts.DiskBusType);
            Assert.True(facts.DiskIsSystem);
            Assert.Equal(10L * 1024 * 1024 * 1024, facts.ShrinkableBytes);
            Assert.Equal("skipped", facts.BootNextProbeResult);
        }
    }
}
