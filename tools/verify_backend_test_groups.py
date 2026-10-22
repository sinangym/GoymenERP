#!/usr/bin/env python3
"""Source-bound, fail-closed TestSDK project and TRX gate for backend CI groups."""

from __future__ import annotations

import argparse
from collections import Counter
from enum import Enum
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
from typing import Callable, TypeVar
import xml.etree.ElementTree as ET


GROUP_PROJECTS = {
    "architecture": (
        "tests/GoymenERP.ArchitectureTests/GoymenERP.ArchitectureTests.csproj",
    ),
    "api": (
        "tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj",
    ),
    "modules": (
        "tests/GoymenERP.Accounting.UnitTests/GoymenERP.Accounting.UnitTests.csproj",
        "tests/GoymenERP.Ai.UnitTests/GoymenERP.Ai.UnitTests.csproj",
        "tests/GoymenERP.Audit.UnitTests/GoymenERP.Audit.UnitTests.csproj",
        "tests/GoymenERP.Counterparty.UnitTests/GoymenERP.Counterparty.UnitTests.csproj",
        "tests/GoymenERP.DocumentGeneration.Pdf.Tests/GoymenERP.DocumentGeneration.Pdf.Tests.csproj",
        "tests/GoymenERP.Documents.UnitTests/GoymenERP.Documents.UnitTests.csproj",
        "tests/GoymenERP.Finance.UnitTests/GoymenERP.Finance.UnitTests.csproj",
        "tests/GoymenERP.Idempotency.UnitTests/GoymenERP.Idempotency.UnitTests.csproj",
        "tests/GoymenERP.Identity.UnitTests/GoymenERP.Identity.UnitTests.csproj",
        "tests/GoymenERP.Inventory.UnitTests/GoymenERP.Inventory.UnitTests.csproj",
        "tests/GoymenERP.Logistics.UnitTests/GoymenERP.Logistics.UnitTests.csproj",
        "tests/GoymenERP.Manufacturing.UnitTests/GoymenERP.Manufacturing.UnitTests.csproj",
        "tests/GoymenERP.Organization.UnitTests/GoymenERP.Organization.UnitTests.csproj",
        "tests/GoymenERP.Product.UnitTests/GoymenERP.Product.UnitTests.csproj",
        "tests/GoymenERP.Purchasing.UnitTests/GoymenERP.Purchasing.UnitTests.csproj",
        "tests/GoymenERP.Sales.UnitTests/GoymenERP.Sales.UnitTests.csproj",
        "tests/GoymenERP.Settings.UnitTests/GoymenERP.Settings.UnitTests.csproj",
        "tests/GoymenERP.SqlServerRcsiReadiness.Tests/GoymenERP.SqlServerRcsiReadiness.Tests.csproj",
        "tests/GoymenERP.Work.UnitTests/GoymenERP.Work.UnitTests.csproj",
    ),
}
SOLUTION_PROJECT_COUNT = 72
TEST_PROJECT_COUNT = 21
PROOF_SCHEMA = 2
MAX_PROOF_BYTES = 16384
HEAD_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
HASH_PATTERN = re.compile(r"[0-9a-f]{64}\Z")
FQN_PATTERN = re.compile(r"GoymenERP\.[A-Za-z0-9_.+`]+\Z")
EXECUTION_ID_PATTERN = re.compile(
    r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\Z"
)


class Stage(str, Enum):
    BOOTSTRAP_PREFLIGHT = "bootstrapPreflight"
    BUILD = "build"
    DISCOVERY = "discovery"
    TEST = "test"


STAGES = tuple(stage.value for stage in Stage)
T = TypeVar("T")


def digest(value: object) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
    ).hexdigest()


def canonical_head(value: str, label: str) -> str:
    if HEAD_PATTERN.fullmatch(value) is None:
        raise RuntimeError(f"{label} must be a canonical commit id.")
    return value


def _within_repository(root: Path, value: Path, suffix: str) -> Path:
    path = (root / value).resolve()
    if not path.is_relative_to(root) or path.suffix.lower() != suffix:
        raise RuntimeError("Backend CI artifact path is outside its source or has a wrong extension.")
    return path


