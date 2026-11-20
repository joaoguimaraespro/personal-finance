#!/usr/bin/env python3
"""setup-tools.py: check or install the machine-level tools listed in Armature's tools.json.

Armature never vendors tools. Each machine installs them once, at the pinned versions in tools.json, and scripts
call them from PATH (or from the resolved paths this script prints).

Usage:
  setup-tools.py [--manifest PATH] [--check]     report each tool: ok, missing, wrong-version (default: --check)
  setup-tools.py [--manifest PATH] --install     install missing or wrong-version tools that have an install command
                                                 or a download for this platform
  setup-tools.py ... --only likec4,playwright-core   limit to these tools

A tool is probed in one of three ways (tools.json):
  bin + check   the executable is on PATH and its version output matches `version` (or is >= `min_version`)
  package       a package in the global bun folder; its package.json `version` must match
  path          a file that must exist (e.g. a browser revision); `version` is then a label, such as a revision
`path` may be combined with the other two. In paths, $BUN_GLOBAL is the global bun node_modules folder
($BUN_INSTALL/install/global/node_modules, default ~/.bun/...) and $PLAYWRIGHT_BROWSERS_PATH defaults to
~/.cache/ms-playwright. Each result carries `path` (the resolved file) when the tool has one.

A tool is installed by its `install` command, or by a `download` for this platform (`<os>-<arch>`, e.g.
linux-x86_64; $ARMATURE_PLATFORM overrides): {url, sha256[, archive: "tar.gz", member]} fetched, checked against the
pinned sha256 (nothing is installed on a mismatch) and written to `dest` (default ~/.local/bin/<bin>), executable.
Otherwise `install_hint` says how to install it by hand.

The manifest is --manifest, else $ARMATURE_TOOLS, else armature-tools.json beside this script (the copy that travels
with the git hooks), else a tools.json in its parent folder, else Armature's own tools.json.

Output: JSON on stdout; hints on stderr. Exit: 0 all ok, 1 a tool is missing or wrong, 2 usage error.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
MANIFEST_CANDIDATES = (HERE / "armature-tools.json", HERE.parent / "tools.json", HERE.parents[2] / "tools.json")
DOWNLOAD_TIMEOUT_S = 120
ARCH = {"amd64": "x86_64", "x64": "x86_64", "aarch64": "arm64"}


def default_manifest() -> str:
    if os.environ.get("ARMATURE_TOOLS"):
        return os.environ["ARMATURE_TOOLS"]
    for path in MANIFEST_CANDIDATES:
        if path.is_file():
            return str(path)
    return str(MANIFEST_CANDIDATES[-1])


def platform_key() -> str:
    """linux-x86_64, darwin-arm64, ... ($ARMATURE_PLATFORM wins)."""
    if os.environ.get("ARMATURE_PLATFORM"):
        return os.environ["ARMATURE_PLATFORM"]
    machine = platform.machine().lower()
    return f"{platform.system().lower()}-{ARCH.get(machine, machine)}"


def dest_of(tool: dict) -> str:
    return os.path.expanduser(tool.get("dest") or f"~/.local/bin/{tool.get('bin', tool['name'])}")


def download_install(tool: dict) -> tuple[bool, str]:
    """Fetch the tool's download for this platform, check its sha256, write it to dest. (ok, message)."""
    spec = (tool.get("download") or {}).get(platform_key())
    if spec is None:
        return False, f"no download for {platform_key()}"
    try:
        with urllib.request.urlopen(spec["url"], timeout=DOWNLOAD_TIMEOUT_S) as resp:
            blob = resp.read()
    except OSError as exc:
        return False, f"download failed ({exc})"
    digest = hashlib.sha256(blob).hexdigest()
    if digest != spec["sha256"]:
        return False, f"sha256 mismatch: got {digest}, want {spec['sha256']}; nothing installed"
    if spec.get("archive") == "tar.gz":
        with tarfile.open(fileobj=io.BytesIO(blob), mode="r:gz") as tar:
            member = tar.extractfile(spec["member"])
            if member is None:
                return False, f"{spec['member']} is not in the archive"
            blob = member.read()
    dest = Path(dest_of(tool))
    dest.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=dest.parent)
    with os.fdopen(fd, "wb") as fh:
        fh.write(blob)
    os.chmod(tmp, 0o755)
    os.replace(tmp, dest)
    return True, f"installed {dest}"
VERSION_RE = re.compile(r"(\d+)\.(\d+)(?:\.(\d+))?")


def parse_version(text: str) -> tuple[int, int, int] | None:
    """The first X.Y.Z (or X.Y, read as X.Y.0: vulture prints "2.16") in text."""
    m = VERSION_RE.search(text or "")
    return tuple(int(x or 0) for x in m.groups()) if m else None


