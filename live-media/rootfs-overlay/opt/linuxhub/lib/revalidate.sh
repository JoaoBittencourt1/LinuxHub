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
require_cmd lsblk blkid findmnt blockdev sha256sum jq

PLAN_PATH="${1:?uso: revalidate.sh <plan.json>}"
[[ -f "$PLAN_PATH" ]] || die "plano não encontrado: $PLAN_PATH"

DISK_UNIQUE_ID="$(json_get "$PLAN_PATH" '.disk.uniqueId')"
DISK_PARTITION_TABLE_ID="$(json_get "$PLAN_PATH" '.disk.partitionTableId')"
DISK_SIZE_BYTES="$(json_get "$PLAN_PATH" '.disk.sizeBytes')"
WINDOWS_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.windows.number')"
BOOT_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.boot.number')"

# --- 4.2: identidade e geometria do disco ---
#
# A comparação é contra `partitionTableId`, NÃO contra `uniqueId`. O plano
# carrega os dois, e só um serve deste lado:
#
#   uniqueId          identificador de armazenamento do Windows (SCSI/VPD, do
#                     Get-Disk). Não existe no namespace do Linux — comparar
#                     contra ele nunca casa, em máquina nenhuma.
#   partitionTableId  "gpt:<guid da tabela>" — é exatamente o que o Linux expõe
#                     como PTUUID. É o campo criado para provar propriedade dos
#                     dois lados do reboot.
#
# Bug real: a versão anterior comparava o uniqueId com o PTUUID e morria com
# "disco não encontrado" tendo o disco certo bem ali.
case "$DISK_PARTITION_TABLE_ID" in
  gpt:*) EXPECTED_PTUUID="${DISK_PARTITION_TABLE_ID#gpt:}" ;;
  mbr:*) EXPECTED_PTUUID="${DISK_PARTITION_TABLE_ID#mbr:}" ;;
  *) die "partitionTableId do plano em formato desconhecido: '$DISK_PARTITION_TABLE_ID' (4.2)" ;;
esac

TARGET_DISK=""
mapfile -t CANDIDATE_DISKS < <(lsblk -nlo PATH,TYPE | awk '$2=="disk"{print $1}')
for disk in "${CANDIDATE_DISKS[@]}"; do
  ptuuid="$(blkid -s PTUUID -o value "$disk" 2>/dev/null || true)"
  if [[ -n "$ptuuid" && "${ptuuid,,}" == "${EXPECTED_PTUUID,,}" ]]; then
    TARGET_DISK="$disk"
    break
  fi
done

if [[ -z "$TARGET_DISK" ]]; then
  # Despeja o que foi comparado contra o quê: sem isto, "não encontrado" não
  # distingue disco ausente de identificador errado — e foi essa ambiguidade
  # que escondeu o bug acima.
  log "esperado PTUUID '$EXPECTED_PTUUID' (de partitionTableId '$DISK_PARTITION_TABLE_ID')"
  log "uniqueId do plano, só para referência: $DISK_UNIQUE_ID"
  for disk in "${CANDIDATE_DISKS[@]}"; do
    log "  visto: $disk -> PTUUID '$(blkid -s PTUUID -o value "$disk" 2>/dev/null || echo '<sem PTUUID>')'"
  done
  die "disco alvo do plano não encontrado (4.2)"
fi

step "disco alvo identificado: $TARGET_DISK"

ACTUAL_SIZE_BYTES="$(blockdev --getsize64 "$TARGET_DISK")"
[[ "$ACTUAL_SIZE_BYTES" -eq "$DISK_SIZE_BYTES" ]] || \
  die "geometria do disco divergente: esperado ${DISK_SIZE_BYTES} bytes, encontrado ${ACTUAL_SIZE_BYTES} (4.2)"
step "geometria confere (${ACTUAL_SIZE_BYTES} bytes)"

windows_part_path() { echo "${TARGET_DISK}${WINDOWS_PART_NUMBER}"; }
boot_part_path() { echo "${TARGET_DISK}${BOOT_PART_NUMBER}"; }

# Windows e boot só têm o número no plano (não carregam GUID), e número não é
# identificador: o Windows renumera partições por posição no disco. Sem conferir
# a geometria, "existe uma partição com este número e ela é ntfs" seria aceito
# como prova de que é A partição do plano — e não é. Offset e tamanho vêm do
# plano e são fatos físicos: se batem, é ela.
assert_matches_plan_geometry() {
  local part="$1" name="$2" expected_offset="$3" expected_size="$4"
  local sys_start actual_offset actual_size
  sys_start="$(cat "/sys/class/block/$(basename "$part")/start" 2>/dev/null || true)"
  if [[ -n "$sys_start" ]]; then
    actual_offset=$(( sys_start * 512 ))
    [[ "$actual_offset" -eq "$expected_offset" ]] || \
      die "offset da partição ${name} divergente: plano diz ${expected_offset}, ${part} está em ${actual_offset} (4.2)"
  fi
  actual_size="$(blockdev --getsize64 "$part")"
  [[ "$actual_size" -eq "$expected_size" ]] || \
    die "tamanho da partição ${name} divergente: plano diz ${expected_size}, ${part} tem ${actual_size} (4.2)"
}

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

