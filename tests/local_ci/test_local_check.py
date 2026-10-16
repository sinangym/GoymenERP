from __future__ import annotations
import importlib.util
from pathlib import Path
import tempfile
import types
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("local_check", ROOT / "tools/ci/local_check.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class LocalCheckTests(unittest.TestCase):
    def test_all_canonical_full_jobs_remain_visible(self):
        inventory = module.full_inventory(ROOT)
        names = {(item["workflow"], item["job"]) for item in inventory}
        expected = {(relative, job) for relative in module.WORKFLOWS
                    for job in module.workflow(ROOT, relative)["jobs"]}
        self.assertEqual(expected, names)
        self.assertEqual(6, len(module.WORKFLOWS))
        self.assertIn((".github/workflows/browser-acceptance.yml", "browser"), names)
        self.assertIn((".github/workflows/openapi-generate.yml", "generate"), names)
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
        for sample in ("password=opaque value", "token:opaque value", "secret=opaque value",
                       "Authorization: Bearer opaque-token", "Proxy-Authorization: Basic opaque-token"):
            value, detected = module.scrub_output(sample)
            self.assertNotIn("opaque", value)
            self.assertTrue(detected)

    def test_full_run_cannot_report_pass_without_owned_adapters(self):
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as folder:
            argv = ["local_check", "--profile", "full", "--repository", str(ROOT), "--evidence-root", folder, "--expected-head", "a"*40, "--launcher-lease", "/unused"]
            with patch.object(module.sys, "argv", argv), patch.object(module, "validate_lease", return_value={}):
                code = module.main()
            result_path = next(Path(folder).glob("*/result.json"))
            import json
            result = json.loads(result_path.read_text(encoding="utf-8"))
        self.assertEqual(2, code)
        self.assertEqual("LOCAL_FULL_BLOCKED", result["status"])
        self.assertEqual([], result["steps"])
        self.assertFalse(result["githubJobStarted"])
        self.assertFalse(result["githubStorageUsed"])

    def test_p032_profile_is_heavy_and_launcher_transport_is_nested_fail_closed(self):
        self.assertIn(module.P032_BROWSER_PROFILE, module.HEAVY_PROFILES)
        launcher = (ROOT / "tools/ci/Invoke-LocalChecks.ps1").read_text(encoding="utf-8")
        for expected in (
            "'p032-u02-r14'",
            "Evidence manifest path is not a safe relative path.",
            "Evidence manifest path is duplicate or traversing.",
            "Evidence parent must be a regular directory.",
            "Linux evidence hash/size mismatch.",
            "Evidence manifest does not contain result.json.",
        ):
            self.assertIn(expected, launcher)

    def test_nested_evidence_manifest_hashes_regular_files_and_rejects_symlinks(self):
        import hashlib
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder)
            nested = directory / "suites/u02/artifacts"
            nested.mkdir(parents=True)
            content = b"closed proof\n"
            (nested / "result.json").write_bytes(content)
            entries = module.evidence_manifest(directory)
            valid = {
                "path": "suites/u02/artifacts/result.json",
                "bytes": len(content),
                "sha256": hashlib.sha256(content).hexdigest(),
            }
            self.assertEqual([valid], entries)
            with self.assertRaisesRegex(ValueError, "Unsafe local evidence path"):
                module.validate_evidence_manifest(directory, [{**valid, "path": "../result.json"}])
            with self.assertRaisesRegex(ValueError, "Duplicate local evidence path"):
                module.validate_evidence_manifest(directory, [valid, valid])
            with self.assertRaisesRegex(ValueError, "hash/size mismatch"):
                module.validate_evidence_manifest(directory, [{**valid, "sha256": "0" * 64}])
            target = directory / "target.txt"
            target.write_text("target\n", encoding="utf-8")
            (nested / "link.txt").symlink_to(target)
            with self.assertRaisesRegex(ValueError, "Symlink evidence rejected"):
                module.evidence_manifest(directory)

    def test_p032_dispatches_only_the_owned_adapter_and_remains_targeted(self):
        from contextlib import nullcontext
        import json
        from unittest.mock import Mock, patch
        state = {"commit": "a" * 40, "tree": "b" * 40, "dirty": False, "files": []}
        entry = {
            "name": "P03.2 fixed U02/R14 disposable browser acceptance",
            "exitCode": 0,
            "status": "PASS",
            "counts": {"total": 2, "passed": 2, "failed": 0, "errors": 0, "skipped": 0},
        }
        adapter = types.SimpleNamespace(PROFILE=module.P032_BROWSER_PROFILE, run_acceptance=Mock(return_value=entry))
        with tempfile.TemporaryDirectory() as folder:
            argv = [
                "local_check", "--profile", module.P032_BROWSER_PROFILE,
                "--repository", str(ROOT), "--evidence-root", folder,
                "--expected-head", "a" * 40, "--launcher-lease", "/unused",
            ]
            with (patch.object(module.sys, "argv", argv),
                  patch.object(module, "validate_lease", return_value={}),
                  patch.object(module, "source_state", return_value=state),
                  patch.object(module, "full_inventory", return_value=[]),
                  patch.object(module, "load_browser_adapter", return_value=adapter),
                  patch.object(module, "heavy_lock", return_value=nullcontext())):
                code = module.main()
            result = json.loads(next(Path(folder).glob("*/result.json")).read_text(encoding="utf-8"))
        self.assertEqual(0, code)
        self.assertEqual("LOCAL_TARGETED", result["kind"])
        self.assertEqual("LOCAL_TARGETED_PASS", result["status"])
        self.assertEqual([entry], result["steps"])
        adapter.run_acceptance.assert_called_once()

    def test_failed_canonical_check_keeps_its_exit_code(self):
        from unittest.mock import patch
        failure = {"name": "canonical guard", "exitCode": 7, "status": "FAIL", "counts": None}
        with tempfile.TemporaryDirectory() as folder:
            argv = ["local_check", "--profile", "guards", "--repository", str(ROOT), "--evidence-root", folder, "--expected-head", "a"*40, "--launcher-lease", "/unused"]
            with patch.object(module.sys, "argv", argv), patch.object(module, "validate_lease", return_value={}), patch.object(module, "run_step", return_value=failure):
                code = module.main()
            import json
            result = json.loads(next(Path(folder).glob("*/result.json")).read_text(encoding="utf-8"))
        self.assertEqual(7, code)
        self.assertEqual("LOCAL_TARGETED_FAIL", result["status"])
        self.assertEqual(7, result["steps"][0]["exitCode"])
    def test_shell_hooks_and_cloud_credentials_are_not_inherited(self):
        from unittest.mock import patch
        poisons = {"BASH_ENV": "/tmp/untrusted", "ENV": "/tmp/untrusted",
                   "BASH_FUNC_npm%%": "() { return 0; }", "AWS_SECRET_ACCESS_KEY": "opaque",
                   "AZURE_CLIENT_SECRET": "opaque", "NUGET_CREDENTIALPROVIDERS_PATH": "/tmp/untrusted",
                   "NODE_OPTIONS": "--require=/tmp/untrusted"}
        with patch.dict(module.os.environ, poisons):
            environment = module.command_environment()
        for key in poisons:
            if key == "NODE_OPTIONS":
                self.assertNotIn("require", environment[key])
            else:
                self.assertNotIn(key, environment)

    def test_actual_shell_ignores_bash_env_forged_success(self):
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder)
            hook = directory / "untrusted.sh"
            hook.write_text('python3() { echo FORGED_SUCCESS; }; export -f python3\n', encoding="utf-8")
            with patch.dict(module.os.environ, {"BASH_ENV": str(hook)}):
                entry = module.run_step(ROOT, directory, 1, {"run": "python3 -c 'print(123456789)'"})
            log = (directory / "step-01.log").read_text(encoding="utf-8")
        self.assertEqual(0, entry["exitCode"])
        self.assertIn("123456789", log)
        self.assertNotIn("FORGED_SUCCESS", log)

    def test_multiline_credentials_never_enter_closed_evidence(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder)
            command = "printf 'Authorization:\\n Bearer opaque-token\\nsecond-opaque-fragment\\n'"
            entry = module.run_step(ROOT, directory, 1, {"run": command})
            log = (directory / "step-01.log").read_text(encoding="utf-8")
        self.assertNotIn("opaque-token", log)
        self.assertNotIn("second-opaque-fragment", log)
        self.assertEqual("BLOCKED_SECRET_OUTPUT", entry["status"])
        self.assertEqual(2, entry["exitCode"])

    def test_missing_or_wrong_launcher_lease_is_rejected(self):
        with self.assertRaises(ValueError):
            module.validate_lease(ROOT, "frontend", Path("/tmp/untrusted.json"), "a"*40)
        with self.assertRaises(ValueError):
            module.validate_lease(ROOT, "frontend", Path("/tmp/untrusted.json"), "wrong")

    def test_prefixed_credentials_are_redacted_completely(self):
        for name in ("GITHUB_TOKEN", "AWS_SECRET_ACCESS_KEY", "AWS_ACCESS_KEY_ID",
                     "AZURE_CLIENT_SECRET", "NPM_TOKEN", "PRIVATE_KEY", "ConnectionStrings__Default"):
            output, detected = module.scrub_output(name + "=opaque-secret-value\n")
            self.assertTrue(detected, name)
            self.assertEqual("[CREDENTIAL_REDACTED]\n", output)

    def test_decode_error_reaps_owned_child_before_return(self):
        from unittest.mock import patch
        children = []
        original = module.subprocess.Popen
        def capture(*args, **kwargs):
            child = original(*args, **kwargs)
            children.append(child)
            return child
        try:
            with tempfile.TemporaryDirectory() as folder, patch.object(module.subprocess, "Popen", side_effect=capture):
                with self.assertRaises(UnicodeDecodeError):
                    module.run_step(ROOT, Path(folder), 1, {"run":
                        "python3 -c \"import os,time; os.write(1, bytes([255,10])); time.sleep(0.2)\""})
                self.assertIsNotNone(children[0].poll())
        finally:
            for child in children:
                child.wait(timeout=5)

    def test_log_error_reaps_owned_child_before_return(self):
        from unittest.mock import patch
        children = []
        original_popen = module.subprocess.Popen
        original_open = Path.open
        def capture(*args, **kwargs):
            child = original_popen(*args, **kwargs)
            children.append(child)
            return child
        def fail_log(path, *args, **kwargs):
            if path.name == "step-01.log":
                raise OSError("synthetic log writer unavailable")
            return original_open(path, *args, **kwargs)
        try:
            with tempfile.TemporaryDirectory() as folder, patch.object(module.subprocess, "Popen", side_effect=capture), patch.object(Path, "open", fail_log):
                with self.assertRaises(OSError):
                    module.run_step(ROOT, Path(folder), 1, {"run": "python3 -c 'import time; time.sleep(0.2)'"})
                self.assertIsNotNone(children[0].poll())
        finally:
            for child in children:
                child.wait(timeout=5)

    def test_secret_blocked_result_is_not_reclassified(self):
        from unittest.mock import patch
        import json
        entry = {"name": "redacted", "exitCode": 2, "status": "BLOCKED_SECRET_OUTPUT", "counts": None}
        with tempfile.TemporaryDirectory() as folder:
            argv = ["local_check", "--profile", "guards", "--repository", str(ROOT), "--evidence-root", folder,
                    "--expected-head", "a"*40, "--launcher-lease", "/unused"]
            with patch.object(module.sys, "argv", argv), patch.object(module, "validate_lease", return_value={}), patch.object(module, "run_step", return_value=entry):
                code = module.main()
            result = json.loads(next(Path(folder).glob("*/result.json")).read_text(encoding="utf-8"))
        self.assertEqual(2, code)
        self.assertEqual("BLOCKED_SECRET_OUTPUT", result["status"])

    def test_monotonic_evidence_preserves_step_order_when_utc_moves_backwards(self):
        from datetime import datetime, timedelta
        import json
        from unittest.mock import patch
        times = [
            "2026-10-16T14:23:36.436824+00:00",
            "2026-10-16T14:23:36.526604+00:00",
            "2026-10-16T14:23:49.631161+00:00",
            "2026-10-16T14:23:37.671100+00:00",
        ]
        state = {"commit": "a" * 40, "tree": "b" * 40, "dirty": False, "files": []}
        with tempfile.TemporaryDirectory() as folder:
            argv = ["local_check", "--profile", "guards", "--repository", str(ROOT),
                    "--evidence-root", folder, "--expected-head", "a" * 40,
                    "--launcher-lease", "/unused"]
            with (patch.object(module.sys, "argv", argv),
                  patch.object(module, "validate_lease", return_value={}),
                  patch.object(module, "source_state", return_value=state),
                  patch.object(module, "full_inventory", return_value=[]),
                  patch.object(module, "selected_steps", return_value=[{"name": "clock fixture", "run": "true"}]),
                  patch.object(module, "now", side_effect=times),
                  patch.object(module, "monotonic_ns", side_effect=[100, 200, 300, 400], create=True)):
                code = module.main()
            directory = next(Path(folder).glob("local-*"))
            result = json.loads((directory / "result.json").read_text(encoding="utf-8"))
            persisted_step = json.loads((directory / "step-01.json").read_text(encoding="utf-8"))
        self.assertEqual(0, code)
        self.assertEqual("LOCAL_TARGETED_PASS", result["status"])
        step = result["steps"][0]
        self.assertLess(datetime.fromisoformat(result["finishUtc"]), datetime.fromisoformat(step["finishUtc"]))
        self.assertEqual([100, 200, 300, 400], [result["startMonotonicNs"], step["startMonotonicNs"],
                                              step["finishMonotonicNs"], result["finishMonotonicNs"]])
        for entry in (result, step):
            for key in ("startMonotonicNs", "finishMonotonicNs"):
                self.assertIs(type(entry[key]), int)
            self.assertLessEqual(entry["startMonotonicNs"], entry["finishMonotonicNs"])
            for key in ("startUtc", "finishUtc"):
                self.assertEqual(timedelta(0), datetime.fromisoformat(entry[key]).utcoffset())
        for key in ("startUtc", "finishUtc", "startMonotonicNs", "finishMonotonicNs"):
            self.assertEqual(step[key], persisted_step[key])

    def test_unknown_profile_is_rejected(self):
        with self.assertRaises(ValueError):
            module.selected_steps(ROOT, "hosted")


if __name__ == "__main__":
    unittest.main()
