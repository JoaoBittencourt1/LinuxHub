## Context

Duas frentes que compartilham o mesmo diagnóstico — o app decide sozinho o que
deveria perguntar — e que podem ser entregues em ordem. Ver `proposal.md` para a
motivação.

**Estado atual relevante**

- `SystemInfoProvider` (`Features/InstallWizard/Services/SystemInfoProvider.cs`)
  devolve `GetKeymap() => "us"` e `GetTimezone() => "America/Sao_Paulo"` como
  constantes. Só `GetLocale()` lê do Windows, via `CultureInfo.CurrentCulture`.
- O transporte dos três campos **já existe inteiro**: `InstallerConfig` os
  carrega, e `AutoinstallBuilder` (YAML do subiquity), `UbiquityPreseedBuilder`
  (preseed) e `InstallerConfigWriter` (`install.conf`) já os escrevem. Nada disso
  precisa mudar.
- `IUnattendedInstallPreparer` + `UnattendedInstallPreparerRegistry` já despacham
  por mecanismo declarado; `SubiquityInstallPreparer` e `UbiquityInstallPreparer`
  são as implementações existentes.
- `IsAutoinstallToggleVisible` (`IsoAcquisitionViewModel`) já é o padrão de "a UI
  só oferece o que o mecanismo declarado suporta".
- `Assets/Grub/` contém binários GRUB usados para **bootar a ISO a partir do
  Windows** — o staging. Não é o bootloader do sistema instalado.
- `GrubConfigBuilder.BuildIsoBootEntry` só sabe bootar ISO **casper**: monta
  `loopback loop`, carrega `(loop)/casper/vmlinuz` e passa `boot=casper` +
  `iso-scan/filename=`. Nada disso existe numa ISO do archiso. Toda distro do
  catálogo hoje passa por essa entrada.
- A entrada `arch` do catálogo está com `IsTested = false`, e
  `DistroCatalogTests.UnattendedInstall_IsNeverClaimedByAnUntestedDistro`
  proíbe declarar mecanismo sem boot testado. O boot do Arch é pré-requisito do
  gate, não consequência dele.

**Histórico que restringe este change**

O `mint-ubiquity-autoinstall` foi abandonado depois do incidente de 2026-08-05,
em que uma instalação automática apagou a ESP do usuário e as entradas EFI de
todas as outras distros da máquina. A causa: `partman-auto/method` presente com
`partman-auto/disk` vazio fez o partman **eleger o disco sozinho** e
reparticioná-lo inteiro. O teste em VM de 2026-08-10 fechou o assunto ao provar
que `method` é ao mesmo tempo o interruptor da automação e o gatilho do disco
inteiro — não havia falha segura possível.

Restrições de `constitution.md`: §1 (feature-based, MVVM, nenhum evento de UI
chamando lógica), §2 (OCP — despacho por dados, não por `if` de identidade), §4
(nenhuma string de UI fixa no código), §5 (regra de negócio pura e testável),
§6.1 (falhar parando; nada que rode fora do Windows é assumido presente), §7.1
(capacidade só é declarada depois de validada).

## Goals / Non-Goals

**Goals**

- Eliminar valores regionais fixos, sem trocar um chute por outro: detectar do
  Windows e deixar o usuário corrigir.
- Adicionar instalação desatendida do Arch por um mecanismo que satisfaça as duas
  propriedades que faltaram ao Mint: alvo nomeado e validação sem efeito
  destrutivo.
- Oferecer escolha de ambiente gráfico onde ela existe de verdade.
- Corrigir o link do Arch, hoje quebrado, e tornar explícito que essa entrada
  expira.

**Non-Goals**

- **Não** entregar desktop pré-configurado, dotfiles ou tema. O Arch é instalado
  limpo com o ambiente escolhido.
- **Não** usar systemd-boot nem criar partição XBOOTLDR.
- **Não** redimensionar a ESP do Windows, em nenhuma circunstância.
- **Não** adicionar as demais chaves do subiquity (`drivers`, `codecs`,
  `updates`, `packages`, `ssh`), embora sejam próximas.
- **Não** reabrir o caminho do Mint.
- **Não** declarar o Arch no catálogo dentro deste change até a validação real
  passar.

## Decisions

### 1. Detectar como padrão, permitir corrigir

Chumbar erra silenciosamente; perguntar tudo do zero cansa quem já configurou o
Windows. O passo do wizard vem preenchido com o detectado e é editável.

