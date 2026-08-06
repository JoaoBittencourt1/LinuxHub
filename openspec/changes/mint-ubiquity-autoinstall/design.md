## Context

O pipeline de instalação desatendida de hoje foi escrito contra o subiquity do
Ubuntu 24.04: `AutoinstallBuilder` gera YAML `autoinstall:`,
`AutoinstallStorageBuilder` gera `storage:` no formato curtin,
`CloudInitSeedWriter` grava `user-data`/`meta-data` numa partição CIDATA
(datasource NoCloud do cloud-init), e `GrubConfigBuilder` acrescenta
`autoinstall` à linha de kernel. Nada disso é consultado pelo Ubiquity.

O `casper/filesystem.manifest` da ISO do Linux Mint 22.3 Cinnamon traz
`ubiquity 24.04.3+mint19`, `ubiquity-frontend-gtk` e `ubiquity-casper 1.498`, e
**não** traz `subiquity`, `curtin` nem `cloud-init`. Ou seja: o Mint automatiza
por debconf/preseed, e o caminho atual seria simplesmente ignorado.

As decisões abaixo não vêm da documentação do Ubuntu — vêm da leitura do
`casper/initrd.lz` real da ISO do Mint 22.3, extraído e inspecionado
(`scripts/casper-bottom/24preseed`, `scripts/casper-bottom/05mountpoints`,
`scripts/casper-premount/20iso_scan`, `usr/bin/casper-set-selections`). O que
está afirmado aqui como "o casper faz X" foi lido nesses arquivos.

### O que o `24preseed` do Mint realmente aceita

Ele roda na posição 21 da `casper-bottom/ORDER` (depois do `05mountpoints`,
antes do `25adduser`) e tem quatro vias de entrada:

1. **`/preseed.cfg` na raiz do initramfs** — a primeira coisa que faz é
   `if [ -e /preseed.cfg ]; then casper-set-selections /preseed.cfg; fi`.
2. **`file=<caminho>` / `preseed/file=<caminho>`** no cmdline — resolvido como
   `/root$item`, isto é, relativo à raiz do **filesystem live**, não do
   initramfs.
3. **`preseed/url=` / `url=`** — faz `dhclient` + `wget`; depende de rede.
4. **debconf inline no cmdline** — qualquer token no formato
   `caminho/com/barra=valor` vira `casper-preseed /root "$pergunta" "$valor"`;
   a variante `pergunta?=valor` marca a resposta como não-vista.

### A restrição que elimina as vias óbvias

`05mountpoints` move `/cdrom` para `/root/cdrom`, mas **não move
`/isodevice`** — que é onde `casper-premount/20iso_scan` montou (rw) a
partição que hospeda a ISO. Consequência prática: no momento em que o
`24preseed` roda, `/root/isodevice` não existe, então `file=/isodevice/...`
não resolve. E `/root/cdrom` existe, mas é a ISO montada em loopback —
somente-leitura, não dá para injetar arquivo nela.

### Chaves confirmadas no `ubiquity_24.04.3+mint19` da ISO

Extraído de `pool/main/u/ubiquity/` da própria ISO. Tudo abaixo existe no
pacote empacotado pelo Mint — não é herdado da documentação do Ubuntu:

- **Conta**: `passwd/username`, `passwd/user-fullname`,
  `passwd/user-password-crypted`, `passwd/user-uid`,
  `passwd/user-default-groups`, `passwd/auto-login`, `passwd/root-login`.
- **Sistema**: `debian-installer/locale`, `keyboard-configuration/layoutcode`
  (+ `modelcode`, `optionscode`), `clock-setup/utc`, `clock-setup/ntp`.
- **Particionamento**: `partman-auto/disk`, `partman-auto/method`,
  `partman-auto/choose_recipe`, `partman-auto/expert_recipe`,
  **`partman-auto/expert_recipe_file`**, `partman-auto/desired-swap`,
  `partman-auto/init_automatically_partition`.
- **Encerramento**: `ubiquity/reboot`, `ubiquity/poweroff`,
  `ubiquity/success_command`, `ubiquity/reboot_on_failure`.

