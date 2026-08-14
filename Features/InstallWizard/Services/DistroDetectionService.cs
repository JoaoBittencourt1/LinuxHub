using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LinuxHub.Common.Data;
using LinuxHub.Common.Localization;
using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class DistroDetectionService : IDistroDetectionService
    {
        /// <summary>Id sentinela da distro "desconhecida" — quem chama usa isso pra decidir
        /// se bloqueia o fluxo (ver <see cref="ViewModels.IsoAcquisitionViewModel"/>).</summary>
        public const string UnknownDistroId = "unknown";

        private static readonly Regex DigitRun = new(@"\d+", RegexOptions.Compiled);

        public DistroDetectionResult Detect(string isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath) || !File.Exists(isoPath))
                return new DistroDetectionResult(Unknown(), IsExpectedVersion: true);

            var fileName = Path.GetFileName(isoPath);
            var distro = DistroCatalog.FindByIsoFileName(fileName);

            return distro is null
                ? new DistroDetectionResult(Unknown(), IsExpectedVersion: true)
                : new DistroDetectionResult(distro, MatchesExpectedVersion(fileName, distro));
        }

        /// <summary>Só faz diferença pra quem promete autoinstall — pras demais distros o
        /// wizard já não oferece essa opção de jeito nenhum, então uma versão "diferente" aqui
        /// não muda nada no fluxo. Sem número nenhum reconhecível no nome do arquivo, dá o
        /// benefício da dúvida: a maioria das ISOs baixadas pelo próprio catálogo não deveria
        /// ficar marcada como incerta só por causa do nome do arquivo.</summary>
        private static bool MatchesExpectedVersion(string fileName, DistroInfo distro)
        {
            if (!distro.SupportsUnattendedInstall)
                return true;

            int expectedMajorVersion = DigitRun.Matches(distro.Version)
                .Select(m => int.Parse(m.Value))
                .FirstOrDefault();

            if (expectedMajorVersion == 0)
                return true;

            var foundNumbers = DigitRun.Matches(fileName).Select(m => int.Parse(m.Value)).ToList();
            return foundNumbers.Count == 0 || foundNumbers.Contains(expectedMajorVersion);
        }

        private static DistroInfo Unknown() => new()
        {
            Id = UnknownDistroId,
            Name = LocalizationManager.Instance["Distro_UnknownName"],
            // Nota: este asset não existe no projeto hoje (pré-existente ao refactor) —
            // escolher um placeholder real é uma decisão de design fora deste change.
            ImagePath = "pack://application:,,,/Assets/Images/unknown.png"
        };
    }
}
