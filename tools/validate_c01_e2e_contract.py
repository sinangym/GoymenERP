#!/usr/bin/env python3
"""Fail-closed validator for the disposable C-01 browser acceptance contract."""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import hashlib
import json
import pathlib
import re
import sys
import unicodedata
import xml.etree.ElementTree as et
from typing import Any


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[1]
SQLSERVER_IMAGE = (
    "mcr.microsoft.com/mssql/server@"
    "sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89"
)
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
ALLOWED_NETWORK_OUTCOMES = REQUIRED_FAULT_OUTCOMES | {
    "real-200",
    "real-401",
    "real-403",
}
NETWORK_ROOT_KEYS = {
    "schemaVersion",
    "testedCommit",
    "authorizedCompanyId",
    "foreignRecordAbsent",
    "redacted",
    "requests",
}
NETWORK_REQUEST_KEYS = {
    "method",
    "path",
    "ordinal",
    "outcome",
    "retryOrdinal",
}
C01_ARTIFACT_UPLOAD_ALLOWLIST = (
    "c01-e2e-api-preflight.json",
    "c01-e2e-evidence.json",
    "c01-e2e-fixture-evidence.json",
    "c01-e2e-junit.xml",
    "c01-e2e-network-summary.json",
    "c01-e2e-post-browser-sql-evidence.json",
    "c01-e2e-html-report/index.html",
    "container-logs/compose-ps.txt",
)
# CROSS-LANE ADDITION (Claude/K2), mirroring the SAB entry Codex added in #188.
# Needed for SAFETY, not convenience: validate_artifact_privacy scans ONLY the
# files this allowlist selects, so an activation report uploaded without an entry
# here would ship unscanned — and that report is produced by the one suite that
# holds the disposable SQL connection string. Additive: a missing file is skipped.
ACTIVATION_ARTIFACT_UPLOAD_ALLOWLIST = (
    "activation-junit.xml",
)
# PR A owns the server/evidence producer and its exact privacy boundary. PR B
# must add this path to ACTIVATION_ARTIFACT_UPLOAD_ALLOWLIST and to the workflow's
# exact upload list in the same commit that produces the browser half. Until
# then, the source foundation is not activation-acceptance evidence.
ACTIVATION_SERVER_EVIDENCE_RELATIVE_PATH = (
    "c01-server-evidence/c01-activation-api-requests.jsonl"
)

SAB_ARTIFACT_UPLOAD_ALLOWLIST = (
    "sab-junit.xml",
    "sab-html-report/index.html",
)
ARTIFACT_UPLOAD_ALLOWLIST = (
    C01_ARTIFACT_UPLOAD_ALLOWLIST
    + SAB_ARTIFACT_UPLOAD_ALLOWLIST
    + ACTIVATION_ARTIFACT_UPLOAD_ALLOWLIST
)
ARTIFACT_FORBIDDEN_PATTERNS = (
    (
        "cursor query",
        re.compile(rb"(?:\?|&(?:amp;)?)cursor\s*=", re.IGNORECASE),
    ),
    (
        "authorization credential",
        re.compile(
            rb"\bauthorization\s*[:=]\s*(?:bearer|basic)\s+[A-Za-z0-9+/_=.\-]+",
            re.IGNORECASE,
        ),
    ),
    (
        "idempotency key",
        re.compile(rb"\bidempotency-key\s*[:=]\s*[^\s<\"']+", re.IGNORECASE),
    ),
    (
        "generated SQL password",
        re.compile(rb"C01_[0-9a-f]{32}_Aa1!", re.IGNORECASE),
    ),
    (
        "connection string with password",
        re.compile(
            rb"(?:server|data\s+source)=[^;\r\n]+;[^\r\n]*(?:password|pwd)=[^;\s<\"']+",
            re.IGNORECASE,
        ),
    ),
    (
        "secret configuration assignment",
        re.compile(
            rb"(?:MSSQL_SA_PASSWORD|C01_E2E_CURSOR_SIGNING_KEY|"
            rb"Counterparty__CursorSigningKey|GOYMEN_ERP_C01_E2E_SQLSERVER_CONNECTION|"
            rb"DB_CONNECTION_STRING)\s*[:=]\s*[^\s<\"']+",
            re.IGNORECASE,
        ),
    ),
)