*Alternativa descartada:* detectar e não mostrar. Mantém o erro invisível quando
a detecção falha — que é exatamente o defeito atual, só que mais difícil de
diagnosticar.

### 2. Fuso pela BCL, teclado por tabela

`TimeZoneInfo.TryConvertWindowsIdToIanaId()` existe desde o .NET 6 e o projeto é
`net10.0-windows`. Resolve Windows→IANA sem tabela mantida à mão, e o `Try`
devolve o caso sem correspondência em vez de lançar.

Para teclado não há equivalente na BCL. O layout ativo do Windows é lido e
mapeado para o keymap do Linux por uma tabela pequena, cobrindo os layouts
comuns. Quando não houver correspondência, cai num padrão declarado — que o
usuário vê no passo do wizard e pode corrigir (decisão 1). O ponto é que o
"não sei" fica visível, em vez de virar `"us"` calado.

*Nota:* o subiquity aceita `keyboard.layout` **e** `keyboard.variant`, e é o
variant que distingue `br` de `br-abnt2`. Vale expor os dois onde o mecanismo
suporta.

### 3. Por que o Arch é seguro onde o Mint não foi

É a decisão central deste change, e a razão de ele existir.

```
partman (Mint)                      archinstall (Arch)
──────────────                      ──────────────────
method LIGA a automação             wipe:false + status:"existing"
method ARMA o disco inteiro         alvo declarado explicitamente
      ↓                                   ↓
mesma chave para as duas coisas     alvo que não resolve = instalação
sem falha segura possível           falha (não = reparticionar o disco)

sem modo de teste                   --dry-run valida sem tocar em disco
```

**Correção após o levantamento (`research-archinstall.md`):** a versão original
desta decisão dizia "alvo nomeado, não adivinhado", supondo que `obj_id`
identificasse a partição. Não identifica — é um `uuid4()` interno do
archinstall. O alvo real é o **caminho de kernel** (`/dev/nvme0n1p3`), que não
existe do lado do Windows. A propriedade que separa o Arch do Mint continua
valendo, mas por outro motivo: o partman **elegia o disco sozinho** quando o
alvo vinha vazio, enquanto o archinstall, com um caminho que não resolve,
simplesmente não particiona e a instalação falha adiante. Nunca escolhe por
conta própria.

Isso não torna um caminho chutado aceitável: se `/dev/sda` existir e for outro
disco, ele é aceito sem questionar. Por isso a resolução do alvo passa a
acontecer no lado Linux, a partir do PARTUUID que o app nomeia (decisão 9).

O `--dry-run` é o que torna este caminho validável: ele desserializa a
configuração inteira, valida bootloader × layout e sai **antes** de qualquer
operação de filesystem. Não exercita o particionamento em si — passar nele prova
que a configuração é aceita, não que a instalação funciona. É a peça que, se
existisse no caminho do Mint, teria evitado o incidente, e por isso o gate deste
change é construído sobre ele (decisão 8).

### 4. GRUB como bootloader do sistema instalado

O `archinstall` aceita `GRUB`, `Systemd-boot` e `Limine`.

A ESP criada pelo Windows costuma ter 100 MB. O systemd-boot coloca kernel e
initramfs **dentro da ESP** e precisa de 300 MB ou mais — não cabe. E aumentar a
ESP está fora de questão: ela é a primeira partição do disco, e crescê-la exige
deslocar dezenas de gigabytes das partições seguintes, incluindo o Windows.

O GRUB mantém kernel e initramfs em `/boot` na partição raiz e usa poucos
megabytes da ESP. É indiferente ao tamanho dela e não exige partição extra.

