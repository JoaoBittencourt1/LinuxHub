#!/usr/bin/env bash
# Fase 7.5 (design.md D8): instala a cadeia de boot assinada DENTRO do sistema
# extraído, a partir do repositório apt que a própria ISO carrega.
#
# Por que esta fase existe: o squashfs de uma ISO live do Debian traz o kernel,
# mas não o bootloader. Ele tem só `grub-common`; `grub2-common` (que fornece
# update-grub e grub-install), `grub-efi-amd64-signed`, `shim-signed` e
# `efibootmgr` ficam no `pool/` da ISO. Não é omissão da distro — é onde ela
# decidiu pôr essas peças, e o instalador dela faz exatamente isto.
#
# Tudo OFFLINE, do `file:` da ISO montada. Nenhuma rede: uma instalação que
# depende de internet falha na casa de quem não tem, e falha no meio, com o
# disco já formatado.
#
# Uso: install-bootloader-packages.sh <artefato.iso> <suíte> <target-mount>
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"

require_root
require_cmd mount chroot findmnt

ARTIFACT_PATH="${1:?uso: install-bootloader-packages.sh <artefato.iso> <suíte> <target-mount>}"
ISO_SUITE="${2:?uso: install-bootloader-packages.sh <artefato.iso> <suíte> <target-mount>}"
TARGET_MOUNT="${3:?uso: install-bootloader-packages.sh <artefato.iso> <suíte> <target-mount>}"

ledger_start_step "target.bootloader-packages-installed"

REPO_IN_TARGET="/run/linuxhub-iso-repo"
ISO_MOUNT="${TARGET_MOUNT}${REPO_IN_TARGET}"
APT_SOURCE="${TARGET_MOUNT}/etc/apt/sources.list.d/linuxhub-iso.list"

_binds_mounted=0
cleanup() {
  [[ "$_binds_mounted" -eq 1 ]] || return 0
  # A fonte apt aponta para um caminho que só existe enquanto a ISO está
  # montada. Deixá-la para trás faria todo `apt update` do sistema instalado
  # falhar, para sempre, por um repositório que nunca mais vai existir.
  rm -f "$APT_SOURCE"
  rm -rf "${TARGET_MOUNT}/var/lib/apt/lists/"*_run_linuxhub-iso-repo_* 2>/dev/null || true
  strict_umount "${TARGET_MOUNT}/sys"
  strict_umount "${TARGET_MOUNT}/proc"
  strict_umount "${TARGET_MOUNT}/dev/pts"
  strict_umount "${TARGET_MOUNT}/dev"
  strict_umount "$ISO_MOUNT"
  rmdir "$ISO_MOUNT" 2>/dev/null || true
  _binds_mounted=0
}
trap cleanup EXIT

mkdir -p "$ISO_MOUNT"
mount -o loop,ro "$ARTIFACT_PATH" "$ISO_MOUNT" || die "falha ao montar o artefato para instalar o bootloader (7.5)"
mount --bind /dev "${TARGET_MOUNT}/dev"
mount --bind /dev/pts "${TARGET_MOUNT}/dev/pts"
mount -t proc proc "${TARGET_MOUNT}/proc"
mount -t sysfs sysfs "${TARGET_MOUNT}/sys"
_binds_mounted=1

# `trusted=yes` porque a confiança neste repositório já foi estabelecida, e de
# forma mais forte que uma assinatura de índice: o artefato inteiro teve o
# sha256 conferido contra o plano (4.4), duas vezes, uma delas depois do reboot.
# Um `apt-key` aqui só verificaria de novo, com uma cadeia a mais para falhar.
printf 'deb [trusted=yes] file:%s %s main\n' "$REPO_IN_TARGET" "$ISO_SUITE" > "$APT_SOURCE"

step "instalando a cadeia de boot assinada do repositório da ISO (offline)"
if ! chroot "$TARGET_MOUNT" env DEBIAN_FRONTEND=noninteractive \
     apt-get -o Acquire::Retries=0 -o Dir::Etc::sourcelist="sources.list.d/linuxhub-iso.list" \
     -o Dir::Etc::sourceparts="-" -o APT::Get::List-Cleanup="0" update >/dev/null 2>&1; then
  die "apt-get update falhou sobre o repositório da ISO (7.5)"
fi

if ! chroot "$TARGET_MOUNT" env DEBIAN_FRONTEND=noninteractive \
     apt-get install -y --no-install-recommends \
     grub-efi-amd64-signed shim-signed efibootmgr; then
  die "não foi possível instalar a cadeia de boot assinada a partir da ISO (7.5)"
fi

# --- asserção positiva: os binários assinados existem, nos caminhos que o
# empacotamento declara. Sem isto, "o apt não deu erro" viraria prova de que a
# fase 8 tem o que copiar — e ela descobriria o contrário depois de já ter
# mexido na ESP. ---
SHIM_SIGNED="${TARGET_MOUNT}/usr/lib/shim/shimx64.efi.signed"
GRUB_SIGNED="${TARGET_MOUNT}/usr/lib/grub/x86_64-efi-signed/grubx64.efi.signed"
[[ -f "$SHIM_SIGNED" ]] || die "shim assinado ausente após a instalação: ${SHIM_SIGNED#$TARGET_MOUNT} (7.5)"
[[ -f "$GRUB_SIGNED" ]] || die "grub assinado ausente após a instalação: ${GRUB_SIGNED#$TARGET_MOUNT} (7.5)"
[[ -x "${TARGET_MOUNT}/usr/sbin/update-grub" ]] || die "update-grub ausente após a instalação (7.5)"

step "cadeia de boot assinada instalada no alvo"

cleanup
trap - EXIT

ledger_complete_step "target.bootloader-packages-installed"
log "pacotes do bootloader instalados a partir do artefato"