def catalog() -> dict[str, tuple[str, ...]]:
    projects = [path for group in GROUP_PROJECTS.values() for path in group]
    if (len(projects) != TEST_PROJECT_COUNT or len(set(projects)) != TEST_PROJECT_COUNT
            or sorted(GROUP_PROJECTS) != ["api", "architecture", "modules"]
            or [len(GROUP_PROJECTS[group]) for group in ("architecture", "api", "modules")]
            != [1, 1, 19]):
        raise RuntimeError("Backend groups must partition exactly twenty-one TestSDK projects.")
    return GROUP_PROJECTS


def source_inventory(root: Path) -> tuple[str, str]:
    groups = catalog()
    solution = root / "GoymenERP.slnx"
    try:
        solution_bytes = solution.read_bytes()
        document = ET.fromstring(solution_bytes)
        paths = [element.attrib["Path"].replace("\\", "/") for element in document.iter("Project")]
    except (OSError, ET.ParseError, KeyError) as exception:
        raise RuntimeError("Backend solution inventory is unreadable.") from exception
    if len(paths) != SOLUTION_PROJECT_COUNT or len(set(paths)) != SOLUTION_PROJECT_COUNT:
        raise RuntimeError("Backend solution must retain exactly seventy-two unique projects.")
    test_projects = []
    for value in paths:
        if value.startswith("/") or ".." in Path(value).parts or not value.endswith(".csproj"):
            raise RuntimeError("Backend solution project path is not canonical.")
        project = (root / value).resolve()
        if not project.is_relative_to(root) or not project.is_file() or project.is_symlink():
            raise RuntimeError("Backend solution project is missing or outside source.")
        try:
            xml = ET.parse(project).getroot()
        except (OSError, ET.ParseError) as exception:
            raise RuntimeError("Backend project definition is unreadable.") from exception
        references = [
            element for element in xml.iter("PackageReference")
            if element.attrib.get("Include") == "Microsoft.NET.Test.Sdk"
        ]
        if len(references) > 1:
            raise RuntimeError("Backend TestSDK reference is ambiguous.")
        if references:
            if any(element.text and element.text.strip().lower() == "false"
                   for element in xml.iter("IsTestProject")):
                raise RuntimeError("Backend TestSDK project disables test discovery.")
            test_projects.append(value)
    expected = {path for projects in groups.values() for path in projects}
    if set(test_projects) != expected or len(test_projects) != TEST_PROJECT_COUNT:
        raise RuntimeError("Backend TestSDK source inventory differs from its exact group catalog.")
    return hashlib.sha256(solution_bytes).hexdigest(), digest(groups)


def discovery_map(stdout: str, project: str) -> Counter[str]:
    prefix = Path(project).stem + "."
    names = Counter(
        candidate
        for line in stdout.splitlines()
        if (candidate := line.strip().split("(", 1)[0]).startswith(prefix)
        and FQN_PATTERN.fullmatch(candidate) is not None
    )
    if not names:
        raise RuntimeError("Backend project discovery is empty.")
    return names


