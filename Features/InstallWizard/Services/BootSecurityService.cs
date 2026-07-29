using Microsoft.Win32;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Lê o estado das duas proteções direto das fontes autoritativas do Windows, nunca
    /// inferindo uma da outra — são independentes: máquina pode ter Secure Boot sem
    /// BitLocker e vice-versa.
    /// </summary>
    public sealed class BootSecurityService : IBootSecurityService
    {
        private const string SecureBootKeyPath = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
        private const string SecureBootValueName = "UEFISecureBootEnabled";

        internal const string ProtectedMarker = "BITLOCKER_ON";
        internal const string UnprotectedMarker = "BITLOCKER_OFF";

        /// <summary>
        /// Lê o registro em vez de <c>Confirm-SecureBootUEFI</c>: o cmdlet exige elevação
        /// (verificado — devolve "Não é possível definir privilégios apropriados"), enquanto
        /// esta chave é legível por qualquer usuário. Isso permite recusar já na tela do
        /// wizard, sem gastar um prompt de UAC só para descobrir que não dá para instalar.
        ///
        /// A chave não existe em máquina que bootou em BIOS legado — lá não há Secure Boot
        /// para estar ligado, então ausência é <c>false</c>, não erro.
        /// </summary>
        public bool IsSecureBootEnabled()
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SecureBootKeyPath);

            return key?.GetValue(SecureBootValueName) is int value && value == 1;
        }

        /// <summary>
        /// <c>ProtectionStatus</c> = 1 significa protegido (chave ativa). Vale notar que
        /// SUSPENDER o BitLocker devolve 0 aqui e ainda assim deixa os dados cifrados no
        /// disco — mas nesse estado o Windows já está decriptando ou a proteção está off, e
        /// o caso que realmente quebra o GRUB (volume cifrado com proteção ativa) é o 1.
        ///
        /// Exige elevação: o namespace <c>MicrosoftVolumeEncryption</c> nega acesso a usuário
        /// comum (verificado). Por isso roda pelo <see cref="ElevatedPowerShellRunner"/>, e
        /// não com <c>Get-CimInstance</c> direto no processo do app.
        /// </summary>
        public bool IsVolumeBitLockerProtected(char driveLetter)
        {
            string output = ElevatedPowerShellRunner.Run(
                BuildBitLockerScript(driveLetter),
                $"verificação de BitLocker no volume {driveLetter}:");

            return output.Contains(ProtectedMarker, StringComparison.Ordinal);
        }

        /// <summary>
        /// Tudo em uma linha por comando, sem continuação de crase: dentro de uma string
        /// verbatim do C# a crase não é escape, então escrevê-la duplicada (o reflexo natural)
        /// produz DUAS crases no arquivo — o PowerShell lê isso como uma crase literal virando
        /// argumento e quebra o comando em statements soltos. O parser aceita, o script roda,
        /// e o resultado é `$volume` nulo: a guarda reportaria "sem BitLocker" justamente numa
        /// máquina com BitLocker. Bug real, pego antes de sair.
        ///
        /// Volume sem entrada no WMI (disco removível, filesystem não suportado) não é
        /// BitLocker — daí o marcador negativo para lista vazia. Já uma FALHA na consulta sobe
        /// como erro (ErrorActionPreference = Stop, sem SilentlyContinue): não conseguir
        /// verificar não é o mesmo que verificar e não achar nada, e numa guarda de segurança
        /// a dúvida tem que recusar a instalação, não liberá-la.
        /// </summary>
        internal static string BuildBitLockerScript(char driveLetter) => $@"
$ErrorActionPreference = 'Stop'

$volumes = @(Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume)
$alvo = $volumes | Where-Object {{ $_.DriveLetter -eq '{driveLetter}:' }} | Select-Object -First 1

if ($null -ne $alvo -and $alvo.ProtectionStatus -eq 1) {{
    Write-Output '{ProtectedMarker}'
}} else {{
    Write-Output '{UnprotectedMarker}'
}}";
    }
}
