## Context

Hoje a ISO fica onde o usuário a baixou — `%APPDATA%\LinuxHub\ISOs\`, no volume do
Windows. O GRUB de staging a localiza com `search --file` e o casper a monta via
`iso-scan/filename=`. Isso funciona no dual-boot e falha em dois cenários, ambos
reproduzidos em teste real (2026-07-29):

1. **Modo substituir.** O `iso-scan` monta a partição hospedeira em `/isodevice`
   **read-write** e nunca desmonta (`scripts/casper-premount/20iso_scan`, lido do
   initrd da ISO: `find_path "$iso_path" /isodevice rw`, e `/isodevice` não aparece em
   nenhum outro script). Com `layout: name: direct` o curtin precisa liberar o disco
   inteiro e o `clear-holders` falha após ~15 s de tentativas:
   `FAIL: removing previous storage devices` → `CurtinInstallError`, exit 3. Ele
   tentaria desmontar o sistema de arquivos de onde está rodando.
2. **BitLocker.** O GRUB não tem suporte a BitLocker. Com device encryption ligada
   (padrão do Windows 11 com TPM), o `search --file` varre a partição e não enxerga
   nada: `error: no such device: /Users/.../ubuntu.iso`.

O dual-boot passa na mesma topologia porque declara a partição hospedeira com
`preserve: true` — o curtin não encosta nela. **O problema nunca foi o disco único;
foi pedir o reparticionamento total.**

Restrições levantadas lendo a própria ISO do Ubuntu 24.04.4:
- `is_supported_fs` (em `scripts/casper-helpers`) aceita
  `ext2|ext3|ext4|xfs|jfs|reiserfs|vfat|ntfs|iso9660|btrfs|udf`. Sem exFAT.
- FAT32 tem teto de 4 GB por arquivo; a ISO tem 6,2 GB.
- Logo: **NTFS é a única opção** que o Windows cria nativamente e o casper monta.

## Goals / Non-Goals

**Goals:**
- Modo substituir funcionando com um disco só, sem pendrive.
- Boot da ISO independente de o volume do Windows ser legível pelo GRUB.
- Reaproveitar o caminho de particionamento do dual-boot, que já é código testado.
- Devolver o espaço da staging ao usuário depois da instalação.

**Non-Goals:**
- Assinar a cadeia de boot (shim + MOK). Secure Boot continua exigindo desligamento
  manual; é decisão à parte.
- Suportar ISO em disco diferente do alvo. Continua funcionando, mas não é o caminho
  desenhado aqui.
- Redimensionar a raiz do Linux para absorver o espaço da staging após removê-la.
  O espaço volta como não alocado; crescer a raiz é trabalho separado.

## Decisions

### D1 — Partição de staging NTFS dedicada, em vez de `toram`

`toram` faria o casper copiar os squashfs para tmpfs e desmontar o iso9660, o que
liberaria o loop. Bem menos código. Rejeitado por dois motivos: exige ~7 GB de RAM
livre (o `copy_live_to` calcula `size` como o *usado* da mídia e falha se
`MemFree+Cached` for menor), o que exclui máquinas de 8 GB; e **não resolve
BitLocker**, porque o GRUB precisa ler a ISO antes de qualquer coisa do casper rodar.

A partição de staging resolve os dois de uma vez: uma partição criada pelo LinuxHub
nunca é criptografada, então o GRUB a lê; e sendo declarada `preserve: true` no
storage config, o curtin não tenta liberá-la.

### D2 — Modo substituir passa a emitir lista explícita, não `layout: direct`

Já está documentado no `AutoinstallStorageBuilder`: *"Uma partição existente que
ficasse de fora da lista faria o curtin tratá-la como espaço disponível."* Esse
comportamento, que hoje é tratado como armadilha a evitar, vira o mecanismo:

| Partição | Modo substituir |
|---|---|
| Disco | `preserve: true` (não reescreve a tabela inteira) |
| ESP existente | declarada `preserve: true`, formatada fat32 (recebe o GRUB do Ubuntu) |
| Staging (ISO) | declarada `preserve: true` |
| Semente CIDATA | declarada `preserve: true` |
| Windows (C:, MSR, Recovery) | **omitidas** → viram espaço livre |
| Raiz Linux | criada nesse espaço |

O `clear-holders` então só precisa soltar as partições do Windows, que ninguém
segura — o Windows não está rodando. É exatamente a forma do `BuildDualBootConfig`,
mudando apenas quais partições entram na lista.

Alternativa considerada: manter `layout: direct` e tentar soltar o `/isodevice` por
`early-commands` (`losetup -d` + `umount`). Rejeitada: a sessão live inteira, não só o
`/cdrom`, depende do arquivo — arrancá-lo derruba o instalador junto.

### D3 — A ESP é preservada como partição e reformatada

No modo substituir a ESP do Windows não é liberada junto com o resto: ela é declarada
`preserve: true` e formatada fat32 nova. Manter a partição evita que o curtin precise
recriá-la e evita o buraco de "nenhuma ESP declarada, nenhuma criada". O
`grubx64.efi` de staging que mora nela é descartado nessa formatação, o que é o
desejado — quem assume o boot é o GRUB instalado pelo Ubuntu.

### D4 — Espaço vem de um único shrink, calculado de uma vez

Hoje o `CloudInitSeedWriter` abre 128 MB sozinho quando falta espaço. Com a staging,
passam a ser duas necessidades (staging + semente). Fazer dois shrinks encadeados
dobraria o tempo e o risco. O preparo passa a calcular o total necessário e executar
**um** shrink.

No dual-boot, o espaço da staging é **adicional** ao que o usuário pediu no slider:
se ele reservou 100 GB para o Linux, o shrink tira 100 GB + staging + semente, para
que a raiz receba de fato os 100 GB pedidos.

### D5 — A staging é removida no primeiro boot do sistema instalado

Não dá para remover durante a instalação: a sessão live lê a ISO dela até o fim.
`late-commands` também roda dentro da sessão live, então também não serve.

A remoção acontece por um unit systemd `oneshot` gravado via `late-commands`, que no
primeiro boot do sistema instalado apaga a staging e a semente e se desabilita. As
partições são identificadas por **PARTUUID**, gravado no unit em tempo de geração —
nunca por índice, pela mesma regra que já vale no resto do projeto: identidade de
disco/partição é lida da fonte autoritativa, nunca deduzida.

Alternativa considerada: deixar a staging para sempre. Rejeitada — 7 GB
permanentemente perdidos, sem explicação visível para o usuário.

### D6 — As duas guardas ficam: Secure Boot e BitLocker

Secure Boot continua bloqueando porque o `grubx64.efi` do projeto não é assinado —
isso não muda com a staging.

A guarda de BitLocker **deixa de ser estritamente necessária** para o boot (a ISO sai
do volume cifrado e o GRUB passa a lê-la da staging), mas fica mesmo assim, por
decisão do joao. Razões que sustentam manter:

- O preparo **encolhe** o volume do Windows para abrir espaço para a staging.
  Encolher um volume BitLocker é possível, mas é operação sobre dado cifrado e não
  foi validada aqui.
- Alterar a cadeia de boot com BitLocker ativo costuma disparar pedido de chave de
  recuperação no próximo boot do Windows preservado (dual-boot). Bloquear antes é
  mais honesto que descobrir depois, sem a chave em mãos.
- Defesa em profundidade: se a cópia para a staging falhar e algum caminho futuro
  voltar a ler a ISO do volume do Windows, a guarda continua cobrindo.

## Risks / Trade-offs

- **7 GB indisponíveis entre o preparo e o primeiro boot** → é o preço de não exigir
  pendrive; o espaço volta em D5, e a UI informa o custo antes de confirmar.
- **O shrink pode não liberar o necessário** (arquivos imóveis: paginação,
  hibernação, restauração do sistema) → recusar antes de qualquer escrita,
  quantificando o que falta, como já faz o `DiskPartitioningService`.
- **Cópia de 6 GB antes do reboot leva minutos** → reportar progresso; sem isso a
  aplicação parece travada.
- **Unit systemd que apaga partições no sistema do usuário** é o item mais perigoso
  desta mudança → identificar por PARTUUID, conferir rótulo/filesystem antes de
  apagar, abortar em qualquer divergência, e nunca tocar em partição que não bata
  com os dois critérios.
- **BitLocker + mudança de boot pode disparar pedido de chave de recuperação** no
  Windows preservado (dual-boot) → alertar o usuário para ter a chave em mãos antes
  de confirmar. Não é causado pela staging, mas passa a ser o caminho comum.
- **Duas partições extras temporárias** (staging + semente) aparecem no
  Gerenciamento de Disco do Windows entre o preparo e o reboot → ambas já são criadas
  sem letra de unidade, o que as mantém fora do Explorador.

## Migration Plan

Não há estado persistido a migrar. A mudança é interna ao fluxo de instalação:
instalações anteriores já concluídas não são afetadas. Reversão é reverter o commit —
o `layout: direct` volta a valer e o modo substituir volta a falhar como hoje.

Ordem de implantação sugerida: staging + boot apontando para ela (destrava
BitLocker e dual-boot), depois o storage config do substituir, depois a limpeza pós-
instalação. Cada etapa é testável isoladamente em VM.

## Open Questions

Resolvidas durante a implementação:

- **Tamanho da staging** → folga fixa de 512 MB sobre o tamanho da ISO
  (`StagingPartitionService.SlackBytes`). Cobre metadado de NTFS e o alinhamento de
  1 MiB do `New-Partition`. **Não foi medido** — é estimativa com folga generosa; se
  algum dia apertar, o sintoma será falha na cópia, não corrupção.
- **Preservação da ESP no modo substituir** → a ESP é declarada `preserve: true` e
  reformatada. Ficou registrado em `BuildReplacePreservedSet`, junto com o motivo de
  cada uma das três partições que sobrevivem.

Ainda em aberto:

- A remoção da staging devolve o espaço como **não alocado**, e nada o reclama. Fazer
  a raiz crescer nele exigiria posicionar a staging depois da raiz e rodar
  `growpart`+`resize2fs` no primeiro boot — trabalho separado, e que aumentaria o
  alcance de um script que já é o mais perigoso do projeto.
- Não há atalho para quando a ISO já está num disco **diferente** do alvo. Hoje ela é
  copiada para a staging de qualquer jeito: 7 GB e uma cópia longa desnecessários
  nesse caso. Vale medir quantos usuários caem nele antes de manter um segundo
  caminho vivo.
- A `IsoGrubPath` é fixa (`/linuxhub.iso`). Se algum dia duas instalações preparadas
  coexistirem no mesmo disco, elas colidem. Hoje isso não acontece porque cada
  preparo cria uma staging nova, mas nada impede.
