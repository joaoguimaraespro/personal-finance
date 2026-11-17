#!/usr/bin/env python3
"""deps_check.py: every package a change adds to a manifest must exist on its public registry.

Coding agents name packages that do not exist (hallucinated or mistyped names), and attackers register those names
("slopsquatting"). This check reads the manifests the change touches, finds the package names that were not there
before, and asks the registry about each one. Python standard library only; it also runs as a copy next to the git
hooks (armature-deps-check.py), so it imports nothing from Armature.

Manifests: package.json (dependencies, devDependencies, peerDependencies, optionalDependencies; workspace:, file:,
link:, git and URL specs are skipped; npm:<name>@<range> aliases check <name>), pyproject.toml ([project]
dependencies and optional-dependencies, [dependency-groups], [tool.poetry] dependencies and groups, [tool.uv]
dev-dependencies), requirements*.txt, *.csproj / *.fsproj / *.vbproj (PackageReference) and Directory.Packages.props
(PackageVersion).

Registries: npm (registry.npmjs.org), PyPI (pypi.org JSON API), NuGet (v3 registration index).
Verdict per new package:
  fail     not found (or unpublished): a hallucinated or mistyped name
  warn     first release younger than min_age_days (default 30), or fewer than min_versions (default 3) versions
  unknown  the registry could not be reached (no network: the rest are not asked); never a failure
  ignored  matches dependencies.ignore_packages (globs, e.g. "@myorg/*" for a private registry)
Config: <repo>/.armature/quality.json, section "dependencies": {min_age_days, min_versions, ignore_packages}.
Tests: ARMATURE_REGISTRY_FIXTURES=<file.json> answers from {url: body} (missing url or null: 404;
"__offline__": true: no network) instead of the network.

Usage: deps_check.py [--repo DIR] (--staged | [--base REV] [--head REV]) [--config FILE]
  --staged  the index against HEAD (the pre-commit hook)
  --base    default: the merge base with origin/main, then main (PR validation)
Output: JSON {ok, status, summary, offline, packages: [{ecosystem, name, file, status, detail, first_release,
        versions}]} on stdout, one line per problem on stderr.
Exit: 0 pass, warn or skipped; 1 a package does not exist; 2 usage error.
"""
import argparse
import datetime
import fnmatch
import gzip
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request

try:
    import tomllib
except ImportError:   # Python < 3.11: pyproject.toml is then not parsed
    tomllib = None

DEFAULTS = {"min_age_days": 30, "min_versions": 3, "ignore_packages": []}
TIMEOUT_S = 8
USER_AGENT = "armature-deps-check (+https://github.com)"
REGISTRY_NAMES = {"npm": "npm", "pypi": "PyPI", "nuget": "NuGet"}
NPM_SECTIONS = ("dependencies", "devDependencies", "peerDependencies", "optionalDependencies")
NPM_SKIP = ("workspace:", "file:", "link:", "portal:", "git+", "git:", "github:", "gitlab:", "bitbucket:", "http:",
            "https:", "catalog:", "patch:")
GITHUB_SHORTHAND = re.compile(r"^[\w.-]+/[\w.-]+(#.*)?$")
PEP508_NAME = re.compile(r"^\s*([A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?)")
REQ_FILE = re.compile(r"requirements[\w.-]*\.txt")
NUGET_REF = re.compile(r"<Package(?:Reference|Version)\b[^>]*?\bInclude\s*=\s*\"([^\"]+)\"", re.I)
NUGET_UNSET = 1901   # unlisted NuGet versions report a 1900-01-01 publish date


class OfflineError(Exception):
    """The registry could not be reached (no network, DNS, timeout)."""


# ── manifests ────────────────────────────────────────────────────────────────

def manifest_kind(path):
    """'npm', 'pypi', 'nuget' or None for a repo-relative path."""
    if path.startswith("node_modules/") or "/node_modules/" in path:
        return None
    base = path.rsplit("/", 1)[-1]
    if base == "package.json":
        return "npm"
    if base == "pyproject.toml" or REQ_FILE.fullmatch(base):
        return "pypi"
    if base.endswith((".csproj", ".fsproj", ".vbproj")) or base == "Directory.Packages.props":
        return "nuget"
    return None


def normalise(eco, name):
    if eco == "pypi":
        return re.sub(r"[-_.]+", "-", name).lower()
    if eco == "nuget":
        return name.lower()
    return name