assert_matches_plan_geometry "$WINDOWS_PART" "do Windows" \
  "$(json_get "$PLAN_PATH" '.disk.windows.offsetBytes')" \
  "$(json_get "$PLAN_PATH" '.disk.windows.sizeBytes')"
step "partição do Windows confere ($WINDOWS_PART, ntfs, geometria bate com o plano)"

# --- boot do Windows presente no disco alvo (parte de 4.2) ---
BOOT_PART="$(boot_part_path)"
[[ -b "$BOOT_PART" ]] || die "partição de boot do Windows não existe no disco alvo: $BOOT_PART (4.2)"
assert_matches_plan_geometry "$BOOT_PART" "de boot do Windows" \
  "$(json_get "$PLAN_PATH" '.disk.boot.offsetBytes')" \
  "$(json_get "$PLAN_PATH" '.disk.boot.sizeBytes')"
step "partição de boot do Windows confere ($BOOT_PART, geometria bate com o plano)"

# --- 4.4: hash do artefato de novo, e tamanho estável antes de aceitar ---
ARTIFACT_WINDOWS_PATH="$(json_get "$PLAN_PATH" '.distribution.isoWindowsPath')"
ARTIFACT_SHA256="$(json_get "$PLAN_PATH" '.distribution.isoSha256')"
ARTIFACT_SIZE_BYTES="$(json_get "$PLAN_PATH" '.distribution.isoSizeBytes')"

# A descoberta (D13) já montou este volume e o deixou montado de propósito.
# Montar o MESMO dispositivo num segundo ponto não funciona: o ntfs-3g recusa,
# e a revalidação morria com "falha ao montar a partição do Windows" tendo o
# volume montado e legível o tempo todo. Reusar o mount da descoberta também é
# mais forte que remontar: amarra o artefato ao mesmo volume que foi validado,
# em vez de a um que só por acaso tem o mesmo caminho.
ARTIFACT_MOUNT="${LINUXHUB_WINDOWS_MOUNT:-}"
if [[ -n "$ARTIFACT_MOUNT" ]] && mountpoint -q "$ARTIFACT_MOUNT"; then
  MOUNTED_SOURCE="$(findmnt -no SOURCE --target "$ARTIFACT_MOUNT" 2>/dev/null || true)"
  # O plano vive em ProgramData, que está na partição do Windows. Se o volume
  # descoberto não for essa partição, o caminho "C:\..." do artefato seria
  # resolvido contra o volume errado — divergência, não detalhe.
  [[ "$MOUNTED_SOURCE" == "$WINDOWS_PART" ]] || \
    die "o volume onde o plano foi encontrado ($MOUNTED_SOURCE) não é a partição do Windows do plano ($WINDOWS_PART) (4.4)"
  step "reusando o volume do Windows já montado em $ARTIFACT_MOUNT"
else
  ARTIFACT_MOUNT="/run/linuxhub/windows-system-volume"
  mkdir -p "$ARTIFACT_MOUNT"
  mount -t ntfs-3g -o ro "$WINDOWS_PART" "$ARTIFACT_MOUNT" || die "falha ao montar a partição do Windows para ler o artefato (4.4)"
fi
ARTIFACT_LOCAL_PATH="$(windows_path_to_local "$ARTIFACT_MOUNT" "$ARTIFACT_WINDOWS_PATH")"
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

# Conferir o hash de um artefato de vários GB leva minutos e não imprime nada
# enquanto roda. Sem avisar antes, isso é indistinguível de um travamento — e a
# reação natural de quem está olhando é desligar a máquina no meio.
step "conferindo hash do artefato (${CUR_SIZE} bytes) — isto leva alguns minutos"
ACTUAL_SHA256="$(sha256sum "$ARTIFACT_LOCAL_PATH" | cut -d' ' -f1)"
[[ "${ACTUAL_SHA256,,}" == "${ARTIFACT_SHA256,,}" ]] || die "hash do artefato divergente do plano (4.4)"
step "hash do artefato confere"

log "revalidação pós-reboot concluída: disco $TARGET_DISK, artefato $ARTIFACT_LOCAL_PATH"
# A terceira linha é a partição do Windows: o preparo do disco (5.1) desmonta
# tudo neste disco, inclusive este volume, e a extração precisa remontá-lo para
# ler o artefato. Sem devolver o dispositivo, o orquestrador teria que deduzi-lo
# de novo do plano — e deduzir disco é exatamente o que este projeto não faz.
printf '%s\n%s\n%s\n' "$TARGET_DISK" "$ARTIFACT_LOCAL_PATH" "$WINDOWS_PART"
