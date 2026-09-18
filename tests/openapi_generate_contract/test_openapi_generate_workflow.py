import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "openapi-generate.yml"


class OpenApiGenerateWorkflowTests(unittest.TestCase):
    workflow = WORKFLOW.read_text(encoding="utf-8")

    def _trigger_block(self) -> str:
        return self.workflow.split("\non:\n", 1)[1].split("\npermissions:\n", 1)[0]

    def _step(self, name: str) -> str:
        return self.workflow.split(f"      - name: {name}\n", 1)[1].split("      - name:", 1)[0]

    def test_it_is_selectable_only_and_never_runs_on_push(self):
        trigger = self._trigger_block()
        self.assertIn("workflow_dispatch:", trigger)
        self.assertIn("pull_request:\n    types:\n      - labeled\n", trigger)
        for forbidden in ("push:", "synchronize", "schedule:", "opened"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, trigger)
        self.assertIn("github.event.label.name == 'openapi:generate'", self.workflow)
        self.assertNotIn("pull_request_target", self.workflow)

    def test_it_has_no_secrets_and_read_only_permissions(self):
        self.assertNotIn("secrets.", self.workflow)
        permissions = self.workflow.split("\npermissions:\n", 1)[1].split("\n\n", 1)[0]
        self.assertEqual([line.strip() for line in permissions.strip().splitlines()],
                         ["contents: read", "pull-requests: read"])
        self.assertIn("persist-credentials: false", self.workflow)

    def test_exact_head_same_repo_and_base_ancestry(self):
        for required in (
            "OPENAPI_FORK_REFUSED",
            "OPENAPI_PR_NOT_OPEN",
            "OPENAPI_BASE_NOT_MAIN",
            "OPENAPI_HEAD_MOVED",
            "ref: ${{ needs.resolve.outputs.head_sha }}",
            'test "$(git rev-parse HEAD)" = "$EXPECTED_HEAD_SHA"',
            'git merge-base --is-ancestor "$EXPECTED_BASE_SHA" HEAD',
        ):
            with self.subTest(required=required):
                self.assertIn(required, self.workflow)

    def test_measured_limits(self):
        self.assertIn("timeout-minutes: 20", self.workflow)
        self.assertIn("timeout-minutes: 5", self.workflow)
        self.assertIn("cancel-in-progress: true", self.workflow)
        self.assertIn("group: openapi-generate-${{ inputs.pr || github.event.pull_request.number }}", self.workflow)

    def test_it_never_writes_the_contract(self):
        # The workflow is a helper; the committed contract is changed only by a human/agent commit
        # of the downloaded bytes, and the drift job stays the gate.
        self.assertNotRegex(
            self.workflow,
            r"(?m)\b(?:cp|mv|install|tee|rsync)\b[^\n]*contracts/openapi|>\s*contracts/openapi|\bof=\s*contracts/openapi",
        )
        report = self._step("Report the generated document")
        self.assertIn("sha256sum contracts/openapi/goymen-erp-v1.json", report)
        self.assertNotIn("contracts/openapi/goymen-erp-v1.json >", report)

    def test_upload_is_exactly_one_file_named_by_the_exact_head(self):
        uploads = re.findall(r"uses: actions/upload-artifact@", self.workflow)
        self.assertEqual(len(uploads), 1)
        upload = self._step("Upload the generated document")
        self.assertIn("name: openapi-${{ needs.resolve.outputs.head_sha }}", upload)
        self.assertIn("path: artifacts/openapi.json\n", upload)
        self.assertNotIn("path: |", upload)
        self.assertIn("if-no-files-found: error", upload)
        self.assertRegex(upload, r"(?m)^          retention-days: 3$")

    def test_generation_matches_the_drift_jobs_build_inputs(self):
        # Same bootstrap preflight, same restore flags and configuration as ci.yml's drift job, so
        # the artifact is the file that job compares.
        self.assertGreaterEqual(self.workflow.count("python3 -I -S -B tools/validate_dotnet_bootstrap_integrity.py"), 3)
        restore = self._step("Restore API")
        self.assertIn("-noAutoResponse", restore)
        self.assertIn("--configfile NuGet.Config", restore)
        build = self._step("Build API and emit the OpenAPI document")
        self.assertIn("--configuration Release", build)
        self.assertIn("--no-restore", build)
        self.assertIn("src/backend/GoymenERP.Api/GoymenERP.Api.csproj", build)

    def test_job_name_is_quoted(self):
        self.assertIn('name: "Generate OpenAPI for PR ${{ needs.resolve.outputs.pr }}"', self.workflow)


if __name__ == "__main__":
    unittest.main()