class ContractError(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ContractError(message)


def require_exact_keys(value: dict[str, Any], expected: set[str], name: str) -> None:
    actual = set(value)
    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    require(
        not missing and not extra,
        f"{name} keys mismatch; missing={missing}, extra={extra}",
    )


def _catalog_sha256(value: str) -> str:
    normalized = unicodedata.normalize("NFC", value.strip()).casefold()
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def _split_connection_string_components(connection_string: str) -> list[str] | None:
    components: list[str] = []
    current: list[str] = []
    quote: str | None = None
    index = 0

    while index < len(connection_string):
        character = connection_string[index]
        if quote is not None:
            current.append(character)
            if character == quote:
                if index + 1 < len(connection_string) and connection_string[index + 1] == quote:
                    current.append(connection_string[index + 1])
                    index += 1
                else:
                    quote = None
        elif character in {'"', "'"}:
            quote = character
            current.append(character)
        elif character == ";":
            components.append("".join(current))
            current = []
        else:
            current.append(character)
        index += 1

    if quote is not None:
        return None

    components.append("".join(current))
    return components


def _actual_catalog_sha256(connection_string: object) -> str:
    if not isinstance(connection_string, str):
        return "missing"

    components = _split_connection_string_components(connection_string)
    if components is None:
        return "ambiguous"

    catalogs: set[str] = set()
    for component in components:
        key, separator, value = component.partition("=")
        normalized_key = " ".join(key.strip().casefold().split())
        if separator and normalized_key in {"database", "initial catalog"} and value.strip():
            catalogs.add(unicodedata.normalize("NFC", value.strip()).casefold())

    if not catalogs:
        return "missing"
    if len(catalogs) != 1:
        return "ambiguous"
    return hashlib.sha256(next(iter(catalogs)).encode("utf-8")).hexdigest()


def _disposable_database_diagnostic(expected_database: str, connection_string: object) -> str:
    return (
        "API disposable DB drift "
        "[rule=api_disposable_database;service=api;binding=DB_CONNECTION_STRING;"
        f"expectedCatalogSha256={_catalog_sha256(expected_database)};"
        f"actualCatalogSha256={_actual_catalog_sha256(connection_string)}]"
    )


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
    require(sql.get("image") == SQLSERVER_IMAGE, "SQL image digest drift")
    require(bool(sql.get("healthcheck")), "SQL healthcheck is required")

    api = services["api"]
    api_build = api.get("build", {})
    require(
        pathlib.Path(str(api_build.get("context", ""))).resolve() == REPOSITORY_ROOT,
        "API build context drift",
    )
    require(
        api_build.get("dockerfile") == "src/backend/GoymenERP.Api/Dockerfile",
        "API Dockerfile drift",
    )
    api_environment = api.get("environment", {})
    require(api_environment.get("ASPNETCORE_ENVIRONMENT") == "Testing", "API environment drift")
    require(api_environment.get("DB_PROVIDER") == "SqlServer", "API DB provider drift")
    expected_database = "GoymenErpC01_" + "_".join(expected_project.split("-")[-2:])
    require(
        f"Database={expected_database}" in api_environment.get("DB_CONNECTION_STRING", ""),
        _disposable_database_diagnostic(
            expected_database, api_environment.get("DB_CONNECTION_STRING", "")),
    )
    require(
        api_environment.get("C01E2e__EnableListCapability") == "true",
        "API disposable capability flag drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_DISPOSABLE") == "CONFIRMED",
        "API disposable attestation drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_TESTED_COMMIT") == expected_commit,
        "API disposable source SHA drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_RUN_ID") == expected_project.split("-")[-2],
        "API disposable run id drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT") == expected_project,
        "API disposable compose project drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_DATABASE_NAME") == expected_database,
        "API request-evidence database drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_REQUEST_EVIDENCE_PATH")
        == "/evidence/c01-activation-api-requests.jsonl",
        "API request-evidence path drift",
    )
    expected_container = f"{expected_project}-api"
    require(api.get("hostname") == expected_container, "API evidence hostname drift")
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_EVIDENCE_ISSUER")
        == "goymenerp-api-c01-testing-v1",
        "API request-evidence issuer drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_CONTAINER_IDENTITY") == expected_container,
        "API request-evidence container identity drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_PROCESS_ID") == "1",
        "API request-evidence process id drift",
    )
    require(
        api_environment.get("GOYMEN_ERP_C01_E2E_PROCESS_START_IDENTITY")
        == f"{expected_container}-process-1",
        "API request-evidence process start identity drift",
    )
    volumes = api.get("volumes")
    require(isinstance(volumes, list) and len(volumes) == 1,
            "API request-evidence mount drift")
    evidence_mount = volumes[0]
    require(isinstance(evidence_mount, dict),
            "API request-evidence mount must be normalized")
    allowed_mount_keys = {"type", "source", "target", "bind", "read_only"}
    require(
        {"type", "source", "target"}.issubset(evidence_mount)
        and set(evidence_mount).issubset(allowed_mount_keys),
        "API request-evidence mount shape drift",
    )
    require(evidence_mount.get("type") == "bind", "API request-evidence mount type drift")
    require(
        pathlib.Path(str(evidence_mount.get("source", ""))).resolve()
        == (REPOSITORY_ROOT / "artifacts" / "c01-server-evidence").resolve(),
        "API request-evidence mount source drift",
    )
    require(evidence_mount.get("target") == "/evidence",
            "API request-evidence mount target drift")
    require(evidence_mount.get("bind", {}) in ({}, {"create_host_path": True}),
            "API request-evidence bind options drift")
    require(
        evidence_mount.get("read_only", False) is False,
        "API request-evidence mount must remain writable",
    )
    require(
        api_environment.get("BootstrapIdentity__HeaderName") == "X-Development-User",
        "API bootstrap header drift",
    )
    require(
        api_environment.get("BootstrapIdentity__Users__0__UserId")
        == "group-bootstrap-admin"
        and api_environment.get("BootstrapIdentity__Users__0__DisplayName")
        == "C01 E2E Group Administrator",
        "API authorized bootstrap identity drift",
    )
    require(
        api_environment.get("BootstrapIdentity__Users__1__UserId") == "company-a-user"
        and api_environment.get("BootstrapIdentity__Users__1__DisplayName")
        == "C01 E2E Unauthorized User",
        "API unauthorized bootstrap identity drift",
    )
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
        require(
            pathlib.Path(str(build.get("context", ""))).resolve() == REPOSITORY_ROOT,
            f"{name} build context drift",
        )
        require(build.get("dockerfile") == "src/frontend/goymen-erp-web/Dockerfile", f"{name} Dockerfile drift")
        args = build.get("args", {})
        require(args.get("VITE_BOOTSTRAP_IDENTITY_ENABLED") == "true", f"{name} bootstrap flag drift")
        require(args.get("VITE_DEV_USER") == identity, f"{name} identity profile drift")
        build_labels = build.get("labels", {})
        require(
            build_labels.get("org.opencontainers.image.revision") == expected_commit,
            f"{name} image source SHA label mismatch",
        )

    api_build_labels = api_build.get("labels", {})
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
    require_exact_keys(
        evidence,
        {
            "schemaVersion",
            "suiteId",
            "repository",
            "testedCommit",
            "runId",
            "composeProject",
            "fixture",
            "browser",
            "viewports",
            "cases",
            "skipped",
            "startedAtUtc",
            "completedAtUtc",
        },
        "evidence",
    )
    require(evidence.get("schemaVersion") == 1, "evidence schemaVersion mismatch")
    require(evidence.get("suiteId") == "C01-REAL-BROWSER-E2E-V1", "suiteId mismatch")
    require(evidence.get("repository") == "sinangym/GoymenERP", "repository mismatch")
    require(evidence.get("testedCommit") == expected_commit, "testedCommit mismatch")
    require(str(evidence.get("runId")) == expected_run_id, "runId mismatch")
    require(evidence.get("composeProject") == expected_project, "composeProject mismatch")

    fixture = evidence.get("fixture", {})
    require(isinstance(fixture, dict), "fixture evidence must be an object")
    require_exact_keys(
        fixture,
        {
            "id",
            "schemaVersion",
            "byteLength",
            "sha256",
            "partyCount",
            "accountCount",
            "sequenceNextValue",
        },
        "fixture evidence",
    )
    require(fixture.get("id") == FIXTURE_ID, "fixture id mismatch")
    require(fixture.get("schemaVersion") == 1, "fixture schemaVersion mismatch")
    require(fixture.get("byteLength") == 29_817, "fixture byteLength mismatch")
    require(fixture.get("sha256") == FIXTURE_SHA256, "fixture SHA-256 mismatch")
    require(fixture.get("partyCount") == 54, "fixture partyCount mismatch")
    require(fixture.get("accountCount") == 55, "fixture accountCount mismatch")
    require(fixture.get("sequenceNextValue") == 55, "fixture sequence mismatch")

    browser = evidence.get("browser", {})
    require(isinstance(browser, dict), "browser evidence must be an object")
    require_exact_keys(browser, {"name", "version"}, "browser evidence")
    require(browser.get("name") == "chromium", "browser must be chromium")
    require(isinstance(browser.get("version"), str) and browser["version"].strip(), "browser version missing")
    require(evidence.get("viewports") == EXPECTED_VIEWPORTS, "viewport matrix mismatch")
    cases = evidence.get("cases")
    require(isinstance(cases, list) and len(cases) == 10, "exactly ten cases are required")
    require(all(isinstance(case, dict) for case in cases), "every case must be an object")
    for case in cases:
        require_exact_keys(case, {"id", "status"}, "browser case")
    case_ids = [case.get("id") for case in cases]
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
    require_exact_keys(network, NETWORK_ROOT_KEYS, "network evidence")
    require(network.get("schemaVersion") == 1, "network schemaVersion mismatch")
    require(network.get("testedCommit") == expected_commit, "network testedCommit mismatch")
    require(network.get("authorizedCompanyId") == GDC_COMPANY_ID, "network company mismatch")
    require(network.get("foreignRecordAbsent") is True, "foreign record absence is unproven")
    require(network.get("redacted") is True, "network evidence must be redacted")
    requests = network.get("requests")
    require(isinstance(requests, list) and requests, "network requests are required")
    observed_paths: set[str] = set()
    successful_paths: set[str] = set()
    outcomes: set[str] = set()
    ordinals: list[int] = []
    for request in requests:
        require(isinstance(request, dict), "network request entry must be an object")
        require_exact_keys(request, NETWORK_REQUEST_KEYS, "network request")
        require(request.get("method") == "GET", "C-01 browser suite must be read-only")
        path = request.get("path")
        require(isinstance(path, str), "network path is invalid")
        require(
            "?" not in path and "cursor" not in path.lower(),
            "network path must be query-redacted",
        )
        require(path in REQUIRED_REAL_PATHS, "network path is outside the C-01 allowlist")
        require(
            type(request.get("ordinal")) is int and request["ordinal"] > 0,
            "request ordinal invalid",
        )
        require(
            type(request.get("retryOrdinal")) is int and request["retryOrdinal"] >= 0,
            "retry ordinal invalid",
        )
        outcome = request.get("outcome")
        require(
            outcome in ALLOWED_NETWORK_OUTCOMES,
            "network outcome is outside the C-01 allowlist",
        )
        if outcome in REQUIRED_FAULT_OUTCOMES:
            require(
                path == "/api/v1/counterparties",
                f"{outcome} must be bound to the Counterparty read endpoint",
            )
        if outcome == "retry-200":
            require(request["retryOrdinal"] > 0, "retry-200 must have retryOrdinal > 0")
        else:
            require(request["retryOrdinal"] == 0, f"{outcome} must have retryOrdinal 0")
        if path != "/api/v1/counterparties":
            require(
                outcome == "real-200",
                f"{path} must be a successful real bootstrap read",
            )
        observed_paths.add(path)
        outcomes.add(outcome)
        ordinals.append(request["ordinal"])
        if outcome == "real-200":
            successful_paths.add(path)
    require(REQUIRED_REAL_PATHS <= observed_paths, "required real API paths are missing")
    require(
        REQUIRED_REAL_PATHS <= successful_paths,
        "required real API success paths are missing",
    )
    require(
        REQUIRED_FAULT_OUTCOMES <= outcomes,
        "continuation fault/retry evidence is incomplete",
    )
    require(
        sorted(ordinals) == list(range(1, len(ordinals) + 1)),
        "network ordinals must be unique and contiguous from 1",
    )
    serialized = json.dumps(network, ensure_ascii=False).lower()
    for forbidden in ("password=", "authorization", "idempotency-key", "mssql_sa_password"):
        require(forbidden not in serialized, f"network evidence contains forbidden secret field: {forbidden}")


