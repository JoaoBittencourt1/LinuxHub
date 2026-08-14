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

# --- 6.2: qual filesystem instalar — DECLARADO pelo artefato, não escolhido por nós ---
#
# "Exatamente um .squashfs" era a regra antiga, e ela reprovava toda ISO de
# desktop do Ubuntu desde a 23.10: a 24.04.4 tem 41 arquivos .squashfs (uma
# camada base, um delta por variante, um por idioma). A regra não estava
# detectando artefato corrompido — estava detectando que o formato mudou.
#
# Quando o artefato traz `casper/install-sources.yaml`, ele DECLARA quais fontes
# são instaláveis e qual é a padrão. Ler essa declaração é a diferença entre
# escolher pelo que o artefato diz e escolher pelo maior arquivo, que era o
# palpite disponível.
INSTALL_SOURCES="${ISO_MOUNT}/casper/install-sources.yaml"
if [[ -f "$INSTALL_SOURCES" ]]; then
  # Sem parser de YAML na mídia (D1: nada de dependência nova para ler um
  # arquivo de 30 linhas). O formato aqui é uma lista plana de mapas, e o que
  # interessa é o `path:` do item marcado `default: true` — lido pela estrutura
  # do documento, não por posição fixa.
  SOURCE_PATH="$(awk '
    /^- / { current_default = 0; current_path = "" }
    /^[- ] *default: *true/ { current_default = 1 }
    /^ *path: */ {
      p = $0; sub(/^ *path: */, "", p); gsub(/["\r]/, "", p)
      if (current_path == "") current_path = p
    }
    /^ *type: */ { if (current_default && current_path != "") { print current_path; exit } }
  ' "$INSTALL_SOURCES")"
  [[ -n "$SOURCE_PATH" ]] || { strict_umount "$ISO_MOUNT"; die "install-sources.yaml não declara uma fonte padrão instalável (6.2)"; }

  # `fsimage-layered`: o nome do arquivo É a cadeia de camadas, separada por
  # pontos. `minimal.standard.squashfs` significa `minimal` + `minimal.standard`,
  # empilhadas nessa ordem. A fonte padrão do Ubuntu é `minimal.squashfs`, uma
  # camada só — e é a única forma suportada aqui.
  #
  # Mais de uma camada PARA em vez de extrair a de cima sozinha: um delta
  # extraído isolado não é sistema nenhum (`minimal.standard.squashfs` tem só
  # etc/, snap/, usr/ e var/, sem os-release sequer). Empilhar é trabalho a
  # fazer, não um caso a improvisar no meio de uma instalação.
  SOURCE_BASENAME="$(basename "$SOURCE_PATH" .squashfs)"
  LAYER_COUNT="$(awk -F. '{print NF}' <<< "$SOURCE_BASENAME")"
  if [[ "$LAYER_COUNT" -ne 1 ]]; then
    strict_umount "$ISO_MOUNT"
    die "a fonte padrão do artefato ($SOURCE_PATH) tem $LAYER_COUNT camadas; só uma é suportada — extrair um delta sozinho não produz um sistema (6.2)"
  fi

  SQUASHFS_FILE="${ISO_MOUNT}/casper/${SOURCE_PATH}"
  [[ -f "$SQUASHFS_FILE" ]] || { strict_umount "$ISO_MOUNT"; die "a fonte declarada não existe no artefato: casper/${SOURCE_PATH} (6.2)"; }
  step "fonte declarada pelo artefato: casper/${SOURCE_PATH}"
else
  # Artefato sem declaração: aí a regra antiga vale, e um único filesystem é o
  # que impede a ambiguidade.
  mapfile -t SQUASHFS_CANDIDATES < <(find "$ISO_MOUNT" -iname '*.squashfs' -type f)
  if [[ "${#SQUASHFS_CANDIDATES[@]}" -ne 1 ]]; then
    strict_umount "$ISO_MOUNT"
    die "artefato sem install-sources.yaml e com ${#SQUASHFS_CANDIDATES[@]} filesystems squashfs — esperado exatamente 1 (6.2)"
  fi
  SQUASHFS_FILE="${SQUASHFS_CANDIDATES[0]}"
  step "fonte única do artefato: ${SQUASHFS_FILE#$ISO_MOUNT/}"
fi

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

# --- 6.4b: o sistema extraído vai ter kernel? ---
#
# Sem kernel não há boot, e descobrir isso depois de formatar e extrair seria
# descobrir com o disco já mexido. Mas "ter kernel" tem duas formas legítimas, e
# tratá-las como uma só foi o que reprovou a ISO do Ubuntu inteira:
#
#   a) o kernel vem DENTRO do squashfs (ISOs live à moda antiga);
#   b) o kernel vem do `pool/` da própria ISO, instalado depois — que é o que o
#      Ubuntu faz desde que passou a `fsimage-layered`. A camada base não tem
#      kernel nem módulos de propósito.
#
# O que não pode é nenhuma das duas. A pergunta continua não sendo o nome da
# distro (§2): é se existe kernel alcançável a partir deste artefato.
if compgen -G "${SQUASH_MOUNT}/boot/vmlinuz-*" >/dev/null && \
   compgen -G "${SQUASH_MOUNT}/lib/modules/*/kernel" >/dev/null; then
  TARGET_KERNEL_PACKAGE=""
  step "kernel presente no artefato: $(basename "$(compgen -G "${SQUASH_MOUNT}/boot/vmlinuz-*" | head -1)")"
else
  # A versão é LIDA do kernel que a própria ISO carrega — é o que ela declara
  # como seu, e é o mesmo que o instalador da distro instala. Escolher entre os
  # vários linux-image do pool por conta própria seria adivinhar qual dos dois
  # (6.8 ou 6.17, nesta ISO) é o certo.
  ISO_KERNEL_IMAGE="${ISO_MOUNT}/casper/vmlinuz"
  [[ -f "$ISO_KERNEL_IMAGE" ]] || ISO_KERNEL_IMAGE="${ISO_MOUNT}/live/vmlinuz"
  if [[ ! -f "$ISO_KERNEL_IMAGE" ]]; then
    strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
    die "artefato sem kernel no squashfs e sem kernel próprio para identificar a versão — o sistema extraído não arrancaria (6.4)"
  fi
  KERNEL_VERSION="$(grep -aoE '[0-9]+\.[0-9]+\.[0-9]+-[0-9]+-[a-z0-9]+' "$ISO_KERNEL_IMAGE" | head -1)"
  if [[ -z "$KERNEL_VERSION" ]]; then
    strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
    die "não foi possível ler a versão do kernel que o artefato carrega (6.4)"
  fi
  TARGET_KERNEL_PACKAGE="linux-image-${KERNEL_VERSION}"
  if [[ -z "$(find "${ISO_MOUNT}/pool" -name "${TARGET_KERNEL_PACKAGE}_*.deb" -print -quit 2>/dev/null)" ]]; then
    strict_umount "$SQUASH_MOUNT"; strict_umount "$ISO_MOUNT"
    die "o artefato não tem kernel no squashfs nem ${TARGET_KERNEL_PACKAGE} no pool — o sistema extraído não arrancaria (6.4)"
  fi
  step "kernel virá do pool do artefato: ${TARGET_KERNEL_PACKAGE}"
fi

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
# Três linhas: ponto de montagem do alvo, suíte apt declarada pela ISO, e o
# pacote de kernel a instalar (vazio quando o kernel já veio no squashfs).
#
# Tudo isso já foi descoberto e desambiguado aqui. Redescobrir na fase seguinte
# seria manter duas verdades sobre o mesmo artefato — que é como elas divergem.
printf '%s\n%s\n%s\n' "$TARGET_MOUNT" "$ISO_SUITE" "$TARGET_KERNEL_PACKAGE"
