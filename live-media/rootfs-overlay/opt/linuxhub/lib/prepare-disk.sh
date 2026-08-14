#!/usr/bin/env bash
# Fase 5 (design.md D11): o disco alvo é provado livre antes de qualquer
# escrita destrutiva. Toda tarefa aqui é uma asserção que interrompe, nunca
# uma tentativa. Não há redimensionamento de partição nesta fase nem em
# nenhuma outra (D7) — a partição alvo já existe, criada pelo lado Windows.
#
# Uso: prepare-disk.sh <plan.json> <target-disk>
# Saída: caminho do dispositivo de partição alvo formatado (ext4), em stdout.
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"
# Falha sem `die` sai calada; este trap dá nome ao que parou (common.sh).
trap_uncaught_errors

require_root
require_cmd lsblk blkid findmnt fuser mkfs.ext4 blockdev

PLAN_PATH="${1:?uso: prepare-disk.sh <plan.json> <target-disk>}"
TARGET_DISK="${2:?uso: prepare-disk.sh <plan.json> <target-disk>}"

INSTALLER_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.installer.number')"
INSTALLER_PART_UUID="$(json_get "$PLAN_PATH" '.disk.installer.partitionUuid')"
INSTALLER_OFFSET_BYTES="$(json_get "$PLAN_PATH" '.disk.installer.offsetBytes')"
INSTALLER_SIZE_BYTES="$(json_get "$PLAN_PATH" '.disk.installer.sizeBytes')"
WINDOWS_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.windows.number')"
WINDOWS_PART="${TARGET_DISK}${WINDOWS_PART_NUMBER}"

# --- localizar a partição alvo pelo identificador ESTÁVEL, nunca pelo número ---
#
# O número da partição não identifica nada: o Windows numera por posição no
# disco, então inserir uma partição fisicamente antes de outra já existente
# renumera a outra — e o número que o plano guardou passa a apontar para outra
# partição.
#
# Aconteceu de verdade, e o alvo errado era o pior possível: o plano trazia
# `number: 6` para a raiz, mas a raiz é a partição 5 (a criação devolveu o
# índice seguinte, não o número assentado). A partição 6 é a de RECUPERAÇÃO do
# Windows. Formatar por número teria apagado a recuperação do sistema que este
# instalador existe para preservar.
#
# O GUID da partição (PARTUUID) é gravado na tabela GPT, atravessa o reboot e é
# exatamente o mesmo valor que o Windows registrou no plano. É o único
# identificador aqui que não muda. É por ele que se localiza; offset e tamanho
# entram depois como confirmação independente, não como busca.
TARGET_PART=""
mapfile -t DISK_PARTS < <(lsblk -nlo PATH,TYPE "$TARGET_DISK" | awk '$2=="part"{print $1}')
for part in "${DISK_PARTS[@]}"; do
  part_uuid="$(blkid -s PARTUUID -o value "$part" 2>/dev/null || true)"
  if [[ -n "$part_uuid" && "${part_uuid,,}" == "${INSTALLER_PART_UUID,,}" ]]; then
    TARGET_PART="$part"
    break
  fi
done

if [[ -z "$TARGET_PART" ]]; then
  log "PARTUUID esperado (do plano): $INSTALLER_PART_UUID"
  for part in "${DISK_PARTS[@]}"; do
    log "  visto: $part -> PARTUUID '$(blkid -s PARTUUID -o value "$part" 2>/dev/null || echo '<sem PARTUUID>')'"
  done
  die "partição alvo do plano não existe neste disco (5.5) — nada foi formatado"
fi

step "partição alvo localizada por PARTUUID: $TARGET_PART"

# O número do plano é informativo daqui em diante. Divergir dele não é motivo
# para parar (ele nunca foi identificador), mas é sinal de que a tabela mudou
# depois que o plano foi escrito — e isso merece ficar registrado.
if [[ "$TARGET_PART" != "${TARGET_DISK}${INSTALLER_PART_NUMBER}" ]]; then
  log "aviso: o plano registrou o número ${INSTALLER_PART_NUMBER}, mas o PARTUUID está em ${TARGET_PART}."
  log "aviso: seguindo pelo PARTUUID, que é o identificador estável. Offset e tamanho ainda serão conferidos."
