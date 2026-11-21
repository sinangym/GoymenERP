#!/usr/bin/env python3
"""Fail-closed discovery and TRX gate for the Issue #134 RCSI acceptance slice."""

from __future__ import annotations

import argparse
import contextlib
from collections import Counter, deque
import hashlib
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import tempfile
import threading
import time
import uuid
import xml.etree.ElementTree as ET


PROJECT = "tests/GoymenERP.Api.IntegrationTests/GoymenERP.Api.IntegrationTests.csproj"
BROAD_FILTER = "FullyQualifiedName~SqlServerRelationalAcceptanceTests"
RCSI_FILTER = (
    "FullyQualifiedName~"
    "GoymenERP.Api.IntegrationTests."
    "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests"
)
PREFIX = (
    "GoymenERP.Api.IntegrationTests."
    "SqlServerRcsiReadinessSqlServerRelationalAcceptanceTests."
)
EXPECTED_TESTS = tuple(
    sorted(
        PREFIX + name
        for name in (
            "CliProcess_OverallTimeoutKillsAndAwaitsTheHungChildWithoutEvidence",
            "CliProcess_ProducesFreshAtomicEvidenceForReadyOffMismatchAndStalePaths",
            "ExpectedDatabaseMismatch_IsRejectedBeforeTheConnectionIsOpened",
            "PowerShellWrapper_MultipleEndpointsReadyAndOffUseBoundApphostAndPropagateExit",
            "PowerShellWrapper_OverallTimeoutKillsChildWithoutEvidenceOrSecretEcho",
            "PowerShellWrapper_InFlightScriptDriftFailsClosedAndDiscardsEvidence",
            "RcsiOffStartupFailsThenExplicitTestAdminEnableAllowsStartupAndProbe",
            "RcsiOff_ProbeFailsClosedAndDoesNotChangeTheDatabase",
            "RcsiOn_ProbeAndIdentityStartupUseTheSameReadyPolicy",
            "UnapprovedLogicalEndpoint_IsRejectedWithoutChangingDatabase",
            "ValidateOnly_BehindAndForwardMigrationHistoriesFailClosedWithoutRepair",
            "ValidateOnly_CompatibleDatabaseIsReady_AndWriteGuardsProveNoInitializationAttempt",
            "ValidateOnly_MissingDatabaseReturnsRedacted503WithoutCreatingIt",
            "ValidateOnly_RcsiOffRefusesReadinessAndBusinessWithoutChangingTheDatabase",
        )
    )
)
# The broad inventory lives in one file per module (tools/relational_inventory/<module>.txt), so
# that two modules adding tests never edit the same lines. Each file has one owner (single writer);
# its guard pin is central. See load_broad_inventory for the grammar and the fail-closed rules.
INVENTORY_DIRECTORY = Path(__file__).resolve().parent / "relational_inventory"
INVENTORY_MODULES = (
    "accounting",
    "counterparty",
    "finance",
    "identity",
    "inventory",
    "logistics",
    "manufacturing",
    "organization",
    "platform",
    "product",
    "purchasing",
    "sales",
    "work",
)
SHARD_MODULES = {
    "A": ("inventory", "manufacturing", "purchasing", "work"),
    "B": ("finance", "accounting", "product", "identity", "organization", "logistics"),
    "C": ("counterparty", "sales", "platform"),
}
TEST_NAMESPACE = "GoymenERP.Api.IntegrationTests."
# A test class belongs to the module its name starts with; the Cari journey class, whose name has no
# module prefix, is Counterparty's; every other class is central (platform).
MODULE_CLASS_PREFIXES = (
    ("Accounting", "accounting"),
    ("Counterparty", "counterparty"),
    ("Finance", "finance"),
    ("Identity", "identity"),
    ("Inventory", "inventory"),
    ("Logistics", "logistics"),
    ("Manufacturing", "manufacturing"),
    ("Organization", "organization"),
    ("Product", "product"),
    ("Purchasing", "purchasing"),
    ("Sales", "sales"),
    ("Work", "work"),
)
CARI_JOURNEY_CLASS = "SqlServerRelationalAcceptanceTests"
INVENTORY_TEST_NAME = re.compile(
    r"GoymenERP\.Api\.IntegrationTests\.[A-Za-z0-9_]*SqlServerRelationalAcceptanceTests"
    r"\.[A-Za-z_][A-Za-z0-9_]*"
)
INVENTORY_LINE = re.compile(r"(?P<name>\S+)(?:  x(?P<count>[0-9]+)  # (?P<reason>\S.*))?")


def module_of(test_name: str) -> str:
    test_class = test_name[len(TEST_NAMESPACE):].split(".", 1)[0]
    if test_class == CARI_JOURNEY_CLASS:
        return "counterparty"
    for prefix, module in MODULE_CLASS_PREFIXES:
        if test_class.startswith(prefix):
            return module
    return "platform"