def trx_result_map(path: Path, discovered: Counter[str]) -> tuple[dict[str, dict[str, int]], int, int]:
    try:
        document = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exception:
        raise RuntimeError("Backend TRX is missing or malformed.") from exception
    elements = lambda name: [
        element for element in document.iter() if element.tag.rsplit("}", 1)[-1] == name
    ]
    counters = elements("Counters")
    if len(counters) != 1:
        raise RuntimeError("Backend TRX must have exactly one counter element.")
    definitions: dict[str, str] = {}
    for element in elements("UnitTest"):
        test_id = element.attrib.get("id")
        methods = [child for child in element.iter()
                   if child.tag.rsplit("}", 1)[-1] == "TestMethod"]
        if not test_id or test_id in definitions or len(methods) != 1:
            raise RuntimeError("Backend TRX test definition is missing or duplicated.")
        method = methods[0]
        name = f"{method.attrib.get('className', '')}.{method.attrib.get('name', '')}"
        if FQN_PATTERN.fullmatch(name) is None:
            raise RuntimeError("Backend TRX test identity is not canonical.")
        definitions[test_id] = name
    results: dict[str, dict[str, int]] = {}
    total = 0
    passed = 0
    skipped = 0
    execution_ids: set[str] = set()
    for element in elements("UnitTestResult"):
        test_id = element.attrib.get("testId")
        if test_id not in definitions:
            raise RuntimeError("Backend TRX result has no exact test definition.")
        execution_id = element.attrib.get("executionId", "")
        if (EXECUTION_ID_PATTERN.fullmatch(execution_id) is None
                or execution_id.lower() == "00000000-0000-0000-0000-000000000000"):
            raise RuntimeError("Backend TRX execution identity is missing or noncanonical.")
        canonical_execution_id = execution_id.lower()
        if canonical_execution_id in execution_ids:
            raise RuntimeError("Backend TRX repeats an execution identity.")
        execution_ids.add(canonical_execution_id)
        outcome = element.attrib.get("outcome")
        if outcome not in ("Passed", "NotExecuted"):
            raise RuntimeError("Backend TRX contains a failed or unknown test result.")
        entry = results.setdefault(definitions[test_id], {"passed": 0, "skipped": 0})
        entry["passed" if outcome == "Passed" else "skipped"] += 1
        total += 1
        passed += outcome == "Passed"
        skipped += outcome == "NotExecuted"
    if not results or set(results) != set(discovered):
        raise RuntimeError("Backend TRX result identities differ from exact project discovery.")
    if any(sum(results[name].values()) < discovered[name] for name in discovered):
        raise RuntimeError("Backend TRX execution multiplicity is below discovery.")
    expected = {
        # VSTest 17.14.1 writes zero here even for NotExecuted result rows.
        # Skipped executions remain independently counted from the result rows.
        "total": total, "executed": passed, "passed": passed, "notExecuted": 0,
        "failed": 0, "error": 0, "timeout": 0, "aborted": 0, "inconclusive": 0,
    }
    try:
        actual = {key: int(counters[0].attrib[key]) for key in expected}
    except (KeyError, ValueError) as exception:
        raise RuntimeError("Backend TRX counters are incomplete.") from exception
    if actual != expected:
        raise RuntimeError("Backend TRX counters differ from its exact results.")
    return results, passed, skipped


def expected_proof(group: str, source_head: str, tested_commit: str, root: Path) -> dict[str, object]:
    if group not in catalog():
        raise RuntimeError("Unknown backend test group.")
    solution_hash, catalog_hash = source_inventory(root)
    return {
        "schemaVersion": PROOF_SCHEMA,
        "group": group,
        "sourceHead": canonical_head(source_head, "source head"),
        "testedCommit": canonical_head(tested_commit, "tested commit"),
        "solutionHash": solution_hash,
        "catalogHash": catalog_hash,
        "projectCount": len(GROUP_PROJECTS[group]),
        "projects": [],
    }


def _unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for name, value in pairs:
        if name in result:
            raise RuntimeError("Backend group proof repeats a JSON field.")
        result[name] = value
    return result


def _valid_proof(proof: object, expected: dict[str, object], group: str) -> None:
    if not isinstance(proof, dict) or set(proof) != set(expected):
        raise RuntimeError("Backend group proof schema is incomplete or extended.")
    if {key: value for key, value in proof.items() if key != "projects"} != {
        key: value for key, value in expected.items() if key != "projects"
    }:
        raise RuntimeError("Backend group proof does not match source, commit and catalog.")
    rows = proof["projects"]
    if not isinstance(rows, list) or len(rows) != len(GROUP_PROJECTS[group]):
        raise RuntimeError("Backend group proof project count is wrong.")
    for path, row in zip(GROUP_PROJECTS[group], rows, strict=True):
        if not isinstance(row, dict) or set(row) != {
            "path", "discoveryHash", "resultHash", "discoveredMethods",
            "discoveredCases", "executionCount", "passed", "skipped", "stageElapsedMs",
        } or row["path"] != path:
            raise RuntimeError("Backend group proof project identity is wrong.")
        if not isinstance(row["discoveryHash"], str) or not HASH_PATTERN.fullmatch(
            row["discoveryHash"]
        ) or not isinstance(row["resultHash"], str) or not HASH_PATTERN.fullmatch(
            row["resultHash"]
        ):
            raise RuntimeError("Backend group proof has an invalid execution hash.")
        for field in ("discoveredMethods", "discoveredCases", "executionCount", "passed", "skipped"):
            if type(row[field]) is not int or row[field] < 0:
                raise RuntimeError("Backend group proof has an invalid test count.")
        timings = row["stageElapsedMs"]
        if (not isinstance(timings, dict) or set(timings) != set(STAGES)
                or any(type(timings[stage]) is not int or timings[stage] < 0
                       for stage in STAGES)):
            raise RuntimeError("Backend group proof has invalid stage timing.")
        if (row["discoveredMethods"] < 1 or row["discoveredCases"] < row["discoveredMethods"]
                or row["executionCount"] < row["discoveredCases"]
                or row["executionCount"] != row["passed"] + row["skipped"]):
            raise RuntimeError("Backend group proof counts are inconsistent.")


