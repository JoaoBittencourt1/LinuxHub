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

        /// <summary>
        /// <c>archinstall</c> (Arch Linux): configuração JSON entregue à sessão live pelo
        /// parâmetro de boot <c>script=</c>, particionamento por caminho de dispositivo.
        ///
        /// É o único mecanismo em que o app não gera a configuração final: o
        /// <c>disk_config</c> endereça partições por caminho de kernel (<c>/dev/nvme0n1p3</c>),
        /// que não existe do lado do Windows, então o app gera um script que resolve os
        /// PARTUUIDs e emite o JSON já dentro da sessão live (design.md, decisão 9).
        /// </summary>
        Archinstall,

        /// <summary>
        /// Instalador próprio (openspec/changes/own-linux-installer, decisões D0/D1): extrai o
        /// filesystem que a ISO live já carrega para a partição alvo, num ambiente de execução
        /// próprio (mídia live do LinuxHub), em vez de delegar ao instalador nativo da distro.
        /// Bash fixo e versionado, lendo o plano publicado — não gerado por instalação.
        ///
        /// Task 2.1: só o valor existe aqui. Nenhuma distro o declara no catálogo até a fase 11
        /// (validação em VM) passar — §7.1, mesmo padrão de <c>InstallationSafetySwitches</c>.
        /// Escopo UEFI apenas (D16): <see cref="UnattendedInstallMechanismExtensions.RequiresUefi"/>.
        /// </summary>
        OwnLiveInstaller,
    }

    public static class UnattendedInstallMechanismExtensions
    {
        /// <summary>
        /// Se o mecanismo permite escolher o ambiente gráfico da instalação. A pergunta é sobre
        /// o MECANISMO, nunca sobre a identidade da distro (§2): a maioria das ISOs do catálogo
        /// já embute um ambiente, e oferecer uma escolha que seria ignorada promete ao usuário
        /// um controle que ele não tem.
        /// </summary>
        public static bool SupportsDesktopEnvironmentChoice(this UnattendedInstallMechanism mechanism) =>
            mechanism == UnattendedInstallMechanism.Archinstall;

        /// <summary>
        /// Task 2.4 (design.md D16): a mídia live própria é UEFI apenas. Nenhuma tarefa de BIOS
        /// legado para este mecanismo — dual-boot manual e modo substituir continuam cobrindo
        /// BIOS legado pelos caminhos preservados.
        /// </summary>
        public static bool RequiresUefi(this UnattendedInstallMechanism mechanism) =>
            mechanism == UnattendedInstallMechanism.OwnLiveInstaller;
    }
}
