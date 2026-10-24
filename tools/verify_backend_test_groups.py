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
import uuid
from typing import Callable, TypeVar
from types import ModuleType
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
SOLUTION_PROJECT_COUNT = 71
TEST_PROJECT_COUNT = 20
PROOF_SCHEMA = 4
MAX_PROOF_BYTES = 2 * 1024 * 1024
# Release per-process memory between API batches on the bounded local runner.
# Keep complete classes together; a large class remains one indivisible batch.
API_BATCH_METHOD_BUDGET = 32
HEAD_PATTERN = re.compile(r"[0-9a-f]{40}\Z")
HASH_PATTERN = re.compile(r"[0-9a-f]{64}\Z")
FQN_PATTERN = re.compile(r"GoymenERP\.[A-Za-z0-9_.+`]+\Z")
EXECUTION_ID_PATTERN = re.compile(
    r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\Z"
)

# Only these two reviewed nonserializable MemberData methods defer their rows
# until execution. Exact display identities and source bytes are reviewed together.
# An unknown deferred theory must fail closed, rather than permit a lower bound.
DEFERRED_THEORIES: dict[str, tuple[str, str, tuple[str, ...]]] = {'GoymenERP.Manufacturing.UnitTests.ManufacturingCommercialContextSnapshotTests.One_plan_refuses_a_different_identity_or_version': ('tests/GoymenERP.Manufacturing.UnitTests/ManufacturingCommercialContextSnapshotTests.cs',
                                                                                                                                    'd0a327ac6a0daadccf2a421d80354acc461a1806b2bda57677b18ec836bec450',
                                                                                                                                    ('GoymenERP.Manufacturing.UnitTests.ManufacturingCommercialContextSnapshotTests.One_plan_refuses_a_different_identity_or_version(differentContext: '
                                                                                                                                     'PlanCommercialContextSnapshotV1 '
                                                                                                                                     '{ '
                                                                                                                                     'PartyId '
                                                                                                                                     '= '
                                                                                                                                     '30000000-0000-0000-0000-000000000003, '
                                                                                                                                     'CompanyPartyAccountId '
                                                                                                                                     '= '
                                                                                                                                     '40000000-0000-0000-0000-000000000004, '
                                                                                                                                     'PartyVersion '
                                                                                                                                     '= '
                                                                                                                                     'party-v1, '
                                                                                                                                     'AccountVersion '
                                                                                                                                     '= '
                                                                                                                                     'account-v2 '
                                                                                                                                     '})',
                                                                                                                                     'GoymenERP.Manufacturing.UnitTests.ManufacturingCommercialContextSnapshotTests.One_plan_refuses_a_different_identity_or_version(differentContext: '
                                                                                                                                     'PlanCommercialContextSnapshotV1 '
                                                                                                                                     '{ '
                                                                                                                                     'PartyId '
                                                                                                                                     '= '
                                                                                                                                     '30000000-0000-0000-0000-000000000003, '
                                                                                                                                     'CompanyPartyAccountId '
                                                                                                                                     '= '
                                                                                                                                     '40000000-0000-0000-0000-000000000004, '
                                                                                                                                     'PartyVersion '
                                                                                                                                     '= '
                                                                                                                                     'party-v2, '
                                                                                                                                     'AccountVersion '
                                                                                                                                     '= '
                                                                                                                                     'account-v1 '
                                                                                                                                     '})',
                                                                                                                                     'GoymenERP.Manufacturing.UnitTests.ManufacturingCommercialContextSnapshotTests.One_plan_refuses_a_different_identity_or_version(differentContext: '
                                                                                                                                     'PlanCommercialContextSnapshotV1 '
                                                                                                                                     '{ '
                                                                                                                                     'PartyId '
                                                                                                                                     '= '
                                                                                                                                     '30000000-0000-0000-0000-000000000003, '
                                                                                                                                     'CompanyPartyAccountId '
                                                                                                                                     '= '
                                                                                                                                     '40000000-0000-0000-0000-000000000099, '
                                                                                                                                     'PartyVersion '
                                                                                                                                     '= '
                                                                                                                                     'party-v1, '
                                                                                                                                     'AccountVersion '
                                                                                                                                     '= '
                                                                                                                                     'account-v1 '
                                                                                                                                     '})',
                                                                                                                                     'GoymenERP.Manufacturing.UnitTests.ManufacturingCommercialContextSnapshotTests.One_plan_refuses_a_different_identity_or_version(differentContext: '
                                                                                                                                     'PlanCommercialContextSnapshotV1 '
                                                                                                                                     '{ '
                                                                                                                                     'PartyId '
                                                                                                                                     '= '
                                                                                                                                     '30000000-0000-0000-0000-000000000099, '
                                                                                                                                     'CompanyPartyAccountId '
                                                                                                                                     '= '
                                                                                                                                     '40000000-0000-0000-0000-000000000004, '
                                                                                                                                     'PartyVersion '
                                                                                                                                     '= '
                                                                                                                                     'party-v1, '
                                                                                                                                     'AccountVersion '
                                                                                                                                     '= '
                                                                                                                                     'account-v1 '
                                                                                                                                     '})')),
 'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping': ('tests/GoymenERP.Organization.UnitTests/WorkCenterWorkingTimeAvailabilityContractTests.cs',
                                                                                                                                                       'd122f06addfda39be9892bbfc787eb0b5785b712659b5311a50d307bb5c6ce5e',
                                                                                                                                                       ('GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/05/2026 '
                                                                                                                                                        '23:59:59 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '01:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}])',
                                                                                                                                                        'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '00:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/07/2026 '
                                                                                                                                                        '03:00:00 '
                                                                                                                                                        '+03:00 '
                                                                                                                                                        '}])',
                                                                                                                                                        'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '01:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/07/2026 '
                                                                                                                                                        '00:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}])',
                                                                                                                                                        'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '02:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '02:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}])',
                                                                                                                                                        'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '03:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '02:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}])',
                                                                                                                                                        'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '03:00:00 '
                                                                                                                                                        '+03:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '01:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}])',
                                                                                                                                                        'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '06:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '10:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}, '
                                                                                                                                                        'WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '06:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '10:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}])',
                                                                                                                                                        'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '06:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '10:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}, '
                                                                                                                                                        'WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '09:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '12:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}])',
                                                                                                                                                        'GoymenERP.Organization.UnitTests.WorkCenterWorkingTimeAvailabilityContractTests.Intervals_must_be_utc_positive_bounded_ordered_and_nonoverlapping(intervals: '
                                                                                                                                                        '[WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '12:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '14:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}, '
                                                                                                                                                        'WorkCenterWorkingTimeIntervalV1 '
                                                                                                                                                        '{ '
                                                                                                                                                        'StartsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '06:00:00 '
                                                                                                                                                        '+00:00, '
                                                                                                                                                        'EndsAtUtc '
                                                                                                                                                        '= '
                                                                                                                                                        '10/06/2026 '
                                                                                                                                                        '10:00:00 '
                                                                                                                                                        '+00:00 '
                                                                                                                                                        '}])'))}


