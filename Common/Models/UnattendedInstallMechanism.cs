namespace LinuxHub.Common.Models
{
    /// <summary>
    /// Qual mecanismo de instalação desatendida o instalador nativo de uma build de distro
    /// usa. Não é inferível da família: Ubuntu e Mint são ambos Debian no catálogo e usam
    /// mecanismos diferentes, e o mecanismo é propriedade da *build*, não da distro — o
    /// próprio Ubuntu usou Ubiquity até a 23.04 e subiquity depois dela.
    ///
    /// Os formatos são incompatíveis entre si em conteúdo, transporte e modelo de
    /// particionamento, então saber apenas que "existe automação" não basta: o gerador
    /// precisa saber qual.
    /// </summary>
    public enum UnattendedInstallMechanism
    {
        /// <summary>Nenhum mecanismo validado de ponta a ponta. Padrão de qualquer distro
        /// recém-adicionada: o wizard só prepara o boot até o instalador nativo da ISO, e o
        /// resto da instalação fica por conta do usuário.</summary>
        None = 0,

        /// <summary>Instaladores subiquity (Ubuntu 23.04+): YAML <c>autoinstall:</c> entregue
        /// como <c>user-data</c> de cloud-init numa partição NoCloud, particionamento no
        /// formato curtin, ativado pelo parâmetro <c>autoinstall</c> na linha de kernel.</summary>
        Subiquity,

        /// <summary>Instaladores Ubiquity (Linux Mint 22.x, Ubuntu até a 23.04): preseed
        /// debconf, particionamento por receita <c>partman</c>, ativado pelo parâmetro
        /// <c>automatic-ubiquity</c> na linha de kernel.</summary>
        UbiquityPreseed,
    }
}
