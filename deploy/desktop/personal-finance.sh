#!/usr/bin/env bash
# Personal Finance on an everyday Linux or macOS computer (not on 24 hours a day). The Windows equivalent is
# deploy/windows/*.ps1; both run the server's deploy/compose.yml plus the loopback-HTTP override, and share the .env keys
# and the backup format with deploy/scripts (docs/desktop.md).
#
#   personal-finance.sh install [--autostart|--no-autostart] [--backup-recipient age1...] [--port N] [--no-open]
#   personal-finance.sh start [--background] [--no-backup]     # shortcut / sign-in target
#   personal-finance.sh update [--skip-backup]                  # backup, git pull --ff-only, install
#   personal-finance.sh backup | restore <archive> <identity>   # deploy/scripts/backup.sh / restore.sh
#   personal-finance.sh stop | status | uninstall [--purge]
#
# Written for bash 3.2 (macOS's /bin/bash) and BSD/GNU userlands alike: no sed -i, no flock, no GNU-only flags.
set -euo pipefail
umask 077

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DEPLOY="${ROOT}/deploy"
ENV_FILE="${DEPLOY}/.env"
LOGS="${ROOT}/logs"
APP_ID="io.github.joaoguimaraespro.personal-finance"
OVERRIDE="compose.windows.yml" # loopback HTTP marked secure; not Windows-specific despite the name
# Shortcuts, sign-in items and launchd jobs start with a minimal PATH.
PATH="${PATH}:/usr/local/bin:/opt/homebrew/bin:/Applications/Docker.app/Contents/Resources/bin:${HOME}/.docker/bin"
export PATH

case "$(uname -s)" in
  Linux) OS=linux ;;
  Darwin) OS=macos ;;
  *) echo "Unsupported system $(uname -s). On Windows use deploy/windows/Install.ps1." >&2; exit 1 ;;
esac

# ---------------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------------

QUIET=false
log() {
  mkdir -p "${LOGS}"
  printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >> "${LOGS}/${LOG_NAME:-personal-finance}.log"
  [ "${QUIET}" = true ] || printf '%s\n' "$*"
}
die() { log "ERROR: $*"; notify "Personal Finance could not start" "$*"; exit 1; }

# A desktop notification when nobody is watching a terminal (shortcut, sign-in item).
notify() {
  [ -t 1 ] && return 0
  if [ "${OS}" = macos ]; then
    osascript -e "display notification \"${2//\"/\'}\" with title \"${1//\"/\'}\"" >/dev/null 2>&1 || true
  elif command -v notify-send >/dev/null 2>&1; then
    notify-send -a "Personal Finance" "$1" "$2" >/dev/null 2>&1 || true
  fi
}

# Letters and digits from the OS CSPRNG (the alphabet server-deploy.sh produces).
secret() {
  local s=""
  while [ "${#s}" -lt 40 ]; do s="${s}$(head -c 256 /dev/urandom | LC_ALL=C tr -dc 'A-Za-z0-9')"; done
  printf '%s' "${s:0:40}"
}

env_get() { if [ -f "${ENV_FILE}" ]; then sed -n "s/^$1=//p" "${ENV_FILE}" | tail -n 1; fi; }

# Changes (or appends) one setting, keeping every other line; the file keeps mode 600.
env_set() {
  local tmp="${ENV_FILE}.tmp.$$"
  awk -v k="$1" -v v="$2" 'BEGIN { done = 0 }
    index($0, k "=") == 1 { print k "=" v; done = 1; next } { print }
    END { if (!done) print k "=" v }' "${ENV_FILE}" > "${tmp}"
  chmod 600 "${tmp}" && mv "${tmp}" "${ENV_FILE}"
}

is_age_recipient() { printf '%s' "$1" | grep -Eq '^age1[0-9a-z]{58}$'; }

compose() {
  docker compose --project-directory "${DEPLOY}" -f "${DEPLOY}/compose.yml" -f "${DEPLOY}/${OVERRIDE}" \
    --env-file "${ENV_FILE}" "$@"
}