def relational_verifier(root: Path) -> ModuleType:
    path = root / "tools/verify_rcsi_relational_acceptance.py"
    if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(root):
        raise RuntimeError("Separate SQL verifier source is missing or linked.")
    # Execute the reviewed source bytes, never a timestamp-valid ignored .pyc.
    module = ModuleType("backend_separate_sql_gate")
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), "exec"), module.__dict__)
    if module.BROAD_INVENTORY_ERROR is not None:
        raise module.BROAD_INVENTORY_ERROR
    return module


def relational_cases(root: Path) -> dict[str, int]:
    verifier = relational_verifier(root)
    cases = dict(verifier.BROAD_EXECUTIONS)
    # The accepted platform shard already includes all fourteen RCSI cases;
    # its dedicated skip0 job is an additional required check, not a disjoint set.
    for name in verifier.EXPECTED_TESTS:
        if cases.get(name) != 1:
            raise RuntimeError("Broad SQL inventory must retain every exact RCSI case once.")
    return cases


def deferred_cases(root: Path, name: str) -> tuple[str, ...] | None:
    reviewed = DEFERRED_THEORIES.get(name)
    if reviewed is None:
        return None
    relative, fingerprint, cases = reviewed
    path = root / relative
    if path.is_symlink() or not path.is_file() or hashlib.sha256(
        path.read_bytes().replace(b"\r\n", b"\n")
    ).hexdigest() != fingerprint:
        raise RuntimeError("Deferred theory source differs from its reviewed case inventory.")
    if len(cases) < 2 or len(set(cases)) != len(cases) or any(
        not case.startswith(name + "(") for case in cases
    ):
        raise RuntimeError("Deferred theory case inventory is incomplete or duplicated.")
    return cases


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
            != [1, 1, 18]):
        raise RuntimeError("Backend groups must partition exactly twenty TestSDK projects.")
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
        raise RuntimeError("Backend solution must retain exactly seventy-one unique projects.")
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