def load_broad_inventory(directory: Path) -> dict[str, dict[str, int]]:
    """Read every module file: {module: {fully qualified test name: executions}}.

    A line is a comment (`#`), blank, a test name, or `<name>  x<n>  # <reason>` for a test that runs
    n > 1 times (theory rows); the reason is required. Fails closed on an unknown or missing file, a
    file without tests, a malformed line, n < 2 on an x-line, an unsorted or duplicated name, and a
    name whose class belongs to another module's file.
    """
    try:
        present = sorted(entry.name for entry in directory.iterdir())
    except OSError as exception:
        raise RuntimeError(f"Relational inventory directory is unreadable: {exception}") from exception
    expected_files = [f"{module}.txt" for module in INVENTORY_MODULES]
    unknown = sorted(set(present) - set(expected_files))
    missing = sorted(set(expected_files) - set(present))
    if unknown or missing:
        raise RuntimeError(
            "Relational inventory must hold exactly one file per module. "
            f"Unknown {unknown!r}, missing {missing!r}."
        )

    inventory: dict[str, dict[str, int]] = {}
    owner_of: dict[str, str] = {}
    for module in INVENTORY_MODULES:
        path = directory / f"{module}.txt"
        if not path.is_file():
            raise RuntimeError(f"Relational inventory entry {path.name!r} is not a file.")
        try:
            lines = path.read_text(encoding="utf-8").split("\n")
        except (OSError, UnicodeDecodeError) as exception:
            raise RuntimeError(f"Relational inventory {path.name!r} is unreadable: {exception}") from exception
        tests: dict[str, int] = {}
        previous = ""
        for number, raw in enumerate(lines, start=1):
            where = f"{path.name}:{number}"
            if raw != raw.rstrip() or "\t" in raw or "\r" in raw:
                raise RuntimeError(f"Relational inventory line {where} has trailing or tab whitespace.")
            if not raw or raw.startswith("#"):
                continue
            match = INVENTORY_LINE.fullmatch(raw)
            if match is None or INVENTORY_TEST_NAME.fullmatch(match.group("name")) is None:
                raise RuntimeError(
                    f"Relational inventory line {where} is malformed: {raw!r}. Expected a fully "
                    "qualified test name, or `<name>  x<n>  # <reason>`."
                )
            name = match.group("name")
            count = int(match.group("count")) if match.group("count") is not None else 1
            if match.group("count") is not None and count < 2:
                raise RuntimeError(
                    f"Relational inventory line {where} declares x{match.group('count')}; "
                    "only a test that runs n > 1 times carries an x-count."
                )
            if module_of(name) != module:
                raise RuntimeError(
                    f"Relational inventory line {where} names a {module_of(name)} test; "
                    f"it belongs in {module_of(name)}.txt."
                )
            if name in owner_of:
                raise RuntimeError(
                    f"Relational inventory line {where} repeats {name!r} "
                    f"(first in {owner_of[name]}.txt)."
                )
            if name <= previous:
                raise RuntimeError(f"Relational inventory line {where} is out of order; keep each file sorted.")
            previous = name
            owner_of[name] = module
            tests[name] = count
        if not tests:
            raise RuntimeError(f"Relational inventory {path.name!r} names no test.")
        inventory[module] = tests
    return inventory


def _load_checked_in_inventory() -> tuple[dict[str, dict[str, int]], RuntimeError | None]:
    try:
        return load_broad_inventory(INVENTORY_DIRECTORY), None
    except RuntimeError as exception:
        # Reported by main() in --broad mode (fail-closed) instead of a traceback at import.
        return {}, exception


BROAD_INVENTORY, BROAD_INVENTORY_ERROR = _load_checked_in_inventory()
BROAD_EXECUTIONS = {
    name: count for tests in BROAD_INVENTORY.values() for name, count in tests.items()
}
BROAD_EXPECTED_TESTS = tuple(sorted(BROAD_EXECUTIONS))
BROAD_DISCOVERY_COUNT = len(BROAD_EXPECTED_TESTS)
BROAD_EXECUTION_COUNT = sum(BROAD_EXECUTIONS.values())


def verify_discovery(standard_output: str) -> None:
    discovered = tuple(
        sorted(
            line.strip()
            for line in standard_output.splitlines()
            if line.strip().startswith(PREFIX)
        )
    )
    if discovered != EXPECTED_TESTS:
        raise RuntimeError(
            "RCSI discovery must contain exactly the fourteen reviewed tests. "
            f"Expected {EXPECTED_TESTS!r}, found {discovered!r}."
        )


def verify_broad_discovery(standard_output: str) -> tuple[str, ...]:
    discovered = tuple(
        sorted(
            {
                line.strip()
                .split("(", 1)[0]
                for line in standard_output.splitlines()
                if line.strip().startswith("GoymenERP.Api.IntegrationTests.")
                and "SqlServerRelationalAcceptanceTests." in line
            }
        )
    )
    if discovered != tuple(sorted(BROAD_EXPECTED_TESTS)):
        raise RuntimeError(
            "Broad SQL Server relational discovery must retain the exact "
            f"{BROAD_DISCOVERY_COUNT}-test reviewed inventory. "
            f"Expected {tuple(sorted(BROAD_EXPECTED_TESTS))!r}, found {discovered!r}."
        )
    return discovered


def verify_trx(path: Path, expected_total: int) -> None:
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exception:
        raise RuntimeError(f"RCSI TRX could not be parsed: {exception}") from exception

    counters = [
        element
        for element in root.iter()
        if element.tag.rsplit("}", 1)[-1] == "Counters"
    ]
    if len(counters) != 1:
        raise RuntimeError(
            f"RCSI TRX must contain exactly one Counters element; found {len(counters)}."
        )

    expected = {
        "total": expected_total,
        "executed": expected_total,
        "passed": expected_total,
        "failed": 0,
        "error": 0,
        "timeout": 0,
        "aborted": 0,
        "inconclusive": 0,
        "notExecuted": 0,
    }
    actual: dict[str, int] = {}
    for name, expected_value in expected.items():
        raw_value = counters[0].attrib.get(name)
        try:
            actual[name] = int(raw_value) if raw_value is not None else -1
        except ValueError as exception:
            raise RuntimeError(
                f"RCSI TRX counter {name!r} is not an integer: {raw_value!r}."
            ) from exception
        if actual[name] != expected_value:
            raise RuntimeError(
                "SQL Server relational TRX is not an exact all-passed, skipped=0 run. "
                f"Expected {expected!r}, found {actual!r}."
            )


