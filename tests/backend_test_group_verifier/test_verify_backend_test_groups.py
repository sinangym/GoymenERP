from __future__ import annotations

import importlib.util
from collections import Counter
import json
import copy
import hashlib
import uuid
import os
from pathlib import Path
import py_compile
import shutil
import tempfile
import unittest
from unittest import mock
from xml.sax.saxutils import quoteattr


SOURCE = Path(__file__).resolve().parents[2] / "tools" / "verify_backend_test_groups.py"
SPEC = importlib.util.spec_from_file_location("verify_backend_test_groups", SOURCE)
assert SPEC is not None and SPEC.loader is not None
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)
HEAD = "a" * 40
COMMIT = "b" * 40
FIXTURE_ASSEMBLY = Path("/fixture/UnitTests.dll").resolve()


def make_repository(root: Path) -> None:
    source_root = SOURCE.parent.parent
    for relative in ("tools/verify_rcsi_relational_acceptance.py",):
        destination = root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source_root / relative, destination)
    shutil.copytree(source_root / "tools/relational_inventory", root / "tools/relational_inventory")
    for relative, _, _ in VERIFIER.DEFERRED_THEORIES.values():
        destination = root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source_root / relative, destination)
    projects = [path for group in VERIFIER.GROUP_PROJECTS.values() for path in group]
    projects += [f"src/backend/Other{i:02d}/Other{i:02d}.csproj" for i in range(51)]
    solution = ["<Solution>", '  <Folder Name="/tests/">']
    for path in projects:
        project = root / path
        project.parent.mkdir(parents=True, exist_ok=True)
        references = (
            '<ItemGroup>' + "".join(
                f'<PackageReference Include="{package}" Version="{VERIFIER.FRAMEWORK_PACKAGES[package][0]}" />'
                for package in ("Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio")
            ) + '</ItemGroup>'
            if path in {p for group in VERIFIER.GROUP_PROJECTS.values() for p in group}
            else ""
        )
        project.write_text(f'<Project Sdk="Microsoft.NET.Sdk">{references}</Project>', encoding="utf-8")
        if references:
            assets = project.parent / "obj/project.assets.json"
            assets.parent.mkdir()
            assets.write_text(json.dumps({
                "project": {"restore": {"projectPath": str(project)}},
                "libraries": {package + "/" + version: {"sha512": fingerprint}
                              for package, (version, fingerprint) in VERIFIER.FRAMEWORK_PACKAGES.items()},
            }), encoding="utf-8")
        solution.append(f'    <Project Path="{path}" />')
    solution += ["  </Folder>", "</Solution>"]
    (root / "GoymenERP.slnx").write_text("\n".join(solution), encoding="utf-8")


def case_identity(name: str, display: str, index: int = 0) -> tuple[str, str]:
    seed = name if name in VERIFIER.DEFERRED_THEORIES else json.dumps([display, index])
    unique = hashlib.sha1(seed.encode()).hexdigest()
    identity = str(uuid.UUID(bytes_le=hashlib.sha1(
        (VERIFIER.EXECUTOR_URI + unique).encode("utf-16-le")
    ).digest()[:16]))
    return identity, unique


def diagnostic_message(root: Path, project: str, displays: list[str]) -> dict:
    source = str((root / Path(project).parent / "bin/Release/net10.0" /
                  (Path(project).stem + ".dll")).resolve())
    counts = Counter()
    cases = []
    for display in displays:
        name = display.split("(", 1)[0]
        identity, unique = case_identity(name, display, counts[name])
        counts[name] += 1
        cases.append({
            "Id": identity, "FullyQualifiedName": name, "DisplayName": display,
            "ExecutorUri": VERIFIER.EXECUTOR_URI, "Source": source,
            "CodeFilePath": None, "LineNumber": -1,
            "Properties": [{"Key": {"Id": key}, "Value": value} for key, value in {
                "XunitTestCaseUniqueID": unique,
                "TestCase.ManagedType": name.rsplit(".", 1)[0],
                "TestCase.ManagedMethod": name.rsplit(".", 1)[1],
            }.items()],
        })
    return {"Version": 7, "MessageType": "TestDiscovery.Completed", "Payload": {
        "TotalTests": len(cases), "LastDiscoveredTests": cases, "IsAborted": False,
        "Metrics": {}, "FullyDiscoveredSources": [source], "PartiallyDiscoveredSources": [],
        "NotDiscoveredSources": [], "SkippedDiscoverySources": [], "DiscoveredExtensions": {},
    }}


