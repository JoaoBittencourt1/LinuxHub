namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Entrada de boot para a mídia live própria (design.md D0), usada apenas quando o par
    /// (<see cref="Common.Models.InstallMode.DualBoot"/>,
    /// <see cref="Common.Models.UnattendedInstallMechanism.OwnLiveInstaller"/>) está ativo
    /// (task 2.2). Deliberadamente FORA de <see cref="IIsoBootEntryBuilder"/>/
    /// <see cref="IIsoBootEntryBuilderRegistry"/>: aquele registro despacha por
    /// <see cref="Common.Models.LiveSessionFamily"/> — propriedade da ISO da DISTRO. A mídia
    /// live própria não é a ISO de uma distro, é o ambiente de execução do próprio app (D0), e
    /// o despacho aqui é por mecanismo (§2), resolvido diretamente em
    /// <see cref="BootStagingService"/> — nunca por família de live de terceiro.
    ///
    /// Ao contrário de <see cref="CasperIsoBootEntryBuilder"/>/<see cref="ArchisoIsoBootEntryBuilder"/>,
    /// não carrega nenhum parâmetro de kernel de instalação desatendida: o instalador live
    /// descobre tudo sozinho a partir do plano publicado no disco (D13) — não há autoinstall=
    /// nem preseed para injetar na linha de boot.
    /// </summary>
    public static class OwnLiveMediaBootEntryBuilder
    {
        /// <param name="liveMediaIsoWindowsPath">
        /// Caminho da mídia live COMO O WINDOWS o conhece (ex.:
        /// <c>C:\ProgramData\LinuxHub\LiveMedia\linuxhub-live.iso</c>). A conversão para o
        /// caminho que o GRUB entende é feita aqui, por
        /// <see cref="GrubConfigBuilder.ToGrubPath"/> — o GRUB não conhece letra de unidade e
        /// localiza o arquivo com <c>search --file</c>, relativo à raiz de cada volume.
        /// Emitir o caminho do Windows cru produziria
        /// <c>set isofile="C:\ProgramData\..."</c>, que o GRUB não resolve: a máquina
        /// reiniciaria num GRUB incapaz de achar a própria mídia.
        /// </param>
        public static string Build(string liveMediaIsoWindowsPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(liveMediaIsoWindowsPath);

            string isoGrubPath = GrubConfigBuilder.ToGrubPath(liveMediaIsoWindowsPath);

            string config = $@"menuentry ""LinuxHub - instalar"" {{
    insmod part_gpt
    insmod part_msdos
    insmod ntfs
    insmod loopback
    insmod iso9660
    set isofile=""{isoGrubPath}""
    search --no-floppy --file --set=root $isofile
    loopback loop $isofile
    linux (loop)/live/vmlinuz boot=live quiet nosplash
    initrd (loop)/live/initrd.img
}}
";

            // Mesma razão que GrubConfigBuilder documenta: literais verbatim num .cs salvo em
            // Windows carregam CRLF, e o GRUB é um parser de herança Unix — um '\r' fantasma
            // no fim do valor de $isofile faz o search --file procurar um arquivo que não
            // existe. Normalizar aqui também, senão a correção do outro builder não vale
            // para este caminho.
            return config.Replace("\r\n", "\n");
        }
    }
}
