using System.Net.Http;
using System.Text.Json;

namespace LinuxHub.Common.Data
{
    public sealed class CatalogClient : ICatalogClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly ICatalogSourceConfig _sourceConfig;
        private readonly ICatalogSignatureVerifier _signatureVerifier;
        private readonly IReadOnlyList<Models.DistroInfo> _localFallback;
        private readonly Func<HttpClient> _httpClientFactory;

        public CatalogClient(
            ICatalogSourceConfig sourceConfig,
            ICatalogSignatureVerifier signatureVerifier,
            IReadOnlyList<Models.DistroInfo> localFallback)
            : this(sourceConfig, signatureVerifier, localFallback, () => new HttpClient())
        {
        }

        /// <summary>Permite injetar um <see cref="HttpMessageHandler"/> de teste sem tocar rede
        /// de verdade — a assinatura e o merge são a parte crítica de segurança deste service, e
        /// precisam de cobertura contra respostas reais (assinatura inválida, JSON malformado,
        /// falha de rede), não só contra as funções puras que eles chamam.</summary>
        internal CatalogClient(
            ICatalogSourceConfig sourceConfig,
            ICatalogSignatureVerifier signatureVerifier,
            IReadOnlyList<Models.DistroInfo> localFallback,
            Func<HttpClient> httpClientFactory)
        {
            _sourceConfig = sourceConfig ?? throw new ArgumentNullException(nameof(sourceConfig));
            _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
            _localFallback = localFallback ?? throw new ArgumentNullException(nameof(localFallback));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<CatalogFetchResult> FetchAsync(CancellationToken cancellationToken)
        {
            byte[] documentBytes;
            byte[] signatureBytes;

            try
            {
                using var client = _httpClientFactory();
                documentBytes = await client.GetByteArrayAsync(_sourceConfig.CatalogDocumentUrl, cancellationToken);
                string signatureBase64 = await client.GetStringAsync(_sourceConfig.CatalogSignatureUrl, cancellationToken);
                signatureBytes = Convert.FromBase64String(signatureBase64.Trim());
            }
            catch (Exception ex) when (ex is HttpRequestException or FormatException or TaskCanceledException)
            {
                // TaskCanceledException aqui cobre tanto timeout de rede quanto o cancellationToken
                // limitado que App.xaml.cs usa para não travar a inicialização — os dois convergem
                // para o mesmo resultado: seguir com o fallback embarcado.
                return new CatalogFetchResult(null, CatalogFetchOutcome.NetworkUnavailable);
            }

            // Assinatura verificada antes de qualquer parsing: um documento cuja assinatura não
            // bate é descartado por inteiro, nunca inspecionado campo a campo (spec artifact-integrity,
            // "Catálogo com assinatura inválida").
            if (!_signatureVerifier.Verify(documentBytes, signatureBytes))
                return new CatalogFetchResult(null, CatalogFetchOutcome.SignatureInvalid);

            RemoteCatalogDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<RemoteCatalogDocument>(documentBytes, JsonOptions);
            }
            catch (JsonException)
            {
                return new CatalogFetchResult(null, CatalogFetchOutcome.MalformedDocument);
            }

            if (document is null || document.SchemaVersion != 1)
                return new CatalogFetchResult(null, CatalogFetchOutcome.MalformedDocument);

            var merged = CatalogMerge.Merge(document, _localFallback);
            if (merged is null)
                return new CatalogFetchResult(null, CatalogFetchOutcome.MalformedDocument);

            return new CatalogFetchResult(merged, CatalogFetchOutcome.Verified);
        }
    }
}
