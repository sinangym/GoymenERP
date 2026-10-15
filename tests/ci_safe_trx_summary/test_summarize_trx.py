from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import re
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
REVIEWED_CONTINUOUS_INTEGRATION_WORKFLOW_FINGERPRINT = (
    "8f852a4759a1a273900929138d3c0a36ca36718572927dd7eb8aa360ce74373d"
)
REVIEWED_RECOVERY_SURFACE_INVENTORY_JOB_FINGERPRINT = (
    "cd305f39ce3483947044ca1a28bb9ce9deea69c820e5b7f7d1a00e1777e7234d"
)


def reviewed_source_fingerprint(source: str) -> str:
    normalized = "\n".join(
        line.rstrip()
        for line in source.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    ).rstrip("\n") + "\n"
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


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
                "CI_SAFE_DIAGNOSTIC:acc_nj_sql_startup_bankseed",
                "acc_nj_sql_startup_bankseed",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_nj_sql_receipt_prepare",
                "acc_nj_sql_receipt_prepare",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_nj_sql_receipt_record",
                "acc_nj_sql_receipt_record",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_nj_sql_receipt_replay_freshread_counts",
                "acc_nj_sql_receipt_replay_freshread_counts",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_nj_sql_catalog_prepare_activate",
                "acc_nj_sql_catalog_prepare_activate",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_nj_sql_journal_create_replay_approve",
                "acc_nj_sql_journal_create_replay_approve",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_nj_sql_trial_balance",
                "acc_nj_sql_trial_balance",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_catalog_down_setup",
                "acc_catalog_down_setup",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_catalog_down_refusal",
                "acc_catalog_down_refusal",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:acc_catalog_down_empty_down_up",
                "acc_catalog_down_empty_down_up",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_setup",
                "product_technical_release_rollback_setup",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_reference",
                "product_technical_release_rollback_replay_reference",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_prerequisite",
                "product_technical_release_rollback_replay_prerequisite",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_terminal_insert",
                "product_technical_release_rollback_replay_terminal_insert",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_refusal",
                "product_technical_release_rollback_replay_refusal",
            ),
            (
                "CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_lifetime",
                "product_technical_release_rollback_lifetime",
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

    def test_failed_stdout_emits_only_closed_manufacturing_phases(self) -> None:
        codes = (
            "mfg_customer_arrange",
            "mfg_customer_issue",
            "mfg_customer_return",
            "mfg_customer_output",
            "mfg_customer_replay",
            "mfg_customer_assert",
        )
        for code in codes:
            with self.subTest(code=code):
                source = trx(
                    '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
                    + "CI_SAFE_DIAGNOSTIC:" + code
                    + "\ncredential=DO_NOT_EXPORT"
                    + '</StdOut></Output></UnitTestResult>',
                    definition("fail", "GoymenERP.Tests.SafeDiagnostic", "MfgStdOut"),
                )

                summary, encoded = self._summary(source)

                self.assertEqual(code, summary["failureGroups"][0]["diagnosticCode"])
                for forbidden in ("DO_NOT_EXPORT", "credential", "CI_SAFE_DIAGNOSTIC"):
                    self.assertNotIn(forbidden, encoded)

    def test_manufacturing_stdout_markers_remain_closed_on_unsafe_paths(self) -> None:
        cases = (
            '<UnitTestResult testId="fail" outcome="Failed"><Output><Wrapper><StdOut>'
            'CI_SAFE_DIAGNOSTIC:mfg_customer_arrange'
            '</StdOut></Wrapper></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:mfg_customer_issue\n'
            'CI_SAFE_DIAGNOSTIC:mfg_customer_issue'
            '</StdOut></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:mfg_customer_<Wrapper>return</Wrapper>'
            '</StdOut></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:mfg_customer_unknown'
            '</StdOut></Output></UnitTestResult>',
        )
        for index, result in enumerate(cases):
            with self.subTest(index=index):
                summary, encoded = self._summary(trx(
                    result,
                    definition("fail", "GoymenERP.Tests.SafeDiagnostic", "ClosedMfgStdOut"),
                ))

                self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
                self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

    def test_finance_work_legacy_markers_are_closed_and_bound_to_the_exact_test(self) -> None:
        codes = (
            "finance_work_legacy_start",
            "finance_work_legacy_seed",
            "finance_work_legacy_up",
            "finance_work_legacy_replay",
            "finance_work_legacy_counts",
            "finance_work_legacy_down_up",
            "finance_work_legacy_preview_validation",
            "finance_work_legacy_serialization",
            "finance_work_legacy_batch_insert",
            "finance_work_legacy_command_insert",
        )
        class_name = (
            "GoymenERP.Api.IntegrationTests."
            "FinanceOfficialInvoiceImportWorkDeliverySqlServerRelationalAcceptanceTests"
        )
        method_name = (
            "Legacy_previous_migration_receipt_replays_after_up_without_fabricating_work_history"
        )
        self.assertEqual(
            frozenset(codes), SUMMARIZER.FINANCE_WORK_LEGACY_DIAGNOSTIC_CODES
        )
        self.assertEqual(
            f"{class_name}.{method_name}", SUMMARIZER.FINANCE_WORK_LEGACY_TEST_FQN
        )

        for code in codes:
            with self.subTest(code=code):
                marker = f"CI_SAFE_DIAGNOSTIC:{code}"
                source = trx(
                    '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
                    + marker
                    + "\ncredential=DO_NOT_EXPORT"
                    + '</StdOut></Output></UnitTestResult>',
                    definition("fail", class_name, method_name),
                )
                summary, encoded = self._summary(source)
                self.assertEqual(code, summary["failureGroups"][0]["diagnosticCode"])
                for forbidden in ("DO_NOT_EXPORT", "credential", "CI_SAFE_DIAGNOSTIC"):
                    self.assertNotIn(forbidden, encoded)

                wrong_test = trx(
                    '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
                    + marker
                    + '</StdOut></Output></UnitTestResult>',
                    definition("fail", class_name, "Other_test"),
                )
                wrong_summary, wrong_encoded = self._summary(wrong_test)
                self.assertNotIn("diagnosticCode", wrong_summary["failureGroups"][0])
                self.assertNotIn(code, wrong_encoded)

        rejected = (
            (
                "unknown",
                "Failed",
                "<Output><StdOut>CI_SAFE_DIAGNOSTIC:finance_work_legacy_unknown</StdOut></Output>",
            ),
            (
                "duplicate",
                "Failed",
                "<Output><StdOut>CI_SAFE_DIAGNOSTIC:finance_work_legacy_batch_insert\n"
                "CI_SAFE_DIAGNOSTIC:finance_work_legacy_batch_insert</StdOut></Output>",
            ),
            (
                "wrong path",
                "Failed",
                "<Output><Wrapper><StdOut>CI_SAFE_DIAGNOSTIC:finance_work_legacy_command_insert"
                "</StdOut></Wrapper></Output>",
            ),
            (
                "nested stdout content",
                "Failed",
                "<Output><StdOut>CI_SAFE_DIAGNOSTIC:finance_work_legacy_"
                "<Wrapper>serialization</Wrapper></StdOut></Output>",
            ),
            (
                "success",
                "Passed",
                "<Output><StdOut>CI_SAFE_DIAGNOSTIC:finance_work_legacy_start"
                "</StdOut></Output>",
            ),
        )
        for label, outcome, output in rejected:
            with self.subTest(rejection=label):
                source = trx(
                    f'<UnitTestResult testId="case" outcome="{outcome}">'
                    + output
                    + "</UnitTestResult>",
                    definition("case", class_name, method_name),
                )
                summary, encoded = self._summary(source)
                for group in summary["failureGroups"]:
                    self.assertNotIn("diagnosticCode", group)
                self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)
                self.assertNotIn("finance_work_legacy_", encoded)

    def test_finance_work_legacy_message_markers_are_never_published(self) -> None:
        class_name = (
            "GoymenERP.Api.IntegrationTests."
            "FinanceOfficialInvoiceImportWorkDeliverySqlServerRelationalAcceptanceTests"
        )
        method_name = (
            "Legacy_previous_migration_receipt_replays_after_up_without_fabricating_work_history"
        )
        for code in (
            "finance_work_legacy_start",
            "finance_work_legacy_seed",
            "finance_work_legacy_up",
            "finance_work_legacy_replay",
            "finance_work_legacy_counts",
            "finance_work_legacy_down_up",
            "finance_work_legacy_preview_validation",
            "finance_work_legacy_serialization",
            "finance_work_legacy_batch_insert",
            "finance_work_legacy_command_insert",
        ):
            with self.subTest(code=code):
                source = trx(
                    '<UnitTestResult testId="fail" outcome="Failed"><Output>'
                    '<ErrorInfo><Message>CI_SAFE_DIAGNOSTIC:'
                    + code
                    + ' credential=DO_NOT_EXPORT</Message></ErrorInfo>'
                    '</Output></UnitTestResult>',
                    definition("fail", class_name, method_name),
                )
                summary, encoded = self._summary(source)
                self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
                for forbidden in (code, "DO_NOT_EXPORT", "credential", "CI_SAFE_DIAGNOSTIC"):
                    self.assertNotIn(forbidden, encoded)

    def test_finance_work_legacy_message_invalidates_mixed_channel_markers(self) -> None:
        class_name = (
            "GoymenERP.Api.IntegrationTests."
            "FinanceOfficialInvoiceImportWorkDeliverySqlServerRelationalAcceptanceTests"
        )
        method_name = (
            "Legacy_previous_migration_receipt_replays_after_up_without_fabricating_work_history"
        )
        for stdout_code in (
            "finance_work_legacy_start",
            "acc_nj_sql_trial_balance",
        ):
            with self.subTest(stdout_code=stdout_code):
                source = trx(
                    '<UnitTestResult testId="fail" outcome="Failed"><Output>'
                    '<ErrorInfo><Message>CI_SAFE_DIAGNOSTIC:finance_work_legacy_seed'
                    '</Message></ErrorInfo><StdOut>CI_SAFE_DIAGNOSTIC:'
                    + stdout_code
                    + '</StdOut></Output></UnitTestResult>',
                    definition("fail", class_name, method_name),
                )
                summary, encoded = self._summary(source)
                self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
                self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)
                self.assertNotIn("finance_work_legacy_", encoded)

    def test_new_finance_stdout_markers_remain_closed_on_unsafe_paths(self) -> None:
        cases = (
            '<UnitTestResult testId="fail" outcome="Failed"><Output><Wrapper><StdOut>'
            'CI_SAFE_DIAGNOSTIC:acc_nj_sql_startup_bankseed'
            '</StdOut></Wrapper></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:acc_catalog_down_refusal\n'
            'CI_SAFE_DIAGNOSTIC:acc_catalog_down_refusal'
            '</StdOut></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:acc_nj_sql_receipt_<Wrapper>replay_</Wrapper>'
            'freshread_counts</StdOut></Output></UnitTestResult>',
        )
        for index, result in enumerate(cases):
            with self.subTest(index=index):
                summary, encoded = self._summary(trx(
                    result,
                    definition(
                        "fail", "GoymenERP.Tests.SafeDiagnostic", "ClosedFinanceStdOut"
                    ),
                ))

                self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
                self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

    def test_new_product_stdout_markers_remain_closed_on_unsafe_paths(self) -> None:
        cases = (
            '<UnitTestResult testId="fail" outcome="Failed"><Output><Wrapper><StdOut>'
            'CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_prerequisite'
            '</StdOut></Wrapper></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_refusal\n'
            'CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_refusal'
            '</StdOut></Output></UnitTestResult>',
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:product_technical_release_rollback_replay_'
            '<Wrapper>terminal_</Wrapper>insert'
            '</StdOut></Output></UnitTestResult>',
        )
        for index, result in enumerate(cases):
            with self.subTest(index=index):
                summary, encoded = self._summary(trx(
                    result,
                    definition(
                        "fail", "GoymenERP.Tests.SafeDiagnostic", "ClosedProductStdOut"
                    ),
                ))

                self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
                self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

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

    def test_stdout_with_an_unexpected_child_emits_no_diagnostic(self) -> None:
        source = trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:acc_nj_sql_<Wrapper>trial_</Wrapper>balance'
            '</StdOut></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "ChildXml"),
        )

        summary, encoded = self._summary(source)

        self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
        self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

    def test_stdout_overflow_invalidates_an_earlier_message_diagnostic(self) -> None:
        message_marker = "CI_SAFE_DIAGNOSTIC:product_price_list_startup_sql_208"
        stdout_marker = "CI_SAFE_DIAGNOSTIC:acc_nj_sql_trial_balance\n"
        source = trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output>'
            '<ErrorInfo><Message>' + message_marker + '</Message></ErrorInfo>'
            '<StdOut>' + stdout_marker
            + ("x" * SUMMARIZER.MAX_DIAGNOSTIC_SCAN_CHARS)
            + '</StdOut></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "OverflowAcrossChannels"),
        )

        summary, encoded = self._summary(source)

        self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
        self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

    def test_ambiguous_stdout_invalidates_a_later_message_diagnostic(self) -> None:
        stdout_marker = "CI_SAFE_DIAGNOSTIC:acc_nj_sql_trial_balance"
        message_marker = "CI_SAFE_DIAGNOSTIC:product_price_list_startup_sql_208"
        source = trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            + stdout_marker + "\n" + stdout_marker
            + '</StdOut><ErrorInfo><Message>' + message_marker
            + '</Message></ErrorInfo></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "AmbiguousAcrossChannels"),
        )

        summary, encoded = self._summary(source)

        self.assertNotIn("diagnosticCode", summary["failureGroups"][0])
        self.assertNotIn("CI_SAFE_DIAGNOSTIC", encoded)

    def test_conflicting_message_and_stdout_markers_emit_no_diagnostic(self) -> None:
        source = trx(
            '<UnitTestResult testId="fail" outcome="Failed"><Output><StdOut>'
            'CI_SAFE_DIAGNOSTIC:acc_nj_sql_trial_balance'
            '</StdOut><ErrorInfo><Message>'
            'CI_SAFE_DIAGNOSTIC:product_price_list_startup_sql_208'
            '</Message></ErrorInfo></Output></UnitTestResult>',
            definition("fail", "GoymenERP.Tests.SafeDiagnostic", "ConflictingChannels"),
        )

        summary, encoded = self._summary(source)

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
        self.assert_ci_workflow_summary_contract(workflow)

    def assert_ci_workflow_summary_contract(self, workflow: str) -> None:
        self.assertEqual(
            REVIEWED_CONTINUOUS_INTEGRATION_WORKFLOW_FINGERPRINT,
            reviewed_source_fingerprint(workflow),
            "CI workflow left its complete reviewed root and job execution context",
        )
        head_ref = "${{ github.event.pull_request.head.sha || github.sha }}"
        shard_ref = "${{ matrix.shard }}"
        needs_shard = "${{ needs.relational-shard.result }}"
        needs_rcsi = "${{ needs.relational-rcsi.result }}"
        job_markers = list(re.finditer(r"(?m)^jobs:\n", workflow))
        self.assertEqual(len(job_markers), 1)
        jobs_source = workflow[job_markers[0].end():]
        jobs = {
            match.group(1): match.group(2)
            for match in re.finditer(
                r"(?ms)^  ([A-Za-z0-9_-]+):\n(.*?)(?=^  [A-Za-z0-9_-]+:\n|\Z)",
                jobs_source,
            )
        }
        required_jobs = {
            "backend-build",
            "backend-test-group",
            "backend",
            "frontend",
            "relational-shard",
            "relational-rcsi",
            "relational",
            "contract-drift",
            "containers",
            "recovery-surface-inventory",
        }
        self.assertEqual(required_jobs, set(jobs))
        self._assert_recovery_surface_inventory_job_contract(
            jobs["recovery-surface-inventory"]
        )

        summaries = {
            "backend-build": (
                "Produce secret-free backend test summary",
                "Upload secret-free backend test summary",
                "artifacts/ci-test-results/backend",
                "artifacts/ci-safe-test-summary/backend.json",
                "artifacts/ci-safe-test-summary/backend.json",
                "ci-test-summary-backend-" + head_ref,
                "artifacts/ci-test-results/backend/**/*.trx",
                "test-suite",
            ),
            "backend-test-group": (
                "Produce secret-free backend group summary",
                "Upload secret-free backend group summary",
                '"artifacts/ci-test-results/backend-groups/${{ matrix.group }}"',
                '"artifacts/ci-safe-test-summary/backend-${{ matrix.group }}.json"',
                "artifacts/ci-safe-test-summary/backend-${{ matrix.group }}.json",
                "ci-test-summary-backend-group-${{ matrix.group }}-" + head_ref,
                "artifacts/ci-test-results/backend-groups/*/*.trx",
                "test-suite",
            ),
            "relational-shard": (
                "Produce secret-free relational shard summary",
                "Upload secret-free relational shard summary",
                "artifacts/ci-test-results/relational",
                f'"artifacts/ci-safe-test-summary/shard-{shard_ref}.json"',
                f"artifacts/ci-safe-test-summary/shard-{shard_ref}.json",
                f"ci-test-summary-relational-shard-{shard_ref}-{head_ref}",
                "artifacts/ci-test-results/relational/*.trx",
                "sql-acceptance",
            ),
            "relational-rcsi": (
                "Produce secret-free RCSI test summary",
                "Upload secret-free RCSI test summary",
                "artifacts/ci-test-results/relational",
                "artifacts/ci-safe-test-summary/relational.json",
                "artifacts/ci-safe-test-summary/relational.json",
                "ci-test-summary-relational-rcsi-" + head_ref,
                "artifacts/ci-test-results/relational/*.trx",
                "sql-acceptance",
            ),
        }
        for job_name, (
            producer_name,
            upload_name,
            trx_directory,
            output_argument,
            upload_path,
            artifact_name,
            trx_pattern,
            phase,
        ) in summaries.items():
            try:
                body = jobs[job_name]
                self.assertEqual(body.count("tools/ci/summarize_trx.py"), 1)
                self.assertEqual(
                    self._workflow_step_lines(body, producer_name),
                    [
                        f"if: always() && hashFiles('{trx_pattern}') != ''",
                        "run: |",
                        "python3 -I -S -B tools/ci/summarize_trx.py \\",
                        f"--trx-directory {trx_directory} \\",
                        f"--output {output_argument} \\",
                        f'--source-head "{head_ref}" \\',
                        '--tested-commit "$(git rev-parse HEAD)" \\',
                        f"--job {job_name} \\",
                        f"--phase {phase}",
                    ],
                )
                upload_glob = (
                    "artifacts/ci-safe-test-summary/shard-*.json"
                    if job_name == "relational-shard"
                    else "artifacts/ci-safe-test-summary/backend-*.json"
                    if job_name == "backend-test-group"
                    else upload_path
                )
                self.assertEqual(
                    self._workflow_step_lines(body, upload_name),
                    [
                        f"if: always() && hashFiles('{upload_glob}') != ''",
                        "uses: actions/upload-artifact@v7",
                        "with:",
                        f"name: {artifact_name}",
                        f"path: {upload_path}",
                        "if-no-files-found: error",
                        "retention-days: 7",
                    ],
                )
                self.assertEqual(
                    body.count("actions/upload-artifact@v7"),
                    2 if job_name in ("relational-shard", "backend-test-group") else 1,
                )
            except AssertionError as error:
                raise AssertionError(f"{job_name}: {error}") from error

        backend_worker = jobs["backend-test-group"]
        self.assertEqual(
            self._workflow_step_lines(
                backend_worker, "Upload exact backend group proof"
            ),
            [
                "uses: actions/upload-artifact@v7",
                "with:",
                "name: ci-backend-test-group-proof-${{ matrix.group }}",
                "path: artifacts/ci-backend-group-proofs/proof-${{ matrix.group }}.json",
                "if-no-files-found: error",
                "retention-days: 7",
            ],
        )
        self.assertIn(
            '--results-directory "artifacts/ci-test-results/backend-groups/${{ matrix.group }}" \\',
            self._workflow_step_lines(backend_worker, "Run exact backend test group"),
        )
        shard = jobs["relational-shard"]
        self.assertEqual(
            self._workflow_step_lines(shard, "Upload exact SQL shard proof"),
            [
                "uses: actions/upload-artifact@v7",
                "with:",
                f"name: ci-relational-shard-proof-{shard_ref}",
                f"path: artifacts/ci-relational-proofs/proof-{shard_ref}.json",
                "if-no-files-found: error",
                "retention-days: 7",
            ],
        )
        self.assertIn(
            f'--trx-output "artifacts/ci-test-results/relational/broad-{shard_ref}.trx" \\',
            self._workflow_step_lines(shard, "Run exact SQL Server relational shard"),
        )
        self.assertIn(
            "--trx-output artifacts/ci-test-results/relational/rcsi.trx",
            self._workflow_step_lines(
                jobs["relational-rcsi"],
                "Run exact RCSI relational acceptance verifier",
            ),
        )
        self.assertNotIn(
            "--trx-output artifacts/ci-test-results/relational/broad.trx",
            workflow,
        )

        aggregate = jobs["relational"]
        self.assertEqual(
            re.findall(r"^      - name: (.+)$", aggregate, re.M),
            [
                "Verify relational shard proof parser",
                "Download exact SQL shard proofs",
                "Verify complete SQL Server relational acceptance",
            ],
        )
        self.assertEqual(
            self._workflow_step_lines(aggregate, "Download exact SQL shard proofs"),
            [
                "uses: actions/download-artifact@v8",
                "with:",
                "pattern: ci-relational-shard-proof-*",
                "path: artifacts/ci-relational-proofs",
            ],
        )
        self.assertEqual(
            self._workflow_step_lines(
                aggregate, "Verify complete SQL Server relational acceptance"
            ),
            [
                "run: |",
                "python3 -I -S -B tools/verify_rcsi_relational_acceptance.py \\",
                "--aggregate \\",
                "--proof-directory artifacts/ci-relational-proofs \\",
                f'--source-head "{head_ref}" \\',
                '--tested-commit "$(git rev-parse HEAD)" \\',
                f'--worker-result "{needs_shard}" \\',
                f'--rcsi-result "{needs_rcsi}"',
            ],
        )
        for job_name, body in jobs.items():
            if job_name not in summaries:
                self.assertNotIn("tools/ci/summarize_trx.py", body)
                self.assertNotIn("actions/upload-artifact@v7", body)
        self.assertEqual(workflow.count("tools/ci/summarize_trx.py"), 4)
        self.assertEqual(workflow.count("actions/upload-artifact@v7"), 6)
        self.assertNotRegex(workflow, r"(?m)^\s*path:\s*[^\n]*ci-test-results")
        self.assertNotRegex(workflow, r"(?m)^\s*path:\s*[^\n]*\.trx\b")
        self.assertIn("permissions:\n  contents: read", workflow)
        self.assertNotIn("continue-on-error", workflow)

    def test_recovery_surface_inventory_job_is_artifact_free_and_fail_closed(self) -> None:
        workflow = (REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml").read_text(
            encoding="utf-8"
        )
        self.assert_ci_workflow_summary_contract(workflow)

        deleted = self._workflow_without_job(workflow, "recovery-surface-inventory")
        with self.assertRaises(AssertionError):
            self.assert_ci_workflow_summary_contract(deleted)

        renamed = workflow.replace(
            "  recovery-surface-inventory:\n",
            "  recovery-surface-inventory-renamed:\n",
            1,
        )
        self.assertNotEqual(renamed, workflow)
        with self.assertRaises(AssertionError):
            self.assert_ci_workflow_summary_contract(renamed)

        safe_job = self._workflow_job_body(workflow, "recovery-surface-inventory")

        inherited_syntax_only_shell = workflow.replace(
            "permissions:\n  contents: read\n\nconcurrency:\n",
            "permissions:\n"
            "  contents: read\n\n"
            "defaults:\n"
            "  run:\n"
            "    shell: bash -n {0}\n\n"
            "concurrency:\n",
            1,
        )
        self.assertNotEqual(inherited_syntax_only_shell, workflow)
        self.assertEqual(
            safe_job,
            self._workflow_job_body(
                inherited_syntax_only_shell, "recovery-surface-inventory"
            ),
        )
        self._assert_recovery_surface_inventory_job_contract(
            self._workflow_job_body(
                inherited_syntax_only_shell, "recovery-surface-inventory"
            )
        )
        with self.assertRaises(AssertionError):
            self.assert_ci_workflow_summary_contract(inherited_syntax_only_shell)

        no_op_job = (
            "    name: Recovery surface inventory exact-source proof\n"
            "    runs-on: ubuntu-latest\n"
            "    steps:\n"
            "      - run: echo ok\n\n"
        )
        with self.assertRaises(AssertionError):
            self.assert_ci_workflow_summary_contract(
                self._workflow_with_job_body(
                    workflow, "recovery-surface-inventory", no_op_job
                )
            )

        proof_mutations = (
            ("checkout action", "actions/checkout@v7", "actions/checkout@v6"),
            ("shallow checkout", "fetch-depth: 1", "fetch-depth: 0"),
            (
                "persisted credentials",
                "persist-credentials: false",
                "persist-credentials: true",
            ),
            (
                "exact checkout ref",
                "ref: ${{ github.event.pull_request.head.sha || github.sha }}",
                "ref: main",
            ),
            (
                "proof working directory",
                "working-directory: ${{ github.workspace }}",
                "working-directory: .",
            ),
            (
                "proof executing shell",
                "shell: /usr/bin/env -u BASH_ENV -u ENV /bin/bash --noprofile --norc -p -euo pipefail -- {0}",
                "shell: bash -n {0}",
            ),
            (
                "reviewed executable path",
                'export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"',
                'export PATH="$PATH"',
            ),
            (
                "ambient Git redirect clearing",
                "GIT_OBJECT_DIRECTORY \\",
                "GIT_OBJECT_DIRECTORY_DISABLED \\",
            ),
            (
                "ambient Python redirect clearing",
                "PYTHONPATH \\",
                "PYTHONPATH_DISABLED \\",
            ),
            (
                "replace-object defense",
                'GIT_NO_REPLACE_OBJECTS: "1"',
                'GIT_NO_REPLACE_OBJECTS: "0"',
            ),
            (
                "source commit binding",
                "SOURCE_COMMIT: ${{ github.event.pull_request.head.sha || github.sha }}",
                "SOURCE_COMMIT: ${{ github.sha }}",
            ),
            (
                "source commit shape",
                "grep -E '^[0-9a-f]{40}$'",
                "grep -E '^[0-9a-f]{39}$'",
            ),
            (
                "checked out HEAD equality",
                'test "$(git rev-parse HEAD)" = "$SOURCE_COMMIT"',
                'test "$(git rev-parse HEAD^)" = "$SOURCE_COMMIT"',
            ),
            (
                "target suite command",
                "python3 -I -S -B -m unittest discover \\",
                "python3 -I -S -B -m unittest \\",
            ),
            (
                "target suite path",
                "-s tests/recovery_surface_inventory \\",
                "-s tests \\",
            ),
            (
                "target suite pattern",
                '-p "test_*.py" \\',
                '-p "other_*.py" \\',
            ),
            (
                "immutable generator extraction",
                'git cat-file blob "$SOURCE_COMMIT:$generator_path" > "$generator_temp"',
                'git show "$SOURCE_COMMIT:$generator_path" > "$generator_temp"',
            ),
            (
                "generator blob equality",
                'test "$(git hash-object "$generator_temp")" = "$generator_blob"',
                'test "$(git hash-object "$generator_temp")" != "$generator_blob"',
            ),
            (
                "extracted generator execution",
                'python3 -I -S -B "$generator_temp" \\',
                'python3 -I -S -B "$generator_path" \\',
            ),
            (
                "repository root argument",
                '--repository-root "$GITHUB_WORKSPACE" \\',
                '--repository-root "." \\',
            ),
            (
                "source commit argument",
                '--source-commit "$SOURCE_COMMIT" \\',
                '--source-commit "$(git rev-parse HEAD)" \\',
            ),
            (
                "JSON validation",
                'python3 -I -S -B -m json.tool "$inventory_temp" > /dev/null',
                'cat "$inventory_temp" > /dev/null',
            ),
        )
        for label, original, replacement in proof_mutations:
            with self.subTest(mutant=label):
                changed_job = safe_job.replace(original, replacement, 1)
                self.assertNotEqual(changed_job, safe_job)
                changed_workflow = self._workflow_with_job_body(
                    workflow, "recovery-surface-inventory", changed_job
                )
                with self.assertRaises(AssertionError):
                    self.assert_ci_workflow_summary_contract(changed_workflow)

        execution_bypass_mutations = (
            (
                "unreviewed executable line added",
                "          set -euo pipefail\n",
                "          set -euo pipefail\n          echo bypass\n",
            ),
            (
                "reviewed executable line removed",
                "          trap cleanup EXIT\n",
                "",
            ),
            (
                "reviewed executable lines reordered",
                '          generator_path="tools/generate_recovery_surface_inventory.py"\n'
                '          generator_blob="$(git rev-parse "$SOURCE_COMMIT:$generator_path")"\n',
                '          generator_blob="$(git rev-parse "$SOURCE_COMMIT:$generator_path")"\n'
                '          generator_path="tools/generate_recovery_surface_inventory.py"\n',
            ),
            (
                "errexit disabled",
                "          set -euo pipefail\n",
                "          set +e\n",
            ),
            (
                "python executable overridden",
                "          python3 -I -S -B -m unittest discover ",
                "          python3() { :; }\n"
                "          python3 -I -S -B -m unittest discover ",
            ),
            (
                "successful early exit",
                "          set -euo pipefail\n",
                "          set -euo pipefail\n          exit 0\n",
            ),
            (
                "successful early return",
                "          set -euo pipefail\n",
                "          set -euo pipefail\n          return 0\n",
            ),
            (
                "successful exec bypass",
                "          set -euo pipefail\n",
                "          set -euo pipefail\n          exec true\n",
            ),
            (
                "absolute successful exec bypass",
                "          set -euo pipefail\n",
                "          set -euo pipefail\n          exec /bin/true\n",
            ),
            (
                "evaluated successful exit",
                "          set -euo pipefail\n",
                "          set -euo pipefail\n          eval 'exit 0'\n",
            ),
            (
                "job condition disabled",
                "    runs-on: ubuntu-latest\n",
                "    runs-on: ubuntu-latest\n    if: false\n",
            ),
            (
                "proof step condition disabled",
                "      - name: Verify recovery inventory exact event source\n",
                "      - name: Verify recovery inventory exact event source\n"
                "        if: false\n",
            ),
            (
                "proof step syntax-only shell",
                "      - name: Verify recovery inventory exact event source\n",
                "      - name: Verify recovery inventory exact event source\n"
                "        shell: bash -n {0}\n",
            ),
            (
                "job default non-executing shell",
                "    steps:\n",
                "    defaults:\n"
                "      run:\n"
                "        shell: bash -n {0}\n"
                "    steps:\n",
            ),
        )
        for label, original, replacement in execution_bypass_mutations:
            with self.subTest(mutant=label):
                changed_job = safe_job.replace(original, replacement, 1)
                self.assertNotEqual(changed_job, safe_job)
                changed_workflow = self._workflow_with_job_body(
                    workflow, "recovery-surface-inventory", changed_job
                )
                with self.assertRaises(AssertionError):
                    self.assert_ci_workflow_summary_contract(changed_workflow)

        step_mutations = (
            (
                "artifact upload",
                "      - uses: actions/upload-artifact@v7\n",
            ),
            (
                "artifact download",
                "      - uses: actions/download-artifact@v8\n",
            ),
            (
                "TRX summary producer",
                "      - run: python3 -I -S -B tools/ci/summarize_trx.py\n",
            ),
            (
                "deployment command",
                "      - run: ./deploy.sh\n",
            ),
            (
                "secret context",
                "      - run: echo '${{ secrets.RECOVERY_TOKEN }}'\n",
            ),
        )
        job_mutations = (
            (
                "continue on error",
                "    continue-on-error: true\n",
            ),
            (
                "write permission",
                "    permissions:\n      contents: write\n",
            ),
            (
                "deployment environment",
                "    environment: production\n",
            ),
            (
                "unrelated job dependency",
                "    needs: backend-build\n",
            ),
        )
        for label, addition in step_mutations:
            with self.subTest(mutant=label):
                changed_job = safe_job.replace(
                    "    steps:\n",
                    "    steps:\n" + addition,
                    1,
                )
                self.assertNotEqual(changed_job, safe_job)
                changed_workflow = self._workflow_with_job_body(
                    workflow, "recovery-surface-inventory", changed_job
                )
                with self.assertRaises(AssertionError):
                    self.assert_ci_workflow_summary_contract(changed_workflow)
        for label, addition in job_mutations:
            with self.subTest(mutant=label):
                changed_job = safe_job.replace("    steps:\n", addition + "    steps:\n", 1)
                self.assertNotEqual(changed_job, safe_job)
                changed_workflow = self._workflow_with_job_body(
                    workflow, "recovery-surface-inventory", changed_job
                )
                with self.assertRaises(AssertionError):
                    self.assert_ci_workflow_summary_contract(changed_workflow)

    def test_ci_workflow_rejects_unsafe_or_mismatched_uploads(self) -> None:
        workflow = (REPOSITORY_ROOT / ".github" / "workflows" / "ci.yml").read_text(
            encoding="utf-8"
        )
        mutations = (
            ("missing head", '--source-head "', '--missing-source-head "'),
            (
                "mismatched summary upload",
                "path: artifacts/ci-safe-test-summary/backend.json",
                "path: artifacts/ci-safe-test-summary/relational.json",
            ),
            (
                "raw TRX upload",
                "path: artifacts/ci-safe-test-summary/backend.json",
                "path: artifacts/ci-test-results/backend/**/*.trx",
            ),
            (
                "directory upload",
                "path: artifacts/ci-safe-test-summary/backend.json",
                "path: artifacts/ci-safe-test-summary/",
            ),
            (
                "proof passed as summary",
                "path: artifacts/ci-safe-test-summary/shard-${{ matrix.shard }}.json",
                "path: artifacts/ci-relational-proofs/proof-${{ matrix.shard }}.json",
            ),
            (
                "backend proof passed as summary",
                "path: artifacts/ci-safe-test-summary/backend-${{ matrix.group }}.json",
                "path: artifacts/ci-backend-group-proofs/proof-${{ matrix.group }}.json",
            ),
            (
                "wrong producer identity",
                "--job relational-shard \\",
                "--job relational \\",
            ),
        )
        for label, original, replacement in mutations:
            with self.subTest(mutant=label):
                changed = workflow.replace(original, replacement, 1)
                self.assertNotEqual(changed, workflow)
                with self.assertRaises(AssertionError):
                    self.assert_ci_workflow_summary_contract(changed)

        extra_upload = (
            "      - name: Unknown upload\n"
            "        uses: actions/upload-artifact@v7\n"
            "        with:\n"
            "          path: artifacts/ci-safe-test-summary/\n\n"
        )
        changed = workflow.replace(
            "  contract-drift:\n", extra_upload + "  contract-drift:\n", 1
        )
        self.assertNotEqual(changed, workflow)
        with self.assertRaises(AssertionError):
            self.assert_ci_workflow_summary_contract(changed)

    def _assert_recovery_surface_inventory_job_contract(self, job: str) -> None:
        checkout = (
            "    steps:\n"
            "      - uses: actions/checkout@v7\n"
            "        with:\n"
            "          fetch-depth: 1\n"
            "          persist-credentials: false\n"
            "          ref: ${{ github.event.pull_request.head.sha || github.sha }}\n"
        )
        self.assertIn(checkout, job)
        self.assertEqual(job.count("actions/checkout@v7"), 1)
        self.assertEqual(
            re.findall(r"^      - name: (.+)$", job, re.M),
            ["Verify recovery inventory exact event source"],
        )
        proof_lines = self._workflow_step_lines(
            job, "Verify recovery inventory exact event source"
        )
        required_lines = (
            "working-directory: ${{ github.workspace }}",
            "shell: /usr/bin/env -u BASH_ENV -u ENV /bin/bash --noprofile --norc -p -euo pipefail -- {0}",
            'GIT_NO_REPLACE_OBJECTS: "1"',
            "SOURCE_COMMIT: ${{ github.event.pull_request.head.sha || github.sha }}",
            "set -euo pipefail",
            'export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"',
            "unset \\",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES \\",
            "GIT_CEILING_DIRECTORIES \\",
            "GIT_COMMON_DIR \\",
            "GIT_CONFIG_COUNT \\",
            "GIT_CONFIG_GLOBAL \\",
            "GIT_CONFIG_PARAMETERS \\",
            "GIT_CONFIG_SYSTEM \\",
            "GIT_DIR \\",
            "GIT_DISCOVERY_ACROSS_FILESYSTEM \\",
            "GIT_EXEC_PATH \\",
            "GIT_GRAFT_FILE \\",
            "GIT_INDEX_FILE \\",
            "GIT_NAMESPACE \\",
            "GIT_OBJECT_DIRECTORY \\",
            "GIT_REPLACE_REF_BASE \\",
            "GIT_SHALLOW_FILE \\",
            "GIT_WORK_TREE \\",
            "PYTHONBREAKPOINT \\",
            "PYTHONHOME \\",
            "PYTHONINSPECT \\",
            "PYTHONPATH \\",
            "PYTHONPLATLIBDIR \\",
            "PYTHONSTARTUP \\",
            "PYTHONUSERBASE \\",
            "PYTHONWARNINGS",
            'test "$SOURCE_COMMIT" = "$(printf \'%s\' "$SOURCE_COMMIT" | grep -E \'^[0-9a-f]{40}$\')"',
            'test "$(git rev-parse HEAD)" = "$SOURCE_COMMIT"',
            'generator_path="tools/generate_recovery_surface_inventory.py"',
            'generator_blob="$(git rev-parse "$SOURCE_COMMIT:$generator_path")"',
            'generator_temp="$(mktemp --suffix=.py)"',
            'inventory_temp="$(mktemp --suffix=.json)"',
            "python3 -I -S -B -m unittest discover \\",
            "-s tests/recovery_surface_inventory \\",
            '-p "test_*.py" \\',
            "-v",
            'git cat-file blob "$SOURCE_COMMIT:$generator_path" > "$generator_temp"',
            'test "$(git hash-object "$generator_temp")" = "$generator_blob"',
            'python3 -I -S -B "$generator_temp" \\',
            '--repository-root "$GITHUB_WORKSPACE" \\',
            '--source-commit "$SOURCE_COMMIT" \\',
            '> "$inventory_temp"',
            'python3 -I -S -B -m json.tool "$inventory_temp" > /dev/null',
        )
        previous_index = -1
        for line in required_lines:
            try:
                current_index = proof_lines.index(line, previous_index + 1)
            except ValueError as error:
                raise AssertionError(
                    f"Missing ordered recovery proof line: {line}"
                ) from error
            previous_index = current_index

        self.assertEqual(
            REVIEWED_RECOVERY_SURFACE_INVENTORY_JOB_FINGERPRINT,
            reviewed_source_fingerprint(job),
            "Recovery inventory proof job left its complete reviewed execution body",
        )

        self.assertNotIn("actions/upload-artifact@", job)
        self.assertNotIn("actions/download-artifact@", job)
        self.assertNotIn("tools/ci/summarize_trx.py", job)
        self.assertNotIn("continue-on-error", job)
        self.assertNotRegex(job, r"(?m)^\s*permissions\s*:")
        self.assertNotRegex(job, r"(?m)^\s*needs\s*:")
        self.assertNotRegex(job, r"(?m)^\s*environment\s*:")
        self.assertNotRegex(job, r"(?i)\bdeploy(?:ment)?\b")
        self.assertNotRegex(job, r"(?i)\bsecrets\b")

    @staticmethod
    def _workflow_job_match(workflow: str, job_name: str) -> re.Match[str]:
        match = re.search(
            rf"(?ms)^  {re.escape(job_name)}:\n"
            r"(.*?)(?=^  [A-Za-z0-9_-]+:\n|\Z)",
            workflow,
        )
        if match is None:
            raise AssertionError(f"Expected workflow job: {job_name}")
        return match

    def _workflow_job_body(self, workflow: str, job_name: str) -> str:
        return self._workflow_job_match(workflow, job_name).group(1)

    def _workflow_without_job(self, workflow: str, job_name: str) -> str:
        match = self._workflow_job_match(workflow, job_name)
        return workflow[:match.start()] + workflow[match.end():]

    def _workflow_with_job_body(
        self, workflow: str, job_name: str, job_body: str
    ) -> str:
        match = self._workflow_job_match(workflow, job_name)
        replacement = f"  {job_name}:\n{job_body}"
        return workflow[:match.start()] + replacement + workflow[match.end():]

    def _workflow_step_lines(self, job: str, step_name: str) -> list[str]:
        matches = list(
            re.finditer(
                rf"(?ms)^      - name: {re.escape(step_name)}\n"
                r"(.*?)(?=^      - (?:name:|uses:|run:)|\Z)",
                job,
            )
        )
        self.assertEqual(len(matches), 1, f"Expected one workflow step: {step_name}")
        return [
            line.strip()
            for line in matches[0].group(1).splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        ]

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
