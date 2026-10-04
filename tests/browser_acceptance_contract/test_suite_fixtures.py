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


SHARED_DIRECTORY = TOOL / "Shared"


def seed_sources():
    """Owner seeds and the shared seeds they call (e.g. CARİ's Cari seed)."""
    return sorted(path for directory in (SUITES_DIRECTORY, SHARED_DIRECTORY) if directory.is_dir()
                  for path in directory.rglob("*.cs"))


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

    def test_u02_exports_the_fixed_technical_family_identity(self):
        self.assertEqual(
            frozenset({"companyId", "codeGroupId", "technicalFamilyId", "authorizedActor", "unauthorizedActor",
                       "activatorActor"}),
            browser.SUITE_FIXTURE_OUTPUTS["u02"])

    def test_s04_exports_the_sales_allocation_tuple(self):
        self.assertEqual(
            frozenset({
                "preparerActor", "approverActor", "readerActor", "companyId", "draftId",
                "deliveryDraftId", "realPartyId", "realCompanyAccountId", "deliveryAddressId",
                "deliveryContactId", "supportCreatorActor", "ordinaryCreatorActor", "supportDraftId",
                "ordinaryApproveDraftId", "ordinaryRejectDraftId", "withdrawalDraftId",
                "productId", "productVersion", "productUnitId", "productUnitProfileId",
                "productUnitProfileVersion", "productCode", "productName",
                "allocationLineId", "allocationLabelId", "allocationLabelVersion",
                "plannerActor", "scheduleDraftId",
            }),
            browser.SUITE_FIXTURE_OUTPUTS["s04"])

    def test_l04_exports_the_shipment_allocation_and_label_tuple(self):
        self.assertEqual(
            frozenset({
                "preparerActor", "loaderActor", "readerActor", "companyId", "otherCompanyId",
                "customerPartyId", "companyAccountId", "draftId", "approvedLineId",
                "instructionId", "blockedLineId", "openLineId", "shipmentAllocationId",
                "shipmentLabelId", "shipmentReceiptAcceptanceId", "shipmentReceiptMovementId",
            }),
            browser.SUITE_FIXTURE_OUTPUTS["l04"])

    def test_pur01_exports_the_separate_creator_supplier_and_existing_approval_axis(self):
        self.assertEqual(
            frozenset({
                "creatorActor", "approverActor", "readerActor", "receiverActor", "stockReaderActor",
                "companyId", "otherCompanyId",
                "orderId", "lineId", "supplierPartyId", "companyAccountId", "productId",
                "productVersion", "productUnitId", "productUnitProfileId",
                "productUnitProfileVersion", "productCode", "productName",
            }),
            browser.SUITE_FIXTURE_OUTPUTS["pur01"])

    def test_r14_exports_the_recorder_fence_location_and_product_tuple(self):
        self.assertEqual(
            frozenset({
                "recorderActor", "deniedActor", "arrangerActor", "transformerActor",
                "counterActor", "countApproverActor", "scrapperActor", "scrapApproverActor",
                "companyId", "otherCompanyId",
                "locationId", "productId", "productVersion", "productUnitId",
                "productUnitProfileId", "productUnitProfileVersion", "productCode", "productName",
            }),
            browser.SUITE_FIXTURE_OUTPUTS["r14"])

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
    def test_r14_browser_environment_uses_the_canonical_disposable_database_shape(self):
        source = (ROOT / "tests/e2e/inventory-r14/src/env.ts").read_text(encoding="utf-8")
        self.assertIn("/^GoymenErpC01_[0-9]+_[0-9]+$/", source)
        self.assertNotIn("GoymenERP_C01_E2E_", source)

    def test_owner_seeds_use_no_sql(self):
        self.assertTrue(seed_sources(), "the shared seeds are scanned too")
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

    def test_the_runner_materializes_the_existing_product_runtime_before_owner_fixtures(self):
        runner = (TOOL / "BrowserSuiteFixtureTool.cs").read_text(encoding="utf-8")
        start = runner.index("using (host.CreateClient())")
        initialize = runner.index("await host.Services.InitializeProductRuntimeAsync(CancellationToken.None);")
        seed = runner.index("outputs = await fixture.SeedAsync(context, CancellationToken.None);")
        self.assertLess(start, initialize)
        self.assertLess(initialize, seed)
        self.assertNotIn("ProductDbContext", runner)
        self.assertNotIn("MigrateAsync", runner)

    def test_only_the_runner_opens_the_fixture_writer_gate(self):
        # Decision W1: the BrowserSuiteFixture:* keys come only from the runner's host settings.
        host = (TOOL / "BrowserSuiteHost.cs").read_text(encoding="utf-8")
        for key in ("Host", "TestedCommit", "RunId", "ComposeProject"):
            with self.subTest(key=key):
                self.assertIn(f'new("BrowserSuiteFixture:{key}", ', host)
        for suite in ("s03", "s04", "l04", "org03", "smoke"):
            for key in ("BrowserSuiteFixture__Host", "BrowserSuiteFixture__TestedCommit"):
                with self.subTest(suite=suite, key=key):
                    model = profile_tests.model(browser.profile_of(suite))
                    model["services"]["api"]["environment"][key] = "CONFIRMED"
                    with self.assertRaises(browser.ContractError):
                        browser.validate_suite_compose(model, suite, profile_tests.COMMIT, profile_tests.PROJECT)

    def test_the_host_removes_envelope_variables_the_container_does_not_carry(self):
        # The runner process has the run's GOYMEN_ERP_C01_E2E_* variables for every suite; a normal-profile
        # container has none, and any one of them would make the in-process host request the C-01 envelope.
        # Program reads that gate before it builds the app, from the process environment: an in-memory
        # mask is too late (u02 run 35411370499), so the variables are removed from the process.
        host = (TOOL / "BrowserSuiteHost.cs").read_text(encoding="utf-8")
        self.assertIn("Environment.SetEnvironmentVariable(name, null);", host)
        self.assertIn('name.StartsWith("GOYMEN_ERP_C01_E2E_", StringComparison.Ordinal)', host)
        self.assertIn('name.StartsWith("C01E2e__", StringComparison.Ordinal)', host)
        self.assertIn("!apiEnvironment.ContainsKey(name)", host)
        # Create is the only way to a host, and it removes the envelope first; the runner uses it.
        self.assertIn("    private BrowserSuiteHost(\n", host)
        create = host.split("internal static BrowserSuiteHost Create(", 1)[1].split("\n    }\n", 1)[0]
        self.assertLess(create.index("RemoveProcessEnvelope(apiEnvironment);"), create.index("return new BrowserSuiteHost("))
        runner = (TOOL / "BrowserSuiteFixtureTool.cs").read_text(encoding="utf-8")
        self.assertIn("BrowserSuiteHost.Create(", runner)
        self.assertNotIn("new BrowserSuiteHost(", runner)
        activation = (ROOT / "src/backend/GoymenERP.Api/Shell/C01E2eShellCapabilityActivation.cs").read_text(
            encoding="utf-8")
        # Every activation trigger key is one of the removed families.
        keys = activation.split("private static readonly string[] ConfigurationKeys =", 1)[1].split("];", 1)[0]
        for constant in re.findall(r"(\w+ConfigurationKey)", keys):
            value = re.search(rf'{constant} =\s*"([^"]+)"', activation).group(1)
            with self.subTest(key=value):
                self.assertTrue(value.startswith(("GOYMEN_ERP_C01_E2E_", "C01E2e:")), value)

    def test_the_host_passes_every_value_program_reads_as_a_host_setting(self):
        # The shell profile (C01E2e:Profile) and the fixture-writer gate are read before the app is built;
        # only host settings (UseSetting) are visible there. No value may travel as in-memory configuration.
        host = (TOOL / "BrowserSuiteHost.cs").read_text(encoding="utf-8")
        self.assertNotIn("ConfigureAppConfiguration", host)
        self.assertNotIn("AddInMemoryCollection", host)
        self.assertIn('builder.UseSetting(key.Replace("__", ":", StringComparison.Ordinal), value);', host)
        self.assertIn("foreach (var (key, value) in FixtureWriterBinding())", host)

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

    def test_pur01_accepts_only_its_exact_product_scalars(self):
        keys = frozenset({"productVersion", "productUnitProfileVersion", "productCode", "productName"})
        good = {
            "productVersion": "2",
            "productUnitProfileVersion": "1",
            "productCode": "SYNPUR01-1",
            "productName": "SENTETIK browser-suite:pur01 purchase candidate",
        }
        with mock.patch.dict(browser.SUITE_FIXTURE_OUTPUTS, {"pur01": keys}):
            self.assertIn('"productCode":"SYNPUR01-1"', browser.seed_export("pur01", good))
            for key, value in (
                ("productVersion", "02"),
                ("productUnitProfileVersion", "0"),
                ("productCode", "SYNPUR01-0"),
                ("productName", "SENTETIK browser-suite:pur01 purchase candidate "),
            ):
                with self.subTest(key=key, value=value), self.assertRaises(browser.ContractError):
                    browser.seed_export("pur01", {**good, key: value})

    def test_s04_accepts_only_its_exact_product_and_allocation_scalars(self):
        keys = frozenset({
            "productVersion", "productUnitProfileVersion", "productCode", "productName",
            "allocationLabelVersion",
        })
        good = {
            "productVersion": "2",
            "productUnitProfileVersion": "1",
            "productCode": "SYNS04-1",
            "productName": "SENTETIK browser-suite:s04 sales candidate",
            "allocationLabelVersion": "3",
        }
        with mock.patch.dict(browser.SUITE_FIXTURE_OUTPUTS, {"s04": keys}):
            self.assertIn('"allocationLabelVersion":"3"', browser.seed_export("s04", good))
            for key, value in (
                ("allocationLabelVersion", "0"),
                ("allocationLabelVersion", "03"),
                ("allocationLabelVersion", "3.5"),
            ):
                with self.subTest(key=key, value=value), self.assertRaises(browser.ContractError):
                    browser.seed_export("s04", {**good, key: value})

    def test_r14_accepts_only_its_exact_product_scalars(self):
        keys = frozenset({"productVersion", "productUnitProfileVersion", "productCode", "productName"})
        good = {
            "productVersion": "2",
            "productUnitProfileVersion": "1",
            "productCode": "SYNR14-1",
            "productName": "SENTETIK browser-suite:r14 stock candidate",
        }
        with mock.patch.dict(browser.SUITE_FIXTURE_OUTPUTS, {"r14": keys}):
            self.assertIn('"productCode":"SYNR14-1"', browser.seed_export("r14", good))
            for key, value in (
                ("productVersion", "02"),
                ("productUnitProfileVersion", "0"),
                ("productCode", "SYNR14-0"),
                ("productName", "SENTETIK browser-suite:r14 stock candidate "),
            ):
                with self.subTest(key=key, value=value), self.assertRaises(browser.ContractError):
                    browser.seed_export("r14", {**good, key: value})

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
            "actor without any user or assignment": {key: None for key in self.identity()
                                                     if "Users__6__" in key or "Assignments__8__" in key},
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
        """The rule is about the FLAG, so the flag is what the case sets.

        This used to borrow org03, the last configured suite whose `identity` was False. The org03
        browser slice flipped it, and now NO configured suite has it False — so a case written
        against a real suite would have had to be deleted or quietly re-pointed, and either way the
        rule would have stopped being covered on the day its last example disappeared. Constructing
        the condition keeps the rule tested no matter how the suites are configured.
        """
        extension = {key: value for key, value in self.identity().items()
                     if key.startswith("IdentitySeed__") or key.startswith("BootstrapIdentity__")}
        extension = {key.replace("s03-", "org03-"): value.replace("s03-", "org03-")
                     for key, value in extension.items()}
        without_identity = dict(browser.SUITE_API_EXTENSIONS["org03"], identity=False)
        with mock.patch.dict(browser.SUITE_API_EXTENSIONS, {"org03": without_identity}):
            with self.assertRaisesRegex(browser.ContractError, "may not seed Identity users"):
                self._validate(extension, suite="org03")

    def test_org03_actors_without_any_identity_user_are_refused(self):
        """The successor to the case this replaces, and the reason the replacement is not a deletion.

        While org03's grants were undeclared, actors WITHOUT Identity users were the correct shape and
        this case asserted it. The browser slice declared them, so the same input is now a defect: its
        actors cannot sign in. The fact moved, so the case moves with it rather than being dropped —
        an expired case removed leaves no record that the rule reversed.
        """
        with self.assertRaisesRegex(browser.ContractError, "need their Identity users"):
            self._validate({
                "BootstrapIdentity__Users__2__UserId": "org03-approver",
                "BootstrapIdentity__Users__2__DisplayName": "SENTETIK ORG-03 Onaycı",
            }, suite="org03")


