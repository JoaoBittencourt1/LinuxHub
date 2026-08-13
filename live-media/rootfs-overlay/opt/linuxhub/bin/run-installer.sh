#!/usr/bin/env bash
# Task 1.1: ponto de entrada único da mídia live, chamado por
# linuxhub-installer.service. Orquestra as fases 4 a 9 chamando os scripts
# fixos em lib/ na ordem — nenhuma lógica de instalação vive aqui além da
# sequência e do espelhamento de progresso (D14).
set -euo pipefail
LIB_DIR="/opt/linuxhub/lib"
# shellcheck source=../lib/common.sh
source "${LIB_DIR}/common.sh"
# shellcheck source=../lib/mirror-state.sh
source "${LIB_DIR}/mirror-state.sh"

require_root

on_fatal_error() {
  local exit_code=$?

  # Volta o console para a tty1 ANTES de imprimir. Se a tela de progresso subiu,
  # o Xorg trocou o VT ativo — e a mensagem de erro sairia num VT que ninguém
  # está vendo. Bug real: uma falha aparecia como tela preta muda, sem uma
  # palavra na tela, porque os gettys estão mascarados (task 1.6) e não há
  # shell nenhuma para onde cair.
  if [[ -n "${LINUXHUB_UI_PID:-}" ]]; then
    kill "$LINUXHUB_UI_PID" 2>/dev/null || true
  fi
  command -v chvt >/dev/null 2>&1 && chvt 1 2>/dev/null || true

  {
    echo
    echo "############################################################"
    echo "#  A INSTALAÇÃO PAROU (código $exit_code)"
    echo "#"
    echo "#  Nada foi escrito no disco depois deste ponto."
    echo "#  A mensagem da causa está logo acima desta caixa."
    echo "#"
    echo "#  Diagnóstico: journalctl -b  |  cat $LINUXHUB_UI_LOG"
    echo "############################################################"
    echo
  } > /dev/tty1 2>/dev/null || true

  log "instalação interrompida (código $exit_code) — o registro mostra o passo que começou e não concluiu"

  # Sem isto o systemd encerra a unidade e a tela volta a ficar preta e muda
  # antes de alguém conseguir ler o erro. A instalação já parou; segurar o
  # console é a única forma de a causa chegar a quem está olhando.
  exec sleep infinity
}
trap on_fatal_error ERR

# A tela de progresso é apresentação (D14): não pode impedir, atrasar nem
# derrubar a instalação. Toda falha aqui é aviso, nunca erro.
#
# Chamada DEPOIS da descoberta e da revalidação, de propósito: o Xorg troca o
# VT ativo ao subir, e com isso qualquer mensagem das fases iniciais sai numa
# tela que ninguém está vendo. Essas fases são as que mais falham e as que mais
# precisam ser lidas (plano ausente, disco divergente, hash errado); a tela
# existe para cobrir a extração, que é longa e silenciosa. Levantá-la antes
# trocava diagnóstico por estética — e foi o que transformou várias falhas
# distintas na mesma "tela preta" indistinguível.
start_progress_ui() {
  if ! command -v xinit >/dev/null 2>&1; then
    log "aviso: xinit ausente — instalação segue mostrando progresso só no console"
    return 0
  fi

  # `xinit` sobe o servidor X E roda a tela como cliente dele. Chamar o python
  # direto (como estava) morre com "no display name and no \$DISPLAY": os
  # pacotes do Xorg estão instalados, mas ninguém iniciava o servidor — bug real
  # no primeiro boot da mídia. Vai para vt7 porque a unidade systemd já é dona
  # da tty1; o Xorg troca de VT sozinho ao subir.
  xinit /usr/bin/python3 /opt/linuxhub/bin/progress-ui.py -- :0 vt7 -nolisten tcp \
    >"$LINUXHUB_UI_LOG" 2>&1 &
  LINUXHUB_UI_PID=$!

  # Sem o `&` seguido de espera curta, um X que falha no arranque só apareceria
  # como tela preta. Um segundo basta para distinguir "subiu" de "morreu já".
  sleep 1
  if ! kill -0 "$LINUXHUB_UI_PID" 2>/dev/null; then
    log "aviso: a tela gráfica não subiu; instalação segue pelo console (log em $LINUXHUB_UI_LOG)"
    sed -n '1,20p' "$LINUXHUB_UI_LOG" 2>/dev/null | while IFS= read -r l; do log "  ui: $l"; done
    return 0
  fi
  log "tela de progresso iniciada (pid $LINUXHUB_UI_PID)"
}

