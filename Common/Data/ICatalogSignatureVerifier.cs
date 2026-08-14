namespace LinuxHub.Common.Data
{
    /// <summary>Verifica a assinatura RSA destacada de um documento de catálogo contra a chave
    /// pública embarcada (D8). Não decide o que fazer com o resultado — <c>ICatalogClient</c> é
    /// quem descarta o documento inteiro numa falha.</summary>
    public interface ICatalogSignatureVerifier
    {
        bool Verify(byte[] documentBytes, byte[] signatureBytes);
    }
}
