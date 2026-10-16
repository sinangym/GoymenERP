from __future__ import annotations
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("local_check", ROOT / "tools/ci/local_check.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class LocalCheckTests(unittest.TestCase):
    def test_all_canonical_full_jobs_remain_visible(self):
        inventory = module.full_inventory(ROOT)
        names = {(item["workflow"], item["job"]) for item in inventory}
        self.assertEqual(14, len(names))
        for job in ("backend-build", "backend-test-group", "backend", "recovery-surface-inventory",
                    "release-manifest", "frontend", "relational-shard", "relational-rcsi",
                    "relational", "contract-drift", "containers"):
            self.assertIn((module.WORKFLOWS[0], job), names)
        self.assertTrue(all(item["status"] == "NOT_RUN" for item in inventory))
        self.assertTrue(any(item["services"] for item in inventory))

    def test_guard_commands_are_read_from_canonical_source(self):
        step = module.selected_steps(ROOT, "guards")[0]
        self.assertIn("tools/validate_dotnet_bootstrap_integrity.py", step["run"])
        self.assertIn("tests/ci_safe_trx_summary", step["run"])
        self.assertNotIn("continue-on-error", step)

    def test_frontend_retains_every_canonical_run(self):
        steps = module.selected_steps(ROOT, "frontend")
        self.assertEqual(["npm ci", "npm audit --audit-level=high", "npm run lint",
                          "npm test -- --maxWorkers=1", "npm run build"], [item["run"] for item in steps])

    def test_catalog_retains_negative_tests_and_golden_validation(self):
        steps = module.selected_steps(ROOT, "catalog")
        self.assertEqual(2, len(steps))
        self.assertIn("tests/ui_catalog_validator", steps[0]["run"])
        self.assertIn("tools/validate_ui_catalog.py", steps[1]["run"])

    def test_python_failure_error_skip_counts_are_not_passed(self):
        counts = module.test_counts("Ran 46 tests in 1.0s\nFAILED (failures=3, errors=1, skipped=2)\n")
        self.assertEqual({"total": 46, "passed": 40, "failed": 3, "errors": 1, "skipped": 2}, counts)

    def test_missing_counts_remain_unknown(self):
        self.assertIsNone(module.test_counts("process exited before test discovery"))

    def test_vitest_counts_keep_skips(self):
        counts = module.test_counts(" Tests  20 passed | 1 skipped (21)\n")
        self.assertEqual(21, counts["total"])
        self.assertEqual(1, counts["skipped"])
        self.assertEqual(20, counts["passed"])

    def test_credentials_and_connections_are_not_inherited(self):
        from unittest.mock import patch
        with patch.dict(module.os.environ, {"GH_TOKEN": "hidden", "GOYMEN_ERP_TEST_SQLSERVER_CONNECTION": "hidden"}):
            environment = module.command_environment()
        self.assertNotIn("GH_TOKEN", environment)
        self.assertNotIn("GOYMEN_ERP_TEST_SQLSERVER_CONNECTION", environment)
        self.assertEqual("true", environment["CI"])

    def test_command_log_redacts_common_credential_forms(self):
        value = module.SENSITIVE.sub(r"\1[REDACTED]", "password=opaque token:opaque secret=opaque")
        self.assertNotIn("opaque", value)

    def test_full_run_cannot_report_pass_without_owned_adapters(self):
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as folder:
            argv = ["local_check", "--profile", "full", "--repository", str(ROOT), "--evidence-root", folder]
            with patch.object(module.sys, "argv", argv):
                code = module.main()
            result_path = next(Path(folder).glob("*/result.json"))
            import json
            result = json.loads(result_path.read_text(encoding="utf-8"))
        self.assertEqual(2, code)
        self.assertEqual("LOCAL_FULL_BLOCKED", result["status"])
        self.assertEqual([], result["steps"])
        self.assertFalse(result["githubJobStarted"])
        self.assertFalse(result["githubStorageUsed"])

    def test_failed_canonical_check_keeps_its_exit_code(self):
        from unittest.mock import patch
        failure = {"name": "canonical guard", "exitCode": 7, "status": "FAIL", "counts": None}
        with tempfile.TemporaryDirectory() as folder:
            argv = ["local_check", "--profile", "guards", "--repository", str(ROOT), "--evidence-root", folder]
            with patch.object(module.sys, "argv", argv), patch.object(module, "run_step", return_value=failure):
                code = module.main()
            import json
            result = json.loads(next(Path(folder).glob("*/result.json")).read_text(encoding="utf-8"))
        self.assertEqual(7, code)
        self.assertEqual("LOCAL_TARGETED_FAIL", result["status"])
        self.assertEqual(7, result["steps"][0]["exitCode"])
    def test_unknown_profile_is_rejected(self):
        with self.assertRaises(ValueError):
            module.selected_steps(ROOT, "hosted")


if __name__ == "__main__":
    unittest.main()
