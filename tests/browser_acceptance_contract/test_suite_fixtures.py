"""BROWSER-CI stage 2 (a2 part 2): the suite fixture runner's contract.

The runner (tests/GoymenERP.BrowserSuiteFixtureTool) hands each suite owner's seed the real API host;
these tests pin what can be checked without .NET: the closed fixture list against the owner seed
directories, no SQL in owner seeds, the runner's SQL limited to counting, the same disposable-run rules
as the C-01 tool, the seed output rules, and the workflow wiring.
"""
import contextlib
import importlib.util
import io
import json
import pathlib
import re
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))

import validate_browser_acceptance as browser  # noqa: E402

_spec = importlib.util.spec_from_file_location(
    "suite_profile_tests", pathlib.Path(__file__).resolve().parent / "test_suite_profiles.py")
profile_tests = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(profile_tests)

TOOL = ROOT / "tests/GoymenERP.BrowserSuiteFixtureTool"
SUITES_DIRECTORY = ROOT / browser.FIXTURE_TOOL_SUITES_DIRECTORY
WORKFLOW = (ROOT / ".github/workflows/browser-acceptance.yml").read_text(encoding="utf-8")
# SQL write or query surfaces an owner seed must never touch (it goes through endpoints and services).
FORBIDDEN_IN_SEEDS = (
    "Microsoft.Data.SqlClient", "SqlConnection", "SqlCommand", "ExecuteSql", "FromSql",
    "IgnoreQueryFilters", "DbContext", ".Database.", "System.Data.",
)


def seed_sources():
    if not SUITES_DIRECTORY.is_dir():
        return []
    return sorted(SUITES_DIRECTORY.rglob("*.cs"))


class FixtureListTests(unittest.TestCase):
    def test_the_closed_fixture_list_is_exactly_the_owner_seed_directories(self):
        directories = (
            {path.name.lower() for path in SUITES_DIRECTORY.iterdir() if path.is_dir()}
            if SUITES_DIRECTORY.is_dir() else set()
        )
        self.assertEqual(set(browser.SUITE_FIXTURE_OUTPUTS), directories)
        for suite in directories:
            with self.subTest(suite=suite):
                self.assertIn(suite, browser.SUITES)
                sources = "\n".join(path.read_text(encoding="utf-8")
                                    for path in (SUITES_DIRECTORY / suite.upper()).rglob("*.cs"))
                self.assertIn(f'"{suite}"', sources)

    def test_every_output_key_is_safe(self):
        for suite, keys in browser.SUITE_FIXTURE_OUTPUTS.items():
            for key in keys:
                with self.subTest(suite=suite, key=key):
                    self.assertRegex(key, r"^[a-z][A-Za-z0-9]{0,39}$")

    def test_a_read_only_suite_cannot_have_a_fixture(self):
        with mock.patch.dict(browser.SUITE_FIXTURE_OUTPUTS, {"c03": frozenset({"partyId"})}):
            with self.assertRaisesRegex(browser.ContractError, "read-only suite c03"):
                browser.has_fixture("c03")

    def test_the_profile_line_reports_the_fixture(self):
        with mock.patch.dict(browser.SUITE_FIXTURE_OUTPUTS, {"org03": frozenset({"companyId"})}):
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(0, browser.main(["profile", "--suite", "org03"]))
        self.assertIn("fixture=true\n", output.getvalue())