def discovery_cases(stdout: str, project: str) -> dict[str, list[str]]:
    prefix = Path(project).stem + "."
    names: dict[str, list[str]] = {}
    for line in stdout.splitlines():
        display = line.strip()
        candidate = display.split("(", 1)[0]
        if candidate.startswith(prefix) and FQN_PATTERN.fullmatch(candidate) is not None:
            names.setdefault(candidate, []).append(display)
    if not names:
        raise RuntimeError("Backend project discovery is empty.")
    return names


def discovery_map(stdout: str, project: str) -> Counter[str]:
    return Counter({name: len(cases) for name, cases in discovery_cases(stdout, project).items()})


# The supported adapter IDs are independent of its lossy display-name view.
# https://github.com/xunit/visualstudio.xunit/blob/3.1.4/src/xunit.runner.visualstudio/Sinks/VsDiscoverySink.cs
FRAMEWORK_PACKAGES = {
    "Microsoft.NET.Test.Sdk": ("17.14.1", "HJKqKOE+vshXra2aEHpi2TlxYX7Z9VFYkr+E5rwEvHC8eIXiyO+K9kNm8vmNom3e2rA56WqxU+/N9NJlLGXsJQ=="),
    "Microsoft.TestPlatform.TestHost": ("17.14.1", "d78LPzGKkJwsJXAQwsbJJ7LE7D1wB+rAyhHHAaODF+RDSQ0NgMjDFkSA1Djw18VrxO76GlKAjRUhl+H8NL8Z+Q=="),
    "xunit": ("2.9.3", "TlXQBinK35LpOPKHAqbLY4xlEen9TBafjs0V5KnA4wZsoQLQJiirCR4CbIXvOH8NzkW4YeJKP5P/Bnrodm0h9Q=="),
    "xunit.runner.visualstudio": ("3.1.4", "5mj99LvCqrq3CNi06xYdyIAXOEh+5b33F2nErCzI5zWiDdLHXiPXEWFSUAF8zlIv0ZWqjZNCwHTQeAPYbF3pCg=="),
}
DISCOVERY_BOUNDARY = "vstest.console.dll, TestRequestSender.OnDiscoveryMessageReceived: Received message: "
EXECUTOR_URI = "executor://xunit/VsTestRunner3/netcore/"


def framework_inventory(root: Path) -> str:
    rows = []
    for project in (p for paths in catalog().values() for p in paths):
        path = root / project
        document = ET.parse(path).getroot()
        for package in ("Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio"):
            refs = [e for e in document.iter("PackageReference")
                    if e.attrib.get("Include") == package]
            if len(refs) != 1 or refs[0].attrib.get("Version") != FRAMEWORK_PACKAGES[package][0]:
                raise RuntimeError("Backend discovery framework version is unsupported or ambiguous.")
        rows.append((project, hashlib.sha256(path.read_bytes()).hexdigest()))
    return digest([rows, FRAMEWORK_PACKAGES])


def verify_framework_assets(root: Path, project: str) -> None:
    path = root / Path(project).parent / "obj/project.assets.json"
    try:
        if path.is_symlink():
            raise RuntimeError("Backend framework assets are linked.")
        assets = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_unique_object)
        if Path(assets["project"]["restore"]["projectPath"]).resolve() != (root / project).resolve():
            raise RuntimeError("Backend framework assets belong to another project.")
        libraries = assets["libraries"]
        for package, (version, fingerprint) in FRAMEWORK_PACKAGES.items():
            matches = [key for key in libraries if key.split("/", 1)[0].lower() == package.lower()]
            key = package + "/" + version
            if matches != [key] or libraries[key].get("sha512") != fingerprint:
                raise RuntimeError("Backend restored framework version or package hash is unsupported.")
    except (OSError, ValueError, KeyError, TypeError) as exception:
        raise RuntimeError("Backend framework assets are missing or malformed.") from exception


def canonical_test_id(value: object) -> str:
    if (not isinstance(value, str) or not EXECUTION_ID_PATTERN.fullmatch(value)
            or value != value.lower() or not uuid.UUID(value).int):
        raise RuntimeError("Backend native test identity is missing or noncanonical.")
    return value


