from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
from xml.sax.saxutils import escape

SPEC = importlib.util.spec_from_file_location(
    "assertion_summary", Path(__file__).resolve().parents[2] / "tools/ci/summarize_trx.py"
)
assert SPEC is not None and SPEC.loader is not None
SUMMARY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SUMMARY)
FQN = "GoymenERP.Tests.ReceiptSqlTests.Records"
FRAME = "   at " + FQN + "() in C:/private-path/private-file.cs:line 123"


class ClosedAssertionDiagnosticsTests(unittest.TestCase):
    def result(self, message="Assert.Equal() Failure: private-data", stack=FRAME, *, outcome="Failed", raw=False, chunk=17):
        source = (
            '<TestRun><Results><UnitTestResult testId="one" outcome="' + outcome + '">'
            '<Output><ErrorInfo><Message>' + (message if raw else escape(message)) + '</Message>'
            '<StackTrace>' + (stack if raw else escape(stack)) + '</StackTrace></ErrorInfo></Output>'
            '</UnitTestResult></Results><TestDefinitions><UnitTest id="one">'
            '<TestMethod className="GoymenERP.Tests.ReceiptSqlTests" name="Records" />'
            '</UnitTest></TestDefinitions></TestRun>'
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.trx"
            path.write_text(source, encoding="utf-8")
            with mock.patch.object(SUMMARY, "STREAM_CHUNK_BYTES", chunk):
                result = SUMMARY.build_summary([path], "a" * 40, "b" * 40, "sql-c", "native-failure", 50)
        encoded = json.dumps(result)
        for private in ("private-path", "private-file", "private-data", "12345678901234567890", "ClientConnectionId"):
            self.assertNotIn(private, encoded)
        self.assertEqual(2, result["schemaVersion"])
        self.assertEqual("unknown", result["groupCompleteness"])
        self.assertEqual("not_evaluated", result["jobAcceptance"])
        return result

    def group(self, *args, **kwargs):
        groups = self.result(*args, **kwargs)["failureGroups"]
        return groups[0] if groups else {}

    def test_closed_assertion_names_and_exact_frame_survive_chunk_boundaries(self):
        for kind in SUMMARY.ASSERTION_KINDS:
            with self.subTest(kind=kind):
                group = self.group("Assert." + kind + "() Failure: private-data", chunk=1)
                self.assertEqual(kind, group["assertionKind"])
                self.assertEqual(123, group["sourceLine"])
                self.assertEqual(1, group["count"])

    def test_unknown_or_multiple_assertion_names_remain_closed(self):
        for message in ("Assert.PrivateThing() Failure: private-data", "Assert.Equal() Failure: x\nAssert.Single() Failure: y", "Assert.Equal(secret) Failure: private-data"):
            self.assertEqual("unknown", self.group(message)["assertionKind"])

    def test_unrelated_message_is_not_labeled_an_assertion(self):
        group = self.group("private-data ClientConnectionId:12345678901234567890", stack="")
        self.assertNotIn("assertionKind", group)
        self.assertNotIn("sourceLine", group)

    def test_foreign_or_noncanonical_frame_never_emits_line(self):
        for stack in (FRAME.replace(FQN, FQN + "Other"), FRAME.replace("Records", "Other"), FRAME.replace("123", "0123"), FRAME.replace("123", "0"), FRAME.replace("123", "12345678901234567890"), FRAME + " trailing-private-data", "private-data:line 123"):
            with self.subTest(stack=stack):
                self.assertNotIn("sourceLine", self.group(stack=stack))

    def test_multiple_matching_frames_are_ambiguous_even_if_one_is_malformed(self):
        for stack in (FRAME + "\n" + FRAME, FRAME + "\n" + FRAME.replace("123", "0123")):
            self.assertNotIn("sourceLine", self.group(stack=stack))

    def test_other_frames_do_not_hide_one_exact_frame(self):
        stack = "   at GoymenERP.Private.Helper() in /private-path/other.cs:line 987\n" + FRAME
        self.assertEqual(123, self.group(stack=stack)["sourceLine"])

    def test_overflow_and_nested_markup_omit_untrusted_projections(self):
        group = self.group("Assert.Equal() Failure: " + "x" * 16384, FRAME + "\n" + "x" * 16384)
        self.assertNotIn("assertionKind", group)
        self.assertNotIn("sourceLine", group)
        group = self.group("<Foreign>Assert.Equal() Failure: private-data</Foreign>", "<Foreign>" + escape(FRAME) + "</Foreign>", raw=True)
        self.assertNotIn("assertionKind", group)
        self.assertNotIn("sourceLine", group)

    def test_continued_overflow_storage_is_bounded_and_duplicate_stacks_are_ambiguous(self):
        collector = SUMMARY.ResultCollector({"one": FQN})
        collector.start("UnitTestResult", {"testId": "one", "outcome": "Failed"}, 1)
        collector.start("Output", {}, 2)
        collector.start("ErrorInfo", {}, 3)
        collector.start("StackTrace", {}, 4)
        collector.data("x" * SUMMARY.MAX_DIAGNOSTIC_SCAN_CHARS)
        for _ in range(10000):
            collector.data("private-data")
            collector.data("")
        self.assertEqual(1, len(collector.stack_parts))
        self.assertEqual(SUMMARY.MAX_DIAGNOSTIC_SCAN_CHARS, collector.stack_chars)
        self.assertTrue(collector.stack_rejected)
        collector.end("StackTrace", 4)
        self.assertIsNone(collector.source_line)
        # A second exact frame cannot override an overflowed/earlier stack.
        collector.start("StackTrace", {}, 4)
        collector.data(FRAME)
        collector.end("StackTrace", 4)
        self.assertIsNone(collector.source_line)
        duplicate = SUMMARY.ResultCollector({"one": FQN})
        duplicate.start("UnitTestResult", {"testId": "one", "outcome": "Failed"}, 1)
        duplicate.start("Output", {}, 2)
        duplicate.start("ErrorInfo", {}, 3)
        for _ in range(2):
            duplicate.start("StackTrace", {}, 4)
            duplicate.data(FRAME)
            duplicate.end("StackTrace", 4)
        self.assertIsNone(duplicate.source_line)

    def test_passed_and_skipped_outcomes_cannot_supply_diagnostics(self):
        for outcome in ("Passed", "NotExecuted", "Skipped"):
            result = self.result(outcome=outcome)
            self.assertEqual([], result["failureGroups"])
            self.assertEqual(0, result["totals"]["failed"])

    def test_sql_and_existing_marker_are_preserved_with_assertion_metadata(self):
        message = ("CI_SAFE_DIAGNOSTIC:mfg_customer_arrange\nAssert.Single() Failure: private-data\n"
                   "Microsoft.Data.SqlClient.SqlException (0x80131904)\nError Number:468,State:9,Class:16")
        group = self.group(message)
        self.assertEqual("mfg_customer_arrange", group["diagnosticCode"])
        self.assertEqual("sql_error_468", group["sqlErrorCode"])
        self.assertEqual("Single", group["assertionKind"])
        self.assertEqual(123, group["sourceLine"])


if __name__ == "__main__":
    unittest.main()