def verify_aggregate(
    root: Path, directory: Path, source_head: str, tested_commit: str,
    build_result: str, worker_result: str,
) -> int:
    if build_result != "success" or worker_result != "success":
        raise RuntimeError("Backend full build and every TestSDK worker must succeed.")
    if directory.is_symlink() or not directory.is_dir():
        raise RuntimeError("Backend group proof directory is missing or linked.")
    expected_names = {f"ci-backend-test-group-proof-{group}" for group in GROUP_PROJECTS}
    if {entry.name for entry in directory.iterdir()} != expected_names:
        raise RuntimeError("Backend group artifacts are missing, duplicated or unknown.")
    covered: set[str] = set()
    total = 0
    for group in GROUP_PROJECTS:
        artifact = directory / f"ci-backend-test-group-proof-{group}"
        if artifact.is_symlink() or not artifact.is_dir() or {
            entry.name for entry in artifact.iterdir()
        } != {f"proof-{group}.json"}:
            raise RuntimeError("Backend group artifact contents are missing or extra.")
        path = artifact / f"proof-{group}.json"
        if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_PROOF_BYTES:
            raise RuntimeError("Backend group proof is missing or oversized.")
        try:
            proof = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_unique_object)
        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exception:
            raise RuntimeError("Backend group proof is unreadable or malformed.") from exception
        _valid_proof(proof, expected_proof(group, source_head, tested_commit, root), group)
        for row in proof["projects"]:
            if row["path"] in covered:
                raise RuntimeError("Backend group proof projects overlap.")
            covered.add(row["path"])
            total += row["executionCount"]
    if covered != {path for projects in catalog().values() for path in projects}:
        raise RuntimeError("Backend group proofs do not cover every TestSDK project.")
    return total


def run_checked(arguments: list[str], root: Path) -> str:
    result = subprocess.run(
        arguments, cwd=root, capture_output=True, text=True, encoding="utf-8",
        errors="replace", check=False, timeout=2400,
    )
    if result.returncode != 0:
        raise RuntimeError(f"Backend project command exited with code {result.returncode}.")
    return result.stdout


def run_bootstrap_preflight(root: Path) -> None:
    run_checked(
        [sys.executable, "-I", "-S", "-B", "tools/validate_dotnet_bootstrap_integrity.py"],
        root,
    )


