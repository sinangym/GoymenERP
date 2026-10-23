from __future__ import annotations

import importlib.util
from collections import Counter
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock


SOURCE = Path(__file__).resolve().parents[2] / "tools" / "verify_backend_test_groups.py"
SPEC = importlib.util.spec_from_file_location("verify_backend_test_groups", SOURCE)
assert SPEC is not None and SPEC.loader is not None
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)
HEAD = "a" * 40
COMMIT = "b" * 40


def make_repository(root: Path) -> None:
    projects = [path for group in VERIFIER.GROUP_PROJECTS.values() for path in group]
    projects += [f"src/backend/Other{i:02d}/Other{i:02d}.csproj" for i in range(51)]
    solution = ["<Solution>", '  <Folder Name="/tests/">']
    for path in projects:
        project = root / path
        project.parent.mkdir(parents=True, exist_ok=True)
        references = (
            '<ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" /></ItemGroup>'
            if path in {p for group in VERIFIER.GROUP_PROJECTS.values() for p in group}
            else ""
        )
        project.write_text(f'<Project Sdk="Microsoft.NET.Sdk">{references}</Project>', encoding="utf-8")
        solution.append(f'    <Project Path="{path}" />')
    solution += ["  </Folder>", "</Solution>"]
    (root / "GoymenERP.slnx").write_text("\n".join(solution), encoding="utf-8")


def trx_source(project: str, *, outcome: str = "Passed", theory_rows: int = 1) -> str:
    test_name = Path(project).stem + ".ExampleTests.Runs"
    results = "".join(
        f'<UnitTestResult testId="one" '
        f'executionId="12345678-1234-4abc-8def-{index + 1:012x}" '
        f'outcome="{outcome}" testName="{test_name}" />'
        for index in range(theory_rows)
    )
    passed = theory_rows if outcome == "Passed" else 0
    return (
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f'<Counters total="{theory_rows}" executed="{passed}" passed="{passed}" '
        'notExecuted="0" failed="0" error="0" timeout="0" '
        'aborted="0" inconclusive="0" />'
        f"<Results>{results}</Results><TestDefinitions>"
        f'<UnitTest id="one"><TestMethod className="{Path(project).stem}.ExampleTests" '
        'name="Runs" /></UnitTest></TestDefinitions></TestRun>'
    )


def project_row(path: str) -> dict[str, object]:
    return {
        "path": path,
        "discoveryHash": "c" * 64,
        "resultHash": "d" * 64,
        "discoveredMethods": 1,
        "discoveredCases": 1,
        "executionCount": 1,
        "passed": 1,
        "skipped": 0,
        "stageElapsedMs": {stage: 0 for stage in VERIFIER.STAGES},
    }


def batch_trx_source(methods: Counter[str], first_execution: int) -> str:
    results = []
    definitions = []
    execution = first_execution
    for index, (name, cases) in enumerate(methods.items()):
        class_name, method = name.rsplit(".", 1)
        definitions.append(
            f'<UnitTest id="{index}"><TestMethod className="{class_name}" '
            f'name="{method}" /></UnitTest>'
        )
        for _ in range(cases):
            results.append(
                f'<UnitTestResult testId="{index}" '
                f'executionId="12345678-1234-4abc-8def-{execution:012x}" '
                'outcome="Passed" />'
            )
            execution += 1
    total = sum(methods.values())
    return (
        '<TestRun><Counters '
        f'total="{total}" executed="{total}" passed="{total}" '
        'notExecuted="0" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" />'
        f'<Results>{"".join(results)}</Results>'
        f'<TestDefinitions>{"".join(definitions)}</TestDefinitions></TestRun>'
    )


class BackendGroupVerifierTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        make_repository(self.root)

    def test_exact_seventy_one_project_solution_has_twenty_disjoint_testsdk_projects(self) -> None:
        solution_hash, catalog_hash = VERIFIER.source_inventory(self.root)
        self.assertEqual(len(solution_hash), 64)
        self.assertEqual(len(catalog_hash), 64)
        self.assertEqual(
            [len(VERIFIER.GROUP_PROJECTS[key]) for key in ("architecture", "api", "modules")],
            [1, 1, 18],
        )
        self.assertEqual(
            len({p for paths in VERIFIER.GROUP_PROJECTS.values() for p in paths}), 20
        )

    def test_missing_extra_duplicate_and_unclassified_project_fail_closed(self) -> None:
        source = (self.root / "GoymenERP.slnx").read_text(encoding="utf-8")
        for mutation in (
            source.replace('    <Project Path="src/backend/Other00/Other00.csproj" />\n', ""),
            source.replace(
                '    <Project Path="src/backend/Other00/Other00.csproj" />',
                '    <Project Path="tests/GoymenERP.Api.IntegrationTests/'
                'GoymenERP.Api.IntegrationTests.csproj" />',
            ),
        ):
            with self.subTest(mutation=mutation[-80:]):
                (self.root / "GoymenERP.slnx").write_text(mutation, encoding="utf-8")
                with self.assertRaises(RuntimeError):
                    VERIFIER.source_inventory(self.root)
        (self.root / "GoymenERP.slnx").write_text(source, encoding="utf-8")
        extra = self.root / "src/backend/Other00/Other00.csproj"
        extra.write_text(
            '<Project><PackageReference Include="Microsoft.NET.Test.Sdk" /></Project>',
            encoding="utf-8",
        )
        with self.assertRaisesRegex(RuntimeError, "TestSDK source inventory"):
            VERIFIER.source_inventory(self.root)

    def test_discovery_and_trx_preserve_deferred_module_theory_multiplicity_and_skip(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["modules"][10]
        name = Path(project).stem + ".ExampleTests.Runs"
        discovered = VERIFIER.discovery_map(name + "\n", project)
        path = self.root / "test.trx"
        path.write_text(trx_source(project, theory_rows=2), encoding="utf-8")
        results, passed, skipped = VERIFIER.trx_result_map(path, discovered)
        self.assertEqual((passed, skipped), (2, 0))
        self.assertEqual(results[name], {"passed": 2, "skipped": 0})
        path.write_text(trx_source(project, outcome="NotExecuted"), encoding="utf-8")
        _, passed, skipped = VERIFIER.trx_result_map(path, discovered)
        self.assertEqual((passed, skipped), (0, 1))

    def test_exact_api_multiplicity_rejects_surplus_theory_rows_with_consistent_counters(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["api"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        discovered = Counter({name: 2})
        path = self.root / "exact-api.trx"
        for cases in (1, 3):
            with self.subTest(cases=cases):
                path.write_text(trx_source(project, theory_rows=cases), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "multiplicity differs"):
                    VERIFIER.trx_result_map(path, discovered, exact_multiplicity=True)
        path.write_text(trx_source(project, theory_rows=2), encoding="utf-8")
        _, passed, skipped = VERIFIER.trx_result_map(path, discovered, exact_multiplicity=True)
        self.assertEqual((passed, skipped), (2, 0))
        path.write_text(trx_source(project, outcome="NotExecuted"), encoding="utf-8")
        _, passed, skipped = VERIFIER.trx_result_map(
            path, Counter({name: 1}), exact_multiplicity=True,
        )
        self.assertEqual((passed, skipped), (0, 1))

    def test_trx_refuses_absent_wrong_extra_or_duplicate_counts(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        discovered = VERIFIER.discovery_map(name + "\n", project)
        path = self.root / "test.trx"
        for source in (
            trx_source(project, outcome="Failed"),
            trx_source(project).replace('passed="1"', 'passed="0"'),
            trx_source(project).replace('total="1"', 'total="2"'),
            trx_source(project).replace('executed="1"', 'executed="0"'),
            trx_source(project, outcome="NotExecuted").replace('notExecuted="0"', 'notExecuted="1"'),
            trx_source(project).replace('testId="one"', 'testId="missing"'),
        ):
            with self.subTest(source=source[:80]):
                path.write_text(source, encoding="utf-8")
                with self.assertRaises(RuntimeError):
                    VERIFIER.trx_result_map(path, discovered)
        path.write_text(trx_source(project), encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "identities differ"):
            VERIFIER.trx_result_map(path, VERIFIER.discovery_map(name + "\n" + name + ".Other\n", project))

    def test_trx_rejects_missing_invalid_and_duplicate_execution_identities(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        discovered = VERIFIER.discovery_map(name + "\n", project)
        path = self.root / "test.trx"
        first_id = "12345678-1234-4abc-8def-000000000001"
        second_id = "12345678-1234-4abc-8def-000000000002"
        single = trx_source(project)
        double = trx_source(project, theory_rows=2)
        copied_row = single.split("<UnitTestResult ", 1)[1].split(" />", 1)[0]
        copied = single.replace(
            "</Results>", f'<UnitTestResult {copied_row} /></Results>'
        ).replace('total="1"', 'total="2"').replace(
            'executed="1"', 'executed="2"'
        ).replace('passed="1"', 'passed="2"')
        cases = (
            ("missing", single.replace(f' executionId="{first_id}"', ""),
             "execution identity"),
            ("empty", single.replace(first_id, ""), "execution identity"),
            ("malformed", single.replace(first_id, "not-a-guid"), "execution identity"),
            ("nil", single.replace(first_id, "00000000-0000-0000-0000-000000000000"),
             "execution identity"),
            ("duplicate theory", double.replace(second_id, first_id),
             "repeats an execution identity"),
            ("copied row adjusted counters", copied, "repeats an execution identity"),
        )
        for label, source, error in cases:
            with self.subTest(case=label):
                path.write_text(source, encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, error):
                    VERIFIER.trx_result_map(path, discovered)

    def proof_directory(self) -> Path:
        directory = self.root / "artifacts" / "proofs"
        for group, paths in VERIFIER.GROUP_PROJECTS.items():
            artifact = directory / f"ci-backend-test-group-proof-{group}"
            artifact.mkdir(parents=True, exist_ok=True)
            proof = VERIFIER.expected_proof(group, HEAD, COMMIT, self.root)
            proof["projects"] = [project_row(path) for path in paths]
            (artifact / f"proof-{group}.json").write_text(json.dumps(proof), encoding="utf-8")
        return directory

    def test_aggregate_requires_all_workers_source_and_exact_twenty_project_union(self) -> None:
        directory = self.proof_directory()
        self.assertEqual(
            VERIFIER.verify_aggregate(self.root, directory, HEAD, COMMIT, "success", "success"),
            20,
        )
        # Module MemberData can defer nonserializable records until execution.
        # Preserve that existing behavior while API's pre-enumerated batches
        # require exact method multiplicities and an exact total case count.
        module_path = directory / "ci-backend-test-group-proof-modules" / "proof-modules.json"
        module_proof = json.loads(module_path.read_text(encoding="utf-8"))
        module_proof["projects"][10].update(executionCount=4, passed=4)
        module_path.write_text(json.dumps(module_proof), encoding="utf-8")
        self.assertEqual(
            VERIFIER.verify_aggregate(self.root, directory, HEAD, COMMIT, "success", "success"), 23,
        )
        for build, workers in (("failure", "success"), ("success", "skipped")):
            with self.subTest(build=build, workers=workers):
                with self.assertRaisesRegex(RuntimeError, "must succeed"):
                    VERIFIER.verify_aggregate(self.root, directory, HEAD, COMMIT, build, workers)
        with self.assertRaisesRegex(RuntimeError, "does not match source"):
            VERIFIER.verify_aggregate(self.root, directory, "f" * 40, COMMIT, "success", "success")

    def test_aggregate_rejects_missing_unknown_duplicate_json_and_bad_count(self) -> None:
        directory = self.proof_directory()
        path = directory / "ci-backend-test-group-proof-api" / "proof-api.json"
        original = path.read_text(encoding="utf-8")
        for mutation in (
            original.replace('"passed": 1', '"passed": 2', 1),
            original.replace('"passed": 1', '"passed": 2', 1).replace(
                '"executionCount": 1', '"executionCount": 2', 1
            ),
            original.replace('"projectCount": 1', '"projectCount": 2', 1),
            original.replace('"schemaVersion": 2', '"schemaVersion": 1', 1),
            original.replace('"group": "api"', '"group": "modules"', 1),
            original.replace('"group": "api"', '"group": "api", "group": "api"', 1),
        ):
            with self.subTest(mutation=mutation[-80:]):
                path.write_text(mutation, encoding="utf-8")
                with self.assertRaises(RuntimeError):
                    VERIFIER.verify_aggregate(self.root, directory, HEAD, COMMIT, "success", "success")
        path.write_text(original, encoding="utf-8")
        unexpected = directory / "ci-backend-test-group-proof-unknown"
        unexpected.mkdir()
        with self.assertRaisesRegex(RuntimeError, "missing, duplicated or unknown"):
            VERIFIER.verify_aggregate(self.root, directory, HEAD, COMMIT, "success", "success")
        unexpected.rmdir()
        path.unlink()
        with self.assertRaisesRegex(RuntimeError, "contents are missing"):
            VERIFIER.verify_aggregate(self.root, directory, HEAD, COMMIT, "success", "success")

    def test_aggregate_rejects_missing_extra_or_invalid_stage_timings(self) -> None:
        directory = self.proof_directory()
        path = directory / "ci-backend-test-group-proof-api" / "proof-api.json"
        original = path.read_text(encoding="utf-8")
        cases = (
            ("missing", lambda values: values.pop("build")),
            ("extra", lambda values: values.update({"command": 1})),
            ("negative", lambda values: values.update({"test": -1})),
            ("boolean", lambda values: values.update({"test": True})),
            ("fraction", lambda values: values.update({"test": 1.5})),
            ("nonfinite", lambda values: values.update({"test": float("inf")})),
        )
        for label, mutate in cases:
            with self.subTest(case=label):
                proof = json.loads(original)
                mutate(proof["projects"][0]["stageElapsedMs"])
                path.write_text(json.dumps(proof), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "invalid stage timing"):
                    VERIFIER.verify_aggregate(
                        self.root, directory, HEAD, COMMIT, "success", "success"
                    )

    def test_group_runner_discovers_then_executes_exact_project_and_writes_safe_proof(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["api"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        commands: list[list[str]] = []

        def command(arguments: list[str], root: Path) -> str:
            commands.append(arguments)
            if "--list-tests" in arguments:
                return name + "\n"
            if arguments[0] == "dotnet" and "--results-directory" in arguments:
                directory = Path(arguments[arguments.index("--results-directory") + 1])
                directory.mkdir(parents=True, exist_ok=True)
                logger = next(value for value in arguments if value.startswith("trx;LogFileName="))
                (directory / logger.split("=", 1)[1]).write_text(
                    trx_source(project), encoding="utf-8"
                )
            return ""

        proof = self.root / "artifacts" / "proof-api.json"
        results = self.root / "artifacts" / "results"
        with mock.patch.object(VERIFIER, "run_checked", side_effect=command), mock.patch.object(
            VERIFIER.time, "monotonic_ns", side_effect=(
                0, 1_000_000, 2_000_000, 4_000_000, 5_000_000, 6_000_000,
                7_000_000, 10_000_000, 11_000_000, 12_000_000,
                13_000_000, 17_000_000,
            )
        ), mock.patch.dict(
            os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": ""}, clear=False
        ):
            self.assertEqual(VERIFIER.run_group(self.root, "api", results, proof, HEAD, COMMIT), 1)
        self.assertEqual(len(commands), 6)
        self.assertEqual(commands[1][:3], ["dotnet", "build", project])
        self.assertIn("--no-restore", commands[1])
        self.assertIn("--list-tests", commands[3])
        self.assertIn("--no-build", commands[3])
        self.assertIn("--no-build", commands[5])
        self.assertEqual(commands[5][-2:], ["--filter", "FullyQualifiedName=" + name])
        row = json.loads(proof.read_text(encoding="utf-8"))["projects"][0]
        self.assertEqual(row["path"], project)
        self.assertEqual(row["stageElapsedMs"], {
            "bootstrapPreflight": 3, "build": 2, "discovery": 3, "test": 4,
        })
        with self.assertRaisesRegex(RuntimeError, "fresh"):
            VERIFIER.run_group(self.root, "api", results, proof, HEAD, COMMIT)

    def test_api_batches_preserve_complete_classes_theories_and_case_equivalent_filters(self) -> None:
        prefix = Path(VERIFIER.GROUP_PROJECTS["api"][0]).stem
        names = Counter({
            f"{prefix}.Alpha.One": 3, f"{prefix}.Alpha.Two": 1,
            f"{prefix}.alpha.Three": 2, f"{prefix}.Beta.One": 1,
        })
        with mock.patch.object(VERIFIER, "API_BATCH_METHOD_BUDGET", 2):
            batches = VERIFIER.api_test_batches(names)
        self.assertEqual(len(batches), 2)
        self.assertEqual(len(batches[0]), 3)  # One complete case-equivalent class.
        self.assertEqual(sum(batches, Counter()), names)
        self.assertEqual(sum(sum(batch.values()) for batch in batches), 7)
        self.assertEqual(VERIFIER.api_test_batches(Counter(dict(reversed(list(names.items()))))),
                         VERIFIER.api_test_batches(names))
        for invalid in (Counter(), Counter({"x|FullyQualifiedName=Other": 1}),
                        Counter({f"{prefix}.Alpha.One": 0}),
                        Counter({f"{prefix}.Alpha.One": True}), Counter({1: 1})):
            with self.subTest(invalid=invalid), self.assertRaises(RuntimeError):
                VERIFIER.api_test_batches(invalid)

    def test_api_batch_runner_requires_exact_full_union_multiplicity_and_unique_execution_ids(self) -> None:
        prefix = Path(VERIFIER.GROUP_PROJECTS["api"][0]).stem
        alpha, beta, extra = (f"{prefix}.{name}.Runs" for name in ("Alpha", "Beta", "Extra"))
        discovery = Counter({alpha: 2, beta: 1})
        modes = ("valid", "missing_batch", "overlap", "extra_result", "too_few_theory_rows",
                 "too_many_theory_rows",
                 "duplicate_execution", "failed_command", "missing_trx")
        for mode in modes:
            with self.subTest(mode=mode):
                proof = self.root / mode / "proof.json"
                results = self.root / mode / "results"
                commands: list[list[str]] = []
                test_commands: list[list[str]] = []

                def command(arguments: list[str], root: Path) -> str:
                    commands.append(arguments)
                    if "--list-tests" in arguments:
                        return "\n".join(name for name, count in discovery.items() for _ in range(count))
                    if "--results-directory" not in arguments:
                        return ""
                    test_commands.append(arguments)
                    number = len(test_commands)
                    selected = arguments[arguments.index("--filter") + 1]
                    names = selected.split("|")
                    self.assertTrue(all(value.startswith("FullyQualifiedName=") for value in names))
                    batch = Counter({value.split("=", 1)[1]: discovery[value.split("=", 1)[1]]
                                     for value in names})
                    if mode == "failed_command" and number == 2:
                        raise RuntimeError("Backend project command exited with code 1.")
                    if mode == "missing_trx" and number == 2:
                        return ""
                    if mode == "extra_result" and number == 2:
                        batch[extra] = 1
                    if mode == "too_few_theory_rows" and number == 1:
                        batch[alpha] = 1
                    if mode == "too_many_theory_rows" and number == 1:
                        batch[alpha] = 3
                    first_execution = 100 if mode == "duplicate_execution" else number * 100
                    logger = next(value for value in arguments if value.startswith("trx;LogFileName="))
                    (results / logger.split("=", 1)[1]).write_text(
                        batch_trx_source(batch, first_execution), encoding="utf-8"
                    )
                    return ""

                planner = VERIFIER.api_test_batches
                if mode == "missing_batch":
                    planner = lambda _: [Counter({alpha: 2})]
                elif mode == "overlap":
                    planner = lambda _: [Counter({alpha: 2}), Counter({alpha: 2})]
                with mock.patch.object(VERIFIER, "API_BATCH_METHOD_BUDGET", 1), mock.patch.object(
                    VERIFIER, "api_test_batches", side_effect=planner
                ), mock.patch.object(VERIFIER, "run_checked", side_effect=command), mock.patch.dict(
                    os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": ""}, clear=False
                ):
                    if mode != "valid":
                        with self.assertRaises(RuntimeError):
                            VERIFIER.run_group(self.root, "api", results, proof, HEAD, COMMIT)
                        self.assertFalse(proof.exists())
                        continue
                    self.assertEqual(VERIFIER.run_group(self.root, "api", results, proof, HEAD, COMMIT), 3)
                row = json.loads(proof.read_text(encoding="utf-8"))["projects"][0]
                self.assertEqual((row["discoveredMethods"], row["discoveredCases"], row["passed"],
                                  row["executionCount"], row["skipped"]), (2, 3, 3, 3, 0))
                self.assertEqual(row["discoveryHash"], VERIFIER.digest(sorted(discovery.items())))
                self.assertEqual(len(test_commands), 2)
                for arguments in test_commands:
                    self.assertIn("--no-build", arguments)
                    self.assertIn("--no-restore", arguments)
                    index = commands.index(arguments)
                    self.assertEqual(commands[index - 1][0], VERIFIER.sys.executable)
                    self.assertIn("tools/validate_dotnet_bootstrap_integrity.py", commands[index - 1])

    def test_build_failure_stops_before_discovery_or_proof(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["api"][0]
        proof = self.root / "artifacts" / "proof-api.json"
        results = self.root / "artifacts" / "results"
        commands: list[list[str]] = []

        def command(arguments: list[str], root: Path) -> str:
            commands.append(arguments)
            if arguments[:3] == ["dotnet", "build", project]:
                raise RuntimeError("Backend project command exited with code 1.")
            return ""

        with mock.patch.object(VERIFIER, "run_checked", side_effect=command), mock.patch.dict(
            os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": ""}, clear=False
        ):
            with self.assertRaisesRegex(RuntimeError, "exited with code 1"):
                VERIFIER.run_group(self.root, "api", results, proof, HEAD, COMMIT)
        self.assertEqual(len(commands), 2)
        self.assertFalse(proof.exists())

    def test_group_refuses_real_sql_connection_before_dispatch(self) -> None:
        with mock.patch.dict(
            os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": "not-a-real-connection"}
        ), mock.patch.object(VERIFIER, "run_checked") as runner:
            with self.assertRaisesRegex(RuntimeError, "separate real-SQL"):
                VERIFIER.run_group(
                    self.root, "api", self.root / "results", self.root / "proof.json", HEAD, COMMIT
                )
            runner.assert_not_called()


if __name__ == "__main__":
    unittest.main()
