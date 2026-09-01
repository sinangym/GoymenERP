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
                "image": "mcr.microsoft.com/mssql/server:2022-latest",
                "healthcheck": {"test": ["CMD-SHELL", "SELECT 1"]},
                "ports": [port(1433, 11433)],
                "labels": labels,
            },
            "api": {
                "build": {
                    "labels": {"org.opencontainers.image.revision": COMMIT}
                },
                "environment": {
                    "ASPNETCORE_ENVIRONMENT": "Development",
                    "DB_PROVIDER": "SqlServer",
                    "DB_CONNECTION_STRING": "Database=GoymenErp",
                    "Counterparty__CursorSigningKey": "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
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

    def test_identity_drift_fails(self):
        model = valid_compose()
        model["services"]["web-authorized"]["build"]["args"]["VITE_DEV_USER"] = "company-a-user"
        with self.assertRaisesRegex(validator.ContractError, "identity"):
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
            "actions/upload-artifact@v7",
        ):
            self.assertIn(required, workflow)
        self.assertNotIn("pull_request_target", workflow)
        self.assertNotIn("secrets.", workflow)


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


if __name__ == "__main__":
    unittest.main()