port() { local p; p="$(env_get HTTP_PORT)"; echo "${p:-8080}"; }

# web (Caddy) -> API -> database: /api/auth/me is anonymous and answers 200 once the API is up.
app_ready() { curl -fsS -o /dev/null --max-time 3 -H 'Cache-Control: no-store' "http://127.0.0.1:$(port)/api/auth/me" 2>/dev/null; }

wait_for() { # seconds command...
  local deadline=$(( $(date +%s) + $1 )); shift
  until "$@" >/dev/null 2>&1; do
    [ "$(date +%s)" -lt "${deadline}" ] || return 1
    sleep 3
  done
}

# Docker Desktop on Linux has its own context; use it when the default socket has no engine.
engine_up() {
  docker info >/dev/null 2>&1 && return 0
  if [ -z "${DOCKER_CONTEXT:-}" ] && docker context inspect desktop-linux >/dev/null 2>&1 \
     && DOCKER_CONTEXT=desktop-linux docker info >/dev/null 2>&1; then
    export DOCKER_CONTEXT=desktop-linux
    return 0
  fi
  return 1
}

start_engine() {
  command -v docker >/dev/null 2>&1 || die "Docker is not installed. See docs/desktop.md#requirements."
  engine_up && return 0
  log "Docker is not running; starting it."
  if [ "${OS}" = macos ]; then
    if [ -d /Applications/Docker.app ] || [ -d "${HOME}/Applications/Docker.app" ]; then open -ga Docker
    elif command -v orb >/dev/null 2>&1; then orb start >/dev/null 2>&1 || true
    elif command -v colima >/dev/null 2>&1; then colima start >/dev/null 2>&1 || true
    fi
  elif systemctl --user cat docker-desktop.service >/dev/null 2>&1; then
    systemctl --user start docker-desktop.service || true
  elif systemctl --user cat docker.service >/dev/null 2>&1; then
    systemctl --user start docker.service || true # rootless Docker Engine
  fi
  wait_for 300 engine_up \
    || die "Docker did not become ready in 5 minutes. Start Docker (Docker Desktop, or 'sudo systemctl start docker') and try again."
}

start_stack() {
  app_ready && return 0
  start_engine
  # Containers come back by themselves (restart: unless-stopped) when the engine starts; give them a moment.
  wait_for 20 app_ready && return 0
  log "Starting the containers..."
  compose up -d >> "${LOGS}/${LOG_NAME:-personal-finance}.log" 2>&1 || die "docker compose up failed; see ${LOGS}."
  wait_for 360 app_ready || die "No answer on http://localhost:$(port) within 6 minutes. See 'personal-finance.sh status'."
}

backup_dir() { local d; d="$(env_get BACKUP_DIR)"; echo "${d:-${ROOT}/backups}"; }

backup_overdue() { # true when no archive is younger than 24 h
  [ -d "$(backup_dir)" ] || return 0
  [ -z "$(find "$(backup_dir)" -name 'finance-*.tar.age' -mmin -1440 2>/dev/null | head -n 1)" ]
}

# The app in its own window with a dedicated browser profile (no extensions, cookies apart from everyday browsing).
open_window() {
  local url profile="${ROOT}/browser-profile" b app
  url="http://localhost:$(port)/"
  mkdir -p "${profile}"
  if [ "${OS}" = macos ]; then
    for app in "Google Chrome" "Microsoft Edge" "Brave Browser" "Chromium"; do
      if open -Ra "${app}" 2>/dev/null; then
        open -na "${app}" --args --app="${url}" --user-data-dir="${profile}" --no-first-run --no-default-browser-check
        return 0
      fi
    done
    open "${url}"
  else
    for b in chromium chromium-browser google-chrome google-chrome-stable microsoft-edge microsoft-edge-stable brave-browser; do
      if command -v "${b}" >/dev/null 2>&1; then
        nohup "${b}" --app="${url}" --user-data-dir="${profile}" --no-first-run --no-default-browser-check \
          >/dev/null 2>&1 &
        return 0
      fi
    done
    xdg-open "${url}" >/dev/null 2>&1 &
  fi
}

