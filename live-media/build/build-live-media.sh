#!/usr/bin/env bash
# Task 1.1/1.3: pipeline reprodutível que constrói a mídia live a partir das
# fontes versionadas neste diretório. Roda em CI (Linux), nunca no dev
# machine Windows deste repo (design.md D0).
#
# Uso: sudo live-media/build/build-live-media.sh [saida-dir]
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "erro: precisa rodar como root (debootstrap/chroot/mksquashfs)." >&2
  exit 1
fi

for tool in debootstrap mksquashfs grub-mkrescue sha256sum chroot; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "erro: ferramenta de build ausente: $tool" >&2
    exit 1
  fi
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LIVE_MEDIA_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
OUT_DIR="$(cd "$(mkdir -p "${1:-${LIVE_MEDIA_DIR}/out}" && echo "${1:-${LIVE_MEDIA_DIR}/out}")" && pwd)"

# O work dir NÃO usa /tmp por padrão: em WSL (e em qualquer sistema com /tmp em
# tmpfs) /tmp é RAM, e o rootfs debootstrap + squashfs + árvore de ISO passa
# facilmente de 2 GB — o build morre sem espaço no meio, com bind mounts vivos.
# LINUXHUB_BUILD_WORK_ROOT permite apontar para outro lugar; o padrão
# (/var/tmp) é disco em qualquer distro, por definição do FHS.
# Checa AGORA que dá para gravar a saída, não depois de ~6 minutos de
# debootstrap + mksquashfs. Bug real: a ISO anterior estava anexada como DVD
# virtual numa VM, o Windows a mantinha aberta, e o build inteiro era descartado
# no último passo com um "Permission denied" que não dizia a causa.
ISO_OUT_PATH="${OUT_DIR}/linuxhub-live.iso"
if [[ -e "${ISO_OUT_PATH}" ]] && ! rm -f "${ISO_OUT_PATH}" 2>/dev/null; then
  echo "erro: não foi possível remover a ISO anterior em ${ISO_OUT_PATH}." >&2
  echo "      O arquivo está em uso. A causa mais comum é ele estar anexado como" >&2
  echo "      DVD virtual numa VM ligada — desanexe (ou desligue a VM) e rode de novo." >&2
  echo "      Alternativa: passe outro diretório de saída como primeiro argumento." >&2
  exit 1
fi
if ! touch "${ISO_OUT_PATH}.probe" 2>/dev/null; then
  echo "erro: ${OUT_DIR} não é gravável." >&2
  exit 1
fi
rm -f "${ISO_OUT_PATH}.probe"

BUILD_WORK_ROOT="${LINUXHUB_BUILD_WORK_ROOT:-/var/tmp}"
mkdir -p "${BUILD_WORK_ROOT}"
WORK_DIR="$(mktemp -d "${BUILD_WORK_ROOT}/linuxhub-live-build.XXXXXX")"
ROOTFS_DIR="${WORK_DIR}/rootfs"
ISO_TREE_DIR="${WORK_DIR}/iso-tree"
DEBIAN_SUITE="bookworm"
DEBIAN_MIRROR="http://deb.debian.org/debian"

cleanup() {
  local status=$?

  # `mount --make-rprivate` ANTES de desmontar: /dev e /dev/pts do chroot são
  # views propagadas das do host, e sem cortar a propagação o umount responde
  # "target is busy" mesmo sem nenhum processo segurando (qualquer terminal
  # aberto no host conta). Bug real: um build interrompido deixou /dev, /dev/pts,
  # /proc e /sys montados sob o work dir.
  for mnt in dev/pts dev proc sys; do
    target="${ROOTFS_DIR}/${mnt}"
    if mountpoint -q "${target}" 2>/dev/null; then
      mount --make-rprivate "${target}" 2>/dev/null || true
      # Desmontagem estrita, mesma regra que D11 exige do instalador: nunca -l.
      umount -R "${target}" 2>/dev/null || umount "${target}" 2>/dev/null || true
    fi
  done

  # NUNCA apagar o work dir com montagem viva sob ele: `rm -rf` atravessaria o
  # bind mount e apagaria o /dev REAL do sistema. Falhar em limpar é aceitável;
  # apagar o /dev da máquina de build não é.
  if mount | grep -q -- "${WORK_DIR}"; then
    echo "AVISO: montagens ainda ativas sob ${WORK_DIR}; NÃO removendo o diretório." >&2
    mount | grep -- "${WORK_DIR}" >&2 || true
    exit "$status"
  fi

  rm -rf "${WORK_DIR}"
  exit "$status"
}
# EXIT sozinho NÃO cobre interrupção: bash não roda o trap de EXIT quando morre
# por sinal não capturado, e foi assim que os bind mounts vazaram.
trap cleanup EXIT INT TERM HUP

