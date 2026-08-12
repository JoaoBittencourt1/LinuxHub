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
        public static string Build(string liveMediaIsoGrubPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(liveMediaIsoGrubPath);

            return $@"menuentry ""LinuxHub - instalar"" {{
    insmod part_gpt
    insmod part_msdos
    insmod ntfs
    insmod loopback
    insmod iso9660
    set isofile=""{liveMediaIsoGrubPath}""
    search --no-floppy --file --set=root $isofile
    loopback loop $isofile
    linux (loop)/live/vmlinuz boot=live quiet nosplash
    initrd (loop)/live/initrd.img
}}
";
        }
    }
}
