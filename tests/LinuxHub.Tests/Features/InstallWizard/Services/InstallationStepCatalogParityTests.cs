using System.IO;
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
            ];

            Assert.Equal(expectedIds, InstallationStepCatalog.All.Select(s => s.Id).ToArray());

            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.WindowsDiskPrepared).Compensatable);
            Assert.False(InstallationStepCatalog.Get(InstallationStepIds.WindowsPlanPublished).Compensatable);
            Assert.False(InstallationStepCatalog.Get(InstallationStepIds.LiveIsoMounted).Armed);
            Assert.True(InstallationStepCatalog.Get(InstallationStepIds.WindowsTemporaryBootPrepared).Armed);
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
        /// Task 7.6 / D3: o catálogo de passos em C# é a única fonte de verdade hoje — os
        /// passos `live.*`/`target.*` estão desarmados justamente porque uma cópia em script
        /// ainda não existe. `design.md` aceita essa duplicação como desvio deliberado da §3,
        /// com uma condição: "nenhuma cópia nova entra sem entrar no teste".
        ///
        /// Este teste é a parte automática dessa condição. Ele varre `Scripts/` e `installer/`
        /// atrás dos ids literais do catálogo — se um deles aparecer em um arquivo que este
        /// teste não conhece, é sinal de que alguém começou a espelhar o catálogo num script
        /// sem estender a comparação de paridade para cobri-lo. Falhar aqui força a decisão a
        /// ser tomada de propósito, em vez de a cópia divergir em silêncio.
        /// </summary>
        [Fact]
        public void NoStepIdLiteralExistsOutsideTheCSharpCatalog()
        {
            string repoRoot = FindRepoRoot();
            string[] stepIds =
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
            ];

            string[] scanRoots =
            [
                Path.Combine(repoRoot, "Scripts"),
                Path.Combine(repoRoot, "installer"),
            ];

            var offenders = new List<string>();
            foreach (string dir in scanRoots.Where(Directory.Exists))
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string content = File.ReadAllText(file);
                    foreach (string id in stepIds)
                    {
                        if (content.Contains(id, StringComparison.Ordinal))
                            offenders.Add($"{Path.GetRelativePath(repoRoot, file)}: contém \"{id}\"");
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                "Um ou mais arquivos fora do C# citam ids do catálogo de passos instalação — " +
                "isto é uma cópia da mesma verdade (constitution §3, aceito como desvio no " +
                "design.md D3 apenas quando coberta por teste de paridade). Estenda " +
                "InstallationStepCatalogParityTests para comparar a cópia encontrada, em vez " +
                "de deixá-la divergir sem verificação:\n" + string.Join("\n", offenders));
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
