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
    ///
    /// UM parâmetro de kernel é necessário mesmo assim: <c>findiso=</c>. O GRUB acha o arquivo
    /// na NTFS sozinho (<c>search --file</c>) e dá chainload no kernel de dentro do loopback —
    /// mas isso só entrega o KERNEL. O <c>live-boot</c>, já rodando como processo Linux dentro
    /// do initramfs, tem sua própria busca pelo sistema live (função <c>find_livefs</c> em
    /// <c>/lib/live/boot/9990-misc-helpers.sh</c>), e por padrão só varre mídia óptica/USB —
    /// não sabe que existe um arquivo ISO específico numa partição NTFS. Sem isso, ele erra com
    /// "Unable to find a medium containing a live file system" mesmo com o kernel já rodando
    /// (bug real, encontrado no primeiro teste em VM via reboot completo: o boot direto pela
    /// ISO como DVD funcionava, porque aí existe mídia óptica de verdade; o boot pela mídia
    /// real do produto — GRUB de staging → NTFS → loopback — não).
    ///
    /// <c>findiso=</c>, não <c>fromiso=</c> — os dois existem no live-boot e fazem coisas
    /// diferentes (confirmado lendo o código-fonte de <c>live-boot 20230131+deb12u1</c>, a
    /// versão do bookworm): <c>fromiso=</c> espera o NOME DO DISPOSITIVO LINUX embutido no
    /// caminho (ex. <c>/dev/sda3/ProgramData/...</c>), que o GRUB não tem como produzir — ele
    /// não conhece nomenclatura de dispositivo Linux, só a própria (<c>(hd0,gpt3)</c>).
    /// <c>findiso=</c> é o que varre TODO dispositivo, monta cada um, e testa se o caminho
    /// RELATIVO existe nele — exatamente a mesma semântica do <c>search --file</c> do GRUB e
    /// do <c>iso-scan/filename=</c> do Casper (<see cref="CasperIsoBootEntryBuilder"/>). NTFS
    /// entra nessa varredura via <c>ntfs-3g</c> (userspace), não driver de kernel — por isso
    /// <c>ntfs-3g</c> precisa estar no initramfs (está — ver <c>live-media/packages.list</c>).
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

            // Bug real, encontrado em VM: sem isto, o GRUB define a entrada mas não boota nela
            // sozinho — fica parado no próprio menu esperando Enter, para sempre, porque o
            // padrão do GRUB quando "timeout" nunca foi definido é esperar entrada do usuário
            // indefinidamente. Instalação desatendida não tem ninguém ali pra apertar tecla.
            // GrubConfigBuilder (usado pelos outros mecanismos) já fazia isto; este builder,
            // por ser novo, nunca teve. timeout=0 em vez de um valor com contagem: o usuário
            // não deveria ver o menu do GRUB, só a tela de progresso do próprio app depois.
            //
            // `quiet` NÃO entra na linha de kernel enquanto o mecanismo não passar a fase 11.
            // Ele suprime justamente as mensagens do live-boot e do systemd, que são a única
            // forma de saber onde um boot travou — e travou várias vezes, sempre aparecendo
            // como a mesma tela preta indistinguível. `systemd.show_status=true` força o
            // systemd a listar cada unidade. Silenciar o boot é decisão de acabamento; tomá-la
            // antes de o caminho estar validado custou vários ciclos de teste em VM.
            string config = $@"set default=0
set timeout=0

menuentry ""LinuxHub - instalar"" {{
    insmod part_gpt
    insmod part_msdos
    insmod ntfs
    insmod loopback
    insmod iso9660
    set isofile=""{isoGrubPath}""
    search --no-floppy --file --set=root $isofile
    loopback loop $isofile
    linux (loop)/live/vmlinuz boot=live findiso=$isofile nosplash systemd.show_status=true
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
