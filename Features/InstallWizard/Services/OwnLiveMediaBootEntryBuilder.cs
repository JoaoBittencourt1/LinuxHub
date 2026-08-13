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
    /// o despacho aqui é por mecanismo (§2), nunca por família de live de terceiro.
    ///
    /// O kernel é carregado DIRETO de uma partição FAT32 preparada por
    /// <see cref="LiveMediaStagingService"/> — sem <c>loopback</c>, sem <c>findiso=</c>, sem
    /// montar NTFS no initramfs. A versão anterior deixava a ISO como arquivo no volume do
    /// Windows e pedia ao GRUB que a montasse em laço; isso obrigava o <c>live-boot</c> a
    /// percorrer, de dentro do initramfs, uma cadeia inteira (varrer dispositivos → montar
    /// NTFS por FUSE → localizar o arquivo → criar dispositivo de laço → montar iso9660 →
    /// achar o squashfs) antes de existir sistema nenhum. Cada elo dessa cadeia é uma forma de
    /// o boot morrer antes de qualquer diagnóstico, e foi o que aconteceu repetidamente em VM.
    ///
    /// Ao contrário de <see cref="CasperIsoBootEntryBuilder"/>/<see cref="ArchisoIsoBootEntryBuilder"/>,
    /// não carrega parâmetro de instalação desatendida: o instalador live descobre tudo a
    /// partir do plano publicado no disco (D13) — não há autoinstall= nem preseed na linha de
    /// boot.
    /// </summary>
    public static class OwnLiveMediaBootEntryBuilder
    {
        public static string Build()
        {
            // `search --file` porque o GRUB pré-compilado embarca `search_fs_file`, e não
            // `search_label`/`search_fs_uuid` (ver Assets/Grub/README.md). Procurar pelo kernel
            // encontra a partição da mídia live sem depender de número de partição, que muda
            // conforme o disco.
            //
            // `toram`: copia a mídia para a RAM e SOLTA o dispositivo de origem. É o mesmo
            // problema que D0 existe para eliminar — um ambiente live segurando a partição do
            // disco em que se vai escrever — e sem isto a mídia própria o reintroduziria por
            // outro caminho.
            //
            // `console=tty1` explícito: sem ele a saída pode ir parar num console que ninguém
            // vê, e um instalador invisível é indistinguível de um instalador travado.
            //
            // Sem `quiet` enquanto o mecanismo não passar a fase 11: as mensagens do live-boot
            // e do systemd são a única forma de saber onde um boot parou.
            string config = @"set default=0
set timeout=0

menuentry ""LinuxHub - instalar"" {
    insmod part_gpt
    insmod part_msdos
    insmod fat
    search --no-floppy --file --set=root /live/vmlinuz
    linux /live/vmlinuz boot=live toram components console=tty1 systemd.show_status=true
    initrd /live/initrd.img
}
";

            // GRUB é parser de herança Unix, e literais verbatim num .cs salvo em Windows
            // carregam CRLF: um '\r' fantasma no fim de uma linha faz o GRUB falhar o parse ou
            // procurar um caminho que não existe.
            return config.Replace("\r\n", "\n");
        }
    }
}