def trx_test_executions(path: Path) -> dict[str, int]:
    """Executions per fully qualified test in a TRX (theory rows fold onto their method); any
    result that is not Passed fails."""
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exception:
        raise RuntimeError(f"RCSI TRX could not be parsed: {exception}") from exception

    executions: dict[str, int] = {}
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] != "UnitTestResult":
            continue
        name = element.attrib.get("testName", "").split("(", 1)[0].strip()
        outcome = element.attrib.get("outcome")
        if outcome != "Passed":
            raise RuntimeError(
                f"SQL Server relational TRX result {name!r} is {outcome!r}, not Passed."
            )
        executions[name] = executions.get(name, 0) + 1
    return executions


def verify_broad_trx_results(path: Path, discovered: tuple[str, ...]) -> list[str]:
    """Every inventory test ran exactly its declared number of times and nothing else ran; returns
    one report line per module."""
    executed = trx_test_executions(path)
    if executed != BROAD_EXECUTIONS:
        differences = sorted(
            f"{name}: expected {BROAD_EXECUTIONS.get(name, 0)}, executed {executed.get(name, 0)}"
            for name in set(executed) | set(BROAD_EXECUTIONS)
            if executed.get(name, 0) != BROAD_EXECUTIONS.get(name, 0)
        )
        raise RuntimeError(
            "SQL Server relational TRX executions must equal the inventory per test. "
            f"Differences: {differences!r}."
        )
    report = []
    for module in INVENTORY_MODULES:
        tests = BROAD_INVENTORY[module]
        report.append(
            f"SQLSERVER_RELATIONAL_ACCEPTANCE module={module} tests={len(tests)} "
            f"discovered={sum(1 for name in discovered if module_of(name) == module)} "
            f"executions={sum(tests.values())} "
            f"executed={sum(executed.get(name, 0) for name in tests)}"
        )
    return report


def shard_executions(shard: str) -> dict[str, int]:
    if BROAD_INVENTORY_ERROR is not None:
        raise BROAD_INVENTORY_ERROR
    if shard not in SHARD_MODULES:
        raise RuntimeError("Unknown relational shard.")
    modules = [module for group in SHARD_MODULES.values() for module in group]
    if len(modules) != len(set(modules)) or set(modules) != set(INVENTORY_MODULES):
        raise RuntimeError("Relational shards must partition every inventory module exactly once.")
    expected = {
        name: count
        for module in SHARD_MODULES[shard]
        for name, count in BROAD_INVENTORY[module].items()
    }
    if not expected:
        raise RuntimeError("Relational shard cannot be empty.")
    return expected


def verify_shard_trx_results(path: Path, shard: str) -> None:
    expected = shard_executions(shard)
    verify_trx(path, sum(expected.values()))
    if trx_test_executions(path) != expected:
        raise RuntimeError("Relational shard TRX must equal its exact FQN and execution inventory.")


def verify_rcsi_trx_results(path: Path) -> None:
    verify_trx(path, len(EXPECTED_TESTS))
    if trx_test_executions(path) != {name: 1 for name in EXPECTED_TESTS}:
        raise RuntimeError("RCSI TRX must contain each of the fourteen reviewed tests exactly once.")


def _map_hash(executions: dict[str, int]) -> str:
    encoded = json.dumps(sorted(executions.items()), separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def _canonical_head(value: str, name: str) -> str:
    if re.fullmatch(r"[0-9a-f]{40}", value) is None:
        raise RuntimeError(f"{name} must be a canonical 40-character commit id.")
    return value


def expected_shard_proof(shard: str, source_head: str, tested_commit: str) -> dict[str, object]:
    expected = shard_executions(shard)
    return {
        "schemaVersion": 1,
        "shard": shard,
        "sourceHead": _canonical_head(source_head, "source head"),
        "testedCommit": _canonical_head(tested_commit, "tested commit"),
        "catalogHash": _map_hash(BROAD_EXECUTIONS),
        "executionMapHash": _map_hash(expected),
        "testCount": len(expected),
        "executionCount": sum(expected.values()),
    }


def _unique_json_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for name, value in pairs:
        if name in result:
            raise RuntimeError("Relational shard proof repeats a JSON field.")
        result[name] = value
    return result


def verify_aggregate_proofs(
    directory: Path,
    source_head: str,
    tested_commit: str,
    worker_result: str,
    rcsi_result: str,
) -> None:
    _canonical_head(source_head, "source head")
    _canonical_head(tested_commit, "tested commit")
    if worker_result != "success" or rcsi_result != "success":
        raise RuntimeError("Every SQL shard and the separate RCSI job must succeed.")
    if BROAD_INVENTORY_ERROR is not None:
        raise BROAD_INVENTORY_ERROR
    expected_names = {f"ci-relational-shard-proof-{shard}" for shard in SHARD_MODULES}
    try:
        actual_names = {entry.name for entry in directory.iterdir()}
    except OSError as exception:
        raise RuntimeError("Relational shard proof directory is unavailable.") from exception
    if actual_names != expected_names:
        raise RuntimeError("Relational shard proofs are missing, duplicated or unknown.")
    covered: dict[str, int] = {}
    for shard in SHARD_MODULES:
        artifact = directory / f"ci-relational-shard-proof-{shard}"
        try:
            contents = {entry.name for entry in artifact.iterdir()} if artifact.is_dir() else set()
        except OSError as exception:
            raise RuntimeError("Relational shard artifact is unavailable.") from exception
        if contents != {f"proof-{shard}.json"}:
            raise RuntimeError("Relational shard artifact is missing, duplicated or malformed.")
        path = artifact / f"proof-{shard}.json"
        try:
            if not path.is_file() or path.stat().st_size > 4096:
                raise RuntimeError("Relational shard proof is missing or oversized.")
            proof = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_unique_json_object)
        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exception:
            raise RuntimeError("Relational shard proof is unreadable or malformed.") from exception
        if proof != expected_shard_proof(shard, source_head, tested_commit):
            raise RuntimeError("Relational shard proof does not match the exact source and inventory.")
        for name, count in shard_executions(shard).items():
            if name in covered:
                raise RuntimeError("Relational shard proofs overlap.")
            covered[name] = count
    if covered != BROAD_EXECUTIONS:
        raise RuntimeError("Relational shard proofs do not cover the complete broad SQL inventory.")


