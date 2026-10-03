#!/usr/bin/env python3
"""Produce a bounded, secret-free JSON summary from VSTest TRX results."""

from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import re
import sys
from xml.parsers import expat


MAX_TRX_FILES = 128
MAX_TRX_BYTES = 256 * 1024 * 1024
MAX_TOTAL_TRX_BYTES = 512 * 1024 * 1024
MAX_TEST_DEFINITIONS = 2_000_000
MAX_TEST_RESULTS = 2_000_000
MAX_METADATA_CHARS = 4096
MAX_XML_DEPTH = 256
STREAM_CHUNK_BYTES = 1024 * 1024
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


class SummaryInputFailure(RuntimeError):
    """A fixed, non-sensitive input failure that may be published as JSON."""

    def __init__(
        self,
        category: str,
        trx_file_count: int,
        input_byte_count: int | None,
    ) -> None:
        super().__init__(category)
        self.category = category
        self.trx_file_count = trx_file_count
        self.input_byte_count = input_byte_count


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


class RejectedXml(RuntimeError):
    """An XML shape that cannot be processed within the fixed safety bounds."""


def bounded_attribute(attributes: dict[str, str], name: str) -> str:
    value = attributes.get(name, "")
    if len(value) > MAX_METADATA_CHARS:
        raise RejectedXml("metadata limit exceeded")
    return value


def stream_xml(path: Path, handler: object) -> None:
    parser = expat.ParserCreate(namespace_separator="}")
    depth = 0

    def start_element(name: str, attributes: dict[str, str]) -> None:
        nonlocal depth
        depth += 1
        if depth > MAX_XML_DEPTH:
            raise RejectedXml("XML depth limit exceeded")
        handler.start(local_name(name), attributes, depth)

    def end_element(name: str) -> None:
        nonlocal depth
        handler.end(local_name(name), depth)
        depth -= 1

    def reject_declaration(*_arguments: object) -> None:
        raise RejectedXml("DTD and entity declarations are forbidden")

    def reject_external_entity(*_arguments: object) -> int:
        raise RejectedXml("external entities are forbidden")

    parser.StartElementHandler = start_element
    parser.EndElementHandler = end_element
    parser.StartDoctypeDeclHandler = reject_declaration
    parser.EntityDeclHandler = reject_declaration
    parser.UnparsedEntityDeclHandler = reject_declaration
    parser.ExternalEntityRefHandler = reject_external_entity

    with path.open("rb") as stream:
        while chunk := stream.read(STREAM_CHUNK_BYTES):
            parser.Parse(chunk, False)
    parser.Parse(b"", True)


class DefinitionCollector:
    def __init__(self) -> None:
        self.methods: dict[str, str] = {}
        self.definition_count = 0
        self.unit_test_depth: int | None = None
        self.test_id = ""
        self.candidates: list[tuple[str, str]] = []

    def start(
        self, name: str, attributes: dict[str, str], depth: int
    ) -> None:
        if name == "UnitTest":
            if self.unit_test_depth is not None:
                raise RejectedXml("nested UnitTest metadata")
            self.definition_count += 1
            if self.definition_count > MAX_TEST_DEFINITIONS:
                raise RejectedXml("test definition limit exceeded")
            self.unit_test_depth = depth
            self.test_id = bounded_attribute(attributes, "id")
            self.candidates = []
        elif name == "TestMethod" and self.unit_test_depth is not None:
            class_name = bounded_attribute(attributes, "className")
            method_name = bounded_attribute(attributes, "name")
            if len(self.candidates) < 2:
                self.candidates.append((class_name, method_name))

    def end(self, name: str, depth: int) -> None:
        if name != "UnitTest" or depth != self.unit_test_depth:
            return
        if self.test_id and len(self.candidates) == 1:
            class_name, method_name = self.candidates[0]
            if CLASS_PATTERN.fullmatch(class_name) and METHOD_PATTERN.fullmatch(
                method_name
            ):
                self.methods[self.test_id] = f"{class_name}.{method_name}"
        self.unit_test_depth = None
        self.test_id = ""
        self.candidates = []


class ResultCollector:
    def __init__(self, methods: dict[str, str]) -> None:
        self.methods = methods
        self.totals: Counter[str] = Counter()
        self.failures: Counter[tuple[str | None, str]] = Counter()

    def start(
        self, name: str, attributes: dict[str, str], _depth: int
    ) -> None:
        if name != "UnitTestResult":
            return
        self.totals["total"] += 1
        if self.totals["total"] > MAX_TEST_RESULTS:
            raise RejectedXml("test result limit exceeded")
        test_id = bounded_attribute(attributes, "testId")
        outcome = bounded_attribute(attributes, "outcome")
        status, category = result_category(outcome)
        self.totals[status] += 1
        if category is not None:
            self.failures[(self.methods.get(test_id), category)] += 1

    def end(self, _name: str, _depth: int) -> None:
        return