def assembly_relative_path(project: str) -> str:
    return (Path(project).parent / "bin/Release/net10.0" /
            (Path(project).stem + ".dll")).as_posix()


def structured_discovery(path: Path, root: Path, project: str) -> dict[str, list[str]]:
    """Read one actual VSTest17.14.1 console receive stream, never echoed host frames.

    TestFound batches plus completion's LastDiscoveredTests are disjoint. IDs,
    not escaped/truncated DisplayName strings, define ordinary case identity.
    """
    expected_source = (root / assembly_relative_path(project)).resolve()
    try:
        if path.is_symlink():
            raise RuntimeError("Backend discovery diagnostic is linked.")
        lines = path.read_text(encoding="utf-8", errors="strict").splitlines()
    except (OSError, UnicodeError) as exception:
        raise RuntimeError("Backend structured discovery is missing or malformed.") from exception
    cases = []
    completed = False
    total = None
    for line in lines:
        if DISCOVERY_BOUNDARY not in line:
            continue
        prefix, payload = line.split(DISCOVERY_BOUNDARY, 1)
        if not re.fullmatch(r"TpTrace Verbose: 0 : [0-9]+, [0-9]+, [^\r\n]+, ", prefix):
            raise RuntimeError("Backend discovery transport boundary is unsupported.")
        try:
            message = json.loads(payload, object_pairs_hook=_unique_object)
        except (ValueError, UnicodeError) as exception:
            raise RuntimeError("Backend discovery packet is malformed.") from exception
        if (not isinstance(message, dict) or set(message) != {"Version", "MessageType", "Payload"}
                or type(message["Version"]) is not int or message["Version"] != 7):
            raise RuntimeError("Backend discovery protocol version or shape is unsupported.")
        kind = message["MessageType"]
        body = message["Payload"]
        if kind == "TestSession.Message":
            continue
        if completed:
            raise RuntimeError("Backend discovery has extra or repeated completion packets.")
        if kind == "TestDiscovery.TestFound":
            if not isinstance(body, list) or not body:
                raise RuntimeError("Backend discovery case batch is malformed.")
            cases.extend(body)
        elif kind == "TestDiscovery.Completed":
            required = {"TotalTests", "LastDiscoveredTests", "IsAborted", "Metrics",
                        "FullyDiscoveredSources", "PartiallyDiscoveredSources",
                        "NotDiscoveredSources", "SkippedDiscoverySources", "DiscoveredExtensions"}
            if (not isinstance(body, dict) or set(body) != required
                    or type(body["TotalTests"]) is not int or body["TotalTests"] < 1
                    or body["IsAborted"] is not False
                    or body["FullyDiscoveredSources"] != [str(expected_source)]
                    or any(body[k] != [] for k in ("PartiallyDiscoveredSources", "NotDiscoveredSources", "SkippedDiscoverySources"))
                    or not isinstance(body["LastDiscoveredTests"], list)):
                raise RuntimeError("Backend discovery completion is partial, aborted or from another source.")
            cases.extend(body["LastDiscoveredTests"])
            total = body["TotalTests"]
            completed = True
        else:
            raise RuntimeError("Backend discovery message type is unsupported.")
    if not completed or total != len(cases):
        raise RuntimeError("Backend discovery is incomplete or has inconsistent case counts.")
    names: dict[str, list[str]] = {}
    seen: set[str] = set()
    for case in cases:
        if not isinstance(case, dict) or set(case) != {"Id", "FullyQualifiedName", "DisplayName", "ExecutorUri", "Source", "CodeFilePath", "LineNumber", "Properties"}:
            raise RuntimeError("Backend discovered case shape is unsupported.")
        test_id = canonical_test_id(case["Id"])
        name = case["FullyQualifiedName"]
        display = case["DisplayName"]
        if (test_id in seen or not isinstance(name, str) or not FQN_PATTERN.fullmatch(name)
                or not name.startswith(Path(project).stem + ".")
                or case["ExecutorUri"] != EXECUTOR_URI or case["Source"] != str(expected_source)
                or not isinstance(display, str) or not display):
            raise RuntimeError("Backend discovered case identity, executor or assembly is wrong or duplicated.")
        try:
            display.encode("utf-8", errors="strict")
        except UnicodeError as exception:
            raise RuntimeError("Backend discovery has an unpaired UTF-16 surrogate.") from exception
        properties = case["Properties"]
        if not isinstance(properties, list):
            raise RuntimeError("Backend discovery properties are malformed.")
        values = {}
        for prop in properties:
            if not isinstance(prop, dict) or set(prop) != {"Key", "Value"} or not isinstance(prop["Key"], dict):
                raise RuntimeError("Backend discovery property shape is unsupported.")
            key = prop["Key"].get("Id")
            if not isinstance(key, str) or key in values:
                raise RuntimeError("Backend discovery repeats a property identity.")
            values[key] = prop["Value"]
        unique = values.get("XunitTestCaseUniqueID")
        if (not isinstance(unique, str) or not re.fullmatch(r"[0-9a-f]{40}", unique)
                or values.get("TestCase.ManagedType") != name.rsplit(".", 1)[0]
                or values.get("TestCase.ManagedMethod") != name.rsplit(".", 1)[1]):
            raise RuntimeError("Backend discovery lacks its actual xUnit native identity.")
        native = uuid.UUID(bytes_le=hashlib.sha1((EXECUTOR_URI + unique).encode("utf-16-le")).digest()[:16])
        if str(native) != test_id:
            raise RuntimeError("Backend SDK test ID does not match the adapter's full native case identity.")
        seen.add(test_id)
        names.setdefault(name, []).append(test_id)
    return {name: sorted(values) for name, values in names.items()}


