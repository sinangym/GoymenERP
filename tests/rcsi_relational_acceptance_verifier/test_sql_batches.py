from __future__ import annotations

import contextlib
import importlib.util
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import uuid

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("relational_sql_batches", ROOT / "tools/verify_rcsi_relational_acceptance.py")
assert SPEC is not None and SPEC.loader is not None
V = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(V)
PREFIX = "GoymenERP.Api.IntegrationTests.InventorySqlServerRelationalAcceptanceTests."


def trx(executions: dict[str, int], *, outcome: str = "Passed") -> str:
    results, definitions = [], []
    total = sum(executions.values())
    for name, count in executions.items():
        test_id = str(uuid.uuid5(uuid.NAMESPACE_URL, name))
        class_name, method = name.rsplit(".", 1)
        definitions.append(f'<UnitTest id="{test_id}"><TestMethod className="{class_name}" name="{method}" /></UnitTest>')
        for row in range(count):
            execution_id = str(uuid.uuid5(uuid.NAMESPACE_URL, f"{name}/{row}"))
            results.append(f'<UnitTestResult testId="{test_id}" executionId="{execution_id}" testName="{name}" outcome="{outcome}" />')
    passed = total if outcome == "Passed" else 0
    failed = total if outcome == "Failed" else 0
    skipped = total if outcome == "NotExecuted" else 0
    return ('<TestRun><Results>' + ''.join(results) + '</Results><TestDefinitions>'
            + ''.join(definitions) + '</TestDefinitions><ResultSummary><Counters '
            + f'total="{total}" executed="{total - skipped}" passed="{passed}" failed="{failed}" '
            + f'error="0" timeout="0" aborted="0" inconclusive="0" notExecuted="{skipped}" '
            + '/></ResultSummary></TestRun>')