def validate_junit(path: str) -> None:
    root = et.parse(path).getroot()
    cases = root.findall(".//testcase")
    require(
        len(cases) == len(EXPECTED_CASES),
        "JUnit report must contain exactly ten test cases",
    )
    case_ids: list[str] = []
    for case in cases:
        name = case.attrib.get("name", "")
        match = re.match(r"^(B-C01-\d{3})(?:\s|$)", name)
        require(
            match is not None,
            "each JUnit testcase name must start with one exact B-C01 ID",
        )
        case_ids.append(match.group(1))
    require(sorted(case_ids) == EXPECTED_CASES, "JUnit case IDs must be exact and unique")
    require(not root.findall(".//failure"), "JUnit report contains failures")
    require(not root.findall(".//error"), "JUnit report contains errors")
    require(not root.findall(".//skipped"), "JUnit report contains skipped tests")


def artifact_upload_paths(root: str | pathlib.Path) -> list[pathlib.Path]:
    artifact_root = pathlib.Path(root).resolve()
    require(artifact_root.is_dir(), "artifact root is missing")
    selected: list[pathlib.Path] = []
    for relative in ARTIFACT_UPLOAD_ALLOWLIST:
        candidate = artifact_root / relative
        if not candidate.exists():
            continue
        require(candidate.is_file(), f"artifact allowlist entry is not a file: {relative}")
        require(not candidate.is_symlink(), f"artifact allowlist entry is a symlink: {relative}")
        require(
            candidate.resolve().is_relative_to(artifact_root),
            f"artifact allowlist entry escapes root: {relative}",
        )
        selected.append(candidate)
    require(selected, "artifact upload allowlist selected no files")
    return selected