# One launcher at a time (mkdir is atomic everywhere; flock is not on macOS). A stale lock older than 15 min is taken over.
LOCK="${ROOT}/.launcher.lock"
lock() {
  local i=0
  until mkdir "${LOCK}" 2>/dev/null; do
    if [ -n "$(find "${LOCK}" -maxdepth 0 -mmin +15 2>/dev/null)" ]; then rmdir "${LOCK}" 2>/dev/null || true; continue; fi
    i=$((i + 1)); [ "${i}" -lt 600 ] || die "Another launcher is still running (${LOCK})."
    sleep 1
  done
  trap 'rmdir "${LOCK}" 2>/dev/null || true' EXIT
}

self() { printf '%s' "${DEPLOY}/desktop/personal-finance.sh"; }

# ---------------------------------------------------------------------------------------------------------------------
# Shortcuts, sign-in item and backup schedule
# ---------------------------------------------------------------------------------------------------------------------

LINUX_APPS="${XDG_DATA_HOME:-${HOME}/.local/share}/applications"
LINUX_AUTOSTART="${XDG_CONFIG_HOME:-${HOME}/.config}/autostart"
SYSTEMD_USER="${XDG_CONFIG_HOME:-${HOME}/.config}/systemd/user"
MAC_APP="${HOME}/Applications/Personal Finance.app"
MAC_AGENTS="${HOME}/Library/LaunchAgents"

desktop_entry() { # $1 = extra arguments, $2 = extra keys
  cat <<EOF
[Desktop Entry]
Type=Application
Name=Personal Finance
Comment=Self-hosted personal finance and investments
Exec="$(self)" start $1
Icon=${ROOT}/web/public/icon-512.png
Terminal=false
Categories=Office;Finance;
StartupNotify=false
$2
EOF
}

launch_agent() { # $1 = label, $2 = argument, $3 = extra plist keys
  cat <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$1</string>
  <key>ProgramArguments</key>
  <array><string>/bin/bash</string><string>$(self)</string><string>$2</string></array>
  <key>StandardOutPath</key><string>${LOGS}/launchd.log</string>
  <key>StandardErrorPath</key><string>${LOGS}/launchd.log</string>
$3
</dict>
</plist>
EOF
}

agent_load() { launchctl bootout "gui/$(id -u)/$1" >/dev/null 2>&1 || true; launchctl bootstrap "gui/$(id -u)" "${MAC_AGENTS}/$1.plist"; }
agent_remove() { launchctl bootout "gui/$(id -u)/$1" >/dev/null 2>&1 || true; rm -f "${MAC_AGENTS}/$1.plist"; }

install_shortcut() {
  if [ "${OS}" = linux ]; then
    mkdir -p "${LINUX_APPS}"
    desktop_entry "" "" > "${LINUX_APPS}/${APP_ID}.desktop"
    chmod 644 "${LINUX_APPS}/${APP_ID}.desktop"
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "${LINUX_APPS}" >/dev/null 2>&1 || true
    log "Shortcut: 'Personal Finance' in the applications menu."
  else
    rm -rf "${MAC_APP}"
    mkdir -p "${MAC_APP}/Contents/MacOS" "${MAC_APP}/Contents/Resources"
    printf '#!/bin/bash\nexec /bin/bash "%s" start\n' "$(self)" > "${MAC_APP}/Contents/MacOS/personal-finance"
    chmod 755 "${MAC_APP}/Contents/MacOS/personal-finance"
    local iconset; iconset="$(mktemp -d)/pf.iconset"; mkdir -p "${iconset}"
    if sips -z 512 512 "${ROOT}/web/public/icon-512.png" --out "${iconset}/icon_512x512.png" >/dev/null 2>&1 \
       && sips -z 256 256 "${ROOT}/web/public/icon-512.png" --out "${iconset}/icon_256x256.png" >/dev/null 2>&1 \
       && iconutil -c icns "${iconset}" -o "${MAC_APP}/Contents/Resources/personal-finance.icns" 2>/dev/null; then :; fi
    cat > "${MAC_APP}/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Personal Finance</string>
  <key>CFBundleIdentifier</key><string>${APP_ID}</string>
  <key>CFBundleExecutable</key><string>personal-finance</string>
  <key>CFBundleIconFile</key><string>personal-finance</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSUIElement</key><true/>
</dict>
</plist>
EOF
    chmod -R go-w "${MAC_APP}"
    log "Shortcut: ~/Applications/Personal Finance.app (drag it to the Dock to pin it)."
  fi
}

