#!/usr/bin/env bash
# Fase 8 (design.md D5, D7, D8, D10): bootloader assinado, sem grub-install,
# com o Windows no menu por prova, e a ESP só tocada onde a transação prova
# posse.
#
# Uso: install-bootloader.sh <plan.json> <target-disk> <target-mount>
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"

require_root
require_cmd mount efibootmgr grub-script-check chroot blkid findmnt

PLAN_PATH="${1:?uso: install-bootloader.sh <plan.json> <target-disk> <target-mount>}"
TARGET_DISK="${2:?uso: install-bootloader.sh <plan.json> <target-disk> <target-mount>}"
TARGET_MOUNT="${3:?uso: install-bootloader.sh <plan.json> <target-disk> <target-mount>}"

PLAN_ID="$(json_get "$PLAN_PATH" '.planId')"
BOOT_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.boot.number')"
BOOT_PART="${TARGET_DISK}${BOOT_PART_NUMBER}"
VENDOR_DIR_REL="EFI/LinuxHub"
MARKER_NAME=".linuxhub-transaction"

ledger_start_step "target.bootloader-installed"

# --- 8.1: prova a partição de boot antes de escrever qualquer entrada (D10) ---
ESP_MOUNT="/run/linuxhub/esp"
mkdir -p "$ESP_MOUNT"
mount -o ro "$BOOT_PART" "$ESP_MOUNT" || die "falha ao montar a partição de boot para prová-la (8.1)"
if [[ ! -f "${ESP_MOUNT}/EFI/Microsoft/Boot/bootmgfw.efi" ]]; then
  strict_umount "$ESP_MOUNT"
  die "partição de boot não contém EFI/Microsoft/Boot/bootmgfw.efi — não é a do Windows (8.1)"
fi
strict_umount "$ESP_MOUNT"
log "partição de boot provada como sendo do Windows: $BOOT_PART"

mount -o rw "$BOOT_PART" "$ESP_MOUNT" || die "falha ao montar a partição de boot (rw) para instalar o bootloader"

# --- 8.3: marca de posse na ESP (D5) ---
VENDOR_DIR="${ESP_MOUNT}/${VENDOR_DIR_REL}"
MARKER_PATH="${VENDOR_DIR}/${MARKER_NAME}"
if [[ -d "$VENDOR_DIR" ]]; then
  if [[ ! -f "$MARKER_PATH" ]]; then
    strict_umount "$ESP_MOUNT"
    die "diretório de vendor na ESP existe sem marcador de posse (8.3)"
  fi
  EXISTING_OWNER="$(cat "$MARKER_PATH")"
  if [[ "$EXISTING_OWNER" != "$PLAN_ID" ]]; then
    strict_umount "$ESP_MOUNT"
    die "diretório de vendor na ESP pertence a outra transação: $EXISTING_OWNER (8.3)"
  fi
else
  mkdir -p "$VENDOR_DIR"
  printf '%s' "$PLAN_ID" > "$MARKER_PATH"
  sync
fi

# --- 8.4: cadeia assinada copiada do ALVO já extraído, não da mídia de execução (D8) ---
SIGNED_CHAIN_SOURCE_DIR=""
for candidate_dir in "${TARGET_MOUNT}/boot/efi/EFI/ubuntu" "${TARGET_MOUNT}/boot/efi/EFI/debian"; do
  if [[ -f "${candidate_dir}/shimx64.efi" ]]; then
    SIGNED_CHAIN_SOURCE_DIR="$candidate_dir"
    break
  fi
done
[[ -n "$SIGNED_CHAIN_SOURCE_DIR" ]] || die "cadeia assinada não encontrada no alvo extraído (8.4) — deveria ter sido pega em 6.4"

cp "${SIGNED_CHAIN_SOURCE_DIR}/shimx64.efi" "${VENDOR_DIR}/shimx64.efi"
for grub_name in grubx64.efi grub.efi; do
  if [[ -f "${SIGNED_CHAIN_SOURCE_DIR}/${grub_name}" ]]; then
    cp "${SIGNED_CHAIN_SOURCE_DIR}/${grub_name}" "${VENDOR_DIR}/grubx64.efi"
    break
  fi
done
[[ -f "${VENDOR_DIR}/grubx64.efi" ]] || die "GRUB assinado ausente na origem (8.4)"
if [[ -f "${SIGNED_CHAIN_SOURCE_DIR}/mmx64.efi" ]]; then
  cp "${SIGNED_CHAIN_SOURCE_DIR}/mmx64.efi" "${VENDOR_DIR}/mmx64.efi"
fi

# --- stub de configuração encadeando pela raiz (D8), espelhado no diretório
# de vendor da distro dentro do alvo, porque é lá que o GRUB assinado da
# distro procura configuração ao lado do binário. ---
ROOT_UUID="$(blkid -s UUID -o value "$(findmnt -no SOURCE "$TARGET_MOUNT")")"
[[ -n "$ROOT_UUID" ]] || die "não foi possível ler o UUID da partição raiz do alvo (8.4)"
GRUB_STUB="search --no-floppy --fs-uuid --set=root ${ROOT_UUID}
set prefix=(\$root)/boot/grub
configfile \$prefix/grub.cfg
"
printf '%s' "$GRUB_STUB" > "${VENDOR_DIR}/grub.cfg"
printf '%s' "$GRUB_STUB" > "${SIGNED_CHAIN_SOURCE_DIR}/grub.cfg"

