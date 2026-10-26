#!/usr/bin/env python3
"""Run existing repository checks locally; never starts a GitHub job or uploads evidence."""
from __future__ import annotations

import argparse
import contextlib
import fcntl
import stat
import types
from datetime import datetime, timezone
from time import monotonic_ns
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import uuid

# Workflow parsing is limited to the installed PyYAML safe loader. No action is executed.
import yaml

WORKFLOWS = (
    ".github/workflows/ci.yml",
    ".github/workflows/ui-catalog-validation.yml",
    ".github/workflows/cari-c02-link-acceptance.yml",
    ".github/workflows/c01-real-browser-e2e.yml",
    ".github/workflows/browser-acceptance.yml",
    ".github/workflows/openapi-generate.yml",
)
ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")
SENSITIVE = re.compile(r"(?i)\b[\w.-]*(?:password|passwd|secret|token|api[_-]?key|access[_-]?key|private[_-]?key|authorization|connection[_-]?string)[\w.-]*\s*[=:]")
BEARER = re.compile(r"(?i)\bBearer\s+[^\r\n]+")
LEASE_ROOT = Path("/home/ci/local-ci-leases")
LOCAL_PATH = "/opt/goymen-ci/tools/dotnet-10.0.303:/opt/goymen-ci/tools/node-v24.20.0-linux-x64/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
P032_BROWSER_PROFILE = "p032-u02-r14"
HEAVY_PROFILES = frozenset(("frontend", P032_BROWSER_PROFILE))
EVIDENCE_SEGMENT = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")
WINDOWS_RESERVED_NAMES = frozenset({"CON", "PRN", "AUX", "NUL"}) | frozenset(
    {f"{prefix}{index}" for prefix in ("COM", "LPT") for index in range(1, 10)}
)


def scrub_output(value: str) -> tuple[str, bool]:
    """Fail closed on credential-shaped output; never persist its value."""
    detected = bool(SENSITIVE.search(value) or BEARER.search(value))
    if detected:
        return "[CREDENTIAL_REDACTED]" + ("\n" if value.endswith("\n") else ""), True
    return value, False


def validate_lease(root: Path, profile: str, path: Path, expected_head: str) -> dict:
    if not re.fullmatch(r"[0-9a-f]{40}", expected_head):
        raise ValueError("Expected source commit is required")
    if path.parent != LEASE_ROOT or not re.fullmatch(r"lease-[0-9a-f]{32}\.json", path.name):
        raise ValueError("A source-bound Windows launcher lease is required")
    if path.is_symlink() or not stat.S_ISREG(path.stat().st_mode) or path.stat().st_uid != os.getuid():
        raise ValueError("Invalid launcher lease file owner/type")
    if path.stat().st_size > 4096:
        raise ValueError("Invalid launcher lease size")
    lease = json.loads(path.read_text(encoding="utf-8-sig"))
    if (lease.get("host") != "DESKTOP-11CDRHD" or lease.get("status") != "ACTIVE"
            or lease.get("repository") != str(root) or lease.get("expectedHead") != expected_head
            or lease.get("profile") != profile or lease.get("nonce") != path.stem[6:]):
        raise ValueError("Launcher lease context mismatch")
    expires = datetime.fromisoformat(lease["expiresUtc"])
    if expires <= datetime.now(timezone.utc):
        raise ValueError("Launcher lease expired")
    if git(root, "rev-parse", "--show-toplevel") != str(root):
        raise ValueError("A canonical repository root is required")
    if git(root, "remote", "get-url", "origin") != "https://github.com/sinangym/GoymenERP.git":
        raise ValueError("Unexpected repository origin")
    if git(root, "rev-parse", "HEAD") != expected_head:
        raise ValueError("Source moved from the launcher expected commit")
    if platform.node() != "goymen-ci-linux":
        raise ValueError("Unexpected local worker host")
    if profile in HEAVY_PROFILES and int(lease.get("availableMemoryBytes", 0)) < 2 * 1024**3:
        raise ValueError("Launcher resource admission is missing")
    return lease


@contextlib.contextmanager
def heavy_lock(profile: str):
    if profile not in HEAVY_PROFILES:
        yield
        return
    path = Path("/home/ci/.local/state/goymen-ci/local-heavy.lock")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a+") as handle:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            yield
        finally:
            fcntl.flock(handle, fcntl.LOCK_UN)



