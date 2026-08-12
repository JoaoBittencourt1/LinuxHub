#!/usr/bin/env bash
# Fase 6 (design.md D6): verificar o artefato dentro do squashfs, antes de
# extrair — exatamente um filesystem, identidade batendo com o plano,
# capacidades presentes. O nome da distro nunca seleciona caminho de código
# (§2) — só perguntamos se o artefato tem o que a operação exige.
#
# Uso: verify-and-extract.sh <plan.json> <artefato.iso> <partição-alvo>
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"

require_root
require_cmd unsquashfs mount rsync blkid

PLAN_PATH="${1:?uso: verify-and-extract.sh <plan.json> <artefato.iso> <partição-alvo>}"
ARTIFACT_PATH="${2:?uso: verify-and-extract.sh <plan.json> <artefato.iso> <partição-alvo>}"
TARGET_PART="${3:?uso: verify-and-extract.sh <plan.json> <artefato.iso> <partição-alvo>}"

EXPECTED_IDENTITY="$(json_get "$PLAN_PATH" '.distribution.expectedIdentity')"

# Capacidades de que as fases 7 e 8 dependem — presença é propriedade do
# artefato real, nunca de documentação (task 6.4). A cadeia de boot assinada
# entra aqui com nomes alternativos: distros diferentes empacotam em
# caminhos ligeiramente diferentes (research/D8).
REQUIRED_PATHS=(
  "usr/sbin/update-initramfs"
  "usr/sbin/update-grub"
  "usr/sbin/locale-gen"
  "usr/sbin/chroot"
  "etc/os-release"
)
REQUIRED_SIGNED_CHAIN_CANDIDATES=(
  "boot/efi/EFI/ubuntu/shimx64.efi"
  "boot/efi/EFI/debian/shimx64.efi"
  "boot/efi/EFI/BOOT/BOOTX64.EFI"
)

ISO_MOUNT="/run/linuxhub/iso"
SQUASH_MOUNT="/run/linuxhub/squashfs"
mkdir -p "$ISO_MOUNT" "$SQUASH_MOUNT"

mount -o loop,ro "$ARTIFACT_PATH" "$ISO_MOUNT" || die "falha ao montar o artefato em loopback: $ARTIFACT_PATH"

# --- 6.2: exatamente um filesystem no formato esperado ---
mapfile -t SQUASHFS_CANDIDATES < <(find "$ISO_MOUNT" -iname '*.squashfs' -type f)
if [[ "${#SQUASHFS_CANDIDATES[@]}" -ne 1 ]]; then
  strict_umount "$ISO_MOUNT"
  die "artefato ambíguo: ${#SQUASHFS_CANDIDATES[@]} filesystems squashfs encontrados, esperado exatamente 1 (6.2)"
fi
SQUASHFS_FILE="${SQUASHFS_CANDIDATES[0]}"

mount -o loop,ro "$SQUASHFS_FILE" "$SQUASH_MOUNT" || { strict_umount "$ISO_MOUNT"; die "falha ao montar o squashfs"; }

ledger_start_step "live.iso-mounted"

# --- 6.3: identidade lida sem extrair ---
ACTUAL_IDENTITY="$(grep -oP '^ID=\K.*' "${SQUASH_MOUNT}/etc/os-release" 2>/dev/null | tr -d '"' || true)"
if [[ -z "$ACTUAL_IDENTITY" || "${ACTUAL_IDENTITY,,}" != "${EXPECTED_IDENTITY,,}" ]]; then
  strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
  die "identidade do artefato divergente: esperado '$EXPECTED_IDENTITY', encontrado '${ACTUAL_IDENTITY:-<ausente>}' (6.3)"
fi

# --- 6.4: capacidades presentes, antes de extrair ---
for rel_path in "${REQUIRED_PATHS[@]}"; do
  if [[ ! -e "${SQUASH_MOUNT}/${rel_path}" ]]; then
    strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
    die "capacidade ausente no artefato: $rel_path (6.4)"
  fi
done

SIGNED_CHAIN_FOUND=0
for candidate in "${REQUIRED_SIGNED_CHAIN_CANDIDATES[@]}"; do
  if [[ -e "${SQUASH_MOUNT}/${candidate}" ]]; then
    SIGNED_CHAIN_FOUND=1
    break
  fi
done
if [[ "$SIGNED_CHAIN_FOUND" -eq 0 ]]; then
  strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
  die "cadeia de boot assinada ausente no artefato (nenhum candidato de D8 encontrado) (6.4)"
fi

# --- 6.5: extrai para a partição alvo, com progresso ---
TARGET_MOUNT="/mnt/linuxhub-target"
mkdir -p "$TARGET_MOUNT"
mount "$TARGET_PART" "$TARGET_MOUNT" || { strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"; die "falha ao montar a partição alvo"; }

log "extraindo filesystem para $TARGET_MOUNT"
emit_progress "install.extracting" 0
rsync -aHAX --info=progress2 "${SQUASH_MOUNT}/" "${TARGET_MOUNT}/" 2>&1 | \
  while IFS= read -r line; do
    if [[ "$line" =~ ([0-9]{1,3})% ]]; then
      emit_progress "install.extracting" "${BASH_REMATCH[1]}"
    fi
  done
emit_progress "install.extracting" 100

ledger_complete_step "live.iso-mounted"
ledger_start_step "live.distribution-extracted"

# --- 6.6: identidade conferida de novo, dentro do sistema extraído ---
EXTRACTED_IDENTITY="$(grep -oP '^ID=\K.*' "${TARGET_MOUNT}/etc/os-release" 2>/dev/null | tr -d '"' || true)"
if [[ -z "$EXTRACTED_IDENTITY" || "${EXTRACTED_IDENTITY,,}" != "${EXPECTED_IDENTITY,,}" ]]; then
  die "identidade divergente no sistema extraído: esperado '$EXPECTED_IDENTITY', encontrado '${EXTRACTED_IDENTITY:-<ausente>}' (6.6)"
fi

ledger_complete_step "live.distribution-extracted"

strict_umount "$SQUASH_MOUNT"
strict_umount "$ISO_MOUNT"

log "extração concluída e verificada em $TARGET_MOUNT"
printf '%s\n' "$TARGET_MOUNT"