def resolve_proof_path(repository_root: Path, requested: Path) -> Path:
    path = (repository_root / requested).resolve()
    if not path.is_relative_to(repository_root) or path.suffix.lower() != ".json":
        raise RuntimeError("Relational proof must be a JSON file inside the repository.")
    return path


def resolve_trx_path(
    repository_root: Path,
    requested_output: Path | None,
    temporary_directory: Path,
) -> Path:
    if requested_output is None:
        return temporary_directory / "rcsi.trx"
    trx_path = (repository_root / requested_output).resolve()
    if not trx_path.is_relative_to(repository_root) or trx_path.suffix.lower() != ".trx":
        raise RuntimeError("TRX output must be a .trx file inside the repository.")
    return trx_path


def trx_logger(trx_path: Path) -> str:
    return f"trx;LogFileName={trx_path.name}"


TEST_EXECUTION_TIMEOUT_SECONDS = 2400
# User decision 2026-11-18: one broad-shard budget; each native process remains capped at 2400s.
SQL_SHARD_EXECUTION_TIMEOUT_SECONDS = 10800
MAX_PROGRESS_LINE_BYTES = 8192
MAX_PROGRESS_EVENTS = 4096
PROGRESS_FQN = r"GoymenERP\.Api\.IntegrationTests\.[A-Za-z0-9_]+\.[A-Za-z0-9_]+"
RESULT_PROGRESS = re.compile(
    rf"^\s*(Passed|Failed|Skipped) ({PROGRESS_FQN})(?:\(.*\))? "
    r"\[((?:< )?\d+(?:\.\d+)? (?:ms|s|m|h)(?: \d+(?:\.\d+)? (?:ms|s|m|h))*)\]\s*$"
)
LONG_RUNNING_PROGRESS = re.compile(
    rf"^\s*(?:\[xUnit\.net [0-9:.]+\]\s*)?(?:GoymenERP\.Api\.IntegrationTests:\s*)?"
    rf"\[Long Running Test\] '({PROGRESS_FQN})(?:\(.*\))?', Elapsed: (\d{{2}}:[0-5]\d:[0-5]\d)\s*$"
)


class TestProgress:
    """Bounded, lossy diagnostic projection, never an input to the acceptance gate."""

    def __init__(self) -> None:
        self.pending = bytearray()
        self.discard_line = False
        self.events = 0
        self.allowed = frozenset(BROAD_EXPECTED_TESTS) | frozenset(EXPECTED_TESTS)

    def feed(self, chunk: bytes, *, final: bool = False) -> None:
        parts = chunk.split(b"\n")
        for index, part in enumerate(parts):
            if len(self.pending) + len(part) > MAX_PROGRESS_LINE_BYTES:
                self.pending.clear()
                self.discard_line = True
            if not self.discard_line:
                self.pending.extend(part)
            if index < len(parts) - 1 or final:
                if not self.discard_line:
                    self.report(self.pending.decode("utf-8", errors="replace"))
                self.pending.clear()
                self.discard_line = False

    def report(self, line: str) -> None:
        if self.events >= MAX_PROGRESS_EVENTS:
            return
        match = RESULT_PROGRESS.fullmatch(line)
        if match:
            outcome, name, duration = match.groups()
            seconds = sum(
                float(value) * {"ms": .001, "s": 1, "m": 60, "h": 3600}[unit]
                for value, unit in re.findall(r"(\d+(?:\.\d+)?) (ms|s|m|h)", duration)
            )
        else:
            match = LONG_RUNNING_PROGRESS.fullmatch(line)
            if not match:
                return
            name, duration = match.groups()
            hours, minutes, seconds_part = map(int, duration.split(":"))
            seconds = hours * 3600 + minutes * 60 + seconds_part
            outcome = "long-running"
        if name in self.allowed and math.isfinite(seconds):
            # Display names may contain theory arguments. Only the checked-in method name escapes.
            print(f"SQL-DIAGNOSTIC phase=test-execution fqn={name} "
                  f"outcome={outcome} duration_seconds={seconds:.3f}", flush=True)
            self.events += 1