fi

# Sem passo de registro próprio: o catálogo (InstallationStepCatalog) não
# reserva um id para "disco preparado" do lado live — esta preparação é
# interna à fase que antecede live.iso-mounted, e inventar um id aqui
# quebraria a paridade que a task 9.6 exige (nenhum id de passo fora do
# catálogo C#).

# --- 5.1: desmontagem estrita de TODAS as partições do disco alvo ---
# `-l` (lista plana), NUNCA `-d`: `-d` é --nodeps e omite as partições, deixando
# só o disco. Aqui isso seria pior que na descoberta — esta lista alimenta a
# desmontagem e as asserções do D11 ANTES de formatar. Vazia, o laço não
# desmontaria nada, a asserção "nenhuma partição montada" passaria sem ter
# verificado coisa alguma, e a formatação seguiria com o disco possivelmente em
# uso. Uma verificação que não verifica é pior que nenhuma: ela autoriza.
mapfile -t TARGET_DISK_PARTS < <(lsblk -nlo PATH "$TARGET_DISK" | grep -v "^${TARGET_DISK}$")
for part in "${TARGET_DISK_PARTS[@]}"; do
  strict_umount "$part"
done

# --- 5.2: asserção de que nenhuma partição do disco alvo permanece montada ---
for part in "${TARGET_DISK_PARTS[@]}"; do
  assert_not_mounted "$part"
done

# --- 5.4: volume do Windows desmontado antes de qualquer alteração de tabela ---
assert_not_mounted "$WINDOWS_PART"

# --- 5.3: partição alvo sem usuário aberto, duas amostras ociosas ---
assert_partition_idle "$TARGET_PART"

# --- 5.5: releitura de geometria contra o valor pretendido ---
ACTUAL_OFFSET_BYTES="$(cat "/sys/class/block/$(basename "$TARGET_PART")/start" 2>/dev/null)"
if [[ -n "$ACTUAL_OFFSET_BYTES" ]]; then
  ACTUAL_OFFSET_BYTES=$(( ACTUAL_OFFSET_BYTES * 512 ))
  [[ "$ACTUAL_OFFSET_BYTES" -eq "$INSTALLER_OFFSET_BYTES" ]] || \
    die "offset da partição alvo divergente: esperado ${INSTALLER_OFFSET_BYTES}, encontrado ${ACTUAL_OFFSET_BYTES} (5.5)"
fi
ACTUAL_SIZE_BYTES="$(blockdev --getsize64 "$TARGET_PART")"
[[ "$ACTUAL_SIZE_BYTES" -eq "$INSTALLER_SIZE_BYTES" ]] || \
  die "tamanho da partição alvo divergente: esperado ${INSTALLER_SIZE_BYTES}, encontrado ${ACTUAL_SIZE_BYTES} (5.5)"

# --- formata a partição alvo (não é redimensionamento — D7) ---
log "formatando $TARGET_PART como ext4"
mkfs.ext4 -q -F -L linuxhub-root "$TARGET_PART" || {
  dump_holders_diagnostic "$TARGET_PART"
  die "mkfs.ext4 falhou em $TARGET_PART"
}

# --- 5.5 (de novo, após a escrita): releitura confirma o dispositivo continua o mesmo ---
blockdev --rereadpt "$TARGET_DISK" 2>/dev/null || true
POST_SIZE_BYTES="$(blockdev --getsize64 "$TARGET_PART")"
[[ "$POST_SIZE_BYTES" -eq "$INSTALLER_SIZE_BYTES" ]] || \
  die "tamanho da partição alvo mudou depois da formatação: esperado ${INSTALLER_SIZE_BYTES}, encontrado ${POST_SIZE_BYTES} (5.5)"

log "disco alvo preparado: $TARGET_PART"
printf '%s\n' "$TARGET_PART"