`usr/share/ubiquity/start-ubiquity-dm` mostra que os modos de cmdline **não**
são equivalentes: `only-ubiquity` apenas faz do Ubiquity a única aplicação
(continua interativo), enquanto **`automatic-ubiquity` é o que passa
`--automatic`**, que é o modo dirigido por preseed. Há ainda `noninteractive`,
que troca o frontend inteiro por `ubiquity noninteractive` — e o script já usa
esse frontend como fallback automático se o X quebrar antes de subir.

### Incidente de 2026-08-05 — o que a primeira tentativa quebrou

A primeira implementação foi ao ar num boot real e **apagou a ESP da máquina do
usuário**, levando junto as entradas EFI de todas as outras distros instaladas.
Três defeitos independentes, todos confirmados depois no artefato real:

1. **`preseed/early_command` usava `lsblk` e `debconf-set`.** Nenhum dos dois
   existe no initramfs do casper — só `blkid`, `sed`, `cp` e `casper-preseed`
   (conferido no `initrd.lz` da ISO). O comando falhava inteiro, e
   `partman-auto/disk` nunca era definido.
2. **`partman-auto/expert_recipe_file` apontava para o initramfs.** O arquivo
   vinha no cpio e vivia em `/linuxhub.recipe` — mas o initramfs desaparece no
   `switch_root`, e o partman roda depois disso. A receita não existia na hora
   do uso.
3. **As confirmações destrutivas estavam preseedadas.**
   `partman/confirm`, `partman/confirm_nooverwrite` e
   `partman-partitioning/confirm_write_new_label` (que é literalmente o prompt
   de "escrever tabela de partição nova" — `lib/partman/lib/disk-label.sh`)
   respondidas como `true`.

Havia ainda um quarto ingrediente, e é ele que fecha a cadeia — só apareceu ao
ler o `display.d/10initial_auto` do partman da ISO: `partman-auto/method=regular`
estava preseedado, e **com `method` setado e `partman-auto/disk` vazio o partman
elege sozinho o disco quando a máquina tem apenas um**. Foi assim que ele chegou
ao NVMe do usuário sem que ninguém o tivesse indicado. `method=regular` então
significa `autopartition` no **disco inteiro**.

A ordem real dos fatos: (1) o alvo nunca foi gravado, (2) o partman elegeu o
único disco, (3) `regular` mandou reparticionar tudo, (4) as confirmações
preseedadas tiraram a última chance de parar. O erro final relatado pelo
usuário — `device or resource busy` em `/dev/nvme0n1p1` — é a ESP sendo
modificada enquanto montada.

Uma hipótese que se investigou e se **descartou**: que a cadeia
`casper-bottom` tivesse abortado no `24preseed`. Não abortou — `set -e` está
comentado no `scripts/casper` e o `24preseed` termina com `exit 0` de qualquer
forma. O fundo preto do instalador também não era defeito: `automatic-ubiquity`
roda o Ubiquity numa sessão X pelada, sem desktop, por design. Os ~300 erros de
arquivo não encontrado permanecem **sem explicação confirmada**.

### Consequência de design: fechar a seleção de alvo, não desligar a automação

A leitura fácil seria "não preseedar as confirmações". Mas elas foram o último
ingrediente, não a causa: o que colocou o partman no disco errado foi a seleção
de alvo preenchida pela metade — `method` presente, `disk` ausente.

Por isso a correção ataca a seleção, e não a automação (ver D9):

- `method` só é emitido no modo **substituir**, onde disco inteiro é o objetivo.
  No dual-boot ele não existe, e sem ele o ramo de eleição automática de disco
  nem chega a ser avaliado.
- O alvo só é gravado sob `[ -b "$d" ]` — nunca um valor vazio, que contaria
  como pergunta respondida.

Com a seleção fechada, manter as confirmações ligadas é o que permite o teste em
VM exercitar o caminho inteiro, que é onde se aprende o que falta. A trava contra
uma máquina real é o catálogo em `None` (`constitution.md` §7.1), não o
instalador parar no meio.

