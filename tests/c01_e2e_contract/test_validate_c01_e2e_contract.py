import copy
import pathlib
import tempfile
import unittest

import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))

import validate_c01_e2e_contract as validator  # noqa: E402


COMMIT = "a" * 40
PROJECT = "goymen-c01-100-1"


def valid_compose():
    port = lambda target, published: {
        "host_ip": "127.0.0.1",
        "target": target,
        "published": str(published),
        "protocol": "tcp",
    }
    labels = {
        "com.goymen.e2e.suite": "C01-REAL-BROWSER-E2E-V1",
        "com.goymen.e2e.tested-commit": COMMIT,
    }
    web = lambda identity, published: {
        "build": {
            "context": str(ROOT),
            "dockerfile": "src/frontend/goymen-erp-web/Dockerfile",
            "args": {
                "VITE_BOOTSTRAP_IDENTITY_ENABLED": "true",
                "VITE_DEV_USER": identity,
            },
            "labels": {"org.opencontainers.image.revision": COMMIT},
        },
        "ports": [port(80, published)],
        "labels": labels,
    }
    return {
        "name": PROJECT,
        "services": {
            "sqlserver": {
                "image": validator.SQLSERVER_IMAGE,
                "healthcheck": {"test": ["CMD-SHELL", "SELECT 1"]},
                "ports": [port(1433, 11433)],
                "labels": labels,
            },
            "api": {
                "build": {
                    "context": str(ROOT),
                    "dockerfile": "src/backend/GoymenERP.Api/Dockerfile",
                    "labels": {"org.opencontainers.image.revision": COMMIT}
                },
                "environment": {
                    "ASPNETCORE_ENVIRONMENT": "Testing",
                    "DB_PROVIDER": "SqlServer",
                    "DB_CONNECTION_STRING": "Database=GoymenErpC01_100_1",
                    "Counterparty__CursorSigningKey": "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                    "C01E2e__EnableListCapability": "true",
                    "GOYMEN_ERP_C01_E2E_DISPOSABLE": "CONFIRMED",
                    "GOYMEN_ERP_C01_E2E_TESTED_COMMIT": COMMIT,
                    "GOYMEN_ERP_C01_E2E_RUN_ID": "100",
                    "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT": PROJECT,
                    "BootstrapIdentity__HeaderName": "X-Development-User",
                    "BootstrapIdentity__Users__0__UserId": "group-bootstrap-admin",
                    "BootstrapIdentity__Users__0__DisplayName": "C01 E2E Group Administrator",
                    "BootstrapIdentity__Users__1__UserId": "company-a-user",
                    "BootstrapIdentity__Users__1__DisplayName": "C01 E2E Unauthorized User",
                },
                "ports": [port(8080, 15080)],
                "labels": labels,
            },
            "web-authorized": web("group-bootstrap-admin", 15173),
            "web-unauthorized": web("company-a-user", 15174),
        },
        "volumes": {"volume": {"labels": {"com.goymen.e2e.tested-commit": COMMIT}}},
    }