**Achado do levantamento:** `bootloader_config.removable` tem default **`true`**
quando a chave é omitida. Com GRUB em UEFI isso instala no caminho de mídia
removível (`\EFI\BOOT\BOOTX64.EFI`) em vez de `\EFI\arch\` mais entrada no
firmware — e numa máquina com Windows esse caminho é o fallback do próprio
firmware. `"removable": false` precisa ser **explícito** na configuração
gerada; omitir não é neutro. O valor canônico do bootloader nesta versão é
`"Grub"` (`"GRUB"` também passa, porque `from_arg` aplica `.capitalize()`).

*Alternativa descartada:* systemd-boot com partição XBOOTLDR no espaço livre.
É a solução canônica para ESP pequena e seria mais "nativa" do Arch, mas o
suporte do `archinstall` a XBOOTLDR não está resolvido (issue #1072, fechada sem
resolução clara). Trocaria uma incógnita conhecida por outra desconhecida.

*Ponto de atenção:* o GRUB de `Assets/Grub/` **não** tem relação com esta
decisão. Aquele boota a ISO a partir do Windows; este é do sistema instalado, e
quem o instala é o `archinstall`. Confundir os dois levaria a supor que os
binários existentes são reaproveitáveis aqui — não são.

### 5. Ambiente gráfico como campo opcional da configuração

`profile_config` do `archinstall` oferece profiles de desktop. A escolha só
existe no Arch porque as demais ISOs do catálogo já embutem um ambiente.

O campo entra como **opcional** no `InstallerConfig`, lido apenas pelo preparer
do Arch. Há precedente de campo que não serve a todo caminho
(`EfiPartitionIndex`), e um campo de dado ignorado por quem não o usa não é
violação de OCP — o que seria violação é um `if` de identidade de distro.

A UI segue a regra que já existe para o toggle de autoinstall: a disponibilidade
vem do **mecanismo declarado**, nunca de `distro.Id == "arch"` (§2).

*Alternativa descartada:* bolsa de opções por mecanismo. Mais correta em tese,
mais código e indireção do que o problema justifica com um único mecanismo
usando o campo.

### 6. Link do Arch: fixo e datado, com expiração assumida

O catálogo aponta hoje para `2026.01.01`, que **já não existe** — o mirror mantém
apenas ~3 releases mensais. É um 404 em produção.

Existe um endereço estável sem data (`iso/latest/archlinux-x86_64.iso`) que nunca
quebraria. Ele foi descartado: §7.1 exige que a versão e o link apontem para a
build em que o mecanismo foi validado, e o schema do JSON do `archinstall` muda
entre versões. Um link `latest` entregaria todo mês uma ISO com um `archinstall`
que ninguém testou — trocaria uma falha ruidosa (404) por uma silenciosa
(configuração incompatível na máquina do usuário).

Consequência assumida: a entrada do Arch **expira**, ao contrário de Ubuntu e
Mint. A revisão periódica passa a fazer parte do processo de release.

### 7. Nada é assumido; tudo é verificado na ISO — **feito**

§6.1 é explícita, e foi violá-la que custou caro no Mint. O levantamento está
concluído e registrado em `research-archinstall.md`, com a procedência de cada
fato: `archinstall 4.4-1` (manifesto de pacotes da própria build), schema e
flags (pacote exato do Arch Linux Archive), `.automated_script.sh` e ganchos do
initramfs (pacotes `archiso 89` e `mkinitcpio-archiso 73`, os que construíram
esta ISO).

Duas suposições deste documento caíram: `obj_id` não identifica partição
(decisão 3) e `script=` **é documentado** — está em `README.bootparams` do
`mkinitcpio-archiso`, seção "Boot parameters (configs/releng)". O que não foi
lido é o `airootfs.sfs` da ISO em si; onde isso importa, a prova fica no boot
real (decisão 8).

### 8. O gate

`UnattendedInstallMechanism.Archinstall` só é declarado no catálogo depois de,
nesta ordem: **boot real do Arch pela staging** (decisão 12), que também é o que
levanta `IsTested`; `--dry-run` passando com a configuração real; instalação em
VM com snapshot no modo dual-boot, com ESP e partições do Windows íntegras ao
final; e o mesmo no modo substituir.

O boot entrou na frente da fila por dependência dura: sem ele a sessão live não
chega a existir, e nenhuma das demais provas pode ser produzida.

A trava contra uma máquina real é a **declaração no catálogo**, não o instalador
parar no meio (§7.1). Implementar adiante do gate é aceitável; deixar alcançável
não é.

### 9. O JSON do archinstall é gerado no lado Linux

Consequência direta da decisão 3: o `disk_config` endereça partição por caminho
de kernel, e o Windows não tem como saber se o disco será `/dev/sda` ou
`/dev/nvme0n1`. Gerar o JSON completo do lado do Windows exigiria chutar esse
caminho — o tipo de palpite que a spec `unattended-install` proíbe.

Então o app não gera o JSON: **gera um script de shell** que o gera. O script
carrega, como texto literal, tudo que o app de fato sabe — locale, keymap, fuso,
hostname, usuário, bootloader, profile — e resolve em runtime só as duas coisas
que dependem da máquina: o caminho da partição raiz e o da ESP, a partir dos
**PARTUUIDs que o app nomeia**. `blkid` e `lsblk` existem na sessão live do Arch
(é um sistema completo, não o initramfs mínimo do casper, onde a ausência deles
quebrou a identificação de disco no incidente).

O alvo continua nomeado pelo app; o que muda é onde o nome vira caminho. E é o
mesmo identificador estável que `identify-disk-by-partuuid` já resolveu — só que
lido no único lugar onde ele pode ser traduzido.

Falha parando: PARTUUID que não resolve faz o script sair sem chamar o
`archinstall`. Nenhuma configuração parcial chega ao instalador.

*Alternativa descartada:* gerar o JSON no Windows com o caminho chutado e contar
com o `archinstall` recusar. Ele não recusa — um `/dev/sda` que exista e seja
outro disco é aceito sem questionar.

### 10. Transporte: `script=`, com a ISO no lugar onde o app já grava

O gancho `.automated_script.sh` do perfil `releng` lê `script=` do
`/proc/cmdline`, aceita URL ou caminho local, e roda no autologin do root em
`tty1`. URL exige rede e servidor — descartada para um app offline. Sobra o
caminho local, e ele precisa existir dentro da sessão live.

O que torna isso possível é o hook `archiso_loop_mnt`: bootando a ISO como
arquivo (`img_dev=` + `img_loop=`), ele monta **a partição que hospeda a ISO** em
`/run/archiso/img_dev`, com `x-initrd.mount`, e essa montagem segue visível na
sessão live. Ou seja, o script vai ao lado da ISO — exatamente onde
`UnattendedInitrdWriter` já grava o cpio do Ubiquity — e é referenciado como
`script=/run/archiso/img_dev/<caminho>/<arquivo>.sh`.

E `img_dev` aceita `PARTUUID=` direto (o hook repassa a tag para o `mount`), o
que fecha o transporte com o mesmo identificador estável da decisão 9.

*Confirmado em boot real (2026-08-11), depois de uma tentativa falha:* a primeira
implementação perguntava o UUID ao próprio GRUB em tempo de boot
(`probe --fs-uuid`), como faz o `loopback.cfg` do fornecedor — seria melhor, por
não depender de nada calculado no Windows. Não funciona aqui: o `grubx64.efi`
que o app embarca é gerado com uma lista fixa de módulos que **não inclui
`probe`**, e não acompanha diretório de módulos, então nem `insmod` resolve. A
entrada morria em `can't find command 'probe'` e seguia com `img_dev=` vazio,
caindo no prompt interativo do initramfs.