remove_shortcut() { rm -f "${LINUX_APPS}/${APP_ID}.desktop"; [ "${OS}" = macos ] && rm -rf "${MAC_APP}" || true; }

set_autostart() { # on|off
  if [ "${OS}" = linux ]; then
    if [ "$1" = on ]; then
      mkdir -p "${LINUX_AUTOSTART}"
      desktop_entry "--background" "NoDisplay=true
X-GNOME-Autostart-Delay=30" > "${LINUX_AUTOSTART}/${APP_ID}.desktop"
      log "Auto-start: brings the app up quietly when you sign in."
    else
      rm -f "${LINUX_AUTOSTART}/${APP_ID}.desktop"
    fi
  else
    if [ "$1" = on ]; then
      mkdir -p "${MAC_AGENTS}"
      launch_agent "${APP_ID}.autostart" "start" "  <key>RunAtLoad</key><true/>
  <key>EnvironmentVariables</key><dict><key>PF_BACKGROUND</key><string>1</string></dict>" \
        > "${MAC_AGENTS}/${APP_ID}.autostart.plist"
      agent_load "${APP_ID}.autostart" || log "Could not load the sign-in agent now; it runs at the next sign-in."
      log "Auto-start: brings the app up quietly when you sign in."
    else
      agent_remove "${APP_ID}.autostart"
    fi
  fi
}

set_backup_schedule() { # on|off
  if [ "${OS}" = linux ]; then
    if ! command -v systemctl >/dev/null 2>&1 || ! systemctl --user show-environment >/dev/null 2>&1; then
      [ "$1" = on ] && log "No systemd user session: backups run from the launcher when the newest is older than 24 h."
      return 0
    fi
    if [ "$1" = on ]; then
      mkdir -p "${SYSTEMD_USER}"
      cat > "${SYSTEMD_USER}/personal-finance-backup.service" <<EOF
[Unit]
Description=Encrypted backup of Personal Finance

[Service]
Type=oneshot
ExecStart="$(self)" backup --quiet
EOF
      # Persistent: a run missed while the computer was off starts as soon as you are signed in again.
      cat > "${SYSTEMD_USER}/personal-finance-backup.timer" <<EOF
[Unit]
Description=Daily Personal Finance backup

[Timer]
OnCalendar=*-*-* 03:30:00
RandomizedDelaySec=15m
Persistent=true

[Install]
WantedBy=timers.target
EOF
      systemctl --user daemon-reload && systemctl --user enable --now personal-finance-backup.timer >/dev/null 2>&1
      log "Backups: daily at 03:30, or as soon as the computer is on after a missed run (${SYSTEMD_USER})."
    else
      systemctl --user disable --now personal-finance-backup.timer >/dev/null 2>&1 || true
      rm -f "${SYSTEMD_USER}"/personal-finance-backup.*
      systemctl --user daemon-reload >/dev/null 2>&1 || true
    fi
  else
    if [ "$1" = on ]; then
      mkdir -p "${MAC_AGENTS}"
      # launchd runs a calendar job missed during sleep on wake; a Mac that was shut down is covered by the launcher.
      launch_agent "${APP_ID}.backup" "backup" "  <key>StartCalendarInterval</key><dict><key>Hour</key><integer>3</integer><key>Minute</key><integer>30</integer></dict>
  <key>EnvironmentVariables</key><dict><key>PF_QUIET</key><string>1</string></dict>" \
        > "${MAC_AGENTS}/${APP_ID}.backup.plist"
      agent_load "${APP_ID}.backup" || true
      log "Backups: daily at 03:30 (launchd), or from the launcher when the newest is older than 24 h."
    else
      agent_remove "${APP_ID}.backup"
    fi
  fi
}

