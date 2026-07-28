using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Cobre só as partes puras do runner — montar o wrapper e traduzir a saída em mensagem.
    /// Executar de fato exige elevação e UAC, e não cabe em teste automatizado.
    /// </summary>
    public class ElevatedPowerShellRunnerTests
    {
        [Fact]
        public void WrapWithErrorMarker_KeepsOriginalScriptAndCatchesFailures()
        {
            string wrapped = ElevatedPowerShellRunner.WrapWithErrorMarker("Get-Partition -DiskNumber 0");

            Assert.Contains("Get-Partition -DiskNumber 0", wrapped);
            Assert.StartsWith("try {", wrapped);
            Assert.Contains("catch {", wrapped);
            Assert.Contains("exit 1", wrapped);
        }

        [Fact]
        public void FailureMessage_ShowsOnlyTheMarkedLine_NotThePowerShellErrorRecord()
        {
            // Saída real de um shrink recusado: a frase útil vem primeiro e depois se repete
            // dentro do registro de erro do PowerShell, junto do caminho do .ps1 temporário.
            string output = string.Join('\n',
                "LINUXHUB_ERROR: O Windows só consegue liberar 21 GB na partição 3 do disco 0.",
                @"No C:\Users\joaov\AppData\Local\Temp\linuxhub_6c2f6ed0.ps1:8 caractere:5",
                "+     throw \"O Windows só consegue liberar ...",
                "+     ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~",
                "    + CategoryInfo          : OperationStopped: (...:String) [], RuntimeException",
                "    + FullyQualifiedErrorId : O Windows só consegue liberar 21 GB ...");

            string message = ElevatedPowerShellRunner.BuildFailureMessage(output, "redimensionamento", 1);

            Assert.StartsWith("O Windows só consegue liberar 21 GB na partição 3 do disco 0.", message);
            Assert.DoesNotContain("CategoryInfo", message);
            Assert.DoesNotContain("FullyQualifiedErrorId", message);
            Assert.DoesNotContain(".ps1", message);
            Assert.DoesNotContain("~~~", message);
        }

        [Fact]
        public void FailureMessage_WithoutMarker_FallsBackToRawOutput()
        {
            // Sem marcador o processo morreu antes do catch (PowerShell não iniciou, política
            // de execução). Feio, mas nunca silencioso — constitution §6.
            string message = ElevatedPowerShellRunner.BuildFailureMessage(
                "Acesso negado.", "backup do MBR", 5);

            Assert.Contains("backup do MBR", message);
            Assert.Contains("Acesso negado.", message);
            Assert.Contains("5", message);
        }

        [Fact]
        public void FailureMessage_AlwaysPointsAtThePersistentLog()
        {
            string withMarker = ElevatedPowerShellRunner.BuildFailureMessage(
                "LINUXHUB_ERROR: falhou", "shrink", 1);
            string withoutMarker = ElevatedPowerShellRunner.BuildFailureMessage("ruído", "shrink", 1);

            Assert.Contains("install-", withMarker);
            Assert.Contains("install-", withoutMarker);
        }
    }
}