Quem nomeia a partição, portanto, é o app: `IsoHostPartitionLocator` lê o
PARTUUID pelo mesmo WMI que o resto do projeto já usa. Regerar o GRUB com o
módulo `probe` foi descartado — trocaria uma peça de dado por uma peça binária
regerada à mão, e o `core.img` do BIOS teria que ir junto.

**`copytoram=n` é obrigatório.** O default é `auto`, que vira `y` sempre que o
`airootfs.sfs` couber em RAM com folga de 2 GiB — e nesse caso o
`archiso_loop_mount_handler` **desmonta** `/run/archiso/img_dev` ainda no
initramfs. O arquivo sumiria antes do login, e o `.automated_script.sh` falharia
calado (ele só executa se o `cp` devolver 0). É o tipo de default que só
apareceria como "não fez nada" depois do reboot.

### 11. Modo substituir preserva a partição de staging

Consequência de `copytoram=n`: a partição que hospeda a ISO fica presa durante
toda a instalação, porque é dela que vem o squashfs da raiz da sessão live. No
substituir essa partição é a staging, no mesmo disco que o `archinstall` vai
reparticionar.

Então a configuração gerada declara a staging como `existing` e não a apaga —
que é literalmente o que `AutoinstallStorageBuilder` já faz no caminho
subiquity, e pela mesma razão. Puxar o chão da sessão live no meio da instalação
é a versão archiso do problema já conhecido: a ISO em loopback prende a
partição hospedeira.

Consequência assumida: sobra uma partição no disco ao final. A limpeza (unidade
de primeiro boot, como o `PostInstallCleanupBuilder` faz no outro caminho) é
task própria e **não** entra nesta entrega — anotar isso não a resolve (§7.1),
então ela fica explícita como pendência, não como detalhe.

### 12. A entrada de boot é escolhida por dado, em classe separada