def stop_test_process(process: subprocess.Popen[bytes]) -> None:
    """Kill the POSIX group (Windows tree best effort), then reap the direct child silently."""
    try:
        if os.name == "nt":
            subprocess.run(
                ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                check=False, timeout=10,
            )
        else:
            os.killpg(process.pid, signal.SIGKILL)
    except (OSError, subprocess.SubprocessError):
        pass
    try:
        process.kill()
    except OSError:
        pass
    process.wait(timeout=10)


class NativeTestFailure(RuntimeError):
    """The actual test process failed; its exit code is never inferred from text."""

    def __init__(self, exit_code: int) -> None:
        self.exit_code = exit_code
        super().__init__(f"RCSI acceptance command exited with native code {exit_code}.")


def run_test_checked(
    arguments: list[str], repository_root: Path, *, execution_deadline: float | None = None,
) -> subprocess.CompletedProcess[str]:
    # Cap this process and its pipe completion by the remaining shard budget; progress never renews either.
    started = time.monotonic()
    deadline = started + TEST_EXECUTION_TIMEOUT_SECONDS
    if execution_deadline is not None:
        deadline = min(deadline, execution_deadline)
        if deadline <= started:
            raise RuntimeError("SQL Server relational execution exhausted its global deadline.")
    deadline_kind = (
        "global" if execution_deadline is not None and execution_deadline <= started + TEST_EXECUTION_TIMEOUT_SECONDS
        else "process"
    )
    effective_budget_seconds = deadline - started
    progress = TestProgress()
    reader_errors: list[type[BaseException]] = []
    print("SQL-DIAGNOSTIC phase=test-execution outcome=started duration_seconds=0.000", flush=True)
    process = subprocess.Popen(
        arguments, cwd=repository_root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        bufsize=0, start_new_session=os.name != "nt",
    )
    assert process.stdout is not None

    def read_output() -> None:
        try:
            while chunk := process.stdout.read(4096):
                progress.feed(chunk)
            progress.feed(b"", final=True)
        except Exception as exception:
            reader_errors.append(type(exception))

    reader = threading.Thread(target=read_output, daemon=True)
    reader_started = False
    try:
        reader.start()
        reader_started = True
        returncode = process.wait(timeout=max(0, deadline - time.monotonic()))
        if returncode != 0:
            # A failed runner may leave a descendant holding the pipe. Keep its native failure;
            # do not replace it with an output-drain timeout.
            raise NativeTestFailure(returncode)
        reader.join(timeout=max(0, deadline - time.monotonic()))
        if reader.is_alive():
            raise subprocess.TimeoutExpired(arguments, effective_budget_seconds)
        if reader_errors:
            raise RuntimeError("RCSI acceptance test output could not be read.")
    except BaseException as exception:
        # Cleanup must not replace the timeout/native error that caused acceptance to fail.
        try:
            stop_test_process(process)
        except (OSError, subprocess.SubprocessError):
            pass
        if reader_started:
            reader.join(timeout=10)
        if isinstance(exception, subprocess.TimeoutExpired):
            print(f"SQL-DIAGNOSTIC phase=test-execution outcome=timeout "
                  f"duration_seconds={time.monotonic() - started:.3f} "
                  f"deadline={deadline_kind} effective_budget_seconds={effective_budget_seconds:.3f}", flush=True)
            raise RuntimeError(
                f"SQL Server relational test execution timed out: deadline={deadline_kind} "
                f"effective_budget_seconds={effective_budget_seconds:.3f}."
            ) from exception
        print(f"SQL-DIAGNOSTIC phase=test-execution outcome=failed "
              f"duration_seconds={time.monotonic() - started:.3f}", flush=True)
        raise
    finally:
        if not reader.is_alive():
            process.stdout.close()
    print(f"SQL-DIAGNOSTIC phase=test-execution outcome=completed "
          f"duration_seconds={time.monotonic() - started:.3f}", flush=True)
    # Raw test output is deliberately not replayed: live diagnostics are already safely projected.
    return subprocess.CompletedProcess(arguments, returncode, stdout="", stderr="")


def run_checked(
    arguments: list[str], repository_root: Path, *, execution_deadline: float | None = None,
) -> subprocess.CompletedProcess[str]:
    if "--no-build" in arguments and "--results-directory" in arguments:
        if execution_deadline is None:
            return run_test_checked(arguments, repository_root)
        return run_test_checked(arguments, repository_root, execution_deadline=execution_deadline)
    remaining = 2400 if execution_deadline is None else execution_deadline - time.monotonic()
    if remaining <= 0:
        raise RuntimeError("SQL Server relational execution exhausted its global deadline.")
    result = subprocess.run(
        arguments,
        cwd=repository_root,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        # The broad suite grew past 20 minutes (17m22s at #512, 19m19s at #513, a timeout at #514). Only the
        # wall-clock limit changes; every listed test and the 0-skip / exact-inventory rules stay (user decision
        # 2026-09-29).
        timeout=min(2400, remaining),
    )
    if result.returncode != 0:
        sys.stdout.write(result.stdout)
        sys.stderr.write(result.stderr)
        raise RuntimeError(
            f"RCSI acceptance command exited with native code {result.returncode}."
        )
    return result


