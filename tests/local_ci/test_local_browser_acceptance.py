from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "local_browser_acceptance",
    ROOT / "tools/ci/local_browser_acceptance.py",
)
module = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
import sys
sys.modules[SPEC.name] = module
SPEC.loader.exec_module(module)
HEAD = "a" * 40


def binding(suite: str = "u02", run_id: str = "123456789"):
    return module.new_binding(suite, 1, run_id=run_id)


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value) + "\n", encoding="utf-8")


def materialize_artifacts(root: Path, item, *, suite: str = "u02", outcome: str = "pass") -> None:
    write_json(root / "browser-fixture-evidence.json", {
        "schemaVersion": 1,
        "suiteId": "C01-E2E-INFRASTRUCTURE-V1",
        "testedCommit": HEAD,
        "runId": item.run_id,
        "composeProject": item.compose_project,
    })
    write_json(root / "browser-suite-fixture-evidence.json", {
        "schemaVersion": 1,
        "suiteId": "BROWSER-SUITE-FIXTURE-V1",
        "suite": suite,
        "testedCommit": HEAD,
        "runId": item.run_id,
        "composeProject": item.compose_project,
        "requests": 2,
        "auditEventsBefore": 1,
        "auditEventsAfter": 2,
        "commandExecutionsBefore": 3,
        "commandExecutionsAfter": 4,
    })
    write_json(root / "browser-suite-seed.json", {"companyId": "10000000-0000-0000-0000-000000000001"})
    outcome_xml = "" if outcome == "pass" else f"<{outcome}>unsafe</{outcome}>"
    (root / f"browser-{suite}-junit.xml").write_text(
        f'<testsuites><testsuite><testcase classname="suite" name="proof">{outcome_xml}</testcase></testsuite></testsuites>',
        encoding="utf-8",
    )
    logs = root / "container-logs"
    logs.mkdir(parents=True, exist_ok=True)
    (logs / "compose-ps.txt").write_text("NAME STATUS\n", encoding="utf-8")


class FakeSourceContract:
    PROFILE_COMPOSE_FILES = {"normal": "infra/docker-compose.browser-normal.yml"}

    @staticmethod
    def profile_of(suite):
        return "normal"

    @staticmethod
    def suite_override_file(suite):
        return f"infra/browser-suites/{suite}.override.yml"

    @staticmethod
    def has_fixture(suite):
        return True