def trx_result_map(
    path: Path, discovered: Counter[str], *, expected_source: Path,
    execution_ids: set[str] | None = None,
    exact_multiplicity: bool = True, allowed_skips: set[str] | None = None,
    expected_case_names: dict[str, list[str]] | None = None,
    expected_case_ids: dict[str, list[str]] | None = None,
    observed_case_ids: dict[str, list[str]] | None = None,
    observed_case_names: dict[str, list[str]] | None = None,
) -> tuple[dict[str, dict[str, int]], int, int]:
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
    expected_assembly = str(expected_source.resolve())
    for element in elements("UnitTest"):
        test_id = element.attrib.get("id")
        methods = [child for child in element.iter()
                   if child.tag.rsplit("}", 1)[-1] == "TestMethod"]
        if not test_id or test_id in definitions or len(methods) != 1:
            raise RuntimeError("Backend TRX test definition is missing or duplicated.")
        method = methods[0]
        # VSTest17.14.1's TRX serializer lowercases storage; codeBase retains the
        # exact native path. Check both actual formats and the pinned executor.
        # https://github.com/microsoft/vstest/blob/v17.14.1/src/Microsoft.TestPlatform.Extensions.TrxLogger/ObjectModel/TestElement.cs
        if (element.attrib.get("storage") != expected_assembly.lower()
                or method.attrib.get("codeBase") != expected_assembly
                or method.attrib.get("adapterTypeName") != EXECUTOR_URI):
            raise RuntimeError("Backend TRX assembly or executor provenance differs from discovery.")
        name = f"{method.attrib.get('className', '')}.{method.attrib.get('name', '')}"
        if FQN_PATTERN.fullmatch(name) is None:
            raise RuntimeError("Backend TRX test identity is not canonical.")
        definitions[test_id] = name
    results: dict[str, dict[str, int]] = {}
    case_names: dict[str, list[str]] = {}
    case_ids: dict[str, list[str]] = {}
    total = 0
    passed = 0
    skipped = 0
    if execution_ids is None:
        execution_ids = set()
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
        name = definitions[test_id]
        if outcome == "NotExecuted" and name not in (allowed_skips or set()):
            raise RuntimeError("Required non-SQL backend test was not executed.")
        entry = results.setdefault(name, {"passed": 0, "skipped": 0})
        case_names.setdefault(name, []).append(element.attrib.get("testName", ""))
        case_ids.setdefault(name, []).append(test_id)
        entry["passed" if outcome == "Passed" else "skipped"] += 1
        total += 1
        passed += outcome == "Passed"
        skipped += outcome == "NotExecuted"
    if set(definitions) != {identity for values in case_ids.values() for identity in values}:
        raise RuntimeError("Backend TRX has unused or missing native test definitions.")
    if not results or set(results) != set(discovered):
        raise RuntimeError("Backend TRX result identities differ from exact project discovery.")
    if not exact_multiplicity:
        raise RuntimeError("Backend execution multiplicity cannot use a lower bound.")
    if any(
        sum(results[name].values()) != discovered[name] for name in discovered
    ):
        raise RuntimeError("Backend TRX execution multiplicity differs from exact discovery.")
    if expected_case_ids is not None:
        if {name: Counter(values) for name, values in case_ids.items()} != {
            name: Counter(values) for name, values in expected_case_ids.items()
        }:
            raise RuntimeError("Backend TRX native case identities differ from exact SDK discovery.")
        for values in case_ids.values():
            for value in values:
                canonical_test_id(value)
    if expected_case_names is not None and any(
        Counter(case_names.get(name, [])) != Counter(values)
        for name, values in expected_case_names.items()
    ):
        raise RuntimeError("Backend TRX case identities differ from exact reviewed data.")
    if observed_case_ids is not None:
        observed_case_ids.update({name: sorted(values) for name, values in case_ids.items()})
    if observed_case_names is not None:
        observed_case_names.update({name: sorted(values) for name, values in case_names.items()})
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


