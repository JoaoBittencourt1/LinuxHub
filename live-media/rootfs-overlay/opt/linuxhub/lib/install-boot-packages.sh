#!/usr/bin/env bash
# Fase 7.5 (design.md D8): instala no sistema extraído o que falta para ele
# arrancar — kernel (quando o artefato não o traz no squashfs) e a cadeia de
# boot assinada — a partir do repositório apt que a própria ISO carrega.
#
# Por que esta fase existe: a ISO de desktop do Ubuntu é `fsimage-layered` desde
# a 23.10, e a camada que o `install-sources.yaml` declara como padrão
# (`casper/minimal.squashfs`) não tem kernel, nem módulos, nem bootloader. Isso
# não é defeito do artefato: o instalador do Ubuntu extrai a camada e instala
# essas peças do `pool/` depois. Aqui é a mesma coisa, pelo mesmo caminho.
#
# Tudo OFFLINE, do `file:` da ISO montada. Nenhuma rede: uma instalação que
# depende de internet falha na casa de quem não tem, e falha no meio, com o
# disco já formatado.
#
# Uso: install-boot-packages.sh <artefato.iso> <suíte> <pacote-kernel|""> <target-mount>
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"

require_root
require_cmd mount chroot findmnt

ARTIFACT_PATH="${1:?uso: install-boot-packages.sh <artefato.iso> <suíte> <pacote-kernel> <target-mount>}"
ISO_SUITE="${2:?uso: install-boot-packages.sh <artefato.iso> <suíte> <pacote-kernel> <target-mount>}"
KERNEL_PACKAGE="${3-}"
TARGET_MOUNT="${4:?uso: install-boot-packages.sh <artefato.iso> <suíte> <pacote-kernel> <target-mount>}"

ledger_start_step "target.boot-packages-installed"

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
mount -o loop,ro "$ARTIFACT_PATH" "$ISO_MOUNT" || die "falha ao montar o artefato para instalar os pacotes de boot (7.5)"
mount --bind /dev "${TARGET_MOUNT}/dev"
mount --bind /dev/pts "${TARGET_MOUNT}/dev/pts"
mount -t proc proc "${TARGET_MOUNT}/proc"
mount -t sysfs sysfs "${TARGET_MOUNT}/sys"
_binds_mounted=1

# `trusted=yes` porque a confiança neste repositório já foi estabelecida, e de
# forma mais forte que uma assinatura de índice: o artefato inteiro teve o
# sha256 conferido contra o plano (4.4), duas vezes, uma delas depois do reboot.
# Um chaveiro apt aqui só verificaria de novo, com uma cadeia a mais para falhar.
printf 'deb [trusted=yes] file:%s %s main\n' "$REPO_IN_TARGET" "$ISO_SUITE" > "$APT_SOURCE"

# `Dir::Etc::sourceparts=-` isola o update a ESTE repositório: a lista que veio
# no squashfs aponta para a internet, e sem isolar, um `apt update` sem rede
# demoraria minutos em timeouts antes de falhar por um motivo que não é o nosso.
apt_in_target() {
  chroot "$TARGET_MOUNT" env DEBIAN_FRONTEND=noninteractive \
    apt-get -o Acquire::Retries=0 \
            -o Dir::Etc::sourcelist="sources.list.d/linuxhub-iso.list" \
            -o Dir::Etc::sourceparts="-" \
            -o APT::Get::List-Cleanup="0" "$@"
}

if ! apt_in_target update >/dev/null 2>&1; then
  die "apt-get update falhou sobre o repositório da ISO (7.5)"
fi

PACKAGES=(grub-efi-amd64-signed shim-signed efibootmgr)
# Vazio quando o kernel já veio dentro do squashfs — a fase 6.4 decidiu isso
# lendo o artefato, e repetir a decisão aqui abriria espaço para as duas
# divergirem.
if [[ -n "$KERNEL_PACKAGE" ]]; then
  PACKAGES=("$KERNEL_PACKAGE" "${PACKAGES[@]}")
fi

step "instalando do repositório da ISO (offline): ${PACKAGES[*]}"
if ! apt_in_target install -y --no-install-recommends "${PACKAGES[@]}"; then
  die "não foi possível instalar os pacotes de boot a partir da ISO (7.5)"
fi

# --- asserções positivas: o que foi instalado existe, nos caminhos que o
# empacotamento declara. Sem isto, "o apt não deu erro" viraria prova de que as
# fases 7 e 8 têm com o que trabalhar — e elas descobririam o contrário depois
# de já terem mexido no initramfs e na ESP. ---
if [[ -n "$KERNEL_PACKAGE" ]]; then
  compgen -G "${TARGET_MOUNT}/boot/vmlinuz-*" >/dev/null \
    || die "kernel ausente em /boot após instalar ${KERNEL_PACKAGE} (7.5)"
  compgen -G "${TARGET_MOUNT}/lib/modules/*/kernel" >/dev/null \
    || die "módulos ausentes em /lib/modules após instalar ${KERNEL_PACKAGE} (7.5)"
  step "kernel instalado: $(basename "$(compgen -G "${TARGET_MOUNT}/boot/vmlinuz-*" | head -1)")"
fi

SHIM_SIGNED="${TARGET_MOUNT}/usr/lib/shim/shimx64.efi.signed"
GRUB_SIGNED="${TARGET_MOUNT}/usr/lib/grub/x86_64-efi-signed/grubx64.efi.signed"
[[ -f "$SHIM_SIGNED" ]] || die "shim assinado ausente após a instalação: ${SHIM_SIGNED#"$TARGET_MOUNT"} (7.5)"
[[ -f "$GRUB_SIGNED" ]] || die "grub assinado ausente após a instalação: ${GRUB_SIGNED#"$TARGET_MOUNT"} (7.5)"
[[ -x "${TARGET_MOUNT}/usr/sbin/update-grub" ]] || die "update-grub ausente após a instalação (7.5)"

step "cadeia de boot assinada instalada no alvo"

cleanup
trap - EXIT

ledger_complete_step "target.boot-packages-installed"
log "pacotes de boot instalados a partir do artefato"
