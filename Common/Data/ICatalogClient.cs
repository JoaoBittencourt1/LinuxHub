using LinuxHub.Common.Models;

namespace LinuxHub.Common.Data
{
    public enum CatalogFetchOutcome
    {
        Verified,
        SignatureInvalid,
        NetworkUnavailable,
        MalformedDocument,
    }

    public readonly record struct CatalogFetchResult(IReadOnlyList<DistroInfo>? Distros, CatalogFetchOutcome Outcome)
    {
        public bool IsVerified => Outcome == CatalogFetchOutcome.Verified;
    }

    /// <summary>Obtém, verifica e mescla o catálogo remoto assinado. Nunca lança para os casos
    /// previstos de falha (assinatura inválida, rede indisponível, documento malformado) — cada
    /// um vira um <see cref="CatalogFetchOutcome"/> distinto, e <see cref="CatalogFetchResult.Distros"/>
    /// só é preenchido quando <see cref="CatalogFetchResult.IsVerified"/>. Quem chama decide o
    /// fallback (artifact-integrity/distro-catalog spec).</summary>
    public interface ICatalogClient
    {
        Task<CatalogFetchResult> FetchAsync(CancellationToken cancellationToken);
    }
}
