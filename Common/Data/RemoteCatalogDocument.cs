namespace LinuxHub.Common.Data
{
    /// <summary>Forma do documento remoto assinado — espelha <c>schemas/distribution-catalog.schema.json</c>,
    /// que é a autoridade sobre a forma (D1/D2). Só os campos que fazem sentido vir de fora
    /// aparecem aqui: assets locais (<c>ImagePath</c>, <c>CarouselImages</c>) nunca vêm do
    /// documento remoto — são recursos <c>pack://</c> compilados no executável, e o merge
    /// (<see cref="CatalogMerge"/>) sempre os toma do fallback embarcado (D14).</summary>
    internal sealed class RemoteCatalogDocument
    {
        public int SchemaVersion { get; set; }
        public List<RemoteDistroEntry> Distributions { get; set; } = new();
    }

    internal sealed class RemoteDistroEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string CreatedYear { get; set; } = string.Empty;
        public int BeginnerRating { get; set; }
        public bool IsTested { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string UnattendedInstall { get; set; } = "None";
        public string LiveSession { get; set; } = "Casper";
        public string DownloadLink { get; set; } = string.Empty;
        public string DirectDownloadLink { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