class SeedSourceTests(unittest.TestCase):
    def test_owner_seeds_use_no_sql(self):
        for path in seed_sources():
            source = path.read_text(encoding="utf-8")
            for token in FORBIDDEN_IN_SEEDS:
                with self.subTest(path=path.relative_to(ROOT).as_posix(), token=token):
                    self.assertNotIn(token, source)

    def test_the_runner_uses_sql_only_to_count_audit_and_command_rows(self):
        users = [path for path in TOOL.rglob("*.cs")
                 if "/bin/" not in path.as_posix() and "/obj/" not in path.as_posix()
                 and "SqlConnection" in path.read_text(encoding="utf-8")]
        self.assertEqual(["BrowserSuiteFixtureTool.cs"], [path.name for path in users])
        statements = re.findall(r'"(SELECT[^"]*|INSERT[^"]*|UPDATE[^"]*|DELETE[^"]*|MERGE[^"]*|EXEC[^"]*)"',
                                users[0].read_text(encoding="utf-8"))
        self.assertEqual(
            ["SELECT COUNT_BIG(*) FROM [audit].[AuditEvents];",
             "SELECT COUNT_BIG(*) FROM [idempotency].[CommandExecutions];"],
            statements)

    def test_the_runner_applies_the_c01_tools_disposable_rules(self):
        runner = (TOOL / "BrowserSuiteFixtureTool.cs").read_text(encoding="utf-8")
        c01_tool = (ROOT / "tests/GoymenERP.C01E2eFixtureTool/Program.cs").read_text(encoding="utf-8")
        for literal in (
            '$"goymen-c01-{runId}-"',
            '$"GoymenErpC01_{runId}_{attempt}"',
            '"CONFIRMED"',
            '"GOYMEN_ERP_C01_E2E_DISPOSABLE"',
            '"GOYMEN_ERP_C01_E2E_SQLSERVER_CONNECTION"',
            '"^[0-9a-fA-F]{40}$"',
            '"^[a-z0-9][a-z0-9_-]{0,62}$"',
            'builder.IntegratedSecurity || string.IsNullOrWhiteSpace(builder.UserID)',
            '"127.0.0.1"',
        ):
            with self.subTest(literal=literal):
                self.assertIn(literal, c01_tool)
                self.assertIn(literal, runner)

    def test_the_host_runs_program_unchanged_in_testing_only(self):
        host = (TOOL / "BrowserSuiteHost.cs").read_text(encoding="utf-8")
        self.assertIn("WebApplicationFactory<global::Program>", host)
        self.assertIn('string.Equals(environment, "Testing", StringComparison.Ordinal)', host)
        self.assertNotIn("ConfigureServices", host)
        self.assertNotIn("ConfigureTestServices", host)


class SeedExportTests(unittest.TestCase):
    OUTPUTS = {"org03": frozenset({"companyId", "recorderActor"})}

    def test_exact_keys_with_guid_or_actor_values_export_one_compact_line(self):
        with mock.patch.dict(browser.SUITE_FIXTURE_OUTPUTS, self.OUTPUTS):
            line = browser.seed_export("org03", {
                "recorderActor": "org03-recorder",
                "companyId": "10000000-0000-0000-0000-000000000001",
            })
        self.assertEqual(
            'BROWSER_E2E_ORG03_SEED={"companyId":"10000000-0000-0000-0000-000000000001",'
            '"recorderActor":"org03-recorder"}',
            line)

    def test_missing_extra_or_unsafe_outputs_are_refused(self):
        good = {"recorderActor": "org03-recorder", "companyId": "10000000-0000-0000-0000-000000000001"}
        cases = (
            ({"recorderActor": "org03-recorder"}, "missing"),
            ({**good, "other": "org03-x"}, "extra"),
            ({**good, "companyId": "00000000-0000-0000-0000-000000000000"}, "GUID or an actor"),
            ({**good, "companyId": "10000000-0000-0000-0000-00000000000A"}, "GUID or an actor"),
            ({**good, "recorderActor": "u02-recorder"}, "GUID or an actor"),
            ({**good, "recorderActor": "free text"}, "GUID or an actor"),
            ({**good, "companyId": 7}, "must be a string"),
            (["not", "an", "object"], "object"),
        )
        with mock.patch.dict(browser.SUITE_FIXTURE_OUTPUTS, self.OUTPUTS):
            for seed, pattern in cases:
                with self.subTest(seed=seed), self.assertRaisesRegex(browser.ContractError, pattern):
                    browser.seed_export("org03", seed)

    def test_a_suite_without_a_fixture_exports_nothing(self):
        with self.assertRaisesRegex(browser.ContractError, "has no fixture"):
            browser.seed_export("smoke", {})


