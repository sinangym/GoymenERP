from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock


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

    def test_large_ignored_output_is_streamed_without_disclosure(self) -> None:
        ignored = "credential=DO_NOT_EXPORT\n" * 250_000
        source = trx(
            '<UnitTestResult testId="pass" outcome="Passed"><Output><StdOut>'
            + ignored
            + "</StdOut></Output></UnitTestResult>",
            definition("pass", "GoymenERP.Tests.Streaming", "Passes"),
        )

        summary, encoded = self._summary(source)

        self.assertEqual(
            {"total": 1, "passed": 1, "failed": 0, "skipped": 0},
            summary["totals"],
        )
        self.assertNotIn("DO_NOT_EXPORT", encoded)
        self.assertNotIn("credential", encoded)

    def test_closed_diagnostic_marker_is_emitted_without_adjacent_message_text(self) -> None:
        source = trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output><ErrorInfo><Message>'
            'credential=DO_NOT_EXPORT CI_SAFE_DIAGNOSTIC:product_price_list_startup_sql_2714 private/path'
            '</Message></ErrorInfo></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "Fails"),
        )

        summary, encoded = self._summary(source)

        self.assertEqual(2, summary["schemaVersion"])
        self.assertEqual(
            [{
                "testFqn": "GoymenERP.Tests.SafeDiagnostic.Fails",
                "category": "assertion_or_test_failure",
                "diagnosticCode": "product_price_list_startup_sql_2714",
                "count": 1,
            }],
            summary["failureGroups"],
        )
        for forbidden in ("DO_NOT_EXPORT", "credential", "private/path", "CI_SAFE_DIAGNOSTIC"):
            self.assertNotIn(forbidden, encoded)

    def test_closed_marker_survives_stream_chunks_without_retaining_text(self) -> None:
        source = trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output><ErrorInfo><Message>'
            'CI_SAFE_DIAGNOSTIC:product_price_list_startup_ef_db_update'
            '</Message></ErrorInfo></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "Chunked"),
        )

        with mock.patch.object(SUMMARIZER, "STREAM_CHUNK_BYTES", 7):
            summary, _ = self._summary(source)

        self.assertEqual(
            "product_price_list_startup_ef_db_update",
            summary["failureGroups"][0]["diagnosticCode"],
        )

    def test_unknown_duplicate_and_late_markers_emit_no_diagnostic(self) -> None:
        cases = (
            "CI_SAFE_DIAGNOSTIC:not_allowlisted",
            "CI_SAFE_DIAGNOSTIC:product_price_list_startup_unknown "
            "CI_SAFE_DIAGNOSTIC:product_price_list_startup_unknown",
            ("x" * (SUMMARIZER.MAX_DIAGNOSTIC_SCAN_CHARS + 1))
            + "CI_SAFE_DIAGNOSTIC:product_price_list_startup_unknown",
        )
        for message in cases:
            with self.subTest(message_length=len(message)):
                summary, encoded = self._summary(trx(
                    '<UnitTestResult testId="fail" outcome="Failed"><Output><ErrorInfo><Message>'
                    + message
                    + '</Message></ErrorInfo></Output></UnitTestResult>',
                    definition("fail", "GoymenERP.Tests.SafeDiagnostic", "Ignored"),
                ))
                self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
                self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

    def test_exact_scan_cap_keeps_a_complete_closed_marker(self) -> None:
        marker = "CI_SAFE_DIAGNOSTIC:product_price_list_startup_sql_208\n"
        message = marker + ("x" * (SUMMARIZER.MAX_DIAGNOSTIC_SCAN_CHARS - len(marker)))

        summary, encoded = self._summary(trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output><ErrorInfo><Message>'
            + message
            + '</Message></ErrorInfo></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "ExactCap"),
        ))

        self.assertEqual(
            "product_price_list_startup_sql_208",
            summary["failureGroups"][0]["diagnosticCode"],
        )
        self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

    def test_scan_cap_overflow_discards_an_early_marker_and_late_duplicate(self) -> None:
        first = "CI_SAFE_DIAGNOSTIC:product_price_list_startup_sql_208\n"
        second = "CI_SAFE_DIAGNOSTIC:product_price_list_startup_sql_547\n"
        message = (
            first
            + ("x" * (SUMMARIZER.MAX_DIAGNOSTIC_SCAN_CHARS - len(first)))
            + second
        )

        summary, encoded = self._summary(trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output><ErrorInfo><Message>'
            + message
            + '</Message></ErrorInfo></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "Overflow"),
        ))

        self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
        for forbidden in ("product_price_list_startup_sql_208", "product_price_list_startup_sql_547"):
            self.assertNotIn(forbidden, encoded)

    def test_marker_outside_the_exact_result_output_error_message_path_is_ignored(self) -> None:
        source = trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output><Wrapper><ErrorInfo><Message>'
            'CI_SAFE_DIAGNOSTIC:product_price_list_startup_unknown'
            '</Message></ErrorInfo></Wrapper></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "WrongPath"),
        )

        summary, encoded = self._summary(source)

        self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
        self.assertNotIn("product_price_list_startup_unknown", encoded)

    def test_failed_stdout_emits_exact_finance_and_product_phase_markers(self) -> None:
        cases = (
            (
                "CI_SAFE_DIAGNOSTIC:acc_nj_sql_receipt_record_replay",
                "acc_nj_sql_receipt_record_replay",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_setup",
                "product_technical_release_rollback_setup",
            ),
        )
        for marker, expected in cases:
            with self.subTest(marker=marker):
                source = trx(
                    '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
                    + marker
                    + "\ncredential=DO_NOT_EXPORT"
                    + '</StdOut></Output></UnitTestResult>',
                    definition("fail", "GoymenERP.Tests.SafeDiagnostic", "StdOut"),
                )

                summary, encoded = self._summary(source)

                self.assertEqual(expected, summary["failureGroups"][0]["diagnosticCode"])
                for forbidden in ("DO_NOT_EXPORT", "credential", "CI_SAFE_DIAGNOSTIC"):
                    self.assertNotIn(forbidden, encoded)

    def test_nonfailed_wrong_path_duplicate_and_overflow_stdout_emit_no_diagnostic(self) -> None:
        marker = "CI_SAFE_DIAGNOSTIC:acc_nj_sql_trial_balance"
        cases = (
            '<UnitTestResult testId="pass" outcome="Passed"><Output><StdOut>'
            + marker + '</StdOut></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><StdOut>'
            + marker + '</StdOut></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            + marker + "\n" + marker + '</StdOut></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            + marker + "\n" + ("x" * SUMMARIZER.MAX_DIAGNOSTIC_SCAN_CHARS)
            + '</StdOut></Output></UnitTestResult>',
        )
        for index, result in enumerate(cases):
            with self.subTest(index=index):
                summary, encoded = self._summary(trx(
                    result,
                    definition(
                        "pass" if index == 0 else "fail",
                        "GoymenERP.Tests.SafeDiagnostic",
                        "IgnoredStdOut",
                    ),
                ))
                if index == 0:
                    self.assertEqual([], summary["failureGroups"])
                else:
                    self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
                self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

    def test_streaming_bounds_reject_result_and_metadata_overflow(self) -> None:
        cases = (
            (
                "results",
                trx(
                    '<UnitTestResult testId="one" outcome="Passed" />'
                    '<UnitTestResult testId="two" outcome="Passed" />',
                    definition("one", "GoymenERP.Tests.Bounds", "One")
                    + definition("two", "GoymenERP.Tests.Bounds", "Two"),
                ),
                mock.patch.object(SUMMARIZER, "MAX_TEST_RESULTS", 1),
            ),
            (
                "metadata",
                trx(
                    '<UnitTestResult testId="too-long" outcome="Passed" />',
                    definition("too-long", "X" * 32, "Passes"),
                ),
                mock.patch.object(SUMMARIZER, "MAX_METADATA_CHARS", 16),
            ),
        )
        for name, source, limit_patch in cases:
            with self.subTest(name=name), limit_patch:
                with self.assertRaises(SUMMARIZER.SummaryInputFailure) as failure:
                    self._summary(source)
                self.assertEqual("unparseable_input", failure.exception.category)

    def test_invalid_metadata_and_empty_results_are_rejected(self) -> None:
        with self.assertRaisesRegex(RuntimeError, "40-character"):
            SUMMARIZER.build_summary(
                [], "not-a-head", "b" * 40, "backend", "tests", 50
            )
        with self.assertRaises(SUMMARIZER.SummaryInputFailure) as failure:
            self._summary(trx("", ""))
        self.assertEqual("no_results", failure.exception.category)

    def test_producer_writes_the_existing_success_shape(self) -> None:
        source = trx(
            '<UnitTestResult testId="pass" outcome="Passed" />',
            definition("pass", "GoymenERP.Tests.SafeSummary", "Passes"),
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "result.trx").write_text(source, encoding="utf-8")
            output = root / "summary" / "result.json"

            self.assertEqual(0, SUMMARIZER.produce(self._args(root, output)))
            summary = json.loads(output.read_text(encoding="utf-8"))

        self.assertEqual(
            {"total": 1, "passed": 1, "failed": 0, "skipped": 0},
            summary["totals"],
        )
        self.assertNotIn("diagnostic", summary)

    def test_producer_writes_only_fixed_bounded_diagnostics_and_still_fails(self) -> None:
        cases = (
            ("input_count", None, None),
            ("unparseable_input", "<secret>DO_NOT_EXPORT", None),
            (
                "unparseable_input",
                '<!DOCTYPE TestRun [<!ENTITY secret "DO_NOT_EXPORT">]>'
                + trx(
                    '<UnitTestResult testId="pass" outcome="Passed" />',
                    definition("pass", "GoymenERP.Tests.SafeSummary", "Passes"),
                ),
                None,
            ),
            ("no_results", trx("", ""), None),
            ("oversized_input", "xx", 1),
        )
        for expected_category, source, maximum_bytes in cases:
            with self.subTest(category=expected_category), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                if source is not None:
                    (root / "private-secret-name.trx").write_text(
                        source, encoding="utf-8"
                    )
                output = root / "summary" / "result.json"
                size_patch = (
                    mock.patch.object(SUMMARIZER, "MAX_TRX_BYTES", maximum_bytes)
                    if maximum_bytes is not None
                    else mock.patch.object(
                        SUMMARIZER, "MAX_TRX_BYTES", SUMMARIZER.MAX_TRX_BYTES
                    )
                )

                with size_patch:
                    self.assertEqual(1, SUMMARIZER.produce(self._args(root, output)))
                encoded = output.read_text(encoding="utf-8")
                summary = json.loads(encoded)

                self.assertIsNone(summary["totals"])
                self.assertEqual([], summary["failureGroups"])
                self.assertEqual(expected_category, summary["diagnostic"]["category"])
                self.assertEqual(summary["trxFileCount"], summary["diagnostic"]["trxFileCount"])
                for forbidden in (
                    "DO_NOT_EXPORT",
                    "private-secret-name",
                    str(root),
                    "secret",
                    "Traceback",
                ):
                    self.assertNotIn(forbidden, encoded)

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

    @staticmethod
    def _args(trx_directory: Path, output: Path):
        return type(
            "Arguments",
            (),
            {
                "trx_directory": trx_directory,
                "output": output,
                "source_head": "a" * 40,
                "tested_commit": "b" * 40,
                "job": "relational",
                "phase": "sql-tests",
                "failure_group_limit": 50,
            },
        )()


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
