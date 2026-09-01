#!/usr/bin/env python3
"""Fail-closed validator for the disposable C-01 browser acceptance contract."""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as et
from typing import Any


FIXTURE_SHA256 = "f23551944a65fe29681d768422b181dd73d68b1bdf8216e42bc643713e4d79d7"
FIXTURE_ID = "C01-E2E-GOLDEN-V1"
GDC_COMPANY_ID = "10000000-0000-0000-0000-000000000001"
EXPECTED_CASES = [f"B-C01-{number:03d}" for number in range(1, 11)]
EXPECTED_VIEWPORTS = ["1600x900", "768x1024", "390x844", "640x900", "320x900"]
REQUIRED_REAL_PATHS = {
    "/api/v1/identity/me",
    "/api/v1/organization/companies",
    "/api/v1/shell/capabilities",
    "/api/v1/counterparties",
}
REQUIRED_FAULT_OUTCOMES = {
    "injected-delay",
    "injected-500",
    "injected-503",
    "injected-abort",
    "injected-offline",
    "retry-200",
    "real-400-invalid-cursor",
}


class ContractError(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ContractError(message)


def load_json(path: str) -> dict[str, Any]:
    if path == "-":
        value = json.load(sys.stdin)
    else:
        value = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    require(isinstance(value, dict), "JSON root must be an object")
    return value


def validate_compose(model: dict[str, Any], expected_commit: str, expected_project: str) -> None:
    require(model.get("name") == expected_project, "compose project name mismatch")
    services = model.get("services")
    require(isinstance(services, dict), "compose services must be an object")
    require(
        set(services) == {"sqlserver", "api", "web-authorized", "web-unauthorized"},
        "compose service inventory must be exact",
    )

    for name, service in services.items():
        require(isinstance(service, dict), f"{name} service must be an object")
        require(not service.get("privileged", False), f"{name} must not be privileged")
        require(service.get("network_mode") != "host", f"{name} must not use host network")
        require(not service.get("cap_add"), f"{name} must not add Linux capabilities")
        labels = service.get("labels", {})
        require(
            labels.get("com.goymen.e2e.suite") == "C01-REAL-BROWSER-E2E-V1",
            f"{name} suite label mismatch",
        )
        require(
            labels.get("com.goymen.e2e.tested-commit") == expected_commit,
            f"{name} source SHA label mismatch",
        )
        ports = service.get("ports", [])
        require(ports, f"{name} must expose an explicit loopback acceptance port")
        for port in ports:
            require(port.get("host_ip") == "127.0.0.1", f"{name} port is not loopback-only")
            require(port.get("protocol") == "tcp", f"{name} port must be TCP")

    sql = services["sqlserver"]
    require(sql.get("image") == "mcr.microsoft.com/mssql/server:2022-latest", "SQL image drift")
    require(bool(sql.get("healthcheck")), "SQL healthcheck is required")

    api = services["api"]
    api_environment = api.get("environment", {})
    require(api_environment.get("ASPNETCORE_ENVIRONMENT") == "Development", "API environment drift")
    require(api_environment.get("DB_PROVIDER") == "SqlServer", "API DB provider drift")
    require("Database=GoymenErp" in api_environment.get("DB_CONNECTION_STRING", ""), "API DB drift")
    cursor_key = api_environment.get("Counterparty__CursorSigningKey", "")
    try:
        decoded_cursor_key = base64.b64decode(cursor_key, validate=True)
    except (ValueError, TypeError) as exception:
        raise ContractError("cursor signing key must be canonical base64") from exception
    require(len(decoded_cursor_key) >= 32, "cursor signing key must be at least 32 bytes")

    for name, identity in (
        ("web-authorized", "group-bootstrap-admin"),
        ("web-unauthorized", "company-a-user"),
    ):
        build = services[name].get("build", {})
        require(build.get("dockerfile") == "src/frontend/goymen-erp-web/Dockerfile", f"{name} Dockerfile drift")
        args = build.get("args", {})
        require(args.get("VITE_BOOTSTRAP_IDENTITY_ENABLED") == "true", f"{name} bootstrap flag drift")
        require(args.get("VITE_DEV_USER") == identity, f"{name} identity profile drift")
        build_labels = build.get("labels", {})
        require(
            build_labels.get("org.opencontainers.image.revision") == expected_commit,
            f"{name} image source SHA label mismatch",
        )

    api_build_labels = api.get("build", {}).get("labels", {})
    require(
        api_build_labels.get("org.opencontainers.image.revision") == expected_commit,
        "API image source SHA label mismatch",
    )
    volumes = model.get("volumes", {})
    require(len(volumes) == 1, "exactly one disposable SQL volume is allowed")
    volume = next(iter(volumes.values()))
    require(
        volume.get("labels", {}).get("com.goymen.e2e.tested-commit") == expected_commit,
        "SQL volume source SHA label mismatch",
    )


def parse_utc(value: Any, name: str) -> dt.datetime:
    require(isinstance(value, str) and value, f"{name} must be a timestamp")
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exception:
        raise ContractError(f"{name} is not ISO-8601") from exception
    require(parsed.tzinfo is not None, f"{name} must include a UTC offset")
    return parsed.astimezone(dt.timezone.utc)


def validate_evidence(
    evidence: dict[str, Any],
    network: dict[str, Any],
    junit_path: str,
    html_path: str,
    expected_commit: str,
    expected_run_id: str,
    expected_project: str,
) -> None:
    require(evidence.get("schemaVersion") == 1, "evidence schemaVersion mismatch")
    require(evidence.get("suiteId") == "C01-REAL-BROWSER-E2E-V1", "suiteId mismatch")
    require(evidence.get("repository") == "sinangym/GoymenERP", "repository mismatch")
    require(evidence.get("testedCommit") == expected_commit, "testedCommit mismatch")
    require(str(evidence.get("runId")) == expected_run_id, "runId mismatch")
    require(evidence.get("composeProject") == expected_project, "composeProject mismatch")

    fixture = evidence.get("fixture", {})
    require(fixture.get("id") == FIXTURE_ID, "fixture id mismatch")
    require(fixture.get("schemaVersion") == 1, "fixture schemaVersion mismatch")
    require(fixture.get("byteLength") == 29_817, "fixture byteLength mismatch")
    require(fixture.get("sha256") == FIXTURE_SHA256, "fixture SHA-256 mismatch")
    require(fixture.get("partyCount") == 54, "fixture partyCount mismatch")
    require(fixture.get("accountCount") == 55, "fixture accountCount mismatch")
    require(fixture.get("sequenceNextValue") == 55, "fixture sequence mismatch")

    browser = evidence.get("browser", {})
    require(browser.get("name") == "chromium", "browser must be chromium")
    require(isinstance(browser.get("version"), str) and browser["version"].strip(), "browser version missing")
    require(evidence.get("viewports") == EXPECTED_VIEWPORTS, "viewport matrix mismatch")
    cases = evidence.get("cases")
    require(isinstance(cases, list) and len(cases) == 10, "exactly ten cases are required")
    case_ids = [case.get("id") for case in cases if isinstance(case, dict)]
    require(case_ids == EXPECTED_CASES, "case IDs or order mismatch")
    require(all(case.get("status") == "passed" for case in cases), "every case must pass")
    require(evidence.get("skipped") == 0, "skipped must be zero")
    started = parse_utc(evidence.get("startedAtUtc"), "startedAtUtc")
    completed = parse_utc(evidence.get("completedAtUtc"), "completedAtUtc")
    require(completed >= started, "evidence time interval is invalid")

    validate_network(network, expected_commit)
    validate_junit(junit_path)
    html = pathlib.Path(html_path)
    require(html.is_file() and html.stat().st_size > 0, "browser HTML report is missing")


def validate_network(network: dict[str, Any], expected_commit: str) -> None:
    require(network.get("schemaVersion") == 1, "network schemaVersion mismatch")
    require(network.get("testedCommit") == expected_commit, "network testedCommit mismatch")
    require(network.get("authorizedCompanyId") == GDC_COMPANY_ID, "network company mismatch")
    require(network.get("foreignRecordAbsent") is True, "foreign record absence is unproven")
    require(network.get("redacted") is True, "network evidence must be redacted")
    requests = network.get("requests")
    require(isinstance(requests, list) and requests, "network requests are required")
    observed_paths: set[str] = set()
    outcomes: set[str] = set()
    for request in requests:
        require(isinstance(request, dict), "network request entry must be an object")
        require(request.get("method") == "GET", "C-01 browser suite must be read-only")
        path = request.get("path")
        require(isinstance(path, str) and path.startswith("/api/v1/"), "network path is invalid")
        require("?" not in path and "cursor" not in path.lower(), "network path must be query-redacted")
        require(isinstance(request.get("ordinal"), int) and request["ordinal"] > 0, "request ordinal invalid")
        require(
            isinstance(request.get("retryOrdinal"), int) and request["retryOrdinal"] >= 0,
            "retry ordinal invalid",
        )
        outcome = request.get("outcome")
        require(isinstance(outcome, str) and outcome, "network outcome is required")
        observed_paths.add(path)
        outcomes.add(outcome)
    require(REQUIRED_REAL_PATHS <= observed_paths, "required real API paths are missing")
    require(REQUIRED_FAULT_OUTCOMES <= outcomes, "continuation fault/retry evidence is incomplete")
    serialized = json.dumps(network, ensure_ascii=False).lower()
    for forbidden in ("password=", "authorization", "idempotency-key", "mssql_sa_password"):
        require(forbidden not in serialized, f"network evidence contains forbidden secret field: {forbidden}")


def validate_junit(path: str) -> None:
    root = et.parse(path).getroot()
    cases = root.findall(".//testcase")
    require(cases, "JUnit report has no test cases")
    joined_names = "\n".join(
        f"{case.attrib.get('classname', '')} {case.attrib.get('name', '')}" for case in cases
    )
    for case_id in EXPECTED_CASES:
        require(case_id in joined_names, f"JUnit report is missing {case_id}")
    require(not root.findall(".//failure"), "JUnit report contains failures")
    require(not root.findall(".//error"), "JUnit report contains errors")
    require(not root.findall(".//skipped"), "JUnit report contains skipped tests")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    subparsers = result.add_subparsers(dest="command", required=True)
    compose = subparsers.add_parser("compose")
    compose.add_argument("--input", required=True)
    compose.add_argument("--expected-commit", required=True)
    compose.add_argument("--expected-project", required=True)
    evidence = subparsers.add_parser("evidence")
    evidence.add_argument("--evidence", required=True)
    evidence.add_argument("--network", required=True)
    evidence.add_argument("--junit", required=True)
    evidence.add_argument("--html", required=True)
    evidence.add_argument("--expected-commit", required=True)
    evidence.add_argument("--expected-run-id", required=True)
    evidence.add_argument("--expected-project", required=True)
    return result


def main() -> int:
    arguments = parser().parse_args()
    try:
        if arguments.command == "compose":
            validate_compose(
                load_json(arguments.input),
                arguments.expected_commit,
                arguments.expected_project,
            )
        else:
            validate_evidence(
                load_json(arguments.evidence),
                load_json(arguments.network),
                arguments.junit,
                arguments.html,
                arguments.expected_commit,
                arguments.expected_run_id,
                arguments.expected_project,
            )
    except (ContractError, json.JSONDecodeError, OSError, et.ParseError) as exception:
        print(f"C-01 E2E contract validation failed: {exception}", file=sys.stderr)
        return 1
    print(f"C-01 E2E {arguments.command} contract validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