def write_diagnostic(path: Path, messages: list[dict]) -> None:
    prefix = "TpTrace Verbose: 0 : 1234, 10, 2026/10/24, 09:14:38.778, 15859071002409, "
    path.write_text("\n".join(prefix + VERIFIER.DISCOVERY_BOUNDARY + json.dumps(message)
                              for message in messages), encoding="utf-8")


def mock_discovery(arguments: list[str], root: Path, project: str, displays: list[str]) -> str:
    write_diagnostic(Path(arguments[arguments.index("--diag") + 1]),
                     [diagnostic_message(root, project, displays)])
    return "\n".join(displays)


def trx_source(project: str, *, outcome: str = "Passed", theory_rows: int = 1,
               source: Path = FIXTURE_ASSEMBLY) -> str:
    name = Path(project).stem + ".ExampleTests.Runs"
    return batch_trx_source(Counter({name: theory_rows}), 1, skipped=outcome == "NotExecuted", source=source).replace(
        'outcome="Passed"', f'outcome="{outcome}"'
    )


def project_row(path: str) -> dict[str, object]:
    name = Path(path).stem + ".ExampleTests.Runs"
    identity, _ = case_identity(name, name)
    return {
        "path": path, "assemblyPath": VERIFIER.assembly_relative_path(path),
        "executorUri": VERIFIER.EXECUTOR_URI, "discoveryHash": VERIFIER.digest([(name, 1)]),
        "resultHash": VERIFIER.digest([(name, 1, 0)]),
        "discoveredMethods": 1, "discoveredCases": 1, "executionCount": 1,
        "passed": 1, "skipped": 0,
        "stageElapsedMs": {stage: 0 for stage in VERIFIER.STAGES},
        "testCases": [[name, 1, 1, 0, [identity], VERIFIER.digest([identity]), VERIFIER.digest([name])]],
    }


def batch_trx_source(
    methods: Counter[str], first_execution: int, *, skipped: bool = False,
    displays: dict[str, list[str]] | None = None, source: Path = FIXTURE_ASSEMBLY,
) -> str:
    results = []
    definitions = {}
    execution = first_execution
    for name, count in methods.items():
        class_name, method = name.rsplit(".", 1)
        for case in range(count):
            display = displays[name][case] if displays is not None else name
            identity, _ = case_identity(name, display, case)
            definitions[identity] = (
                f'<UnitTest id="{identity}" storage={quoteattr(str(source.resolve()).lower())}><TestMethod '
                f'codeBase={quoteattr(str(source.resolve()))} adapterTypeName={quoteattr(VERIFIER.EXECUTOR_URI)} className="{class_name}" '
                f'name="{method}" /></UnitTest>'
            )
            results.append(
                f'<UnitTestResult testId="{identity}" '
                f'executionId="12345678-1234-4abc-8def-{execution:012x}" '
                f'testName={quoteattr(display)} outcome="{"NotExecuted" if skipped else "Passed"}" />'
            )
            execution += 1
    total = sum(methods.values())
    passed = 0 if skipped else total
    return (
        '<TestRun><Counters '
        f'total="{total}" executed="{passed}" passed="{passed}" '
        'notExecuted="0" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" />'
        f'<Results>{"".join(results)}</Results>'
        f'<TestDefinitions>{"".join(definitions.values())}</TestDefinitions></TestRun>'
    )


class BackendGroupVerifierTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        make_repository(self.root)
        self.sql_directory = self.root / "sql-proofs"
        sql = VERIFIER.relational_verifier(self.root)
        for shard in sql.SHARD_MODULES:
            artifact = self.sql_directory / f"ci-relational-shard-proof-{shard}"
            artifact.mkdir(parents=True)
            (artifact / f"proof-{shard}.json").write_text(
                json.dumps(sql.expected_shard_proof(shard, HEAD, COMMIT)), encoding="utf-8"
            )

    def aggregate(self, directory: Path, head: str = HEAD, commit: str = COMMIT,
                  build: str = "success", workers: str = "success",
                  sql_workers: str = "success", rcsi: str = "success") -> int:
        return VERIFIER.verify_aggregate(
            self.root, directory, head, commit, build, workers,
            self.sql_directory, sql_workers, rcsi,
        )

    def produce_group(
        self, group: str, directory: Path, project: str, discovered_names: list[str],
        executed_names: list[str], *, skipped: bool = False,
    ) -> Path:
        """Exercise the real producer and its serialized proof, faking only TestSDK commands."""
        results = self.root / "producer-results" / f"{directory.name}-{group}"
        proof = directory / f"ci-backend-test-group-proof-{group}" / f"proof-{group}.json"

        def command(arguments: list[str], root: Path) -> str:
            if len(arguments) < 3 or arguments[0] != "dotnet":
                return ""
            current = arguments[2]
            listed = discovered_names if current == project else [Path(current).stem + ".ExampleTests.Runs"]
            if "--list-tests" in arguments:
                return mock_discovery(arguments, root, current, listed)
            if "--results-directory" not in arguments:
                return ""
            displays = executed_names if current == project else listed
            names: dict[str, list[str]] = {}
            for display in displays:
                names.setdefault(display.split("(", 1)[0], []).append(display)
            if "--filter" in arguments:
                selected = arguments[arguments.index("--filter") + 1].split("|")
                names = {name: values for name, values in names.items()
                         if "FullyQualifiedName=" + name in selected}
            logger = next(value for value in arguments if value.startswith("trx;LogFileName="))
            (results / logger.split("=", 1)[1]).write_text(
                batch_trx_source(Counter({name: len(values) for name, values in names.items()}),
                                 100, skipped=skipped and current == project, displays=names,
                                 source=root / VERIFIER.assembly_relative_path(current)),
                encoding="utf-8",
            )
            return ""

        with mock.patch.object(VERIFIER, "run_checked", side_effect=command), mock.patch.dict(
            os.environ, {"GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": ""}, clear=False
        ):
            VERIFIER.run_group(self.root, group, results, proof, HEAD, COMMIT)
        return proof

    @staticmethod
    def recount(row: dict[str, object]) -> None:
        tests = row["testCases"]
        row.update(
            discoveredMethods=len(tests), discoveredCases=sum(record[1] for record in tests),
            passed=sum(record[2] for record in tests), skipped=sum(record[3] for record in tests),
            executionCount=sum(record[2] + record[3] for record in tests),
            discoveryHash=VERIFIER.digest([(record[0], record[1]) for record in tests]),
            resultHash=VERIFIER.digest([(record[0], record[2], record[3]) for record in tests]),
        )

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

    def test_separate_sql_inventory_executes_current_source_and_ignores_valid_foreign_bytecode(self) -> None:
        path = self.root / "tools/verify_rcsi_relational_acceptance.py"
        source = path.read_bytes()
        foreign = self.root / "foreign.py"
        foreign.write_bytes(source + b"\n_IGNORED_FOREIGN_SQL_BYTECODE = True\n")
        cached = Path(importlib.util.cache_from_source(str(path)))
        cached.parent.mkdir(parents=True, exist_ok=True)
        py_compile.compile(str(foreign), cfile=str(cached), dfile=str(path), doraise=True,
                           invalidation_mode=py_compile.PycInvalidationMode.TIMESTAMP)
        payload = bytearray(cached.read_bytes())
        payload[8:12] = int(path.stat().st_mtime).to_bytes(4, "little")
        payload[12:16] = len(source).to_bytes(4, "little")
        cached.write_bytes(payload)
        self.assertFalse(hasattr(VERIFIER.relational_verifier(self.root),
                                 "_IGNORED_FOREIGN_SQL_BYTECODE"))

    def test_sql_catalog_requires_the_existing_exact_rcsi_subset_without_double_counting(self) -> None:
        sql = VERIFIER.relational_verifier(self.root)
        self.assertEqual(VERIFIER.relational_cases(self.root), sql.BROAD_EXECUTIONS)
        name = sql.EXPECTED_TESTS[0]
        path = self.root / "tools/relational_inventory/platform.txt"
        original = path.read_text(encoding="utf-8")
        for changed in (
            original.replace(name + "\n", ""),
            original.replace(name + "\n", name + "  x2  # unintended duplicate probe\n"),
        ):
            with self.subTest(inventory=changed[-80:]):
                path.write_text(changed, encoding="utf-8", newline="\n")
                with self.assertRaisesRegex(RuntimeError, "every exact RCSI case once"):
                    VERIFIER.relational_cases(self.root)

    def test_all_groups_require_exact_enumerated_multiplicity_and_reject_non_sql_skip(self) -> None:
        path = self.root / "exact.trx"
        for group in VERIFIER.GROUP_PROJECTS:
            project = VERIFIER.GROUP_PROJECTS[group][0]
            name = Path(project).stem + ".ExampleTests.Runs"
            for cases in (3, 5):
                with self.subTest(group=group, cases=cases):
                    path.write_text(trx_source(project, theory_rows=cases), encoding="utf-8")
                    with self.assertRaisesRegex(RuntimeError, "multiplicity differs"):
                        VERIFIER.trx_result_map(path, Counter({name: 4}), expected_source=FIXTURE_ASSEMBLY)
            path.write_text(trx_source(project, theory_rows=4), encoding="utf-8")
            _, passed, skipped = VERIFIER.trx_result_map(path, Counter({name: 4}), expected_source=FIXTURE_ASSEMBLY)
            self.assertEqual((passed, skipped), (4, 0))
            path.write_text(trx_source(project, outcome="NotExecuted"), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "Required non-SQL"):
                VERIFIER.trx_result_map(path, Counter({name: 1}), expected_source=FIXTURE_ASSEMBLY)

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
            trx_source(project).replace('testId="', 'testId="missing'),
        ):
            with self.subTest(source=source[:80]):
                path.write_text(source, encoding="utf-8")
                with self.assertRaises(RuntimeError):
                    VERIFIER.trx_result_map(path, discovered, expected_source=FIXTURE_ASSEMBLY)
        path.write_text(trx_source(project), encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "identities differ"):
            VERIFIER.trx_result_map(path, VERIFIER.discovery_map(name + "\n" + name + ".Other\n", project), expected_source=FIXTURE_ASSEMBLY)

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
                    VERIFIER.trx_result_map(path, discovered, expected_source=FIXTURE_ASSEMBLY)

    def proof_directory(self, name: str = "proofs") -> Path:
        directory = self.root / "artifacts" / name
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
            self.aggregate(directory),
            20,
        )
        for build, workers in (("failure", "success"), ("success", "skipped")):
            with self.subTest(build=build, workers=workers):
                with self.assertRaisesRegex(RuntimeError, "must succeed"):
                    self.aggregate(directory, build=build, workers=workers)
        with self.assertRaisesRegex(RuntimeError, "does not match"):
            self.aggregate(directory, head="f" * 40)

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
            original.replace('"projectCount": 1', '"projectCount": true', 1),
            original.replace('"projectCount": 1', '"projectCount": 1.0', 1),
            original.replace('"schemaVersion": 4', '"schemaVersion": 3', 1),
            original.replace('"schemaVersion": 4', '"schemaVersion": 4.0', 1),
            original.replace('"group": "api"', '"group": "modules"', 1),
            original.replace('"group": "api"', '"group": "api", "group": "api"', 1),
        ):
            with self.subTest(mutation=mutation[-80:]):
                path.write_text(mutation, encoding="utf-8")
                with self.assertRaises(RuntimeError):
                    self.aggregate(directory)
        path.write_text(original, encoding="utf-8")
        unexpected = directory / "ci-backend-test-group-proof-unknown"
        unexpected.mkdir()
        with self.assertRaisesRegex(RuntimeError, "missing, duplicated or unknown"):
            self.aggregate(directory)
        unexpected.rmdir()
        path.unlink()
        with self.assertRaisesRegex(RuntimeError, "contents are missing"):
            self.aggregate(directory)

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
                    self.aggregate(directory)

    def test_group_runner_discovers_then_executes_exact_project_and_writes_safe_proof(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["api"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        commands: list[list[str]] = []

        def command(arguments: list[str], root: Path) -> str:
            commands.append(arguments)
            if "--list-tests" in arguments:
                return mock_discovery(arguments, root, project, [name])
            if arguments[0] == "dotnet" and "--results-directory" in arguments:
                directory = Path(arguments[arguments.index("--results-directory") + 1])
                directory.mkdir(parents=True, exist_ok=True)
                logger = next(value for value in arguments if value.startswith("trx;LogFileName="))
                (directory / logger.split("=", 1)[1]).write_text(
                    trx_source(project, source=root / VERIFIER.assembly_relative_path(project)), encoding="utf-8"
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
                        return mock_discovery(arguments, root, VERIFIER.GROUP_PROJECTS["api"][0], [name for name, count in discovery.items() for _ in range(count)])
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
                        batch_trx_source(batch, first_execution, source=root / VERIFIER.assembly_relative_path(VERIFIER.GROUP_PROJECTS["api"][0])), encoding="utf-8"
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

    def test_real_producer_and_consumer_reject_required_non_sql_skip(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["api"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        directory = self.proof_directory()
        path = directory / "ci-backend-test-group-proof-api" / "proof-api.json"
        path.unlink()
        with self.assertRaisesRegex(RuntimeError, "Required non-SQL"):
            self.produce_group("api", directory, project, [name], [name], skipped=True)
        self.assertFalse(path.exists())
        directory = self.proof_directory("executed")
        path = directory / "ci-backend-test-group-proof-api" / "proof-api.json"
        path.unlink()
        path = self.produce_group("api", directory, project, [name], [name])
        self.assertEqual(self.aggregate(directory), 20)
        proof = json.loads(path.read_text(encoding="utf-8"))
        row = proof["projects"][0]
        row["testCases"][0][2:4] = [0, 1]
        self.recount(row)
        path.write_text(json.dumps(proof), encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "Required non-SQL"):
            self.aggregate(directory)

    def test_separate_sql_skip_requires_exact_inventory_and_same_source_complete_proofs(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["api"][0]
        name = sorted(VERIFIER.relational_cases(self.root))[0]
        directory = self.proof_directory()
        path = directory / "ci-backend-test-group-proof-api" / "proof-api.json"
        path.unlink()
        path = self.produce_group("api", directory, project, [name], [name], skipped=True)
        self.assertEqual(self.aggregate(directory), 20)
        original = path.read_text(encoding="utf-8")
        for foreign in (name + "Unknown", "GoymenERP.Api.IntegrationTests.OrdinaryTests.Required"):
            with self.subTest(foreign=foreign):
                proof = json.loads(original)
                row = proof["projects"][0]
                row["testCases"][0][0] = foreign
                row["testCases"][0][6] = VERIFIER.digest([foreign])
                self.recount(row)
                path.write_text(json.dumps(proof), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "Required non-SQL"):
                    self.aggregate(directory)
                with self.assertRaisesRegex(RuntimeError, "Required non-SQL"):
                    self.produce_group("api", self.root / foreign.rsplit(".", 1)[-1],
                                       project, [foreign], [foreign], skipped=True)
        path.write_text(original, encoding="utf-8")
        for sql_workers, rcsi in (("failure", "success"), ("success", "skipped")):
            with self.subTest(sql_workers=sql_workers, rcsi=rcsi):
                with self.assertRaisesRegex(RuntimeError, "must succeed"):
                    self.aggregate(directory, sql_workers=sql_workers, rcsi=rcsi)
        sql_path = self.sql_directory / "ci-relational-shard-proof-A" / "proof-A.json"
        sql_original = sql_path.read_text(encoding="utf-8")
        for field in ("sourceHead", "testedCommit", "executionMapHash", "catalogHash"):
            with self.subTest(foreign_field=field):
                proof = json.loads(sql_original)
                proof[field] = "f" * len(proof[field])
                sql_path.write_text(json.dumps(proof), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "does not match"):
                    self.aggregate(directory)
        sql_path.unlink()
        with self.assertRaisesRegex(RuntimeError, "missing"):
            self.aggregate(directory)

    def test_non_api_enumerated_theory_requires_exact_cases_at_producer_and_consumer(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["modules"][12]
        name = Path(project).stem + ".ProductSalesEligibilityServiceTests.Missing_server_scope_denies_before_any_owner_read"
        expected = [f"{name}(value: {index})" for index in range(4)]
        for count in (3, 5):
            with self.subTest(count=count):
                with self.assertRaisesRegex(RuntimeError, "multiplicity differs"):
                    self.produce_group("modules", self.root / f"bad-{count}", project,
                                       expected, [f"{name}(value: {index})" for index in range(count)])
        for label, rows in (
            ("duplicate", expected[:-1] + [expected[0]]),
            ("unknown", expected[:-1] + [f"{name}(value: 99)"]),
        ):
            with self.subTest(identity=label), self.assertRaisesRegex(RuntimeError, "case identities"):
                self.produce_group("modules", self.root / label, project, expected, rows)
        directory = self.proof_directory()
        path = directory / "ci-backend-test-group-proof-modules" / "proof-modules.json"
        path.unlink()
        path = self.produce_group("modules", directory, project, expected, expected)
        self.assertEqual(self.aggregate(directory), 23)
        original = path.read_text(encoding="utf-8")
        for count in (3, 5):
            with self.subTest(aggregate_count=count):
                proof = json.loads(original)
                row = proof["projects"][12]
                row["testCases"][0][2] = count
                self.recount(row)
                path.write_text(json.dumps(proof), encoding="utf-8")
                with self.assertRaisesRegex(RuntimeError, "multiplicity"):
                    self.aggregate(directory)
        proof = json.loads(original)
        row = proof["projects"][12]
        row["testCases"][0][5] = VERIFIER.digest(sorted(expected[:-1] + [expected[0]]))
        path.write_text(json.dumps(proof), encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "multiplicity or identity"):
            self.aggregate(directory)

    def test_native_ids_reject_lossy_suffix_escape_and_unicode_substitutions(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        for number, (first, second) in enumerate((
            ("x" * 500 + "a", "x" * 500 + "b"),
            ("\t", r"\t"), ("\r\n", r"\r\n"),
            ("x" * 443 + "\U0001f600" + "tail", "x" * 443 + "\U0001f601" + "tail"),
        )):
            expected = [name + '(value: "' + first + '")']
            other = [name + '(value: "' + second + '")']
            with self.subTest(number=number):
                with self.assertRaisesRegex(RuntimeError, "native case identities"):
                    self.produce_group("architecture", self.root / f"substitute-{number}", project,
                                       expected, other)
                directory = self.proof_directory(f"exact-{number}")
                (directory / "ci-backend-test-group-proof-architecture/proof-architecture.json").unlink()
                self.produce_group("architecture", directory, project, expected, expected)
                self.assertEqual(self.aggregate(directory), 20)

    def test_structured_discovery_requires_one_complete_native_stream(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        original = diagnostic_message(self.root, project, [name + "(x: 1)", name + "(x: 2)"])
        path = self.root / "discovery.log"
        cases = original["Payload"]["LastDiscoveredTests"]
        expected = {name: sorted(case["Id"] for case in cases)}
        write_diagnostic(path, [original])
        self.assertEqual(VERIFIER.structured_discovery(path, self.root, project), expected)
        # Genuine protocol permits disjoint incremental batches and a final tail.
        split = copy.deepcopy(original)
        split["Payload"]["LastDiscoveredTests"] = [cases[1]]
        found = {"Version": 7, "MessageType": "TestDiscovery.TestFound", "Payload": [cases[0]]}
        write_diagnostic(path, [found, split])
        self.assertEqual(VERIFIER.structured_discovery(path, self.root, project), expected)
        mutations = []
        for label, mutate in (
            ("version", lambda d: d.update(Version=8)),
            ("boolversion", lambda d: d.update(Version=True)),
            ("unknown", lambda d: d.update(MessageType="TestDiscovery.Unknown")),
            ("aborted", lambda d: d["Payload"].update(IsAborted=True)),
            ("partial", lambda d: d["Payload"].update(PartiallyDiscoveredSources=["other.dll"])),
            ("count", lambda d: d["Payload"].update(TotalTests=3)),
            ("missing", lambda d: d["Payload"].update(LastDiscoveredTests=[cases[0]])),
            ("duplicate", lambda d: d["Payload"].update(LastDiscoveredTests=[cases[0], cases[0]])),
            ("foreign", lambda d: d["Payload"]["LastDiscoveredTests"][0].update(Source="/foreign.dll")),
            ("executor", lambda d: d["Payload"]["LastDiscoveredTests"][0].update(ExecutorUri="executor://foreign/")),
            ("id", lambda d: d["Payload"]["LastDiscoveredTests"][0].update(Id=str(uuid.uuid4()))),
            ("nil", lambda d: d["Payload"]["LastDiscoveredTests"][0].update(Id=str(uuid.UUID(int=0)))),
            ("surrogate", lambda d: d["Payload"]["LastDiscoveredTests"][0].update(DisplayName="x" * 443 + "\ud83d" + "\u00b7" * 3)),
        ):
            changed = copy.deepcopy(original)
            mutate(changed)
            mutations.append((label, [changed]))
        mutations += [("echo", [original, original]), ("no-completion", [found]),
                      ("overlap-batch", [found, original])]
        for label, messages in mutations:
            with self.subTest(label=label):
                write_diagnostic(path, messages)
                with self.assertRaises(RuntimeError):
                    VERIFIER.structured_discovery(path, self.root, project)
        # A host SEND echo is never a console RECEIVE event.
        write_diagnostic(path, [original])
        path.write_text(path.read_text().replace(VERIFIER.DISCOVERY_BOUNDARY,
                       "testhost.dll, TestRequestHandler.SendData: sending data from testhost: "))
        with self.assertRaises(RuntimeError):
            VERIFIER.structured_discovery(path, self.root, project)

    def test_framework_source_assets_versions_hashes_and_project_are_bound(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        source = self.root / project
        original = source.read_text()
        original_hash = VERIFIER.framework_inventory(self.root)
        VERIFIER.verify_framework_assets(self.root, project)
        source.write_text(original + "\n<!-- source change -->\n")
        self.assertNotEqual(VERIFIER.framework_inventory(self.root), original_hash)
        for before, after in (("3.1.4", "3.1.5"), ("17.14.1", "17.14.2"), ("2.9.3", "2.9.4")):
            source.write_text(original.replace(before, after))
            with self.assertRaisesRegex(RuntimeError, "framework version"):
                VERIFIER.expected_proof("architecture", HEAD, COMMIT, self.root)
        source.write_text(original)
        path = source.parent / "obj/project.assets.json"
        assets = json.loads(path.read_text())
        for label, mutate in (
            ("hash", lambda a: a["libraries"]["xunit.runner.visualstudio/3.1.4"].update(sha512="foreign")),
            ("mixed", lambda a: a["libraries"].update({"xunit.runner.visualstudio/3.1.5": {}})),
            ("missing", lambda a: a["libraries"].pop("Microsoft.TestPlatform.TestHost/17.14.1")),
            ("project", lambda a: a["project"]["restore"].update(projectPath="/foreign.csproj")),
        ):
            with self.subTest(label=label):
                changed = copy.deepcopy(assets)
                mutate(changed)
                path.write_text(json.dumps(changed))
                with self.assertRaises(RuntimeError):
                    VERIFIER.verify_framework_assets(self.root, project)
        path.write_text(json.dumps(assets))
        directory = self.proof_directory()
        proof_path = directory / "ci-backend-test-group-proof-architecture/proof-architecture.json"
        proof = json.loads(proof_path.read_text())
        proof["frameworkInventoryHash"] = "f" * 64
        proof_path.write_text(json.dumps(proof))
        with self.assertRaisesRegex(RuntimeError, "does not match"):
            self.aggregate(directory)

    def test_native_identity_replacement_cannot_hide_behind_counts_or_new_execution_ids(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        displays = [name + "(x: 1)", name + "(x: 2)"]
        identities = [case_identity(name, display, index)[0] for index, display in enumerate(displays)]
        path = self.root / "native.trx"
        source = batch_trx_source(Counter({name: 2}), 1, displays={name: displays})
        path.write_text(source)
        VERIFIER.trx_result_map(path, Counter({name: 2}), expected_case_ids={name: identities}, expected_source=FIXTURE_ASSEMBLY)
        for replacement in (identities[0], str(uuid.uuid4())):
            with self.subTest(replacement=replacement):
                path.write_text(source.replace('testId="' + identities[1] + '"',
                                               'testId="' + replacement + '"'))
                with self.assertRaises(RuntimeError):
                    VERIFIER.trx_result_map(path, Counter({name: 2}), expected_case_ids={name: identities}, expected_source=FIXTURE_ASSEMBLY)

    def test_aggregate_native_id_multiset_cannot_be_replaced_or_duplicated(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        displays = [name + "(x: 1)", name + "(x: 2)"]
        directory = self.proof_directory()
        path = directory / "ci-backend-test-group-proof-architecture/proof-architecture.json"
        path.unlink()
        self.produce_group("architecture", directory, project, displays, displays)
        original = json.loads(path.read_text())
        for label in ("duplicate", "replace", "wrong-runtime"):
            with self.subTest(label=label):
                proof = copy.deepcopy(original)
                row = proof["projects"][0]
                record = row["testCases"][0]
                if label == "duplicate":
                    record[4] = [record[4][0]] * 2
                    record[5] = VERIFIER.digest(record[4])
                elif label == "replace":
                    record[4] = sorted([record[4][0], str(uuid.uuid4())])
                else:
                    record[5] = VERIFIER.digest([record[4][0]] * 2)
                self.recount(row)
                path.write_text(json.dumps(proof))
                with self.assertRaises(RuntimeError):
                    self.aggregate(directory)

    def test_producer_and_consumer_bind_runtime_assembly_and_executor(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        original_builder = batch_trx_source
        mutations = (
            ("storage-foreign", 'storage="', 'storage="/foreign/'),
            ("storage-missing", 'storage=', 'ignoredStorage='),
            ("codebase-foreign", 'codeBase="', 'codeBase="/foreign/'),
            ("codebase-missing", 'codeBase=', 'ignoredCodeBase='),
            ("executor-foreign", VERIFIER.EXECUTOR_URI, "executor://foreign/"),
            ("executor-missing", 'adapterTypeName=', 'ignoredAdapter='),
        )
        for label, before, after in mutations:
            with self.subTest(label=label):
                directory = self.root / label
                def foreign_trx(*args, **kwargs):
                    return original_builder(*args, **kwargs).replace(before, after)
                with mock.patch.dict(globals(), {"batch_trx_source": foreign_trx}):
                    with self.assertRaisesRegex(RuntimeError, "provenance differs"):
                        self.produce_group("architecture", directory, project, [name], [name])
                self.assertFalse((directory / "ci-backend-test-group-proof-architecture/proof-architecture.json").exists())
        directory = self.proof_directory()
        path = directory / "ci-backend-test-group-proof-architecture/proof-architecture.json"
        path.unlink()
        self.produce_group("architecture", directory, project, [name], [name])
        self.assertEqual(self.aggregate(directory), 20)
        original = json.loads(path.read_text())
        for field in ("assemblyPath", "executorUri"):
            for replacement in (None, "/foreign/Other.dll"):
                with self.subTest(field=field, replacement=replacement):
                    changed = copy.deepcopy(original)
                    if replacement is None:
                        del changed["projects"][0][field]
                    else:
                        changed["projects"][0][field] = replacement
                    path.write_text(json.dumps(changed))
                    with self.assertRaises(RuntimeError):
                        self.aggregate(directory)

    def test_runtime_provenance_has_no_basename_casefold_or_unused_definition_fallback(self) -> None:
        project = VERIFIER.GROUP_PROJECTS["architecture"][0]
        name = Path(project).stem + ".ExampleTests.Runs"
        identity = case_identity(name, name)[0]
        source = trx_source(project)
        path = self.root / "runtime-provenance.trx"
        for label, changed in (
            ("basename", source.replace(str(FIXTURE_ASSEMBLY), str(FIXTURE_ASSEMBLY.parent / "foreign/UnitTests.dll"))),
            ("codebase-casefold", source.replace('codeBase="' + str(FIXTURE_ASSEMBLY), 'codeBase="' + str(FIXTURE_ASSEMBLY).upper())),
            ("storage-casefold", source.replace('storage="' + str(FIXTURE_ASSEMBLY).lower(), 'storage="' + str(FIXTURE_ASSEMBLY).upper())),
            ("unused-definition", source.replace('</TestDefinitions>',
                '<UnitTest id="22222222-2222-2222-2222-222222222222" storage=' + quoteattr(str(FIXTURE_ASSEMBLY).lower()) +
                '><TestMethod codeBase=' + quoteattr(str(FIXTURE_ASSEMBLY)) +
                ' adapterTypeName=' + quoteattr(VERIFIER.EXECUTOR_URI) +
                ' className="GoymenERP.ArchitectureTests.OtherTests" name="Runs" /></UnitTest></TestDefinitions>')),
        ):
            with self.subTest(label=label):
                path.write_text(changed)
                with self.assertRaises(RuntimeError):
                    VERIFIER.trx_result_map(path, Counter({name: 1}), expected_source=FIXTURE_ASSEMBLY,
                                            expected_case_ids={name: [identity]})

    def test_reviewed_deferred_case_identities_and_source_are_exact_at_both_gates(self) -> None:
        self.assertEqual(len(VERIFIER.DEFERRED_THEORIES), 2)
        for number, (name, (relative, _, cases)) in enumerate(VERIFIER.DEFERRED_THEORIES.items()):
            project = next(path for path in VERIFIER.GROUP_PROJECTS["modules"]
                           if name.startswith(Path(path).stem + "."))
            expected = list(cases)
            mutations = {
                "missing": expected[:-1], "surplus": expected + [expected[0]],
                "duplicate": expected[:-1] + [expected[0]],
                "wrong": expected[:-1] + [name + "(unknown: 999)"],
            }
            for label, rows in mutations.items():
                with self.subTest(theory=name, producer=label), self.assertRaises(RuntimeError):
                    self.produce_group("modules", self.root / f"deferred-{number}-{label}",
                                       project, [name], rows)
            directory = self.proof_directory(f"deferred-{number}")
            path = directory / "ci-backend-test-group-proof-modules" / "proof-modules.json"
            path.unlink()
            path = self.produce_group("modules", directory, project, [name], expected)
            self.assertEqual(self.aggregate(directory), 19 + len(expected))
            original = path.read_text(encoding="utf-8")
            for label, rows in mutations.items():
                with self.subTest(theory=name, aggregate=label):
                    proof = json.loads(original)
                    row = next(row for row in proof["projects"] if row["path"] == project)
                    row["testCases"][0][2] = len(rows)
                    row["testCases"][0][6] = VERIFIER.digest(sorted(rows))
                    self.recount(row)
                    path.write_text(json.dumps(proof), encoding="utf-8")
                    with self.assertRaisesRegex(RuntimeError, "multiplicity or identity"):
                        self.aggregate(directory)
            path.write_text(original, encoding="utf-8")
            source = self.root / relative
            original_source = source.read_bytes()
            try:
                source.write_bytes(original_source + b"\n// unreviewed test data change\n")
                with self.assertRaisesRegex(RuntimeError, "Deferred theory source differs"):
                    self.aggregate(directory)
            finally:
                source.write_bytes(original_source)

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
