#!/usr/bin/env bash
# Fase 8 (design.md D5, D7, D8, D10): bootloader assinado, sem grub-install,
# com o Windows no menu por prova, e a ESP só tocada onde a transação prova
# posse.
#
# Uso: install-bootloader.sh <plan.json> <target-disk> <target-mount>
set -euo pipefail
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${LIB_DIR}/common.sh"
# Falha sem `die` sai calada; este trap dá nome ao que parou (common.sh).
trap_uncaught_errors

require_root
require_cmd mount efibootmgr grub-script-check chroot blkid findmnt

PLAN_PATH="${1:?uso: install-bootloader.sh <plan.json> <target-disk> <target-mount>}"
TARGET_DISK="${2:?uso: install-bootloader.sh <plan.json> <target-disk> <target-mount>}"
TARGET_MOUNT="${3:?uso: install-bootloader.sh <plan.json> <target-disk> <target-mount>}"

PLAN_ID="$(json_get "$PLAN_PATH" '.planId')"
BOOT_PART_NUMBER="$(json_get "$PLAN_PATH" '.disk.boot.number')"
BOOT_PART="${TARGET_DISK}${BOOT_PART_NUMBER}"
VENDOR_DIR_REL="EFI/LinuxHub"
MARKER_NAME=".linuxhub-transaction"

ledger_start_step "target.bootloader-installed"

# --- 8.1: prova a partição de boot antes de escrever qualquer entrada (D10) ---
ESP_MOUNT="/run/linuxhub/esp"
mkdir -p "$ESP_MOUNT"
mount -o ro "$BOOT_PART" "$ESP_MOUNT" || die "falha ao montar a partição de boot para prová-la (8.1)"
if [[ ! -f "${ESP_MOUNT}/EFI/Microsoft/Boot/bootmgfw.efi" ]]; then
  strict_umount "$ESP_MOUNT"
  die "partição de boot não contém EFI/Microsoft/Boot/bootmgfw.efi — não é a do Windows (8.1)"
fi
strict_umount "$ESP_MOUNT"
log "partição de boot provada como sendo do Windows: $BOOT_PART"

mount -o rw "$BOOT_PART" "$ESP_MOUNT" || die "falha ao montar a partição de boot (rw) para instalar o bootloader"

# --- 8.3: marca de posse na ESP (D5) ---
VENDOR_DIR="${ESP_MOUNT}/${VENDOR_DIR_REL}"
MARKER_PATH="${VENDOR_DIR}/${MARKER_NAME}"
if [[ -d "$VENDOR_DIR" ]]; then
  if [[ ! -f "$MARKER_PATH" ]]; then
    strict_umount "$ESP_MOUNT"
    die "diretório de vendor na ESP existe sem marcador de posse (8.3)"
  fi
  EXISTING_OWNER="$(cat "$MARKER_PATH")"
  if [[ "$EXISTING_OWNER" != "$PLAN_ID" ]]; then
    strict_umount "$ESP_MOUNT"
    die "diretório de vendor na ESP pertence a outra transação: $EXISTING_OWNER (8.3)"
  fi
else
  mkdir -p "$VENDOR_DIR"
  printf '%s' "$PLAN_ID" > "$MARKER_PATH"
  sync
fi

# --- 8.4: cadeia assinada copiada do ALVO já extraído, não da mídia de execução (D8) ---
#
# A origem é `/usr/lib` dentro do alvo, que é onde os PACOTES põem os binários
# assinados — e não `/boot/efi/EFI/<vendor>`, que é onde o `grub-install` os
# deixaria depois de rodar.
#
# A diferença não é estilística: numa instalação nova o `/boot/efi` do alvo está
# vazio (a ESP é do Windows e só é montada aqui), então procurar ali achava nada
# e a fase morria dizendo que a cadeia "deveria ter sido pega em 6.4". Ler de
# /usr/lib não depende de nenhum passo anterior ter escrito na ESP — depende só
# de os pacotes estarem instalados, que é o que a fase 7.5 garante e verifica.
SHIM_SOURCE="${TARGET_MOUNT}/usr/lib/shim/shimx64.efi.signed"
GRUB_SOURCE="${TARGET_MOUNT}/usr/lib/grub/x86_64-efi-signed/grubx64.efi.signed"
MOK_SOURCE="${TARGET_MOUNT}/usr/lib/shim/mmx64.efi.signed"

[[ -f "$SHIM_SOURCE" ]] || die "shim assinado ausente no alvo: ${SHIM_SOURCE#$TARGET_MOUNT} (8.4) — a fase 7.5 deveria tê-lo instalado"
[[ -f "$GRUB_SOURCE" ]] || die "GRUB assinado ausente no alvo: ${GRUB_SOURCE#$TARGET_MOUNT} (8.4) — a fase 7.5 deveria tê-lo instalado"

