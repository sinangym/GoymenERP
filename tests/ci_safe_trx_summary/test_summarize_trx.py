from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = REPOSITORY_ROOT / "tools" / "ci" / "summarize_trx.py"
SPEC = importlib.util.spec_from_file_location("summarize_trx", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
SUMMARIZER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SUMMARIZER)
VERIFIER_PATH = REPOSITORY_ROOT / "tools" / "verify_rcsi_relational_acceptance.py"
VERIFIER_SPEC = importlib.util.spec_from_file_location(
    "verify_rcsi_relational_acceptance_for_safe_summary", VERIFIER_PATH
)
assert VERIFIER_SPEC is not None and VERIFIER_SPEC.loader is not None
VERIFIER = importlib.util.module_from_spec(VERIFIER_SPEC)
VERIFIER_SPEC.loader.exec_module(VERIFIER)


def trx(results: str, definitions: str) -> str:
    return (
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f"<Results>{results}</Results><TestDefinitions>{definitions}</TestDefinitions>"
        "</TestRun>"
    )


def definition(test_id: str, class_name: str, method_name: str) -> str:
    return (
        f'<UnitTest id="{test_id}" name="ignored"><TestMethod '
        f'className="{class_name}" name="{method_name}" /></UnitTest>'
    )


class SafeTrxSummaryTests(unittest.TestCase):
    def test_only_canonical_method_metadata_and_fixed_categories_are_emitted(self) -> None:
        source = trx(
            '<UnitTestResult testId="pass" testName="Pass(secret=NO)" outcome="Passed" />'
            '<UnitTestResult testId="fail" testName="Fail(secret=LEAK)" outcome="Failed">'
            "<Output><ErrorInfo><Message>credential=DO_NOT_EXPORT</Message>"
            "<StackTrace>/private/path/token</StackTrace></ErrorInfo></Output>"
            "</UnitTestResult>"
            '<UnitTestResult testId="skip" testName="Skip(payload=NO)" outcome="NotExecuted" />',
            definition("pass", "GoymenERP.Tests.PriceTests", "Passes")
            + definition("fail", "GoymenERP.Tests.PriceTests", "Fails")
            + definition("skip", "GoymenERP.Tests.PriceTests", "Skips"),
        )
        summary, encoded = self._summary(source)

        self.assertEqual(
            {"total": 3, "passed": 1, "failed": 1, "skipped": 1},
            summary["totals"],
        )
        self.assertEqual(
            [{
                "testFqn": "GoymenERP.Tests.PriceTests.Fails",
                "category": "assertion_or_test_failure",
                "count": 1,
            }],
            summary["failureGroups"],
        )
        for forbidden in ("LEAK", "DO_NOT_EXPORT", "/private/path", "secret=", "StackTrace"):
            self.assertNotIn(forbidden, encoded)

    def test_unsafe_or_unmapped_test_identity_is_counted_without_disclosure(self) -> None:
        source = trx(
            '<UnitTestResult testId="unsafe" testName="Theory(customer=ACME)" outcome="Error" />'
            '<UnitTestResult testId="missing" testName="Missing(apiKey=NO)" outcome="Timeout" />',
            definition("unsafe", "Bad Class", "Theory(customer)"),
        )
        summary, encoded = self._summary(source)

        self.assertEqual(2, summary["totals"]["failed"])
        self.assertEqual(2, summary["unresolvedFailureCount"])
        self.assertEqual([], summary["failureGroups"])
        self.assertNotIn("ACME", encoded)
        self.assertNotIn("apiKey", encoded)

    def test_failure_groups_are_aggregated_sorted_and_bounded(self) -> None:
        results = "".join(
            f'<UnitTestResult testId="id{index}" outcome="Failed" />'
            for index in range(3)
        )
        definitions = "".join(
            definition(f"id{index}", "GoymenERP.Tests.Bounded", f"Case{index}")
            for index in range(3)
        )
        summary, _ = self._summary(trx(results, definitions), limit=2)

        self.assertEqual(2, len(summary["failureGroups"]))
        self.assertEqual(1, summary["truncatedFailureGroupCount"])

    def test_invalid_metadata_and_empty_results_are_rejected(self) -> None:
        with self.assertRaisesRegex(RuntimeError, "40-character"):
            SUMMARIZER.build_summary(
                [], "not-a-head", "b" * 40, "backend", "tests", 50
            )
        with self.assertRaisesRegex(RuntimeError, "no test results"):
            self._summary(trx("", ""))

    def test_ci_workflow_produces_and_uploads_only_safe_json_summaries(self) -> None:
        workflow = (REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml").read_text(
            encoding="utf-8"
        )
        self.assertEqual(2, workflow.count("tools/ci/summarize_trx.py"))
        self.assertEqual(2, workflow.count("actions/upload-artifact@v7"))
        self.assertIn("artifacts/ci-safe-test-summary/backend.json", workflow)
        self.assertIn("artifacts/ci-safe-test-summary/relational.json", workflow)
        self.assertEqual(2, workflow.count("--source-head"))
        self.assertEqual(2, workflow.count("--tested-commit \"$(git rev-parse HEAD)\""))
        self.assertIn(
            "--trx-output artifacts/ci-test-results/relational/broad.trx", workflow
        )
        self.assertIn(
            "--trx-output artifacts/ci-test-results/relational/rcsi.trx", workflow
        )
        self.assertNotIn("path: artifacts/ci-test-results", workflow)
        self.assertIn("permissions:\n  contents: read", workflow)
        self.assertNotIn("continue-on-error", workflow)

    def _summary(self, source: str, limit: int = 50) -> tuple[dict[str, object], str]:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.trx"
            path.write_text(source, encoding="utf-8")
            summary = SUMMARIZER.build_summary(
                [path], "a" * 40, "b" * 40, "backend", "test-suite", limit
            )
        encoded = json.dumps(summary, sort_keys=True)
        return summary, encoded


class RcsiTrxOutputSelectionTests(unittest.TestCase):
    def test_default_and_explicit_modes_bind_logger_to_the_selected_trx_name(self) -> None:
        repository_root = REPOSITORY_ROOT.resolve()
        temporary_directory = Path(tempfile.gettempdir()) / "goymen-rcsi-default"
        cases = (
            (None, temporary_directory / "rcsi.trx", "trx;LogFileName=rcsi.trx"),
            (
                Path("artifacts/ci-test-results/relational/broad.trx"),
                repository_root / "artifacts/ci-test-results/relational/broad.trx",
                "trx;LogFileName=broad.trx",
            ),
            (
                Path("artifacts/ci-test-results/relational/rcsi.trx"),
                repository_root / "artifacts/ci-test-results/relational/rcsi.trx",
                "trx;LogFileName=rcsi.trx",
            ),
        )
        for requested, expected_path, expected_logger in cases:
            with self.subTest(requested=requested):
                selected = VERIFIER.resolve_trx_path(
                    repository_root, requested, temporary_directory
                )
                self.assertEqual(expected_path, selected)
                self.assertEqual(expected_logger, VERIFIER.trx_logger(selected))


if __name__ == "__main__":
    unittest.main()
