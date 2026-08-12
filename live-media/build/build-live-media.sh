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
chmod 0755 "${ROOTFS_DIR}/opt/linuxhub/bin/"*.sh "${ROOTFS_DIR}/opt/linuxhub/lib/"*.sh

# Task 1.6: getty@tty1 e serial-getty@ttyS0 desviados para /dev/null — mask
# systemd padrão, não uma unidade sobrescrita, para não deixar nenhum prompt
# de login possível.
ln -sf /dev/null "${ROOTFS_DIR}/etc/systemd/system/getty@tty1.service"
ln -sf /dev/null "${ROOTFS_DIR}/etc/systemd/system/serial-getty@ttyS0.service"

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
# Só -d aponta módulos x86_64-efi: sem grub-pc-bin instalado, grub-mkrescue
# não tem como emitir catálogo El Torito BIOS mesmo se pedíssemos.
grub-mkrescue -o "${OUT_DIR}/linuxhub-live.iso" "${ISO_TREE_DIR}" \
  -d /usr/lib/grub/x86_64-efi \
  -- -volid LINUXHUB_LIVE

echo "==> hash e manifesto"
( cd "${OUT_DIR}" && sha256sum linuxhub-live.iso > linuxhub-live.iso.sha256 )
ISO_SHA256="$(cut -d' ' -f1 "${OUT_DIR}/linuxhub-live.iso.sha256")"
ISO_SIZE_BYTES="$(stat -c%s "${OUT_DIR}/linuxhub-live.iso")"
cat > "${OUT_DIR}/linuxhub-live.manifest.json" <<EOF
{
  "sha256": "${ISO_SHA256}",
  "sizeBytes": ${ISO_SIZE_BYTES}
}
EOF

echo "==> build concluído: ${OUT_DIR}/linuxhub-live.iso (${ISO_SIZE_BYTES} bytes, sha256 ${ISO_SHA256})"
