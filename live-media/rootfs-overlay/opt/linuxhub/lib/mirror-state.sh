#!/usr/bin/env bash
# Task 4.5 (design.md D3, revertida): espelha o registro de execução no
# volume do Windows a cada transição aceita — mesma localização que o lado
# Windows já lê (InstallationTransactionPaths), sem segundo formato.
#
# Cuidados (research §I), todos aqui: findmnt antes de escrever, recuperação
# do flag "unsafe" via ntfsfix -d, teste real de escrita antes de aceitar o
# mount como bom, escrita atômica, desmonte estrito ao final da instalação
# (não a cada escrita — strict_umount_windows_mount é chamado pelo
# orquestrador, run-installer.sh, uma única vez no fim).
#
# Falha em espelhar NUNCA é falha de instalação (research §O) — log é
# evidência, não sinal. Todo caller trata o retorno não-zero como aviso.
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"

# Preenchidos pelo orquestrador antes da primeira chamada.
LINUXHUB_WINDOWS_MOUNT="${LINUXHUB_WINDOWS_MOUNT:-}"
LINUXHUB_STATE_WINDOWS_PATH="${LINUXHUB_STATE_WINDOWS_PATH:-}"

_mirror_write_test_done=0

_ensure_mirror_mount_writable() {
  [[ -n "$LINUXHUB_WINDOWS_MOUNT" ]] || { log "mirror: LINUXHUB_WINDOWS_MOUNT não definido"; return 1; }

  if ! mountpoint -q "$LINUXHUB_WINDOWS_MOUNT"; then
    log "mirror: volume do Windows não está montado em $LINUXHUB_WINDOWS_MOUNT"
    return 1
  fi

  if [[ "$_mirror_write_test_done" -eq 0 ]]; then
    local probe="${LINUXHUB_WINDOWS_MOUNT}/.linuxhub-write-probe"
    if ! ( printf 'probe' > "$probe" 2>/dev/null && sync && rm -f "$probe" ); then
      log "mirror: teste de escrita real falhou em $LINUXHUB_WINDOWS_MOUNT"
      return 1
    fi
    _mirror_write_test_done=1
  fi
  return 0
}

mirror_state_to_windows() {
  if ! _ensure_mirror_mount_writable; then
    return 1
  fi
  [[ -n "$LINUXHUB_STATE_WINDOWS_PATH" ]] || { log "mirror: LINUXHUB_STATE_WINDOWS_PATH não definido"; return 1; }
  [[ -f "$LINUXHUB_RUN_STATE" ]] || { log "mirror: cópia de trabalho ausente em $LINUXHUB_RUN_STATE"; return 1; }

  local content
  content="$(cat "$LINUXHUB_RUN_STATE")"
  atomic_write "$LINUXHUB_STATE_WINDOWS_PATH" "$content" \
    || { log "mirror: escrita atômica falhou em $LINUXHUB_STATE_WINDOWS_PATH"; return 1; }
  return 0
}
