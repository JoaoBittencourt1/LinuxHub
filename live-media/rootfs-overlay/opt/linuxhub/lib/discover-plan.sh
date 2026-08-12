#!/usr/bin/env bash
# Task 4.1 (design.md D13): descoberta do plano sem ambiguidade, ou a
# instalação para. O ponto de montagem desta mídia não expõe de forma
# confiável o volume que carrega o plano — procura em todo dispositivo de
# bloco, valida cada candidato, exige exatamente um contexto válido.
#
# Saída (stdout, em sucesso): três linhas — caminho do plano, caminho do
# registro, ponto de montagem do volume do Windows encontrado. Efeito
# colateral: monta o volume vencedor em LINUXHUB_WINDOWS_MOUNT (rw, para que
# 4.5/mirror-state.sh escreva nele depois).
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"

require_root
require_cmd lsblk jq mount ntfsfix

CANDIDATES_ROOT="/run/linuxhub/candidates"
mkdir -p "$CANDIDATES_ROOT"

declare -a VALID_CONTEXTS=()

try_candidate() {
  local device="$1"
  local mnt
  mnt="$CANDIDATES_ROOT/$(basename "$device")"
  mkdir -p "$mnt"

  # Montagem real de escrita (D3 revertida): tentamos rw direto porque o
  # espelhamento do estado (4.5) vai precisar dela na mesma sessão de mount;
  # falha aqui só descarta o candidato, não é fatal para a descoberta.
  if ! mount -t ntfs-3g -o rw "$device" "$mnt" 2>/dev/null; then
    ntfsfix -d "$device" >/dev/null 2>&1 || true
    if ! mount -t ntfs-3g -o rw "$device" "$mnt" 2>/dev/null; then
      rmdir "$mnt" 2>/dev/null || true
      return
    fi
  fi

  local transactions_dir="$mnt/ProgramData/LinuxHub/Transactions"
  if [[ ! -d "$transactions_dir" ]]; then
    strict_umount "$mnt"; rmdir "$mnt" 2>/dev/null || true
    return
  fi

  local plan_dir
  for plan_dir in "$transactions_dir"/*/; do
    [[ -d "$plan_dir" ]] || continue
    local plan_path="${plan_dir}installation-plan.json"
    local state_path="${plan_dir}installation-state.json"
    [[ -f "$plan_path" && -f "$state_path" ]] || continue

    json_validate_schema "$plan_path" "schemas/installation-plan.schema.json" || continue
    json_validate_schema "$state_path" "schemas/installation-state.schema.json" || continue

    local plan_id state_plan_id
    plan_id="$(jq -er '.planId' "$plan_path" 2>/dev/null)" || continue
    state_plan_id="$(jq -er '.planId' "$state_path" 2>/dev/null)" || continue
    [[ "$plan_id" == "$state_plan_id" ]] || continue

    local dir_name
    dir_name="$(basename "$plan_dir")"
    [[ "$dir_name" == "$plan_id" ]] || continue

    VALID_CONTEXTS+=("${plan_path}|${state_path}|${mnt}|${device}")
  done

  # Não desmontamos aqui: se este candidato acabar sendo o vencedor único,
  # 4.5 reusa o mesmo mount. Desmontar candidatos perdedores acontece depois
  # que sabemos qual é o vencedor (loop principal, abaixo).
}

mapfile -t BLOCK_DEVICES < <(lsblk -ndo PATH,TYPE | awk '$2=="part"{print $1}')
for dev in "${BLOCK_DEVICES[@]}"; do
  try_candidate "$dev"
done

if [[ "${#VALID_CONTEXTS[@]}" -eq 0 ]]; then
  die "nenhum plano válido encontrado em nenhum dispositivo"
fi
if [[ "${#VALID_CONTEXTS[@]}" -gt 1 ]]; then
  log "mais de um plano válido encontrado:"
  for ctx in "${VALID_CONTEXTS[@]}"; do log "  candidato: ${ctx%%|*}"; done
  die "ambiguidade de plano (${#VALID_CONTEXTS[@]} candidatos) — a instalação para antes de qualquer escrita"
fi

WINNER="${VALID_CONTEXTS[0]}"
IFS='|' read -r WINNER_PLAN WINNER_STATE WINNER_MOUNT WINNER_DEVICE <<< "$WINNER"

# Desmonta qualquer outro candidato que tenha sido montado sem ter plano
# válido (limpeza; não afeta o resultado, que já está decidido).
for cand_mnt in "$CANDIDATES_ROOT"/*; do
  [[ -d "$cand_mnt" ]] || continue
  if [[ "$cand_mnt" != "$WINNER_MOUNT" ]] && mountpoint -q "$cand_mnt"; then
    strict_umount "$cand_mnt"
  fi
done

log "plano encontrado: $WINNER_PLAN (dispositivo $WINNER_DEVICE)"
printf '%s\n%s\n%s\n' "$WINNER_PLAN" "$WINNER_STATE" "$WINNER_MOUNT"