def npm_names(text):
    try:
        data = json.loads(text)
    except ValueError:
        return {}
    out = {}
    for section in NPM_SECTIONS:
        deps = data.get(section) if isinstance(data, dict) else None
        if not isinstance(deps, dict):
            continue
        for name, spec in deps.items():
            spec = spec if isinstance(spec, str) else ""
            if spec.startswith("npm:"):
                alias = spec[4:]
                name = alias.rsplit("@", 1)[0] if alias.rfind("@") > 0 else alias
            elif spec.startswith(NPM_SKIP + (".", "/", "~/")) or GITHUB_SHORTHAND.match(spec):
                continue   # a path, a URL or a GitHub shorthand (owner/repo), not a registry package
            out[name] = name
    return out


def pep508_name(spec):
    if not isinstance(spec, str) or " @ " in spec.split(";")[0] or re.match(r"^\s*\S+\s*@\s*\S+://", spec):
        return None
    m = PEP508_NAME.match(spec)
    return m.group(1) if m else None


def pyproject_names(text):
    if tomllib is None:
        return {}
    try:
        data = tomllib.loads(text)
    except (tomllib.TOMLDecodeError, ValueError):
        return {}
    specs = []
    project = data.get("project") or {}
    specs += project.get("dependencies") or []
    for group in (project.get("optional-dependencies") or {}).values():
        specs += group or []
    for group in (data.get("dependency-groups") or {}).values():
        specs += [s for s in group or [] if isinstance(s, str)]
    tool = data.get("tool") or {}
    specs += (tool.get("uv") or {}).get("dev-dependencies") or []
    out = {}
    for s in specs:
        name = pep508_name(s)
        if name:
            out[normalise("pypi", name)] = name
    poetry = tool.get("poetry") or {}
    tables = [poetry.get("dependencies") or {}, poetry.get("dev-dependencies") or {}]
    tables += [(g or {}).get("dependencies") or {} for g in (poetry.get("group") or {}).values()]
    for table in tables:
        for name, spec in table.items():
            if name.lower() == "python" or (isinstance(spec, dict) and ({"path", "git", "url"} & set(spec))):
                continue
            out[normalise("pypi", name)] = name
    return out


def requirements_names(text):
    out = {}
    for raw in text.splitlines():
        line = raw.split(" #", 1)[0].strip()
        if not line or line.startswith(("#", "-", ".", "/")) or "://" in line:
            continue
        name = pep508_name(line)
        if name:
            out[normalise("pypi", name)] = name
    return out


def nuget_names(text):
    return {normalise("nuget", n): n for n in NUGET_REF.findall(text or "")}


def names_in(path, text):
    """{normalised: as written} for one manifest's text (None or unparsable: empty)."""
    if not text:
        return {}
    kind = manifest_kind(path)
    if kind == "npm":
        return npm_names(text)
    if kind == "nuget":
        return nuget_names(text)
    if kind == "pypi":
        return pyproject_names(text) if path.endswith("pyproject.toml") else requirements_names(text)
    return {}


def added_packages(path, old_text, new_text):
    """[(ecosystem, name)] in new_text that old_text did not have, sorted by name."""
    kind = manifest_kind(path)
    if kind is None:
        return []
    old, new = names_in(path, old_text), names_in(path, new_text)
    # PyPI names are compared and reported normalised (PEP 503); npm and NuGet names as written.
    return sorted(((kind, k if kind == "pypi" else new[k]) for k in new if k not in old), key=lambda t: t[1].lower())


# ── registries ───────────────────────────────────────────────────────────────

def registry_url(eco, name):
    if eco == "npm":
        return "https://registry.npmjs.org/" + name.replace("/", "%2F")
    if eco == "pypi":
        return f"https://pypi.org/pypi/{normalise('pypi', name)}/json"
    return f"https://api.nuget.org/v3/registration5-gz-semver2/{name.lower()}/index.json"


def decode_body(raw):
    if raw[:2] == b"\x1f\x8b":
        raw = gzip.decompress(raw)
    return json.loads(raw.decode("utf-8"))


def http_fetch(url):
    """(status, body or None). Raises OfflineError when the registry cannot be reached."""
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json",
                                               "Accept-Encoding": "gzip"})
    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT_S) as resp:   # noqa: S310 (fixed https registry hosts)
            return resp.status, decode_body(resp.read())
    except urllib.error.HTTPError as e:
        return e.code, None
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        raise OfflineError(str(getattr(e, "reason", e)))
    except ValueError:
        return 200, None


