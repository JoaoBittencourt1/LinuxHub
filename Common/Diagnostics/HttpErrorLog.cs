using System.IO;

namespace LinuxHub.Common.Diagnostics
{
    /// <summary>
    /// Log persistente das falhas de chamadas de rede do app (hoje, a checagem de
    /// atualização no startup).
    ///
    /// É um arquivo separado do <see cref="DiagnosticLog"/> de propósito, e não uma seção
    /// dele: o <c>install-{data}.log</c> existe para diagnosticar boot e disco quebrados
    /// depois de um reboot, e é lido justamente no pior momento. Falha de rede acontece em
    /// todo startup sem internet — despejar esse ruído recorrente ali degradaria o material
    /// usado no diagnóstico mais crítico do app. Ver decisão 7 do change
    /// update-available-notice.
    ///
    /// Estas falhas são invisíveis para o usuário por decisão de produto (estar sem internet
    /// é situação esperada, não defeito). Este arquivo é, portanto, a única evidência de que
    /// a checagem rodou e do que aconteceu — sem ele, "não há versão nova" e "o recurso está
    /// quebrado" seriam indistinguíveis.
    /// </summary>
    internal static class HttpErrorLog
    {
        private static readonly Lock Gate = new();

        public static string LogDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinuxHub",
            "logs");

        public static string CurrentLogFile => Path.Combine(LogDirectory, "http_erros.log");

        /// <summary>
        /// Registra uma tentativa de rede que falhou. <paramref name="target"/> é o que se
        /// tentou alcançar (URL) e <paramref name="detail"/> o motivo — status HTTP recebido
        /// ou a exceção. Sem os dois não dá para distinguir "sem internet" de "403 por falta
        /// de User-Agent", que é exatamente a distinção que se precisa fazer aqui.
        /// </summary>
        public static void Write(string target, string detail)
        {
            string entry =
                $"{Environment.NewLine}===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {target} ====={Environment.NewLine}" +
                $"{detail}{Environment.NewLine}";

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
                // Não conseguir gravar o log de uma falha de rede não pode virar uma segunda
                // falha visível — o registro é diagnóstico, não parte da operação. Mesma
                // tolerância do DiagnosticLog: só disco cheio / permissão; outra exceção é bug e sobe.
                System.Diagnostics.Debug.WriteLine($"HttpErrorLog falhou: {ex.Message}");
            }
        }

        public static void Write(string target, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            Write(target, exception.ToString());
        }
    }
}