def api_test_batches(discovered: Counter[str]) -> list[Counter[str]]:
    """Partition the complete discovery, preserving classes and theory rows."""
    classes: dict[str, Counter[str]] = {}
    if not discovered:
        raise RuntimeError("API batch discovery is empty.")
    for name, cases in sorted(discovered.items()):
        if not isinstance(name, str) or FQN_PATTERN.fullmatch(name) is None \
                or type(cases) is not int or cases < 1:
            raise RuntimeError("API batch discovery has a noncanonical test identity or count.")
        # VSTest filters are case insensitive. Keep case-equivalent class names
        # together so exact method filters cannot select a different batch.
        classes.setdefault(name.rsplit(".", 1)[0].lower(), Counter())[name] = cases
    batches: list[Counter[str]] = []
    current: Counter[str] = Counter()
    for methods in classes.values():
        if current and len(current) + len(methods) > API_BATCH_METHOD_BUDGET:
            batches.append(current)
            current = Counter()
        current.update(methods)
    if current:
        batches.append(current)
    union: Counter[str] = Counter()
    for batch in batches:
        if union.keys() & batch.keys():
            raise RuntimeError("API test batches overlap.")
        union.update(batch)
    if union != discovered:
        raise RuntimeError("API test batches differ from complete project discovery.")
    return batches


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
        "frameworkInventoryHash": framework_inventory(root),
        "separateSqlInventoryHash": digest(sorted(relational_cases(root).items())),
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


