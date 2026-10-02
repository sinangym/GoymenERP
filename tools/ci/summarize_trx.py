#!/usr/bin/env python3
"""Produce a bounded, secret-free JSON summary from VSTest TRX results."""

from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


MAX_TRX_FILES = 128
MAX_TRX_BYTES = 64 * 1024 * 1024
DEFAULT_FAILURE_GROUP_LIMIT = 50
HEAD_PATTERN = re.compile(r"^[0-9a-fA-F]{40}$")
LABEL_PATTERN = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")
CLASS_PATTERN = re.compile(r"^[A-Za-z_][A-Za-z0-9_.+`]*$")
METHOD_PATTERN = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")

PASSED_OUTCOMES = {"passed"}
SKIPPED_OUTCOMES = {"notexecuted", "inconclusive", "skipped"}
FAILURE_CATEGORIES = {
    "failed": "assertion_or_test_failure",
    "error": "test_error",
    "timeout": "timeout",
    "aborted": "aborted",
}


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def canonical_test_methods(root: ET.Element) -> dict[str, str]:
    methods: dict[str, str] = {}
    for unit_test in root.iter():
        if local_name(unit_test.tag) != "UnitTest":
            continue
        test_id = unit_test.attrib.get("id", "")
        candidates = [
            child
            for child in unit_test.iter()
            if local_name(child.tag) == "TestMethod"
        ]
        if not test_id or len(candidates) != 1:
            continue
        class_name = candidates[0].attrib.get("className", "")
        method_name = candidates[0].attrib.get("name", "")
        if not CLASS_PATTERN.fullmatch(class_name):
            continue
        if not METHOD_PATTERN.fullmatch(method_name):
            continue
        methods[test_id] = f"{class_name}.{method_name}"
    return methods


def result_category(outcome: str) -> tuple[str, str | None]:
    normalized = outcome.strip().lower()
    if normalized in PASSED_OUTCOMES:
        return "passed", None
    if normalized in SKIPPED_OUTCOMES:
        return "skipped", None
    return "failed", FAILURE_CATEGORIES.get(normalized, "unknown_outcome")


def parse_trx(path: Path) -> tuple[Counter[str], Counter[tuple[str | None, str]]]:
    if path.is_symlink():
        raise RuntimeError("TRX symlinks are not accepted.")
    if path.stat().st_size > MAX_TRX_BYTES:
        raise RuntimeError("TRX input exceeds the bounded size limit.")
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exception:
        raise RuntimeError("TRX input could not be parsed.") from exception

    methods = canonical_test_methods(root)
    totals: Counter[str] = Counter()
    failures: Counter[tuple[str | None, str]] = Counter()
    for result in root.iter():
        if local_name(result.tag) != "UnitTestResult":
            continue
        status, category = result_category(result.attrib.get("outcome", ""))
        totals["total"] += 1
        totals[status] += 1
        if category is not None:
            failures[(methods.get(result.attrib.get("testId", "")), category)] += 1
    if totals["total"] == 0:
        raise RuntimeError("TRX input contains no test results.")
    return totals, failures


def build_summary(
    trx_paths: list[Path],
    source_head: str,
    tested_commit: str,
    job: str,
    phase: str,
    failure_group_limit: int,
) -> dict[str, object]:
    for label, value in (("source head", source_head), ("tested commit", tested_commit)):
        if not HEAD_PATTERN.fullmatch(value):
            raise RuntimeError(f"{label} must be a 40-character hexadecimal commit id.")
    for label, value in (("job", job), ("phase", phase)):
        if not LABEL_PATTERN.fullmatch(value):
            raise RuntimeError(f"{label} must be a canonical lower-case label.")
    if not 1 <= failure_group_limit <= 100:
        raise RuntimeError("Failure group limit must be between 1 and 100.")
    if not trx_paths or len(trx_paths) > MAX_TRX_FILES:
        raise RuntimeError("TRX file count is outside the accepted bounds.")

    totals: Counter[str] = Counter()
    failures: Counter[tuple[str | None, str]] = Counter()
    for path in trx_paths:
        file_totals, file_failures = parse_trx(path)
        totals.update(file_totals)
        failures.update(file_failures)

    unresolved_failure_count = sum(
        count for (test_fqn, _), count in failures.items() if test_fqn is None
    )
    safe_groups = sorted(
        (
            {"testFqn": test_fqn, "category": category, "count": count}
            for (test_fqn, category), count in failures.items()
            if test_fqn is not None
        ),
        key=lambda item: (str(item["testFqn"]), str(item["category"])),
    )
    reported_groups = safe_groups[:failure_group_limit]
    return {
        "schemaVersion": 1,
        "sourceHead": source_head.lower(),
        "testedCommit": tested_commit.lower(),
        "job": job,
        "phase": phase,
        "totals": {
            "total": totals["total"],
            "passed": totals["passed"],
            "failed": totals["failed"],
            "skipped": totals["skipped"],
        },
        "failureGroups": reported_groups,
        "unresolvedFailureCount": unresolved_failure_count,
        "truncatedFailureGroupCount": max(0, len(safe_groups) - len(reported_groups)),
        "trxFileCount": len(trx_paths),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx-directory", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--source-head", required=True)
    parser.add_argument("--tested-commit", required=True)
    parser.add_argument("--job", required=True)
    parser.add_argument("--phase", required=True)
    parser.add_argument(
        "--failure-group-limit", type=int, default=DEFAULT_FAILURE_GROUP_LIMIT
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    trx_paths = sorted(args.trx_directory.rglob("*.trx"))
    summary = build_summary(
        trx_paths,
        args.source_head,
        args.tested_commit,
        args.job,
        args.phase,
        args.failure_group_limit,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(
        "CI_SAFE_TEST_SUMMARY: "
        f"job={args.job} phase={args.phase} total={summary['totals']['total']} "
        f"failed={summary['totals']['failed']} skipped={summary['totals']['skipped']}"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as exception:
        print(f"CI_SAFE_TEST_SUMMARY: FAIL {exception}", file=sys.stderr)
        raise SystemExit(1) from exception
