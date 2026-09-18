#!/usr/bin/env python3
"""Artifact contract of the selectable browser workflow (.github/workflows/browser-acceptance.yml).

The workflow reuses the C-01 disposable stack. Its artifacts are NOT C-01's, so they get their own
upload allowlist here; the privacy rules themselves are C-01's, loaded from
tools/validate_c01_e2e_contract.py unchanged (by path: the workflow runs tools with `python3 -I`,
which keeps the script directory off sys.path).
"""
from __future__ import annotations

import argparse
import importlib.util
import pathlib
import re
import sys

_C01_PATH = pathlib.Path(__file__).resolve().with_name("validate_c01_e2e_contract.py")
_spec = importlib.util.spec_from_file_location("validate_c01_e2e_contract", _C01_PATH)
assert _spec is not None and _spec.loader is not None
c01 = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = c01
_spec.loader.exec_module(c01)

ContractError = c01.ContractError
require = c01.require

SUITES = ("smoke", "c03", "org03", "u02")
READ_ONLY_SUITES = ("smoke", "c03")
_SUITE_PATTERN = re.compile(r"^[a-z0-9]+$")


def upload_allowlist(suite: str) -> tuple[str, ...]:
    """The only files the workflow may upload for one suite, relative to the artifact root."""
    require(isinstance(suite, str) and _SUITE_PATTERN.fullmatch(suite) is not None, "suite name is not safe")
    require(suite in SUITES, f"unknown browser suite: {suite}")
    return (
        f"browser-{suite}-junit.xml",
        "browser-fixture-evidence.json",
        "browser-post-browser-sql-evidence.json",
        "container-logs/compose-ps.txt",
    )


def upload_paths(root: str | pathlib.Path, suite: str) -> list[pathlib.Path]:
    artifact_root = pathlib.Path(root).resolve()
    require(artifact_root.is_dir(), "artifact root is missing")
    selected: list[pathlib.Path] = []
    for relative in upload_allowlist(suite):
        candidate = artifact_root / relative
        if not candidate.exists():
            continue
        require(candidate.is_file(), f"artifact allowlist entry is not a file: {relative}")
        require(not candidate.is_symlink(), f"artifact allowlist entry is a symlink: {relative}")
        require(candidate.resolve().is_relative_to(artifact_root), f"artifact allowlist entry escapes root: {relative}")
        selected.append(candidate)
    require(selected, "artifact upload allowlist selected no files")
    return selected


def validate_privacy(root: str | pathlib.Path, suite: str, forbidden_values: list[str] | tuple[str, ...] = ()) -> None:
    """Every file that would be uploaded is free of C-01's forbidden patterns and of the run's secrets."""
    artifact_root = pathlib.Path(root).resolve()
    selected = upload_paths(artifact_root, suite)
    needles: list[bytes] = []
    for index, value in enumerate(forbidden_values):
        require(isinstance(value, str) and len(value.encode("utf-8")) >= 8,
                f"runtime forbidden value {index} is missing or too short")
        needles.append(value.encode("utf-8"))
    for path in selected:
        content = path.read_bytes()
        relative = path.relative_to(artifact_root).as_posix()
        for label, pattern in c01.ARTIFACT_FORBIDDEN_PATTERNS:
            require(pattern.search(content) is None, f"artifact privacy violation ({label}): {relative}")
        for index, needle in enumerate(needles):
            require(needle not in content, f"artifact privacy violation (runtime secret {index}): {relative}")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    commands = result.add_subparsers(dest="command", required=True)
    privacy = commands.add_parser("privacy")
    privacy.add_argument("--root", required=True)
    privacy.add_argument("--suite", required=True)
    privacy.add_argument("--forbidden-value", action="append", default=[])
    return result


def main(argv: list[str] | None = None) -> int:
    arguments = parser().parse_args(argv)
    try:
        if arguments.command == "privacy":
            validate_privacy(arguments.root, arguments.suite, arguments.forbidden_value)
    except ContractError as error:
        print(f"Browser acceptance contract validation failed: {error}", file=sys.stderr)
        return 1
    print("Browser acceptance contract validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
