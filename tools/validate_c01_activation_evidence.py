#!/usr/bin/env python3
"""Validate C-01 activation JUnit and the API's redacted request evidence."""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET
from decimal import Decimal, InvalidOperation
from typing import Any

CASE_ID = "C01A-HISTORY-REVOKE-forward"
CASE_FILE = "historyRevoke.spec.ts"
EVIDENCE_CORRELATION_PREFIX = "c01a-evidence-"
EVIDENCE_ISSUER = "goymenerp-api-c01-testing-v1"
STALE_WINDOW_MS = Decimal("30000")
SHA40 = re.compile(r"[0-9a-f]{40}\Z")
POSITIVE_INTEGER = re.compile(r"[1-9][0-9]*\Z")
SAFE_IDENTITY = re.compile(r"[A-Za-z0-9_.-]{1,128}\Z")
SAFE_CORRELATION = re.compile(r"[A-Za-z0-9_.:-]{1,128}\Z")
SAFE_METHOD = re.compile(r"[A-Z]{3,10}\Z")
STRICT_DECIMAL = re.compile(r"(?:0|[1-9][0-9]*)(?:\.[0-9]+)?\Z")
IDENTITY_PROPERTIES = {
    "testedCommit": "activation.testedCommit",
    "runId": "activation.runId",
    "runAttempt": "activation.runAttempt",
    "composeProject": "activation.composeProject",
    "database": "activation.database",
}
ELAPSED_PROPERTY = "activation.elapsedMs"
PATH_PROPERTY = "activation.authorizedResponsePath"
STATUS_PROPERTY = "activation.authorizedResponseStatus"
REQUESTS_PROPERTY = "activation.requestEvidence"
COUNTER_KEYS = {"tests", "failures", "errors", "skipped"}
ROOT_REPORTER_KEYS = {"id", "name", "time"}
SUITE_REPORTER_KEYS = {"name", "timestamp", "hostname", "time"}
CASE_KEYS = {"name", "classname", "time"}
SERVER_KEYS = {
    "schemaVersion", "correlationId", "method", "path", "status", "testedCommit",
    "runId", "runAttempt", "composeProject", "database", "issuer", "containerId",
    "processId", "processStartIdentity", "ordinal",
}
BROWSER_KEYS = {"correlationId", "method", "path", "status", "ordinal"}


