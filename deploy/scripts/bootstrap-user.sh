#!/usr/bin/env bash
# One-time host setup (run with sudo by the server owner). Creates an isolated "finance" user with rootless
# Docker, following the one-user-per-project isolation model used on this server.
set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "Run with sudo." >&2; exit 1; }

APP_USER=finance
APP_HOME=/home/${APP_USER}

if ! id "${APP_USER}" >/dev/null 2>&1; then
  useradd --create-home --shell /bin/bash "${APP_USER}"
  echo "Created user ${APP_USER}"
fi

apt-get install -y uidmap dbus-user-session age docker-ce-rootless-extras >/dev/null
loginctl enable-linger "${APP_USER}"

# Rootless Docker for the finance user only; it cannot see other projects' containers or networks.
sudo -iu "${APP_USER}" bash -lc 'dockerd-rootless-setuptool.sh install --skip-iptables >/dev/null && systemctl --user enable --now docker'
sudo -iu "${APP_USER}" bash -lc 'grep -q DOCKER_HOST ~/.bashrc || echo "export DOCKER_HOST=unix:///run/user/$(id -u)/docker.sock" >> ~/.bashrc'

install -d -m 700 -o "${APP_USER}" -g "${APP_USER}" "${APP_HOME}/personal-finance" "${APP_HOME}/personal-finance/backups"
echo "Done. Next: clone the repo as ${APP_USER}, create deploy/.env, then follow docs/deployment.md."
