#!/usr/bin/env python3
"""Fail-closed preflight and evidence validator for the SAB browser gate."""

from __future__ import annotations

import argparse
import dataclasses
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as et
from typing import Any


EXPECTED_CASES = frozenset({"SAB-006", "SAB-007", "SAB-008", "SAB-009"})
EXPECTED_SCRIPT = "playwright test --config playwright.sab.config.ts"
PACKAGE_PATH = "tests/e2e/package.json"
LOCKFILE_PATH = "tests/e2e/package-lock.json"
CONFIG_PATH = "tests/e2e/playwright.sab.config.ts"
SPEC_PATH = "tests/e2e/sab/sab.spec.ts"
SOURCE_MARKERS = (CONFIG_PATH, SPEC_PATH)
REQUIRED_PATHS = (PACKAGE_PATH, LOCKFILE_PATH, CONFIG_PATH, SPEC_PATH)
CASE_ID_PATTERN = re.compile(r"^(SAB-\d{3})(?:\s|$)")
ANY_CASE_ID_PATTERN = re.compile(r"(?<![A-Z0-9-])SAB-\d{3}(?![A-Z0-9-])")
PINNED_VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+$")


class ContractError(ValueError):
    pass


@dataclasses.dataclass(frozen=True)
class PreflightResult:
    enabled: bool
    reason: str


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ContractError(message)


def _required_file(root: pathlib.Path, relative: str) -> pathlib.Path:
    candidate = root / relative
    require(candidate.exists(), f"SAB_REQUIRED_FILE_MISSING: {relative}")
    require(candidate.is_file(), f"SAB_REQUIRED_PATH_NOT_FILE: {relative}")
    require(not candidate.is_symlink(), f"SAB_REQUIRED_FILE_SYMLINK: {relative}")
    require(
        candidate.resolve().is_relative_to(root),
        f"SAB_REQUIRED_FILE_ESCAPES_ROOT: {relative}",
    )
    require(candidate.stat().st_size > 0, f"SAB_REQUIRED_FILE_EMPTY: {relative}")
    return candidate


def _object(value: Any, label: str) -> dict[str, Any]:
    require(isinstance(value, dict), f"{label} must be a JSON object")
    return value


def _validate_package_contract(root: pathlib.Path) -> None:
    package_path = _required_file(root, PACKAGE_PATH)
    lockfile_path = _required_file(root, LOCKFILE_PATH)
    package = _object(json.loads(package_path.read_text(encoding="utf-8")), PACKAGE_PATH)
    scripts = _object(package.get("scripts"), f"{PACKAGE_PATH} scripts")
    require(
        scripts.get("sab:ci") == EXPECTED_SCRIPT,
        f"SAB_SCRIPT_MISMATCH: expected {EXPECTED_SCRIPT!r}",
    )
    dependencies = _object(
        package.get("devDependencies"), f"{PACKAGE_PATH} devDependencies"
    )
    playwright_version = dependencies.get("@playwright/test")
    require(
        isinstance(playwright_version, str)
        and PINNED_VERSION_PATTERN.fullmatch(playwright_version) is not None,
        "SAB_PLAYWRIGHT_NOT_PINNED: @playwright/test must use an exact x.y.z version",
    )

    lockfile = _object(
        json.loads(lockfile_path.read_text(encoding="utf-8")), LOCKFILE_PATH
    )
    packages = _object(lockfile.get("packages"), f"{LOCKFILE_PATH} packages")
    root_package = _object(packages.get(""), f"{LOCKFILE_PATH} root package")
    root_dependencies = _object(
        root_package.get("devDependencies"),
        f"{LOCKFILE_PATH} root devDependencies",
    )
    playwright_package = _object(
        packages.get("node_modules/@playwright/test"),
        f"{LOCKFILE_PATH} @playwright/test package",
    )
    playwright_core_package = _object(
        packages.get("node_modules/playwright-core"),
        f"{LOCKFILE_PATH} playwright-core package",
    )
    for label, value in (
        ("root @playwright/test", root_dependencies.get("@playwright/test")),
        ("locked @playwright/test", playwright_package.get("version")),
        ("locked playwright-core", playwright_core_package.get("version")),
    ):
        require(
            value == playwright_version,
            f"SAB_PLAYWRIGHT_LOCK_DRIFT: {label} must equal {playwright_version}",
        )