## Goals / Non-Goals

**Goals:**

- Escolher o gerador de configuração desatendida a partir do mecanismo
  declarado pela distro, sem `if (distro.Id == "mint")` espalhado pelo código.
- Entregar o preseed ao Ubiquity sem depender de rede e sem escrever dentro da
  ISO.
- Cobrir, no Mint, o mesmo conjunto de dados que o Ubuntu já cobre: conta com
  senha em hash, hostname, locale/timezone/keymap, particionamento nos dois
  modos, e reboot final sem prompt de mídia.
- Manter o caminho do Ubuntu byte-a-byte equivalente ao atual.

**Non-Goals:**

- Suportar Calamares, Anaconda, archinstall ou d-i puro.
- Unificar os dois geradores num modelo intermediário comum — os formatos são
  incompatíveis o bastante para que a abstração custe mais do que a duplicação
  aparente; o que se compartilha é a *entrada* (`InstallerConfig`/`DiskLayout`),
  não a saída.
- Remasterizar a ISO do Mint.

## Decisions

### D1. Transporte do preseed: um initrd adicional carregado pelo GRUB

O GRUB carrega mais de um arquivo no comando `initrd`, e o kernel concatena
todos os cpio num único initramfs. Gravamos um cpio minúsculo contendo um
único arquivo — `preseed.cfg` — e emitimos:

```
initrd (loop)/casper/initrd.lz /LinuxHub/mint.cpio
```

O resultado é exatamente o gatilho da via 1 acima: `/preseed.cfg` passa a
existir na raiz do initramfs, e o `24preseed` o consome sozinho, sem nenhum
parâmetro extra de cmdline.

Por que esta e não as outras:

- **`file=/isodevice/...`** — não funciona: `/isodevice` não é movido para
  `/root` antes do `24preseed` (ver Context).
- **`file=/cdrom/preseed/x.seed`** — exigiria escrever dentro da ISO
  (read-only) ou remasterizá-la.
- **`preseed/url=`** — exige DHCP + rede funcionando no live, numa máquina que
  acabou de rebootar do Windows; transforma uma falha de rede em falha de
  instalação.
- **Só debconf inline no cmdline** — funciona para escalares, mas o
  `24preseed` lê o cmdline com `for x in $(cat /proc/cmdline)`, que faz
  word-split: **nenhum valor pode conter espaço**. Receita `partman` e
  `early_command` contêm espaços, então não cabem ali.

Escrever cpio SVR4 (`070701`) do lado C# é um formato simples de cabeçalho
ASCII de tamanho fixo + nome + dados alinhados em 4 bytes, com um registro
`TRAILER!!!` no fim — não precisa de biblioteca externa.

### D2. Cmdline carrega só o que precisa vencer o arquivo

O `24preseed` aplica `/preseed.cfg` **antes** de varrer o cmdline, então
valores no cmdline sobrescrevem o arquivo. Reservamos o cmdline para os
parâmetros de ativação (`automatic-ubiquity`/`only-ubiquity`,
`debian-installer/locale`) e mantemos todo o resto — inclusive tudo que tenha
espaço — no `preseed.cfg`. Isso também evita esbarrar no limite de tamanho do
cmdline.

### D3. Mecanismo declarado no catálogo, não inferido da família

`DistroInfo.SupportsAutoinstall` (bool) vira um enum
`UnattendedInstallMechanism { None, Subiquity, UbiquityPreseed }`.

Não inferir de `Family`: Mint e Ubuntu são ambos `Debian` no catálogo e usam
mecanismos diferentes; e o mecanismo é propriedade da *build*, não da família —
o próprio Ubuntu usava Ubiquity até a 23.04. `None` continua sendo o padrão de
quem não foi validado de ponta a ponta, que é a semântica que o comentário
atual do campo já defende.

### D4. Seleção por mecanismo via despacho, não condicional espalhada

