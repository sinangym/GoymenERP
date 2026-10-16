#!/usr/bin/env python3
"""Run existing repository checks locally; never starts a GitHub job or uploads evidence."""
from __future__ import annotations

import argparse
import contextlib
import fcntl
import stat
from datetime import datetime, timezone
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
SENSITIVE = re.compile(r"(?i)\b(?:password|passwd|secret|token|api[_-]?key|client[_-]?secret|authorization|proxy-authorization)\s*[=:]\s*.*")
BEARER = re.compile(r"(?i)\bBearer\s+[^\r\n]+")
LEASE_ROOT = Path("/home/ci/local-ci-leases")
LOCAL_PATH = "/opt/goymen-ci/tools/dotnet-10.0.303:/opt/goymen-ci/tools/node-v24.20.0-linux-x64/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"


def scrub_output(value: str) -> tuple[str, bool]:
    """Fail closed on credential-shaped output; never persist its value."""
    redacted = SENSITIVE.sub("[CREDENTIAL_REDACTED]", value)
    redacted = BEARER.sub("[CREDENTIAL_REDACTED]", redacted)
    if value.endswith("\n") and not redacted.endswith("\n"):
        redacted += "\n"
    return redacted, redacted != value


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
    if profile == "frontend" and int(lease.get("availableMemoryBytes", 0)) < 2 * 1024**3:
        raise ValueError("Launcher resource admission is missing")
    return lease


@contextlib.contextmanager
def heavy_lock(profile: str):
    if profile != "frontend":
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
             "workingDirectory": str(working), "startUtc": now(), "status": "RUNNING"}
    write_json(directory / f"step-{index:02d}.json", entry)
    process = subprocess.Popen(["/bin/bash", "--noprofile", "--norc", "-p", "-euo", "pipefail", "-c", command],
                               cwd=working, env=command_environment(), stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="strict")
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
    entry.update({"finishUtc": now(), "exitCode": code, "status": "PASS" if code == 0 else "FAIL",
                  "counts": test_counts("".join(chunks)), "credentialOutputDetected": credential_output})
    if credential_output:
        entry.update({"status": "BLOCKED_SECRET_OUTPUT", "exitCode": code if code else 2})
    write_json(directory / f"step-{index:02d}.json", entry)
    return entry


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", choices=("guards", "catalog", "frontend", "full"), required=True)
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
              "profile": args.profile, "startUtc": now(), "computer": platform.node(),
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
            if args.profile == "frontend" and initial["dirty"]:
                raise ValueError("Heavy acceptance requires a clean exact source commit")
            if args.profile == "frontend":
                version = subprocess.check_output(["node", "--version"], text=True, env=command_environment()).strip()
                if not re.fullmatch(r"v24\.\d+\.\d+", version):
                    raise ValueError("Node24 is required by the canonical workflow")
            code = 0
            with heavy_lock(args.profile):
                for index, step in enumerate(selected_steps(root, args.profile), start=1):
                    validate_lease(root, args.profile, args.launcher_lease, args.expected_head)
                    entry = run_step(root, directory, index, step)
                    result["steps"].append(entry)
                    if entry["exitCode"] != 0:
                        code = entry["exitCode"]
                        break
            result["status"] = ("LOCAL_TARGETED_PASS_DIRTY" if initial["dirty"] else "LOCAL_TARGETED_PASS") if code == 0 else "LOCAL_TARGETED_FAIL"
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        result.update({"status": "BLOCKED", "reason": type(error).__name__ + ": " + scrub_output(str(error))[0]})
        code = 2
    finally:
        final = source_state(root)
        write_json(directory / "source-finish.json", final)
        if initial != final:
            result.update({"status": "BLOCKED_SOURCE_CHANGED", "reason": "Source changed during local checks"})
            code = 2
        result.update({"finishUtc": now(), "exitCode": code})
        write_json(directory / "result.json", result)
        files = []
        for path in sorted(directory.iterdir()):
            if path.is_file():
                files.append({"path": path.name, "bytes": path.stat().st_size,
                              "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        write_json(directory / "SHA256_MANIFEST.json", files)
        print("LOCAL_EVIDENCE=" + str(directory))
        print("LOCAL_RESULT=" + result["status"])
    return code


if __name__ == "__main__":
    raise SystemExit(main())
