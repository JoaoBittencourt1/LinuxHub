#!/usr/bin/env bash
# Fase 7 (design.md D9): configura o sistema extraído e faz com que ele
# deixe de ser live — não por remoção esperançosa, mas com verificação
# positiva do initramfs produzido (7.6).
#
# Uso: configure-target.sh <plan.json> <secret-file> <target-mount>
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"
# Falha sem `die` sai calada; este trap dá nome ao que parou (common.sh).
trap_uncaught_errors

require_root
require_cmd chroot mount umount

PLAN_PATH="${1:?uso: configure-target.sh <plan.json> <secret-file> <target-mount>}"
SECRET_FILE="${2:?uso: configure-target.sh <plan.json> <secret-file> <target-mount>}"
TARGET_MOUNT="${3:?uso: configure-target.sh <plan.json> <secret-file> <target-mount>}"

ledger_start_step "target.system-configured"

_target_binds_mounted=0
mount_target_binds() {
  [[ "$_target_binds_mounted" -eq 1 ]] && return 0
  mount --bind /dev "${TARGET_MOUNT}/dev"
  mount --bind /dev/pts "${TARGET_MOUNT}/dev/pts"
  mount -t proc proc "${TARGET_MOUNT}/proc"
  mount -t sysfs sysfs "${TARGET_MOUNT}/sys"
  mount -t efivarfs efivarfs "${TARGET_MOUNT}/sys/firmware/efi/efivars" 2>/dev/null || true
  _target_binds_mounted=1
}
umount_target_binds() {
  [[ "$_target_binds_mounted" -eq 1 ]] || return 0
  strict_umount "${TARGET_MOUNT}/sys/firmware/efi/efivars"
  strict_umount "${TARGET_MOUNT}/sys"
  strict_umount "${TARGET_MOUNT}/proc"
  strict_umount "${TARGET_MOUNT}/dev/pts"
  strict_umount "${TARGET_MOUNT}/dev"
  _target_binds_mounted=0
}
trap umount_target_binds EXIT

mount_target_binds

# --- 7.1: conta de usuário, segredo lido de arquivo, nunca embutido ---
USERNAME="$(json_get "$PLAN_PATH" '.account.username')"
HOSTNAME_VALUE="$(json_get "$PLAN_PATH" '.account.hostname')"
PASSWORD_HASH="$(read_account_secret_hash "$SECRET_FILE")"

chroot "$TARGET_MOUNT" useradd -m -s /bin/bash "$USERNAME"
chroot "$TARGET_MOUNT" usermod --password "$PASSWORD_HASH" "$USERNAME"
for group in sudo adm cdrom dip plugdev; do
  chroot "$TARGET_MOUNT" usermod -aG "$group" "$USERNAME" 2>/dev/null || true
done

# --- 7.2: hostname e montagens declaradas ---
printf '%s\n' "$HOSTNAME_VALUE" > "${TARGET_MOUNT}/etc/hostname"
sed -i "s/^127\\.0\\.1\\.1.*/127.0.1.1\\t${HOSTNAME_VALUE}/" "${TARGET_MOUNT}/etc/hosts" 2>/dev/null || \
  printf '127.0.1.1\t%s\n' "$HOSTNAME_VALUE" >> "${TARGET_MOUNT}/etc/hosts"

ROOT_UUID="$(blkid -s UUID -o value "$(findmnt -no SOURCE "$TARGET_MOUNT")")"
[[ -n "$ROOT_UUID" ]] || die "não foi possível ler o UUID da partição raiz recém-formatada"
cat > "${TARGET_MOUNT}/etc/fstab" <<EOF
UUID=${ROOT_UUID}  /  ext4  errors=remount-ro  0  1
EOF

# --- 7.3: idioma, teclado e fuso — os mesmos valores revisados no wizard ---
LOCALE_VALUE="$(json_get "$PLAN_PATH" '.locale.locale')"
TIMEZONE_VALUE="$(json_get "$PLAN_PATH" '.locale.timezone')"
KEYMAP_VALUE="$(json_get "$PLAN_PATH" '.locale.keymap')"

printf '%s UTF-8\n' "$LOCALE_VALUE" >> "${TARGET_MOUNT}/etc/locale.gen"
chroot "$TARGET_MOUNT" locale-gen
printf 'LANG=%s\n' "$LOCALE_VALUE" > "${TARGET_MOUNT}/etc/default/locale"
chroot "$TARGET_MOUNT" ln -sf "/usr/share/zoneinfo/${TIMEZONE_VALUE}" /etc/localtime
printf '%s\n' "$TIMEZONE_VALUE" > "${TARGET_MOUNT}/etc/timezone"
sed -i "s/^XKBLAYOUT=.*/XKBLAYOUT=\"${KEYMAP_VALUE}\"/" "${TARGET_MOUNT}/etc/default/keyboard" 2>/dev/null || true
chroot "$TARGET_MOUNT" setupcon --force 2>/dev/null || true

# --- 7.4: remover unidades de sessão live e seu estado (D9) ---
for unit in live-config.service live-config-user.service; do
  chroot "$TARGET_MOUNT" systemctl disable "$unit" 2>/dev/null || true
  chroot "$TARGET_MOUNT" systemctl mask "$unit" 2>/dev/null || true
done
rm -f "${TARGET_MOUNT}/etc/systemd/system/getty@tty1.service"
rm -f "${TARGET_MOUNT}/etc/systemd/system/serial-getty@ttyS0.service"
rm -f "${TARGET_MOUNT}/etc/systemd/system/linuxhub-installer.service"
rm -f "${TARGET_MOUNT}/etc/systemd/system/multi-user.target.wants/linuxhub-installer.service"
chroot "$TARGET_MOUNT" apt-get purge -y live-boot live-boot-initramfs-tools live-config live-config-systemd 2>/dev/null || true

# --- 7.5: reconstruir o initramfs do sistema instalado (D9) ---
TARGET_KERNEL_VERSION="$(chroot "$TARGET_MOUNT" bash -c 'ls /lib/modules' | sort -V | tail -n1)"
[[ -n "$TARGET_KERNEL_VERSION" ]] || die "nenhum kernel encontrado no sistema extraído"
chroot "$TARGET_MOUNT" update-initramfs -u -k "$TARGET_KERNEL_VERSION"

# --- 7.6: verificar o initramfs produzido — asserção positiva, não rm esperançoso ---
INITRAMFS_PATH="${TARGET_MOUNT}/boot/initrd.img-${TARGET_KERNEL_VERSION}"
[[ -f "$INITRAMFS_PATH" ]] || die "initramfs reconstruído não existe: $INITRAMFS_PATH (7.6)"

INITRAMFS_DUMP_DIR="$(mktemp -d)"
( cd "$INITRAMFS_DUMP_DIR" && \
  ( lsinitramfs "$INITRAMFS_PATH" 2>/dev/null || : ) > listing.txt )
if grep -qE '(^|/)(casper|live-boot|live-config|initrd\.live)(/|$)' "${INITRAMFS_DUMP_DIR}/listing.txt"; then
  rm -rf "$INITRAMFS_DUMP_DIR"
  die "initramfs reconstruído ainda contém configuração que força boot live (7.6)"
fi
rm -rf "$INITRAMFS_DUMP_DIR"

umount_target_binds
trap - EXIT

ledger_complete_step "target.system-configured"
log "configuração do alvo concluída"
