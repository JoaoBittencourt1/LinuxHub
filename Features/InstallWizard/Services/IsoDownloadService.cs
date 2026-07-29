using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
                var buffer = new byte[65536];
                long totalRead = 0;

                var stopwatch = Stopwatch.StartNew();
                var lastReportElapsed = TimeSpan.Zero;
                long lastReportBytes = 0;

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = File.Create(downloadPath);

                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
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

                progress.Report(new IsoDownloadProgress(totalRead, totalBytes, 0));

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
