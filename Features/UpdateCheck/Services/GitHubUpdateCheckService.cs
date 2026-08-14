using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LinuxHub.Features.UpdateCheck.Services
{
    /// <summary>
    /// Lê a última release publicada pela API pública do GitHub, sem autenticação.
    ///
    /// Nenhuma exceção é capturada aqui: quem decide o que fazer com a falha é o ponto de
    /// orquestração no startup, que a registra no log de rede e segue. Engolir a exceção
    /// aqui violaria constitution §4 e apagaria a única evidência de que o recurso quebrou.
    /// </summary>
    internal sealed class GitHubUpdateCheckService : IUpdateCheckService
    {
        /// <summary>
        /// O endpoint <c>/releases/latest</c> já exclui rascunhos e pré-lançamentos — não é
        /// preciso filtrar depois.
        /// </summary>
        public const string LatestReleaseUrl =
            "https://api.github.com/repos/joaobittencourt1/linuxbit/releases/latest";

        /// <summary>
        /// O default do HttpClient é 100s — tempo demais para algo que roda na abertura do app.
        /// </summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly HttpClient _httpClient;

        public GitHubUpdateCheckService()
            : this(new HttpClient())
        {
        }

        internal GitHubUpdateCheckService(HttpClient httpClient)
        {
            ArgumentNullException.ThrowIfNull(httpClient);

            _httpClient = httpClient;
            _httpClient.Timeout = RequestTimeout;

            // A API do GitHub responde 403 para requisição SEM User-Agent. Sem este header o
            // recurso falharia em 100% das execuções — e, como a falha é invisível por decisão
            // de produto, falharia sem nenhum sintoma perceptível. Não remover.
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("LinuxHub", "1.0"));

            // Fixa a versão do formato da resposta; sem isso o GitHub pode servir outro schema.
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        public async Task<LatestRelease> GetLatestReleaseAsync(
            CancellationToken cancellationToken = default)
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            GitHubRelease? release = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (release is null)
                throw new InvalidOperationException("Resposta da API de releases veio vazia.");

            if (!ReleaseVersionParser.TryParseTag(release.TagName, out Version version))
            {
                throw new InvalidOperationException(
                    $"Tag de release fora do formato vX.Y.Z: '{release.TagName}'.");
            }

            if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? url))
                throw new InvalidOperationException($"URL de release inválida: '{release.HtmlUrl}'.");

            return new LatestRelease(version, url);
        }

        private sealed record GitHubRelease(
            [property: JsonPropertyName("tag_name")] string? TagName,
            [property: JsonPropertyName("html_url")] string? HtmlUrl);
    }
}
