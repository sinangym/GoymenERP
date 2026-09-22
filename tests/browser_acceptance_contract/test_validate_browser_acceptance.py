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
            ),
            validator.SUITE_IDENTITY_PERMISSIONS["fin01"],
        )

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
