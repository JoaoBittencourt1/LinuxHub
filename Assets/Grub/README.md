# Assets/Grub

Binários GRUB2 pré-compilados consumidos por `IGrubAssetProvider`
(`Features/InstallWizard/Services/GrubAssetProvider.cs`). Não são gerados pelo
app em runtime — não há toolchain GRUB (`grub-mkimage`, `grub-bios-setup`)
nativo no Windows; foram gerados uma vez via WSL/Ubuntu (pacotes
`grub-efi-amd64-bin`, `grub-pc-bin`, `grub-common`, `grub2-common`) e
commitados aqui. Regenerados em 2026-07-27 (v2) depois de um teste real expor
dois bugs na v1 — ver "Histórico" no fim deste arquivo.

## `uefi/grubx64.efi`

Gerada com `grub-mkimage` (não `grub-mkstandalone` — trocado na v2) e um
**early config embutido**, que roda antes de qualquer outra coisa:

```
search --no-floppy --file --set=root /EFI/linuxhub/grub.cfg
configfile /EFI/linuxhub/grub.cfg
```

```sh
grub-mkimage -O x86_64-efi -o grubx64.efi -c early-uefi.cfg -p /boot/grub \
  part_gpt part_msdos ntfs loopback iso9660 search search_fs_file chain fat normal linux configfile probe
```

O `probe` entrou na v3 e não é opcional para o Arch: o `loopback.cfg` da ISO
dele descobre o UUID do filesystem que hospeda a imagem com
`probe --fs-uuid`, e passa esse UUID ao kernel em `img_dev=UUID=...` (ver
`GrubConfigBuilder.ArchisoRecipe`). Sem o módulo, o comando não existe e a
entrada morre no menu. A alternativa — descobrir o UUID pelo lado do Windows e
gravar fixo no `grub.cfg` — foi descartada: exigiria deduzir como o Linux
nomeia aquele volume, que é exatamente o tipo de palpite que este projeto não
faz sobre disco.

Por quê: o `grub.cfg` real (com o menu de boot, caminho da ISO etc.) é gerado
em **runtime** por `GrubConfigBuilder`/`BootStagingService` — não dá pra
embutir ele no binário no momento do build, porque o conteúdo muda a cada
instalação. Sem um early config explícito, o GRUB não tem garantia de achar
esse arquivo sozinho (o prefixo padrão de uma imagem sem `-c`/sem memdisk
pode cair num shell de rescate em vez de carregar o menu) — o early config
resolve isso com dois comandos simples e bem documentados (`search` +
`configfile`), sem depender de nenhum comportamento implícito do GRUB.

## `bios/boot.img` + `bios/core.img`

`core.img` segue o mesmo princípio do UEFI — early config embutido via `-c`,
procurando `/boot/grub/grub.cfg` (onde `BootStagingService` escreve o
`grub.cfg` real na partição do Windows em BIOS legado):

```sh
grub-mkimage -O i386-pc -o core.img -c early-bios.cfg -p /boot/grub \
  biosdisk part_msdos part_gpt ntfs loopback iso9660 search search_fs_file normal linux configfile probe
```

`boot.img` (440 bytes) e o embutimento do `core.img` no gap pós-MBR
(`MbrPartitionTableReader` + `MbrBackupService.WriteCoreImageToGap`, chamados
por `BootStagingService.InstallBios`) continuam como documentado abaixo — o
conteúdo de `core.img` mudou (novo early config), mas o formato do patch em
`boot.img` (onde/como o `core.img` é referenciado) não depende do conteúdo
dele, só da posição (LBA 1), então `boot.img` não precisou ser regerado —
reverificado byte a byte contra um `grub-bios-setup` real rodando de novo.

Rodei o `grub-bios-setup` real (via WSL, contra um disco sintético — `losetup`
+ `parted`, formato MBR comum, sem partição `bios_grub` dedicada) e comparei
byte a byte o MBR resultante contra o `boot.img` de fábrica
(`/usr/lib/grub/i386-pc/boot.img`) para entender exatamente o que a ferramenta
real muda:

1. **`core.img` é embutido a partir do LBA 1** (logo após o MBR) — não numa
   posição calculada de forma mais sofisticada; é literalmente "primeiro
   espaço livre depois do setor de boot".
2. **Só dois bytes do `boot.img` mudam**, independente de onde o embutimento
   acontece: offset 102–103 (0-indexed), de `EB 05` (`jmp short +5`) para
   `90 90` (`NOP NOP`) — isso "ativa" o caminho de carregamento via
   `core.img` embutido (presente em toda instalação real, com ou sem
   `--no-rs-codes`, com ou sem partição `bios_grub`).
3. O campo `kernel_sector` (offset 92, `grub_uint64_t` little-endian) que
   `grub-bios-setup` normalmente patcha com o LBA do embutimento **já vem
   como `1` no `boot.img` de fábrica** — ou seja, como sempre embutimos a
   partir do LBA 1 (ponto 1), esse campo não precisa de patch em runtime.

Por isso, `Assets/Grub/bios/boot.img` já é o resultado final (440 bytes, com
o NOP aplicado) — não o `boot.img` cru do pacote — e não precisa de nenhum
patch adicional em C# antes de ser escrito no MBR real.

`BootStagingService.EnsurePostMbrGapFitsCoreImage` lê o MBR real do disco
alvo, calcula o gap (`MbrPartitionTableReader.GapSectorsAfterMbr`) e **aborta
antes de qualquer escrita** se ele for pequeno demais — discos com
alinhamento pré-Vista (partição 1 no LBA 63, ~31KB de gap) não cabem os
~146KB do `core.img`; discos modernos (alinhamento de 1MiB, LBA 2048, ~1MB de
gap) cabem com folga.

## Estado atual

Ambos os caminhos (UEFI e BIOS legado) têm todo o código e os assets
necessários, incluindo o early config que resolve como o GRUB acha o
`grub.cfg` real.

**UEFI está validado de ponta a ponta** desde a v3 (2026-08-17): entrada BCD →
`grubx64.efi` → early config → menu → sistema live do Arch bootado a partir da
ISO em loopback. **BIOS legado continua sem nenhum boot real.** A comparação byte a byte contra o
`grub-bios-setup` real (WSL, disco sintético em loop device) é a validação
mais forte disponível sem QEMU/hardware pro lado BIOS, mas não substitui
testar de verdade. Ver `TEST_MATRIX.md`.

## Histórico

- **v1 (2026-07-27, manhã)**: primeira geração, via `grub-mkstandalone` sem
  `-c`/early config. Teste real em VM UEFI (Hyper-V) expôs dois bugs
  Windows-side não relacionados aos binários em si (BCD registrado como
  `osloader` em vez de cópia de `{bootmgr}` — `0xc000007b`; ESP desmontada
  antes do `bcdedit` rodar). Corrigidos em `BootConfigurationService`/
  `BootStagingService`.
- **v3 (2026-08-17)**: acrescentado o módulo `probe` aos dois binários, para o
  suporte ao Arch (ver acima). Regerados com `grub-mkimage` 2.14-2ubuntu2.1 via
  WSL/Ubuntu. A lista de módulos embutidos do `grubx64.efi` foi comparada antes
  e depois parseando a tabela de módulos dos próprios arquivos: a única
  diferença é a linha `probe`, e o early config e o prefixo saíram byte a byte
  idênticos aos da v2. **Validada em boot real** (UEFI, dual-boot, ISO do Arch
  2026.08.01) — primeira vez que um binário daqui chega ao sistema live. O
  `core.img` (BIOS legado) continua sem teste de boot.
- **v2 (2026-07-27, tarde)**: ao revisar o restante do pipeline depois desses
  bugs, identifiquei que a v1 não tinha nenhuma garantia de achar o
  `grub.cfg` real (nem UEFI nem BIOS) — trocado `grub-mkstandalone` por
  `grub-mkimage -c` com early config explícito nos dois. `boot.img` (BIOS)
  não mudou; `core.img` e `grubx64.efi` foram regenerados.
