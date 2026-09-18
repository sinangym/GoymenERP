#!/usr/bin/env python3
"""Fail-closed discovery and TRX gate for the Issue #134 RCSI acceptance slice."""

from __future__ import annotations

import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
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
    "manufacturing",
    "organization",
    "platform",
    "product",
    "sales",
)
TEST_NAMESPACE = "GoymenERP.Api.IntegrationTests."
# A test class belongs to the module its name starts with; the Cari journey class, whose name has no
# module prefix, is Counterparty's; every other class is central (platform).
MODULE_CLASS_PREFIXES = (
    ("Accounting", "accounting"),
    ("Counterparty", "counterparty"),
    ("Finance", "finance"),
    ("Identity", "identity"),
    ("Inventory", "inventory"),
    ("Manufacturing", "manufacturing"),
    ("Organization", "organization"),
    ("Product", "product"),
    ("Sales", "sales"),
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
            "RCSI discovery must contain exactly the ten reviewed tests. "
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


def run_checked(arguments: list[str], repository_root: Path) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        arguments,
        cwd=repository_root,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=1200,
    )
    if result.returncode != 0:
        sys.stdout.write(result.stdout)
        sys.stderr.write(result.stderr)
        raise RuntimeError(
            f"RCSI acceptance command exited with native code {result.returncode}."
        )
    return result


def run_bootstrap_preflight(repository_root: Path) -> None:
    run_checked(
        [
            sys.executable,
            "-I",
            "-S",
            "-B",
            "tools/validate_dotnet_bootstrap_integrity.py",
        ],
        repository_root,
    )


def main() -> int:
    repository_root = Path(__file__).resolve().parent.parent
    if not (repository_root / "GoymenERP.slnx").is_file():
        raise RuntimeError("Göymen ERP repository root could not be resolved.")
    if not os.environ.get("GOYMEN_ERP_TEST_SQLSERVER_CONNECTION", "").strip():
        raise RuntimeError(
            "GOYMEN_ERP_TEST_SQLSERVER_CONNECTION must be injected by the relational runner."
        )

    if sys.argv[1:] == ["--broad"]:
        if BROAD_INVENTORY_ERROR is not None:
            raise BROAD_INVENTORY_ERROR
        filter_value = BROAD_FILTER
        expected_total = BROAD_EXECUTION_COUNT
        verify_discovered = verify_broad_discovery
        gate_name = "SQLSERVER_RELATIONAL_ACCEPTANCE"
    elif sys.argv[1:] == ["--rcsi"]:
        filter_value = RCSI_FILTER
        expected_total = len(EXPECTED_TESTS)
        verify_discovered = verify_discovery
        gate_name = "RCSI_RELATIONAL_ACCEPTANCE"
    else:
        raise RuntimeError("Exactly one mode is required: --broad or --rcsi.")

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
            filter_value,
            "--logger",
            "console;verbosity=quiet",
        ],
        repository_root,
    )
    discovered = verify_discovered(list_result.stdout)
    report: list[str] = []

    with tempfile.TemporaryDirectory(prefix="goymen-rcsi-trx-") as temporary_directory:
        trx_path = Path(temporary_directory) / "rcsi.trx"
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
                temporary_directory,
                "--logger",
                "trx;LogFileName=rcsi.trx",
            ],
            repository_root,
        )
        sys.stdout.write(run_result.stdout)
        sys.stderr.write(run_result.stderr)
        verify_trx(trx_path, expected_total)
        if filter_value == BROAD_FILTER:
            report = verify_broad_trx_results(trx_path, discovered)

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
