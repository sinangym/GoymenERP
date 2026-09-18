import pathlib
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


class WorkflowTests(unittest.TestCase):
    workflow = WORKFLOW.read_text(encoding="utf-8")

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

    def test_read_only_verification_runs_only_for_declared_read_only_suites(self):
        case = self.workflow.split('case "$suite" in', 1)[1].split("esac", 1)[0]
        read_only_line = next(line for line in case.splitlines() if "read_only=true" in line)
        declared = set(read_only_line.split(")", 1)[0].strip().split("|"))
        self.assertEqual(declared, set(validator.READ_ONLY_SUITES))
        verify = self.workflow.split("      - name: Verify post-browser SQL remains read-only", 1)[1].split("      - name:", 1)[0]
        self.assertIn("if: needs.resolve.outputs.read_only == 'true'", verify)


if __name__ == "__main__":
    unittest.main()