class IssuerUserRuleTests(unittest.TestCase):
    """The issuer-user rule (coordinator decision, a narrow D1 extension): a suite that binds the Product
    issuer may seed one non-actor Identity user, the issuer's own, holding nothing."""

    LOCAL = "b2000000-0000-4000-8000-000000000201"
    EXTERNAL = "b2100000-0000-4000-8000-000000000201"

    def issuer(self, suite="u02", **changes):
        user = "IdentitySeed__Users__5__"
        environment = {
            "ProductRuntime__IssuerLocalUserId": self.LOCAL,
            "ProductRuntime__IssuerExternalIdentityId": self.EXTERNAL,
            user + "Id": self.LOCAL,
            user + "DisplayName": f"SENTETIK browser-suite:{suite} Product issuer",
            user + "ExternalIdentities__0__Id": self.EXTERNAL,
            user + "ExternalIdentities__0__Issuer": "urn:goymen:development-bootstrap",
            user + "ExternalIdentities__0__Subject": f"{suite}-issuer",
        }
        for key, value in changes.items():
            if value is None:
                environment.pop(key, None)
            else:
                environment[key] = value
        return environment

    @staticmethod
    def _validate(extension, suite="u02"):
        model = profile_tests.model(browser.profile_of(suite))
        model["services"]["api"]["environment"].update(extension)
        browser.validate_suite_compose(model, suite, profile_tests.COMMIT, profile_tests.PROJECT)

    def test_the_u02_issuer_user_is_accepted(self):
        self._validate(self.issuer())

    def test_the_checked_in_override_files_pass_their_profiles(self):
        # Every owner override in infra/browser-suites is valid against its suite's profile model.
        directory = ROOT / browser.SUITE_OVERRIDE_DIRECTORY
        compose_bindings = {
            "${C01_E2E_TESTED_COMMIT:?C01_E2E_TESTED_COMMIT must be set}": profile_tests.COMMIT,
            "${GOYMEN_ERP_C01_E2E_RUN_ID:?GOYMEN_ERP_C01_E2E_RUN_ID must be set}":
                profile_tests.PROJECT.split("-")[-2],
            "${GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT:?GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT must be set}":
                profile_tests.PROJECT,
        }
        for path in sorted(directory.glob("*.override.yml")) if directory.is_dir() else []:
            suite = path.name.split(".", 1)[0]
            environment = {}
            for line in path.read_text(encoding="utf-8").splitlines():
                match = re.fullmatch(r"      ([A-Za-z0-9_]+): (.+)", line)
                if match:
                    raw = match.group(2).strip('"')
                    environment[match.group(1)] = compose_bindings.get(raw, raw)
            with self.subTest(suite=suite):
                self.assertTrue(environment)
                self._validate(environment, suite)

    def test_pur01_binds_the_existing_product_issuer_as_one_ungranted_non_actor(self):
        path = ROOT / browser.SUITE_OVERRIDE_DIRECTORY / "pur01.override.yml"
        environment = {}
        for line in path.read_text(encoding="utf-8").splitlines():
            match = re.fullmatch(r"      ([A-Za-z0-9_]+): (.+)", line)
            if match:
                environment[match.group(1)] = match.group(2).strip('"')

        user = "IdentitySeed__Users__8__"
        self.assertEqual(self.LOCAL, environment["ProductRuntime__IssuerLocalUserId"])
        self.assertEqual(self.EXTERNAL, environment["ProductRuntime__IssuerExternalIdentityId"])
        self.assertEqual(self.LOCAL, environment[user + "Id"])
        self.assertEqual(self.EXTERNAL, environment[user + "ExternalIdentities__0__Id"])
        self.assertEqual("pur01-issuer", environment[user + "ExternalIdentities__0__Subject"])
        # The issuer is an ungranted non-actor: no role is defined for it and (below) no assignment names it.
        # (The earlier check tested for an absent Roles__8 index, conflating the issuer's USER index 8 with a
        # role index; Roles__8 is now the disposable goods receiver, so the intent is stated directly.)
        self.assertFalse(any(key.startswith("IdentitySeed__Roles__") and key.endswith("__Code")
                             and "ISSUER" in value.upper() for key, value in environment.items()))
        self.assertFalse(any(value == self.LOCAL for key, value in environment.items()
                             if key.startswith("IdentitySeed__Assignments__")))
        self._validate(environment, suite="pur01")

    def test_l04_binds_the_product_issuer_as_one_ungranted_non_actor(self):
        path = ROOT / browser.SUITE_OVERRIDE_DIRECTORY / "l04.override.yml"
        environment = {}
        for line in path.read_text(encoding="utf-8").splitlines():
            match = re.fullmatch(r"      ([A-Za-z0-9_]+): (.+)", line)
            if match:
                environment[match.group(1)] = match.group(2).strip('"')

        user = "IdentitySeed__Users__10__"
        self.assertEqual(self.LOCAL, environment["ProductRuntime__IssuerLocalUserId"])
        self.assertEqual(self.EXTERNAL, environment["ProductRuntime__IssuerExternalIdentityId"])
        self.assertEqual(self.LOCAL, environment[user + "Id"])
        self.assertEqual(self.EXTERNAL, environment[user + "ExternalIdentities__0__Id"])
        self.assertEqual("l04-issuer", environment[user + "ExternalIdentities__0__Subject"])
        # The issuer is an ungranted non-actor: no role is defined for it and (below) no assignment names it.
        # (The earlier check tested for an absent Roles__10 index, conflating the issuer's USER index 10 with a
        # role index; Roles__10 is now the disposable AT-025 operations reader, so the intent is stated directly,
        # as pur01 does.)
        self.assertFalse(any(key.startswith("IdentitySeed__Roles__") and key.endswith("__Code")
                             and "ISSUER" in value.upper() for key, value in environment.items()))
        self.assertFalse(any(value == self.LOCAL for key, value in environment.items()
                             if key.startswith("IdentitySeed__Assignments__")))
        def permissions(role):
            prefix = f"IdentitySeed__Roles__{role}__Permissions__"
            return {value for key, value in environment.items() if key.startswith(prefix)}

        preparer = permissions(5)
        loader = permissions(6)
        reader = permissions(7)
        self.assertTrue({"logistics.customer-returns.read", "logistics.customer-returns.request"} <= preparer)
        self.assertNotIn("logistics.customer-returns.decision", preparer)
        self.assertTrue({"logistics.customer-returns.read", "logistics.customer-returns.decision"} <= loader)
        self.assertNotIn("logistics.customer-returns.request", loader)
        self.assertIn("logistics.customer-returns.read", reader)
        self.assertNotIn("logistics.customer-returns.request", reader)
        self.assertNotIn("logistics.customer-returns.decision", reader)
        self._validate(environment, suite="l04")

    def test_every_rule_refuses_its_violation(self):
        user = "IdentitySeed__Users__5__"
        cases = {
            "user id is not the issuer's local id": {user + "Id": "b2000000-0000-4000-8000-000000000202"},
            "external id is not the issuer's external id": {
                user + "ExternalIdentities__0__Id": "b2100000-0000-4000-8000-000000000202"},
            "subject is not <suite>-issuer": {user + "ExternalIdentities__0__Subject": "u02-product-issuer"},
            "subject is a canonical actor": {user + "ExternalIdentities__0__Subject": "group-bootstrap-admin"},
            "not SENTETIK": {user + "DisplayName": "Product issuer"},
            "another issuer": {user + "ExternalIdentities__0__Issuer": "urn:other"},
            "issuer keys absent": {"ProductRuntime__IssuerLocalUserId": None,
                                   "ProductRuntime__IssuerExternalIdentityId": None},
            "a second non-actor user": {
                "IdentitySeed__Users__6__Id": "b2000000-0000-4000-8000-000000000203",
                "IdentitySeed__Users__6__DisplayName": "SENTETIK another",
                "IdentitySeed__Users__6__ExternalIdentities__0__Id": "b2100000-0000-4000-8000-000000000203",
                "IdentitySeed__Users__6__ExternalIdentities__0__Issuer": "urn:goymen:development-bootstrap",
                "IdentitySeed__Users__6__ExternalIdentities__0__Subject": "u02-other"},
            "a role (u02 holds no permission)": {
                "IdentitySeed__Roles__5__Id": "b3000000-0000-4000-8000-000000000201",
                "IdentitySeed__Roles__5__Code": "BROWSER_U02_ISSUER",
                "IdentitySeed__Roles__5__Name": "SENTETIK issuer",
                "IdentitySeed__Roles__5__IsSystem": "false",
                "IdentitySeed__Roles__5__Permissions__0": "product.operational-selection.read"},
            "an assignment for the issuer": {
                "IdentitySeed__Assignments__7__Id": "b4000000-0000-4000-8000-000000000201",
                "IdentitySeed__Assignments__7__UserId": self.LOCAL,
                "IdentitySeed__Assignments__7__RoleId": "30000000-0000-0000-0000-000000000003",
                "IdentitySeed__Assignments__7__CompanyId": "10000000-0000-0000-0000-000000000001",
                "IdentitySeed__Assignments__7__ValidFromUtc": "2026-01-01T00:00:00+00:00",
                "IdentitySeed__Assignments__7__Scopes__0__Type": "Company",
                "IdentitySeed__Assignments__7__Scopes__0__Id": "10000000-0000-0000-0000-000000000001"},
        }
        for label, changes in cases.items():
            with self.subTest(label), self.assertRaises(browser.ContractError):
                self._validate(self.issuer(**changes))

    def test_a_suite_actor_cannot_be_named_as_the_issuer(self):
        # s03 binds the issuer and has actors: an actor called s03-issuer would sign in as the issuer.
        extension = self.issuer(suite="s03")
        extension.update({
            "BootstrapIdentity__Users__2__UserId": "s03-issuer",
            "BootstrapIdentity__Users__2__DisplayName": "SENTETIK S03 issuer actor",
        })
        with self.assertRaisesRegex(browser.ContractError, "s03-issuer"):
            self._validate(extension, suite="s03")

    def test_a_suite_without_the_issuer_list_cannot_seed_a_non_actor_user(self):
        # An identity-enabled suite that does not bind the issuer (org03 given an Identity list here).
        extension = {key: value for key, value in self.issuer(suite="org03").items()
                     if not key.startswith("ProductRuntime__")}
        lists = {"org03": {"issuer": False, "actors": True, "identity": True}}
        with mock.patch.dict(browser.SUITE_API_EXTENSIONS, lists), \
                self.assertRaisesRegex(browser.ContractError, "only the bound Product issuer"):
            self._validate(extension, suite="org03")


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

    def test_the_runner_is_built_without_the_envelope_and_run_without_building(self):
        # Run 35392194453: building the runner builds the Api, whose build-time OpenAPI generation starts
        # Program with the step's environment; the run's C-01 envelope variables made it request the envelope
        # outside Testing. The build removes every envelope variable by prefix; materialize never builds.
        build = self._step("Build the suite fixture runner")
        self.assertIn("if: env.BROWSER_E2E_SUITE_FIXTURE == 'true'", build)
        self.assertIn("compgen -e | grep -E '^(GOYMEN_ERP_C01_E2E_|C01E2e__)'", build)
        self.assertIn('env "${envelope[@]}" dotnet build', build)
        self.assertIn("tests/GoymenERP.BrowserSuiteFixtureTool/GoymenERP.BrowserSuiteFixtureTool.csproj", build)
        materialize = self._step("Materialize the suite's fixture")
        self.assertIn("--no-build", materialize)
        order = [WORKFLOW.index(f"      - name: {name}\n")
                 for name in ("Restore suite fixture runner", "Build the suite fixture runner",
                              "Materialize the suite's fixture")]
        self.assertEqual(sorted(order), order)
        # No other step may build anything that references the Api with the envelope in its environment.
        for step in re.split(r"\n      - name: ", WORKFLOW)[1:]:
            if "BrowserSuiteFixtureTool.csproj" in step and not step.startswith("Restore suite fixture runner"):
                with self.subTest(step=step.split("\n", 1)[0]):
                    self.assertTrue("--no-build" in step or 'env "${envelope[@]}" dotnet build' in step)

    def test_the_profile_step_exports_a_boolean_fixture_flag(self):
        step = self._step("Select the suite's capability profile")
        self.assertIn('test "$fixture" = "true" || test "$fixture" = "false"', step)
        self.assertIn('echo "BROWSER_E2E_SUITE_FIXTURE=$fixture"', step)


if __name__ == "__main__":
    unittest.main()