def run_bootstrap_preflight(repository_root: Path, *, execution_deadline: float | None = None) -> None:
    run_checked(
        [
            sys.executable,
            "-I",
            "-S",
            "-B",
            "tools/validate_dotnet_bootstrap_integrity.py",
        ],
        repository_root,
        **({"execution_deadline": execution_deadline} if execution_deadline is not None else {}),
    )


# Keep every theory's executions in one process. Splitting methods changes process/static
# lifetime: this diagnostic-oriented runner change remains subject to independent K3 review.
# Round-robin class queues retain xUnit's opportunity to run independent classes together.
# The goal is earlier completed TRX diagnostics, not a promise of a green or faster SQL run.
SQL_BATCH_MAX_METHODS = 8
SQL_BATCH_MAX_EXECUTIONS = 16


def plan_sql_batches(expected: dict[str, int]) -> tuple[dict[str, int], ...]:
    if not expected or any(
        not INVENTORY_TEST_NAME.fullmatch(name)
        or type(count) is not int or count <= 0
        for name, count in expected.items()
    ):
        raise RuntimeError("SQL batches require a nonempty canonical relational execution inventory.")
    queues: dict[str, deque[tuple[str, int]]] = {}
    for name, count in sorted(expected.items()):
        queues.setdefault(name.rsplit(".", 1)[0], deque()).append((name, count))
    ordered: list[tuple[str, int]] = []
    while queues:
        # Each pass selects at most one method from every remaining class. Never start
        # concurrent runner processes or split a theory to create this opportunity.
        for class_name in sorted(queues):
            ordered.append(queues[class_name].popleft())
            if not queues[class_name]:
                del queues[class_name]
    batches: list[dict[str, int]] = []
    current: dict[str, int] = {}
    for name, count in ordered:
        if current and (len(current) >= SQL_BATCH_MAX_METHODS
                        or sum(current.values()) + count > SQL_BATCH_MAX_EXECUTIONS):
            batches.append(current)
            current = {}
        current[name] = count
    if current:
        batches.append(current)
    union = {name: count for batch in batches for name, count in batch.items()}
    if union != expected or sum(len(batch) for batch in batches) != len(expected):
        raise RuntimeError("SQL batch plan must partition the exact execution inventory.")
    return tuple(batches)


def verify_sql_batch_trx(
    path: Path, expected: dict[str, int], seen_execution_ids: set[str], *,
    diagnostic_only: bool = False,
) -> dict[str, int]:
    if path.is_symlink() or not path.is_file():
        raise RuntimeError("SQL batch TRX must be a normal output file, not a symlink.")
    if not diagnostic_only:
        verify_trx(path, sum(expected.values()))
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exception:
        raise RuntimeError("SQL batch TRX could not be parsed.") from exception
    failed_results = 0
    definitions: dict[str, str] = {}
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] != "UnitTest":
            continue
        test_id = element.attrib.get("id", "")
        methods = [node for node in element.iter() if node.tag.rsplit("}", 1)[-1] == "TestMethod"]
        if not test_id or len(test_id) > 128 or test_id in definitions or len(methods) != 1:
            raise RuntimeError("SQL batch TRX must contain unique mapped test definitions.")
        method = methods[0]
        name = method.attrib.get("className", "") + "." + method.attrib.get("name", "")
        if name not in expected:
            raise RuntimeError("SQL batch TRX definition is outside its exact selected methods.")
        definitions[test_id] = name
    executed: Counter[str] = Counter()
    batch_ids: set[str] = set()
    used_test_ids: set[str] = set()
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] != "UnitTestResult":
            continue
        test_id = element.attrib.get("testId", "")
        execution_id = element.attrib.get("executionId", "")
        if test_id not in definitions:
            raise RuntimeError("SQL batch TRX result lacks an exact test definition identity.")
        try:
            identity = uuid.UUID(execution_id)
        except (ValueError, AttributeError) as exception:
            raise RuntimeError("SQL batch TRX execution identities must be canonical nonzero UUIDs.") from exception
        canonical_id = str(identity)
        if not identity.int or execution_id.lower() != canonical_id:
            raise RuntimeError("SQL batch TRX execution identities must be canonical nonzero UUIDs.")
        if canonical_id in batch_ids or canonical_id in seen_execution_ids:
            raise RuntimeError("SQL batch TRX execution identities must be unique.")
        outcome = element.attrib.get("outcome")
        if diagnostic_only and outcome == "Failed":
            failed_results += 1
        elif outcome != "Passed":
            raise RuntimeError("SQL batch TRX requires passed results or explicit diagnostic-only test failures; skipped=0.")
        batch_ids.add(canonical_id)
        used_test_ids.add(test_id)
        executed[definitions[test_id]] += 1
    if used_test_ids != set(definitions):
        raise RuntimeError("SQL batch TRX must use every selected test definition identity.")
    if dict(executed) != expected:
        raise RuntimeError("SQL batch TRX execution multiplicity must equal its exact selected inventory.")
    if diagnostic_only:
        # Native exit 1 alone proves neither complete test execution nor test-only failure.
        # This branch can retain diagnostic coverage, but can never certify acceptance.
        summaries = [element for element in root.iter() if element.tag.rsplit("}", 1)[-1] == "ResultSummary"]
        counters = [element for element in root.iter() if element.tag.rsplit("}", 1)[-1] == "Counters"]
        if (not failed_results or len(summaries) != 1
                or summaries[0].attrib.get("outcome") != "Failed" or len(counters) != 1):
            raise RuntimeError("Diagnostic SQL TRX requires one failed summary and complete failed test results.")
        total = sum(expected.values())
        required = {"total": total, "executed": total, "passed": total - failed_results,
                    "failed": failed_results, "error": 0, "timeout": 0, "aborted": 0,
                    "inconclusive": 0, "notExecuted": 0}
        try:
            actual = {name: int(counters[0].attrib.get(name, "")) for name in required}
        except ValueError as exception:
            raise RuntimeError("Diagnostic SQL TRX counters must be exact integers.") from exception
        if actual != required:
            raise RuntimeError("Diagnostic SQL TRX counters must equal its complete exact execution results.")
        try:
            other_counters = [int(value) for name, value in counters[0].attrib.items()
                              if name not in required]
        except ValueError as exception:
            raise RuntimeError("Diagnostic SQL TRX additional counters must be integer zero.") from exception
        if any(other_counters):
            raise RuntimeError("Diagnostic SQL TRX additional counters must be integer zero.")
        # RunInfo metadata cannot certify a run, and does not veto diagnostic coverage
        # once native 1 and every exact failed/passed result have been validated above.
        # Even RunInfo errors leave the first native failure sticky: no acceptance proof
        # can be produced after this branch. Malformed/incomplete results still fail fast.
    seen_execution_ids.update(batch_ids)
    return dict(executed)


