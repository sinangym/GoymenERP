import contextlib
import io
import json
import pathlib
import re
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))

import validate_browser_acceptance as validator  # noqa: E402

WORKFLOW = ROOT / ".github" / "workflows" / "browser-acceptance.yml"


class AllowlistTests(unittest.TestCase):
    def test_each_suite_uploads_only_its_own_junit_and_the_shared_evidence(self):
        for suite in validator.SUITES:
            with self.subTest(suite=suite):
                self.assertEqual(
                    validator.upload_allowlist(suite),
                    (
                        f"browser-{suite}-junit.xml",
                        "browser-fixture-evidence.json",
                        "browser-suite-fixture-evidence.json",
                        "browser-suite-seed.json",
                        "browser-post-browser-sql-evidence.json",
                        "container-logs/compose-ps.txt",
                    ),
                )

    def test_unknown_or_unsafe_suite_names_are_refused(self):
        for suite in ("", "C03", "c03/../x", "../smoke", "unknown", "c 03"):
            with self.subTest(suite=suite):
                with self.assertRaises(validator.ContractError):
                    validator.upload_allowlist(suite)

    def test_read_only_suites_are_a_subset_of_the_suites(self):
        self.assertTrue(set(validator.READ_ONLY_SUITES) <= set(validator.SUITES))


class U02InventoryRecorderContractTests(unittest.TestCase):
    @staticmethod
    def environment() -> dict[str, str]:
        source = (ROOT / "infra" / "browser-suites" / "u02.override.yml").read_text(encoding="utf-8")
        environment = {}
        for line in source.splitlines():
            match = re.fullmatch(r"\s{6}([A-Za-z0-9_]+):\s*(.+)", line)
            if match is not None:
                raw = match.group(2)
                environment[match.group(1)] = json.loads(raw) if raw.startswith('"') else raw
        return environment

    def test_exact_recorder_identity_permissions_and_existing_issuer(self):
        environment = self.environment()
        self.assertEqual(8, validator.MAX_SUITE_ACTORS)
        self.assertNotIn("u02", validator.SUITE_ACTOR_LIMITS)
        self.assertEqual(
            ("identity.profile.read", "inventory.opening-stock.record",
             "product.operational-selection.read", "inventory.labels.read", "inventory.balances.read"),
            validator.SUITE_IDENTITY_PERMISSIONS["u02"],
        )
        self.assertEqual(
            "b2000000-0000-4000-8000-000000000201",
            environment["ProductRuntime__IssuerLocalUserId"],
        )
        self.assertEqual("u02-inventory-recorder", environment["BootstrapIdentity__Users__3__UserId"])
        self.assertEqual("b2000000-0000-4000-8000-000000000220", environment["IdentitySeed__Users__7__Id"])
        self.assertEqual("b2100000-0000-4000-8000-000000000220",
                         environment["IdentitySeed__Users__7__ExternalIdentities__0__Id"])
        self.assertEqual("b3000000-0000-4000-8000-000000000220", environment["IdentitySeed__Roles__6__Id"])
        self.assertEqual("b4000000-0000-4000-8000-000000000220",
                         environment["IdentitySeed__Assignments__8__Id"])
        validator.validate_suite_extensions("u02", environment)

    def test_recorder_and_activator_roles_refuse_union_foreign_company_and_issuer_drift(self):
        environment = self.environment()
        mutations = (
            ("recorder privilege union", {"IdentitySeed__Roles__6__Permissions__4": "identity.profile.read"}),
            ("activator privilege union", {"IdentitySeed__Roles__5__Permissions__1": "inventory.labels.read"}),
            ("recorder role swap", {"IdentitySeed__Assignments__8__RoleId":
                                     "b3000000-0000-4000-8000-000000000210"}),
            ("foreign company", {"IdentitySeed__Assignments__8__CompanyId":
                                  "10000000-0000-0000-0000-000000000002",
                                  "IdentitySeed__Assignments__8__Scopes__0__Id":
                                  "10000000-0000-0000-0000-000000000002"}),
            ("issuer drift", {"ProductRuntime__IssuerLocalUserId":
                              "b2000000-0000-4000-8000-000000000202",
                              "IdentitySeed__Users__5__Id":
                              "b2000000-0000-4000-8000-000000000202"}),
            ("unlisted permission", {"IdentitySeed__Roles__6__Permissions__4": "documents.files.read"}),
        )
        for label, changed in mutations:
            with self.subTest(case=label):
                candidate = dict(environment)
                candidate.update(changed)
                with self.assertRaises(validator.ContractError):
                    validator.validate_suite_extensions("u02", candidate)

    def test_issuer_only_remains_valid_but_inventory_permission_requires_complete_recorder(self):
        environment = self.environment()
        issuer_only = {
            key: value for key, value in environment.items()
            if key.startswith("ProductRuntime__Issuer") or key.startswith("IdentitySeed__Users__5__")
        }
        validator.validate_suite_extensions("u02", issuer_only)
        prior_activator = {
            key: value for key, value in environment.items()
            if not key.startswith(("BootstrapIdentity__Users__3__", "IdentitySeed__Users__7__",
                                   "IdentitySeed__Roles__6__", "IdentitySeed__Assignments__8__"))
        }
        validator.validate_suite_extensions("u02", prior_activator)
        forbidden = dict(prior_activator)
        forbidden["IdentitySeed__Roles__5__Permissions__1"] = "inventory.labels.read"
        with self.assertRaises(validator.ContractError):
            validator.validate_suite_extensions("u02", forbidden)

        unknown_role = dict(environment)
        unknown_role.update({
            "IdentitySeed__Roles__7__Id": "b3000000-0000-4000-8000-000000000230",
            "IdentitySeed__Roles__7__Code": "BROWSER_U02_EXTRA",
            "IdentitySeed__Roles__7__Name": "SENTETIK browser-suite:u02 extra role",
            "IdentitySeed__Roles__7__IsSystem": "false",
            "IdentitySeed__Roles__7__Permissions__0": "inventory.labels.read",
        })
        with self.assertRaises(validator.ContractError):
            validator.validate_suite_extensions("u02", unknown_role)

    def test_fixture_output_keeps_six_keys_and_adds_only_exact_recorder(self):
        seed = {
            "companyId": "10000000-0000-0000-0000-000000000001",
            "codeGroupId": "b1000000-0000-4000-8000-000000000001",
            "technicalFamilyId": "b1000000-0000-4000-8000-000000000002",
            "authorizedActor": "u02-authorized",
            "unauthorizedActor": "u02-unauthorized",
            "activatorActor": "u02-activator",
            "inventoryRecorderActor": "u02-inventory-recorder",
        }
        self.assertEqual(frozenset(seed), validator.SUITE_FIXTURE_OUTPUTS["u02"])
        self.assertEqual(seed, json.loads(validator.seed_export("u02", seed).split("=", 1)[1]))
        for label, changed in (
            ("missing recorder", {"inventoryRecorderActor": None}),
            ("duplicate actor", {"inventoryRecorderActor": "u02-activator"}),
            ("foreign company", {"companyId": "10000000-0000-0000-0000-000000000002"}),
            ("actor as family", {"technicalFamilyId": "u02-authorized"}),
            ("unhashable actor", {"authorizedActor": []}),
            ("extra location", {"eligibleLocationId": "10000000-0000-0000-0000-000000000001"}),
        ):
            with self.subTest(case=label):
                candidate = dict(seed)
                candidate.update(changed)
                with self.assertRaises(validator.ContractError):
                    validator.seed_export("u02", candidate)
        missing = dict(seed)
        missing.pop("inventoryRecorderActor")
        with self.assertRaises(validator.ContractError):
            validator.seed_export("u02", missing)