class ContractError(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ContractError(message)


def _counter(element: ET.Element, name: str, expected: int) -> None:
    raw = element.attrib.get(name)
    require(raw is not None and re.fullmatch(r"0|[1-9][0-9]*", raw) is not None,
            f"<{element.tag}> {name} must be a canonical integer")
    require(int(raw) == expected, f"<{element.tag}> {name} must be {expected}")


def _reject_text(element: ET.Element) -> None:
    require(element.text is None or element.text.strip() == "", f"stray text under <{element.tag}>")
    for child in element:
        require(child.tail is None or child.tail.strip() == "", f"stray tail after <{child.tag}>")
        _reject_text(child)


def _strict_json_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        require(key not in result, f"JSON object repeats key {key}")
        result[key] = value
    return result


def parse_junit(path: pathlib.Path) -> tuple[dict[str, str], list[dict[str, Any]]]:
    raw = path.read_bytes()
    require(raw.strip() != b"", "activation JUnit is empty")
    # Removing NUL bytes makes the declaration scan encoding-safe for UTF-16/32
    # while ElementTree still receives the original bytes and enforces the XML encoding.
    declaration_scan = raw.lower().replace(b"\x00", b"")
    require(b"<!doctype" not in declaration_scan and b"<!entity" not in declaration_scan,
            "DTD/entity declarations are forbidden")
    try:
        root = ET.fromstring(raw)
    except ET.ParseError as error:
        raise ContractError("activation JUnit is malformed") from error

    require(root.tag == "testsuites", "JUnit root must be <testsuites>")
    require(
        set(root.attrib) == COUNTER_KEYS | ROOT_REPORTER_KEYS,
        "JUnit root attributes mismatch",
    )
    _counter(root, "tests", 1)
    for name in ("failures", "errors", "skipped"):
        _counter(root, name, 0)
    require(root.attrib["id"] == "" and root.attrib["name"] == "",
            "JUnit root reporter identity mismatch")
    require(STRICT_DECIMAL.fullmatch(root.attrib["time"]) is not None,
            "JUnit root time must be a strict decimal")

    suites = list(root)
    require(len(suites) == 1 and suites[0].tag == "testsuite", "JUnit must contain one testsuite")
    suite = suites[0]
    require(
        set(suite.attrib) == COUNTER_KEYS | SUITE_REPORTER_KEYS,
        "JUnit suite attributes mismatch",
    )
    _counter(suite, "tests", 1)
    for name in ("failures", "errors", "skipped"):
        _counter(suite, name, 0)
    require(suite.attrib["name"] == CASE_FILE, "JUnit suite name mismatch")
    require(re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z",
                         suite.attrib["timestamp"]) is not None,
            "JUnit suite timestamp is invalid")
    # The current activation Playwright config has no named projects. Playwright
    # 1.62 therefore emits the exact empty projectName as the JUnit hostname.
    require(suite.attrib["hostname"] == "", "JUnit suite hostname mismatch")
    require(STRICT_DECIMAL.fullmatch(suite.attrib["time"]) is not None,
            "JUnit suite time must be a strict decimal")

    cases = list(suite)
    require(len(cases) == 1 and cases[0].tag == "testcase",
            "JUnit must contain exactly one direct testcase")
    case = cases[0]
    require(set(case.attrib) == CASE_KEYS, "testcase attributes mismatch")
    case_name = case.attrib["name"]
    require(
        re.fullmatch(rf"{re.escape(CASE_ID)} [^\r\n]+", case_name) is not None,
        f"testcase must begin with the exact {CASE_ID} token and a description",
    )
    require(STRICT_DECIMAL.fullmatch(case.attrib["time"]) is not None,
            "testcase time must be a strict decimal")
    # The pinned reporter uses the source file suite title as classname.
    require(case.attrib["classname"] == CASE_FILE, "testcase classname mismatch")
    require(len(case) == 1 and case[0].tag == "properties",
            "testcase must contain exactly one properties element")
    properties_node = case[0]
    require(not properties_node.attrib, "properties attributes are forbidden")

    properties: dict[str, str] = {}
    for child in properties_node:
        require(child.tag == "property", "properties may contain only property elements")
        require(set(child.attrib) == {"name", "value"}, "property attributes mismatch")
        name = child.attrib["name"]
        require(name not in properties, f"duplicate property {name}")
        properties[name] = child.attrib["value"]

    expected_properties = set(IDENTITY_PROPERTIES.values()) | {
        ELAPSED_PROPERTY, PATH_PROPERTY, STATUS_PROPERTY, REQUESTS_PROPERTY,
    }
    require(set(properties) == expected_properties, "activation property set mismatch")
    _reject_text(root)

    elapsed_raw = properties[ELAPSED_PROPERTY]
    require(STRICT_DECIMAL.fullmatch(elapsed_raw) is not None, "elapsed must be a strict decimal")
    try:
        elapsed = Decimal(elapsed_raw)
    except InvalidOperation as error:
        raise ContractError("elapsed must be a strict decimal") from error
    require(elapsed < STALE_WINDOW_MS, "elapsed must be less than 30000 ms")
    require(properties[PATH_PROPERTY] == "/api/v1/counterparties",
            "authorized response path mismatch")
    require(properties[STATUS_PROPERTY] == "200", "authorized response status must be 200")

    try:
        browser_requests = json.loads(
            properties[REQUESTS_PROPERTY],
            object_pairs_hook=_strict_json_object,
        )
    except json.JSONDecodeError as error:
        raise ContractError("browser request evidence is malformed JSON") from error
    require(isinstance(browser_requests, list) and browser_requests,
            "browser request evidence must be a non-empty array")
    return properties, browser_requests


def parse_server_evidence(path: pathlib.Path) -> list[dict[str, Any]]:
    lines = path.read_text(encoding="utf-8").splitlines()
    require(lines, "server request evidence is empty")
    records: list[dict[str, Any]] = []
    for line_number, line in enumerate(lines, start=1):
        require(line != "" and line == line.strip(), f"server evidence line {line_number} is malformed")
        try:
            record = json.loads(line, object_pairs_hook=_strict_json_object)
        except json.JSONDecodeError as error:
            raise ContractError(f"server evidence line {line_number} is malformed JSON") from error
        require(isinstance(record, dict), f"server evidence line {line_number} is not an object")
        require(set(record) == SERVER_KEYS, f"server evidence line {line_number} keys mismatch")
        records.append(record)
    return records