echo "==> debootstrap ${DEBIAN_SUITE} (minbase) em ${ROOTFS_DIR}"
debootstrap --arch=amd64 --variant=minbase "${DEBIAN_SUITE}" "${ROOTFS_DIR}" "${DEBIAN_MIRROR}"

echo "==> montando binds para instalar pacotes no chroot"
mount --bind /dev "${ROOTFS_DIR}/dev"
mount --bind /dev/pts "${ROOTFS_DIR}/dev/pts"
mount -t proc proc "${ROOTFS_DIR}/proc"
mount -t sysfs sysfs "${ROOTFS_DIR}/sys"

cat > "${ROOTFS_DIR}/etc/apt/sources.list" <<EOF
deb ${DEBIAN_MIRROR} ${DEBIAN_SUITE} main
EOF

# Task 1.2: só os pacotes fixados em packages.list, um por linha, comentários
# e linhas em branco ignorados.
mapfile -t PACKAGES < <(grep -vE '^\s*(#|$)' "${LIVE_MEDIA_DIR}/packages.list" | awk '{print $1}')
echo "==> instalando ${#PACKAGES[@]} pacotes fixados: ${PACKAGES[*]}"
chroot "${ROOTFS_DIR}" env DEBIAN_FRONTEND=noninteractive apt-get update
chroot "${ROOTFS_DIR}" env DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends "${PACKAGES[@]}"
chroot "${ROOTFS_DIR}" apt-get clean

echo "==> aplicando rootfs-overlay/"
cp -a "${LIVE_MEDIA_DIR}/rootfs-overlay/." "${ROOTFS_DIR}/"

# Permissões explícitas, nunca as herdadas da origem: quando as fontes vêm de um
# filesystem Windows (WSL/9p) todo arquivo chega 0777, e o systemd recusa — ou,
# nas versões que só avisam, registra "marked world-writable ... Proceeding
# anyway" a cada boot. Executável só o que é executado.
find "${ROOTFS_DIR}/opt/linuxhub" -type d -exec chmod 0755 {} +
find "${ROOTFS_DIR}/opt/linuxhub" -type f -exec chmod 0644 {} +
chmod 0755 "${ROOTFS_DIR}/opt/linuxhub/bin/"*.sh "${ROOTFS_DIR}/opt/linuxhub/lib/"*.sh
chmod 0755 "${ROOTFS_DIR}/opt/linuxhub/bin/progress-ui.py"
chmod 0644 "${ROOTFS_DIR}/etc/systemd/system/linuxhub-installer.service"
chown -R root:root "${ROOTFS_DIR}/opt/linuxhub" "${ROOTFS_DIR}/etc/systemd/system/linuxhub-installer.service"

# Task 1.6 manda desviar getty@tty1 para /dev/null, para nenhum prompt de login
# aparecer durante a instalação. Isso fica para DEPOIS da fase 11.
#
# Enquanto o mecanismo não estiver validado, o getty é a única forma de
# distinguir dois estados que hoje são idênticos na tela — pretos, os dois:
#
#   prompt de login  -> a unidade do instalador NÃO rodou
#   tela sem prompt  -> a unidade rodou (e travou, ou está trabalhando calada)
#
# A unidade declara Conflicts=getty@tty1.service, então quando ela sobe o
# systemd derruba o getty sozinho: o prompt sumir É o sinal de que o instalador
# assumiu. E se algo der errado, sobra um shell para investigar de dentro, em
# vez de um console mudo. Mesma lição do `quiet` e da tela gráfica — esconder
# saída antes de o caminho estar provado custou vários ciclos de teste.
ln -sf /dev/null "${ROOTFS_DIR}/etc/systemd/system/serial-getty@ttyS0.service"
chroot "${ROOTFS_DIR}" systemctl set-default multi-user.target

echo "==> habilitando linuxhub-installer.service"
chroot "${ROOTFS_DIR}" systemctl enable linuxhub-installer.service

echo "==> gerando initramfs"
KERNEL_VERSION="$(chroot "${ROOTFS_DIR}" bash -c 'ls /lib/modules' | sort -V | tail -n1)"
chroot "${ROOTFS_DIR}" update-initramfs -u -k "${KERNEL_VERSION}"

for mnt in dev/pts dev proc sys; do
  umount "${ROOTFS_DIR}/${mnt}"
done