cp "$SHIM_SOURCE" "${VENDOR_DIR}/shimx64.efi"
cp "$GRUB_SOURCE" "${VENDOR_DIR}/grubx64.efi"
# O MokManager é o que permite ao usuário gerenciar chaves quando o Secure Boot
# recusa algo. Opcional porque vem de um pacote separado (shim-helpers), mas sem
# ele um Secure Boot que rejeite a cadeia não deixa saída pela interface do shim.
if [[ -f "$MOK_SOURCE" ]]; then
  cp "$MOK_SOURCE" "${VENDOR_DIR}/mmx64.efi"
else
  log "aviso: mmx64.efi (MokManager) não encontrado no alvo — a cadeia funciona, mas sem tela de gestão de chaves do Secure Boot"
fi

# --- onde o GRUB assinado procura a configuração: LIDO do binário ---
#
# O prefixo fica COMPILADO dentro do grubx64.efi assinado. O do Debian é
# `/EFI/debian`. Ele não procura a configuração ao lado de si mesmo: mesmo
# carregado de EFI/LinuxHub, vai ler /EFI/debian/grub.cfg na ESP. Sem o stub
# nesse caminho exato, o boot cai no shell do GRUB — que é a falha mais cara
# possível, porque só aparece no reboot final, com tudo já instalado.
#
# O nome do vendor não é escrito aqui (§2: nome de distro não seleciona caminho
# de código). É lido do próprio artefato, e exige-se um único valor: se o binário
# declarar mais de um prefixo, não há como saber qual vale, e adivinhar aqui
# custaria o boot.
mapfile -t GRUB_PREFIXES < <(grep -aoE '/EFI/[A-Za-z0-9_.-]+' "$GRUB_SOURCE" | sort -u)
if [[ "${#GRUB_PREFIXES[@]}" -ne 1 ]]; then
  die "o GRUB assinado declara ${#GRUB_PREFIXES[@]} prefixos (${GRUB_PREFIXES[*]:-nenhum}) — sem um único, não dá para saber onde ele lê a configuração (8.4)"
fi
GRUB_PREFIX="${GRUB_PREFIXES[0]}"
log "prefixo lido do GRUB assinado: ${GRUB_PREFIX}"

# --- stub de configuração encadeando pela raiz (D8), espelhado no diretório
# de vendor da distro dentro do alvo, porque é lá que o GRUB assinado da
# distro procura configuração ao lado do binário. ---
ROOT_UUID="$(blkid -s UUID -o value "$(findmnt -no SOURCE "$TARGET_MOUNT")")"
[[ -n "$ROOT_UUID" ]] || die "não foi possível ler o UUID da partição raiz do alvo (8.4)"
GRUB_STUB="search --no-floppy --fs-uuid --set=root ${ROOT_UUID}
set prefix=(\$root)/boot/grub
configfile \$prefix/grub.cfg
"
printf '%s' "$GRUB_STUB" > "${VENDOR_DIR}/grub.cfg"

# O stub no caminho que o binário procura. Guardado pela mesma regra de posse do
# D5: se o diretório já existe sem o nosso marcador, ele é de outra instalação —
# sobrescrever a configuração de boot de outro sistema seria exatamente o tipo de
# dano que esta transação existe para não causar.
PREFIX_DIR="${ESP_MOUNT}${GRUB_PREFIX}"
PREFIX_MARKER="${PREFIX_DIR}/${MARKER_NAME}"
if [[ -d "$PREFIX_DIR" ]]; then
  if [[ ! -f "$PREFIX_MARKER" || "$(cat "$PREFIX_MARKER")" != "$PLAN_ID" ]]; then
    strict_umount "$ESP_MOUNT"
    die "a ESP já tem ${GRUB_PREFIX} de outra instalação — o GRUB assinado leria a configuração dela, e sobrescrevê-la quebraria aquele sistema (8.4)"
  fi
else
  mkdir -p "$PREFIX_DIR"
  printf '%s' "$PLAN_ID" > "$PREFIX_MARKER"
fi
printf '%s' "$GRUB_STUB" > "${PREFIX_DIR}/grub.cfg"
sync