# --- 8.2 + geração real do grub.cfg do alvo, com os-prober desligado ---
_target_binds_mounted=0
mount_target_binds() {
  [[ "$_target_binds_mounted" -eq 1 ]] && return 0
  mount --bind /dev "${TARGET_MOUNT}/dev"
  mount -t proc proc "${TARGET_MOUNT}/proc"
  mount -t sysfs sysfs "${TARGET_MOUNT}/sys"
  mount --bind "$ESP_MOUNT" "${TARGET_MOUNT}/boot/efi"
  _target_binds_mounted=1
}
umount_target_binds() {
  [[ "$_target_binds_mounted" -eq 1 ]] || return 0
  strict_umount "${TARGET_MOUNT}/boot/efi"
  strict_umount "${TARGET_MOUNT}/sys"
  strict_umount "${TARGET_MOUNT}/proc"
  strict_umount "${TARGET_MOUNT}/dev"
  _target_binds_mounted=0
}
trap umount_target_binds EXIT
mount_target_binds

echo 'GRUB_DISABLE_OS_PROBER=true' >> "${TARGET_MOUNT}/etc/default/grub"
chroot "$TARGET_MOUNT" grub-mkconfig -o /boot/grub/grub.cfg

# --- entrada do Windows escrita por nós, a partir da identidade provada (D10) ---
WINDOWS_ENTRY="menuentry \"Windows\" {
    insmod part_gpt
    insmod fat
    insmod chain
    search --no-floppy --fs-uuid --set=root $(blkid -s UUID -o value "$BOOT_PART")
    chainloader /EFI/Microsoft/Boot/bootmgfw.efi
}
"
printf '\n%s' "$WINDOWS_ENTRY" >> "${TARGET_MOUNT}/boot/grub/grub.cfg"

# --- 8.7: validar a configuração gerada — sintaxe, entradas prometidas, contagem exata ---
grub-script-check "${TARGET_MOUNT}/boot/grub/grub.cfg" || die "grub.cfg gerado tem sintaxe inválida (8.7)"
ROOT_MENUENTRY_COUNT="$(grep -cE '^menuentry ' "${TARGET_MOUNT}/boot/grub/grub.cfg")"
grep -q 'menuentry "Windows"' "${TARGET_MOUNT}/boot/grub/grub.cfg" || die "entrada do Windows ausente no grub.cfg gerado (8.7)"
log "grub.cfg gerado com $ROOT_MENUENTRY_COUNT entradas de nível raiz"
[[ "$ROOT_MENUENTRY_COUNT" -ge 2 ]] || die "grub.cfg gerado com menos entradas de nível raiz do que o esperado (8.7)"

umount_target_binds
trap - EXIT

# --- 8.5: registrar a entrada no firmware, primeiro na ordem de boot ---
efibootmgr --create --disk "$TARGET_DISK" --part "$BOOT_PART_NUMBER" \
  --label "LinuxHub" --loader "\\${VENDOR_DIR_REL//\//\\}\\shimx64.efi" >/dev/null
NEW_BOOT_ID="$(efibootmgr | grep 'LinuxHub' | tail -n1 | grep -oP '^Boot\K[0-9A-Fa-f]{4}')"
[[ -n "$NEW_BOOT_ID" ]] || die "falha ao localizar a entrada de boot recém-criada (8.5)"
CURRENT_ORDER="$(efibootmgr | grep -oP '^BootOrder: \K.*')"
NEW_ORDER="${NEW_BOOT_ID},${CURRENT_ORDER//${NEW_BOOT_ID},/}"
efibootmgr --bootorder "$NEW_ORDER" >/dev/null

# --- 8.6: remover o espaço temporário de boot na ESP, só depois de provar posse (D5) ---
STAGING_DIR_REL="$(json_get "$PLAN_PATH" '.disk.installer.stagingEspDirectory' 2>/dev/null || true)"
if [[ -n "$STAGING_DIR_REL" && "$STAGING_DIR_REL" != "null" ]]; then
  STAGING_DIR="${ESP_MOUNT}/${STAGING_DIR_REL}"
  STAGING_MARKER="${STAGING_DIR}/${MARKER_NAME}"
  if [[ -d "$STAGING_DIR" ]]; then
    if [[ -f "$STAGING_MARKER" && "$(cat "$STAGING_MARKER")" == "$PLAN_ID" ]]; then
      rm -rf "$STAGING_DIR"
      [[ -d "$STAGING_DIR" ]] && die "espaço temporário da ESP não foi removido (8.6)"
    else
      log "aviso: espaço temporário $STAGING_DIR_REL não tem marcador desta transação — não removido (8.6)"
    fi
  fi
fi

strict_umount "$ESP_MOUNT"

ledger_complete_step "target.bootloader-installed"
log "bootloader instalado e Windows presente no menu"
