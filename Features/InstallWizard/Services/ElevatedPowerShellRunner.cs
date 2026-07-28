using System.Diagnostics;
using System.IO;
using System.Text;
using LinuxHub.Common.Diagnostics;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Executa um script PowerShell em processo elevado. bcdedit/diskpart/PowerShell
    /// lançados com <c>Verb=runas</c> exigem <c>UseShellExecute=true</c>, que não suporta
    /// redirecionamento direto de stdout/stderr — por isso o script roda via
    /// <c>cmd.exe</c>, que redireciona a saída para um arquivo de log lido depois.
    /// Compartilhado entre <see cref="MbrBackupService"/>, <see cref="BootStagingService"/>,
    /// <see cref="BootConfigurationService"/> e <see cref="DiskPartitioningService"/> para
    /// não duplicar esse boilerplate de elevação.
    /// </summary>
    internal static class ElevatedPowerShellRunner
    {
        /// <summary>
        /// Prefixo da única linha que a UI mostra quando um script falha. Sem ele o diálogo
        /// de erro recebia o registro cru do PowerShell — caminho do .ps1 temporário, til
        /// apontando a coluna, <c>CategoryInfo</c>, <c>FullyQualifiedErrorId</c> — com a frase
        /// que interessa repetida três vezes no meio (erro real em teste). A saída bruta
        /// continua inteira no <see cref="DiagnosticLog"/>.
        /// </summary>
        private const string ErrorMarker = "LINUXHUB_ERROR:";

        public static string Run(string script, string operationDescription)
        {
            script = WrapWithErrorMarker(script);

            string scriptPath = Path.Combine(Path.GetTempPath(), $"linuxhub_{Guid.NewGuid():N}.ps1");
            string logPath = Path.Combine(Path.GetTempPath(), $"linuxhub_{Guid.NewGuid():N}.log");

            // UTF-8 COM BOM: o Windows PowerShell 5.1 interpreta um .ps1 sem BOM usando a
            // codepage ANSI do sistema, então todo acento vira mojibake ("Não" -> "NÃ£o") —
            // inclusive dentro das mensagens de erro que os scripts lançam. O BOM é o que faz
            // ele reconhecer o arquivo como UTF-8. (File.WriteAllText grava sem BOM.)
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                // `chcp 65001` põe o console em UTF-8 antes de rodar, senão a saída das
                // ferramentas nativas (bcdedit, diskpart) sai na codepage OEM e é lida aqui
                // como UTF-8, virando "vers�o" no log e nas mensagens de erro.
                Arguments = $"/c chcp 65001 > nul && powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\" > \"{logPath}\" 2>&1",
                Verb = "runas",
                // CreateNoWindow só tem efeito com UseShellExecute=false — incompatível com
                // Verb=runas (elevação via UAC exige ShellExecute). WindowStyle é o que
                // realmente esconde a janela nesse caso (honrado pelo ShellExecuteEx).
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            };

            try
            {
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException(
                        $"Não foi possível iniciar o processo elevado para {operationDescription}.");

                process.WaitForExit();

                string output = File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;

                // Arquiva script + saída ANTES de qualquer throw: o `finally` apaga os dois
                // arquivos temporários, e sem isso uma falha não deixava nenhum rastro pra
                // diagnosticar depois (ver DiagnosticLog).
                DiagnosticLog.Write(
                    $"{operationDescription} (exit={process.ExitCode})",
                    $"--- script ---{Environment.NewLine}{script}{Environment.NewLine}" +
                    $"--- saída ---{Environment.NewLine}{output}");

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(BuildFailureMessage(
                        output, operationDescription, process.ExitCode));
                }

                return output;
            }
            finally
            {
                if (File.Exists(scriptPath))
                    File.Delete(scriptPath);
                if (File.Exists(logPath))
                    File.Delete(logPath);
            }
        }

        /// <summary>
        /// Envolve o script num try/catch que reduz qualquer falha — <c>throw</c> do próprio
        /// script ou erro de cmdlet — a uma linha só. Fica aqui, e não em cada serviço, para
        /// valer pra todo script elevado sem repetir o bloco. O <c>-replace</c> achata
        /// mensagens de várias linhas: a extração do lado C# lê uma linha por erro.
        /// </summary>
        internal static string WrapWithErrorMarker(string script) => $@"try {{
{script}
}}
catch {{
    Write-Output ""{ErrorMarker} $($_.Exception.Message -replace '\r?\n', ' ')""
    exit 1
}}";

        /// <summary>
        /// Mensagem de falha para a UI: a linha marcada pelo script quando existe, senão a
        /// saída bruta. O caminho bruto cobre o caso em que o processo morre sem passar pelo
        /// catch (PowerShell não iniciou, política de execução, o próprio cmd falhou) —
        /// feio, mas nunca silencioso.
        /// </summary>
        internal static string BuildFailureMessage(string output, string operationDescription, int exitCode)
        {
            string? reason = ExtractMarkedError(output);

            return reason is not null
                ? $"{reason}{Environment.NewLine}{Environment.NewLine}" +
                  $"Detalhes técnicos em {DiagnosticLog.CurrentLogFile}"
                : $"Falha em: {operationDescription} (código {exitCode}). " +
                  $"Log completo em {DiagnosticLog.CurrentLogFile}{Environment.NewLine}{output}";
        }

        private static string? ExtractMarkedError(string output)
        {
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith(ErrorMarker, StringComparison.Ordinal))
                    return trimmed[ErrorMarker.Length..].Trim();
            }

            return null;
        }
    }
}
