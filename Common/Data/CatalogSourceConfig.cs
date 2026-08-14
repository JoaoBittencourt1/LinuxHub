namespace LinuxHub.Common.Data
{
    /// <summary>
    /// Resolve a base URL do catálogo por variável de ambiente, com um placeholder óbvio como
    /// padrão. Não existe host oficial do LinuxHub hoje — quem builda a release aponta para o
    /// próprio catálogo assinado via <c>LINUXHUB_CATALOG_BASE_URL</c>, por variável de ambiente
    /// em vez de argumento de linha de comando: este app ainda não tem parsing de CLI, e
    /// introduzi-lo só para isto seria escopo além do que esta mudança pede.
    ///
    /// Uma URL malformada ou não-HTTPS no override é rejeitada na inicialização — silenciosamente
    /// cair para o placeholder (que sempre falha) esconderia um erro de configuração atrás de um
    /// comportamento que parece "sem catálogo remoto configurado".
    /// </summary>
    public sealed class CatalogSourceConfig : ICatalogSourceConfig
    {
        public const string EnvironmentVariableName = "LINUXHUB_CATALOG_BASE_URL";

        // Não resolve para host nenhum de propósito — até um operador configurar o próprio
        // catálogo assinado (LINUXHUB_CATALOG_BASE_URL), o app opera inteiramente a partir do
        // fallback embarcado (DistroCatalog.Fallback), que é o comportamento seguro por padrão.
        internal const string PlaceholderBaseUrl = "https://catalog.invalid.example/linuxhub";

        public Uri CatalogDocumentUrl { get; }
        public Uri CatalogSignatureUrl { get; }

        public CatalogSourceConfig() : this(Environment.GetEnvironmentVariable(EnvironmentVariableName))
        {
        }

        internal CatalogSourceConfig(string? overrideBaseUrl)
        {
            string baseUrl = string.IsNullOrWhiteSpace(overrideBaseUrl) ? PlaceholderBaseUrl : overrideBaseUrl.Trim();

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException(
                    $"{EnvironmentVariableName} must be an absolute https URL; got '{overrideBaseUrl}'.",
                    nameof(overrideBaseUrl));
            }

            string trimmedPath = baseUri.ToString().TrimEnd('/');
            CatalogDocumentUrl = new Uri($"{trimmedPath}/catalog.json");
            CatalogSignatureUrl = new Uri($"{trimmedPath}/catalog.json.sig");
        }
    }
}