def now() -> str:
    return datetime.now(timezone.utc).isoformat()


def clock_fields(prefix: str) -> dict[str, str | int]:
    """Wall time is descriptive; monotonic ordering is local to this process/run only."""
    return {prefix + "Utc": now(), prefix + "MonotonicNs": monotonic_ns()}


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def git(root: Path, *arguments: str) -> str:
    return subprocess.check_output(["git", "-C", str(root), *arguments], text=True, encoding="utf-8", env=command_environment()).strip()


def source_state(root: Path) -> dict:
    paths = subprocess.check_output(
        ["git", "-C", str(root), "ls-files", "-z", "--cached", "--others", "--exclude-standard"], env=command_environment()
    ).decode("utf-8").split("\0")
    manifest = []
    for relative in sorted(set(filter(None, paths))):
        path = root / relative
        if path.is_symlink():
            kind, content = "symlink", os.readlink(path).encode("utf-8")
        elif path.is_file():
            kind, content = "file", path.read_bytes()
        elif not path.exists():
            kind, content = "deleted", b""
        else:
            raise ValueError("Unsupported source entry")
        manifest.append({"path": relative, "kind": kind, "sha256": hashlib.sha256(content).hexdigest()})
    encoded = json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return {
        "commit": git(root, "rev-parse", "HEAD"),
        "tree": git(root, "rev-parse", "HEAD^{tree}"),
        "dirty": bool(git(root, "status", "--porcelain=v1")),
        "worktreeSha256": hashlib.sha256(encoded).hexdigest(),
        "files": manifest,
    }


def workflow(root: Path, relative: str) -> dict:
    data = yaml.safe_load((root / relative).read_text(encoding="utf-8"))
    if not isinstance(data, dict) or not isinstance(data.get("jobs"), dict):
        raise ValueError("Invalid canonical workflow")
    return data


def full_inventory(root: Path) -> list[dict]:
    result = []
    for relative in WORKFLOWS:
        data = workflow(root, relative)
        for job_id, job in data["jobs"].items():
            result.append({
                "workflow": relative, "job": job_id, "name": job.get("name", job_id),
                "runner": job["runs-on"], "needs": job.get("needs", []),
                "matrix": job.get("strategy", {}).get("matrix", {}),
                "services": sorted(job.get("services", {})),
                "steps": [{key: step[key] for key in ("name", "run", "uses", "working-directory", "if") if key in step}
                          for step in job["steps"]],
                "status": "NOT_RUN",
            })
    return result


def selected_steps(root: Path, profile: str) -> list[dict]:
    ci = workflow(root, WORKFLOWS[0])["jobs"]
    if profile == "guards":
        return [next(step for step in ci["backend-build"]["steps"]
                     if step.get("name") == ".NET bootstrap integrity preflight")]
    if profile == "catalog":
        return [step for step in workflow(root, WORKFLOWS[1])["jobs"]["validate-ui-catalog"]["steps"] if "run" in step]
    if profile == "frontend":
        steps = [dict(step) for step in ci["frontend"]["steps"] if "run" in step]
        for step in steps:
            step["working-directory"] = "src/frontend/goymen-erp-web"
            if step["run"] == "npm test":
                # Worker capacity only; no test is filtered or removed.
                step["run"] = "npm test -- --maxWorkers=1"
        return steps
    raise ValueError("Unknown targeted profile")


def load_tracked_source_module(root: Path, relative: str, name: str, revision: str = "HEAD"):
    """Execute the clean tracked UTF-8 source bytes, never an ignored bytecode cache."""
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()) or path.is_symlink() or not stat.S_ISREG(path.stat().st_mode):
        raise ValueError("Reviewed Python source must be a confined regular file")
    if git(root, "ls-files", "--error-unmatch", relative) != relative:
        raise ValueError("Reviewed Python source must be tracked")
    source_bytes = subprocess.check_output(
        ["git", "-C", str(root), "cat-file", "blob", f"{revision}:{relative}"],
        env=command_environment(),
    )
    if path.read_bytes() != source_bytes:
        raise ValueError("Reviewed Python source does not match the exact HEAD blob")
    try:
        source = source_bytes.decode("utf-8")
    except UnicodeDecodeError as error:
        raise ValueError("Reviewed Python source must be strict UTF-8") from error
    module = types.ModuleType(name)
    module.__file__ = str(path)
    module.__package__ = name.rpartition(".")[0]
    sys.modules[name] = module
    try:
        exec(compile(source, str(path), "exec", dont_inherit=True), module.__dict__)
    except BaseException:
        if sys.modules.get(name) is module:
            del sys.modules[name]
        raise
    return module