# --- 8.2 + geração real do grub.cfg do alvo, com os-prober desligado ---
_target_binds_mounted=0
mount_target_binds() {
  [[ "$_target_binds_mounted" -eq 1 ]] && return 0
  mount --bind /dev "${TARGET_MOUNT}/dev"
  mount -t proc proc "${TARGET_MOUNT}/proc"
  mount -t sysfs sysfs "${TARGET_MOUNT}/sys"
  mount --bind "$ESP_MOUNT" "${TARGET_MOUNT}/boot/efi"
  _target_binds_mounted=1
}
umount_target_binds() {
  [[ "$_target_binds_mounted" -eq 1 ]] || return 0
  strict_umount "${TARGET_MOUNT}/boot/efi"
  strict_umount "${TARGET_MOUNT}/sys"
  strict_umount "${TARGET_MOUNT}/proc"
  strict_umount "${TARGET_MOUNT}/dev"
  _target_binds_mounted=0
}
trap umount_target_binds EXIT
mount_target_binds

echo 'GRUB_DISABLE_OS_PROBER=true' >> "${TARGET_MOUNT}/etc/default/grub"
chroot "$TARGET_MOUNT" grub-mkconfig -o /boot/grub/grub.cfg

# --- entrada do Windows escrita por nós, a partir da identidade provada (D10) ---
WINDOWS_ENTRY="menuentry \"Windows\" {
    insmod part_gpt
    insmod fat
    insmod chain
    search --no-floppy --fs-uuid --set=root $(blkid -s UUID -o value "$BOOT_PART")
    chainloader /EFI/Microsoft/Boot/bootmgfw.efi
}
"
printf '\n%s' "$WINDOWS_ENTRY" >> "${TARGET_MOUNT}/boot/grub/grub.cfg"

# --- 8.7: validar a configuração gerada — sintaxe, entradas prometidas, contagem exata ---
grub-script-check "${TARGET_MOUNT}/boot/grub/grub.cfg" || die "grub.cfg gerado tem sintaxe inválida (8.7)"
ROOT_MENUENTRY_COUNT="$(grep -cE '^menuentry ' "${TARGET_MOUNT}/boot/grub/grub.cfg")"
grep -q 'menuentry "Windows"' "${TARGET_MOUNT}/boot/grub/grub.cfg" || die "entrada do Windows ausente no grub.cfg gerado (8.7)"
log "grub.cfg gerado com $ROOT_MENUENTRY_COUNT entradas de nível raiz"
[[ "$ROOT_MENUENTRY_COUNT" -ge 2 ]] || die "grub.cfg gerado com menos entradas de nível raiz do que o esperado (8.7)"

umount_target_binds
trap - EXIT

# --- 8.5: registrar a entrada no firmware, primeiro na ordem de boot ---
efibootmgr --create --disk "$TARGET_DISK" --part "$BOOT_PART_NUMBER" \
  --label "LinuxHub" --loader "\\${VENDOR_DIR_REL//\//\\}\\shimx64.efi" >/dev/null
NEW_BOOT_ID="$(efibootmgr | grep 'LinuxHub' | tail -n1 | grep -oP '^Boot\K[0-9A-Fa-f]{4}')"
[[ -n "$NEW_BOOT_ID" ]] || die "falha ao localizar a entrada de boot recém-criada (8.5)"
CURRENT_ORDER="$(efibootmgr | grep -oP '^BootOrder: \K.*')"
NEW_ORDER="${NEW_BOOT_ID},${CURRENT_ORDER//${NEW_BOOT_ID},/}"
efibootmgr --bootorder "$NEW_ORDER" >/dev/null

# --- 8.6: remover o espaço temporário de boot na ESP, só depois de provar posse (D5) ---
STAGING_DIR_REL="$(json_get "$PLAN_PATH" '.disk.installer.stagingEspDirectory' 2>/dev/null || true)"
if [[ -n "$STAGING_DIR_REL" && "$STAGING_DIR_REL" != "null" ]]; then
  STAGING_DIR="${ESP_MOUNT}/${STAGING_DIR_REL}"
  STAGING_MARKER="${STAGING_DIR}/${MARKER_NAME}"
  if [[ -d "$STAGING_DIR" ]]; then
    if [[ -f "$STAGING_MARKER" && "$(cat "$STAGING_MARKER")" == "$PLAN_ID" ]]; then
      rm -rf "$STAGING_DIR"
      # `if`, e não `[[ … ]] && die`: no caminho de SUCESSO (o diretório sumiu) a
      # lista `&&` devolve 1, e esse 1 sobe como status do bloco. Mesma armadilha
      # que matou a fase 5 sem imprimir nada — e aqui seria pior, no último passo
      # do último script, depois da ESP já escrita.
      if [[ -d "$STAGING_DIR" ]]; then
        die "espaço temporário da ESP não foi removido (8.6)"
      fi
    else
      log "aviso: espaço temporário $STAGING_DIR_REL não tem marcador desta transação — não removido (8.6)"
    fi
  fi
fi

strict_umount "$ESP_MOUNT"

ledger_complete_step "target.bootloader-installed"
log "bootloader instalado e Windows presente no menu"
