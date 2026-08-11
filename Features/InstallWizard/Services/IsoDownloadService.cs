using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using LinuxHub.Common.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    public sealed class IsoDownloadService : IIsoDownloadService
    {
        // Reportar a cada leitura de 8KB deixava a velocidade/ETA instáveis (cada
        // chunk media um intervalo de tempo minúsculo e ruidoso) e fazia a UI
        // repintar centenas de vezes por segundo. Amostrar num intervalo fixo dá
        // uma velocidade instantânea estável o bastante pra um ETA que não fica
        // pulando de forma errática.
        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(400);

        public async Task<string> DownloadAsync(DistroInfo distro, IProgress<IsoDownloadProgress> progress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(distro);

            // Guarda defensiva, não o gate principal: quem monta a UI já esconde o botão de
            // download para uma distro sem hash de referência (artifact-integrity spec). Chegar
            // aqui sem hash é um bug de chamador, não um caminho esperado.
            if (!distro.HasVerifiableArtifact)
            {
                throw new InvalidOperationException(
                    $"'{distro.Id}' has no reference SHA-256/size; automatic download is disabled for it.");
            }

            Directory.CreateDirectory(IsoStorage.BaseDirectory);

            string downloadPath = Path.Combine(IsoStorage.BaseDirectory, $"{distro.Id}.iso");

            try
            {
                using var client = new HttpClient();
                using var response = await client.GetAsync(
                    distro.DirectDownloadLink,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                // Alguns mirrors não enviam Content-Length (chunked transfer); nesse
                // caso não dá pra calcular percentual nem ETA — melhor reportar como
                // desconhecido do que fingir um total de 1 byte e gerar percentuais
                // e tempos restantes sem sentido.
                var totalBytes = response.Content.Headers.ContentLength;

                // Tamanho é conferido antes de escrever um byte sequer, quando o servidor
                // informa (D10): um mirror que já anuncia um tamanho diferente do catálogo
                // não vale a pena baixar por completo pra só então rejeitar.
                if (totalBytes is { } announcedBytes && announcedBytes != distro.SizeBytes)
                {
                    throw new ArtifactVerificationException(
                        ArtifactVerificationOutcome.SizeMismatch,
                        $"Server announced {announcedBytes} bytes for '{distro.Id}', catalog expects {distro.SizeBytes}.");
                }

                var buffer = new byte[65536];
                long totalRead = 0;

                var stopwatch = Stopwatch.StartNew();
                var lastReportElapsed = TimeSpan.Zero;
                long lastReportBytes = 0;

                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = File.Create(downloadPath))
                {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        // Calculado enquanto os bytes já passam pela memória, em vez de reler o
                        // arquivo depois — uma ISO de vários GB não ganha uma segunda passada
                        // inteira só para verificação (D10).
                        hasher.AppendData(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        var elapsed = stopwatch.Elapsed;
                        var sinceLastReport = elapsed - lastReportElapsed;
                        if (sinceLastReport < ReportInterval)
                            continue;

                        var bytesSinceLastReport = totalRead - lastReportBytes;
                        var speed = bytesSinceLastReport / sinceLastReport.TotalSeconds;

                        progress.Report(new IsoDownloadProgress(totalRead, totalBytes, speed));

                        lastReportElapsed = elapsed;
                        lastReportBytes = totalRead;
                    }
                }

                progress.Report(new IsoDownloadProgress(totalRead, totalBytes, 0));

                string actualSha256 = Convert.ToHexString(hasher.GetHashAndReset());
                var result = ArtifactVerificationClassifier.Classify(
                    totalRead, distro.SizeBytes, actualSha256, distro.Sha256);

                if (!result.IsVerified)
                {
                    File.Delete(downloadPath);
                    throw new ArtifactVerificationException(
                        result.Outcome,
                        $"Downloaded artifact for '{distro.Id}' failed verification ({result.Outcome}).");
                }

                return downloadPath;
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(downloadPath))
                    File.Delete(downloadPath);
                throw;
            }
        }
    }
}