class PrivacyTests(unittest.TestCase):
    def _root(self, files: dict[str, bytes]) -> tempfile.TemporaryDirectory:
        directory = tempfile.TemporaryDirectory()
        root = pathlib.Path(directory.name)
        for relative, content in files.items():
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        return directory

    def test_a_clean_suite_report_passes(self):
        with self._root({"browser-smoke-junit.xml": b"<testsuites><testcase name='a'/></testsuites>"}) as root:
            validator.validate_privacy(root, "smoke", ["long-enough-secret"])

    def test_a_runtime_secret_in_an_uploaded_file_is_refused(self):
        with self._root({"browser-c03-junit.xml": b"<x>long-enough-secret</x>"}) as root:
            with self.assertRaisesRegex(validator.ContractError, "runtime secret 0"):
                validator.validate_privacy(root, "c03", ["long-enough-secret"])

    def test_a_c01_forbidden_pattern_is_refused(self):
        with self._root({"browser-smoke-junit.xml": b"GET /api/v1/counterparties?cursor=abc"}) as root:
            with self.assertRaisesRegex(validator.ContractError, "cursor query"):
                validator.validate_privacy(root, "smoke")

    def test_another_suites_report_is_not_uploaded_and_nothing_selected_fails(self):
        with self._root({"browser-c03-junit.xml": b"<ok/>"}) as root:
            with self.assertRaisesRegex(validator.ContractError, "selected no files"):
                validator.validate_privacy(root, "smoke")

    def test_short_runtime_values_are_refused(self):
        with self._root({"browser-smoke-junit.xml": b"<ok/>"}) as root:
            with self.assertRaisesRegex(validator.ContractError, "too short"):
                validator.validate_privacy(root, "smoke", ["short"])

    def test_the_cli_reports_failure_with_exit_code_one(self):
        with self._root({"browser-smoke-junit.xml": b"<x>long-enough-secret</x>"}) as root:
            self.assertEqual(
                validator.main(["privacy", "--root", root, "--suite", "smoke", "--forbidden-value", "long-enough-secret"]),
                1,
            )

    @staticmethod
    def _s04_report(
        case_body: str,
        case_name: str = (
            "S04-PL-03 below-floor pricing is saved pending and a different actor approves it once with a reason"
        ),
    ) -> bytes:
        return (
            "<testsuites name=''><testsuite name='chromium'>"
            f"<testcase name='{case_name}' classname='tests/e2e/sales/s04-price-list.spec.ts'>"
            f"{case_body}</testcase>"
            "</testsuite></testsuites>"
        ).encode("utf-8")

    def _privacy_cli(self, report: bytes, *forbidden: str) -> tuple[int, str, str]:
        with self._root({"browser-s04-junit.xml": report}) as root:
            stdout = io.StringIO()
            stderr = io.StringIO()
            arguments = ["privacy", "--root", root, "--suite", "s04"]
            for value in forbidden:
                arguments.extend(("--forbidden-value", value))
            with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = validator.main(arguments)
            return result, stdout.getvalue(), stderr.getvalue()

    def test_s04_failed_case_emits_only_the_closed_direct_phase(self):
        report = self._s04_report(
            "<properties><property name='safe_failure_phase' value='s04_pl03_p4'/></properties>"
            "<failure message='opaque-secret-path'>opaque-secret-message</failure>"
            "<system-out>opaque-secret-stdout</system-out>"
        )
        result, stdout, stderr = self._privacy_cli(report)
        self.assertEqual(0, result)
        self.assertEqual(
            "BROWSER_SAFE_PHASE=s04_pl03_p4\nBrowser acceptance contract validation passed.\n",
            stdout,
        )
        self.assertEqual("", stderr)
        for forbidden in ("opaque-secret-path", "opaque-secret-message", "opaque-secret-stdout"):
            self.assertNotIn(forbidden, stdout)

    def test_s04_error_case_accepts_the_last_closed_phase(self):
        report = self._s04_report(
            "<properties><property name='safe_failure_phase' value='s04_pl03_p7'/></properties>"
            "<error/>"
        )
        self.assertEqual("s04_pl03_p7", validator._s04_safe_failure_phase(report))

    def test_s04_passed_skipped_and_other_cases_emit_no_phase(self):
        reports = (
            self._s04_report("<properties><property name='safe_failure_phase' value='s04_pl03_p1'/></properties>"),
            self._s04_report(
                "<properties><property name='safe_failure_phase' value='s04_pl03_p2'/></properties><skipped/>"
            ),
            self._s04_report("<failure/>", case_name="S04-PL-02"),
        )
        for report in reports:
            with self.subTest(report=report):
                result, stdout, _ = self._privacy_cli(report)
                self.assertEqual(0, result)
                self.assertEqual("Browser acceptance contract validation passed.\n", stdout)

    def test_s04_xml_declarations_and_wrong_depth_are_refused(self):
        cases = (
            b"<!DOCTYPE testsuites><testsuites/>",
            b"<!ENTITY leak 'x'><testsuites/>",
            (
                "<?xml version='1.0' encoding='utf-16'?>"
                "<!DOCTYPE testsuites><testsuites/>"
            ).encode("utf-16"),
            (
                "<testsuites><testsuite><testsuite>"
                f"<testcase name='{validator._S04_SAFE_FAILURE_CASE}'>"
                "<properties><property name='safe_failure_phase' value='s04_pl03_p1'/></properties>"
                "<failure/></testcase></testsuite></testsuite></testsuites>"
            ).encode("utf-8"),
            self._s04_report(
                "<metadata><properties><property name='safe_failure_phase' value='s04_pl03_p1'/>"
                "</properties></metadata><failure/>"
            ),
        )
        for report in cases:
            with self.subTest(report=report):
                result, stdout, _ = self._privacy_cli(report)
                self.assertEqual(1, result)
                self.assertEqual("", stdout)

    def test_s04_oversized_excess_element_and_deep_xml_are_refused(self):
        oversized = b"<testsuites>" + (b" " * validator._MAX_JUNIT_BYTES) + b"</testsuites>"
        excess_elements = (
            "<testsuites>"
            + "<node/>" * validator._MAX_JUNIT_ELEMENTS
            + "</testsuites>"
        ).encode("ascii")
        nested = (
            ("<testsuites>" + "<node>" * validator._MAX_JUNIT_DEPTH)
            + "</node>" * validator._MAX_JUNIT_DEPTH
            + "</testsuites>"
        ).encode("ascii")
        for report in (oversized, excess_elements, nested):
            with self.subTest(size=len(report)):
                result, stdout, _ = self._privacy_cli(report)
                self.assertEqual(1, result)
                self.assertEqual("", stdout)

    def test_s04_duplicate_case_marker_and_mixed_outcome_are_refused(self):
        marker = "<properties><property name='safe_failure_phase' value='s04_pl03_p1'/></properties>"
        cases = (
            (
                "<testsuites><testsuite>"
                f"<testcase name='{validator._S04_SAFE_FAILURE_CASE}'>{marker}<failure/></testcase>"
                f"<testcase name='{validator._S04_SAFE_FAILURE_CASE}'>{marker}<failure/></testcase>"
                "</testsuite></testsuites>"
            ).encode("utf-8"),
            self._s04_report(
                "<properties>"
                "<property name='safe_failure_phase' value='s04_pl03_p1'/>"
                "<property name='safe_failure_phase' value='s04_pl03_p2'/>"
                "</properties><failure/>"
            ),
            self._s04_report(f"{marker}<failure/><error/>"),
            self._s04_report(f"{marker}<failure/><skipped/>"),
        )
        for report in cases:
            with self.subTest(report=report):
                result, stdout, _ = self._privacy_cli(report)
                self.assertEqual(1, result)
                self.assertEqual("", stdout)

    def test_s04_unknown_non_ascii_and_oversize_phase_are_refused(self):
        values = ("s04_pl03_p8", "s04_pl03_p1-ş", "x" * 65)
        for value in values:
            report = self._s04_report(
                "<properties>"
                f"<property name='safe_failure_phase' value='{value}'/>"
                "</properties><failure/>"
            )
            with self.subTest(value=value):
                result, stdout, _ = self._privacy_cli(report)
                self.assertEqual(1, result)
                self.assertEqual("", stdout)

    def test_s04_secret_refusal_never_emits_phase_or_secret(self):
        sentinel = "do-not-export-secret-path-or-message"
        report = self._s04_report(
            "<properties><property name='safe_failure_phase' value='s04_pl03_p3'/></properties>"
            f"<failure message='{sentinel}'>{sentinel}</failure>"
        )
        result, stdout, stderr = self._privacy_cli(report, sentinel)
        self.assertEqual(1, result)
        self.assertEqual("", stdout)
        self.assertNotIn(sentinel, stderr)


