using System.IO;

namespace LinuxHub.Common.Diagnostics
{
    /// <summary>
    /// Log persistente em disco das operações elevadas (bcdedit, diskpart, escrita na ESP).
    /// Existe porque essas operações rodam num processo elevado separado, com a janela
    /// escondida: quando algo falha, a única evidência era a exceção na UI, e o script e a
    /// saída eram apagados logo em seguida — sem nenhum rastro pra diagnosticar depois do
    /// reboot. Como o sintoma típico de uma falha de boot só aparece DEPOIS de reiniciar,
    /// não dá pra depender de nada que viva só na memória do processo.
    /// </summary>
    internal static class DiagnosticLog
    {
        private static readonly Lock Gate = new();

        public static string LogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinuxHub",
            "logs");

        public static string CurrentLogFile => Path.Combine(
            LogDirectory, $"install-{DateTime.Now:yyyy-MM-dd}.log");

        public static void Write(string section, string content)
        {
            string entry =
                $"{Environment.NewLine}===== {DateTime.Now:HH:mm:ss} | {section} ====={Environment.NewLine}" +
                $"{content}{Environment.NewLine}";

            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(CurrentLogFile, entry);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Não conseguir gravar o log nunca pode abortar uma instalação em andamento —
                // o log é diagnóstico, não parte da operação. Só estas duas exceções são
                // toleradas (disco cheio / permissão); qualquer outra é bug e sobe.
                System.Diagnostics.Debug.WriteLine($"DiagnosticLog falhou: {ex.Message}");
            }
        }
    }
}
