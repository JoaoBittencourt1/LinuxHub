using LinuxHub.Common.Models;

namespace LinuxHub.Common.Data
{
    /// <summary>
    /// Função pura que combina um documento remoto já verificado com o fallback embarcado —
    /// não é service (D18): sem I/O, sem ponto de variação, só transformação de dados.
    ///
    /// Uma entrada remota cujo Id não existe no fallback embarcado é ignorada, não criada: uma
    /// distro nova anunciada só pelo catálogo não tem <c>ImagePath</c>/<c>CarouselImages</c>
    /// compilados no executável (são recursos <c>pack://</c>), então o app não tem como exibi-la
    /// direito até um build novo trazer os assets dela. O catálogo remoto atualiza metadado,
    /// hash e link de distros que o executável já conhece — não substitui a necessidade de uma
    /// nova versão do app para uma distro genuinamente nova.
    /// </summary>
    internal static class CatalogMerge
    {
        /// <summary>Devolve null quando o documento tem qualquer entrada com enum inválido
        /// (<c>UnattendedInstall</c>/<c>LiveSession</c>) — o documento inteiro é malformado
        /// nesse caso, não só a entrada (mesma regra de "rejeitar por inteiro" do plano, D2).
        ///
        /// Também devolve null quando NENHUMA entrada casa com o fallback embarcado. Um
        /// documento assim é sintaticamente válido e pode até estar corretamente assinado —
        /// um catálogo de outra versão do produto, ou com um erro de digitação nos ids — mas
        /// descreve um catálogo que não é o deste app. Aceitá-lo substituiria a lista inteira
        /// de distros por uma vazia, deixando o usuário com uma tela sem nada e sem nenhum
        /// aviso, já que a assinatura conferiu.</summary>
        public static IReadOnlyList<DistroInfo>? Merge(RemoteCatalogDocument remote, IReadOnlyList<DistroInfo> localFallback)
        {
            var localById = localFallback.ToDictionary(distro => distro.Id);
            var merged = new List<DistroInfo>();

            foreach (var entry in remote.Distributions)
            {
                if (!localById.TryGetValue(entry.Id, out var local))
                    continue;

                if (!Enum.TryParse<UnattendedInstallMechanism>(entry.UnattendedInstall, out var unattendedInstall))
                    return null;

                if (!Enum.TryParse<LiveSessionFamily>(entry.LiveSession, out var liveSession))
                    return null;

                merged.Add(new DistroInfo
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Family = entry.Family,
                    Version = entry.Version,
                    CreatedYear = entry.CreatedYear,
                    BeginnerRating = entry.BeginnerRating,
                    IsTested = entry.IsTested,
                    IsEnabled = entry.IsEnabled,
                    UnattendedInstall = unattendedInstall,
                    LiveSession = liveSession,
                    DownloadLink = entry.DownloadLink,
                    DirectDownloadLink = entry.DirectDownloadLink,
                    Sha256 = entry.Sha256,
                    SizeBytes = entry.SizeBytes,
                    // Assets sempre locais — nunca vêm do documento remoto (D14): pack:// é
                    // recurso compilado no executável, não algo que um documento externo
                    // deveria conseguir apontar.
                    ImagePath = local.ImagePath,
                    CarouselImages = local.CarouselImages,
                });
            }

            return merged.Count > 0 ? merged : null;
        }
    }
}