def load_browser_adapter(root: Path, revision: str = "HEAD"):
    module = load_tracked_source_module(
        root,
        "tools/ci/local_browser_acceptance.py",
        "local_browser_acceptance",
        revision,
    )
    if module.PROFILE != P032_BROWSER_PROFILE:
        raise ValueError("P03.2 browser adapter profile mismatch")
    return module


def evidence_manifest(directory: Path) -> list[dict]:
    """Hash every confined regular file while preserving nested proof paths."""
    result = []
    for path in sorted(directory.rglob("*")):
        relative = path.relative_to(directory).as_posix()
        if relative == "SHA256_MANIFEST.json":
            continue
        parts = relative.split("/")
        if any(part in {".", ".."} or EVIDENCE_SEGMENT.fullmatch(part) is None for part in parts):
            raise ValueError("Unsafe local evidence path")
        metadata = path.lstat()
        if stat.S_ISLNK(metadata.st_mode):
            raise ValueError("Symlink evidence rejected")
        if path.is_dir():
            continue
        if not stat.S_ISREG(metadata.st_mode) or not path.resolve().is_relative_to(directory.resolve()):
            raise ValueError("Non-regular evidence rejected")
        content = path.read_bytes()
        result.append({"path": relative, "bytes": len(content), "sha256": hashlib.sha256(content).hexdigest()})
    return validate_evidence_manifest(directory, result)


def windows_portable_evidence_key(relative: str) -> str:
    """Return the destination identity used by a case-insensitive Windows bundle."""
    parts = relative.split("/") if isinstance(relative, str) else []
    if not parts or len(relative) > 240:
        raise ValueError("Evidence path is not Windows-portable")
    normalized = []
    for part in parts:
        if (not part or len(part) > 255 or part.endswith((".", " "))
                or part.split(".", 1)[0].upper() in WINDOWS_RESERVED_NAMES):
            raise ValueError("Evidence path is not Windows-portable")
        normalized.append(part.casefold())
    return "/".join(normalized)


def validate_evidence_manifest(directory: Path, entries: list[dict]) -> list[dict]:
    """Re-open and verify the exact relative-path manifest consumed by Windows."""
    root = directory.resolve()
    seen = set()
    windows_seen = set()
    validated = []
    for entry in entries:
        if not isinstance(entry, dict) or set(entry) != {"path", "bytes", "sha256"}:
            raise ValueError("Invalid local evidence manifest entry")
        relative = entry["path"]
        parts = relative.split("/") if isinstance(relative, str) else []
        if not parts or any(part in {".", ".."} or EVIDENCE_SEGMENT.fullmatch(part) is None for part in parts):
            raise ValueError("Unsafe local evidence path")
        if relative in seen:
            raise ValueError("Duplicate local evidence path")
        seen.add(relative)
        windows_key = windows_portable_evidence_key(relative)
        if windows_key in windows_seen:
            raise ValueError("Windows-portable evidence path collision")
        windows_seen.add(windows_key)
        if (type(entry["bytes"]) is not int or entry["bytes"] < 0
                or not isinstance(entry["sha256"], str)
                or re.fullmatch(r"[0-9a-f]{64}", entry["sha256"]) is None):
            raise ValueError("Invalid local evidence hash/size")
        path = root
        for index, part in enumerate(parts):
            path = path / part
            try:
                metadata = path.lstat()
            except OSError as error:
                raise ValueError("Manifest evidence path is missing") from error
            if stat.S_ISLNK(metadata.st_mode):
                raise ValueError("Symlink evidence rejected")
            if index < len(parts) - 1 and not stat.S_ISDIR(metadata.st_mode):
                raise ValueError("Evidence parent is not a directory")
        if not stat.S_ISREG(path.stat().st_mode) or not path.resolve().is_relative_to(root):
            raise ValueError("Non-regular evidence rejected")
        content = path.read_bytes()
        if len(content) != entry["bytes"] or hashlib.sha256(content).hexdigest() != entry["sha256"]:
            raise ValueError("Local evidence hash/size mismatch")
        validated.append(entry)
    return validated


