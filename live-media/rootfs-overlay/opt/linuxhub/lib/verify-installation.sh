#!/usr/bin/env bash
# Task 9.3 (design.md D12): verifica o resultado antes de a instalação ser
# declarada concluída. "Instalado" deixa de significar "os comandos
# rodaram" só depois deste passo passar.
#
# Uso: verify-installation.sh <plan.json> <target-disk> <target-mount>
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"

require_root
require_cmd findmnt chroot blkid

PLAN_PATH="${1:?uso: verify-installation.sh <plan.json> <target-disk> <target-mount>}"
TARGET_DISK="${2:?uso: verify-installation.sh <plan.json> <target-disk> <target-mount>}"
TARGET_MOUNT="${3:?uso: verify-installation.sh <plan.json> <target-disk> <target-mount>}"

ledger_start_step "target.installation-verified"

INSTALLER_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.installer.number')"
EXPECTED_TARGET_PART="${TARGET_DISK}${INSTALLER_PART_NUMBER}"

# --- raiz monta, e a origem montada é de fato a partição alvo do plano ---
ACTUAL_ROOT_SOURCE="$(findmnt -no SOURCE "$TARGET_MOUNT")"
[[ -n "$ACTUAL_ROOT_SOURCE" ]] || die "raiz do sistema instalado não monta (9.3)"
ACTUAL_ROOT_SOURCE_RESOLVED="$(readlink -f "$ACTUAL_ROOT_SOURCE")"
EXPECTED_TARGET_PART_RESOLVED="$(readlink -f "$EXPECTED_TARGET_PART")"
[[ "$ACTUAL_ROOT_SOURCE_RESOLVED" == "$EXPECTED_TARGET_PART_RESOLVED" ]] || \
  die "raiz montada não é a partição alvo do plano: montada=$ACTUAL_ROOT_SOURCE_RESOLVED, esperada=$EXPECTED_TARGET_PART_RESOLVED (9.3)"

# --- fstab do alvo válido, resolvido DENTRO do chroot (não no ambiente live) ---
FSTAB_CHECK_OUTPUT="$(chroot "$TARGET_MOUNT" findmnt --verify --fstab 2>&1)" || \
  die "fstab do alvo inválido, resolvido dentro do chroot: ${FSTAB_CHECK_OUTPUT} (9.3)"

# --- partição de recuperação do Windows no offset/tamanho registrados no plano ---
if jq -e '.disk.recovery' "$PLAN_PATH" >/dev/null 2>&1 && [[ "$(jq -r '.disk.recovery' "$PLAN_PATH")" != "null" ]]; then
  RECOVERY_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.recovery.number')"
  RECOVERY_OFFSET_BYTES="$(json_get "$PLAN_PATH" '.disk.recovery.offsetBytes')"
  RECOVERY_SIZE_BYTES="$(json_get "$PLAN_PATH" '.disk.recovery.sizeBytes')"
  RECOVERY_PART="${TARGET_DISK}${RECOVERY_PART_NUMBER}"
  [[ -b "$RECOVERY_PART" ]] || die "partição de recuperação do Windows não existe mais (9.3)"
  ACTUAL_RECOVERY_SIZE="$(blockdev --getsize64 "$RECOVERY_PART")"
  [[ "$ACTUAL_RECOVERY_SIZE" -eq "$RECOVERY_SIZE_BYTES" ]] || \
    die "partição de recuperação do Windows mudou de tamanho: esperado ${RECOVERY_SIZE_BYTES}, encontrado ${ACTUAL_RECOVERY_SIZE} (9.3)"
  ACTUAL_RECOVERY_OFFSET="$(( $(cat "/sys/class/block/$(basename "$RECOVERY_PART")/start") * 512 ))"
  [[ "$ACTUAL_RECOVERY_OFFSET" -eq "$RECOVERY_OFFSET_BYTES" ]] || \
    die "partição de recuperação do Windows mudou de offset: esperado ${RECOVERY_OFFSET_BYTES}, encontrado ${ACTUAL_RECOVERY_OFFSET} (9.3)"
fi

# --- D9: sistema instalado não é a sessão live ---
if [[ -f "${TARGET_MOUNT}/etc/systemd/system/linuxhub-installer.service" ]]; then
  die "unidade do instalador live ainda presente no sistema instalado (9.3, D9)"
fi
if chroot "$TARGET_MOUNT" systemctl is-enabled live-config.service >/dev/null 2>&1; then
  die "live-config.service ainda habilitado no sistema instalado (9.3, D9)"
fi

# --- D10: entrada do Windows presente no grub.cfg gerado ---
grep -q 'menuentry "Windows"' "${TARGET_MOUNT}/boot/grub/grub.cfg" || \
  die "entrada do Windows ausente no grub.cfg do sistema instalado (9.3, D10)"

ledger_complete_step "target.installation-verified"
log "instalação verificada com sucesso"
