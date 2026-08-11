namespace LinuxHub.Common.Models
{
    public class DistroInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string CreatedYear { get; set; } = string.Empty;

        /// <summary>Quão recomendada a distro é para iniciantes, de 1 (nada) a 5 (muito).</summary>
        public int BeginnerRating { get; set; }

        /// <summary>
        /// Se o fluxo do app já foi exercitado de verdade nesta distro — baixar a ISO, montar
        /// o boot pela staging e chegar ao instalador dela. Nada disso é genérico: cada distro
        /// empacota o initrd com um nome diferente, boota o live de um jeito diferente e traz
        /// um instalador diferente. As não marcadas aqui não estão quebradas por definição,
        /// só nunca foram testadas — e o usuário precisa saber disso antes de reiniciar a
        /// máquina.
        ///
        /// É independente de <see cref="UnattendedInstall"/>: o Mint boota (testado) mas ainda
        /// não tem instalação desatendida validada. Testar o boot é pré-requisito de declarar
        /// mecanismo, nunca o contrário.
        /// </summary>
        public bool IsTested { get; set; }

        /// <summary>Mecanismo de instalação desatendida desta build — <see
        /// cref="UnattendedInstallMechanism.None"/> (o padrão) para toda build que não foi
        /// validada de ponta a ponta. Para essas, o wizard só prepara o boot até o instalador
        /// nativo da própria ISO: nenhum schema de instalação desatendida tem garantia de
        /// compatibilidade entre distros/versões, e nem todas usam o mesmo mecanismo.</summary>
        public UnattendedInstallMechanism UnattendedInstall { get; set; }

        /// <summary>SHA-256 do artefato de download, obtido da fonte oficial da distro — nunca
        /// calculado a partir de um arquivo já baixado (isso só provaria consistência consigo
        /// mesmo, não que o arquivo é o que deveria ser). Vazio para uma entrada cuja fonte não
        /// publica SHA-256 (ver <see cref="HasVerifiableArtifact"/>): download automático fica
        /// desligado para ela até que publique.</summary>
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>Tamanho esperado do artefato em bytes, da mesma fonte que <see cref="Sha256"/>.
        /// Conferido antes do hash — um arquivo de tamanho errado é rejeitado sem gastar tempo
        /// calculando SHA-256 de um artefato já sabido divergente.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Se esta build tem identidade criptográfica de referência. Sem ela, nem
        /// download automático nem instalação desatendida podem ser oferecidos — ver
        /// <see cref="SupportsUnattendedInstall"/>.</summary>
        public bool HasVerifiableArtifact => !string.IsNullOrWhiteSpace(Sha256) && SizeBytes > 0;

        /// <summary>Se esta entrada é exibida no catálogo e oferecida no seletor de download do
        /// wizard. Uma entrada desabilitada permanece no código — com seu comentário explicando
        /// o motivo — em vez de ser apagada: "temporariamente fora do ar" e "nunca existiu" são
        /// coisas diferentes, e apagar perderia o trabalho de levantar versão/hash/link quando o
        /// motivo for resolvido. Detecção por nome de arquivo (seleção manual) continua
        /// funcionando para uma entrada desabilitada — este flag não é sobre confiança do
        /// artefato, é só sobre o que é oferecido de cara.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Atalho de leitura para "esta build tem algum mecanismo validado". Exige
        /// também <see cref="HasVerifiableArtifact"/>: automação desatendida sobre um artefato
        /// que o app não consegue verificar seria automação sobre um arquivo não confiável —
        /// mesmo problema de fundo que o §6.1 da constitution já trata para particionamento.</summary>
        public bool SupportsUnattendedInstall => UnattendedInstall != UnattendedInstallMechanism.None && HasVerifiableArtifact;

        /// <summary>Como esta build monta a sessão live, o que decide a entrada de boot usada
        /// para iniciá-la a partir de um arquivo. O padrão é <see cref="LiveSessionFamily.Casper"/>
        /// porque era a única forma que o app sabia gerar até aqui — declarar errado não é um
        /// erro visível no Windows, é uma máquina que reinicia e não acha o kernel.</summary>
        public LiveSessionFamily LiveSession { get; set; }

        // Texto de Description/Maintainer nunca é hardcoded aqui — são chaves de recurso
        // (ver constitution.md, "Nenhuma string hardcoded"), resolvidas via
        // LocalizationManager para poderem ser traduzidas e trocar de idioma em runtime.
        public string DescriptionKey => $"Distro_{Id}_Description";
        public string MaintainerKey => $"Distro_{Id}_Maintainer";
        public string ImagePath { get; set; } = string.Empty;
        public string DownloadLink { get; set; } = string.Empty;
        public string DirectDownloadLink { get; set; } = string.Empty;
        public string[] CarouselImages { get; set; } = Array.Empty<string>();
    }
}