LINUXHUB_UI_LOG="/run/linuxhub-progress-ui.log"
LINUXHUB_UI_PID=""

# Prova de vida, direto no /dev/tty1 — sem passar pelo `log`, pelo journal nem
# pelo roteamento de console do systemd, que `quiet` na linha de kernel pode
# silenciar. Se esta caixa não aparecer na tela, o problema é ANTES do
# instalador: a unidade não subiu, ou o sistema nem chegou no multi-user.
# Distinguir "não rodou" de "rodou e falhou calado" custava um boot inteiro de
# adivinhação a cada tentativa.
{
  echo
  echo "############################################################"
  echo "#  LinuxHub - instalador live iniciado"
  echo "#  $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "############################################################"
  echo
} > /dev/tty1 2>/dev/null || true

# O canal de progresso precisa existir desde já (emit_progress escreve nele),
# mas a TELA só sobe depois da revalidação — ver abaixo.
mkfifo -m 600 "$LINUXHUB_PROGRESS_FIFO" 2>/dev/null || true
progress_open_channel

emit_progress "install.discovering-plan"

# discover-plan.sh para (D13) quando não há exatamente um plano válido. Sem esta
# checagem o `set -u` estouraria com "unbound variable" no índice do array — que
# é verdade, mas não diz nada a quem está olhando a tela.
if ! mapfile -t DISCOVERY < <("${LIB_DIR}/discover-plan.sh") || [[ "${#DISCOVERY[@]}" -lt 4 ]]; then
  die "nenhum plano de instalação utilizável foi encontrado — nada foi escrito no disco"
fi
PLAN_PATH="${DISCOVERY[0]}"
STATE_PATH="${DISCOVERY[1]}"
LINUXHUB_WINDOWS_MOUNT="${DISCOVERY[2]}"
export LINUXHUB_WINDOWS_MOUNT
# Se o volume só montou em ro (Fast Startup do Windows), o espelhamento do
# registro (D3) vira no-op com aviso — nunca falha de instalação (research §O).
LINUXHUB_WINDOWS_MOUNT_WRITABLE="${DISCOVERY[3]}"
export LINUXHUB_WINDOWS_MOUNT_WRITABLE
LINUXHUB_STATE_WINDOWS_PATH="$STATE_PATH"
export LINUXHUB_STATE_WINDOWS_PATH

cp "$STATE_PATH" "$LINUXHUB_RUN_STATE"

PLAN_ID="$(json_get "$PLAN_PATH" '.planId')"
SECRET_FILE="$(dirname "$PLAN_PATH")/account-secret.env"

emit_progress "install.revalidating"
mapfile -t REVALIDATION < <("${LIB_DIR}/revalidate.sh" "$PLAN_PATH")
TARGET_DISK="${REVALIDATION[0]}"
ARTIFACT_PATH="${REVALIDATION[1]}"

# Só agora a tela sobe: daqui para a frente vem a parte longa e silenciosa
# (formatar, extrair, configurar), que é onde uma tela ajuda. Tudo que podia
# falhar de forma que exige LEITURA já passou, e passou visível no console.
start_progress_ui

emit_progress "install.preparing-disk"
TARGET_PART="$("${LIB_DIR}/prepare-disk.sh" "$PLAN_PATH" "$TARGET_DISK")"

TARGET_MOUNT="$("${LIB_DIR}/verify-and-extract.sh" "$PLAN_PATH" "$ARTIFACT_PATH" "$TARGET_PART")"

emit_progress "install.configuring"
"${LIB_DIR}/configure-target.sh" "$PLAN_PATH" "$SECRET_FILE" "$TARGET_MOUNT"

emit_progress "install.installing-bootloader"
"${LIB_DIR}/install-bootloader.sh" "$PLAN_PATH" "$TARGET_DISK" "$TARGET_MOUNT"

emit_progress "install.verifying"
"${LIB_DIR}/verify-installation.sh" "$PLAN_PATH" "$TARGET_DISK" "$TARGET_MOUNT"

jq '.status = "succeeded" | .phase = "complete"' "$LINUXHUB_RUN_STATE" > "${LINUXHUB_RUN_STATE}.tmp"
mv -f "${LINUXHUB_RUN_STATE}.tmp" "$LINUXHUB_RUN_STATE"
mirror_state_to_windows || log "aviso: falha ao espelhar estado final (não fatal, research §O)"

strict_umount "$TARGET_MOUNT"
strict_umount "$LINUXHUB_WINDOWS_MOUNT"

emit_progress "install.complete"
log "instalação de $PLAN_ID concluída — reinicie para entrar no sistema instalado"
