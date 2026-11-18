from __future__ import annotations

from collections import Counter
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

import test_verify_backend_test_groups as fixtures


VERIFIER = fixtures.VERIFIER
PREFIX = "GoymenERP.Api.IntegrationTests"
CLASS = PREFIX + ".InventorySqlServerRelationalAcceptanceTests"
CASE_VARIANT = PREFIX + ".inventorysqlserverrelationalacceptancetests"
COMMAND = ["dotnet", "test", "tests/Api.csproj", "--no-build", "--no-restore"]


def large_class() -> Counter[str]:
    return Counter({f"{CLASS}.Case_{index:03d}_" + "long_method_name_" * 7: 1
                    for index in range(298)})


def command_units(arguments: list[str], expression: str) -> int:
    return len(subprocess.list2cmdline([*arguments, "--filter", expression])
               .encode("utf-16-le")) // 2 + 1


def selected_cases(expression: str, discovered: Counter[str]) -> Counter[str]:
    """Independent TestSDK command double: = equality and ~ literal contains."""
    def matches(name: str, term: str) -> bool:
        if term.startswith("FullyQualifiedName="):
            return name.casefold() == term.removeprefix("FullyQualifiedName=").casefold()
        if term.startswith("FullyQualifiedName~"):
            return term.removeprefix("FullyQualifiedName~").casefold() in name.casefold()
        raise AssertionError("Unexpected test filter operator")

    return Counter({name: count for name, count in discovered.items()
                    if any(matches(name, term) for term in expression.split("|"))})


class WindowsApiFilterTests(unittest.TestCase):
    def test_normal_windows_and_non_windows_commands_retain_exact_filters(self) -> None:
        for batch, windows in ((Counter({CLASS + ".One": 3}), True),
                               (large_class(), False)):
            with self.subTest(windows=windows):
                expected = "|".join("FullyQualifiedName=" + name for name in sorted(batch))
                self.assertEqual(VERIFIER.api_batch_filter(COMMAND, batch, batch, windows=windows),
                                 expected)

    def test_298_case_class_compacts_without_repartitioning_or_losing_cases(self) -> None:
        batch = large_class()
        before = batch.copy()
        batches = VERIFIER.api_test_batches(batch)
        self.assertEqual(batches, [batch])
        exact = "|".join("FullyQualifiedName=" + name for name in batch)
        self.assertGreater(command_units(COMMAND, exact), 32767)
        expression = VERIFIER.api_batch_filter(COMMAND, batch, batch, windows=True)
        self.assertEqual(expression, "FullyQualifiedName~" + CLASS.lower() + ".")
        self.assertEqual(selected_cases(expression, batch), before)
        self.assertEqual(batch, before)
        self.assertLess(command_units(COMMAND, expression), 32767)

    def test_complete_case_equivalent_classes_and_theories_preserve_literal_boundaries(self) -> None:
        batch = large_class()
        batch[CASE_VARIANT + ".AnotherTheory"] = 4
        full = batch + Counter({CLASS + "Sibling.Run": 1, CLASS + "+Nested.Run": 2})
        expression = VERIFIER.api_batch_filter(COMMAND, batch, full, windows=True)
        self.assertEqual(selected_cases(expression, full), batch)
        self.assertEqual(expression.count("FullyQualifiedName~"), 1)

    def test_substring_namespace_and_case_collisions_outside_batch_fail_closed(self) -> None:
        batch = large_class()
        for extra in (CLASS + ".Child.Run", PREFIX + ".Other." + CLASS + ".Run",
                      CASE_VARIANT + ".OtherCase"):
            with self.subTest(extra=extra), self.assertRaisesRegex(RuntimeError, "complete project discovery"):
                VERIFIER.api_batch_filter(COMMAND, batch, batch + Counter({extra: 1}), windows=True)

    def test_overlapping_class_tokens_count_each_discovered_case_once(self) -> None:
        batch = large_class() + Counter({CLASS + ".Child.Theory": 3})
        expression = VERIFIER.api_batch_filter(COMMAND, batch, batch, windows=True)
        self.assertEqual(expression.count("FullyQualifiedName~"), 2)
        self.assertEqual(selected_cases(expression, batch), batch)

    def test_missing_methods_wrong_theory_counts_and_noncanonical_discovery_are_rejected(self) -> None:
        full = large_class()
        first = next(iter(full))
        missing = full.copy()
        del missing[first]
        wrong_count = full.copy()
        wrong_count[first] = 2
        for batch in (missing, wrong_count):
            with self.subTest(batch_size=len(batch)), self.assertRaisesRegex(RuntimeError, "complete project discovery"):
                VERIFIER.api_batch_filter(COMMAND, batch, full, windows=True)
        with self.assertRaisesRegex(RuntimeError, "noncanonical"):
            VERIFIER.api_batch_filter(COMMAND, full, full + Counter({"x|FullyQualifiedName=Other": 1}),
                                      windows=True)

    def test_full_argv_quoting_utf16_and_terminating_null_define_the_boundary(self) -> None:
        batch = Counter({CLASS + ".First": 1, CLASS + ".Second": 1})
        exact = "|".join("FullyQualifiedName=" + name for name in sorted(batch))
        arguments = ["dotnet", "test", 'C:\\space dir\\emoji-\U0001f600\\Api.csproj', "x"]
        arguments[-1] *= 1 + 32767 - command_units(arguments, exact)
        self.assertEqual(command_units(arguments, exact), 32767)
        self.assertEqual(VERIFIER.api_batch_filter(arguments, batch, batch, windows=True), exact)
        arguments[-1] += "x"
        self.assertEqual(command_units(arguments, exact), 32768)
        compact = VERIFIER.api_batch_filter(arguments, batch, batch, windows=True)
        self.assertEqual(compact, "FullyQualifiedName~" + CLASS.lower() + ".")
        self.assertLessEqual(command_units(arguments, compact), 32767)

    def test_still_oversized_compact_command_is_rejected_before_launch(self) -> None:
        batch = large_class()
        with self.assertRaisesRegex(RuntimeError, "still exceeds"):
            VERIFIER.api_batch_filter([*COMMAND, "x" * 32767], batch, batch, windows=True)


class CompactApiProducerTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        fixtures.make_repository(self.root)

    def test_compact_producer_retains_native_ids_theories_partition_and_proof_failures(self) -> None:
        batch = large_class()
        first = next(iter(batch))
        batch[first] = 3
        outside = PREFIX + ".OtherTests.Run"
        full = batch + Counter({outside: 1, PREFIX + ".ThirdTests.Run": 1})
        project = VERIFIER.GROUP_PROJECTS["api"][0]
        modes = {"valid": None, "missing_method": "result identities", "missing_theory": "multiplicity",
                 "extra_result": "result identities", "wrong_native_id": "native case identities",
                 "duplicate_execution": "repeats an execution", "unauthorized_skip": "not executed",
                 "failed_command": "exited with code 1", "missing_trx": "exact TRX"}
        for mode, failure in modes.items():
            with self.subTest(mode=mode):
                results = self.root / mode / "results"
                proof = self.root / mode / "proof.json"
                test_commands = []

                def command(arguments: list[str], root: Path) -> str:
                    if "--list-tests" in arguments:
                        return fixtures.mock_discovery(arguments, root, project,
                            [name for name, count in full.items() for _ in range(count)])
                    if "--results-directory" not in arguments:
                        return ""
                    test_commands.append(arguments)
                    expression = arguments[arguments.index("--filter") + 1]
                    self.assertTrue(expression.startswith("FullyQualifiedName~"))
                    executed = selected_cases(expression, full)
                    self.assertEqual(executed, batch)
                    if mode == "failed_command":
                        raise RuntimeError("Backend project command exited with code 1.")
                    if mode == "missing_trx":
                        return ""
                    if mode == "missing_method":
                        del executed[next(reversed(executed))]
                    if mode == "missing_theory":
                        executed[first] -= 1
                    if mode == "extra_result":
                        executed[outside] = 1
                    document = fixtures.batch_trx_source(executed, 1, skipped=mode == "unauthorized_skip",
                        source=root / VERIFIER.assembly_relative_path(project))
                    if mode == "wrong_native_id":
                        identity, _ = fixtures.case_identity(first, first, 0)
                        document = document.replace(identity, "12345678-1234-4abc-8def-000000000999")
                    if mode == "duplicate_execution":
                        document = document.replace("12345678-1234-4abc-8def-000000000002",
                                                    "12345678-1234-4abc-8def-000000000001")
                    logger = next(value for value in arguments if value.startswith("trx;LogFileName="))
                    (results / logger.split("=", 1)[1]).write_text(document, encoding="utf-8")
                    return ""

                original_filter = VERIFIER.api_batch_filter
                with mock.patch.object(VERIFIER, "run_checked", side_effect=command), \
                     mock.patch.object(VERIFIER, "api_batch_filter", side_effect=lambda args, selected, all_cases:
                         original_filter(args, selected, all_cases, windows=True)), \
                     mock.patch.dict(os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": ""}):
                    if failure is not None:
                        with self.assertRaisesRegex(RuntimeError, failure):
                            VERIFIER.run_group(self.root, "api", results, proof, fixtures.HEAD, fixtures.COMMIT, 1)
                        self.assertFalse(proof.exists())
                    else:
                        self.assertEqual(VERIFIER.run_group(self.root, "api", results, proof,
                                         fixtures.HEAD, fixtures.COMMIT, 1), 300)
                        row = json.loads(proof.read_text(encoding="utf-8"))["projects"][0]
                        self.assertEqual((row["discoveredMethods"], row["discoveredCases"], row["passed"],
                                          row["skipped"]), (298, 300, 300, 0))
                        self.assertEqual(row["discoveryHash"], VERIFIER.digest(sorted(batch.items())))
                self.assertEqual(len(test_commands), 1)

    def test_collision_in_another_shard_is_rejected_before_test_launch_or_proof(self) -> None:
        batch = large_class()
        collision = CLASS + ".Child.Run"
        full = batch + Counter({collision: 1, PREFIX + ".ThirdTests.Run": 1})
        project = VERIFIER.GROUP_PROJECTS["api"][0]
        self.assertNotIn(collision, VERIFIER.api_shard_partition(full, 3, set())[0])
        results, proof = self.root / "results", self.root / "proof.json"
        launched = []

        def command(arguments: list[str], root: Path) -> str:
            if "--list-tests" in arguments:
                return fixtures.mock_discovery(arguments, root, project, list(full.elements()))
            if "--results-directory" in arguments:
                launched.append(arguments)
            return ""

        original_filter = VERIFIER.api_batch_filter
        with mock.patch.object(VERIFIER, "run_checked", side_effect=command), \
             mock.patch.object(VERIFIER, "api_batch_filter", side_effect=lambda args, selected, all_cases:
                 original_filter(args, selected, all_cases, windows=True)), \
             mock.patch.dict(os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": ""}), \
             self.assertRaisesRegex(RuntimeError, "complete project discovery"):
            VERIFIER.run_group(self.root, "api", results, proof, fixtures.HEAD, fixtures.COMMIT, 1)
        self.assertEqual(launched, [])
        self.assertFalse(proof.exists())


if __name__ == "__main__":
    unittest.main()