class SqlBatchTests(unittest.TestCase):
    def test_plan_is_deterministic_disjoint_and_preserves_all_theory_executions(self) -> None:
        expected = {PREFIX + f"Method{i:02}": 3 if i == 0 else 1 for i in range(24)}
        batches = V.plan_sql_batches(expected)
        self.assertEqual(batches, V.plan_sql_batches(dict(reversed(list(expected.items())))))
        self.assertEqual(expected, {name: count for batch in batches for name, count in batch.items()})
        self.assertEqual(len(expected), sum(map(len, batches)))
        for batch in batches:
            self.assertLessEqual(len(batch), V.SQL_BATCH_MAX_METHODS)
            self.assertLessEqual(sum(batch.values()), V.SQL_BATCH_MAX_EXECUTIONS)
        self.assertEqual(1, sum(PREFIX + "Method00" in batch for batch in batches))
        self.assertEqual(3, next(batch[PREFIX + "Method00"] for batch in batches if PREFIX + "Method00" in batch))

    def test_class_round_robin_mixes_independent_classes_deterministically(self) -> None:
        a = "GoymenERP.Api.IntegrationTests.InventoryASqlServerRelationalAcceptanceTests."
        b = "GoymenERP.Api.IntegrationTests.InventoryBSqlServerRelationalAcceptanceTests."
        expected = {prefix + f"Method{i:02}": 1 for prefix in (a, b) for i in range(9)}
        batches = V.plan_sql_batches(expected)
        self.assertEqual(batches, V.plan_sql_batches(dict(reversed(list(expected.items())))))
        order = [name for batch in batches for name in batch]
        self.assertEqual([prefix + f"Method{i:02}" for i in range(9) for prefix in (a, b)], order)
        self.assertEqual([8, 8, 2], list(map(len, batches)))
        self.assertEqual(expected, {name: count for batch in batches for name, count in batch.items()})
        for batch in batches:
            self.assertEqual({a[:-1], b[:-1]}, {name.rsplit(".", 1)[0] for name in batch})

    def test_short_class_is_not_starved_behind_long_class_queue(self) -> None:
        short = "GoymenERP.Api.IntegrationTests.WorkSqlServerRelationalAcceptanceTests.One"
        expected = {PREFIX + f"Method{i:02}": 1 for i in range(20)} | {short: 1}
        batches = V.plan_sql_batches(expected)
        self.assertEqual([PREFIX + "Method00", short], list(batches[0])[:2])
        self.assertEqual(expected, {name: count for batch in batches for name, count in batch.items()})
        self.assertEqual(21, sum(map(len, batches)))

    def test_large_theory_stays_alone_and_is_never_split_or_dropped(self) -> None:
        expected = {PREFIX + "A": 1, PREFIX + "B": 21, PREFIX + "C": 1}
        self.assertEqual(({PREFIX + "A": 1}, {PREFIX + "B": 21}, {PREFIX + "C": 1}), V.plan_sql_batches(expected))

    def test_empty_non_sql_identity_and_invalid_multiplicity_are_rejected(self) -> None:
        for expected in ({}, {"GoymenERP.Tests.Http.Case": 1}, {PREFIX + "Case": 0},
                         {PREFIX + "Case": -1}, {PREFIX + "Case": True}, {PREFIX + "Case": 1.5}):
            with self.subTest(expected=expected), self.assertRaisesRegex(RuntimeError, "canonical relational"):
                V.plan_sql_batches(expected)

    def test_real_inventory_partitions_each_shard_without_case_changes(self) -> None:
        for shard in V.SHARD_MODULES:
            expected = V.shard_executions(shard)
            batches = V.plan_sql_batches(expected)
            self.assertEqual(expected, {name: count for batch in batches for name, count in batch.items()})
            self.assertEqual(sum(expected.values()), sum(sum(batch.values()) for batch in batches))

    def test_exact_definition_identity_and_theory_rows_are_verified(self) -> None:
        expected = {PREFIX + "Case": 3}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "batch.trx"
            path.write_text(trx(expected), encoding="utf-8")
            seen: set[str] = set()
            self.assertEqual(expected, V.verify_sql_batch_trx(path, expected, seen))
            self.assertEqual(3, len(seen))
            with self.assertRaisesRegex(RuntimeError, "unique"):
                V.verify_sql_batch_trx(path, expected, seen)

    def test_missing_extra_and_wrong_method_multiplicity_fail(self) -> None:
        expected = {PREFIX + "A": 1, PREFIX + "B": 1}
        variants = ({PREFIX + "A": 1}, {PREFIX + "A": 1, PREFIX + "C": 1}, {PREFIX + "A": 2})
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "batch.trx"
            for actual in variants:
                path.write_text(trx(actual), encoding="utf-8")
                with self.subTest(actual=actual), self.assertRaises(RuntimeError):
                    V.verify_sql_batch_trx(path, expected, set())

    def test_unmapped_duplicate_ids_failed_and_skipped_results_fail_closed(self) -> None:
        expected = {PREFIX + "Case": 2}
        source = trx(expected)
        first_id = str(uuid.uuid5(uuid.NAMESPACE_URL, PREFIX + "Case/0"))
        second_id = str(uuid.uuid5(uuid.NAMESPACE_URL, PREFIX + "Case/1"))
        variants = (source.replace(second_id, first_id), source.replace('testId="', 'wrongTestId="'),
                    trx(expected, outcome="Failed"), trx(expected, outcome="NotExecuted"))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "batch.trx"
            for variant in variants:
                path.write_text(variant, encoding="utf-8")
                with self.subTest(variant=variants.index(variant)), self.assertRaises(RuntimeError):
                    V.verify_sql_batch_trx(path, expected, set())

    def test_execution_identity_alias_non_uuid_zero_and_noncanonical_forms_fail(self) -> None:
        expected = {PREFIX + "Case": 2}
        source = trx(expected)
        first_id = str(uuid.uuid5(uuid.NAMESPACE_URL, PREFIX + "Case/0"))
        second_id = str(uuid.uuid5(uuid.NAMESPACE_URL, PREFIX + "Case/1"))
        for replacement in (first_id.upper(), "not-a-uuid", str(uuid.UUID(int=0)), "{" + second_id + "}", second_id.replace("-", "")):
            with self.subTest(replacement=replacement), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "batch.trx"
                path.write_text(source.replace(second_id, replacement), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "unique|canonical nonzero UUID"):
                    V.verify_sql_batch_trx(path, expected, set())

    def test_uppercase_execution_identity_is_normalized_across_batches(self) -> None:
        expected = {PREFIX + "Case": 1}
        execution_id = str(uuid.uuid5(uuid.NAMESPACE_URL, PREFIX + "Case/0"))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "batch.trx"
            path.write_text(trx(expected).replace(execution_id, execution_id.upper()), encoding="utf-8")
            seen: set[str] = set()
            V.verify_sql_batch_trx(path, expected, seen)
            self.assertEqual({execution_id}, seen)
            path.write_text(trx(expected), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "unique"):
                V.verify_sql_batch_trx(path, expected, seen)

    def test_unused_definition_identity_is_rejected_even_when_method_counts_match(self) -> None:
        expected = {PREFIX + "Case": 1}
        extra = ('<UnitTest id="' + str(uuid.uuid4()) + '"><TestMethod className="'
                 + PREFIX.rsplit(".", 1)[0] + '" name="Case" /></UnitTest>')
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "batch.trx"
            path.write_text(trx(expected).replace("</TestDefinitions>", extra + "</TestDefinitions>"), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "use every"):
                V.verify_sql_batch_trx(path, expected, set())

    def test_symlink_and_non_file_outputs_are_refused_before_parsing(self) -> None:
        expected = {PREFIX + "Case": 1}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "batch.trx"
            path.write_text(trx(expected), encoding="utf-8")
            with mock.patch.object(Path, "is_symlink", return_value=True), mock.patch.object(V, "verify_trx") as parse:
                with self.assertRaisesRegex(RuntimeError, "normal output file"):
                    V.verify_sql_batch_trx(path, expected, set())
                parse.assert_not_called()
            with self.assertRaisesRegex(RuntimeError, "normal output file"):
                V.verify_sql_batch_trx(Path(directory), expected, set())

    def fake_runner(self, expected: dict[str, int], native_calls: list, *, fail: bool = False):
        def run(arguments, root, **kwargs):
            native_calls.append((arguments, kwargs))
            selected = {name.removeprefix("FullyQualifiedName="): expected[name.removeprefix("FullyQualifiedName=")]
                        for name in arguments[arguments.index("--filter") + 1].split("|")}
            logger = next(value for value in arguments if value.startswith("trx;LogFileName="))
            path = Path(arguments[arguments.index("--results-directory") + 1]) / logger.split("=", 1)[1]
            path.write_text(trx(selected), encoding="utf-8")
            if fail:
                raise RuntimeError("RCSI acceptance command exited with native code 7.")
            return subprocess.CompletedProcess(arguments, 0, stdout="", stderr="")
        return run

    def test_success_uses_numbered_trxs_one_deadline_and_exact_union(self) -> None:
        expected = {PREFIX + f"Case{i}": 1 for i in range(10)}
        calls = []
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with mock.patch.object(V, "run_bootstrap_preflight") as preflight, mock.patch.object(
                V, "run_checked", side_effect=self.fake_runner(expected, calls)
            ), mock.patch.object(V.time, "monotonic", return_value=100):
                self.assertEqual(expected, V.run_sql_batches(root, root / "result.trx", expected))
            self.assertEqual(["result.batch-0001.trx", "result.batch-0002.trx"], sorted(path.name for path in root.glob("*.trx")))
        self.assertEqual([2500, 2500], [kwargs["execution_deadline"] for _, kwargs in calls])
        self.assertEqual([2500, 2500], [call.kwargs["execution_deadline"] for call in preflight.call_args_list])

    def test_first_native_failure_retains_trx_and_does_not_execute_remaining_batches(self) -> None:
        expected = {PREFIX + f"Case{i}": 1 for i in range(10)}
        calls = []
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with mock.patch.object(V, "run_bootstrap_preflight"), mock.patch.object(
                V, "run_checked", side_effect=self.fake_runner(expected, calls, fail=True)
            ), self.assertRaisesRegex(RuntimeError, "native code 7"):
                V.run_sql_batches(root, root / "result.trx", expected)
            self.assertEqual(["result.batch-0001.trx"], [path.name for path in root.glob("*.trx")])
            self.assertTrue((root / "result.batch-0001.trx").read_text(encoding="utf-8").startswith("<TestRun>"))
            self.assertFalse(list(root.glob("*.json")))
        self.assertEqual(1, len(calls))

    def test_missing_trx_fails_before_next_batch(self) -> None:
        expected = {PREFIX + f"Case{i}": 1 for i in range(10)}
        with tempfile.TemporaryDirectory() as directory, mock.patch.object(V, "run_bootstrap_preflight"), mock.patch.object(V, "run_checked") as run:
            with self.assertRaisesRegex(RuntimeError, "normal output file"):
                V.run_sql_batches(Path(directory), Path(directory) / "result.trx", expected)
            self.assertEqual(1, run.call_count)

    def test_stale_trx_is_not_reused_or_overwritten(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stale = root / "result.batch-0001.trx"
            stale.write_text("old evidence", encoding="utf-8")
            with mock.patch.object(V, "run_checked") as run, self.assertRaisesRegex(RuntimeError, "fresh"):
                V.run_sql_batches(root, root / "result.trx", {PREFIX + "Case": 1})
            run.assert_not_called()
            self.assertEqual("old evidence", stale.read_text(encoding="utf-8"))

    def test_stale_extra_batch_outside_current_plan_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stale = root / "result.batch-9999.trx"
            stale.write_text("old evidence", encoding="utf-8")
            with mock.patch.object(V, "run_checked") as run, self.assertRaisesRegex(RuntimeError, "fresh"):
                V.run_sql_batches(root, root / "result.trx", {PREFIX + "Case": 1})
            run.assert_not_called()
            self.assertEqual("old evidence", stale.read_text(encoding="utf-8"))

    def test_overlapping_batch_methods_cannot_be_accepted(self) -> None:
        expected = {PREFIX + "Case": 1}
        calls = []
        with tempfile.TemporaryDirectory() as directory, mock.patch.object(V, "plan_sql_batches", return_value=(expected, expected)), mock.patch.object(V, "run_bootstrap_preflight"), mock.patch.object(V, "run_checked", side_effect=self.fake_runner(expected, calls)):
            with self.assertRaisesRegex(RuntimeError, "unique|overlap"):
                V.run_sql_batches(Path(directory), Path(directory) / "result.trx", expected)

    def test_first_failed_shard_batch_deletes_stale_proof_and_never_publishes_one(self) -> None:
        native_calls = []
        selected = V.shard_executions("A")
        fake = self.fake_runner(selected, native_calls, fail=True)
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            root = Path(directory)
            proof = root / "proof.json"
            proof.write_text("stale proof", encoding="utf-8")
            def run(arguments, repository_root, **kwargs):
                if "--list-tests" in arguments:
                    return subprocess.CompletedProcess(arguments, 0, stdout="\n".join(V.BROAD_EXPECTED_TESTS), stderr="")
                if "--no-build" in arguments:
                    return fake(arguments, repository_root, **kwargs)
                return subprocess.CompletedProcess(arguments, 0, stdout="", stderr="")
            with mock.patch.object(V, "run_checked", side_effect=run), mock.patch.dict(V.os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": "synthetic"}), mock.patch.object(sys, "argv", [
                "verifier", "--shard", "A", "--trx-output", str(root / "result.trx"),
                "--proof-output", str(proof), "--source-head", "a" * 40, "--tested-commit", "b" * 40,
            ]), contextlib.redirect_stdout(io.StringIO()), self.assertRaisesRegex(RuntimeError, "native code 7"):
                V.main()
            self.assertFalse(proof.exists())
            self.assertEqual(1, len(native_calls))
            self.assertTrue((root / "result.batch-0001.trx").exists())
            self.assertFalse((root / "result.batch-0002.trx").exists())

    def test_global_deadline_is_not_renewed_after_first_completed_batch(self) -> None:
        expected = {PREFIX + f"Case{i}": 1 for i in range(10)}
        clock = [100.0]
        calls = []
        fake = self.fake_runner(expected, calls)
        def run(arguments, root, **kwargs):
            result = fake(arguments, root, **kwargs)
            clock[0] = 2500
            return result
        with tempfile.TemporaryDirectory() as directory, mock.patch.object(V.time, "monotonic", side_effect=lambda: clock[0]), mock.patch.object(V, "run_bootstrap_preflight"), mock.patch.object(V, "run_checked", side_effect=run):
            with self.assertRaisesRegex(RuntimeError, "global deadline"):
                V.run_sql_batches(Path(directory), Path(directory) / "result.trx", expected)
        self.assertEqual(1, len(calls))

    def test_deadline_flows_to_real_child_runner_without_reset(self) -> None:
        arguments = ["dotnet", "test", "--no-build", "--results-directory", "results"]
        with mock.patch.object(V, "run_test_checked") as run:
            V.run_checked(arguments, ROOT, execution_deadline=321.0)
        run.assert_called_once_with(arguments, ROOT, execution_deadline=321.0)

    def test_rcsi_fourteen_keep_single_process_original_filter_and_trx(self) -> None:
        calls = []
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            output = Path(directory) / "rcsi.trx"
            def run(arguments, root, **kwargs):
                calls.append((arguments, kwargs))
                if "--list-tests" in arguments:
                    return subprocess.CompletedProcess(arguments, 0, stdout="\n".join(V.EXPECTED_TESTS), stderr="")
                if "--no-build" in arguments:
                    output.write_text(trx({name: 1 for name in V.EXPECTED_TESTS}), encoding="utf-8")
                return subprocess.CompletedProcess(arguments, 0, stdout="", stderr="")
            with mock.patch.object(V, "run_checked", side_effect=run), mock.patch.object(V, "run_sql_batches") as batcher, mock.patch.dict(V.os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": "synthetic"}), mock.patch.object(sys, "argv", ["verifier", "--rcsi", "--trx-output", str(output)]), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, V.main())
            batcher.assert_not_called()
            native = [(arguments, kwargs) for arguments, kwargs in calls if "--no-build" in arguments]
            self.assertEqual(1, len(native))
            self.assertEqual({}, native[0][1])
            self.assertEqual(V.RCSI_FILTER, native[0][0][native[0][0].index("--filter") + 1])
            self.assertTrue(output.exists())


if __name__ == "__main__":
    unittest.main()