def validate_artifact_privacy(
    root: str | pathlib.Path,
    forbidden_values: tuple[str, ...] | list[str] = (),
) -> None:
    artifact_root = pathlib.Path(root).resolve()
    selected = artifact_upload_paths(artifact_root)
    runtime_needles: list[bytes] = []
    for index, value in enumerate(forbidden_values):
        require(
            isinstance(value, str) and len(value.encode("utf-8")) >= 8,
            f"runtime forbidden value {index} is missing or too short",
        )
        runtime_needles.append(value.encode("utf-8"))
    for path in selected:
        content = path.read_bytes()
        relative = path.relative_to(artifact_root).as_posix()
        for label, pattern in ARTIFACT_FORBIDDEN_PATTERNS:
            require(
                pattern.search(content) is None,
                f"artifact privacy violation ({label}): {relative}",
            )
        for index, needle in enumerate(runtime_needles):
            require(
                needle not in content,
                f"artifact privacy violation (runtime secret {index}): {relative}",
            )


def validate_activation_server_evidence_privacy(
    root: str | pathlib.Path,
    forbidden_values: tuple[str, ...] | list[str] = (),
) -> None:
    artifact_root = pathlib.Path(root).resolve()
    require(artifact_root.is_dir(), "artifact root is missing")
    candidate = artifact_root / ACTIVATION_SERVER_EVIDENCE_RELATIVE_PATH
    require(candidate.is_file(), "activation server evidence is missing")
    require(not candidate.is_symlink(), "activation server evidence is a symlink")
    require(candidate.resolve().is_relative_to(artifact_root),
            "activation server evidence escapes root")
    content = candidate.read_bytes()
    for label, pattern in ARTIFACT_FORBIDDEN_PATTERNS:
        require(pattern.search(content) is None,
                f"activation server evidence privacy violation ({label})")
    for index, value in enumerate(forbidden_values):
        require(isinstance(value, str) and len(value.encode("utf-8")) >= 8,
                f"runtime forbidden value {index} is missing or too short")
        require(value.encode("utf-8") not in content,
                f"activation server evidence privacy violation (runtime secret {index})")


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
    privacy = subparsers.add_parser("privacy")
    privacy.add_argument("--root", required=True)
    privacy.add_argument("--forbidden-value", action="append", default=[])
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
        elif arguments.command == "evidence":
            validate_evidence(
                load_json(arguments.evidence),
                load_json(arguments.network),
                arguments.junit,
                arguments.html,
                arguments.expected_commit,
                arguments.expected_run_id,
                arguments.expected_project,
            )
        else:
            validate_artifact_privacy(arguments.root, arguments.forbidden_value)
    except (ContractError, json.JSONDecodeError, OSError, et.ParseError) as exception:
        print(f"C-01 E2E contract validation failed: {exception}", file=sys.stderr)
        return 1
    print(f"C-01 E2E {arguments.command} contract validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
