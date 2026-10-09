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
ISSUER = "goymenerp-api-c01-testing-v1"
CONTAINER = f"{PROJECT}-api"
PROCESS_ID = "1"
PROCESS_START = f"{CONTAINER}-process-{PROCESS_ID}"


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
        "issuer": ISSUER,
        "containerId": CONTAINER,
        "processId": int(PROCESS_ID),
        "processStartIdentity": PROCESS_START,
        "ordinal": ordinal,
    }


def server_records():
    return [
        server_record("c01a-evidence-capability", 1, "/api/v1/shell/capabilities"),
        server_record("c01a-evidence-business", 2),
    ]


def browser_requests():
    return [
        {
            "correlationId": "c01a-evidence-capability",
            "method": "GET",
            "path": "/api/v1/shell/capabilities",
            "status": 200,
            "ordinal": 1,
        },
        {
            "correlationId": "c01a-evidence-business",
            "method": "GET",
            "path": "/api/v1/counterparties",
            "status": 200,
            "ordinal": 2,
        },
    ]


def junit(properties=None, *, root=None, suite=None,
          case_name=f"{validator.CASE_ID} a revoked grant closes the route and issues no read",
          case_attrs=None):
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
    root_attrs = root or (
        'id="" name="" tests="1" failures="0" skipped="0" errors="0" time="1.761"'
    )
    suite_attrs = suite or (
        'name="historyRevoke.spec.ts" timestamp="2026-10-09T12:00:00.000Z" '
        'hostname="" tests="1" failures="0" skipped="0" time="1.761" errors="0"'
    )
    testcase_attrs = case_attrs or (
        f'name="{case_name}" classname="historyRevoke.spec.ts" time="1.761"'
    )
    return (
        f"<testsuites {root_attrs}><testsuite {suite_attrs}>"
        f"<testcase {testcase_attrs}><properties>{property_xml}</properties></testcase>"
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

    def write(self, xml=None, records=None, server_text=None):
        xml = junit() if xml is None else xml
        if isinstance(xml, bytes):
            self.junit_path.write_bytes(xml)
        else:
            self.junit_path.write_text(xml, encoding="utf-8")
        if server_text is None:
            records = server_records() if records is None else records
            server_text = "".join(
                json.dumps(record, separators=(",", ":")) + "\n" for record in records
            )
        self.server_path.write_text(server_text, encoding="utf-8")

    def validate(self, **overrides):
        arguments = {
            "junit_path": self.junit_path,
            "server_path": self.server_path,
            "tested_commit": SHA,
            "run_id": RUN,
            "run_attempt": ATTEMPT,
            "compose_project": PROJECT,
            "database": DATABASE,
            "issuer": ISSUER,
            "container_id": CONTAINER,
            "process_id": PROCESS_ID,
            "process_start_identity": PROCESS_START,
        }
        arguments.update(overrides)
        validator.validate(**arguments)

    def test_representative_playwright_junit_and_exact_server_evidence_pass(self):
        self.validate()

    def test_dtd_entity_stray_text_nested_suite_and_malformed_xml_fail(self):
        utf16_doctype = ('<?xml version="1.0" encoding="utf-16"?>'
                         '<!DOCTYPE testsuites SYSTEM "sentinel"><testsuites />').encode("utf-16")
        utf16_entity = ('<?xml version="1.0" encoding="utf-16"?>'
                        '<!DOCTYPE testsuites [<!ENTITY x "sentinel">]><testsuites />').encode("utf-16")
        mutations = (
            '<!DOCTYPE testsuites SYSTEM "sentinel"><testsuites />',
            utf16_doctype,
            utf16_entity,
            junit().replace("<testsuite ", "stray<testsuite ", 1),
            junit().replace("</testsuite>", "<testsuite></testsuite></testsuite>"),
            junit()[:-5],
        )
        for mutation in mutations:
            with self.subTest(kind=type(mutation).__name__):
                self.write(xml=mutation)
                with self.assertRaises(validator.ContractError):
                    self.validate()

    def test_root_suite_and_case_contract_is_strict_for_the_real_reporter_shape(self):
        mutations = (
            junit(root='tests="2" failures="0" errors="0" skipped="0"'),
            junit(suite='tests="1" failures="1" errors="0" skipped="0"'),
            junit(root='tests="1" failures="0" errors="0" skipped="0" retries="0"'),
            junit(case_name="RENAMED-C01A-HISTORY-REVOKE-forward lookalike"),
            junit(case_name=f"{validator.CASE_ID}-LOOKALIKE description"),
            junit(case_attrs=f'name="{validator.CASE_ID} description"'),
            junit(case_attrs=(
                f'name="{validator.CASE_ID} description" '
                'classname="historyRevoke.spec.ts"'
            )),
            junit(suite=(
                'name="historyRevoke.spec.ts" timestamp="2026-10-09T12:00:00.000Z" '
                'hostname="named-project" tests="1" failures="0" skipped="0" '
                'time="1.761" errors="0"'
            )),
            junit(suite=(
                'name="activation/historyRevoke.spec.ts" '
                'timestamp="2026-10-09T12:00:00.000Z" hostname="" tests="1" '
                'failures="0" skipped="0" time="1.761" errors="0"'
            )),
            junit().replace("</testsuite>", '<testcase name="extra" /></testsuite>'),
        )
        for mutation in mutations:
            with self.subTest(mutation=mutation[:80]):
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

    def test_expected_provenance_is_fixed_and_server_records_match_it_exactly(self):
        for argument, value in (
            ("issuer", "other"),
            ("container_id", "other-api"),
            ("process_id", "2"),
            ("process_start_identity", "other-start"),
        ):
            with self.subTest(argument=argument):
                with self.assertRaises(validator.ContractError):
                    self.validate(**{argument: value})

        for field, value in (
            ("issuer", "other"),
            ("containerId", "other-api"),
            ("processId", 2),
            ("processStartIdentity", "other-start"),
        ):
            with self.subTest(field=field):
                records = server_records()
                records[0][field] = value
                self.write(records=records)
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
                records = server_records()
                records[0][field] = value
                self.write(records=records)
                with self.assertRaises(validator.ContractError):
                    self.validate()

        record = server_record("c01a-evidence-business", 1)
        record["query"] = "cursor=secret"
        self.write(records=[record])
        with self.assertRaisesRegex(validator.ContractError, "keys mismatch"):
            self.validate()

    def test_duplicate_json_object_keys_fail_for_server_and_browser(self):
        first = json.dumps(server_records()[0], separators=(",", ":"))
        duplicate_server = first[:-1] + ',"status":200}\n'
        self.write(server_text=duplicate_server)
        with self.assertRaisesRegex(validator.ContractError, "repeats key status"):
            self.validate()

        duplicate_browser = (
            '[{"correlationId":"c01a-evidence-capability","method":"GET",'
            '"path":"/api/v1/shell/capabilities","status":200,"status":200,"ordinal":1}]'
        )
        self.write(xml=junit({"activation.requestEvidence": duplicate_browser}))
        with self.assertRaisesRegex(validator.ContractError, "repeats key status"):
            self.validate()

    def test_server_ordinals_correlations_and_provenance_are_exact(self):
        duplicate = [
            server_records()[0],
            {**server_records()[1], "correlationId": "c01a-evidence-capability"},
        ]
        gap = [server_records()[0], {**server_records()[1], "ordinal": 3}]
        for records in (duplicate, gap):
            with self.subTest(records=records):
                self.write(records=records)
                with self.assertRaises(validator.ContractError):
                    self.validate()

    def test_browser_server_bijection_rejects_order_extra_missing_duplicate_and_mismatch(self):
        reversed_order = [
            {**browser_requests()[1], "ordinal": 1},
            {**browser_requests()[0], "ordinal": 2},
        ]
        request_lists = (
            reversed_order,
            [browser_requests()[0]],
            [browser_requests()[1]],
            [browser_requests()[0], browser_requests()[0]],
            [browser_requests()[0], {**browser_requests()[1], "status": 503}],
        )
        for request_list in request_lists:
            with self.subTest(request_list=request_list):
                encoded = json.dumps(request_list, separators=(",", ":"))
                self.write(xml=junit({"activation.requestEvidence": encoded}))
                with self.assertRaises(validator.ContractError):
                    self.validate()

        self.write(records=server_records() + [
            server_record("c01a-evidence-extra", 3, "/api/v1/other")
        ])
        with self.assertRaisesRegex(validator.ContractError, "unmatched server"):
            self.validate()

    def test_browser_and_server_correlations_require_the_evidence_namespace(self):
        records = server_records()
        records[0]["correlationId"] = "ordinary-capability"
        self.write(records=records)
        with self.assertRaisesRegex(validator.ContractError, "outside the evidence"):
            self.validate()

        requests = browser_requests()
        requests[0]["correlationId"] = "ordinary-capability"
        self.write(
            xml=junit({
                "activation.requestEvidence": json.dumps(requests, separators=(",", ":"))
            }),
            records=server_records(),
        )
        with self.assertRaisesRegex(validator.ContractError, "outside the evidence"):
            self.validate()

    def test_cli_returns_zero_only_for_exact_evidence(self):
        arguments = [
            "--junit", str(self.junit_path),
            "--server-evidence", str(self.server_path),
            "--tested-commit", SHA,
            "--run-id", RUN,
            "--run-attempt", ATTEMPT,
            "--compose-project", PROJECT,
            "--database", DATABASE,
            "--expected-issuer", ISSUER,
            "--expected-container-id", CONTAINER,
            "--expected-process-id", PROCESS_ID,
            "--expected-process-start-identity", PROCESS_START,
        ]
        self.assertEqual(0, validator.main(arguments))
        wrong = list(arguments)
        wrong[wrong.index(SHA)] = "f" * 40
        self.assertEqual(1, validator.main(wrong))


if __name__ == "__main__":
    unittest.main()
