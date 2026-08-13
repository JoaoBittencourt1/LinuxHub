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
# ntfsfix saiu daqui de propósito: ele existia para limpar o flag "dirty" e
# forçar uma montagem rw que a descoberta não precisa — e num volume hibernado
# (Fast Startup) isso levaria a escrever num filesystem que o Windows ainda
# considera seu, corrompendo-o na retomada. Exigi-lo aqui só criaria um motivo
# a mais para a descoberta morrer sem necessidade.
require_cmd lsblk jq mount

CANDIDATES_ROOT="/run/linuxhub/candidates"
mkdir -p "$CANDIDATES_ROOT"

declare -a VALID_CONTEXTS=()

try_candidate() {
  local device="$1"
  local mnt
  mnt="$CANDIDATES_ROOT/$(basename "$device")"
  mkdir -p "$mnt"

  # Tenta rw, mas ACEITA ro: descobrir o plano é leitura pura, e exigir escrita
  # aqui descartava o volume inteiro quando o Windows estava com Fast Startup
  # ligado (padrão no 11). Nesse estado o ntfs-3g recusa rw de propósito — o
  # volume está hibernado, e escrever nele corrompe o filesystem quando o
  # Windows retoma a sessão. Bug real: a instalação morria com "nenhum plano
  # válido" e tela preta, com o plano ali, íntegro, montável em ro.
  #
  # Quem precisa de escrita é só o espelhamento do registro (D3), muito depois.
  # Se não deu rw, isso é registrado e o espelhamento vira no-op com aviso —
  # falha em espelhar nunca é falha de instalação (research §O).
  local writable=no
  if mount -t ntfs-3g -o rw "$device" "$mnt" 2>/dev/null; then
    writable=yes
  elif mount -t ntfs-3g -o ro "$device" "$mnt" 2>/dev/null; then
    writable=no
  else
    rmdir "$mnt" 2>/dev/null || true
    return
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

    VALID_CONTEXTS+=("${plan_path}|${state_path}|${mnt}|${device}|${writable}")
  done

  # Não desmontamos aqui: se este candidato acabar sendo o vencedor único,
  # 4.5 reusa o mesmo mount. Desmontar candidatos perdedores acontece depois
  # que sabemos qual é o vencedor (loop principal, abaixo).
}

# `-l` (lista plana), NUNCA `-d`: `-d` é --nodeps e lista apenas discos
# inteiros, omitindo as partições. Com ele o filtro por TYPE=="part" casava um
# conjunto que nunca conteria partição alguma — a varredura devolvia zero em
# qualquer máquina, e a instalação morria com "nenhum plano válido" tendo o
# plano ali, intacto. Bug real, encontrado em boot com o console legível.
mapfile -t BLOCK_DEVICES < <(lsblk -nlo PATH,TYPE | awk '$2=="part"{print $1}')
step "procurando o plano em ${#BLOCK_DEVICES[@]} partições: ${BLOCK_DEVICES[*]}"

# Uma linha por partição, ANTES de tentar montar: montar NTFS pode demorar ou
# pendurar, e sem isto um travamento aqui aparece como silêncio absoluto — sem
# nem dizer qual dispositivo estava sendo tentado.
for dev in "${BLOCK_DEVICES[@]}"; do
  step "  tentando $dev"
  try_candidate "$dev"
done
step "varredura concluída: ${#VALID_CONTEXTS[@]} contexto(s) válido(s)"

if [[ "${#VALID_CONTEXTS[@]}" -eq 0 ]]; then
  die "nenhum plano válido encontrado em nenhum dispositivo"
fi
if [[ "${#VALID_CONTEXTS[@]}" -gt 1 ]]; then
  log "mais de um plano válido encontrado:"
  for ctx in "${VALID_CONTEXTS[@]}"; do log "  candidato: ${ctx%%|*}"; done
  die "ambiguidade de plano (${#VALID_CONTEXTS[@]} candidatos) — a instalação para antes de qualquer escrita"
fi

WINNER="${VALID_CONTEXTS[0]}"
IFS='|' read -r WINNER_PLAN WINNER_STATE WINNER_MOUNT WINNER_DEVICE WINNER_WRITABLE <<< "$WINNER"

# Desmonta qualquer outro candidato que tenha sido montado sem ter plano
# válido (limpeza; não afeta o resultado, que já está decidido).
for cand_mnt in "$CANDIDATES_ROOT"/*; do
  [[ -d "$cand_mnt" ]] || continue
  if [[ "$cand_mnt" != "$WINNER_MOUNT" ]] && mountpoint -q "$cand_mnt"; then
    strict_umount "$cand_mnt"
  fi
done

log "plano encontrado: $WINNER_PLAN (dispositivo $WINNER_DEVICE, gravável: $WINNER_WRITABLE)"
if [[ "$WINNER_WRITABLE" != "yes" ]]; then
  log "aviso: o volume do Windows montou somente-leitura (típico de Fast Startup ligado)."
  log "aviso: a instalação segue normal; só o espelhamento do registro fica indisponível."
fi
printf '%s\n%s\n%s\n%s\n' "$WINNER_PLAN" "$WINNER_STATE" "$WINNER_MOUNT" "$WINNER_WRITABLE"
