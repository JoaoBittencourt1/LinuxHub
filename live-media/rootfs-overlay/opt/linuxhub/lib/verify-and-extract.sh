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
# artefato real, nunca de documentação (task 6.4).
#
# `usr/sbin/update-grub` SAIU desta lista, e não por relaxamento: ele não existe
# no squashfs de uma ISO live do Debian. Ela traz só `grub-common`; quem fornece
# o update-grub é o `grub2-common`, que fica no `pool/` da própria ISO junto com
# `grub-efi-amd64-signed` e `shim-signed`. Exigi-lo aqui era exigir do artefato
# algo que a distro deliberadamente deixa para a instalação — e reprovaria todo
# artefato válido. O bootloader passou a ser instalado (fase 7.5) em vez de
# pressuposto; o que se verifica aqui é a matéria-prima dessa instalação.
REQUIRED_PATHS=(
  "usr/sbin/update-initramfs"
  "usr/sbin/locale-gen"
  "usr/sbin/chroot"
  "usr/bin/lsinitramfs"
  "var/lib/dpkg/status"
  "etc/os-release"
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

# --- 6.4b: kernel DENTRO do artefato, não pressuposto ---
#
# É a diferença que decidiu qual distro este caminho suporta. A ISO do Ubuntu
# desde a 23.10 é `fsimage-layered`: a camada base não tem kernel nem módulos,
# porque o instalador dele os instala depois. Extrair aquilo produziria um
# sistema que não arranca — e o erro só apareceria no reboot final, com o
# Windows já encolhido e a partição já formatada.
#
# Aqui a pergunta não é o nome da distro (§2), é se o artefato tem kernel.
if ! compgen -G "${SQUASH_MOUNT}/boot/vmlinuz-*" >/dev/null; then
  strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
  die "artefato sem kernel em /boot — o sistema extraído não arrancaria (6.4)"
fi
if ! compgen -G "${SQUASH_MOUNT}/lib/modules/*/kernel" >/dev/null; then
  strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
  die "artefato sem módulos de kernel em /lib/modules — o sistema extraído não arrancaria (6.4)"
fi
step "kernel presente no artefato: $(basename "$(compgen -G "${SQUASH_MOUNT}/boot/vmlinuz-*" | head -1)")"

# --- 6.4c: os pacotes do bootloader, no repositório que a ISO carrega ---
#
# A cadeia assinada não vem pronta no squashfs do Debian; vem do `pool/` da
# própria ISO, que é um repositório apt completo. Verificar AGORA, antes de
# extrair, é o que o D6 pede: descobrir que o bootloader é inalcançável depois
# de formatar e extrair seria descobrir tarde demais.
# O nome da suíte (trixie, bookworm, …) NÃO é escrito aqui: fixá-lo seria
# deduzir a versão da distro a partir do nome, e amarrar o instalador a um
# release. Procura-se a suíte que o artefato declara, e exige-se exatamente uma
# com repositório binário — ambiguidade para, não escolhe.
mapfile -t ISO_SUITES < <(
  find "${ISO_MOUNT}/dists" -maxdepth 1 -mindepth 1 -type d -printf '%f\n' 2>/dev/null |
  while IFS= read -r suite; do
    if compgen -G "${ISO_MOUNT}/dists/${suite}/main/binary-amd64/Packages*" >/dev/null; then
      printf '%s\n' "$suite"
    fi
  done
)
if [[ "${#ISO_SUITES[@]}" -ne 1 ]]; then
  strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
  die "esperava exatamente 1 suíte apt na ISO, encontrei ${#ISO_SUITES[@]} (${ISO_SUITES[*]:-nenhuma}) — o bootloader não teria de onde sair sem ambiguidade (6.4)"
fi
ISO_SUITE="${ISO_SUITES[0]}"
step "repositório apt da ISO: suíte '${ISO_SUITE}'"
# Procurados por NOME em todo o pool, não por caminho fixo: a árvore do pool é
# organizada pela primeira letra do pacote-fonte, que não é a do binário
# (grub2-common vem de "grub2", shim-signed de "shim-signed"). Escrever o
# caminho à mão seria codificar um detalhe de empacotamento que não nos pertence.
for pkg in grub-efi-amd64-signed shim-signed grub2-common efibootmgr; do
  if [[ -z "$(find "${ISO_MOUNT}/pool" -name "${pkg}_*.deb" -print -quit 2>/dev/null)" ]]; then
    strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
    die "pacote do bootloader ausente no pool da ISO: ${pkg} (6.4)"
  fi
done
step "pacotes do bootloader presentes no pool da ISO"

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
# Segunda linha: a suíte apt que a ISO declara. A instalação do bootloader
# (7.5) precisa dela e ela já foi descoberta e desambiguada aqui — redescobrir
# lá seria duas verdades sobre o mesmo artefato, que é como elas divergem.
printf '%s\n%s\n' "$TARGET_MOUNT" "$ISO_SUITE"