# ---------------------------------------------------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------------------------------------------------

cmd_install() {
  local autostart="" recipient="" http_port=8080 open_app=true
  while [ $# -gt 0 ]; do
    case "$1" in
      --autostart) autostart=on ;;
      --no-autostart) autostart=off ;;
      --backup-recipient) recipient="${2:?--backup-recipient needs an age public key}"; shift ;;
      --port) http_port="${2:?--port needs a number}"; shift ;;
      --no-open) open_app=false ;;
      *) echo "Unknown option $1" >&2; exit 2 ;;
    esac
    shift
  done
  LOG_NAME=install
  for tool in docker git curl; do
    command -v "${tool}" >/dev/null 2>&1 || die "${tool} is not installed. See docs/desktop.md#requirements."
  done
  docker compose version >/dev/null 2>&1 || die "Docker Compose v2 ('docker compose') is missing."
  if [ -n "${recipient}" ] && ! is_age_recipient "${recipient}"; then
    die "--backup-recipient must be an age PUBLIC key (age1...), never the private key."
  fi

  if [ ! -f "${ENV_FILE}" ]; then
    log "Creating deploy/.env with random secrets (mode 600)."
    cat > "${ENV_FILE}" <<EOF
POSTGRES_SUPERUSER_PASSWORD=$(secret)
FINANCE_MIGRATOR_PASSWORD=$(secret)
FINANCE_APP_PASSWORD=$(secret)
FINANCE_BACKUP_PASSWORD=$(secret)
AUTH_SETUP_TOKEN=$(secret)
BIND_ADDRESS=127.0.0.1
HTTP_PORT=${http_port}
INTEGRATIONS_ENABLE_DEMO=false
OTEL_EXPORTER_OTLP_ENDPOINT=
API_IMAGE=personal-finance-api:local
WEB_IMAGE=personal-finance-web:local
MCP_IMAGE=personal-finance-mcp:local
PF_COMPOSE_OVERRIDE=${OVERRIDE}
BACKUP_AGE_RECIPIENT=
BACKUP_DIR=${ROOT}/backups
BACKUP_RETENTION_DAYS=35
BACKUP_REMOTE=
BACKUP_REMOTE_RETENTION_DAYS=
ANTHROPIC_API_KEY=
ASSISTANT_MODEL=claude-opus-5
EOF
    chmod 600 "${ENV_FILE}"
  fi
  [ -n "${recipient}" ] && env_set BACKUP_AGE_RECIPIENT "${recipient}"
  mkdir -p "$(backup_dir)" "${LOGS}"

  start_engine
  log "Building and starting (the first build takes several minutes)..."
  compose up -d --build --wait || die "docker compose up failed."
  wait_for 300 app_ready || die "The app did not answer on http://localhost:$(port)."

  install_shortcut
  [ -n "${autostart}" ] && set_autostart "${autostart}"
  if is_age_recipient "$(env_get BACKUP_AGE_RECIPIENT)"; then
    command -v age >/dev/null 2>&1 || log "Warning: 'age' is not installed yet; backups fail until it is."
    set_backup_schedule on
  else
    log "Backups: off. Re-run with --backup-recipient age1... to enable them."
  fi

  printf '\nPersonal Finance is running on http://localhost:%s\n' "$(port)"
  if curl -fsS --max-time 5 "http://127.0.0.1:$(port)/api/auth/me" | grep -q '"setupRequired":true'; then
    printf 'Create the owner with this one-time setup token (refused forever once the owner exists):\n  %s\n' \
      "$(env_get AUTH_SETUP_TOKEN)"
  fi
  [ "${open_app}" = true ] && open_window
  return 0
}

