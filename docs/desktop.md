# Linux or macOS computer

Run Personal Finance on an everyday Linux or macOS computer that is **not on 24 hours a day**, the same way as on a
[Windows PC](windows.md): an app shortcut that starts whatever is not running, an optional sign-in item, and a daily
encrypted backup that catches up after the computer was off. Same images and `deploy/compose.yml` as the
[server](deployment.md), plus the loopback-HTTP override ([`deploy/compose.windows.yml`](../deploy/compose.windows.yml),
not Windows-specific despite its name). Everything listens on `127.0.0.1` only.

One script does it all: [`deploy/desktop/personal-finance.sh`](../deploy/desktop/personal-finance.sh).

| Install mode | Windows | macOS | Linux |
|---|---|---|---|
| Try it (demo, throwaway) | `scripts/local-up.ps1` | `scripts/local-up.sh` | `scripts/local-up.sh` |
| Everyday computer | [`Install.ps1`](windows.md) | `personal-finance.sh install` | `personal-finance.sh install` |
| 24/7 home server | `Install.ps1 -AutoStart` + `tailscale serve`¹ | `personal-finance.sh install --autostart` + `tailscale serve`¹ | [`server-deploy.sh`](deployment.md) (isolated user, rootless Docker) |

¹ An always-on Windows or Mac works as a server with the everyday install: enable auto-start, keep it from sleeping,
and add [phone access](#phone-access-optional). The Linux server script adds user isolation and rootless Docker on top.

## Requirements

| | macOS | Linux |
|---|---|---|
| Docker | [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or OrbStack / Colima) | Docker Engine with the Compose plugin (`docker compose`), rootful or rootless, or Docker Desktop |
| Tools | `git`, `curl` (both come with the Xcode command line tools) | `git`, `curl` |
| Backups | `brew install age` (+ `rclone` for the off-site copy) | `apt install age` / `dnf install age` (+ `rclone`) |
| Browser for the app window | Chrome, Edge, Brave or Chromium (else the default browser) | Chromium, Chrome, Edge or Brave (else `xdg-open`) |
| Memory / disk | 8 GB RAM, about 10 GB free | same |

On Linux, add yourself to the `docker` group (or use rootless Docker) so the script runs without `sudo`.
Apple Silicon is supported: every image is built locally for the machine's architecture.

## Install

```bash
# macOS
git clone https://github.com/joaoguimaraespro/personal-finance.git "$HOME/Library/Application Support/PersonalFinance"
"$HOME/Library/Application Support/PersonalFinance/deploy/desktop/personal-finance.sh" install --autostart --backup-recipient age1...

# Linux
git clone https://github.com/joaoguimaraespro/personal-finance.git ~/.local/share/personal-finance
~/.local/share/personal-finance/deploy/desktop/personal-finance.sh install --autostart --backup-recipient age1...
```

`install` is idempotent and:

1. checks Docker (and Compose v2), Git and curl, and starts Docker if it is not running;
2. generates `deploy/.env` once, with random secrets from the OS CSPRNG, mode 600 (same keys as the server and Windows);
3. builds the images and starts the stack (`up -d --build --wait`; the first build takes several minutes);
4. adds the **Personal Finance** shortcut: an applications-menu entry on Linux, `~/Applications/Personal Finance.app`
   on macOS (drag it to the Dock);
5. `--autostart`: a sign-in item that brings the app up quietly (XDG autostart on Linux, a LaunchAgent on macOS);
   `--no-autostart` removes it;
6. `--backup-recipient age1…`: stores the backup public key and schedules the daily backup;
7. prints the one-time **setup token** while no owner exists, and opens the app (`--no-open` to skip).

`--port N` picks another port when the `.env` is created (default 8080; later, edit `HTTP_PORT` and run `install`
again). Run it in your own terminal, since it prints the setup token. Then create the owner and enrol MFA.

## Daily use

The shortcut runs `personal-finance.sh start`: if the app answers it just opens the window; otherwise it starts Docker
(Docker Desktop, OrbStack, Colima, or a user-level Docker service), runs `docker compose up -d` if needed, waits until
web → API → database answer, and opens the app in its own window with a **dedicated browser profile**
(`browser-profile/`). A notification shows while a slow start is in progress, and errors are notified and logged in
`logs/launcher.log`.

```bash
personal-finance.sh status      # containers and whether the app answers
personal-finance.sh stop        # stop the containers (data kept)
```

Background jobs catch up after the computer was off or asleep exactly as described for Windows:
[When the PC was off](windows.md#when-the-pc-was-off).

## Backups and restore

Create the age key pair **off this computer** and keep the private key offline ([details](windows.md#backups-and-restore)).

| | Linux | macOS |
|---|---|---|
| Daily run | systemd user timer at 03:30, `Persistent=true`: a run missed while the computer was off starts right after you sign in | LaunchAgent at 03:30: launchd runs a run missed during sleep on wake |
| Fallback | the launcher starts a backup in the background whenever the newest one is older than 24 h | same (covers a Mac that was shut down) |

Archives go to `backups/finance-<UTC>.tar.age`, logs to `logs/backup.log`. Archives older than
`BACKUP_RETENTION_DAYS` (35) are deleted, never the newest. The optional rclone off-site copy is configured as on the
server: [Off-site copy](deployment.md#off-site-copy).

```bash
personal-finance.sh backup                                                # now
personal-finance.sh restore backups/finance-20261005T033000Z.tar.age /Volumes/USB/finance-backup-identity.txt
```

Restore is DESTRUCTIVE (it asks you to type `restore`). The archive format is shared by all three platforms, so a
server or Windows backup restores here and vice versa: install on the target first, then restore.

## Update and uninstall

```bash
personal-finance.sh update               # backup first (if configured), git pull --ff-only, install
personal-finance.sh uninstall            # removes containers, shortcut, sign-in item, schedule; keeps data
personal-finance.sh uninstall --purge    # also deletes volumes, images, .env, backups and the folder (type 'delete')
```

## Phone access (optional)

Install [Tailscale](https://tailscale.com/download), enable HTTPS certificates in the admin console, then
`tailscale serve --bg --https=8443 http://127.0.0.1:8080`. Reachable only from your tailnet while the computer is
awake ([ADR-0003](adr/0003-vpn-only-exposure.md)). `tailscale serve reset` turns it off.

## Security notes

The same as [on Windows](windows.md#security-notes): localhost only, Secure cookies on `http://localhost` (a secure
context for browsers), `.env` readable only by you, the backup private key never on this computer, and a separate
browser profile. Turn on disk encryption (FileVault on macOS, LUKS on Linux): the database lives in Docker's data
directory. Membership of the `docker` group is effectively root on Linux; rootless Docker avoids that.
