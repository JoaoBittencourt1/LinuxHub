using Microsoft.Win32;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Reads Secure Boot and volume encryption from authoritative Windows sources.
    /// Encryption is reported as conversion status + percent (task 6.4), never a boolean.
    /// </summary>
    public sealed class BootSecurityService : IBootSecurityService
    {
        private const string SecureBootKeyPath = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
        private const string SecureBootValueName = "UEFISecureBootEnabled";

        public bool IsSecureBootEnabled()
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SecureBootKeyPath);
            return key?.GetValue(SecureBootValueName) is int value && value == 1;
        }

        public VolumeEncryptionState GetVolumeEncryptionState(char driveLetter)
        {
            string output = ElevatedPowerShellRunner.Run(
                BuildEncryptionScript(driveLetter),
                $"volume encryption status on {driveLetter}:");

            if (output.Contains("ENC_QUERY_FAILED=true", StringComparison.Ordinal))
            {
                return new VolumeEncryptionState(
                    ConversionStatus: "QueryFailed",
                    PercentComplete: 0,
                    ProtectionStatus: -1,
                    QuerySucceeded: false);
            }

            string status = ReadMarker(output, "ENC_CONVERSION") ?? "Unknown";
            double percent = 0;
            if (double.TryParse(ReadMarker(output, "ENC_PERCENT"), out double parsed))
                percent = parsed;
            int protection = 0;
            if (int.TryParse(ReadMarker(output, "ENC_PROTECTION"), out int parsedProtection))
                protection = parsedProtection;

            return new VolumeEncryptionState(status, percent, protection, QuerySucceeded: true);
        }

        /// <summary>
        /// Emits conversion status, percent, and protection. Query failure is an explicit
        /// marker — never ENC_CONVERSION=FullyDecrypted by omission.
        /// </summary>
        internal static string BuildEncryptionScript(char driveLetter) => $@"
$ErrorActionPreference = 'Stop'
try {{
  $volumes = @(Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume)
  $alvo = $volumes | Where-Object {{ $_.DriveLetter -eq '{driveLetter}:' }} | Select-Object -First 1
  if ($null -eq $alvo) {{
    Write-Output 'ENC_CONVERSION=FullyDecrypted'
    Write-Output 'ENC_PERCENT=0'
    Write-Output 'ENC_PROTECTION=0'
    exit 0
  }}
  $conversion = Invoke-CimMethod -InputObject $alvo -MethodName GetConversionStatus -ErrorAction Stop
  $protection = Invoke-CimMethod -InputObject $alvo -MethodName GetProtectionStatus -ErrorAction Stop
  if ($conversion.ReturnValue -ne 0 -or $protection.ReturnValue -ne 0) {{
    Write-Output 'ENC_QUERY_FAILED=true'
    exit 0
  }}
  $map = @{{ 0='FullyDecrypted'; 1='FullyEncrypted'; 2='EncryptionInProgress'; 3='DecryptionInProgress'; 4='EncryptionPaused'; 5='DecryptionPaused' }}
  $status = $map[[int]$conversion.ConversionStatus]
  if (-not $status) {{ $status = 'Unknown' }}
  Write-Output (""ENC_CONVERSION={{0}}"" -f $status)
  Write-Output (""ENC_PERCENT={{0}}"" -f [double]$conversion.EncryptionPercentage)
  Write-Output (""ENC_PROTECTION={{0}}"" -f [int]$protection.ProtectionStatus)
}} catch {{
  Write-Output 'ENC_QUERY_FAILED=true'
}}
";

        private static string? ReadMarker(string output, string key)
        {
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                string prefix = key + "=";
                if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                    return trimmed[prefix.Length..].Trim();
            }

            return null;
        }
    }
}
