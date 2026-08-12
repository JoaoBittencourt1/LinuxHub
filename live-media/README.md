# Mídia live própria

Ver `openspec/changes/own-linux-installer/design.md` (D0) para a decisão e a
razão de existir. Resumo: a ISO da distro deixa de ser bootada; ela passa a
ser um arquivo de dados montado em loopback. Quem boota é esta mídia — uma
live Debian mínima cujo único trabalho é executar o instalador em
`rootfs-overlay/opt/linuxhub/lib/`.

Não é código C# da aplicação (ver `CLAUDE.md`). Ocupa o lugar que
`installer/` deixou.

## Layout

- `packages.list` — lista fixa de pacotes instalados no rootfs, um por
  operação que depende deles (task 1.2). Nenhum pacote "por precaução".
- `build/build-live-media.sh` — pipeline reprodutível: `debootstrap` do
  rootfs mínimo, instala os pacotes de `packages.list`, aplica
  `rootfs-overlay/`, empacota em `filesystem.squashfs`, monta a árvore de
  ISO e chama `grub-mkrescue` só com o módulo `x86_64-efi` — a mídia é
  **UEFI apenas** (D16); não há plataforma BIOS na imagem.
- `rootfs-overlay/` — arquivos sobrepostos ao rootfs depois do
  `debootstrap`+pacotes: as unidades systemd que tomam posse do console
  (task 1.6) e a unidade que executa o instalador, mais o instalador em si
  sob `opt/linuxhub/`.
- `boot/grub/grub.cfg` — configuração de boot da própria mídia (o menu que
  o firmware UEFI carrega ao bootar a ISO), não confundir com o
  `GrubConfigBuilder` do lado Windows, que gera a entrada que leva do
  Windows *até* esta mídia.

## Build

```sh
sudo live-media/build/build-live-media.sh
```

Requer root (debootstrap/chroot/mksquashfs) e roda em CI (Linux) — nunca no
dev machine Windows deste repo. Produz `out/linuxhub-live.iso` e
`out/linuxhub-live.iso.sha256`. O hash entra no catálogo assinado (task 1.9,
ver `Common/Data/LiveMediaCatalog.cs`).

## O que esta mídia NÃO é

Não é um produto com gerenciador de pacotes exposto nem sessão de usuário.
Não tem BIOS legado (D16) — os caminhos preservados (dual-boot manual e modo
substituir) continuam bootando a ISO da distro para isso. Ver Non-Goals em
`design.md`.