def _valid_proof(proof: object, expected: dict[str, object], group: str, root: Path) -> None:
    if not isinstance(proof, dict) or set(proof) != set(expected):
        raise RuntimeError("Backend group proof schema is incomplete or extended.")
    if any(type(proof[key]) is not type(value) or proof[key] != value
           for key, value in expected.items() if key != "projects"):
        raise RuntimeError("Backend group proof does not match source, commit and catalog.")
    rows = proof["projects"]
    if not isinstance(rows, list) or len(rows) != len(GROUP_PROJECTS[group]):
        raise RuntimeError("Backend group proof project count is wrong.")
    sql_cases = relational_cases(root)
    for path, row in zip(GROUP_PROJECTS[group], rows, strict=True):
        if not isinstance(row, dict) or set(row) != {
            "path", "discoveryHash", "resultHash", "discoveredMethods",
            "discoveredCases", "executionCount", "passed", "skipped", "stageElapsedMs",
            "testCases", "assemblyPath", "executorUri",
        } or row["path"] != path:
            raise RuntimeError("Backend group proof project identity is wrong.")
        if row["assemblyPath"] != assembly_relative_path(path) or row["executorUri"] != EXECUTOR_URI:
            raise RuntimeError("Backend group proof assembly or executor provenance is wrong.")
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
        tests = row["testCases"]
        # Each compact record binds FQN, SDK discovery count, passed/SQL-excluded
        # counts, discovery display identities and verified runtime identities.
        if not isinstance(tests, list) or not tests:
            raise RuntimeError("Backend group proof lacks exact method-level execution records.")
        discovered: Counter[str] = Counter()
        results: dict[str, dict[str, int]] = {}
        seen_ids: set[str] = set()
        for record in tests:
            if not isinstance(record, list) or len(record) != 7:
                raise RuntimeError("Backend method execution record schema is wrong.")
            name, discovered_count, passed_count, skipped_count, native_ids, result_hash, raw_names_hash = record
            if (not isinstance(name, str) or not FQN_PATTERN.fullmatch(name)
                    or not name.startswith(Path(path).stem + ".") or name in discovered
                    or any(type(value) is not int or value < 0 for value in
                           (discovered_count, passed_count, skipped_count))
                    or discovered_count < 1
                    or any(not isinstance(value, str) or not HASH_PATTERN.fullmatch(value)
                           for value in (result_hash, raw_names_hash))):
                raise RuntimeError("Backend method identity, counts or case hashes are invalid.")
            if skipped_count and (group != "api" or name not in sql_cases):
                raise RuntimeError("Required non-SQL backend test was not executed.")
            if (not isinstance(native_ids, list) or len(native_ids) != discovered_count
                    or any(not isinstance(value, str) for value in native_ids)
                    or native_ids != sorted(native_ids) or len(set(native_ids)) != len(native_ids)
                    or seen_ids.intersection(native_ids)):
                raise RuntimeError("Backend method native discovery identities are missing or duplicated.")
            for value in native_ids:
                canonical_test_id(value)
            seen_ids.update(native_ids)
            deferred = deferred_cases(root, name)
            expected_count = discovered_count
            expected_ids = native_ids
            if deferred is not None:
                if discovered_count != 1:
                    raise RuntimeError("Deferred theory discovery differs from its exact reviewed method.")
                expected_count = len(deferred)
                expected_ids = native_ids * expected_count
                if raw_names_hash != digest(sorted(deferred)):
                    raise RuntimeError("Backend method case multiplicity or identity differs from reviewed data.")
            if passed_count + skipped_count != expected_count or result_hash != digest(sorted(expected_ids)):
                raise RuntimeError("Backend method case multiplicity or identity differs from discovery.")
            discovered[name] = discovered_count
            results[name] = {"passed": passed_count, "skipped": skipped_count}
        if (list(discovered) != sorted(discovered)
                or row["discoveredMethods"] != len(discovered)
                or row["discoveredCases"] != sum(discovered.values())
                or row["passed"] != sum(value["passed"] for value in results.values())
                or row["skipped"] != sum(value["skipped"] for value in results.values())
                or row["executionCount"] != row["passed"] + row["skipped"]
                or row["discoveryHash"] != digest(sorted(discovered.items()))
                or row["resultHash"] != digest(sorted((name, values["passed"], values["skipped"])
                                                    for name, values in results.items()))):
            raise RuntimeError("Backend group proof counts are inconsistent.")


