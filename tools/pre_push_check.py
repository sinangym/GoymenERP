#!/usr/bin/env python3
"""One-command local pre-push checks (light, no SQL Server, no Docker; mirrors CI steps only, so no `dotnet format` gate).

Usage:
  python3 tools/pre_push_check.py            # light checks (seconds)
  python3 tools/pre_push_check.py --full     # also recovery/release pin tests (~8 min, needs a clean git tree)
  python3 tools/pre_push_check.py --list     # print the steps and exit

This is a convenience pre-check, NOT a substitute for CI evidence: it does not
replace the required CI checks, the SQL Server shards, the architecture/api/modules
test groups or the recovery/release jobs, and its result is never merge evidence.

Every step runs the same command CI runs, from the repository root. A step whose tool is missing (for
example `dotnet`) is reported as SKIPPED, never as passed; the exit code is 1
if any step fails, 0 otherwise. Nothing is uploaded and no CI job is started.
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import time
from pathlib import Path

PY = ("python3", "-I", "-S", "-B")
REPO_ROOT = Path(__file__).resolve().parent.parent


def unittest_step(directory: str) -> tuple[str, tuple[str, ...], tuple[str, ...]]:
    return (
        f"unittest {directory}",
        (*PY, "-m", "unittest", "discover", "-s", directory, "-p", "test_*.py"),
        (),
    )


LIGHT = (
    ("git diff --check", ("git", "diff", "--check", "origin/main...HEAD"), ("git",)),
    ("dotnet bootstrap integrity", (*PY, "tools/validate_dotnet_bootstrap_integrity.py"), ()),
    unittest_step("tests/dotnet_bootstrap_integrity"),
    ("task manifest validator", (*PY, "tools/validate_task_manifest.py"), ()),
    unittest_step("tests/task_manifest_validator"),
    ("delivery backlog validator", (*PY, "tools/validate_delivery_backlog.py"), ()),
    unittest_step("tests/delivery_backlog_validator"),
    # Test-group inventory: group/shard partition logic and proof schema.
    unittest_step("tests/backend_test_group_verifier"),
    unittest_step("tests/rcsi_relational_acceptance_verifier"),
    unittest_step("tests/ci_safe_trx_summary"),
    unittest_step("tests/openapi_contract_drift"),
    unittest_step("tests/pre_push_check"),
    # OpenAPI drift: generate with the contract test, then compare with the baseline.
    (
        "OpenAPI contract generation",
        ("dotnet", "test", "tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj",
         "-noAutoResponse", "--configuration", "Release", "--filter", "FullyQualifiedName~OpenApiContractGenerationTests"),
        ("dotnet",),
    ),
    (
        "OpenAPI baseline compare",
        (*PY, "tools/validate_openapi_contract.py", "--baseline", "contracts/openapi/goymen-erp-v1.json",
         "--generated", "artifacts/openapi.json"),
        ("dotnet",),
    ),
)

FULL = (
    unittest_step("tests/recovery_surface_inventory"),
    unittest_step("tests/release_manifest"),
)


def run(steps: tuple, cwd: Path = REPO_ROOT) -> int:
    failed: list[str] = []
    skipped: list[str] = []
    for name, command, needs in steps:
        missing = [tool for tool in needs if shutil.which(tool) is None]
        if missing:
            print(f"SKIPPED  {name} (missing: {', '.join(missing)})", flush=True)
            skipped.append(name)
            continue
        started = time.monotonic()
        result = subprocess.run(command, check=False, capture_output=True, text=True, cwd=cwd)
        seconds = time.monotonic() - started
        if result.returncode == 0:
            print(f"OK       {name} ({seconds:.1f}s)", flush=True)
        else:
            print(f"FAILED   {name} (exit {result.returncode}, {seconds:.1f}s)", flush=True)
            print((result.stdout + result.stderr)[-4000:], flush=True)
            failed.append(name)
    print(f"\n{len(steps) - len(failed) - len(skipped)} ok, {len(failed)} failed, {len(skipped)} skipped")
    if skipped:
        print("Skipped steps are NOT verified locally; CI will run them.")
    return 1 if failed else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--full", action="store_true", help="also run the slow recovery/release pin tests")
    parser.add_argument("--list", action="store_true", help="print the steps and exit")
    args = parser.parse_args()
    steps = LIGHT + (FULL if args.full else ())
    if args.list:
        for name, command, _ in steps:
            print(f"{name}: {' '.join(command)}")
        return 0
    return run(steps)


if __name__ == "__main__":
    sys.exit(main())