class WorkflowTests(unittest.TestCase):
    workflow = WORKFLOW.read_text(encoding="utf-8")

    @staticmethod
    def _acc01_seed():
        return {
            "companyId": "10000000-0000-0000-0000-000000000001",
            "companyBId": "10000000-0000-0000-0000-000000000002",
            "preparerActor": "acc01-preparer",
            "approverActor": "acc01-approver",
            "normalSourceLabel": "Kaynak ref. ...0000ac01 - 2026-09-24",
            "normalOccurredOn": "2026-09-24",
            "normalAmount": "10.25",
            "normalDebitLabel": "SENTETIK-100 - Sentetik borc hesabi (Borç)",
            "normalCreditLabel": "SENTETIK-120 - Sentetik alacak hesabi (Alacak)",
            "openPeriodId": "10000000-0000-0000-0000-000000000003",
        }

    @staticmethod
    def _mfg_seed():
        company_id = "10000000-0000-0000-0000-000000000001"
        return {
            "recorderActor": "mfg-recorder",
            "readerActor": "mfg-reader",
            "closerActor": "mfg-closer",
            "companyId": company_id,
            "workCenterId": "10000000-0000-0000-0000-000000000002",
            "orderId": "10000000-0000-0000-0000-000000000003",
            "rollId": "10000000-0000-0000-0000-000000000004",
            "issueRollId": "10000000-0000-0000-0000-000000000005",
            "issueRollCode": "SYN-MFG-ROLL-2",
            "replayRollId": "10000000-0000-0000-0000-000000000006",
            "replayRollCode": "SYN-MFG-ROLL-3",
            "conflictRollId": "10000000-0000-0000-0000-000000000007",
            "conflictRollCode": "SYN-MFG-ROLL-4",
            "listRollId": "10000000-0000-0000-0000-000000000008",
            "listRollCode": "SYN-MFG-ROLL-5",
            "closeRollId": "10000000-0000-0000-0000-000000000009",
            "closeRollCode": "SYN-MFG-ROLL-6",
            "planCreateRollId": "10000000-0000-0000-0000-000000000010",
            "planCreateRollCode": "SYN-MFG-ROLL-7",
            "planReplayRollId": "10000000-0000-0000-0000-000000000011",
            "planReplayRollCode": "SYN-MFG-ROLL-8",
            "planRefusalRollId": "10000000-0000-0000-0000-000000000012",
            "planRefusalRollCode": "SYN-MFG-ROLL-9",
            "outputUiRollId": "10000000-0000-0000-0000-000000000013",
            "outputUiRollCode": "SYN-MFG-ROLL-10",
            "outputProductId": "10000000-0000-0000-0000-000000000014",
            "outputProductVersion": "1",
            "outputRequiredDocuments": "[]",
            "outputOwnerKind": "Company",
            "outputOwnerId": company_id,
            "outputCustodianKind": "Company",
            "outputCustodianId": company_id,
            "outputLocationId": "10000000-0000-0000-0000-000000000015",
            "outputInitialBlock": "Free",
        }

    def test_mfg_fixture_output_is_the_exact_current_closed_contract(self):
        seed = self._mfg_seed()
        exported = validator.seed_export("mfg", seed)
        prefix, payload = exported.split("=", 1)

        self.assertEqual("BROWSER_E2E_MFG_SEED", prefix)
        self.assertEqual(seed, json.loads(payload))
        self.assertEqual(frozenset(seed), validator.SUITE_FIXTURE_OUTPUTS["mfg"])

    def test_mfg_fixture_output_rejects_missing_and_extra_keys(self):
        missing = self._mfg_seed()
        missing.pop("outputLocationId")
        extra = self._mfg_seed()
        extra["futureOutput"] = "10000000-0000-0000-0000-000000000015"

        for seed in (missing, extra):
            with self.subTest(keys=sorted(seed)):
                with self.assertRaisesRegex(validator.ContractError, "closed list"):
                    validator.seed_export("mfg", seed)

    def test_mfg_fixture_output_rejects_noncanonical_values(self):
        invalid_values = {
            "issueRollCode": "SYN-MFG-ROLL-02",
            "outputUiRollCode": "SYN-MFG-ROLL-11",
            "outputProductVersion": "0",
            "outputRequiredDocuments": "[ ]",
            "outputOwnerKind": "Counterparty",
            "outputCustodianKind": "counterparty",
            "outputInitialBlock": "Blocked",
        }
        for key, value in invalid_values.items():
            seed = self._mfg_seed()
            seed[key] = value
            with self.subTest(key=key, value=value):
                with self.assertRaises(validator.ContractError):
                    validator.seed_export("mfg", seed)

    def test_mfg_fixture_output_binds_owner_and_custodian_to_the_seeded_company(self):
        for key in ("outputOwnerId", "outputCustodianId"):
            seed = self._mfg_seed()
            seed[key] = "20000000-0000-0000-0000-000000000001"
            with self.subTest(key=key):
                with self.assertRaisesRegex(validator.ContractError, "seeded company"):
                    validator.seed_export("mfg", seed)

    def test_mfg_fixture_output_rejects_cross_type_bypasses(self):
        actor_keys = {"recorderActor", "readerActor", "closerActor"}
        scalar_keys = {
            "issueRollCode", "replayRollCode", "conflictRollCode", "listRollCode",
            "closeRollCode", "planCreateRollCode", "planReplayRollCode", "planRefusalRollCode",
            "outputUiRollCode",
            "outputProductVersion", "outputRequiredDocuments", "outputOwnerKind",
            "outputCustodianKind", "outputInitialBlock",
        }
        identity_keys = set(self._mfg_seed()) - actor_keys - scalar_keys
        guid_value = "30000000-0000-0000-0000-000000000001"
        actor_value = "mfg-cross-type-actor"

        for key in actor_keys:
            seed = self._mfg_seed()
            seed[key] = guid_value
            with self.subTest(kind="actor-as-guid", key=key):
                with self.assertRaisesRegex(validator.ContractError, "current-run actor"):
                    validator.seed_export("mfg", seed)

        for key in identity_keys:
            seed = self._mfg_seed()
            seed[key] = actor_value
            with self.subTest(kind="guid-as-actor", key=key):
                with self.assertRaisesRegex(validator.ContractError, "lowercase GUID"):
                    validator.seed_export("mfg", seed)

        for key in scalar_keys:
            for value in (guid_value, actor_value):
                seed = self._mfg_seed()
                seed[key] = value
                with self.subTest(kind="scalar-fallback", key=key, value=value):
                    with self.assertRaises(validator.ContractError):
                        validator.seed_export("mfg", seed)

        seed = self._mfg_seed()
        seed["companyId"] = actor_value
        seed["outputOwnerId"] = actor_value
        seed["outputCustodianId"] = actor_value
        with self.assertRaisesRegex(validator.ContractError, "lowercase GUID"):
            validator.seed_export("mfg", seed)

    def test_mfg_identity_is_the_real_plan_and_opening_stock_closed_list(self):
        self.assertTrue(validator.SUITE_API_EXTENSIONS["mfg"]["issuer"])
        self.assertEqual(
            (
                "manufacturing.plans.write",
                "manufacturing.plans.read",
                "manufacturing.plans.approve",
                "manufacturing.orders.create",
                "manufacturing.facts.record",
                "manufacturing.orders.read",
                "manufacturing.orders.close",
                "manufacturing.orders.close-exception.approve",
                "organization.work-centers.read",
                "inventory.labels.read",
                "inventory.balances.read",
                "inventory.opening-stock.record",
                "product.operational-selection.read",
            ),
            validator.SUITE_IDENTITY_PERMISSIONS["mfg"],
        )

    def test_acc01_fixture_output_is_the_exact_current_closed_contract(self):
        seed = self._acc01_seed()
        exported = validator.seed_export("acc01", seed)
        prefix, payload = exported.split("=", 1)

        self.assertEqual("BROWSER_E2E_ACC01_SEED", prefix)
        self.assertEqual(seed, json.loads(payload))
        self.assertEqual(frozenset(seed), validator.SUITE_FIXTURE_OUTPUTS["acc01"])

    def test_acc01_fixture_output_rejects_every_noncanonical_scalar_and_unknown_key(self):
        invalid_values = {
            "normalSourceLabel": "Kaynak ref. ...0000AC01 - 2026-09-24",
            "normalOccurredOn": "2026-09-25",
            "normalAmount": "10.250",
            "normalDebitLabel": "SENTETIK-100 - Sentetik borc hesabi",
            "normalCreditLabel": "SENTETIK-120 - Sentetik alacak hesabi (Alacak) ",
        }
        for key, value in invalid_values.items():
            seed = self._acc01_seed()
            seed[key] = value
            with self.subTest(key=key, value=value):
                with self.assertRaises(validator.ContractError):
                    validator.seed_export("acc01", seed)

        unknown = self._acc01_seed()
        unknown["unexpected"] = "10.25"
        with self.assertRaisesRegex(validator.ContractError, "closed list"):
            validator.seed_export("acc01", unknown)

    def test_acc01_fixture_output_rejects_guid_and_actor_fallback_for_every_scalar(self):
        scalar_keys = {
            "normalSourceLabel", "normalOccurredOn", "normalAmount",
            "normalDebitLabel", "normalCreditLabel",
        }
        for key in scalar_keys:
            for value in (
                "10000000-0000-0000-0000-000000000001",
                "acc01-preparer",
            ):
                seed = self._acc01_seed()
                seed[key] = value
                with self.subTest(key=key, value=value):
                    with self.assertRaises(validator.ContractError):
                        validator.seed_export("acc01", seed)

    def test_fin01_identity_permissions_match_the_owner_fixture_closed_list(self):
        self.assertEqual(
            (
                "finance.bank-account.read",
                "finance.bank-account.propose",
                "finance.bank-account.approve",
                "finance.manual-receipt.draft.save",
                "finance.manual-receipt.draft.read",
                "finance.manual-receipt.amount.read",
                "counterparty.parties.read",
                "finance.manual-receipt.record",
                "accounting.periods.read",
                "finance.manual-receipt.reverse.propose",
                "finance.manual-receipt.reverse.approve",
                "finance.manual-bank-payment.draft.save",
                "finance.manual-bank-payment.draft.read",
                "finance.manual-bank-payment.approve",
                "finance.manual-bank-payment.record",
                "finance.customer-instrument.read",
                "finance.customer-instrument.amount.read",
                "finance.customer-instrument.receive",
                "finance.customer-instrument.status.change",
                "finance.customer-instrument.collect",
                "finance.party-ledger.read",
                "finance.party-ledger.amount.read",
                "finance.official-invoice-import.upload",
                "finance.official-invoice-import.approve",
                "finance.allocation.read",
                "finance.allocation.write",
            ),
            validator.SUITE_IDENTITY_PERMISSIONS["fin01"],
        )

    def test_fin01_fixture_output_requires_the_read_only_actor(self):
        seed = {
            "companyId": "10000000-0000-0000-0000-000000000001",
            "partyId": "b1000000-0000-4000-8000-00000000f101",
            "companyAccountId": "b1000000-0000-4000-8000-00000000f102",
            "proposerActor": "fin01-proposer",
            "approverActor": "fin01-approver",
            "readerActor": "fin01-reader",
        }

        exported = validator.seed_export("fin01", seed)
        prefix, payload = exported.split("=", 1)

        self.assertEqual("BROWSER_E2E_FIN01_SEED", prefix)
        self.assertEqual(seed, json.loads(payload))
        self.assertEqual(frozenset(seed), validator.SUITE_FIXTURE_OUTPUTS["fin01"])
        for key in ("readerActor", "unexpectedActor"):
            malformed = dict(seed)
            if key == "readerActor":
                malformed.pop(key)
            else:
                malformed[key] = "fin01-unlisted"
            with self.subTest(key=key):
                with self.assertRaises(validator.ContractError):
                    validator.seed_export("fin01", malformed)

        duplicate = dict(seed)
        duplicate["readerActor"] = duplicate["approverActor"]
        with self.assertRaisesRegex(validator.ContractError, "distinct"):
            validator.seed_export("fin01", duplicate)

    def test_s04_identity_permissions_accept_customer_acceptance_and_reject_unlisted_grants(self):
        permissions = validator.SUITE_IDENTITY_PERMISSIONS["s04"]
        self.assertEqual(
            (
                "sales.drafts.create",
                "sales.drafts.read",
                "sales.orders.approve",
                "counterparty.parties.read",
                "counterparty.parties.update.basic",
                "product.operational-selection.read",
                "sales.orders.price.read",
                "sales.orders.price.write",
                "product.price-lists.read",
                "product.price-lists.write",
                "product.price-lists.publish",
                "sales.orders.price-floor-exception.approve",
                "inventory.opening-stock.record",
                "inventory.labels.read",
                "inventory.balances.read",
                "inventory.physical-allocation.write",
                # DEC-SAL-003: held only by the disposable s04-planner.
                "sales.orders.delivery-schedule.write",
                # DEC-SAL-005: held only by the disposable s04-canceller.
                "sales.orders.cancel",
                # DEC-SAL-002: held only by the disposable s04-preparer.
                "sales.orders.quantity.write",
                # Q9: held only by that same disposable preparer; no production grant is widened.
                "sales.orders.customer-acceptance.record",
                # S04-RM: two new disposable actors reproduce L04's preparer/loader split.
                "logistics.loading-instructions.create",
                "logistics.loading-instructions.read",
                "logistics.loads.record",
                "logistics.shipments.record",
                "logistics.dispatch-notes.create",
                "logistics.dispatch-notes.approve",
                "logistics.deliveries.record",
            ),
            permissions,
        )
        self.assertIn("sales.orders.customer-acceptance.record", permissions)
        self.assertNotIn("counterparty.parties.create", permissions)

        source = (ROOT / "infra" / "browser-suites" / "s04.override.yml").read_text(encoding="utf-8")
        preparer = {
            int(index): permission
            for index, permission in re.findall(
                r"IdentitySeed__Roles__14__Permissions__(\d+): ([^\r\n]+)", source)
        }
        loader = {
            int(index): permission
            for index, permission in re.findall(
                r"IdentitySeed__Roles__15__Permissions__(\d+): ([^\r\n]+)", source)
        }
        self.assertEqual(
            {
                0: "logistics.loading-instructions.create",
                1: "logistics.loading-instructions.read",
                2: "counterparty.parties.read",
            },
            preparer,
        )
        self.assertEqual(
            {
                0: "logistics.loads.record",
                1: "logistics.loading-instructions.read",
                2: "logistics.shipments.record",
                3: "inventory.physical-allocation.write",
                4: "logistics.dispatch-notes.create",
                5: "logistics.dispatch-notes.approve",
                6: "logistics.deliveries.record",
            },
            loader,
        )
        for forbidden in (
            "sales.orders.cancel",
            "logistics.weighings.record",
            "logistics.dispatch-notes.cancel",
        ):
            self.assertNotIn(forbidden, preparer.values())
            self.assertNotIn(forbidden, loader.values())

        def assignment(index: int) -> dict[str, str]:
            return {
                field: value
                for field, value in re.findall(
                    rf"IdentitySeed__Assignments__{index}__(Id|UserId|RoleId|CompanyId|ApprovedByUserId|Scopes__0__Type|Scopes__0__Id): ([^\r\n]+)",
                    source,
                )
            }

        company_id = "10000000-0000-0000-0000-000000000001"
        canonical_approver_id = "20000000-0000-0000-0000-000000000003"
        self.assertEqual(
            {
                "Id": "b4000000-0000-4000-8000-000000000410",
                "UserId": "b2000000-0000-4000-8000-000000000409",
                "RoleId": "b3000000-0000-4000-8000-000000000410",
                "CompanyId": company_id,
                "ApprovedByUserId": canonical_approver_id,
                "Scopes__0__Type": "Company",
                "Scopes__0__Id": company_id,
            },
            assignment(16),
        )
        self.assertEqual(
            {
                "Id": "b4000000-0000-4000-8000-000000000411",
                "UserId": "b2000000-0000-4000-8000-000000000410",
                "RoleId": "b3000000-0000-4000-8000-000000000411",
                "CompanyId": company_id,
                "ApprovedByUserId": canonical_approver_id,
                "Scopes__0__Type": "Company",
                "Scopes__0__Id": company_id,
            },
            assignment(17),
        )

        environment = {}
        for line in source.splitlines():
            match = re.fullmatch(r"\s{6}([A-Za-z0-9_]+):\s*(.+)", line)
            if match is not None:
                raw = match.group(2)
                environment[match.group(1)] = json.loads(raw) if raw.startswith('"') else raw

        self.assertEqual(8, validator.MAX_SUITE_ACTORS)
        self.assertEqual({"s04": 10}, validator.SUITE_ACTOR_LIMITS)
        validator.validate_suite_extensions("s04", environment)

        too_many = dict(environment)
        too_many["BootstrapIdentity__Users__12__UserId"] = "s04-eleventh-actor"
        too_many["BootstrapIdentity__Users__12__DisplayName"] = "SENTETIK browser-suite:s04 eleventh actor"
        with self.assertRaisesRegex(validator.ContractError, "at most 10 suite actors"):
            validator.validate_suite_extensions("s04", too_many)

        duplicate = dict(environment)
        duplicate["BootstrapIdentity__Users__11__UserId"] = environment["BootstrapIdentity__Users__10__UserId"]
        with self.assertRaisesRegex(validator.ContractError, "suite actor id repeated"):
            validator.validate_suite_extensions("s04", duplicate)

        noncontiguous = dict(environment)
        for field in ("UserId", "DisplayName"):
            noncontiguous[f"BootstrapIdentity__Users__12__{field}"] = noncontiguous.pop(
                f"BootstrapIdentity__Users__11__{field}"
            )
        with self.assertRaisesRegex(validator.ContractError, "contiguously from index 2"):
            validator.validate_suite_extensions("s04", noncontiguous)

        foreign_scope = dict(environment)
        foreign_scope["IdentitySeed__Assignments__17__Scopes__0__Id"] = "10000000-0000-0000-0000-000000000002"
        with self.assertRaisesRegex(validator.ContractError, "company-wide on its own company"):
            validator.validate_suite_extensions("s04", foreign_scope)

    def test_l04_identity_permissions_include_the_shipment_delivery_and_inventory_axis(self):
        self.assertEqual(
            (
                "logistics.loading-instructions.create",
                "logistics.loading-instructions.read",
                "logistics.loads.record",
                "logistics.shipments.record",
                "logistics.deliveries.record",
                "logistics.weighings.record",
                "logistics.dispatch-notes.create",
                "logistics.dispatch-notes.approve",
                "logistics.dispatch-notes.cancel",
                "logistics.customer-returns.read",
                "logistics.customer-returns.request",
                "logistics.customer-returns.decision",
                "sales.drafts.create",
                "sales.drafts.read",
                "sales.orders.approve",
                "counterparty.parties.read",
                "inventory.opening-stock.record",
                "inventory.labels.read",
                "inventory.balances.read",
                "inventory.physical-allocation.write",
                "product.operational-selection.read",
                "logistics.operations-work.sales.read",
                "logistics.operations-work.purchase.read",
                "purchasing.orders.read",
                "purchasing.goods-receipts.record",
                "purchasing.orders.approve",
            ),
            validator.SUITE_IDENTITY_PERMISSIONS["l04"],
        )

    def test_pur01_identity_permissions_are_the_creator_owner_reads_and_existing_approval_axis(self):
        self.assertEqual(
            (
                "purchasing.orders.read",
                "purchasing.orders.approve",
                "purchasing.orders.create",
                "counterparty.parties.read",
                "product.operational-selection.read",
                "purchasing.goods-receipts.record",
                "inventory.labels.read",
                "inventory.balances.read",
            ),
            validator.SUITE_IDENTITY_PERMISSIONS["pur01"],
        )

    def test_r14_identity_permissions_are_the_command_detail_reads_and_location_arranger(self):
        self.assertEqual(
            (
                "inventory.opening-stock.record",
                "product.operational-selection.read",
                "inventory.labels.read",
                "inventory.balances.read",
                # AT-065: disposable recorder only; denied actor remains without either Documents permission.
                "documents.files.read",
                "documents.files.upload",
                # Classification write remains on the same disposable recorder only.
                "inventory.label-classification.write",
                "organization.locations.read",
                # R19: held only by the disposable r14-transformer.
                "inventory.label-transformation.record",
                # AT-037: record held only by r14-counter, approve only by r14-count-approver.
                "inventory.stock-count.record",
                "inventory.stock-count.approve",
                # ST-02 scrap write-off: record held only by r14-scrapper, approve only by r14-scrap-approver.
                "inventory.stock-scrap.record",
                "inventory.stock-scrap.approve",
            ),
            validator.SUITE_IDENTITY_PERMISSIONS["r14"],
        )

        source = (ROOT / "infra" / "browser-suites" / "r14.override.yml").read_text(encoding="utf-8")
        recorder = {
            int(index): permission
            for index, permission in re.findall(
                r"IdentitySeed__Roles__5__Permissions__(\d+): ([^\r\n]+)", source)
        }
        denied = {
            int(index): permission
            for index, permission in re.findall(
                r"IdentitySeed__Roles__6__Permissions__(\d+): ([^\r\n]+)", source)
        }
        self.assertEqual("inventory.label-classification.write", recorder[6])
        self.assertNotIn("inventory.label-classification.write", denied.values())

    def _trigger_block(self) -> str:
        return self.workflow.split("\non:\n", 1)[1].split("\npermissions:\n", 1)[0]

    def test_it_is_selectable_only_and_never_runs_on_push(self):
        trigger = self._trigger_block()
        self.assertIn("workflow_dispatch:", trigger)
        self.assertIn("pull_request:\n    types:\n      - labeled\n", trigger)
        self.assertNotIn("push:", trigger)
        self.assertNotIn("synchronize", trigger)
        self.assertNotIn("schedule:", trigger)
        self.assertNotIn("pull_request_target", self.workflow)

    def test_it_has_no_secrets_and_read_only_permissions(self):
        self.assertNotIn("secrets.", self.workflow)
        permissions = self.workflow.split("\npermissions:\n", 1)[1].split("\n\n", 1)[0]
        self.assertEqual([line.strip() for line in permissions.strip().splitlines()], ["contents: read", "pull-requests: read"])
        self.assertIn("persist-credentials: false", self.workflow)

    def test_dispatch_suites_match_the_validator(self):
        options = self.workflow.split("options:\n", 1)[1].split("      pr:", 1)[0]
        listed = [line.strip().removeprefix("- ") for line in options.splitlines() if line.strip()]
        self.assertEqual(tuple(listed), validator.SUITES)
        for suite in validator.SUITES:
            self.assertIn(suite, self.workflow.split('case "$suite" in', 1)[1].split("esac", 1)[0])

    def test_exact_head_fork_refusal_and_base_ancestry_are_enforced(self):
        for required in (
            "BROWSER_FORK_REFUSED",
            "BROWSER_PR_NOT_OPEN",
            "BROWSER_BASE_NOT_MAIN",
            "BROWSER_HEAD_MOVED",
            'test "$actual_head" = "$EXPECTED_HEAD_SHA"',
            'git merge-base --is-ancestor "$EXPECTED_BASE_SHA" HEAD',
            "ref: ${{ needs.resolve.outputs.head_sha }}",
        ):
            with self.subTest(required=required):
                self.assertIn(required, self.workflow)

    def test_measured_limits_and_fail_visible_gates(self):
        for required in (
            "timeout-minutes: 45",
            "timeout-minutes: 5",
            "cancel-in-progress: true",
            "retention-days: 7",
            "SUITE_NOT_READY",
            "BROWSER_NOT_CLEAN",
            "BROWSER_NO_CASES",
            "GOYMEN_ERP_C01_E2E_DISPOSABLE=CONFIRMED",
            "down -v --remove-orphans",
            "id: artifact_privacy",
            "tools/validate_browser_acceptance.py privacy",
            "steps.artifact_privacy.outcome == 'success'",
        ):
            with self.subTest(required=required):
                self.assertIn(required, self.workflow)

    def test_upload_paths_are_exactly_the_allowlist(self):
        upload = self.workflow.split("      - name: Upload exact-head browser evidence", 1)[1]
        path_block = upload.split("          path: |", 1)[1].split("          if-no-files-found:", 1)[0]
        uploaded = {line.strip().removeprefix("artifacts/") for line in path_block.splitlines() if line.strip()}
        template = "${{ needs.resolve.outputs.suite }}"
        expected = {entry.replace("smoke", template) for entry in validator.upload_allowlist("smoke")}
        self.assertEqual(uploaded, expected)

    def test_disposable_names_follow_the_c01_tooling(self):
        # The C-01 compose validator and fixture tool derive the database from run id + attempt
        # (second real run 35358425231 failed "API disposable DB drift" with another name).
        self.assertIn('database_name="GoymenErpC01_${GITHUB_RUN_ID}_${GITHUB_RUN_ATTEMPT}"', self.workflow)
        # The API's disposable activation accepts only goymen-c01-<run>-<attempt> (third real run
        # 35358660852: the API refused to start with goymen-browser-...).
        self.assertIn('compose_project="goymen-c01-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"', self.workflow)

    def test_bound_names_satisfy_every_consumer_of_the_c01_naming_contract(self):
        # Four places carry the disposable naming contract; runs 2 and 3 each failed on one of them.
        # This reads each consumer's rule from source and checks the workflow's bound values against
        # it, so a drift in any of the four fails here, locally, before a run spends minutes.
        run_id, attempt = "35359455991", "1"
        bind = self.workflow.split("      - name: Bind exact source and disposable run secrets", 1)[1]
        project_template = re.search(r'compose_project="([^"]+)"', bind).group(1)
        database_template = re.search(r'database_name="([^"]+)"', bind).group(1)
        project = project_template.replace("${GITHUB_RUN_ID}", run_id).replace("${GITHUB_RUN_ATTEMPT}", attempt)
        database = database_template.replace("${GITHUB_RUN_ID}", run_id).replace("${GITHUB_RUN_ATTEMPT}", attempt)

        activation = (ROOT / "src/backend/GoymenERP.Api/Shell/C01E2eShellCapabilityActivation.cs").read_text(encoding="utf-8")
        fixture_tool = (ROOT / "tests/GoymenERP.C01E2eFixtureTool/Program.cs").read_text(encoding="utf-8")
        compose_validator = (ROOT / "tools/validate_c01_e2e_contract.py").read_text(encoding="utf-8")

        # 1) API activation: project = goymen-c01-<runId>-<digits>.
        self.assertIn('$"goymen-c01-{runId}-"', activation)
        # 2) Fixture tool: same project prefix, database GoymenErpC01_<runId>_<attempt>.
        self.assertIn('$"goymen-c01-{runId}-"', fixture_tool)
        self.assertIn('$"GoymenErpC01_{runId}_{attempt}"', fixture_tool)
        # 3) Compose validator: database from the project's last two parts; run id = second to last.
        self.assertIn('expected_database = "GoymenErpC01_" + "_".join(expected_project.split("-")[-2:])', compose_validator)
        self.assertIn('expected_project.split("-")[-2]', compose_validator)

        prefix = f"goymen-c01-{run_id}-"
        self.assertTrue(project.startswith(prefix), project)
        self.assertTrue(project[len(prefix):].isdigit(), project)
        self.assertEqual(database, f"GoymenErpC01_{run_id}_{attempt}")
        self.assertEqual(database, "GoymenErpC01_" + "_".join(project.split("-")[-2:]))
        self.assertEqual(project.split("-")[-2], run_id)
        # 4) Fixture tool's safe-name pattern for the project.
        self.assertIsNotNone(re.fullmatch(r"[a-z0-9][a-z0-9_-]{0,62}", project))

    def test_start_failures_print_diagnostics_to_the_job_log_only(self):
        # The 479-byte artifact of run 3 could not explain an API that never listened. Diagnostics go
        # to the job log (masked), never into an uploaded artifact.
        for step in ("Build and start disposable SQL/API", "Build and start authorized and unauthorized web profiles"):
            with self.subTest(step=step):
                body = self.workflow.split(f"      - name: {step}\n", 1)[1].split("      - name:", 1)[0]
                self.assertIn("ps --all", body)
                self.assertIn("logs --no-color --tail", body)
                self.assertNotIn("artifacts/", body)

    def test_cleanup_runs_only_once_the_run_is_bound(self):
        # Before the bind step there is no stack, artifact root or project name; failing the
        # cleanup steps there only buries the real refusal (first run 35358106322).
        self.assertIn('echo "bound=true" >> "$GITHUB_OUTPUT"', self.workflow)
        for step in (
            "Capture disposable runtime state",
            "Enforce artifact privacy allowlist",
            "Remove disposable containers, network and volume",
        ):
            with self.subTest(step=step):
                body = self.workflow.split(f"      - name: {step}\n", 1)[1].split("      - name:", 1)[0]
                self.assertIn("if: always() && steps.bind.outputs.bound == 'true'", body)

    def test_job_names_are_quoted_where_they_contain_a_hash_prone_label(self):
        # An unquoted ' #' starts a YAML comment and silently cut the PR number off the job name.
        browser = self.workflow.split("\n  browser:\n", 1)[1].split("\n    needs:", 1)[0]
        self.assertIn('name: "Browser suite ${{ needs.resolve.outputs.suite }} on PR ${{ needs.resolve.outputs.pr }}"', browser)

    def test_read_only_verification_runs_only_for_declared_read_only_suites(self):
        case = self.workflow.split('case "$suite" in', 1)[1].split("esac", 1)[0]
        read_only_line = next(line for line in case.splitlines() if "read_only=true" in line)
        declared = set(read_only_line.split(")", 1)[0].strip().split("|"))
        self.assertEqual(declared, set(validator.READ_ONLY_SUITES))
        verify = self.workflow.split("      - name: Verify post-browser SQL remains read-only", 1)[1].split("      - name:", 1)[0]
        self.assertIn("if: needs.resolve.outputs.read_only == 'true'", verify)


if __name__ == "__main__":
    unittest.main()
