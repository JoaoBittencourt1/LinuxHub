using System.IO;
using System.Text.RegularExpressions;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Features.InstallWizard.Services
{
    /// <summary>
    /// Contract parity: the C# step catalog is the sole authority. When another runtime
    /// restates steps, this test must grow to compare copies (task 7.5 / D13).
    /// </summary>
    public class InstallationStepCatalogParityTests
    {
        [Fact]
        public void Catalog_HasStableIdsOrderAndFlags()
        {
            string[] expectedIds =
            [
                InstallationStepIds.WindowsPlanPublished,
                InstallationStepIds.WindowsDiskPrepared,
                InstallationStepIds.WindowsStagingPrepared,
                InstallationStepIds.WindowsInstallerConfigWritten,
                InstallationStepIds.WindowsTemporaryBootPrepared,
                InstallationStepIds.LiveIsoMounted,
                InstallationStepIds.LiveDistributionExtracted,
                InstallationStepIds.TargetSystemConfigured,
                InstallationStepIds.TargetBootloaderInstalled,
                InstallationStepIds.TargetInstallationVerified,
            ];

            Assert.Equal(expectedIds, InstallationStepCatalog.All.Select(s => s.Id).ToArray());

            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.WindowsDiskPrepared).Compensatable);
            Assert.False(InstallationStepCatalog.Get(InstallationStepIds.WindowsPlanPublished).Compensatable);

            // own-linux-installer task 9.1: os quatro reservados pelo change anterior agora
            // Armed=true — é esta mudança que os liga. O teste que provava a inalcançabilidade
            // deles precisa provar o oposto agora, não ser apagado (tasks.md §9.5).
            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.LiveIsoMounted).Armed);
            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.LiveDistributionExtracted).Armed);
            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.TargetSystemConfigured).Armed);
            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.TargetBootloaderInstalled).Armed);
            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.WindowsTemporaryBootPrepared).Armed);

            // Task 9.2/9.4 (D12/D5): passo novo, obrigatório, não compensável — só o bootloader
            // e a verificação tocam/atestam algo que não pode ser desfeito reformatando a
            // partição alvo.
            InstallationStepDefinition verified = InstallationStepCatalog.Get(InstallationStepIds.TargetInstallationVerified);
            Assert.True(verified.Armed);
            Assert.True(verified.Required);
            Assert.False(verified.Compensatable);
        }

        [Fact]
        public void VersionedScripts_IncludeStepFacingRecoveryAgent()
        {
            Assert.Contains(
                "DISARMED",
                ScriptCatalog.Read(ScriptCatalog.RecoveryAgent),
                StringComparison.Ordinal);
            Assert.True(File.Exists(ScriptCatalog.GetPath(ScriptCatalog.CompatibilityPreflight)));
        }

        /// <summary>
        /// Task 7.6 (change anterior) / task 9.6 (own-linux-installer): o catálogo de passos em
        /// C# é a única fonte de verdade. Qualquer citação literal de um id fora dele é uma
        /// cópia (constitution §3) — só aceitável coberta por teste de paridade.
        ///
        /// Com esta mudança a cópia deixou de ser hipotética: `live-media/` cita os cinco ids
        /// live/target de propósito (D3 — o lado Linux escreve no mesmo registro). Este teste
        /// não bane mais a citação; verifica que ela é EXATAMENTE a esperada — nenhum id
        /// desconhecido, e a ordem de `ledger_start_step`/`ledger_complete_step` nos scripts
        /// bate com a ordem do catálogo C#.
        /// </summary>
        [Fact]
        public void NoStepIdLiteralExistsOutsideTheCSharpCatalog()
        {
            string repoRoot = FindRepoRoot();
            var knownIds = new HashSet<string>(InstallationStepCatalog.All.Select(s => s.Id), StringComparer.Ordinal);

            string[] scanRoots =
            [
                Path.Combine(repoRoot, "Scripts"),
                Path.Combine(repoRoot, "live-media"),
            ];

            var unknownIdOffenders = new List<string>();
            // Lookaround em vez de \b: \b sozinho considera "-" e "." como fronteira de
            // palavra, então "configure-target.sh" (nome de arquivo) e ".disk.windows.number"
            // (caminho jq) davam falso positivo casando "target.sh" / "windows.number".
            var idPattern = new Regex(
                @"(?<![a-z0-9.-])(?:windows|live|target)\.[a-z0-9]+(?:-[a-z0-9]+)*(?![a-z0-9.-])",
                RegexOptions.Compiled);

            foreach (string dir in scanRoots.Where(Directory.Exists))
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string content = File.ReadAllText(file);
                    foreach (Match match in idPattern.Matches(content))
                    {
                        if (!knownIds.Contains(match.Value))
                            unknownIdOffenders.Add($"{Path.GetRelativePath(repoRoot, file)}: \"{match.Value}\" não existe em InstallationStepCatalog");
                    }
                }
            }

            Assert.True(
                unknownIdOffenders.Count == 0,
                "Id(s) de passo fora do catálogo C# encontrados em live-media/Scripts — ou é " +
                "erro de digitação, ou o catálogo precisa crescer antes da cópia:\n" +
                string.Join("\n", unknownIdOffenders));
        }

        /// <summary>
        /// Task 9.6: a ORDEM em que o lado live inicia/completa os passos precisa bater com a
        /// ordem do catálogo C#, não só os ids em si — um script que chama os passos fora de
        /// ordem quebraria <see cref="InstallationStateMachine.StartStep"/> do lado Windows ao
        /// ler o registro espelhado (ele exige a próxima id esperada).
        /// </summary>
        [Fact]
        public void LiveMediaScripts_InvokeStepsInTheSameOrderAsTheCatalog()
        {
            string repoRoot = FindRepoRoot();
            string libDir = Path.Combine(repoRoot, "live-media", "rootfs-overlay", "opt", "linuxhub", "lib");
            Assert.True(Directory.Exists(libDir), $"Diretório esperado não existe: {libDir}");

            string[] expectedOrder =
            [
                InstallationStepIds.LiveIsoMounted,
                InstallationStepIds.LiveDistributionExtracted,
                InstallationStepIds.TargetSystemConfigured,
                InstallationStepIds.TargetBootloaderInstalled,
                InstallationStepIds.TargetInstallationVerified,
            ];

            // run-installer.sh chama os scripts de fase nesta ordem física — concatenar o
            // conteúdo deles na mesma ordem reproduz a sequência real de execução.
            string[] phaseScriptsInOrder =
            [
                "verify-and-extract.sh", // live.iso-mounted, live.distribution-extracted
                "configure-target.sh",   // target.system-configured
                "install-bootloader.sh", // target.bootloader-installed
                "verify-installation.sh", // target.installation-verified
            ];

            var callPattern = new Regex(
                @"ledger_(?:start|complete)_step\s+""(?<id>[a-z0-9.-]+)""", RegexOptions.Compiled);

            var observedOrder = new List<string>();
            foreach (string scriptName in phaseScriptsInOrder)
            {
                string path = Path.Combine(libDir, scriptName);
                Assert.True(File.Exists(path), $"Script de fase esperado não existe: {path}");
                string content = File.ReadAllText(path);

                foreach (Match match in callPattern.Matches(content))
                {
                    string id = match.Groups["id"].Value;
                    if (!observedOrder.Contains(id))
                        observedOrder.Add(id);
                }
            }

            Assert.Equal(expectedOrder, observedOrder);
        }

        private static string FindRepoRoot()
        {
            string? cursor = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && cursor is not null; i++)
            {
                if (File.Exists(Path.Combine(cursor, "LinuxHub.csproj")))
                    return cursor;

                cursor = Directory.GetParent(cursor)?.FullName;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (LinuxHub.csproj) from " +
                AppContext.BaseDirectory);
        }
    }
}
