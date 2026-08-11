namespace LinuxHub.Common.Data
{
    /// <summary>Onde buscar o documento de catálogo assinado e sua assinatura destacada. Nunca
    /// hardcoded — quem hospeda o catálogo é decisão operacional de quem opera o build, não do
    /// código-fonte (ver design.md, D14).</summary>
    public interface ICatalogSourceConfig
    {
        Uri CatalogDocumentUrl { get; }
        Uri CatalogSignatureUrl { get; }
    }
}