def fixture_fetcher(path):
    with open(path, encoding="utf-8") as fh:
        table = json.load(fh)

    def fetch(url):
        if table.get("__offline__"):
            raise OfflineError("offline (fixtures)")
        body = table.get(url)
        return (404, None) if body is None else (200, body)
    return fetch


def default_fetch():
    path = os.environ.get("ARMATURE_REGISTRY_FIXTURES")
    return fixture_fetcher(path) if path else http_fetch


def parse_time(text):
    try:
        return datetime.datetime.fromisoformat(str(text).replace("Z", "+00:00")).astimezone(datetime.timezone.utc)
    except ValueError:
        return None


def npm_info(body):
    versions = body.get("versions") or {}
    if not versions:
        return None
    times = body.get("time") or {}
    first = parse_time(times["created"]) if times.get("created") else None
    if first is None:
        stamps = [parse_time(times[v]) for v in versions if times.get(v)]
        first = min([s for s in stamps if s], default=None)
    return first, len(versions)


def pypi_info(body):
    releases = {v: files for v, files in (body.get("releases") or {}).items() if files}
    if not releases:
        return None if not body.get("info") else (None, 0)
    stamps = [parse_time(f.get("upload_time_iso_8601") or f.get("upload_time"))
              for files in releases.values() for f in files]
    return min([s for s in stamps if s], default=None), len(releases)


def nuget_info(body, fetch):
    pages = body.get("items") or []
    if not pages:
        return None
    count = sum(int(p.get("count") or 0) for p in pages)
    first_page = pages[0]
    leaves = first_page.get("items")
    if leaves is None and first_page.get("@id"):
        status, page = fetch(first_page["@id"])
        leaves = (page or {}).get("items") or [] if status == 200 else []
    stamps = [parse_time((leaf.get("catalogEntry") or {}).get("published")) for leaf in leaves or []]
    stamps = [s for s in stamps if s and s.year >= NUGET_UNSET]
    return min(stamps, default=None), count or len(leaves or [])


def lookup(eco, name, fetch):
    """(exists, first_release datetime or None, versions or None). Raises OfflineError."""
    status, body = fetch(registry_url(eco, name))
    if status == 404:
        return False, None, None
    if status != 200 or not isinstance(body, dict):
        raise OfflineError(f"{REGISTRY_NAMES[eco]} answered HTTP {status}")
    info = npm_info(body) if eco == "npm" else pypi_info(body) if eco == "pypi" else nuget_info(body, fetch)
    if info is None:
        return False, None, None
    return True, info[0], info[1]


def ignored(eco, name, patterns):
    return any(fnmatch.fnmatchcase(name, p) or fnmatch.fnmatchcase(f"{eco}:{name}", p) for p in patterns)


def assess(eco, name, exists, first, versions, now, cfg):
    reg = REGISTRY_NAMES[eco]
    if not exists:
        return "fail", (f"not found on {reg}: the name may be hallucinated or mistyped (slopsquatting risk). Check "
                        f"it, or list a private package in .armature/quality.json dependencies.ignore_packages")
    notes = []
    if first is not None:
        age = (now - first).days
        if age < cfg["min_age_days"]:
            notes.append(f"first published {age} day(s) ago (< {cfg['min_age_days']})")
    if versions is not None and versions < cfg["min_versions"]:
        notes.append(f"only {versions} version(s) (< {cfg['min_versions']})")
    if notes:
        return "warn", f"on {reg}, but " + " and ".join(notes) + ": make sure it is the package you meant"
    return "pass", f"on {reg}"


