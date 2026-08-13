#!/usr/bin/env bash
# Biblioteca compartilhada pelos scripts de fase do instalador live (D1: bash
# fixo, versionado, sem gerador). Toda fase faz `source` deste arquivo.
#
# Convenção de erro: toda função que pode falhar termina o processo via
# `die` — não há "tentar continuar" em nenhum ponto deste instalador (§6.1).

set -euo pipefail

LINUXHUB_LOG_PREFIX="[linuxhub-installer]"

# Escreve em stderr E direto na tty1. O stderr sozinho depende do roteamento de
# console do systemd, que já se mostrou capaz de engolir tudo — e um instalador
# sem tela é indistinguível de um instalador travado. A tty1 é a única
# superfície que provou funcionar em todos os boots até agora.
log() {
  echo "${LINUXHUB_LOG_PREFIX} $*" >&2
  echo "${LINUXHUB_LOG_PREFIX} $*" > /dev/tty1 2>/dev/null || true
}

# Marca de passo, para o console mostrar o AVANÇO e não só o resultado. Sem
# isto, um travamento no meio de uma fase longa (montar NTFS, conferir hash de
# 6 GB) é visualmente idêntico a um travamento no início dela.
step() {
  log "==> $*"
}

die() {
  log "ERRO: $*"
  exit 1
}

require_root() {
  if [[ "${EUID}" -ne 0 ]]; then
    die "precisa rodar como root"
  fi
}

require_cmd() {
  local cmd
  for cmd in "$@"; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
      die "ferramenta ausente: $cmd (deveria ter sido instalada no build da mídia — task 1.2)"
    fi
  done
}

# --- Desmontagem estrita (D11) ---
# Nunca `umount -l` em lugar nenhum deste instalador. Um `umount -l` esconde
# referências abertas e faz a formatação seguinte parecer segura com o
# dispositivo ainda em uso — é o modo de falha direto do incidente de
# 2026-08-05.
#
# Aceita um ponto de montagem OU um dispositivo de bloco. A distinção importa:
# `mountpoint -q /dev/sda3` responde "não" mesmo com o /dev/sda3 montado em
# outro lugar, porque /dev/sda3 não é um ponto de montagem — é um dispositivo.
# Bug real: a fase 5 chamava strict_umount e assert_not_mounted com CAMINHOS DE
# DISPOSITIVO, então o desmonte não desmontava nada e a asserção seguinte
# passava sem ter verificado coisa alguma — autorizando a formatação com o disco
# ainda montado. Uma verificação que não verifica é pior que nenhuma.
#
# Um dispositivo pode estar montado em vários pontos; todos são desmontados.
mount_targets_of() {
  local arg="$1"
  if [[ -b "$arg" ]]; then
    findmnt -rno TARGET --source "$arg" 2>/dev/null || true
  elif mountpoint -q "$arg" 2>/dev/null; then
    printf '%s\n' "$arg"
  fi
}

strict_umount() {
  local target="$1" mnt
  # Do mais profundo para o mais raso: desmontar um pai antes do filho falha com
  # "target is busy", o que aqui viraria uma morte por ordenação e não por
  # holder real.
  while IFS= read -r mnt; do
    [[ -n "$mnt" ]] || continue
    umount "$mnt" || die "falha ao desmontar $mnt (de $target; umount estrito, -l é proibido)"
  done < <(mount_targets_of "$target" | awk '{print gsub(/\//,"/"), $0}' | sort -rn | cut -d' ' -f2-)
}

assert_not_mounted() {
  local target="$1" remaining
  remaining="$(mount_targets_of "$target")"
  if [[ -n "$remaining" ]]; then
    die "asserção falhou: $target ainda está montado em: $(echo "$remaining" | tr '\n' ' ')"
  fi
}

# Task 5.6: diagnóstico do que está segurando um dispositivo, despejado antes
# de morrer — o modo de falha que ninguém consegue depurar depois do fato.
dump_holders_diagnostic() {
  local device="$1"
  log "--- diagnóstico de holders para ${device} ---"
  findmnt --output SOURCE,TARGET,FSTYPE 2>/dev/null | grep -F "${device}" || log "(findmnt: nenhuma montagem correspondente)"
  if [[ -e "/sys/class/block/$(basename "$device")/holders" ]]; then
    log "holders em sysfs: $(ls "/sys/class/block/$(basename "$device")/holders" 2>/dev/null || true)"
  fi
  fuser -v "$device" 2>&1 | while IFS= read -r line; do log "fuser: $line"; done || true
  log "--- fim do diagnóstico ---"
}

# Task 5.3: usuário aberto na partição, exigindo duas amostras ociosas
# consecutivas. Uma sonda udev/blkid transitória não é motivo para abortar;
# um usuário persistente é. Não usar `fuser -m` (research §H).
assert_partition_idle() {
  local device="$1"
  local sample
  for sample in 1 2; do
    if fuser "$device" >/dev/null 2>&1; then
      dump_holders_diagnostic "$device"
      die "partição $device tem usuário aberto (amostra $sample de 2)"
    fi
    [[ "$sample" -eq 1 ]] && sleep 1
  done
}

# --- JSON (D1: nenhum valor de usuário vira sintaxe de shell) ---
json_get() {
  local file="$1" filter="$2"
  jq -er "$filter" "$file" || die "campo ausente/ inválido em $file: $filter"
}

