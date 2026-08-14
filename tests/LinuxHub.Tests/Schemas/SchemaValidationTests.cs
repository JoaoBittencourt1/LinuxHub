using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using LinuxHub.Common.Data;
using LinuxHub.Common.Models;
using LinuxHub.Features.InstallWizard.Models;
using LinuxHub.Features.InstallWizard.Services;
using LinuxHub.Tests.Features.InstallWizard.Services;
using Xunit;

namespace LinuxHub.Tests.Schemas
{
    /// <summary>
    /// Task 7.4: valida os três schemas versionados e um documento de exemplo real (não um
    /// literal JSON escrito à mão, que provaria só que o teste concorda consigo mesmo) contra
    /// cada um. É este teste que teria pego a divergência já corrigida entre
    /// <c>UnattendedInstallMechanism.UbiquityPreseed</c> e o antigo enum do schema
    /// (<c>"Ubiquity"</c>) — um documento que o C# de fato produz, batendo contra o schema que
    /// é a autoridade de forma (D1/D2).
    /// </summary>
    public class SchemaValidationTests
    {
        [Theory]
        [InlineData("distribution-catalog.schema.json")]
        [InlineData("installation-plan.schema.json")]
        [InlineData("installation-state.schema.json")]
        public void Schema_IsWellFormedAndLoadable(string fileName)
        {
            string path = ResolveSchemaPath(fileName);
            Assert.True(File.Exists(path), $"Schema not found at {path}");

            // FromText valida a sintaxe e as palavras-chave do próprio schema; um schema
            // sintaticamente quebrado (chave duplicada, $ref pendurado, tipo inválido) lança
            // aqui, antes de qualquer documento ser avaliado contra ele.
            JsonSchema schema = JsonSchema.FromText(File.ReadAllText(path));

            // Evaluate contra um documento trivial garante que o schema resolve por inteiro
            // (incluindo $defs/$ref internos) sem lançar em tempo de avaliação.
            EvaluationResults smokeTest = schema.Evaluate(JsonNode.Parse("{}"));
            Assert.NotNull(smokeTest);
        }

        [Fact]
        public void DistributionCatalogExample_MatchesTheSchema()
        {
            // O documento vem do MESMO escritor que o pipeline de release usa (task 7.7) — não
            // um JSON escrito à mão no teste, que só provaria que o teste concorda consigo
            // mesmo.
            string json = CatalogDocumentWriter.BuildJson(DistroCatalog.Fallback);

            AssertValidAgainstSchema("distribution-catalog.schema.json", json);
        }

        [Fact]
        public void InstallationPlanExample_MatchesTheSchema()
        {
            InstallationPlan plan = InstallationPlanValidatorTests.ValidUefiPlan();
            string json = JsonSerializer.Serialize(plan, InstallationPlanPublisher.SerializerOptions);

            AssertValidAgainstSchema("installation-plan.schema.json", json);
        }

        [Fact]
        public void InstallationStateExample_MatchesTheSchema()
        {
            InstallationStateMachine machine = InstallationStateMachine.Create(new string('a', 32));
            machine.StartStep(InstallationStepIds.WindowsPlanPublished);

            string json = JsonSerializer.Serialize(machine.State, InstallationExecutionLedger.SerializerOptions);

            AssertValidAgainstSchema("installation-state.schema.json", json);
        }

        private static void AssertValidAgainstSchema(string schemaFileName, string documentJson)
        {
            JsonSchema schema = JsonSchema.FromText(File.ReadAllText(ResolveSchemaPath(schemaFileName)));
            JsonNode? document = JsonNode.Parse(documentJson);

            EvaluationResults result = schema.Evaluate(
                document,
                new EvaluationOptions { OutputFormat = OutputFormat.List });

            Assert.True(
                result.IsValid,
                $"Documento gerado pelo C# não bate com {schemaFileName}:\n" +
                string.Join(
                    "\n",
                    result.Details
                        .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
                        .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} — {e.Value}"))));
        }

        /// <summary>Mesmo padrão de resolução usado em <c>InstallationPlanSchemaParityTests</c>:
        /// caminho relativo ao <c>bin/</c> de teste primeiro, diretório de trabalho como
        /// segunda tentativa (alguns runners de CI iniciam de outro cwd).</summary>
        private static string ResolveSchemaPath(string fileName)
        {
            string fromBinDir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schemas", fileName));

            if (File.Exists(fromBinDir))
                return fromBinDir;

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "schemas", fileName));
        }
    }
}
