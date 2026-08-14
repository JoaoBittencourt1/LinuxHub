using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    public class MbrBackupServiceTests
    {
        [Fact]
        public void BuildBackupScript_ReadsFromCorrectPhysicalDrive()
        {
            string script = MbrBackupService.BuildBackupScript(diskIndex: 2, backupPath: @"C:\temp\backup.bin");

            Assert.Contains(@"\\.\PhysicalDrive2", script);
            Assert.Contains(@"C:\temp\backup.bin", script);
            Assert.Contains("'Read'", script);
        }

        [Fact]
        public void BuildWriteBootCodeScript_OnlyOverwritesFirst440BytesPreservingPartitionTable()
        {
            string script = MbrBackupService.BuildWriteBootCodeScript(diskIndex: 0, bootCodeFilePath: @"C:\temp\boot.img");

            Assert.Contains("[Array]::Copy($bootCode, $mbr, 440)", script);
            Assert.Contains("$stream.Write($mbr, 0, 512)", script);
            Assert.Contains("bootCode.Length -ne 440", script);
        }

        [Fact]
        public void VersionedRestoreMbrScript_ValidatesBackupSizeBeforeWriting()
        {
            string script = ScriptCatalog.Read(ScriptCatalog.RestoreMbr);

            Assert.Contains("PhysicalDrive$DiskNumber", script);
            Assert.Contains("mbr.Length -ne 512", script);
            Assert.Contains("param(", script);
        }

        [Fact]
        public void BuildReadMbrScript_ReadsFromCorrectDriveAndEmitsBase64Marker()
        {
            string script = MbrBackupService.BuildReadMbrScript(diskIndex: 3);

            Assert.Contains(@"\\.\PhysicalDrive3", script);
            Assert.Contains("MBRBASE64:", script);
            Assert.Contains("'Read'", script);
        }

        [Fact]
        public void BuildWriteCoreImageScript_WritesStartingAtSector1NeverAtMbr()
        {
            string script = MbrBackupService.BuildWriteCoreImageScript(diskIndex: 0, coreImageFilePath: @"C:\temp\core.img");

            Assert.Contains(@"\\.\PhysicalDrive0", script);
            Assert.Contains("$stream.Position = 512", script);
            Assert.Contains(@"C:\temp\core.img", script);
        }
    }
}