def test_counts(log: str) -> dict | None:
    text = ANSI.sub("", log)
    totals = re.findall(r"(?m)^Ran (\d+) tests? in ", text)
    if totals:
        failed = sum(int(n) for n in re.findall(r"failures=(\d+)", text))
        errors = sum(int(n) for n in re.findall(r"errors=(\d+)", text))
        skipped = sum(int(n) for n in re.findall(r"skipped=(\d+)", text))
        total = sum(map(int, totals))
        return {"total": total, "passed": None if re.search(r"(?m)^ {2}test_.*\.\.\. (?:FAIL|ERROR)", text) else total - failed - errors - skipped,
                "failed": failed, "errors": errors, "skipped": skipped}
    match = re.search(r"(?m)^\s*Tests\s+(.+)$", text)
    if match:
        fields = {key: int(value) for value, key in re.findall(r"(\d+) (passed|failed|skipped|todo)", match[1])}
        if fields:
            return {"total": sum(fields.values()), "passed": fields.get("passed", 0),
                    "failed": fields.get("failed", 0), "errors": None,
                    "skipped": fields.get("skipped", 0), "todo": fields.get("todo", 0)}
    return None


def command_environment() -> dict[str, str]:
    # Do not copy the caller environment, shell startup hooks or credential providers.
    return {
        "PATH": LOCAL_PATH, "HOME": "/home/ci", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
        "DOTNET_ROOT": "/opt/goymen-ci/tools/dotnet-10.0.303",
        "DOTNET_CLI_HOME": "/home/ci/.dotnet", "NUGET_PACKAGES": "/home/ci/.cache/nuget/packages",
        "NPM_CONFIG_CACHE": "/home/ci/.cache/npm", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_PROCESSOR_COUNT": "2", "NODE_OPTIONS": "--max-old-space-size=1536", "CI": "true",
    }