Uma interface `IUnattendedInstallPreparer` com uma implementação por mecanismo
(a atual `AutoinstallPreparationService` vira a implementação `Subiquity`),
resolvida a partir do mecanismo da distro. `GrubConfigBuilder` recebe os
parâmetros de cmdline e o initrd extra já prontos, em vez de decidir por conta
própria — mantém a classe como texto puro, sem passar a conhecer distro.

### D5. Ativação por `automatic-ubiquity`, com `ubiquity/reboot` no fim

A cmdline do Mint leva `automatic-ubiquity` (não `only-ubiquity`, que continua
interativo — ver acima). O encerramento sem prompt de mídia sai de
`ubiquity/reboot=true` no preseed, e não do `noprompt` do casper: `noprompt`
resolve o `casper-stop`, que é outro estágio.

### D6. Receita de particionamento em arquivo, não em `expert_recipe` inline

`partman-auto/expert_recipe_file` aponta para um arquivo com a receita, em vez
de embutir a receita como valor de debconf. Como o preseed já viaja no cpio
(D1), a receita vai como um segundo arquivo no mesmo cpio — o que também mantém
a receita legível para depuração, em vez de uma linha única gigante.

### D8. Só ferramentas conferidas no initramfs, e receita copiada para o live

O `early_command` roda no initramfs, não no sistema live. Vale apenas o que foi
verificado dentro do `initrd.lz` da ISO: `blkid`, `sed`, `cp`, `echo` e
`casper-preseed` existem; `lsblk` e `debconf-set` **não**.

- O disco pai sai do nome do device por `sed` (`/dev/nvme0n1p4` →
  `/dev/nvme0n1`), não por `lsblk -no pkname`.
- Quem grava no debconf é `casper-preseed /root <pergunta> <valor>` — ele carrega
  o confmodule de `$1`, então escreve no debconf do sistema **live**, que é onde
  o partman vai ler depois.
- A receita é copiada de `/linuxhub.recipe` (initramfs) para
  `/root/linuxhub.recipe` (filesystem live) enquanto os dois ainda coexistem.
- O alvo só é gravado sob `[ -b "$d" ]`: um `partman-auto/disk` vazio conta como
  pergunta já respondida, o que é pior do que não respondê-la.

### D9. `partman-auto/method` só no modo substituir

Ler o `display.d/10initial_auto` do partman da ISO fechou a causa do incidente e
mudou o desenho:

```sh
# If there's only one disk, then preseeding partman-auto/disk is unnecessary...
if [ "$method" ] && [ -z "$disks" ]; then
    ... disks="$(cat "${DEVS%$TAB*}"/device)"   # elege o único disco
fi
# If both are set, let's try to do a completely automatic partitioning
if [ "$method" ] && [ "$disks" ]; then
    regular)  ... autopartition "$id"; exit 0   # DISCO INTEIRO
```

Duas conclusões:

1. `method=regular` significa **disco inteiro, sem perguntar**. É exatamente o
   que o modo **substituir** quer — e exatamente o que o **dual-boot** não pode
   ter.
2. Com `method` setado e `disk` vazio, o partman **elege sozinho** o disco numa
   máquina de um disco só. Era esse o estado em 2026-08-05 (o `debconf-set`
   falhou e o alvo nunca foi gravado): ele escolheu o NVMe e reparticionou.

Portanto: `method` é emitido **apenas no modo substituir**. No dual-boot ele fica
de fora, o que fecha o atalho na origem — sem `method`, o ramo automático nem é
avaliado, independente de o alvo ter sido resolvido ou não.

Para o dual-boot a escolha precisa vir do espaço livre, via
`automatically_partition`. O valor dessa escolha é um id `$dev//$id` calculado em
runtime (`automatically_partition/50biggest_free/choices`), então não há literal
garantido. Mandamos `biggest_free` como **tentativa de falha segura**: se o
partman casar pelo nome do diretório, a instalação corre sozinha; se não casar,
ele pergunta essa única tela e o log do teste revela o valor aceito (task 5b.6).

### D10. As confirmações ficam ligadas, com o catálogo como trava