class LocalBrowserAcceptanceTests(unittest.TestCase):
    def test_fixed_profile_is_exactly_u02_then_r14(self):
        self.assertEqual("p032-u02-r14", module.PROFILE)
        self.assertEqual(("u02", "r14"), module.SUITES)
        self.assertEqual({"normal"}, {item["capabilityProfile"] for item in module.SUITE_CONTRACTS.values()})

    def test_real_source_contract_resolves_owned_normal_fixtures_and_scripts(self):
        contract = module.load_browser_contract(ROOT)
        result = module.validate_source_contract(ROOT, contract)
        self.assertEqual(["u02", "r14"], [item["suite"] for item in result])
        self.assertEqual(
            ["browser:u02:ci", "browser:r14:ci"],
            [item["script"] for item in result],
        )
        self.assertTrue(all(item["fixture"] for item in result))

    def test_retry_or_script_drift_is_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "tests/e2e").mkdir(parents=True)
            package = {
                "scripts": {
                    item["script"]: item["scriptValue"]
                    for item in module.SUITE_CONTRACTS.values()
                }
            }
            write_json(root / "tests/e2e/package.json", package)
            for item in module.SUITE_CONTRACTS.values():
                path = root / item["config"]
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(
                    "export default {"
                    f"testDir: '{item['testDirectory']}', workers: 1, retries: 1, forbidOnly: true, "
                    "trace: 'off', screenshot: 'off', video: 'off', BROWSER_E2E_JUNIT_FILE: true}\n",
                    encoding="utf-8",
                )
            with self.assertRaisesRegex(module.AcceptanceError, "retries=0"):
                module.validate_source_contract(root, FakeSourceContract())

    def test_each_suite_gets_a_distinct_disposable_binding_and_secrets_are_not_public(self):
        u02 = binding("u02", "123456789")
        r14 = binding("r14", "123456790")
        self.assertNotEqual(u02.compose_project, r14.compose_project)
        self.assertNotEqual(u02.database, r14.database)
        self.assertEqual("goymen-c01-123456789-1", u02.compose_project)
        public = json.dumps(u02.public())
        for secret in u02.secret_values():
            self.assertNotIn(secret, public)
        with self.assertRaises(module.AcceptanceError):
            module.new_binding("smoke", 1, run_id="123456789")

    def test_mandatory_nested_artifacts_and_fixture_head_are_verified(self):
        item = binding()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            materialize_artifacts(root, item)
            entries, junit = module.validate_artifacts(root, "u02", item, HEAD)
        self.assertEqual(
            [name.format(suite="u02") for name in module.MANDATORY_ARTIFACTS],
            [entry["path"] for entry in entries],
        )
        self.assertEqual(1, junit["total"])
        self.assertEqual(0, junit["retries"])

    def test_missing_symlink_and_wrong_head_artifacts_fail_closed(self):
        item = binding()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            materialize_artifacts(root, item)
            (root / "browser-suite-seed.json").unlink()
            with self.assertRaisesRegex(module.AcceptanceError, "missing"):
                module.validate_artifacts(root, "u02", item, HEAD)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            materialize_artifacts(root, item)
            target = root / "seed-target.json"
            target.write_text("{}\n", encoding="utf-8")
            (root / "browser-suite-seed.json").unlink()
            (root / "browser-suite-seed.json").symlink_to(target)
            with self.assertRaisesRegex(module.AcceptanceError, "symlink"):
                module.validate_artifacts(root, "u02", item, HEAD)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            materialize_artifacts(root, item)
            value = json.loads((root / "browser-suite-fixture-evidence.json").read_text(encoding="utf-8"))
            value["testedCommit"] = "b" * 40
            write_json(root / "browser-suite-fixture-evidence.json", value)
            with self.assertRaisesRegex(module.AcceptanceError, "testedCommit"):
                module.validate_artifacts(root, "u02", item, HEAD)

    def test_junit_failure_error_skip_and_unsafe_xml_are_rejected(self):
        for outcome in ("failure", "error", "skipped"):
            with self.subTest(outcome=outcome), tempfile.TemporaryDirectory() as folder:
                path = Path(folder) / "result.xml"
                path.write_text(
                    f"<testsuite><testcase name='x'><{outcome}/></testcase></testsuite>",
                    encoding="utf-8",
                )
                with self.assertRaisesRegex(module.AcceptanceError, "failure, error or skipped"):
                    module.validate_junit(path)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "result.xml"
            path.write_text("<!DOCTYPE x [<!ENTITY y 'z'>]><testsuite/>", encoding="utf-8")
            with self.assertRaisesRegex(module.AcceptanceError, "unsafe XML"):
                module.validate_junit(path)

    def test_junit_duplicate_retry_attempt_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "result.xml"
            path.write_text(
                "<testsuite>"
                "<testcase classname='suite' name='proof' file='proof.spec.ts'/>"
                "<testcase classname='suite' name='proof' file='proof.spec.ts'/>"
                "</testsuite>",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(module.AcceptanceError, "duplicate/retried"):
                module.validate_junit(path)

    def test_privacy_runs_before_any_artifact_is_transported(self):
        item = binding()
        calls = []

        class Contract:
            @staticmethod
            def validate_privacy(source, suite, forbidden, compose):
                calls.append("privacy")
                self.assertFalse((destination / "browser-u02-junit.xml").exists())
                self.assertEqual(item.secret_values(), forbidden)

        with tempfile.TemporaryDirectory() as source_folder, tempfile.TemporaryDirectory() as destination_folder:
            source = Path(source_folder)
            destination = Path(destination_folder)
            materialize_artifacts(source, item)
            entries, _ = module.publish_artifacts(
                source,
                destination,
                suite="u02",
                binding=item,
                expected_head=HEAD,
                contract=Contract(),
                compose_model={"services": {"api": {"environment": {}}}},
            )
            calls.append("transport")
            self.assertTrue((destination / "container-logs/compose-ps.txt").is_file())
        self.assertEqual(["privacy", "transport"], calls)
        self.assertEqual(5, len(entries))

    def test_credential_shaped_output_is_never_persisted(self):
        safe, detected = module._safe_output("Password=opaque-value\nnext\n", ())
        self.assertTrue(detected)
        self.assertEqual("[CREDENTIAL_REDACTED]\n", safe)
        self.assertNotIn("opaque", safe)

    def test_source_head_and_dirty_state_are_fail_closed(self):
        with patch.object(module, "git", side_effect=(str(ROOT), HEAD, "c" * 40, "")):
            self.assertEqual(HEAD, module.source_binding(ROOT, HEAD)["commit"])
        with patch.object(module, "git", side_effect=(str(ROOT), "b" * 40, "c" * 40, "")):
            with self.assertRaisesRegex(module.AcceptanceError, "moved"):
                module.source_binding(ROOT, HEAD)
        with patch.object(module, "git", side_effect=(str(ROOT), HEAD, "c" * 40, "M source")):
            with self.assertRaisesRegex(module.AcceptanceError, "clean exact source"):
                module.source_binding(ROOT, HEAD)


class FakeRunner:
    def __init__(self, cleanup_code: int):
        self.cleanup_code = cleanup_code
        self.records = []

    def capture(self, arguments, **kwargs):
        if "config" in arguments:
            return json.dumps({"services": {"api": {"environment": {}}}})
        return ""

    def run(self, arguments, **kwargs):
        environment = kwargs["environment"]
        if "GoymenERP.C01E2eFixtureTool.csproj" in arguments and "run" in arguments:
            item = {
                "schemaVersion": 1,
                "suiteId": "C01-E2E-INFRASTRUCTURE-V1",
                "testedCommit": HEAD,
                "runId": environment["GOYMEN_ERP_C01_E2E_RUN_ID"],
                "composeProject": environment["GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT"],
            }
            write_json(Path(environment["GOYMEN_ERP_C01_E2E_FIXTURE_EVIDENCE"]), item)
        if "GoymenERP.BrowserSuiteFixtureTool.csproj" in arguments and "run" in arguments:
            suite = environment["BROWSER_SUITE"]
            item = {
                "schemaVersion": 1,
                "suiteId": "BROWSER-SUITE-FIXTURE-V1",
                "suite": suite,
                "testedCommit": HEAD,
                "runId": environment["GOYMEN_ERP_C01_E2E_RUN_ID"],
                "composeProject": environment["GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT"],
                "requests": 1,
                "auditEventsBefore": 0,
                "auditEventsAfter": 1,
                "commandExecutionsBefore": 0,
                "commandExecutionsAfter": 1,
            }
            write_json(Path(environment["BROWSER_E2E_SUITE_FIXTURE_EVIDENCE"]), item)
            write_json(Path(environment["BROWSER_E2E_SUITE_SEED_FILE"]), {"companyId": "id"})
        if arguments[:2] == ["npm", "run"]:
            Path(environment["BROWSER_E2E_JUNIT_FILE"]).write_text(
                "<testsuite><testcase classname='x' name='y'/></testsuite>",
                encoding="utf-8",
            )

    def run_unchecked(self, arguments, **kwargs):
        return self.cleanup_code

    def wait_http(self, *args, **kwargs):
        return None

    def _execute(self, arguments, **kwargs):
        return 0, "NAME STATUS\n"


class FakeRuntimeContract:
    @staticmethod
    def validate_suite_compose(model, suite, expected_head, project):
        return None

    @staticmethod
    def api_environment(model, suite, expected_head, project):
        return {}

    @staticmethod
    def seed_export(suite, seed):
        return f"BROWSER_E2E_{suite.upper()}_SEED={json.dumps(seed)}"

    @staticmethod
    def validate_privacy(root, suite, forbidden, model):
        return None


class LocalBrowserCleanupTests(unittest.TestCase):
    def test_cleanup_failure_prevents_a_browser_pass(self):
        item = binding()
        runner = FakeRunner(cleanup_code=1)
        with tempfile.TemporaryDirectory() as root_folder, tempfile.TemporaryDirectory() as evidence_folder:
            result = module._run_suite(
                Path(root_folder),
                Path(evidence_folder),
                HEAD,
                item,
                runner,
                FakeRuntimeContract(),
                lambda: None,
            )
        self.assertEqual("FAIL", result["status"])
        self.assertEqual(1, result["exitCode"])
        self.assertEqual(1, result["cleanup"]["downExitCode"])
        self.assertTrue(any("cleanup" in error.lower() for error in result["errors"]))


if __name__ == "__main__":
    unittest.main()