json_validate_schema() {
  local file="$1" schema="$2"
  # jq garante que é JSON bem-formado; a validação de schema completa roda
  # do lado Windows antes de publicar (InstallationPlanValidator). Aqui a
  # mídia live confere apenas que o documento parseia e tem os campos que
  # esta fase precisa — exigido explicitamente por cada chamador via
  # json_get, que já falha (die) em campo ausente.
  jq -e . "$file" >/dev/null 2>&1 || die "$file não é JSON válido (contra $schema)"
}

# Traduz um caminho no formato do Windows ("C:\ProgramData\...") para o caminho
# equivalente dentro de um ponto de montagem Linux. A letra de unidade é
# descartada de propósito: o volume já foi escolhido pela descoberta (D13), e
# reinterpretar a letra aqui abriria espaço para ler de um volume que não é o
# que foi validado.
windows_path_to_local() {
  local mount="$1" windows_path="$2" relative
  relative="${windows_path#?:}"
  relative="${relative//\\//}"
  printf '%s\n' "${mount}${relative}"
}

atomic_write() {
  local target="$1" content="$2"
  local tmp
  tmp="$(mktemp "${target}.XXXXXX")"
  printf '%s' "$content" > "$tmp"
  sync
  mv -f "$tmp" "$target"
  sync
}

# --- Segredo da conta (D1, research §B): nunca em linha de comando/script,
# rejeita caractere de controle antes de qualquer uso. ---
read_account_secret_hash() {
  local secret_file="$1" hash
  [[ -f "$secret_file" ]] || die "arquivo de segredo da conta ausente: $secret_file"
  hash="$(cat "$secret_file")"
  if [[ ! "$hash" =~ ^\$6\$ ]]; then
    die "segredo da conta não está no formato esperado (\$6\$…)"
  fi
  if printf '%s' "$hash" | grep -qP '[\x00-\x08\x0B\x0C\x0E-\x1F]'; then
    die "segredo da conta contém caractere de controle"
  fi
  printf '%s' "$hash"
}

chroot_exec() {
  local target="$1"; shift
  chroot "$target" "$@"
}

# --- Progresso (D14): nunca autoridade sobre o registro; marcador desconhecido para a instalação. ---
LINUXHUB_PRESENTATION_CATALOG="/opt/linuxhub/catalog/presentation-catalog.json"
LINUXHUB_PROGRESS_FIFO="/run/linuxhub-progress.fifo"

# Abre o FIFO de progresso em LEITURA-ESCRITA e mantém o descritor aberto.
#
# Abrir um FIFO só para escrita BLOQUEIA até alguém abrir para leitura. Se a
# tela de progresso não subiu (ou morreu), não há leitor — e o primeiro
# emit_progress travaria a instalação inteira, para sempre, sem mensagem
# nenhuma. Bug real no primeiro boot da mídia: o Tk morreu por falta de
# $DISPLAY e o instalador ficou pendurado no emit_progress seguinte.
#
# Com O_RDWR sempre existe um leitor (nós mesmos), então a escrita nunca
# bloqueia e nunca falha. É a tradução direta de D14 para código: a
# apresentação não tem autoridade nenhuma sobre a instalação — nem para
# atrasá-la.
progress_open_channel() {
  [[ -p "$LINUXHUB_PROGRESS_FIFO" ]] || return 0
  exec 9<>"$LINUXHUB_PROGRESS_FIFO" 2>/dev/null || true
}

emit_progress() {
  local marker="$1" detail_percent="${2:-}"
  if ! jq -e --arg m "$marker" '.markers[$m]' "$LINUXHUB_PRESENTATION_CATALOG" >/dev/null 2>&1; then
    die "marcador de progresso desconhecido no catálogo de apresentação: $marker"
  fi
  # `>&9` só existe se progress_open_channel rodou; o redirecionamento falha
  # silenciosamente quando não, e a instalação segue igual.
  printf '%s\t%s\n' "$marker" "$detail_percent" >&9 2>/dev/null || true
  log "progresso: $marker ${detail_percent:+(${detail_percent}%)}"
}

# --- Registro transacional (D3): cópia de trabalho em /run, espelhada no
# volume do Windows a cada transição aceita. Falha em espelhar NUNCA vira
# falha de instalação — log é evidência, não sinal (research §O). ---
LINUXHUB_RUN_STATE="/run/linuxhub-installation-state.json"

ledger_start_step() {
  local step_id="$1"
  local now
  now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  jq --arg id "$step_id" --arg now "$now" \
    '.activeStep = $id | .updatedAtUtc = $now' \
    "$LINUXHUB_RUN_STATE" > "${LINUXHUB_RUN_STATE}.tmp"
  mv -f "${LINUXHUB_RUN_STATE}.tmp" "$LINUXHUB_RUN_STATE"
  mirror_state_to_windows || log "aviso: falha ao espelhar início de $step_id (não fatal, research §O)"
}

ledger_complete_step() {
  local step_id="$1"
  local now
  now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  jq --arg id "$step_id" --arg now "$now" \
    '.completedSteps += [$id] | .activeStep = null | .updatedAtUtc = $now' \
    "$LINUXHUB_RUN_STATE" > "${LINUXHUB_RUN_STATE}.tmp"
  mv -f "${LINUXHUB_RUN_STATE}.tmp" "$LINUXHUB_RUN_STATE"
  mirror_state_to_windows || log "aviso: falha ao espelhar conclusão de $step_id (não fatal, research §O)"
}

# Implementada em mirror-state.sh (task 4.5); redeclarada aqui como stub para
# scripts que fazem `source common.sh` isoladamente em teste.
if ! declare -f mirror_state_to_windows >/dev/null 2>&1; then
  mirror_state_to_windows() { return 0; }
fi