def check_packages(packages, fetch=None, now=None, config=None):
    """packages: [(ecosystem, name, file)]. Returns the result dict (see the module doc)."""
    cfg = {**DEFAULTS, **(config or {})}
    fetch = fetch or default_fetch()
    now = now or datetime.datetime.now(datetime.timezone.utc)
    out, offline, seen = [], None, {}
    for eco, name, path in packages:
        rec = {"ecosystem": eco, "name": name, "file": path, "status": "pass", "detail": "",
               "first_release": None, "versions": None}
        key = (eco, normalise(eco, name))
        if ignored(eco, name, cfg["ignore_packages"]):
            rec.update(status="ignored", detail="in dependencies.ignore_packages")
        elif offline is not None:
            rec.update(status="unknown", detail=f"not checked: {offline}")
        elif key in seen:
            rec.update({k: seen[key][k] for k in ("status", "detail", "first_release", "versions")})
        else:
            try:
                exists, first, versions = lookup(eco, name, fetch)
                status, detail = assess(eco, name, exists, first, versions, now, cfg)
                rec.update(status=status, detail=detail, first_release=first.isoformat() if first else None,
                           versions=versions)
            except OfflineError as e:
                offline = f"registry unreachable ({e})"
                rec.update(status="unknown", detail=f"not checked: {offline}")
            seen[key] = rec
        out.append(rec)
    statuses = {r["status"] for r in out}
    if not out:
        status, summary = "skipped", "no package added to a manifest"
    elif "fail" in statuses:
        status = "fail"
    elif statuses & {"warn", "unknown"}:
        status = "warn"
    else:
        status = "pass"
    if out:
        counts = {s: sum(r["status"] == s for r in out) for s in ("fail", "warn", "unknown", "ignored")}
        parts = [f"{n} {s}" for s, n in counts.items() if n]
        summary = f"{len(out)} new package(s)" + (": " + ", ".join(parts) if parts else ", all on their registry")
    return {"ok": status != "fail", "status": status, "summary": summary, "offline": offline is not None,
            "packages": out}


# ── git ──────────────────────────────────────────────────────────────────────

def git(repo, *args):
    p = subprocess.run(["git", "-C", repo, *args], capture_output=True)
    return p.returncode, p.stdout.decode("utf-8", "replace")


def show(repo, spec):
    rc, out = git(repo, "show", spec)
    return out if rc == 0 else None


def manifest_changes(repo, old_rev, new_rev, paths):
    """[(ecosystem, name, path)] added between two revisions ('' as new_rev: the index) for these paths."""
    found = []
    for path in paths:
        if manifest_kind(path) is None:
            continue
        old = show(repo, f"{old_rev}:{path}") if old_rev else None
        new = show(repo, f"{new_rev}:{path}")
        found += [(eco, name, path) for eco, name in added_packages(path, old, new)]
    return found


def staged_changes(repo):
    _, out = git(repo, "diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z")
    head = "HEAD" if git(repo, "rev-parse", "--verify", "--quiet", "HEAD")[0] == 0 else None
    return manifest_changes(repo, head, "", [p for p in out.split("\0") if p])


def merge_base(repo, base, head):
    for ref in [base] if base else ["origin/main", "main", "origin/master", "master"]:
        if git(repo, "rev-parse", "--verify", "--quiet", ref + "^{commit}")[0] == 0:
            rc, mb = git(repo, "merge-base", ref, head)
            if rc == 0 and mb.strip():
                return mb.strip()
    raise ValueError(f"no merge base with {base or 'origin/main or main'}; pass --base")


def range_changes(repo, base_rev, head="HEAD"):
    _, out = git(repo, "diff", "--name-only", "--diff-filter=ACMR", "-z", base_rev, head)
    return manifest_changes(repo, base_rev, head, [p for p in out.split("\0") if p])


def load_config(repo, path=None):
    path = path or os.path.join(repo, ".armature", "quality.json")
    try:
        with open(path, encoding="utf-8") as fh:
            section = (json.load(fh) or {}).get("dependencies") or {}
    except (OSError, ValueError, AttributeError):
        return {}
    return {k: section[k] for k in DEFAULTS if k in section}


def report(result, prefix="deps-check"):
    for p in result["packages"]:
        if p["status"] in ("fail", "warn", "unknown"):
            print(f"{prefix}: {p['status']}: {p['ecosystem']} {p['name']} ({p['file']}): {p['detail']}",
                  file=sys.stderr)


def main(argv=None):
    ap = argparse.ArgumentParser(description="New packages in manifests must exist on their registry.")
    ap.add_argument("--repo", default=".")
    ap.add_argument("--staged", action="store_true")
    ap.add_argument("--base")
    ap.add_argument("--head", default="HEAD")
    ap.add_argument("--config")
    try:
        args = ap.parse_args(argv)
    except SystemExit as e:
        return 2 if e.code else 0
    rc, root = git(args.repo, "rev-parse", "--show-toplevel")
    if rc != 0:
        print(f"deps-check: not a git repo: {args.repo}", file=sys.stderr)
        return 2
    root = root.strip()
    try:
        changes = staged_changes(root) if args.staged else range_changes(root, merge_base(root, args.base, args.head),
                                                                           args.head)
    except ValueError as e:
        print(f"deps-check: {e}", file=sys.stderr)
        return 2
    result = check_packages(changes, config=load_config(root, args.config))
    print(json.dumps(result, indent=2))
    report(result)
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