def run_group(
    root: Path, group: str, results_directory: Path, proof_path: Path,
    source_head: str, tested_commit: str,
) -> int:
    if os.environ.get("GOYMEN_ERP_TEST_SQLSERVER_CONNECTION", "").strip():
        raise RuntimeError("Backend groups cannot use the separate real-SQL acceptance connection.")
    proof = expected_proof(group, source_head, tested_commit, root)
    if proof_path.exists() or (results_directory.exists() and any(results_directory.iterdir())):
        raise RuntimeError("Backend group requires fresh proof and TRX output paths.")
    results_directory.mkdir(parents=True, exist_ok=True)
    for project in GROUP_PROJECTS[group]:
        name = Path(project).stem
        trx = results_directory / f"{name}.trx"
        elapsed_ns = {stage: 0 for stage in Stage}

        def measure(stage: Stage, action: Callable[[], T]) -> T:
            started = time.monotonic_ns()
            result = action()
            duration = time.monotonic_ns() - started
            if duration < 0:
                raise RuntimeError("Backend group stage timing is not monotonic.")
            elapsed_ns[stage] += duration
            return result

        measure(Stage.BOOTSTRAP_PREFLIGHT, lambda: run_bootstrap_preflight(root))
        measure(Stage.BUILD, lambda: run_checked([
            "dotnet", "build", project, "-noAutoResponse", "--configuration", "Release",
            "--no-restore",
        ], root))
        measure(Stage.BOOTSTRAP_PREFLIGHT, lambda: run_bootstrap_preflight(root))
        discovered = discovery_map(measure(Stage.DISCOVERY, lambda: run_checked([
            "dotnet", "test", project, "-noAutoResponse", "--configuration", "Release",
            "--no-restore", "--no-build", "--list-tests",
            "--logger", "console;verbosity=quiet",
        ], root)), project)
        measure(Stage.BOOTSTRAP_PREFLIGHT, lambda: run_bootstrap_preflight(root))
        measure(Stage.TEST, lambda: run_checked([
            "dotnet", "test", project, "-noAutoResponse", "--configuration", "Release",
            "--no-restore", "--no-build", "--results-directory", str(results_directory),
            "--logger", f"trx;LogFileName={trx.name}",
            "--logger", "console;verbosity=quiet",
        ], root))
        if not trx.is_file() or trx.is_symlink():
            raise RuntimeError("Backend project did not produce its exact TRX.")
        results, passed, skipped = trx_result_map(trx, discovered)
        proof["projects"].append({
            "path": project,
            "discoveryHash": digest(sorted(discovered.items())),
            "resultHash": digest(sorted((name, counts["passed"], counts["skipped"])
                                        for name, counts in results.items())),
            "discoveredMethods": len(discovered),
            "discoveredCases": sum(discovered.values()),
            "executionCount": passed + skipped,
            "passed": passed,
            "skipped": skipped,
            "stageElapsedMs": {
                stage.value: elapsed_ns[stage] // 1_000_000 for stage in Stage
            },
        })
    _valid_proof(proof, expected_proof(group, source_head, tested_commit, root), group)
    proof_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = proof_path.with_suffix(".tmp")
    with temporary.open("x", encoding="utf-8") as output:
        json.dump(proof, output, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    temporary.replace(proof_path)
    return sum(row["executionCount"] for row in proof["projects"])


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--group", choices=tuple(GROUP_PROJECTS))
    mode.add_argument("--aggregate", action="store_true")
    parser.add_argument("--results-directory", type=Path)
    parser.add_argument("--proof-output", type=Path)
    parser.add_argument("--proof-directory", type=Path)
    parser.add_argument("--source-head")
    parser.add_argument("--tested-commit")
    parser.add_argument("--build-result")
    parser.add_argument("--worker-result")
    args = parser.parse_args()
    if args.source_head is None or args.tested_commit is None:
        raise RuntimeError("Backend group source and tested commit are mandatory.")
    canonical_head(args.source_head, "source head")
    canonical_head(args.tested_commit, "tested commit")
    if args.group is not None:
        if (args.results_directory is None or args.proof_output is None
                or args.proof_directory is not None or args.build_result is not None
                or args.worker_result is not None):
            raise RuntimeError("Backend group mode requires only its exact output inputs.")
        results_directory = (root / args.results_directory).resolve()
        if not results_directory.is_relative_to(root):
            raise RuntimeError("Backend group results must remain inside source.")
        proof_path = _within_repository(root, args.proof_output, ".json")
        total = run_group(root, args.group, results_directory, proof_path,
                          args.source_head, args.tested_commit)
        print(f"BACKEND_TEST_GROUP: PASS group={args.group} projects={len(GROUP_PROJECTS[args.group])} "
              f"executions={total}")
        return 0
    if (args.proof_directory is None or args.results_directory is not None
            or args.proof_output is not None or args.build_result is None
            or args.worker_result is None):
        raise RuntimeError("Backend aggregate requires only exact proof and worker inputs.")
    directory = (root / args.proof_directory).resolve()
    if not directory.is_relative_to(root):
        raise RuntimeError("Backend group proofs must remain inside source.")
    total = verify_aggregate(
        root, directory, args.source_head, args.tested_commit,
        args.build_result, args.worker_result,
    )
    print(f"BACKEND_TEST_GROUPS: PASS projects={TEST_PROJECT_COUNT} executions={total}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, OSError, subprocess.TimeoutExpired) as exception:
        print(f"BACKEND_TEST_GROUPS: FAIL {exception}", file=sys.stderr)
        raise SystemExit(1) from None
