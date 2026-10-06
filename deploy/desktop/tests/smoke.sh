#!/usr/bin/env bash
# shellcheck disable=SC2016,SC2034  # check() evals its single-quoted conditions
# Checks the pure helpers of personal-finance.sh with the shell and userland of the machine running it (CI: Ubuntu and
# macOS, whose /bin/bash is 3.2 and whose tools are BSD). Docker is not needed.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tmp="$(mktemp -d)"; trap 'rm -rf "${tmp}"' EXIT
fail=0
check() { if eval "$2"; then echo "ok   $1"; else echo "FAIL $1"; fail=1; fi; }

# shellcheck source=deploy/desktop/personal-finance.sh
PF_LIB_ONLY=1 source "${here}/../personal-finance.sh"
set +e
ENV_FILE="${tmp}/.env"; LOGS="${tmp}/logs"

s1="$(secret)"; s2="$(secret)"
check "secret is 40 letters/digits" '[ "${#s1}" -eq 40 ] && printf %s "$s1" | grep -Eq "^[A-Za-z0-9]{40}$"'
check "secrets differ" '[ "$s1" != "$s2" ]'

printf 'A=1\n# comment\nHTTP_PORT=8080\n' > "${ENV_FILE}"; chmod 600 "${ENV_FILE}"
env_set HTTP_PORT 18081; env_set NEW_KEY 'x y'
check "env_set replaces in place" '[ "$(env_get HTTP_PORT)" = 18081 ] && [ "$(grep -c HTTP_PORT "${ENV_FILE}")" -eq 1 ]'
check "env_set appends and keeps comments" '[ "$(env_get NEW_KEY)" = "x y" ] && grep -q "^# comment" "${ENV_FILE}"'
check ".env stays private" '[ -z "$(find "${ENV_FILE}" -perm -004)" ] && [ -z "$(find "${ENV_FILE}" -perm -040)" ]'
check "port() reads HTTP_PORT" '[ "$(port)" = 18081 ]'
rm "${ENV_FILE}"
check "port() defaults without .env" '[ "$(port)" = 8080 ]'

check "age public key accepted" 'is_age_recipient age1z7n5p84295kgxz9ef3u5wjzdh6gkxmv3wrpwxzu95ckw2lad05jsnh9stj'
# Built at runtime: a literal private-key-shaped string would (rightly) trip the secret scanner.
fake_identity="AGE-SECRET-KEY-1$(printf 'Q%.0s' $(seq 58))"
check "age private key rejected" '! is_age_recipient "${fake_identity}"'

printf 'BACKUP_DIR=%s\n' "${tmp}/b" > "${ENV_FILE}"
check "no backup dir = overdue" 'backup_overdue'
mkdir -p "${tmp}/b"; touch "${tmp}/b/finance-20260101T000000Z.tar.age"
check "fresh backup = not overdue" '! backup_overdue'
touch -t 202001010000 "${tmp}/b/finance-20260101T000000Z.tar.age"
check "old backup = overdue" 'backup_overdue'

desktop_entry "--background" "NoDisplay=true" > "${tmp}/e.desktop"
check "desktop entry quotes the script path" 'grep -q "^Exec=\".*/deploy/desktop/personal-finance.sh\" start --background" "${tmp}/e.desktop"'
launch_agent "test.label" "start" "  <key>RunAtLoad</key><true/>" > "${tmp}/a.plist"
if command -v plutil >/dev/null 2>&1; then check "launch agent plist is valid" 'plutil -lint "${tmp}/a.plist" >/dev/null'; fi
if command -v xmllint >/dev/null 2>&1; then check "launch agent plist is well-formed XML" 'xmllint --noout --nonet "${tmp}/a.plist"'; fi

check "lock creates the lock dir" '( LOCK="${tmp}/lock"; lock; [ -d "${LOCK}" ] )'

exit "${fail}"
