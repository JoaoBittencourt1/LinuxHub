namespace LinuxHub.Features.InstallWizard.Models
{
    /// <summary>
    /// Um valor regional (fuso, teclado) derivado da configuração do Windows, junto com a
    /// informação de se ele foi de fato reconhecido ou se é o padrão declarado do mapeamento.
    ///
    /// A distinção existe porque o defeito que originou este tipo era justamente um valor
    /// arbitrário que não se anunciava: <c>"us"</c> chumbado acertava para quem por acaso
    /// coincidia com ele e errava calado para todos os demais. Um padrão que chega marcado
    /// como padrão pode ser sinalizado na tela e corrigido pelo usuário; um valor solto, não.
    /// </summary>
    public sealed record DetectedRegionalSetting(string Value, bool IsFallback)
    {
        public static DetectedRegionalSetting Detected(string value) => new(value, IsFallback: false);

        public static DetectedRegionalSetting Fallback(string value) => new(value, IsFallback: true);
    }
}
