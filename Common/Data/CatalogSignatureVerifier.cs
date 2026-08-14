using System.Security.Cryptography;

namespace LinuxHub.Common.Data
{
    public sealed class CatalogSignatureVerifier : ICatalogSignatureVerifier, IDisposable
    {
        private readonly RSA _publicKey;

        public CatalogSignatureVerifier() : this(CatalogPublicKey.Pem)
        {
        }

        internal CatalogSignatureVerifier(string publicKeyPem)
        {
            _publicKey = RSA.Create();
            _publicKey.ImportFromPem(publicKeyPem);
        }

        public bool Verify(byte[] documentBytes, byte[] signatureBytes)
        {
            ArgumentNullException.ThrowIfNull(documentBytes);
            ArgumentNullException.ThrowIfNull(signatureBytes);

            // RSASSA-PKCS1-v1_5 — verificação síncrona e sem segredo nenhum do lado de quem
            // verifica: só a chave pública embarcada é usada.
            return _publicKey.VerifyData(documentBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        public void Dispose() => _publicKey.Dispose();
    }
}