def validate(
    junit_path: pathlib.Path,
    server_path: pathlib.Path,
    tested_commit: str,
    run_id: str,
    run_attempt: str,
    compose_project: str,
    database: str,
    issuer: str,
    container_id: str,
    process_id: str,
    process_start_identity: str,
) -> None:
    require(SHA40.fullmatch(tested_commit) is not None, "expected tested commit must be lowercase SHA40")
    require(POSITIVE_INTEGER.fullmatch(run_id) is not None, "expected run id must be canonical positive")
    require(POSITIVE_INTEGER.fullmatch(run_attempt) is not None,
            "expected run attempt must be canonical positive")
    require(compose_project == f"goymen-c01-{run_id}-{run_attempt}",
            "expected compose project mismatch")
    require(database == f"GoymenErpC01_{run_id}_{run_attempt}", "expected database mismatch")
    require(issuer == EVIDENCE_ISSUER, "expected evidence issuer mismatch")
    require(container_id == f"{compose_project}-api", "expected container identity mismatch")
    require(process_id == "1", "expected process id mismatch")
    require(
        process_start_identity == f"{container_id}-process-{process_id}",
        "expected process start identity mismatch",
    )
    expected = {
        "testedCommit": tested_commit,
        "runId": run_id,
        "runAttempt": run_attempt,
        "composeProject": compose_project,
        "database": database,
        "issuer": issuer,
        "containerId": container_id,
        "processStartIdentity": process_start_identity,
    }

    properties, browser_requests = parse_junit(junit_path)
    for field, property_name in IDENTITY_PROPERTIES.items():
        require(properties[property_name] == expected[field], f"JUnit {property_name} mismatch")

    server_records = parse_server_evidence(server_path)
    correlations: set[str] = set()
    ordinals: list[int] = []
    for index, record in enumerate(server_records, start=1):
        require(record["schemaVersion"] == 1 and type(record["schemaVersion"]) is int,
                f"server record {index} schema mismatch")
        require(type(record["correlationId"]) is str
                and SAFE_CORRELATION.fullmatch(record["correlationId"]) is not None,
                f"server record {index} correlation mismatch")
        require(record["correlationId"].startswith(EVIDENCE_CORRELATION_PREFIX),
                f"server record {index} is outside the evidence correlation namespace")
        require(record["correlationId"] not in correlations, "server correlation ids must be unique")
        correlations.add(record["correlationId"])
        require(type(record["method"]) is str and SAFE_METHOD.fullmatch(record["method"]) is not None,
                f"server record {index} method mismatch")
        require(type(record["path"]) is str and record["path"].startswith("/")
                and "?" not in record["path"] and "#" not in record["path"]
                and not any(ord(character) < 32 or ord(character) == 127 for character in record["path"]),
                f"server record {index} path is not redacted")
        require(type(record["status"]) is int and 100 <= record["status"] <= 599,
                f"server record {index} status mismatch")
        for field, value in expected.items():
            require(record[field] == value, f"server record {index} {field} mismatch")
        require(SAFE_IDENTITY.fullmatch(record["containerId"]) is not None,
                f"server record {index} container identity mismatch")
        require(type(record["processId"]) is int and record["processId"] == int(process_id),
                f"server record {index} process identity mismatch")
        require(type(record["processStartIdentity"]) is str
                and SAFE_IDENTITY.fullmatch(record["processStartIdentity"]) is not None,
                f"server record {index} process start identity mismatch")
        require(type(record["ordinal"]) is int and record["ordinal"] > 0,
                f"server record {index} ordinal mismatch")
        ordinals.append(record["ordinal"])

    require(ordinals == list(range(1, len(server_records) + 1)),
            "server ordinals must be contiguous and ordered")
    seen_browser: set[str] = set()
    matched_server_ordinals: list[int] = []
    for ordinal, browser in enumerate(browser_requests, start=1):
        require(isinstance(browser, dict) and set(browser) == BROWSER_KEYS,
                f"browser record {ordinal} keys mismatch")
        require(type(browser["ordinal"]) is int and browser["ordinal"] == ordinal,
                "browser ordinals must be contiguous and ordered")
        correlation = browser["correlationId"]
        require(type(correlation) is str and SAFE_CORRELATION.fullmatch(correlation) is not None,
                f"browser record {ordinal} correlation mismatch")
        require(correlation.startswith(EVIDENCE_CORRELATION_PREFIX),
                f"browser record {ordinal} is outside the evidence correlation namespace")
        require(correlation not in seen_browser, "browser correlation ids must be unique")
        seen_browser.add(correlation)
        matches = [record for record in server_records if record["correlationId"] == correlation]
        require(len(matches) == 1, f"browser record {ordinal} has no unique server match")
        server = matches[0]
        require(
            (browser["method"], browser["path"], browser["status"])
            == (server["method"], server["path"], server["status"]),
            f"browser record {ordinal} disagrees with server evidence",
        )
        require(browser["ordinal"] == server["ordinal"],
                f"browser record {ordinal} server order mismatch")
        matched_server_ordinals.append(server["ordinal"])

    require(len(browser_requests) == len(server_records),
            "browser-to-server request evidence has unmatched server records")
    require(matched_server_ordinals == list(range(1, len(server_records) + 1)),
            "browser-to-server request order mismatch")
    matched_paths = {
        (record["method"], record["path"], record["status"])
        for record in server_records
    }
    require(("GET", "/api/v1/shell/capabilities", 200) in matched_paths,
            "no matched GET /api/v1/shell/capabilities 200 evidence")
    require(("GET", "/api/v1/counterparties", 200) in matched_paths,
            "no matched GET /api/v1/counterparties 200 evidence")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--junit", type=pathlib.Path, required=True)
    parser.add_argument("--server-evidence", type=pathlib.Path, required=True)
    parser.add_argument("--tested-commit", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument("--compose-project", required=True)
    parser.add_argument("--database", required=True)
    parser.add_argument("--expected-issuer", required=True)
    parser.add_argument("--expected-container-id", required=True)
    parser.add_argument("--expected-process-id", required=True)
    parser.add_argument("--expected-process-start-identity", required=True)
    args = parser.parse_args(argv)
    try:
        validate(
            args.junit, args.server_evidence, args.tested_commit, args.run_id,
            args.run_attempt, args.compose_project, args.database,
            args.expected_issuer, args.expected_container_id,
            args.expected_process_id, args.expected_process_start_identity,
        )
    except (ContractError, OSError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print("C-01 activation evidence: 1 testcase, retry=0, browser/server request bijection verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