O caminho completo é exercitado no teste em VM — parar no particionamento não
ensinaria nada sobre a parte que ainda não se conhece, e custaria outro ciclo.
O que impede isso de alcançar uma máquina real não é o instalador parar no meio,
é o catálogo: o Mint segue em `None` até 6.3/6.4 passarem.

Isso é coerente com a lição do incidente, que não foi "nunca ligar" e sim a
ordem: as confirmações só valem depois de a seleção do alvo estar garantida.
Hoje está — o alvo só é gravado sob `[ -b "$d" ]`, e no dual-boot não existe
mais o atalho que elegia disco sozinho.

### D7. Identificação do disco alvo reaproveita o mecanismo existente

O `partman` precisa apontar para um dispositivo real, e o índice do disco no
Linux não é previsível a partir do Windows — que é exatamente o problema que
`identify-disk-by-partuuid` já resolveu para o subiquity. Reaproveitamos a
mesma identificação (PARTUUID em GPT, assinatura de disco em MBR),
resolvendo-a para o dispositivo dentro de `preseed/early_command`, que no
arquivo pode conter espaços à vontade.

## Risks / Trade-offs

- **O Ubiquity do Mint é um fork (`+mint19`) e pode ter divergido do preseed do
  Ubuntu** → nenhuma chave de preseed entra por dedução da documentação do
  Ubuntu; cada uma é conferida contra o Ubiquity empacotado na ISO antes de ser
  usada, e a validação final é um boot real.
- **Preseed parcial é pior que nenhum**: se o Ubiquity aceitar parte das chaves
  e parar numa tela intermediária, o usuário fica com um instalador
  semi-automático travado esperando input → o critério de aceite é a instalação
  completa sem intervenção, não "passou da primeira tela".
- **Particionamento por receita `partman` é menos expressivo que o `storage:`
  do curtin** → o escopo de layout suportado no Mint pode ser mais estreito que
  no Ubuntu; se for, a limitação é documentada e refletida na UI, não escondida.
- **`initrd` com múltiplos arquivos depende do GRUB e do kernel concatenarem
  como esperado** → é o mesmo mecanismo usado para microcode em toda
  distribuição, mas ainda assim é o primeiro item a validar num boot real,
  antes de investir no conteúdo do preseed.
- **Duplicação aparente entre os dois geradores** → aceita conscientemente
  (Non-Goals); forçar um modelo comum entre YAML curtin e debconf acoplaria os
  dois formatos pelo menor denominador.

## Migration Plan

1. Trocar o booleano pelo enum e ajustar os consumidores — o Ubuntu passa a
   declarar `Subiquity` e todo o resto `None`. Comportamento observável
   inalterado; `DistroCatalogTests.Autoinstall_IsClaimedByUbuntuOnly` é
   reescrito em termos do enum.
2. Introduzir o despacho por mecanismo com uma única implementação registrada
   (`Subiquity`), mantendo a suíte verde — nenhuma mudança de saída.
3. Só então acrescentar a implementação `UbiquityPreseed` e, por último,
   declará-la no catálogo para o Mint 22.3.

Rollback: reverter o passo 3 (a declaração no catálogo) devolve o Mint ao
comportamento atual — boot preparado até o instalador interativo — sem tocar no
caminho do Ubuntu.

## Open Questions

Respondidas pelo levantamento na ISO (ver "Chaves confirmadas" no Context):
quais chaves o `+mint19` honra, a diferença entre `automatic-ubiquity` e
`only-ubiquity`, e como encerrar sem prompt de mídia. Seguem em aberto:

- Qual receita `partman-auto` expressa fielmente o modo dual-boot — usar o
  espaço livre já liberado pelo shrink sem tocar nas partições existentes. As
  chaves existem, mas a receita em si precisa ser escrita e testada.
- A partição semente CIDATA continua necessária no caminho do Mint apenas como
  âncora de identificação do disco (D7), ou o identificador pode ser embutido
  direto no `preseed.cfg` sem ela?
- O cpio extra é aceito pelo GRUB e concatenado pelo kernel como esperado nesta
  combinação de firmware/kernel? É o gate da task 2.2 — nada depois disso vale
  a pena antes dessa prova.