def result_category(outcome: str) -> tuple[str, str | None]:
    normalized = outcome.strip().lower()
    if normalized in PASSED_OUTCOMES:
        return "passed", None
    if normalized in SKIPPED_OUTCOMES:
        return "skipped", None
    return "failed", FAILURE_CATEGORIES.get(normalized, "unknown_outcome")


def trx_input_size(path: Path) -> int:
    if path.is_symlink():
        raise SummaryInputFailure("unparseable_input", 1, None)
    try:
        input_byte_count = path.stat().st_size
    except OSError as exception:
        raise SummaryInputFailure("unparseable_input", 1, None) from exception
    if input_byte_count > MAX_TRX_BYTES:
        raise SummaryInputFailure("oversized_input", 1, input_byte_count)
    return input_byte_count


def parse_trx(
    path: Path, input_byte_count: int | None = None
) -> tuple[Counter[str], Counter[tuple[str | None, str]]]:
    if input_byte_count is None:
        input_byte_count = trx_input_size(path)
    try:
        definitions = DefinitionCollector()
        stream_xml(path, definitions)
        results = ResultCollector(definitions.methods)
        stream_xml(path, results)
    except (RejectedXml, expat.ExpatError, OSError) as exception:
        raise SummaryInputFailure(
            "unparseable_input", 1, input_byte_count
        ) from exception

    if results.totals["total"] == 0:
        raise SummaryInputFailure("no_results", 1, input_byte_count)
    return results.totals, results.failures


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
        raise SummaryInputFailure("input_count", len(trx_paths), None)

    try:
        input_sizes = [trx_input_size(path) for path in trx_paths]
        total_input_bytes = sum(input_sizes)
        if total_input_bytes > MAX_TOTAL_TRX_BYTES:
            raise SummaryInputFailure(
                "oversized_input", len(trx_paths), total_input_bytes
            )
    except SummaryInputFailure as failure:
        raise SummaryInputFailure(
            failure.category,
            len(trx_paths),
            failure.input_byte_count,
        ) from failure

    totals: Counter[str] = Counter()
    failures: Counter[tuple[str | None, str]] = Counter()
    for path, input_byte_count in zip(trx_paths, input_sizes, strict=True):
        try:
            file_totals, file_failures = parse_trx(path, input_byte_count)
        except SummaryInputFailure as failure:
            raise SummaryInputFailure(
                failure.category, len(trx_paths), failure.input_byte_count
            ) from failure
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


def build_failure_summary(
    source_head: str,
    tested_commit: str,
    job: str,
    phase: str,
    failure: SummaryInputFailure,
) -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "sourceHead": source_head.lower(),
        "testedCommit": tested_commit.lower(),
        "job": job,
        "phase": phase,
        "totals": None,
        "failureGroups": [],
        "unresolvedFailureCount": None,
        "truncatedFailureGroupCount": None,
        "trxFileCount": failure.trx_file_count,
        "diagnostic": {
            "category": failure.category,
            "trxFileCount": failure.trx_file_count,
            "inputByteCount": failure.input_byte_count,
        },
    }


def write_summary(output: Path, summary: dict[str, object]) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )


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


def produce(args: argparse.Namespace) -> int:
    trx_paths = sorted(args.trx_directory.rglob("*.trx"))
    try:
        summary = build_summary(
            trx_paths,
            args.source_head,
            args.tested_commit,
            args.job,
            args.phase,
            args.failure_group_limit,
        )
    except SummaryInputFailure as failure:
        write_summary(
            args.output,
            build_failure_summary(
                args.source_head,
                args.tested_commit,
                args.job,
                args.phase,
                failure,
            ),
        )
        print(
            f"CI_SAFE_TEST_SUMMARY: FAIL category={failure.category}",
            file=sys.stderr,
        )
        return 1

    write_summary(args.output, summary)
    print(
        "CI_SAFE_TEST_SUMMARY: "
        f"job={args.job} phase={args.phase} total={summary['totals']['total']} "
        f"failed={summary['totals']['failed']} skipped={summary['totals']['skipped']}"
    )
    return 0


def main() -> int:
    return produce(parse_args())


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except RuntimeError as exception:
        print(f"CI_SAFE_TEST_SUMMARY: FAIL {exception}", file=sys.stderr)
        raise SystemExit(1) from exception
