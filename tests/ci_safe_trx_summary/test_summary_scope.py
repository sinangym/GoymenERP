from __future__ import annotations

import argparse
from contextlib import redirect_stderr, redirect_stdout
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location(
    "summarize_trx_scope", Path(__file__).resolve().parents[2] / "tools/ci/summarize_trx.py"
)
assert SPEC is not None and SPEC.loader is not None
SUMMARY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SUMMARY)


class ObservedTrxScopeTests(unittest.TestCase):
    def produce(self, root: Path) -> tuple[int, dict[str, object], str]:
        output = root / "summary.json"
        args = argparse.Namespace(
            trx_directory=root, output=output, source_head="a" * 40,
            tested_commit="b" * 40, job="backend-test-group", phase="test-suite",
            failure_group_limit=50,
        )
        console = io.StringIO()
        with redirect_stdout(console), redirect_stderr(console):
            code = SUMMARY.produce(args)
        return code, json.loads(output.read_text(encoding="utf-8")), console.getvalue()

    def write_result(self, root: Path, outcome: str = "Passed") -> None:
        (root / "batch-001.trx").write_text(
            '<TestRun><Results><UnitTestResult testId="one" outcome="' + outcome + '" '
            'testName="private=DO_NOT_EXPORT" /></Results><TestDefinitions>'
            '<UnitTest id="one"><TestMethod className="GoymenERP.Tests.Batch" '
            'name="One" /></UnitTest></TestDefinitions></TestRun>', encoding="utf-8",
        )

    def assert_scope(self, summary: dict[str, object], console: str) -> None:
        self.assertEqual(2, summary["schemaVersion"])
        self.assertEqual("observed_trx_results_only", summary["evidenceScope"])
        self.assertEqual("unknown", summary["groupCompleteness"])
        self.assertEqual("not_evaluated", summary["jobAcceptance"])
        self.assertIn("scope=observed_trx_results_only", console)
        self.assertIn("groupCompleteness=unknown", console)
        self.assertIn("jobAcceptance=not_evaluated", console)
        self.assertNotIn("DO_NOT_EXPORT", json.dumps(summary) + console)

    def test_passing_partial_batch_does_not_certify_group_or_job(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.write_result(root)
            code, summary, console = self.produce(root)
        self.assertEqual(0, code)  # The summary operation succeeded; not the job.
        self.assertEqual({"total": 1, "passed": 1, "failed": 0, "skipped": 0}, summary["totals"])
        self.assertTrue(console.startswith("CI_SAFE_TEST_SUMMARY: job=backend-test-group"))
        self.assert_scope(summary, console)

    def test_failure_count_remains_observed_not_an_invented_verifier_result(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.write_result(root, "Failed")
            code, summary, console = self.produce(root)
        self.assertEqual(0, code)
        self.assertEqual({"total": 1, "passed": 0, "failed": 1, "skipped": 0}, summary["totals"])
        self.assertEqual([{"testFqn": "GoymenERP.Tests.Batch.One", "category": "assertion_or_test_failure", "count": 1}], summary["failureGroups"])
        self.assert_scope(summary, console)

    def test_missing_and_malformed_inputs_keep_unknown_scope_and_fail_summary(self) -> None:
        for source in (None, "<private>DO_NOT_EXPORT"):
            with self.subTest(source_present=source is not None), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                if source is not None:
                    (root / "result.trx").write_text(source, encoding="utf-8")
                code, summary, console = self.produce(root)
                self.assertEqual(1, code)
                self.assertIsNone(summary["totals"])
                self.assert_scope(summary, console)

    def test_untrusted_sibling_proof_cannot_promote_partial_results(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.write_result(root)
            (root / "proof-api-3.json").write_text(
                json.dumps({"success": True, "sourceHead": "c" * 40, "private": "DO_NOT_EXPORT"}),
                encoding="utf-8",
            )
            code, summary, console = self.produce(root)
        self.assertEqual(0, code)
        self.assert_scope(summary, console)


if __name__ == "__main__":
    unittest.main()