def verify_aggregate(
    root: Path, directory: Path, source_head: str, tested_commit: str,
    build_result: str, worker_result: str,
    relational_proof_directory: Path, relational_worker_result: str, rcsi_result: str,
) -> int:
    if build_result != "success" or worker_result != "success":
        raise RuntimeError("Backend full build and every TestSDK worker must succeed.")
    relational_verifier(root).verify_aggregate_proofs(
        relational_proof_directory, source_head, tested_commit, relational_worker_result, rcsi_result,
    )
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
        _valid_proof(proof, expected_proof(group, source_head, tested_commit, root), group, root)
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
    allowed_skips = set(relational_cases(root)) if group == "api" else set()
    if proof_path.exists() or (results_directory.exists() and any(results_directory.iterdir())):
        raise RuntimeError("Backend group requires fresh proof and TRX output paths.")
    results_directory.mkdir(parents=True, exist_ok=True)
    for project in GROUP_PROJECTS[group]:
        name = Path(project).stem
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
        verify_framework_assets(root, project)
        diagnostic = results_directory / f"{name}.discovery.diag.log"
        measure(Stage.DISCOVERY, lambda: run_checked([
            "dotnet", "test", project, "-noAutoResponse", "--configuration", "Release",
            "--no-restore", "--no-build", "--list-tests", "--diag", str(diagnostic),
            "--logger", "console;verbosity=quiet",
        ], root))
        discovered_ids = structured_discovery(diagnostic, root, project)
        discovered = Counter({name: len(cases) for name, cases in discovered_ids.items()})
        expected_names = {name: list(cases) for name in discovered_ids
                          if (cases := deferred_cases(root, name)) is not None}
        expected_ids = {
            name: cases * len(expected_names[name]) if name in expected_names else cases
            for name, cases in discovered_ids.items()
        }
        if any(len(discovered_ids[name]) != 1 for name in expected_names):
            raise RuntimeError("Deferred theory discovery differs from its exact reviewed method.")
        batches = api_test_batches(discovered) if group == "api" else [discovered]
        results: dict[str, dict[str, int]] = {}
        execution_ids: set[str] = set()
        observed_ids: dict[str, list[str]] = {}
        observed_names: dict[str, list[str]] = {}
        passed = skipped = 0
        for index, batch in enumerate(batches, start=1):
            suffix = f".batch-{index:03d}" if group == "api" else ""
            trx = results_directory / f"{name}{suffix}.trx"
            arguments = [
                "dotnet", "test", project, "-noAutoResponse", "--configuration", "Release",
                "--no-restore", "--no-build", "--results-directory", str(results_directory),
                "--logger", f"trx;LogFileName={trx.name}",
                "--logger", "console;verbosity=quiet",
            ]
            if group == "api":
                arguments += ["--filter", "|".join(
                    f"FullyQualifiedName={method}" for method in sorted(batch)
                )]
                print(f"BACKEND_API_BATCH: START batch={index}/{len(batches)} "
                      f"methods={len(batch)} discoveredCases={sum(batch.values())}", flush=True)
            measure(Stage.BOOTSTRAP_PREFLIGHT, lambda: run_bootstrap_preflight(root))
            measure(Stage.TEST, lambda: run_checked(arguments, root))
            if not trx.is_file() or trx.is_symlink():
                raise RuntimeError("Backend project did not produce its exact TRX.")
            batch_results, batch_passed, batch_skipped = trx_result_map(
                trx, Counter({name: len(expected_ids[name]) for name in batch}),
                expected_source=root / assembly_relative_path(project),
                execution_ids=execution_ids, allowed_skips=allowed_skips,
                expected_case_names={name: expected_names[name] for name in batch if name in expected_names},
                expected_case_ids={name: expected_ids[name] for name in batch},
                observed_case_ids=observed_ids, observed_case_names=observed_names,
            )
            if results.keys() & batch_results.keys():
                raise RuntimeError("Backend test batch results overlap.")
            results.update(batch_results)
            passed += batch_passed
            skipped += batch_skipped
            if group == "api":
                print(f"BACKEND_API_BATCH: PASS batch={index}/{len(batches)} "
                      f"passed={batch_passed} skipped={batch_skipped}", flush=True)
        if set(results) != set(discovered):
            raise RuntimeError("Backend test batches do not cover complete project discovery.")
        proof["projects"].append({
            "path": project,
            "assemblyPath": assembly_relative_path(project),
            "executorUri": EXECUTOR_URI,
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
            "testCases": [
                [name, discovered[name], results[name]["passed"], results[name]["skipped"],
                 discovered_ids[name], digest(observed_ids[name]), digest(observed_names[name])]
                for name in sorted(discovered)
            ],
        })
    _valid_proof(proof, expected_proof(group, source_head, tested_commit, root), group, root)
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
    parser.add_argument("--relational-proof-directory", type=Path)
    parser.add_argument("--relational-worker-result")
    parser.add_argument("--rcsi-result")
    args = parser.parse_args()
    if args.source_head is None or args.tested_commit is None:
        raise RuntimeError("Backend group source and tested commit are mandatory.")
    canonical_head(args.source_head, "source head")
    canonical_head(args.tested_commit, "tested commit")
    if args.group is not None:
        if (args.results_directory is None or args.proof_output is None
                or args.proof_directory is not None or args.build_result is not None
                or args.worker_result is not None or args.relational_proof_directory is not None
                or args.relational_worker_result is not None or args.rcsi_result is not None):
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
            or args.worker_result is None or args.relational_proof_directory is None
            or args.relational_worker_result is None or args.rcsi_result is None):
        raise RuntimeError("Backend aggregate requires only exact proof and worker inputs.")
    directory = (root / args.proof_directory).resolve()
    if not directory.is_relative_to(root):
        raise RuntimeError("Backend group proofs must remain inside source.")
    relational_directory = (root / args.relational_proof_directory).resolve()
    if not relational_directory.is_relative_to(root):
        raise RuntimeError("Separate SQL proofs must remain inside source.")
    total = verify_aggregate(
        root, directory, args.source_head, args.tested_commit,
        args.build_result, args.worker_result,
        relational_directory, args.relational_worker_result, args.rcsi_result,
    )
    print(f"BACKEND_TEST_GROUPS: PASS projects={TEST_PROJECT_COUNT} executions={total}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (RuntimeError, OSError, subprocess.TimeoutExpired) as exception:
        print(f"BACKEND_TEST_GROUPS: FAIL {exception}", file=sys.stderr)
        raise SystemExit(1) from None