def run_sql_batches(
    repository_root: Path, trx_path: Path, expected: dict[str, int],
) -> dict[str, int]:
    batches = plan_sql_batches(expected)
    paths = tuple(trx_path.with_name(f"{trx_path.stem}.batch-{index:04d}.trx")
                  for index in range(1, len(batches) + 1))
    if (any(path.exists() or path.is_symlink() for path in paths)
            or any(trx_path.parent.glob(f"{trx_path.stem}.batch-*.trx"))):
        raise RuntimeError("SQL batch TRX outputs must be fresh; existing evidence cannot be reused.")
    deadline = time.monotonic() + SQL_SHARD_EXECUTION_TIMEOUT_SECONDS
    executed: dict[str, int] = {}
    seen_execution_ids: set[str] = set()
    first_test_failure: NativeTestFailure | None = None
    for batch, path in zip(batches, paths, strict=True):
        if time.monotonic() >= deadline:
            raise RuntimeError("SQL Server relational execution exhausted its global deadline.")
        run_bootstrap_preflight(repository_root, execution_deadline=deadline)
        # A failed native process still leaves its completed TRX for the always-running
        # privacy-safe summary step. Do not delete it or convert the failure into a proof.
        try:
            run_checked([
                "dotnet", "test", PROJECT, "-noAutoResponse", "--configuration", "Release",
                "--no-build", "--no-restore", "--filter",
                "|".join(f"FullyQualifiedName={name}" for name in batch),
                "--results-directory", str(path.parent), "--logger", trx_logger(path),
                "--logger", "console;verbosity=detailed", "--",
                "xUnit.DiagnosticMessages=true", "xUnit.LongRunningTestSeconds=60",
            ], repository_root, execution_deadline=deadline)
        except NativeTestFailure as failure:
            if failure.exit_code != 1:
                raise
            actual = verify_sql_batch_trx(path, batch, seen_execution_ids, diagnostic_only=True)
            # Keep the first real failure sticky. Further batches run once, solely for
            # diagnostics under the same deadline; no retry, exclusion or failure proof.
            if first_test_failure is None:
                first_test_failure = failure
        else:
            actual = verify_sql_batch_trx(path, batch, seen_execution_ids)
        if set(executed) & set(actual):
            raise RuntimeError("SQL batch methods must not overlap.")
        executed.update(actual)
        if time.monotonic() >= deadline:
            raise RuntimeError("SQL Server relational execution exhausted its global deadline.")
    if executed != expected:
        raise RuntimeError("SQL batches must retain the exact complete execution inventory.")
    if first_test_failure is not None:
        raise first_test_failure
    return executed