def run_step(root: Path, directory: Path, index: int, step: dict) -> dict:
    command = step["run"]
    if "${{" in command:
        raise ValueError("Unresolved workflow expression cannot run locally")
    working = (root / step.get("working-directory", ".")).resolve()
    if not working.is_relative_to(root):
        raise ValueError("Working directory escaped repository")
    entry = {"name": scrub_output(step.get("name", command))[0], "command": scrub_output(command)[0],
             "workingDirectory": str(working), **clock_fields("start"), "status": "RUNNING"}
    write_json(directory / f"step-{index:02d}.json", entry)
    with subprocess.Popen(["/bin/bash", "--noprofile", "--norc", "-p", "-euo", "pipefail", "-c", command],
                               cwd=working, env=command_environment(), stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="strict") as process:
        chunks = []
        credential_output = False
        with (directory / f"step-{index:02d}.log").open("x", encoding="utf-8") as log:
            assert process.stdout is not None
            for line in process.stdout:
                safe, detected = scrub_output(line)
                if credential_output:
                    # A wrapped/multiline credential cannot resume ordinary logging safely.
                    safe, detected = "[OUTPUT_SUPPRESSED_AFTER_CREDENTIAL]\n", True
                credential_output = credential_output or detected
                chunks.append(safe)
                log.write(safe)
                log.flush()
                print(safe, end="", flush=True)
        process.stdout.close()
        code = process.wait()
    entry.update({**clock_fields("finish"), "exitCode": code, "status": "PASS" if code == 0 else "FAIL",
                  "counts": test_counts("".join(chunks)), "credentialOutputDetected": credential_output})
    if credential_output:
        entry.update({"status": "BLOCKED_SECRET_OUTPUT", "exitCode": code if code else 2})
    write_json(directory / f"step-{index:02d}.json", entry)
    return entry


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--profile",
        choices=("guards", "catalog", "frontend", P032_BROWSER_PROFILE, "full"),
        required=True,
    )
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--evidence-root", type=Path, required=True)
    parser.add_argument("--expected-head", required=True)
    parser.add_argument("--launcher-lease", type=Path, required=True)
    parser.add_argument("--plan", action="store_true", help="Record canonical full scope without running it")
    args = parser.parse_args()
    root = args.repository.resolve()
    evidence = args.evidence_root.resolve()
    if evidence == root or evidence.is_relative_to(root):
        parser.error("Evidence must be outside the source checkout")
    if platform.system() != "Linux":
        parser.error("This entry consumes canonical Linux commands; C006 stays in its Windows owner script")
    os.nice(10)  # Lower only this new local worker and its children, never existing processes.
    evidence.mkdir(parents=True, exist_ok=True)
    run_id = datetime.now(timezone.utc).strftime("local-%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8]
    directory = evidence / run_id
    directory.mkdir(mode=0o700)
    lease = validate_lease(root, args.profile, args.launcher_lease, args.expected_head)
    initial = source_state(root)
    write_json(directory / "source-start.json", initial)
    result = {"runId": run_id, "kind": "LOCAL_FULL" if args.profile == "full" else "LOCAL_TARGETED",
              "profile": args.profile, **clock_fields("start"), "computer": platform.node(),
              "environment": {"platform": platform.platform(), "python": platform.python_version()},
              "source": {key: value for key, value in initial.items() if key != "files"},
              "steps": [], "githubJobStarted": False, "githubStorageUsed": False, "status": "RUNNING"}
    write_json(directory / "full-scope.json", full_inventory(root))
    code = 2
    try:
        if args.plan:
            result["status"] = "NOT_RUN_PLAN_ONLY"
            code = 0
        elif args.profile == "full":
            # Full admission is deliberately fail-closed while local transport/owned SQL/browser
            # adapters and the Windows C006 receipt have not been independently admitted.
            result["status"] = "LOCAL_FULL_BLOCKED"
            result["blockers"] = [
                "Reviewed-source pins and a clean exact source commit are required",
                "Local backend/relational proof transport must retain the existing aggregate verifiers",
                "Fresh owned SQL, C01/browser, container and Windows C006 adapters require admission",
            ]
        else:
            if args.profile in HEAVY_PROFILES and initial["dirty"]:
                raise ValueError("Heavy acceptance requires a clean exact source commit")
            if args.profile == "frontend":
                version = subprocess.check_output(["node", "--version"], text=True, env=command_environment()).strip()
                if not re.fullmatch(r"v24\.\d+\.\d+", version):
                    raise ValueError("Node24 is required by the canonical workflow")
            code = 0
            with heavy_lock(args.profile):
                if args.profile == P032_BROWSER_PROFILE:
                    validate_lease(root, args.profile, args.launcher_lease, args.expected_head)
                    adapter = load_browser_adapter(root, args.expected_head)
                    entry = adapter.run_acceptance(
                        root,
                        directory / P032_BROWSER_PROFILE,
                        args.expected_head,
                        lambda: validate_lease(root, args.profile, args.launcher_lease, args.expected_head),
                    )
                    result["steps"].append(entry)
                    code = entry["exitCode"]
                else:
                    for index, step in enumerate(selected_steps(root, args.profile), start=1):
                        validate_lease(root, args.profile, args.launcher_lease, args.expected_head)
                        entry = run_step(root, directory, index, step)
                        result["steps"].append(entry)
                        if entry["exitCode"] != 0:
                            code = entry["exitCode"]
                            break
            blocked_step = next((entry["status"] for entry in result["steps"]
                                 if entry["status"].startswith("BLOCKED")), None)
            result["status"] = blocked_step or (("LOCAL_TARGETED_PASS_DIRTY" if initial["dirty"] else "LOCAL_TARGETED_PASS")
                                                  if code == 0 else "LOCAL_TARGETED_FAIL")
    except (OSError, RuntimeError, ValueError, subprocess.SubprocessError) as error:
        result.update({"status": "BLOCKED", "reason": type(error).__name__ + ": " + scrub_output(str(error))[0]})
        code = 2
    finally:
        final = source_state(root)
        write_json(directory / "source-finish.json", final)
        if initial != final:
            result.update({"status": "BLOCKED_SOURCE_CHANGED", "reason": "Source changed during local checks"})
            code = 2
        result.update({**clock_fields("finish"), "exitCode": code})
        write_json(directory / "result.json", result)
        write_json(directory / "SHA256_MANIFEST.json", evidence_manifest(directory))
        print("LOCAL_EVIDENCE=" + str(directory))
        print("LOCAL_RESULT=" + result["status"])
    return code


if __name__ == "__main__":
    raise SystemExit(main())
