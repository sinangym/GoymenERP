from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
from xml.sax.saxutils import escape

SPEC = importlib.util.spec_from_file_location(
    "sql_safe_summary", Path(__file__).resolve().parents[2] / "tools/ci/summarize_trx.py"
)
assert SPEC is not None and SPEC.loader is not None
SUMMARY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SUMMARY)
HEADER = "Microsoft.Data.SqlClient.SqlException (0x80131904)"


class SqlErrorDiagnosticTests(unittest.TestCase):
    def summarize(self, message: str, *, outcome: str = "Failed", raw: bool = False, chunk: int = 17):
        content = message if raw else escape(message)
        source = (
            '<TestRun><Results><UnitTestResult testId="one" outcome="' + outcome + '">'
            '<Output><ErrorInfo><Message>' + content + '</Message></ErrorInfo></Output>'
            '</UnitTestResult></Results><TestDefinitions><UnitTest id="one">'
            '<TestMethod className="GoymenERP.Tests.SqlFixture" name="Failure" />'
            '</UnitTest></TestDefinitions></TestRun>'
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.trx"
            path.write_text(source, encoding="utf-8")
            with mock.patch.object(SUMMARY, "STREAM_CHUNK_BYTES", chunk):
                result = SUMMARY.build_summary([path], "a" * 40, "b" * 40, "sql-a", "native-failure", 50)
        encoded = json.dumps(result)
        for private in ("DO_NOT_EXPORT", "private-host", "private-table", "private-path", "987654321987654321"):
            self.assertNotIn(private, encoded)
        self.assertEqual(2, result["schemaVersion"])
        self.assertEqual("not_evaluated", result["jobAcceptance"])
        return result

    def code(self, message: str, **kwargs):
        groups = self.summarize(message, **kwargs)["failureGroups"]
        return groups[0].get("sqlErrorCode") if groups else None

    def test_wrapped_dbupdate_known_provider_record_is_closed_and_chunk_safe(self):
        message = ("Microsoft.EntityFrameworkCore.DbUpdateException: DO_NOT_EXPORT\n ---> " + HEADER
                   + ": private-table private-host C:/private-path\nError Number:468,State:9,Class:16")
        for chunk in (1, 17, 1024 * 1024):
            with self.subTest(chunk=chunk):
                self.assertEqual("sql_error_468", self.code(message, chunk=chunk))

    def test_all_reviewed_codes_are_fixed_categories(self):
        for number in SUMMARY.SQL_ERROR_NUMBERS:
            with self.subTest(number=number):
                expected = "sql_error_timeout" if number == "-2" else "sql_error_" + number
                self.assertEqual(expected, self.code(HEADER + "\nError Number:" + number + ",State:0,Class:16"))

    def test_unknown_malformed_overflow_and_conflicting_records_cannot_export_numbers(self):
        records = (
            "Error Number:987654321987654321,State:0,Class:16",
            "Error Number:12345,State:0,Class:16", "Error Number:0468,State:0,Class:16",
            "Error Number:468,State:256,Class:16", "Error Number:468,State:0,Class:999",
            "Error Number:468,State:0,Class:16\nError Number:207,State:0,Class:16",
            "Error Number:468,State:0,Class:16\nError Number:468,State:0,Class:16",
            "Error Number:468,State:0,Class:16 trailing-private-path",
        )
        for record in records:
            with self.subTest(record=record):
                self.assertEqual("sql_error_unknown", self.code(HEADER + "\n" + record))
        self.assertEqual("sql_error_unknown", self.code(HEADER + "\n" + HEADER + "\nError Number:468,State:0,Class:16"))

    def test_number_without_exact_provider_header_is_ignored(self):
        for header in ("", "System.Data.SqlClient.SqlException (0x80131904)", "Microsoft.Data.SqlClient.SqlException (0xDEADBEEF)"):
            self.assertIsNone(self.code(header + "\nError Number:468,State:0,Class:16"))

    def test_oversized_message_never_certifies_truncated_record(self):
        for message in (HEADER + "\nError Number:468,State:0,Class:16\n" + "x" * 16384,
                        "x" * 16384 + HEADER + "\nError Number:468,State:0,Class:16"):
            self.assertIsNone(self.code(message))

    def test_passed_and_skipped_messages_do_not_produce_failure_diagnostics(self):
        for outcome in ("Passed", "NotExecuted", "Skipped"):
            result = self.summarize(HEADER + "\nError Number:468,State:0,Class:16", outcome=outcome)
            self.assertEqual([], result["failureGroups"])
            self.assertEqual(0, result["totals"]["failed"])

    def test_nested_foreign_error_info_and_message_children_are_ignored(self):
        payload = escape(HEADER + "\nError Number:468,State:0,Class:16")
        for message in ("<Foreign><ErrorInfo><Message>" + payload + "</Message></ErrorInfo></Foreign>",
                        "<Foreign>" + payload + "</Foreign>"):
            self.assertIsNone(self.code(message, raw=True))

    def test_existing_marker_and_sql_projection_coexist_without_changing_counts(self):
        message = "CI_SAFE_DIAGNOSTIC:mfg_customer_arrange\n" + HEADER + "\nError Number:207,State:0,Class:16"
        result = self.summarize(message)
        self.assertEqual({"total": 1, "passed": 0, "failed": 1, "skipped": 0}, result["totals"])
        self.assertEqual("mfg_customer_arrange", result["failureGroups"][0]["diagnosticCode"])
        self.assertEqual("sql_error_207", result["failureGroups"][0]["sqlErrorCode"])


if __name__ == "__main__":
    unittest.main()