class ApiEnvironmentTests(unittest.TestCase):
    def test_the_exported_environment_is_the_validated_api_environment(self):
        model = profile_tests.model("normal")
        environment = browser.api_environment(model, "org03", profile_tests.COMMIT, profile_tests.PROJECT)
        self.assertEqual(model["services"]["api"]["environment"], environment)
        self.assertEqual("Testing", environment["ASPNETCORE_ENVIRONMENT"])

    def test_an_invalid_model_exports_nothing(self):
        model = profile_tests.model("normal")
        model["services"]["api"]["environment"]["ASPNETCORE_ENVIRONMENT"] = "Production"
        with self.assertRaises(browser.ContractError):
            browser.api_environment(model, "org03", profile_tests.COMMIT, profile_tests.PROJECT)

    def test_the_environment_file_never_lands_in_artifacts(self):
        model = json.dumps(profile_tests.model("normal"))
        with mock.patch.object(sys, "stdin", io.StringIO(model)), \
                contextlib.redirect_stderr(io.StringIO()) as errors:
            code = browser.main(["api-environment", "--suite", "org03", "--expected-commit", profile_tests.COMMIT,
                                 "--expected-project", profile_tests.PROJECT,
                                 "--out", str(ROOT / "artifacts" / "api-environment.json")])
        self.assertEqual(1, code)
        self.assertIn("outside artifacts", errors.getvalue())
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(sys, "stdin", io.StringIO(model)):
            target = pathlib.Path(directory) / "api-environment.json"
            self.assertEqual(0, browser.main([
                "api-environment", "--suite", "org03", "--expected-commit", profile_tests.COMMIT,
                "--expected-project", profile_tests.PROJECT, "--out", str(target)]))
            self.assertEqual(profile_tests.model("normal")["services"]["api"]["environment"],
                             json.loads(target.read_text(encoding="utf-8")))


class WorkflowTests(unittest.TestCase):
    @staticmethod
    def _step(name):
        return WORKFLOW.split(f"      - name: {name}\n", 1)[1].split("\n      - name:", 1)[0]

    def test_the_suite_fixture_runs_after_the_canonical_fixture_and_before_the_web_profiles(self):
        order = [
            "Materialize and verify canonical fixture",
            "Materialize the suite's fixture",
            "Build and start authorized and unauthorized web profiles",
            "Run the selected suite",
        ]
        positions = [WORKFLOW.index(f"      - name: {name}\n") for name in order]
        self.assertEqual(sorted(positions), positions)

    def test_the_suite_fixture_step_is_gated_validated_and_leaves_no_secret_file(self):
        step = self._step("Materialize the suite's fixture")
        self.assertIn("if: env.BROWSER_E2E_SUITE_FIXTURE == 'true'", step)
        self.assertIn("tools/validate_browser_acceptance.py api-environment", step)
        self.assertIn("tools/validate_browser_acceptance.py seed-export", step)
        self.assertIn('trap \'rm -f "$compose_model" "$api_environment"\' EXIT', step)
        self.assertIn('api_environment="$RUNNER_TEMP/', step)
        self.assertIn('-- materialize --suite "$BROWSER_SUITE"', step)
        self.assertIn('${BROWSER_E2E_COMPOSE_OVERRIDE:+-f "$BROWSER_E2E_COMPOSE_OVERRIDE"}', step)
        restore = self._step("Restore suite fixture runner")
        self.assertIn("if: env.BROWSER_E2E_SUITE_FIXTURE == 'true'", restore)

    def test_the_profile_step_exports_a_boolean_fixture_flag(self):
        step = self._step("Select the suite's capability profile")
        self.assertIn('test "$fixture" = "true" || test "$fixture" = "false"', step)
        self.assertIn('echo "BROWSER_E2E_SUITE_FIXTURE=$fixture"', step)


if __name__ == "__main__":
    unittest.main()
