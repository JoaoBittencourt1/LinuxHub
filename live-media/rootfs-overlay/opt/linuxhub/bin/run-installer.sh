#!/usr/bin/env bash
# Task 1.1: ponto de entrada único da mídia live, chamado por
# linuxhub-installer.service. Orquestra as fases 4 a 9 chamando os scripts
# fixos em lib/ na ordem — nenhuma lógica de instalação vive aqui além da
# sequência e do espelhamento de progresso (D14).
set -euo pipefail
LIB_DIR="/opt/linuxhub/lib"
# shellcheck source=../lib/common.sh
source "${LIB_DIR}/common.sh"
# shellcheck source=../lib/mirror-state.sh
source "${LIB_DIR}/mirror-state.sh"

require_root

on_fatal_error() {
  local exit_code=$?
  log "instalação interrompida (código $exit_code) — o registro mostra o passo que começou e não concluiu"
  exit "$exit_code"
}
trap on_fatal_error ERR

mkfifo -m 600 "$LINUXHUB_PROGRESS_FIFO" 2>/dev/null || true
( python3 /opt/linuxhub/bin/progress-ui.py & ) || log "aviso: tela de progresso não pôde iniciar"

emit_progress "install.discovering-plan"
mapfile -t DISCOVERY < <("${LIB_DIR}/discover-plan.sh")
PLAN_PATH="${DISCOVERY[0]}"
STATE_PATH="${DISCOVERY[1]}"
LINUXHUB_WINDOWS_MOUNT="${DISCOVERY[2]}"
export LINUXHUB_WINDOWS_MOUNT
LINUXHUB_STATE_WINDOWS_PATH="$STATE_PATH"
export LINUXHUB_STATE_WINDOWS_PATH

cp "$STATE_PATH" "$LINUXHUB_RUN_STATE"

PLAN_ID="$(json_get "$PLAN_PATH" '.planId')"
SECRET_FILE="$(dirname "$PLAN_PATH")/account-secret.env"

emit_progress "install.revalidating"
mapfile -t REVALIDATION < <("${LIB_DIR}/revalidate.sh" "$PLAN_PATH")
TARGET_DISK="${REVALIDATION[0]}"
ARTIFACT_PATH="${REVALIDATION[1]}"

emit_progress "install.preparing-disk"
TARGET_PART="$("${LIB_DIR}/prepare-disk.sh" "$PLAN_PATH" "$TARGET_DISK")"

TARGET_MOUNT="$("${LIB_DIR}/verify-and-extract.sh" "$PLAN_PATH" "$ARTIFACT_PATH" "$TARGET_PART")"

emit_progress "install.configuring"
"${LIB_DIR}/configure-target.sh" "$PLAN_PATH" "$SECRET_FILE" "$TARGET_MOUNT"

emit_progress "install.installing-bootloader"
"${LIB_DIR}/install-bootloader.sh" "$PLAN_PATH" "$TARGET_DISK" "$TARGET_MOUNT"

emit_progress "install.verifying"
"${LIB_DIR}/verify-installation.sh" "$PLAN_PATH" "$TARGET_DISK" "$TARGET_MOUNT"

jq '.status = "succeeded" | .phase = "complete"' "$LINUXHUB_RUN_STATE" > "${LINUXHUB_RUN_STATE}.tmp"
mv -f "${LINUXHUB_RUN_STATE}.tmp" "$LINUXHUB_RUN_STATE"
mirror_state_to_windows || log "aviso: falha ao espelhar estado final (não fatal, research §O)"

strict_umount "$TARGET_MOUNT"
strict_umount "$LINUXHUB_WINDOWS_MOUNT"

emit_progress "install.complete"
log "instalação de $PLAN_ID concluída — reinicie para entrar no sistema instalado"
