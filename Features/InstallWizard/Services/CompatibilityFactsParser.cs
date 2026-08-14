using System.Globalization;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Parses FACT_* lines from <c>Scripts/linuxhub-compatibility-preflight.ps1</c>.
    /// </summary>
    public static class CompatibilityFactsParser
    {
        public static CompatibilityFacts Parse(string scriptOutput)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in (scriptOutput ?? string.Empty).Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("FACT_", StringComparison.OrdinalIgnoreCase))
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 5)
                    continue;

                map[trimmed[5..eq]] = trimmed[(eq + 1)..].Trim();
            }

            return new CompatibilityFacts
            {
                Firmware = Get(map, "FIRMWARE"),
                DiskBusType = Get(map, "DISK_BUS"),
                DiskPartitionStyle = Get(map, "DISK_STYLE"),
                DiskIsDynamic = GetBool(map, "DISK_DYNAMIC"),
                DiskIsBoot = GetBool(map, "DISK_IS_BOOT"),
                DiskIsSystem = GetBool(map, "DISK_IS_SYSTEM"),
                IsStorageSpaces = GetBool(map, "STORAGE_SPACES"),
                IsVirtualDisk = GetBool(map, "DISK_VIRTUAL"),
                IsIscsi = GetBool(map, "DISK_ISCSI"),
                RaidOrVmdSuspected = GetBool(map, "RAID_OR_VMD"),
                TopologyDeterminate = GetBool(map, "TOPOLOGY_DETERMINATE"),
                EncryptionConversionStatus = Get(map, "ENC_CONVERSION"),
                EncryptionPercentComplete = GetDouble(map, "ENC_PERCENT"),
                EncryptionProtectionStatus = GetInt(map, "ENC_PROTECTION"),
                EncryptionQuerySucceeded = GetBool(map, "ENC_QUERY_OK"),
                BootNextProbeResult = Get(map, "BOOTNEXT_PROBE"),
                ShrinkableBytes = GetLong(map, "SHRINKABLE_BYTES"),
                RecoveryOffsetBytes = GetLong(map, "RECOVERY_OFFSET"),
                RecoverySizeBytes = GetLong(map, "RECOVERY_SIZE"),
            };
        }

        private static string? Get(Dictionary<string, string> map, string key) =>
            map.TryGetValue(key, out string? value) ? value : null;

        private static bool? GetBool(Dictionary<string, string> map, string key) =>
            map.TryGetValue(key, out string? value) && bool.TryParse(value, out bool parsed)
                ? parsed
                : null;

        private static int? GetInt(Dictionary<string, string> map, string key) =>
            map.TryGetValue(key, out string? value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : null;

        private static long? GetLong(Dictionary<string, string> map, string key) =>
            map.TryGetValue(key, out string? value) &&
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : null;

        private static double? GetDouble(Dictionary<string, string> map, string key) =>
            map.TryGetValue(key, out string? value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : null;
    }
}
