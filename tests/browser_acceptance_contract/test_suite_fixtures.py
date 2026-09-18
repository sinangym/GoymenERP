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
            ([], "object"),
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


class SuiteIdentityTests(unittest.TestCase):
    """Decision 1: a suite's actors get Identity users and grants through the real seeder, closed list."""

    ISSUER = {
        "ProductRuntime__IssuerLocalUserId": "0f8d8c52-2b6e-4f3a-9c1d-6a1b2c3d4e5f",
        "ProductRuntime__IssuerExternalIdentityId": "1e2d3c4b-5a69-4788-8b9a-0c1d2e3f4a5b",
    }

    @staticmethod
    def identity(**changes):
        environment = {
            "BootstrapIdentity__Users__2__UserId": "s03-writer",
            "BootstrapIdentity__Users__2__DisplayName": "SENTETIK S03 Satış Yazarı",
            "BootstrapIdentity__Users__3__UserId": "s03-reader",
            "BootstrapIdentity__Users__3__DisplayName": "SENTETIK S03 Satış Okuyucu",
            "IdentitySeed__Users__5__Id": "b2000000-0000-0000-0000-000000000001",
            "IdentitySeed__Users__5__DisplayName": "SENTETIK S03 Satış Yazarı",
            "IdentitySeed__Users__5__ExternalIdentities__0__Id": "b2100000-0000-0000-0000-000000000001",
            "IdentitySeed__Users__5__ExternalIdentities__0__Issuer": "urn:goymen:development-bootstrap",
            "IdentitySeed__Users__5__ExternalIdentities__0__Subject": "s03-writer",
            "IdentitySeed__Users__6__Id": "b2000000-0000-0000-0000-000000000002",
            "IdentitySeed__Users__6__DisplayName": "SENTETIK S03 Satış Okuyucu",
            "IdentitySeed__Users__6__ExternalIdentities__0__Id": "b2100000-0000-0000-0000-000000000002",
            "IdentitySeed__Users__6__ExternalIdentities__0__Issuer": "urn:goymen:development-bootstrap",
            "IdentitySeed__Users__6__ExternalIdentities__0__Subject": "s03-reader",
            "IdentitySeed__Roles__5__Id": "b3000000-0000-0000-0000-000000000001",
            "IdentitySeed__Roles__5__Code": "BROWSER_S03_DRAFT_WRITER",
            "IdentitySeed__Roles__5__Name": "SENTETIK S03 taslak yazarı",
            "IdentitySeed__Roles__5__IsSystem": "false",
            "IdentitySeed__Roles__5__Permissions__0": "sales.drafts.create",
            "IdentitySeed__Roles__5__Permissions__1": "sales.drafts.read",
            "IdentitySeed__Roles__5__Permissions__2": "product.operational-selection.read",
            "IdentitySeed__Roles__6__Id": "b3000000-0000-0000-0000-000000000002",
            "IdentitySeed__Roles__6__Code": "BROWSER_S03_DRAFT_READER",
            "IdentitySeed__Roles__6__Name": "SENTETIK S03 taslak okuyucu",
            "IdentitySeed__Roles__6__IsSystem": "false",
            "IdentitySeed__Roles__6__Permissions__0": "sales.drafts.read",
        }
        for index, (user, role) in enumerate((("b2000000-0000-0000-0000-000000000001", "b3000000-0000-0000-0000-000000000001"),
                                              ("b2000000-0000-0000-0000-000000000002", "b3000000-0000-0000-0000-000000000002")), start=7):
            prefix = f"IdentitySeed__Assignments__{index}__"
            environment.update({
                prefix + "Id": f"b4000000-0000-0000-0000-00000000000{index - 6}",
                prefix + "UserId": user,
                prefix + "RoleId": role,
                prefix + "CompanyId": "10000000-0000-0000-0000-000000000001",
                prefix + "ValidFromUtc": "2026-01-01T00:00:00+00:00",
                prefix + "ApprovedByUserId": "20000000-0000-0000-0000-000000000003",
                prefix + "Scopes__0__Type": "Company",
                prefix + "Scopes__0__Id": "10000000-0000-0000-0000-000000000001",
            })
        for key, value in changes.items():
            if value is None:
                environment.pop(key, None)
            else:
                environment[key] = value
        return environment

    def _validate(self, extension, suite="s03"):
        model = profile_tests.model("normal")
        model["services"]["api"]["environment"].update(self.ISSUER if suite == "s03" else {})
        model["services"]["api"]["environment"].update(extension)
        browser.validate_suite_compose(model, suite, profile_tests.COMMIT, profile_tests.PROJECT)

    def test_s03_actors_with_their_users_roles_and_company_wide_grants_are_accepted(self):
        self._validate(self.identity())

    def test_the_built_in_seed_counts_set_the_first_indices(self):
        builtin = json.loads((ROOT / browser.IDENTITY_SEED_FILE).read_text(encoding="utf-8"))["IdentitySeed"]
        self.assertEqual((5, 5, 7), (len(builtin["Users"]), len(builtin["Roles"]), len(builtin["Assignments"])))

    def test_every_rule_refuses_its_violation(self):
        user = "IdentitySeed__Users__5__"
        role = "IdentitySeed__Roles__5__"
        assignment = "IdentitySeed__Assignments__7__"
        cases = {
            "actor without its user": {user + key: None for key in (
                "Id", "DisplayName", "ExternalIdentities__0__Id", "ExternalIdentities__0__Issuer",
                "ExternalIdentities__0__Subject")},
            "user for no actor": {user + "ExternalIdentities__0__Subject": "s03-ghost"},
            "user id outside the synthetic range": {user + "Id": "20000000-0000-0000-0000-000000000009"},
            "external id outside its range": {user + "ExternalIdentities__0__Id": "b2000000-0000-0000-0000-000000000009"},
            "user not SENTETIK": {user + "DisplayName": "Gerçek Kullanıcı"},
            "another issuer": {user + "ExternalIdentities__0__Issuer": "urn:other"},
            "email is not allowed": {user + "Email": "a@b.c"},
            "second external identity": {user + "ExternalIdentities__1__Id": "b2100000-0000-0000-0000-000000000009"},
            "users not contiguous": {key.replace("Users__6__", "Users__7__"): value
                                     for key, value in self.identity().items() if "Users__6__" in key}
                                    | {key: None for key in self.identity() if "Users__6__" in key},
            "role code outside the suite prefix": {role + "Code": "SYSTEM_ADMINISTRATOR"},
            "system role": {role + "IsSystem": "true"},
            "role not SENTETIK": {role + "Name": "Satış"},
            "permission outside the closed list": {role + "Permissions__3": "counterparty.parties.create"},
            "repeated permission": {role + "Permissions__3": "sales.drafts.read"},
            "permissions not from 0": {role + "Permissions__0": None},
            "role without permissions": {role + f"Permissions__{index}": None for index in range(3)},
            "assignment of a foreign user": {assignment + "UserId": "20000000-0000-0000-0000-000000000003"},
            "assignment of a built-in role": {assignment + "RoleId": "30000000-0000-0000-0000-000000000003"},
            "unseeded company": {assignment + "CompanyId": "10000000-0000-0000-0000-000000000009",
                                 assignment + "Scopes__0__Id": "10000000-0000-0000-0000-000000000009"},
            "scope narrower than the company": {assignment + "Scopes__0__Type": "Site"},
            "scope on another company": {assignment + "Scopes__0__Id": "10000000-0000-0000-0000-000000000002"},
            "another approver": {assignment + "ApprovedByUserId": "b2000000-0000-0000-0000-000000000001"},
            "an expiry": {assignment + "ValidUntilUtc": "2027-01-01T00:00:00+00:00"},
            "another start": {assignment + "ValidFromUtc": "2025-01-01T00:00:00+00:00"},
            "repeated synthetic id": {"IdentitySeed__Assignments__8__Id": "b4000000-0000-0000-0000-000000000001"},
            "user without an assignment": {key: None for key in self.identity() if "Assignments__8__" in key},
            "role with an unlisted field": {role + "Description": "x"},
            "user without a display name": {user + "DisplayName": None},
            "assignment without its start": {assignment + "ValidFromUtc": None},
            "extra assignment of a foreign user": {
                "IdentitySeed__Assignments__9__Id": "b4000000-0000-0000-0000-000000000009",
                "IdentitySeed__Assignments__9__UserId": "20000000-0000-0000-0000-000000000003",
                "IdentitySeed__Assignments__9__RoleId": "b3000000-0000-0000-0000-000000000001",
                "IdentitySeed__Assignments__9__CompanyId": "10000000-0000-0000-0000-000000000001",
                "IdentitySeed__Assignments__9__ValidFromUtc": "2026-01-01T00:00:00+00:00",
                "IdentitySeed__Assignments__9__Scopes__0__Type": "Company",
                "IdentitySeed__Assignments__9__Scopes__0__Id": "10000000-0000-0000-0000-000000000001",
            },
        }
        for label, changes in cases.items():
            with self.subTest(label), self.assertRaises(browser.ContractError):
                self._validate(self.identity(**changes))

    def test_s03_actors_without_any_identity_user_are_refused(self):
        actors_only = {key: value for key, value in self.identity().items() if key.startswith("BootstrapIdentity__")}
        with self.assertRaisesRegex(browser.ContractError, "need their Identity users"):
            self._validate(actors_only)

    def test_a_suite_without_an_identity_list_cannot_seed_users(self):
        extension = {key: value for key, value in self.identity().items()
                     if key.startswith("IdentitySeed__") or key.startswith("BootstrapIdentity__")}
        extension = {key.replace("s03-", "org03-"): value.replace("s03-", "org03-")
                     for key, value in extension.items()}
        with self.assertRaisesRegex(browser.ContractError, "may not seed Identity users"):
            self._validate(extension, suite="org03")

    def test_org03_keeps_the_actors_only_shape_until_its_grants_are_declared(self):
        self._validate({
            "BootstrapIdentity__Users__2__UserId": "org03-approver",
            "BootstrapIdentity__Users__2__DisplayName": "SENTETIK ORG-03 Onaycı",
        }, suite="org03")


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