class ComposeContractTests(unittest.TestCase):
    def test_valid_model_passes(self):
        validator.validate_compose(valid_compose(), COMMIT, PROJECT)

    def test_public_port_fails(self):
        model = valid_compose()
        model["services"]["api"]["ports"][0]["host_ip"] = "0.0.0.0"
        with self.assertRaisesRegex(validator.ContractError, "loopback"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_mutable_sql_image_fails(self):
        model = valid_compose()
        model["services"]["sqlserver"]["image"] = (
            "mcr.microsoft.com/mssql/server:2022-latest"
        )
        with self.assertRaisesRegex(validator.ContractError, "SQL image digest"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_identity_drift_fails(self):
        model = valid_compose()
        model["services"]["web-authorized"]["build"]["args"]["VITE_DEV_USER"] = "company-a-user"
        with self.assertRaisesRegex(validator.ContractError, "identity"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_build_provenance_drift_fails(self):
        mutations = (
            ("api", "context", str(ROOT / "stale-tree"), "API build context"),
            ("api", "dockerfile", "stale/Dockerfile", "API Dockerfile"),
            (
                "web-authorized",
                "context",
                str(ROOT / "stale-tree"),
                "web-authorized build context",
            ),
            (
                "web-unauthorized",
                "dockerfile",
                "stale/Dockerfile",
                "web-unauthorized Dockerfile",
            ),
        )
        for service, key, value, message in mutations:
            with self.subTest(service=service, key=key):
                model = valid_compose()
                model["services"][service]["build"][key] = value
                with self.assertRaisesRegex(validator.ContractError, message):
                    validator.validate_compose(model, COMMIT, PROJECT)

    def test_development_environment_fails(self):
        model = valid_compose()
        model["services"]["api"]["environment"]["ASPNETCORE_ENVIRONMENT"] = "Development"
        with self.assertRaisesRegex(validator.ContractError, "environment"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_missing_disposable_flag_fails(self):
        model = valid_compose()
        del model["services"]["api"]["environment"]["C01E2e__EnableListCapability"]
        with self.assertRaisesRegex(validator.ContractError, "capability flag"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_wrong_disposable_attestation_fails(self):
        model = valid_compose()
        model["services"]["api"]["environment"]["GOYMEN_ERP_C01_E2E_DISPOSABLE"] = "confirmed"
        with self.assertRaisesRegex(validator.ContractError, "attestation"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_source_sha_and_compose_binding_drift_fail(self):
        for key, value, message in (
            ("GOYMEN_ERP_C01_E2E_TESTED_COMMIT", "b" * 40, "source SHA"),
            ("GOYMEN_ERP_C01_E2E_RUN_ID", "101", "run id"),
            ("GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT", "goymen-c01-101-1", "compose project"),
        ):
            with self.subTest(key=key):
                model = valid_compose()
                model["services"]["api"]["environment"][key] = value
                with self.assertRaisesRegex(validator.ContractError, message):
                    validator.validate_compose(model, COMMIT, PROJECT)

    def test_non_run_scoped_database_fails(self):
        model = valid_compose()
        model["services"]["api"]["environment"]["DB_CONNECTION_STRING"] = (
            "Database=GoymenErp"
        )
        with self.assertRaisesRegex(validator.ContractError, "disposable DB"):
            validator.validate_compose(model, COMMIT, PROJECT)

    def test_missing_or_reordered_bootstrap_identity_fails(self):
        for key, value, message in (
            ("BootstrapIdentity__HeaderName", "X-Other", "bootstrap header"),
            ("BootstrapIdentity__Users__0__UserId", "company-a-user", "authorized bootstrap"),
            ("BootstrapIdentity__Users__1__UserId", "group-bootstrap-admin", "unauthorized bootstrap"),
        ):
            with self.subTest(key=key):
                model = valid_compose()
                model["services"]["api"]["environment"][key] = value
                with self.assertRaisesRegex(validator.ContractError, message):
                    validator.validate_compose(model, COMMIT, PROJECT)

    def test_workflow_keeps_exact_head_and_cleanup_boundaries(self):
        workflow = (ROOT / ".github" / "workflows" / "c01-real-browser-e2e.yml").read_text(
            encoding="utf-8"
        )
        for required in (
            "name: C-01 real-browser E2E",
            "github.event.pull_request.head.sha || github.sha",
            "git merge-base --is-ancestor",
            "C01_BROWSER_SUITE_MISSING",
            "down -v --remove-orphans",
            "C01_E2E_BASELINE_EVIDENCE",
            "C01_E2E_DATABASE_NAME",
            "GOYMEN_ERP_C01_E2E_DISPOSABLE=CONFIRMED",
            "actions/upload-artifact@v7",
            "id: artifact_privacy",
            "steps.artifact_privacy.outcome == 'success'",
        ):
            self.assertIn(required, workflow)
        self.assertNotIn("pull_request_target", workflow)
        self.assertNotIn("secrets.", workflow)
        self.assertNotIn("compose.log", workflow)
        self.assertNotIn("path: artifacts/", workflow)
        for relative in validator.ARTIFACT_UPLOAD_ALLOWLIST:
            self.assertIn(f"artifacts/{relative}", workflow)
        upload = workflow.split("      - name: Upload exact-head C-01 evidence", 1)[1]
        path_block = upload.split("          path: |", 1)[1].split(
            "          if-no-files-found:", 1
        )[0]
        uploaded = {
            line.strip().removeprefix("artifacts/")
            for line in path_block.splitlines()
            if line.strip()
        }
        self.assertEqual(uploaded, set(validator.ARTIFACT_UPLOAD_ALLOWLIST))


class ArtifactPrivacyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        self.safe = self.root / "container-logs" / "compose-ps.txt"
        self.safe.parent.mkdir(parents=True)
        self.safe.write_text("NAME STATUS\napi Up\n", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def test_safe_allowlisted_artifact_passes(self):
        validator.validate_artifact_privacy(self.root)

    def test_raw_compose_log_is_not_uploadable(self):
        raw_log = self.root / "container-logs" / "compose.log"
        raw_log.write_text("GET /api/v1/counterparties?cursor=SIGNED", encoding="utf-8")
        selected = {
            path.relative_to(self.root).as_posix()
            for path in validator.artifact_upload_paths(self.root)
        }
        self.assertNotIn("container-logs/compose.log", selected)

    def test_cursor_in_html_report_fails_closed(self):
        html = self.root / "c01-e2e-html-report" / "index.html"
        html.parent.mkdir(parents=True)
        html.write_text(
            "<html>GET /api/v1/counterparties?cursor=SIGNED-CURSOR</html>",
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "cursor query"):
            validator.validate_artifact_privacy(self.root)

    def test_secret_matrix_fails_closed(self):
        mutations = (
            "Authorization: Bearer header.payload.signature",
            "Idempotency-Key: secret-key",
            "C01_0123456789abcdef0123456789abcdef_Aa1!",
            "Server=sqlserver;Database=db;User Id=sa;Password=secret",
            "C01_E2E_CURSOR_SIGNING_KEY=base64-secret",
        )
        for index, mutation in enumerate(mutations):
            with self.subTest(mutation=mutation):
                path = self.root / "c01-e2e-api-preflight.json"
                path.write_text(mutation, encoding="utf-8")
                with self.assertRaises(validator.ContractError):
                    validator.validate_artifact_privacy(self.root)
                path.unlink()

    def test_runtime_secret_values_fail_closed(self):
        path = self.root / "c01-e2e-api-preflight.json"
        secret = "otherwise-undetectable-runtime-secret"
        path.write_text(f"prefix:{secret}:suffix", encoding="utf-8")
        with self.assertRaisesRegex(validator.ContractError, "runtime secret"):
            validator.validate_artifact_privacy(self.root, [secret])

    def test_too_short_runtime_secret_fails_closed(self):
        with self.assertRaisesRegex(validator.ContractError, "too short"):
            validator.validate_artifact_privacy(self.root, ["short"])


def valid_evidence():
    return {
        "schemaVersion": 1,
        "suiteId": "C01-REAL-BROWSER-E2E-V1",
        "repository": "sinangym/GoymenERP",
        "testedCommit": COMMIT,
        "runId": "100",
        "composeProject": PROJECT,
        "fixture": {
            "id": validator.FIXTURE_ID,
            "schemaVersion": 1,
            "byteLength": 29817,
            "sha256": validator.FIXTURE_SHA256,
            "partyCount": 54,
            "accountCount": 55,
            "sequenceNextValue": 55,
        },
        "browser": {"name": "chromium", "version": "140.0.1"},
        "viewports": validator.EXPECTED_VIEWPORTS,
        "cases": [{"id": case_id, "status": "passed"} for case_id in validator.EXPECTED_CASES],
        "skipped": 0,
        "startedAtUtc": "2026-09-01T00:00:00Z",
        "completedAtUtc": "2026-09-01T00:01:00Z",
    }


def valid_network():
    requests = [
        {"method": "GET", "path": path, "ordinal": index + 1, "outcome": "real-200", "retryOrdinal": 0}
        for index, path in enumerate(sorted(validator.REQUIRED_REAL_PATHS))
    ]
    for outcome in sorted(validator.REQUIRED_FAULT_OUTCOMES):
        requests.append(
            {
                "method": "GET",
                "path": "/api/v1/counterparties",
                "ordinal": len(requests) + 1,
                "outcome": outcome,
                "retryOrdinal": 1 if outcome == "retry-200" else 0,
            }
        )
    return {
        "schemaVersion": 1,
        "testedCommit": COMMIT,
        "authorizedCompanyId": validator.GDC_COMPANY_ID,
        "foreignRecordAbsent": True,
        "redacted": True,
        "requests": requests,
    }


class EvidenceContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        root = pathlib.Path(self.temp.name)
        self.junit = root / "junit.xml"
        self.html = root / "index.html"
        cases = "".join(
            f'<testcase name="{case_id}" classname="c01" />'
            for case_id in validator.EXPECTED_CASES
        )
        self.junit.write_text(f'<testsuites><testsuite>{cases}</testsuite></testsuites>', encoding="utf-8")
        self.html.write_text("<html>C-01</html>", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def validate(self, evidence=None, network=None):
        validator.validate_evidence(
            evidence or valid_evidence(),
            network or valid_network(),
            str(self.junit),
            str(self.html),
            COMMIT,
            "100",
            PROJECT,
        )

    def test_valid_evidence_passes(self):
        self.validate()

    def test_skip_fails(self):
        evidence = copy.deepcopy(valid_evidence())
        evidence["skipped"] = 1
        with self.assertRaisesRegex(validator.ContractError, "skipped"):
            self.validate(evidence=evidence)

    def test_cursor_query_leak_fails(self):
        network = copy.deepcopy(valid_network())
        network["requests"][0]["path"] = "/api/v1/counterparties?cursor=secret"
        with self.assertRaisesRegex(validator.ContractError, "redacted"):
            self.validate(network=network)

    def test_extra_raw_url_field_fails(self):
        network = copy.deepcopy(valid_network())
        network["requests"][0]["rawUrl"] = "?cursor=RAW-SECRET"
        with self.assertRaisesRegex(validator.ContractError, "keys mismatch"):
            self.validate(network=network)

    def test_fault_outcome_on_identity_path_fails(self):
        network = copy.deepcopy(valid_network())
        fault = next(
            request for request in network["requests"]
            if request["outcome"] == "injected-offline"
        )
        fault["path"] = "/api/v1/identity/me"
        with self.assertRaisesRegex(validator.ContractError, "Counterparty read endpoint"):
            self.validate(network=network)

    def test_retry_200_without_retry_ordinal_fails(self):
        network = copy.deepcopy(valid_network())
        retry = next(
            request for request in network["requests"]
            if request["outcome"] == "retry-200"
        )
        retry["retryOrdinal"] = 0
        with self.assertRaisesRegex(validator.ContractError, "retryOrdinal > 0"):
            self.validate(network=network)

    def test_duplicate_network_ordinal_fails(self):
        network = copy.deepcopy(valid_network())
        network["requests"][-1]["ordinal"] = network["requests"][-2]["ordinal"]
        with self.assertRaisesRegex(validator.ContractError, "unique and contiguous"):
            self.validate(network=network)

    def test_one_junit_case_concatenating_all_ids_fails(self):
        joined = " ".join(validator.EXPECTED_CASES)
        self.junit.write_text(
            "<testsuites><testsuite>"
            f'<testcase name="{joined}" classname="c01" />'
            "</testsuite></testsuites>",
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "exactly ten"):
            self.validate()

    def test_junit_duplicate_case_id_fails(self):
        cases = "".join(
            '<testcase name="B-C01-001 duplicate" classname="c01" />'
            for _ in validator.EXPECTED_CASES
        )
        self.junit.write_text(
            f"<testsuites><testsuite>{cases}</testsuite></testsuites>",
            encoding="utf-8",
        )
        with self.assertRaisesRegex(validator.ContractError, "exact and unique"):
            self.validate()


if __name__ == "__main__":
    unittest.main()