echo "==> empacotando filesystem.squashfs"
mkdir -p "${ISO_TREE_DIR}/live" "${ISO_TREE_DIR}/boot/grub"
mksquashfs "${ROOTFS_DIR}" "${ISO_TREE_DIR}/live/filesystem.squashfs" -comp xz -noappend

cp "${ROOTFS_DIR}/boot/vmlinuz-${KERNEL_VERSION}" "${ISO_TREE_DIR}/live/vmlinuz"
cp "${ROOTFS_DIR}/boot/initrd.img-${KERNEL_VERSION}" "${ISO_TREE_DIR}/live/initrd.img"
cp "${LIVE_MEDIA_DIR}/boot/grub/grub.cfg" "${ISO_TREE_DIR}/boot/grub/grub.cfg"

echo "==> montando ISO UEFI-apenas (D16 — sem plataforma i386-pc)"
# A ISO é montada no work dir (filesystem nativo) e só depois copiada para o
# destino. Escrever direto no destino quebra quando ele é um filesystem montado
# do Windows (WSL/9p): o xorriso reabre a saída como "pseudo-drive" e, se o
# arquivo já existe de um build anterior, morre com
# "libburn : SORRY : Failed to open device (a pseudo-drive) : Permission denied"
# — ou seja, o primeiro build passava e todo rebuild falhava. Bug real.
#
# Só -d aponta módulos x86_64-efi: sem grub-pc-bin instalado, grub-mkrescue
# não tem como emitir catálogo El Torito BIOS mesmo se pedíssemos.
ISO_BUILD_PATH="${WORK_DIR}/linuxhub-live.iso"
# -J (Joliet) porque o Windows precisa LER esta ISO para copiar live/ para a
# partição de boot. Sem Joliet ele cai em ISO9660 puro e vê nomes 8.3 em
# maiúsculas ('FILESYST.SQU' no lugar de 'filesystem.squashfs') — nomes que o
# initramfs não procura. Bug real: a cópia produzia uma partição que o live-boot
# não reconhecia. O lado Windows normaliza os nomes de qualquer forma, mas uma
# ISO legível dos dois lados é o certo, não a compensação sozinha.
grub-mkrescue -o "${ISO_BUILD_PATH}" "${ISO_TREE_DIR}" \
  -d /usr/lib/grub/x86_64-efi \
  -- -volid LINUXHUB_LIVE -J -joliet-long

echo "==> hash e manifesto"
ISO_SHA256="$(sha256sum "${ISO_BUILD_PATH}" | cut -d' ' -f1)"
ISO_SIZE_BYTES="$(stat -c%s "${ISO_BUILD_PATH}")"

# Remover antes de copiar, pela mesma razão: sobrescrever no lugar através do
# 9p é o que falha. A cópia cria o arquivo do zero. A checagem lá no início já
# provou que isto é possível — se falhar aqui mesmo assim, a ISO construída é
# preservada e o caminho dela é informado, em vez de sumir com o work dir.
if ! rm -f "${ISO_OUT_PATH}" || ! cp "${ISO_BUILD_PATH}" "${ISO_OUT_PATH}"; then
  PRESERVED="/var/tmp/linuxhub-live.$(date +%Y%m%d%H%M%S).iso"
  cp "${ISO_BUILD_PATH}" "${PRESERVED}" 2>/dev/null || true
  echo "erro: falha ao gravar ${ISO_OUT_PATH} (arquivo em uso?)." >&2
  echo "      A ISO construída foi preservada em ${PRESERVED} — não precisa reconstruir." >&2
  exit 1
fi

# Confere a cópia: um arquivo truncado aqui viraria uma mídia que o firmware
# recusa depois do reboot, sem log e sem como avisar.
COPIED_SHA256="$(sha256sum "${OUT_DIR}/linuxhub-live.iso" | cut -d' ' -f1)"
if [[ "${COPIED_SHA256}" != "${ISO_SHA256}" ]]; then
  echo "erro: a cópia da ISO para ${OUT_DIR} não confere com a construída (${COPIED_SHA256} != ${ISO_SHA256})" >&2
  exit 1
fi

printf '%s  linuxhub-live.iso\n' "${ISO_SHA256}" > "${OUT_DIR}/linuxhub-live.iso.sha256"
cat > "${OUT_DIR}/linuxhub-live.manifest.json" <<EOF
{
  "sha256": "${ISO_SHA256}",
  "sizeBytes": ${ISO_SIZE_BYTES}
}
EOF

echo "==> build concluído: ${OUT_DIR}/linuxhub-live.iso (${ISO_SIZE_BYTES} bytes, sha256 ${ISO_SHA256})"