A entrada que o `GrubConfigBuilder` gera hoje é casper de ponta a ponta. A do
archiso não se parece com ela: não usa `loopback loop`, não tem `/casper`, e
quem monta o laço é o próprio initramfs a partir de `img_dev`/`img_loop`. A
receita do fornecedor está em `configs/releng/grub/loopback.cfg`.

Editar a entrada existente para servir às duas colocaria em risco o único
caminho de instalação que hoje funciona. Em vez disso, a construção da entrada
vira uma abstração com uma implementação por família de sessão live, resolvida
pelo **dado declarado na distro** — o mesmo padrão de
`UnattendedInstallPreparerRegistry`, e nunca `distro.Id == "arch"` (§2).

O valor declarado default é o casper, de modo que toda entrada existente do
catálogo continua produzindo **exatamente** a mesma entrada de boot de hoje —
travado por teste de caracterização escrito antes de mexer.

*Alternativa descartada:* condicional dentro do `GrubConfigBuilder`. Uma
distro nova voltaria a exigir editar o gerador, e o Ubuntu passaria a depender
de um `if` que só é exercitado pelo Arch.

## Risks / Trade-offs

**O schema do `archinstall` muda entre versões** → a configuração gerada vale
para a build declarada e pode quebrar em outra. *Mitigação:* link datado
(decisão 6) e verificação na ISO real (decisão 7); a versão do catálogo é parte
do contrato, não metadado.

**A entrada do Arch expira em poucos meses** → quando o mirror remover a build, o
download volta a dar 404, agora numa distro que promete instalação automática.
*Mitigação:* entra no processo de release; é o custo aceito por não usar o link
genérico.

**A tabela de teclado nunca cobrirá todos os layouts** → sempre haverá quem caia
no padrão. *Mitigação:* é por isso que a revisão pelo usuário (decisão 1) é
requisito e não conveniência; a tabela reduz o atrito, não o elimina.

**`--dry-run` pode não cobrir tudo que a execução real cobre** → passar no dry-run
não é prova de que a instalação real funciona, apenas de que a configuração é
aceita. *Mitigação:* ele é o primeiro portão, não o único — a VM com snapshot
continua obrigatória (decisão 8).

**~~A entrega do config depende de um parâmetro não documentado~~** → resolvido:
`script=` é documentado no `README.bootparams` do `mkinitcpio-archiso` que a
própria ISO instala, e o transporte está fechado na decisão 10.

**O `img_dev` é uma partição NTFS no dual-boot** → montá-la no initramfs depende
do módulo `ntfs3` estar no initramfs da ISO. O `HOOKS` do perfil `releng` inclui
`block filesystems`, e o hook `filesystems` do mkinitcpio adiciona os módulos de
sistema de arquivos do kernel, então deve estar — mas isso é inferência, não
leitura do artefato. *Mitigação:* é a primeira coisa que o boot real prova ou
derruba (decisão 8); se falhar, o dual-boot do Arch precisa de outro lugar para
a ISO.

**`copytoram=n` mantém a ISO presa à partição hospedeira** → no substituir isso
deixa uma partição sobrando no disco (decisão 11), e no dual-boot mantém o
volume do Windows montado (read-only) durante a instalação. *Mitigação:* é o
mesmo regime do caminho subiquity, que já roda assim; a limpeza fica como
pendência declarada.

**O script gerado roda como root numa sessão live, sem revisão humana** → um
erro nele tem o mesmo alcance de um erro no JSON, com mais superfície (é código,
não dado). *Mitigação:* o script é gerado por classe pura e testado por
comparação de texto, como `AutoinstallBuilder` e `GrubConfigBuilder` já são; e
tudo que não depende da máquina continua sendo dado literal, não lógica.

**Escopo grande num change só** → a parte regional é simples e a do Arch é longa
e cheia de incógnitas. *Mitigação:* as tasks são ordenadas para que a parte
regional seja concluída e validada primeiro, entregando valor sem esperar o
levantamento da ISO.

## Open Questions

O levantamento (decisão 7) foi feito e fechou as incógnitas de schema,
transporte e alvo. O que resta é verificação em máquina, não decisão de
arquitetura:

- O `ntfs3` está no initramfs desta build? Inferido do `HOOKS`, não lido.
  Cai no boot real.
- O `--dry-run` aceita a configuração gerada pelo script, incluindo os caminhos
  resolvidos em runtime? Só a sessão live responde.
- Limpeza da partição de staging no substituir: pendência declarada
  (decisão 11), sem dono neste change.
