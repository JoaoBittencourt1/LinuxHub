using System.Text.Json;
using System.Text.Json.Nodes;
using LinuxHub.Common.Models;

namespace LinuxHub.Common.Data
{
    /// <summary>
    /// Produz o documento de catálogo assinável a partir do catálogo embarcado — o lado
    /// "escrita" da forma que <see cref="RemoteCatalogDocument"/>/<see cref="CatalogClient"/>
    /// consomem do lado "leitura". <see cref="schemas/distribution-catalog.schema.json"/> é a
    /// autoridade sobre a forma; este escritor existe para que ela nunca seja reimplementada à
    /// mão (nem em PowerShell, nem copiada) — só chamada.
    ///
    /// Função pura (D18): sem I/O, sem rede, sem assinatura. Quem assina é o pipeline de
    /// release, fora deste processo (task 2.2/7.7) — a chave privada nunca precisa existir onde
    /// este código roda.
    /// </summary>
    public static class CatalogDocumentWriter
    {
        public static string BuildJson(IReadOnlyList<DistroInfo> distros)
        {
            ArgumentNullException.ThrowIfNull(distros);

            var distributions = new JsonArray();
            foreach (DistroInfo distro in distros)
            {
                var entry = new JsonObject
                {
                    ["id"] = distro.Id,
                    ["name"] = distro.Name,
                    ["family"] = distro.Family,
                    ["version"] = distro.Version,
                    ["createdYear"] = distro.CreatedYear,
                    ["beginnerRating"] = distro.BeginnerRating,
                    ["isTested"] = distro.IsTested,
                    ["isEnabled"] = distro.IsEnabled,
                    ["unattendedInstall"] = distro.UnattendedInstall.ToString(),
                    ["liveSession"] = distro.LiveSession.ToString(),
                    ["downloadLink"] = distro.DownloadLink,
                    ["directDownloadLink"] = distro.DirectDownloadLink,
                };

                // O schema amarra sha256/sizeBytes com dependentRequired: os dois juntos ou
                // nenhum — nunca "sha256": "" (falha o pattern) nem "sizeBytes": 0 (falha o
                // minimum). HasVerifiableArtifact já é a mesma regra que o app usa para decidir
                // se oferece download automático (DistroInfo).
                if (distro.HasVerifiableArtifact)
                {
                    entry["sha256"] = distro.Sha256;
                    entry["sizeBytes"] = distro.SizeBytes;
                }

                distributions.Add(entry);
            }

            var document = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["distributions"] = distributions,
            };

            return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