def expand(text: str) -> str:
    """Expand $BUN_GLOBAL, $PLAYWRIGHT_BROWSERS_PATH (with their defaults) and ~ in a manifest path."""
    bun_install = os.environ.get("BUN_INSTALL") or os.path.expanduser("~/.bun")
    values = {
        "BUN_GLOBAL": os.path.join(bun_install, "install", "global", "node_modules"),
        "PLAYWRIGHT_BROWSERS_PATH": os.environ.get("PLAYWRIGHT_BROWSERS_PATH")
        or os.path.expanduser("~/.cache/ms-playwright"),
    }
    for key, value in values.items():
        text = text.replace("${" + key + "}", value).replace("$" + key, value)
    return os.path.expanduser(text)


def version_status(tool: dict, found: tuple[int, int, int]) -> str:
    if "version" in tool:
        return "ok" if found == parse_version(tool["version"]) else "wrong-version"
    return "ok" if found >= parse_version(tool.get("min_version", "0.0.0")) else "wrong-version"


def probe(tool: dict) -> dict:
    """Return {name, status, found[, path]} for one tool. status: ok | missing | wrong-version | unknown-version."""
    name = tool["name"]
    res: dict = {"name": name}
    if "path" in tool:
        res["path"] = expand(tool["path"])
        if not os.path.exists(res["path"]):
            return {**res, "status": "missing", "found": None}
    if "package" in tool:
        pkg = os.path.join(expand("$BUN_GLOBAL"), tool["package"], "package.json")
        try:
            with open(pkg, encoding="utf-8") as fh:
                text = json.load(fh).get("version", "")
        except (OSError, json.JSONDecodeError):
            return {**res, "status": "missing", "found": None}
    elif "check" in tool:
        exe = shutil.which(tool.get("bin", tool["check"][0]))
        if exe is None and "download" in tool and os.access(dest_of(tool), os.X_OK):
            exe = dest_of(tool)   # downloaded into a folder that is not on PATH (yet)
        if exe is None:
            return {**res, "status": "missing", "found": None}
        try:
            out = subprocess.run([exe, *tool["check"][1:]], capture_output=True, text=True, timeout=60)
            text = (out.stdout or "") + (out.stderr or "")
        except (OSError, subprocess.TimeoutExpired) as exc:
            return {**res, "status": "unknown-version", "found": None, "error": str(exc)}
    else:   # path only: the file exists, and the version is a label (a browser revision, say)
        return {**res, "status": "ok", "found": tool.get("version")}
    found = parse_version(text)
    if found is None:
        return {**res, "status": "unknown-version", "found": text.strip()[:80]}
    return {**res, "status": version_status(tool, found), "found": ".".join(map(str, found))}


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--manifest", default=None)
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--install", action="store_true")
    ap.add_argument("--only", default="")
    args = ap.parse_args(argv)
    args.manifest = args.manifest or default_manifest()

    try:
        tools = json.loads(Path(args.manifest).read_text(encoding="utf-8"))["tools"]
    except (OSError, KeyError, json.JSONDecodeError) as exc:
        print(f"cannot read manifest {args.manifest}: {exc}", file=sys.stderr)
        return 2
    only = {t for t in args.only.split(",") if t}
    unknown = only - {t["name"] for t in tools}
    if unknown:
        print(f"unknown tool(s): {', '.join(sorted(unknown))}", file=sys.stderr)
        return 2
    if only:
        tools = [t for t in tools if t["name"] in only]

    results = []
    for tool in tools:
        res = probe(tool)
        want = tool.get("version") or (">=" + tool.get("min_version", "?"))
        res["want"] = want
        if args.install and res["status"] != "ok":
            if "install" not in tool and platform_key() in (tool.get("download") or {}):
                ok, msg = download_install(tool)
                print(f"{tool['name']}: {msg}", file=sys.stderr)
                res["action"] = "installed" if ok else "install-failed"
                res.update({k: v for k, v in probe(tool).items() if k in ("status", "found", "path")})
            elif "install" not in tool:
                res["action"] = "manual"
                print(f"{tool['name']}: install by hand: {tool.get('install_hint', 'see its docs')}", file=sys.stderr)
            else:
                print(f"{tool['name']}: running {' '.join(tool['install'])}", file=sys.stderr)
                proc = subprocess.run(tool["install"])
                res["action"] = "installed" if proc.returncode == 0 else "install-failed"
                res.update({k: v for k, v in probe(tool).items() if k in ("status", "found", "path")})
        elif res["status"] != "ok":
            how = " ".join(tool["install"]) if "install" in tool else tool.get("install_hint", "see its docs")
            print(f"{tool['name']}: {res['status']} (want {want}): {how}", file=sys.stderr)
        results.append(res)

    ok = all(r["status"] == "ok" for r in results)
    print(json.dumps({"manifest": str(args.manifest), "ok": ok, "tools": results}, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