cmd_start() {
  local background="${PF_BACKGROUND:-}" backup=true
  while [ $# -gt 0 ]; do
    case "$1" in --background) background=1 ;; --no-backup) backup=false ;; *) echo "Unknown option $1" >&2; exit 2 ;; esac
    shift
  done
  LOG_NAME=launcher; QUIET=true
  [ -f "${ENV_FILE}" ] || die "Not installed: run deploy/desktop/personal-finance.sh install first."
  lock
  if ! app_ready; then
    log "App not answering; starting it."
    [ -n "${background}" ] || notify "Personal Finance" "Starting... right after sign-in this can take a minute or two."
    start_stack
  fi
  log "Ready on http://localhost:$(port)"
  [ -n "${background}" ] || open_window
  if [ "${backup}" = true ] && is_age_recipient "$(env_get BACKUP_AGE_RECIPIENT)" && backup_overdue; then
    log "Last backup is older than 24 h; starting one in the background."
    nohup /bin/bash "$(self)" backup --quiet >/dev/null 2>&1 &
  fi
}

cmd_backup() {
  [ "${1:-}" = "--quiet" ] || [ -n "${PF_QUIET:-}" ] && QUIET=true
  LOG_NAME=backup
  engine_up || start_engine
  # One backup at a time, without blocking the launcher's lock.
  if ! mkdir "${ROOT}/.backup.lock" 2>/dev/null; then log "A backup is already running."; return 0; fi
  trap 'rmdir "${ROOT}/.backup.lock" 2>/dev/null || true' EXIT
  if "${DEPLOY}/scripts/backup.sh" >> "${LOGS}/backup.log" 2>&1; then
    log "Backup done: $(ls -t "$(backup_dir)"/finance-*.tar.age | head -n 1)"
  else
    log "Backup FAILED; see ${LOGS}/backup.log"; notify "Personal Finance backup failed" "See ${LOGS}/backup.log"; return 1
  fi
}

cmd_update() {
  LOG_NAME=install
  if [ "${1:-}" != "--skip-backup" ] && is_age_recipient "$(env_get BACKUP_AGE_RECIPIENT)"; then
    cmd_backup || die "Backup before update failed; fix it or use --skip-backup."
  fi
  git -C "${ROOT}" pull --ff-only
  exec /bin/bash "$(self)" install --no-open
}

cmd_uninstall() {
  local purge=false answer
  [ "${1:-}" = "--purge" ] && purge=true
  LOG_NAME=install
  set_autostart off; set_backup_schedule off; remove_shortcut
  if engine_up; then
    if [ "${purge}" = true ]; then
      read -r -p "This deletes the database, .env, local backups and ${ROOT}. Type 'delete' to continue: " answer
      [ "${answer}" = delete ] || { echo "Aborted."; exit 1; }
      compose down -v --rmi local || true
      docker image rm personal-finance-api:local personal-finance-web:local personal-finance-mcp:local >/dev/null 2>&1 || true
      rm -rf "${ROOT}"
      echo "Removed everything. Docker itself is left installed."
      return 0
    fi
    compose down
  fi
  echo "Removed the containers, shortcut, sign-in item and backup schedule. Data, deploy/.env and backups are kept;"
  echo "run install again to bring everything back."
}

# Tests source the helpers without running a command (deploy/desktop/tests/smoke.sh).
[ "${PF_LIB_ONLY:-}" = 1 ] && return 0

cmd="${1:-}"; [ $# -gt 0 ] && shift
case "${cmd}" in
  install) cmd_install "$@" ;;
  start) cmd_start "$@" ;;
  update) cmd_update "$@" ;;
  backup) cmd_backup "$@" ;;
  restore) engine_up || start_engine; exec "${DEPLOY}/scripts/restore.sh" "$@" ;;
  stop) engine_up && compose stop ;;
  status) engine_up && compose ps; app_ready && echo "App answering on http://localhost:$(port)" || echo "App not answering" ;;
  uninstall) cmd_uninstall "$@" ;;
  *) sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
esac
