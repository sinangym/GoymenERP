import copy
import html
import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "tools" / "validate_c01_activation_evidence.py"
SPEC = importlib.util.spec_from_file_location("validate_c01_activation_evidence", MODULE_PATH)
validator = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(validator)

SHA = "0123456789abcdef0123456789abcdef01234567"
RUN = "100"
ATTEMPT = "2"
PROJECT = "goymen-c01-100-2"
DATABASE = "GoymenErpC01_100_2"


def server_record(correlation, ordinal, path="/api/v1/counterparties", status=200):
    return {
        "schemaVersion": 1,
        "correlationId": correlation,
        "method": "GET",
        "path": path,
        "status": status,
        "testedCommit": SHA,
        "runId": RUN,
        "runAttempt": ATTEMPT,
        "composeProject": PROJECT,
        "database": DATABASE,
        "containerId": "api-container",
        "processId": 42,
        "ordinal": ordinal,
    }


def browser_requests():
    return [
        {
            "correlationId": "correlation-capability",
            "method": "GET",
            "path": "/api/v1/shell/capabilities",
            "status": 200,
            "ordinal": 1,
        },
        {
            "correlationId": "correlation-business",
            "method": "GET",
            "path": "/api/v1/counterparties",
            "status": 200,
            "ordinal": 2,
        },
    ]


def junit(properties=None, *, root=None, suite=None, case_name=validator.CASE_ID):
    values = {
        "activation.testedCommit": SHA,
        "activation.runId": RUN,
        "activation.runAttempt": ATTEMPT,
        "activation.composeProject": PROJECT,
        "activation.database": DATABASE,
        "activation.elapsedMs": "125.50",
        "activation.authorizedResponsePath": "/api/v1/counterparties",
        "activation.authorizedResponseStatus": "200",
        "activation.requestEvidence": json.dumps(browser_requests(), separators=(",", ":")),
    }
    values.update(properties or {})
    property_xml = "".join(
        f'<property name="{html.escape(name, quote=True)}" value="{html.escape(value, quote=True)}" />'
        for name, value in values.items()
    )
    root_attrs = root or 'tests="1" failures="0" errors="0" skipped="0" retries="0"'
    suite_attrs = suite or 'tests="1" failures="0" errors="0" skipped="0" retries="0"'
    return (
        f"<testsuites {root_attrs}><testsuite {suite_attrs}>"
        f'<testcase name="{case_name}"><properties>{property_xml}</properties></testcase>'
        "</testsuite></testsuites>"
    )


class ActivationEvidenceContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.temp.name)
        self.junit_path = self.root / "activation.xml"
        self.server_path = self.root / "server.jsonl"
        self.write()

    def tearDown(self):
        self.temp.cleanup()

    def write(self, xml=None, records=None):
        self.junit_path.write_text(xml or junit(), encoding="utf-8")
        records = records or [
            server_record("health-correlation", 1, "/health/ready"),
            server_record("correlation-capability", 2, "/api/v1/shell/capabilities"),
            server_record("correlation-business", 3),
        ]
        self.server_path.write_text(
            "".join(json.dumps(record, separators=(",", ":")) + "\n" for record in records),
            encoding="utf-8",
        )

    def validate(self, **overrides):
        arguments = {
            "junit_path": self.junit_path,
            "server_path": self.server_path,
            "tested_commit": SHA,
            "run_id": RUN,
            "run_attempt": ATTEMPT,
            "compose_project": PROJECT,
            "database": DATABASE,
        }
        arguments.update(overrides)
        validator.validate(**arguments)

    def test_exact_evidence_passes(self):
        self.validate()

    def test_dtd_stray_text_nested_suite_and_malformed_xml_fail(self):
        mutations = (
            '<!DOCTYPE testsuites SYSTEM "sentinel"><testsuites />',
            junit().replace("<testsuite ", "stray<testsuite ", 1),
            junit().replace("</testsuite>", "<testsuite></testsuite></testsuite>"),
            junit()[:-5],
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation[:30]):
                self.write(xml=mutation)
                with self.assertRaises(validator.ContractError):
                    self.validate()

    def test_root_suite_and_case_contract_is_exact(self):
        mutations = (
            junit(root='tests="2" failures="0" errors="0" skipped="0" retries="0"'),
            junit(suite='tests="1" failures="1" errors="0" skipped="0" retries="0"'),
            junit(root='tests="1" failures="0" errors="0" skipped="0" retries="1"'),
            junit(case_name="RENAMED-C01A-HISTORY-REVOKE-forward"),
            junit().replace("</testsuite>", '<testcase name="extra" /></testsuite>'),
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation[:50]):
                self.write(xml=mutation)
                with self.assertRaises(validator.ContractError):
                    self.validate()

    def test_elapsed_is_strict_and_inside_window(self):
        for value in ("030", "+1", "1e2", "30000", "30000.0", "-1", "NaN"):
            with self.subTest(value=value):
                self.write(xml=junit({"activation.elapsedMs": value}))
                with self.assertRaisesRegex(validator.ContractError, "elapsed"):
                    self.validate()

    def test_exact_path_status_and_identity_are_bound(self):
        mutations = (
            {"activation.authorizedResponsePath": "/api/v1/counterparties/other"},
            {"activation.authorizedResponseStatus": "204"},
            {"activation.testedCommit": "f" * 40},
            {"activation.runId": "101"},
            {"activation.runAttempt": "3"},
            {"activation.composeProject": "goymen-c01-100-3"},
            {"activation.database": "GoymenErpC01_100_3"},
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                self.write(xml=junit(mutation))
                with self.assertRaises(validator.ContractError):
                    self.validate()

    def test_server_replay_cross_identity_and_secret_shaped_fields_fail(self):
        for field, value in (
            ("testedCommit", "f" * 40),
            ("runId", "101"),
            ("runAttempt", "3"),
            ("composeProject", "goymen-c01-100-3"),
            ("database", "GoymenErpC01_100_3"),
        ):
            with self.subTest(field=field):
                records = [
                    server_record("correlation-capability", 1, "/api/v1/shell/capabilities"),
                    server_record("correlation-business", 2),
                ]
                records[0][field] = value
                self.write(records=records)
                with self.assertRaises(validator.ContractError):
                    self.validate()

        record = server_record("correlation-business", 1)
        record["query"] = "cursor=secret"
        self.write(records=[record])
        with self.assertRaisesRegex(validator.ContractError, "keys mismatch"):
            self.validate()

    def test_server_ordinals_correlations_and_process_identity_are_exact(self):
        cases = []
        duplicate = [
            server_record("correlation-capability", 1, "/api/v1/shell/capabilities"),
            server_record("correlation-capability", 2),
        ]
        cases.append(duplicate)
        gap = [
            server_record("correlation-capability", 1, "/api/v1/shell/capabilities"),
            server_record("correlation-business", 3),
        ]
        cases.append(gap)
        split = [
            server_record("correlation-capability", 1, "/api/v1/shell/capabilities"),
            server_record("correlation-business", 2),
        ]
        split[1]["processId"] = 43
        cases.append(split)
        for records in cases:
            with self.subTest(records=records):
                self.write(records=records)
                with self.assertRaises(validator.ContractError):
                    self.validate()

    def test_browser_to_server_bijection_rejects_missing_duplicate_and_mismatch(self):
        for requests in (
            [browser_requests()[0]],
            [browser_requests()[0], browser_requests()[0]],
            [
                browser_requests()[0],
                {**browser_requests()[1], "status": 503},
            ],
        ):
            with self.subTest(requests=requests):
                encoded = json.dumps(requests, separators=(",", ":"))
                self.write(xml=junit({"activation.requestEvidence": encoded}))
                with self.assertRaises(validator.ContractError):
                    self.validate()

    def test_cli_returns_zero_only_for_exact_evidence(self):
        self.assertEqual(
            0,
            validator.main([
                "--junit", str(self.junit_path),
                "--server-evidence", str(self.server_path),
                "--tested-commit", SHA,
                "--run-id", RUN,
                "--run-attempt", ATTEMPT,
                "--compose-project", PROJECT,
                "--database", DATABASE,
            ]),
        )
        self.assertEqual(
            1,
            validator.main([
                "--junit", str(self.junit_path),
                "--server-evidence", str(self.server_path),
                "--tested-commit", "f" * 40,
                "--run-id", RUN,
                "--run-attempt", ATTEMPT,
                "--compose-project", PROJECT,
                "--database", DATABASE,
            ]),
        )


if __name__ == "__main__":
    unittest.main()