def validate_preflight(
    repository_root: str | pathlib.Path,
) -> PreflightResult:
    root = pathlib.Path(repository_root).resolve()
    require(root.is_dir(), "SAB_REPOSITORY_ROOT_MISSING")
    marker_presence = tuple((root / relative).exists() for relative in SOURCE_MARKERS)
    require(
        all(marker_presence),
        "SAB_SOURCE_REQUIRED: config and canonical spec must both exist",
    )
    for relative in REQUIRED_PATHS:
        _required_file(root, relative)
    _validate_package_contract(root)
    return PreflightResult(True, "source-present-and-complete")


def _local_name(element: et.Element) -> str:
    return element.tag.rsplit("}", 1)[-1]


def _suite_counter(element: et.Element, counter: str) -> int:
    raw = element.attrib.get(counter)
    require(raw is not None, f"SAB_JUNIT_COUNTER_MISSING: {counter}")
    require(raw.isdigit(), f"SAB_JUNIT_COUNTER_INVALID: {counter}={raw!r}")
    return int(raw)


def validate_evidence(junit_path: str | pathlib.Path, html_path: str | pathlib.Path) -> None:
    junit = pathlib.Path(junit_path)
    html = pathlib.Path(html_path)
    require(junit.is_file(), f"SAB_JUNIT_MISSING: {junit}")
    require(not junit.is_symlink(), f"SAB_JUNIT_SYMLINK: {junit}")
    require(junit.stat().st_size > 0, f"SAB_JUNIT_EMPTY: {junit}")

    root = et.parse(junit).getroot()
    require(
        _local_name(root) in {"testsuites", "testsuite"},
        "SAB_JUNIT_ROOT_INVALID",
    )
    suites = [
        element
        for element in root.iter()
        if _local_name(element) in {"testsuites", "testsuite"}
    ]
    require(suites, "SAB_JUNIT_SUITES_MISSING")
    for suite in suites:
        for counter in ("failures", "errors", "skipped"):
            require(
                _suite_counter(suite, counter) == 0,
                f"SAB_JUNIT_NOT_CLEAN: {counter} must be 0",
            )
    outcome_elements = [
        _local_name(element)
        for element in root.iter()
        if _local_name(element) in {"failure", "error", "skipped"}
    ]
    require(
        not outcome_elements,
        f"SAB_JUNIT_NOT_CLEAN: report contains {sorted(outcome_elements)}",
    )

    cases = [element for element in root.iter() if _local_name(element) == "testcase"]
    require(
        len(cases) == len(EXPECTED_CASES),
        "SAB_JUNIT_CASE_COUNT: exactly four testcases are required",
    )
    case_ids: list[str] = []
    for case in cases:
        name = case.attrib.get("name", "")
        match = CASE_ID_PATTERN.match(name)
        require(
            match is not None,
            "SAB_JUNIT_CASE_ID: every testcase name must start with one SAB ID",
        )
        require(
            ANY_CASE_ID_PATTERN.findall(name) == [match.group(1)],
            "SAB_JUNIT_CASE_ID: every testcase name must contain exactly one SAB ID",
        )
        case_ids.append(match.group(1))
    require(
        set(case_ids) == EXPECTED_CASES and len(case_ids) == len(set(case_ids)),
        "SAB_JUNIT_CASE_SET: expected exact unique SAB-006/007/008/009",
    )

    require(html.is_file(), f"SAB_HTML_MISSING: {html}")
    require(not html.is_symlink(), f"SAB_HTML_SYMLINK: {html}")
    require(html.stat().st_size > 0, f"SAB_HTML_EMPTY: {html}")


def _write_github_output(path: str | None, result: PreflightResult) -> None:
    if path is None:
        return
    output = pathlib.Path(path)
    with output.open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(f"enabled={'true' if result.enabled else 'false'}\n")
        stream.write(f"reason={result.reason}\n")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    subparsers = result.add_subparsers(dest="command", required=True)
    preflight = subparsers.add_parser("preflight")
    preflight.add_argument("--repository-root", required=True)
    preflight.add_argument("--github-output")
    evidence = subparsers.add_parser("evidence")
    evidence.add_argument("--junit", required=True)
    evidence.add_argument("--html", required=True)
    return result


def main() -> int:
    arguments = parser().parse_args()
    try:
        if arguments.command == "preflight":
            result = validate_preflight(
                arguments.repository_root,
            )
            _write_github_output(arguments.github_output, result)
            print(
                "SAB preflight passed: "
                f"enabled={str(result.enabled).lower()} "
                f"reason={result.reason}"
            )
        else:
            validate_evidence(arguments.junit, arguments.html)
            print("SAB evidence passed: exact SAB-006/007/008/009, skip=0, HTML present")
    except (ContractError, json.JSONDecodeError, OSError, et.ParseError) as exception:
        print(f"SAB E2E contract validation failed: {exception}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
