namespace LinuxHub.Common.Data
{
    /// <summary>
    /// Chave pública embarcada usada para verificar a assinatura do catálogo remoto (D8).
    ///
    /// ATENÇÃO — chave de desenvolvimento, não de produção. Este par foi gerado só para esta
    /// mudança compilar e ter testes de verdade contra uma assinatura real; a chave privada
    /// correspondente não está neste repositório (não deveria estar em repositório nenhum) e é
    /// descartável. Antes de publicar qualquer catálogo assinado para usuários reais:
    ///
    /// 1. Gerar o par de produção com <c>Scripts/generate-catalog-signing-key.ps1</c>.
    /// 2. Substituir <see cref="Pem"/> pela chave pública gerada.
    /// 3. Guardar a chave privada só como secret do pipeline que assina o catálogo (task 2.2)
    ///    — nunca em disco de desenvolvedor, nunca commitada.
    ///
    /// Até isso acontecer, qualquer catálogo remoto assinado com uma chave diferente desta
    /// falha verificação e o app cai no fallback embarcado — comportamento seguro por padrão,
    /// não uma falha silenciosa.
    /// </summary>
    internal static class CatalogPublicKey
    {
        public const string Pem = """
            -----BEGIN PUBLIC KEY-----
            MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAtRTDT3r3QNDl40RzUjFd
            UWPX56cBATzecvv1WuTfSr0E/kAP11m8tlniWCue5UoFvkps5StoF01QIQLIv2TV
            ozwR6TB87/dPAkF6z56Y0bBRQYUIyOpSNLDi4KYAPAz8LPseHcWWjUwvt0AdFAJ9
            9njRpDOat0mp7RJWcppLGQjKg/ds6CwAitaPqE58oOt4+HALZLCxbTMjk6lID5Ss
            pA1ljahWRDEH61meZ6tkY6iwrxxtvInD23f9GIAiCUD/FMospkzgiabWhMdR5Lc0
            jj+iipwAt1gwH6F/Jsan8i3DKmuBFgUeAkHIFqnx7W6hhnkUJ3pCffeVGzyIjIoE
            jgs9mNe6Tm+1Z7sy0/2Uixa3yfbFxzajvPoSJ11NMyvtV9jVT3xNGoddXGiXoSOp
            XOyKbnBoxBHYvzKpWAtgkZKmHs+i/RDuIEId2vwhPDkEFpqONMClcVKcgS9kyPZ4
            9hjVUR1U6u2qCYAfwmspjr10iwRlf+YVEZ4g2vV5DNhdAgMBAAE=
            -----END PUBLIC KEY-----
            """;
    }
}
