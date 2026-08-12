#!/usr/bin/env bash
# Tasks 4.2-4.4 (design.md D15): o reboot é fronteira de confiança — nada
# atravessa validado. Reconfere disco, partição do Windows, partição de boot
# do Windows e o artefato de distribuição, antes de qualquer escrita.
#
# Uso: revalidate.sh <plan.json>
# Saída: exporta LINUXHUB_TARGET_DISK (ex: /dev/sda) e
# LINUXHUB_ARTIFACT_PATH via stdout (duas linhas), ou morre.
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"

require_root
require_cmd lsblk blkid sha256sum jq

PLAN_PATH="${1:?uso: revalidate.sh <plan.json>}"
[[ -f "$PLAN_PATH" ]] || die "plano não encontrado: $PLAN_PATH"

DISK_UNIQUE_ID="$(json_get "$PLAN_PATH" '.disk.uniqueId')"
DISK_SIZE_BYTES="$(json_get "$PLAN_PATH" '.disk.sizeBytes')"
WINDOWS_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.windows.number')"
BOOT_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.boot.number')"

# --- 4.2: identidade e geometria do disco ---
TARGET_DISK=""
for disk in /dev/disk/by-id/*; do
  [[ -e "$disk" ]] || continue
  resolved="$(readlink -f "$disk")"
  uuid="$(blkid -s PTUUID -o value "$resolved" 2>/dev/null || true)"
  if [[ -n "$uuid" && "${uuid,,}" == "${DISK_UNIQUE_ID,,}" ]]; then
    TARGET_DISK="$resolved"
    break
  fi
done
[[ -n "$TARGET_DISK" ]] || die "disco com uniqueId $DISK_UNIQUE_ID não encontrado (4.2)"

ACTUAL_SIZE_BYTES="$(blockdev --getsize64 "$TARGET_DISK")"
[[ "$ACTUAL_SIZE_BYTES" -eq "$DISK_SIZE_BYTES" ]] || \
  die "geometria do disco divergente: esperado ${DISK_SIZE_BYTES} bytes, encontrado ${ACTUAL_SIZE_BYTES} (4.2)"

windows_part_path() { echo "${TARGET_DISK}${WINDOWS_PART_NUMBER}"; }
boot_part_path() { echo "${TARGET_DISK}${BOOT_PART_NUMBER}"; }

# --- 4.3: partição do Windows no filesystem esperado, ou causa é criptografia ---
WINDOWS_PART="$(windows_part_path)"
[[ -b "$WINDOWS_PART" ]] || die "partição do Windows não existe no disco alvo: $WINDOWS_PART (4.2)"
WINDOWS_FSTYPE="$(blkid -s TYPE -o value "$WINDOWS_PART" || true)"
if [[ "$WINDOWS_FSTYPE" != "ntfs" ]]; then
  if [[ "$WINDOWS_FSTYPE" == "BitLocker" || "$WINDOWS_FSTYPE" == "crypto_LUKS" ]]; then
    die "partição do Windows está criptografada (BitLocker) — não é ausência, é criptografia (4.3)"
  fi
  die "partição do Windows não está no filesystem esperado (ntfs); encontrado: ${WINDOWS_FSTYPE:-desconhecido} (4.3)"
fi

# --- boot do Windows presente no disco alvo (parte de 4.2) ---
BOOT_PART="$(boot_part_path)"
[[ -b "$BOOT_PART" ]] || die "partição de boot do Windows não existe no disco alvo: $BOOT_PART (4.2)"

# --- 4.4: hash do artefato de novo, e tamanho estável antes de aceitar ---
ARTIFACT_WINDOWS_PATH="$(json_get "$PLAN_PATH" '.distribution.isoWindowsPath')"
ARTIFACT_SHA256="$(json_get "$PLAN_PATH" '.distribution.isoSha256')"
ARTIFACT_SIZE_BYTES="$(json_get "$PLAN_PATH" '.distribution.isoSizeBytes')"

ARTIFACT_MOUNT="/run/linuxhub/windows-system-volume"
mkdir -p "$ARTIFACT_MOUNT"
if ! mountpoint -q "$ARTIFACT_MOUNT"; then
  mount -t ntfs-3g -o ro "$WINDOWS_PART" "$ARTIFACT_MOUNT" || die "falha ao montar a partição do Windows para ler o artefato (4.4)"
fi
# Caminho do plano é Windows-style (C:\...); resolve para o ponto de montagem Linux.
ARTIFACT_RELATIVE="${ARTIFACT_WINDOWS_PATH#?:}"
ARTIFACT_RELATIVE="${ARTIFACT_RELATIVE//\\//}"
ARTIFACT_LOCAL_PATH="${ARTIFACT_MOUNT}${ARTIFACT_RELATIVE}"
[[ -f "$ARTIFACT_LOCAL_PATH" ]] || die "artefato de distribuição não encontrado: $ARTIFACT_LOCAL_PATH (4.4)"

# Tamanho estável: a gravação do Windows pode não ter sido drenada.
PREV_SIZE=-1
for _ in 1 2 3 4 5; do
  CUR_SIZE="$(stat -c%s "$ARTIFACT_LOCAL_PATH")"
  [[ "$CUR_SIZE" -eq "$PREV_SIZE" ]] && break
  PREV_SIZE="$CUR_SIZE"
  sleep 1
done
[[ "$CUR_SIZE" -eq "$PREV_SIZE" ]] || die "tamanho do artefato não estabilizou (4.4)"
[[ "$CUR_SIZE" -eq "$ARTIFACT_SIZE_BYTES" ]] || die "tamanho do artefato divergente do plano (4.4)"

ACTUAL_SHA256="$(sha256sum "$ARTIFACT_LOCAL_PATH" | cut -d' ' -f1)"
[[ "${ACTUAL_SHA256,,}" == "${ARTIFACT_SHA256,,}" ]] || die "hash do artefato divergente do plano (4.4)"

log "revalidação pós-reboot concluída: disco $TARGET_DISK, artefato $ARTIFACT_LOCAL_PATH"
printf '%s\n%s\n' "$TARGET_DISK" "$ARTIFACT_LOCAL_PATH"