def main() -> int:
    repository_root = Path(__file__).resolve().parent.parent
    if not (repository_root / "GoymenERP.slnx").is_file():
        raise RuntimeError("Göymen ERP repository root could not be resolved.")

    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--broad", action="store_true")
    mode.add_argument("--rcsi", action="store_true")
    mode.add_argument("--shard", choices=tuple(SHARD_MODULES))
    mode.add_argument("--aggregate", action="store_true")
    parser.add_argument("--trx-output", type=Path)
    parser.add_argument("--proof-output", type=Path)
    parser.add_argument("--proof-directory", type=Path)
    parser.add_argument("--source-head")
    parser.add_argument("--tested-commit")
    parser.add_argument("--worker-result")
    parser.add_argument("--rcsi-result")
    args = parser.parse_args()

    if args.aggregate:
        if (args.trx_output is not None or args.proof_output is not None
                or args.proof_directory is None or args.source_head is None
                or args.tested_commit is None or args.worker_result is None
                or args.rcsi_result is None):
            raise RuntimeError("Aggregate mode requires only the exact proof and worker inputs.")
        proof_directory = (repository_root / args.proof_directory).resolve()
        if not proof_directory.is_relative_to(repository_root):
            raise RuntimeError("Relational proofs must stay inside the repository.")
        verify_aggregate_proofs(
            proof_directory, args.source_head, args.tested_commit,
            args.worker_result, args.rcsi_result,
        )
        print(
            f"SQLSERVER_RELATIONAL_ACCEPTANCE: PASS total={BROAD_EXECUTION_COUNT} "
            f"passed={BROAD_EXECUTION_COUNT} failed=0 skipped=0"
        )
        return 0

    if not os.environ.get("GOYMEN_ERP_TEST_SQLSERVER_CONNECTION", "").strip():
        raise RuntimeError(
            "GOYMEN_ERP_TEST_SQLSERVER_CONNECTION must be injected by the relational runner."
        )
    if args.shard:
        if (args.proof_output is None or args.source_head is None or args.tested_commit is None
                or args.proof_directory is not None or args.worker_result is not None
                or args.rcsi_result is not None):
            raise RuntimeError("Shard mode requires an exact source-bound proof output.")
        expected_shard_proof(args.shard, args.source_head, args.tested_commit)
    elif any(value is not None for value in (
        args.proof_output, args.proof_directory, args.source_head, args.tested_commit,
        args.worker_result, args.rcsi_result,
    )):
        raise RuntimeError("Broad and RCSI modes cannot accept shard proof inputs.")

    if args.broad:
        if BROAD_INVENTORY_ERROR is not None:
            raise BROAD_INVENTORY_ERROR
        filter_value = BROAD_FILTER
        expected_total = BROAD_EXECUTION_COUNT
        verify_discovered = verify_broad_discovery
        gate_name = "SQLSERVER_RELATIONAL_ACCEPTANCE"
    elif args.rcsi:
        filter_value = RCSI_FILTER
        expected_total = len(EXPECTED_TESTS)
        verify_discovered = verify_discovery
        gate_name = "RCSI_RELATIONAL_ACCEPTANCE"
    else:
        expected = shard_executions(args.shard)
        filter_value = "|".join(f"FullyQualifiedName={name}" for name in sorted(expected))
        expected_total = sum(expected.values())
        verify_discovered = verify_broad_discovery
        gate_name = f"SQLSERVER_RELATIONAL_SHARD_{args.shard}"
    run_bootstrap_preflight(repository_root)
    list_result = run_checked(
        [
            "dotnet",
            "test",
            PROJECT,
            "-noAutoResponse",
            "--configuration",
            "Release",
            "--no-restore",
            "--list-tests",
            "--filter",
            BROAD_FILTER if args.shard else filter_value,
            "--logger",
            "console;verbosity=quiet",
        ],
        repository_root,
    )
    discovered = verify_discovered(list_result.stdout)
    report: list[str] = []

    with contextlib.ExitStack() as stack:
        if args.trx_output is None:
            results_directory = Path(
                stack.enter_context(
                    tempfile.TemporaryDirectory(prefix="goymen-rcsi-trx-")
                )
            )
        else:
            results_directory = repository_root
        trx_path = resolve_trx_path(
            repository_root, args.trx_output, results_directory
        )
        proof_path = resolve_proof_path(repository_root, args.proof_output) if args.shard else None
        if proof_path is not None:
            proof_path.parent.mkdir(parents=True, exist_ok=True)
            proof_path.unlink(missing_ok=True)
        if args.trx_output is not None:
            results_directory = trx_path.parent
            results_directory.mkdir(parents=True, exist_ok=True)
            trx_path.unlink(missing_ok=True)
        if args.rcsi:
            run_bootstrap_preflight(repository_root)
            run_result = run_checked(
                [
                    "dotnet",
                    "test",
                    PROJECT,
                    "-noAutoResponse",
                    "--configuration",
                    "Release",
                    "--no-build",
                    "--no-restore",
                    "--filter",
                    filter_value,
                    "--results-directory",
                    str(results_directory),
                    "--logger",
                    trx_logger(trx_path),
                    "--logger",
                    "console;verbosity=detailed",
                    "--",
                    "xUnit.DiagnosticMessages=true",
                    "xUnit.LongRunningTestSeconds=60",
                ],
                repository_root,
            )
            sys.stdout.write(run_result.stdout)
            sys.stderr.write(run_result.stderr)
            verify_rcsi_trx_results(trx_path)
        else:
            expected = BROAD_EXECUTIONS if args.broad else shard_executions(args.shard)
            executed = run_sql_batches(repository_root, trx_path, expected)
            if args.broad:
                for module in INVENTORY_MODULES:
                    tests = BROAD_INVENTORY[module]
                    report.append(
                        f"SQLSERVER_RELATIONAL_ACCEPTANCE module={module} tests={len(tests)} "
                        f"discovered={sum(1 for name in discovered if module_of(name) == module)} "
                        f"executions={sum(tests.values())} "
                        f"executed={sum(executed.get(name, 0) for name in tests)}"
                    )
            else:
                assert proof_path is not None
                proof = expected_shard_proof(args.shard, args.source_head, args.tested_commit)
                temporary_proof = proof_path.with_suffix(".tmp")
                temporary_proof.write_text(
                    json.dumps(proof, sort_keys=True, separators=(",", ":")) + "\n",
                    encoding="utf-8",
                )
                temporary_proof.replace(proof_path)

    for line in report:
        print(line)
    print(
        f"{gate_name}: PASS total={expected_total} passed={expected_total} "
        "failed=0 skipped=0"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, subprocess.TimeoutExpired) as exception:
        print(f"RCSI_RELATIONAL_ACCEPTANCE: FAIL {exception}", file=sys.stderr)
        raise SystemExit(1) from exception
