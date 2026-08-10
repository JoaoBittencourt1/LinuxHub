using System.Globalization;
using System.Text;
using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Gera o preseed debconf que conduz o Ubiquity — o instalador do Linux Mint 22.x, e do
    /// Ubuntu até a 23.04. Lógica pura de texto, sem I/O, igual ao
    /// <see cref="AutoinstallBuilder"/> do caminho subiquity.
    ///
    /// Toda chave usada aqui foi confirmada dentro do <c>ubiquity_24.04.3+mint19</c> extraído
    /// da própria ISO do Mint 22.3 (ver design.md) — o Mint empacota um fork do Ubiquity, então
    /// a documentação do Ubuntu não é fonte suficiente. Onde a chave é lida:
    /// <list type="bullet">
    /// <item><c>passwd/*</c> e <c>netcfg/get_hostname</c> — <c>plugins/ubi-usersetup.py</c> e
    /// <c>user-setup/user-setup-ask</c>.</item>
    /// <item><c>time/zone</c> — <c>tzsetup/tzsetup</c>.</item>
    /// <item><c>partman-auto/*</c> — receita de particionamento.</item>
    /// <item><c>ubiquity/reboot</c> — encerramento sem prompt de mídia.</item>
    /// </list>
    /// </summary>
    public static class UbiquityPreseedBuilder
    {
        /// <summary>Nome do preseed dentro do cpio. O <c>casper-bottom/24preseed</c> procura
        /// exatamente <c>/preseed.cfg</c> na raiz do initramfs, antes de olhar o cmdline — é o
        /// gancho que dispensa qualquer parâmetro de boot apontando para o arquivo.</summary>
        public const string PreseedFileName = "preseed.cfg";

        /// <summary>Nome do arquivo de receita dentro do cpio. Vai separado porque uma receita
        /// <c>partman</c> tem espaços e quebras que não cabem num valor de debconf numa linha
        /// só — <c>partman-auto/expert_recipe_file</c> existe justamente para isso.</summary>
        public const string RecipeFileName = "linuxhub.recipe";

        /// <summary>Onde o casper desempacota os arquivos do cpio: a raiz do initramfs.</summary>
        private const string RecipePathInInitramfs = "/" + RecipeFileName;

        /// <summary>
        /// Onde a receita precisa estar quando o partman for lê-la. NÃO é o mesmo lugar de
        /// <see cref="RecipePathInInitramfs"/>: o initramfs desaparece no <c>switch_root</c>,
        /// e o partman roda depois disso — apontar para o caminho do initramfs foi um dos bugs
        /// do incidente de 2026-08-05, a receita simplesmente não existia mais na hora do uso.
        /// O <c>early_command</c> copia o arquivo para cá enquanto os dois ainda coexistem.
        /// </summary>
        private const string RecipePathInLiveFilesystem = "/" + RecipeFileName;

        /// <summary>
        /// O parâmetro que ativa o modo dirigido por preseed. Não é <c>only-ubiquity</c>: esse
        /// apenas faz do Ubiquity a única aplicação da sessão e continua interativo — quem
        /// passa <c>--automatic</c> para o binário é este, conforme
        /// <c>usr/share/ubiquity/start-ubiquity-dm</c> da ISO.
        /// </summary>
        public const string AutomaticParameter = "automatic-ubiquity";

        /// <summary>
        /// Valor de <c>partman-auto/init_automatically_partition</c> no dual-boot. Existe em
        /// <c>/lib/partman/automatically_partition/50biggest_free/</c> na ISO do Mint 22.3
        /// (confirmado no teste de 2026-08-10), mas sozinho não produz automação: sem
        /// <c>partman-auto/method</c> o ubiquity nunca entra em modo automático, e com o
        /// <c>method</c> a falha deixa de ser segura. Fica emitido porque é inofensivo e
        /// correto caso o ubiquity chegue a consultá-lo — não porque automatize algo.
        /// </summary>
        public const string DualBootAutomaticPartitionChoice = "biggest_free";

        /// <summary>
        /// Monta o <c>preseed.cfg</c>. <paramref name="passwordHash"/> é o hash SHA-512-crypt
        /// da senha: o preseed é gravado numa partição do Windows e sobrevive ao boot, então a
        /// senha em texto claro aqui ficaria legível para qualquer um com acesso ao disco.
        /// </summary>
        public static string BuildPreseed(
            InstallerConfig config, string passwordHash, string diskResolutionCommand,
            bool isReplaceMode)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

            var sb = new StringBuilder();

            sb.AppendLine("# Gerado pelo LinuxHub — instalação desatendida via Ubiquity/preseed.");
            sb.AppendLine();

            sb.AppendLine("# --- Localização ---");
            Append(sb, "d-i", "debian-installer/locale", "string", config.Locale);
            Append(sb, "d-i", "keyboard-configuration/layoutcode", "string", config.Keymap);
            Append(sb, "d-i", "time/zone", "string", config.Timezone);
            // O relógio do Windows roda em hora local; marcar UTC aqui faria o dual-boot
            // aparecer com o horário deslocado do lado do Windows depois da instalação.
            Append(sb, "d-i", "clock-setup/utc", "boolean", "false");
            sb.AppendLine();

            sb.AppendLine("# --- Conta ---");
            Append(sb, "d-i", "passwd/user-fullname", "string", config.Username);
            Append(sb, "d-i", "passwd/username", "string", config.Username);
            Append(sb, "d-i", "passwd/user-password-crypted", "password", passwordHash);
            Append(sb, "d-i", "passwd/auto-login", "boolean", "false");
            Append(sb, "d-i", "passwd/root-login", "boolean", "false");
            Append(sb, "d-i", "netcfg/get_hostname", "string", config.Hostname);
            sb.AppendLine();

            // A página `prepare` do Ubiquity (ubi-prepare.Page) pergunta estas duas. Sem elas o
            // instalador para e espera resposta — observado no teste em VM de 2026-08-10, onde
            // ele emitiu `INPUT high ubiquity/use_nonfree` e renderizou o widget. Nenhuma
            // leitura estática do pacote tinha apontado essa parada.
            sb.AppendLine("# --- Preparação ---");
            // Codecs e drivers proprietários: o Mint é distro de iniciante e já os traz por
            // padrão na instalação interativa — negar aqui entregaria uma máquina com menos
            // suporte de mídia e Wi-Fi do que quem instala pelo caminho normal.
            Append(sb, "ubiquity", "ubiquity/use_nonfree", "boolean", "true");
            // Baixar atualizações durante a instalação depende de rede, que o LinuxHub não
            // garante — e é justamente o passo que trava por minutos quando ela falta.
            // Atualizar depois do primeiro boot é o caminho seguro.
            Append(sb, "ubiquity", "ubiquity/download_updates", "boolean", "false");
            sb.AppendLine();

            sb.AppendLine("# --- Disco alvo ---");
            // O caminho do disco no Linux não é previsível a partir do Windows, então ele é
            // resolvido em tempo de boot pela tabela de partição e escrito de volta no debconf
            // — mesmo princípio do early-commands do caminho subiquity (design.md, D7).
            Append(sb, "d-i", "preseed/early_command", "string", diskResolutionCommand);
            sb.AppendLine();

            sb.AppendLine("# --- Particionamento ---");
            Append(sb, "d-i", "partman-auto/expert_recipe_file", "string", RecipePathInLiveFilesystem);

            if (isReplaceMode)
            {
                // `regular` só é correto aqui: em `display.d/10initial_auto` (partman da ISO),
                // method+disk caem no ramo `autopartition "$id"`, que reparticiona o DISCO
                // INTEIRO sem perguntar nada. É exatamente o que o modo substituir quer.
                Append(sb, "d-i", "partman-auto/method", "string", "regular");
            }
            else
            {
                // E é exatamente o que o dual-boot NÃO quer — por isso `method` fica de fora
                // aqui. Além de significar disco inteiro, ele arma um atalho perigoso: com
                // `method` setado e `partman-auto/disk` vazio, o mesmo script elege sozinho o
                // disco quando a máquina só tem um. Foi assim que a instalação de 2026-08-05
                // apagou a ESP do usuário.
                //
                // O TESTE EM VM DE 2026-08-10 FECHOU ESTA PORTA DE VEZ. Ele mostrou que o
                // ubiquity consulta `partman-auto/method` para decidir se entra em modo
                // automático: sem a chave ele fica em `auto_state = None` e a pergunta do
                // particionamento nunca é feita — o dual-boot cai no manual, sempre. Ou seja,
                // o interruptor da automação e o gatilho do disco inteiro são a MESMA chave.
                //
                // Não há como automatizar o dual-boot sem apostar que
                // `init_automatically_partition` casa; e se não casar, o partman não pergunta,
                // ele reparticiona. Isso é o que constitution.md §6.1 proíbe expressamente, e
                // por isso o dual-boot permanece manual: o LinuxHub prepara o boot e o usuário
                // escolhe a partição na tela do instalador. Automação incompleta é preferível
                // a automação insegura.
                Append(sb, "d-i", "partman-auto/init_automatically_partition", "select",
                    DualBootAutomaticPartitionChoice);
            }

            // As confirmações do partman. Ficam ligadas para o teste em VM exercitar o caminho
            // completo — é lá que se descobre o que a leitura estática não fecha, e um snapshot
            // desfaz o estrago. O que impede que isto chegue a uma máquina real é o catálogo:
            // o Mint segue em `None` até 6.3/6.4 passarem (constitution.md §7.1).
            //
            // A lição do incidente não foi "nunca ligar", foi a ordem: elas só valem depois de
            // o alvo estar garantido. Hoje ele está — o `early_command` só grava
            // `partman-auto/disk` sob `[ -b "$d" ]`, e no dual-boot não existe mais o atalho
            // que elegia disco sozinho.
            Append(sb, "d-i", "partman-partitioning/confirm_write_new_label", "boolean", "true");
            Append(sb, "d-i", "partman/choose_partition", "select", "finish");
            Append(sb, "d-i", "partman/confirm", "boolean", "true");
            Append(sb, "d-i", "partman/confirm_nooverwrite", "boolean", "true");
            sb.AppendLine();

            sb.AppendLine("# --- Encerramento ---");
            // Reinicia sozinho ao terminar. É outro estágio que o `noprompt` do casper: aquele
            // desarma o casper-stop, este desarma a tela final do próprio Ubiquity.
            Append(sb, "ubiquity", "ubiquity/reboot", "boolean", "true");
            Append(sb, "ubiquity", "ubiquity/poweroff", "boolean", "false");

            // O casper lê o preseed com um parser de linha; CRLF deixaria um '\r' colado no fim
            // de cada valor (um hash de senha com '\r' simplesmente não autentica).
            return sb.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// Receita <c>partman</c> do modo dual-boot: cria a raiz no espaço livre que o shrink
        /// já liberou, sem declarar nenhuma operação sobre as partições existentes. O tamanho
        /// vem em MB porque é a unidade da receita.
        ///
        /// Os três números de cada linha são <c>mínimo</c>, <c>prioridade</c> e <c>máximo</c>.
        /// Usar <c>-1</c> no máximo da raiz faz ela ocupar todo o espaço restante do vão.
        /// </summary>
        public static string BuildDualBootRecipe(bool swapEnabled, int swapSizeGb)
        {
            var sb = new StringBuilder();
            sb.AppendLine("linuxhub-dualboot ::");

            if (swapEnabled && swapSizeGb > 0)
            {
                int swapMb = swapSizeGb * 1024;
                sb.AppendLine($"    {swapMb} {swapMb} {swapMb} linux-swap");
                sb.AppendLine("        method{ swap } format{ }");
                sb.AppendLine("    .");
            }

            sb.AppendLine("    1000 50 -1 ext4");
            sb.AppendLine("        method{ format } format{ }");
            sb.AppendLine("        use_filesystem{ } filesystem{ ext4 }");
            sb.AppendLine("        mountpoint{ / }");
            sb.AppendLine("    .");

            return sb.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// Receita do modo substituir. Difere da de dual-boot por incluir a partição EFI: no
        /// substituir o disco inteiro é reparticionado, então a ESP precisa ser recriada — no
        /// dual-boot ela já existe e é a do Windows, que não pode ser tocada.
        /// </summary>
        public static string BuildReplaceRecipe(bool isUefi, bool swapEnabled, int swapSizeGb)
        {
            var sb = new StringBuilder();
            sb.AppendLine("linuxhub-replace ::");

            if (isUefi)
            {
                sb.AppendLine("    538 538 1075 free");
                sb.AppendLine("        $iflabel{ gpt } $reusemethod{ } method{ efi } format{ }");
                sb.AppendLine("    .");
            }

            if (swapEnabled && swapSizeGb > 0)
            {
                int swapMb = swapSizeGb * 1024;
                sb.AppendLine($"    {swapMb} {swapMb} {swapMb} linux-swap");
                sb.AppendLine("        method{ swap } format{ }");
                sb.AppendLine("    .");
            }

            sb.AppendLine("    1000 50 -1 ext4");
            sb.AppendLine("        method{ format } format{ }");
            sb.AppendLine("        use_filesystem{ } filesystem{ ext4 }");
            sb.AppendLine("        mountpoint{ / }");
            sb.AppendLine("    .");

            return sb.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// Comando que resolve o disco alvo em tempo de boot e o grava em
        /// <c>partman-auto/disk</c>. Roda como <c>preseed/early_command</c>, que o
        /// <c>24preseed</c> executa com <c>sh -c</c> logo depois de aplicar o arquivo — antes,
        /// portanto, de qualquer decisão de particionamento.
        ///
        /// Em GPT o identificador é o PARTUUID da partição semente (e o disco é o pai dela);
        /// em MBR é a assinatura do disco, que já aponta para o disco direto. Mesmos
        /// identificadores usados pelo caminho subiquity — ver
        /// <see cref="EarlyCommandsBuilder"/>.
        /// </summary>
        public static string BuildDiskResolutionCommand(DiskLayout layout, int seedPartitionNumber)
        {
            ArgumentNullException.ThrowIfNull(layout);

            // Só ferramentas conferidas dentro do initrd.lz real da ISO: `blkid`, `cp` e
            // `casper-preseed` existem; `lsblk` e `debconf-set` NÃO — usá-los foi o que
            // quebrou a identificação do disco em 2026-08-05 (constitution.md §6.1).
            string resolve = layout.IsGpt
                // Sem lsblk: o disco pai sai do próprio nome do device da partição, tirando o
                // sufixo (`/dev/nvme0n1p4` -> `/dev/nvme0n1`, `/dev/sda4` -> `/dev/sda`).
                ? $"p=$(blkid -t PARTUUID={SeedPartitionGuid(layout, seedPartitionNumber)} -o device); " +
                  "d=$(echo \"$p\" | sed -e 's/p\\?[0-9]\\+$//')"
                : $"d=$(blkid -t PTUUID={layout.DiskSignatureHex.ToLowerInvariant()} -o device)";

            // A receita vem no cpio e portanto vive no initramfs, que some no switch_root —
            // copiar para /root (o filesystem live) enquanto os dois coexistem é o que a deixa
            // legível para o partman depois.
            string stageRecipe = $"cp {RecipePathInInitramfs} /root{RecipePathInLiveFilesystem}";

            // casper-preseed em vez de debconf-set: ele existe no initramfs e escreve no
            // debconf do sistema LIVE (usr/bin/casper-preseed carrega o confmodule de $1).
            // Só grava o alvo se ele foi de fato resolvido — um `partman-auto/disk` vazio é
            // pior que ausente, porque conta como pergunta respondida.
            return $"{resolve}; {stageRecipe}; " +
                   "[ -b \"$d\" ] && casper-preseed /root partman-auto/disk \"$d\"";
        }

        private static string SeedPartitionGuid(DiskLayout layout, int seedPartitionNumber)
        {
            foreach (var partition in layout.Partitions)
            {
                if (partition.Number == seedPartitionNumber)
                    return partition.Guid.Trim('{', '}').ToLowerInvariant();
            }

            throw new InvalidOperationException(
                $"A partição semente {seedPartitionNumber} não aparece no layout lido do disco " +
                $"{layout.Index} — sem ela não há como identificar o disco alvo do lado Linux.");
        }

        /// <summary>
        /// Uma linha de preseed é <c>&lt;dono&gt; &lt;pergunta&gt; &lt;tipo&gt; &lt;valor&gt;</c>.
        /// Valor vazio é omitido: o <c>casper-set-selections</c> registraria a pergunta como
        /// já respondida com vazio, o que é pior do que não respondê-la — o instalador pularia
        /// a etapa em vez de perguntar.
        /// </summary>
        private static void Append(
            StringBuilder sb, string owner, string question, string type, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture, "{0} {1} {2} {3}", owner, question, type, value));
        }
    }
}
