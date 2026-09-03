#!/usr/bin/env python3
"""Fail-closed validator for the C-01 normal-provider runtime attestation."""

from __future__ import annotations

import argparse
import base64
import binascii
import json
import pathlib
import re
import sys
import uuid
from collections.abc import Sequence
from typing import Any


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[1]
EXPECTED_SCHEMA_VERSION = "C01-NORMAL-PROVIDER-ATTESTATION-V1"
EXPECTED_PROVIDER_ORIGIN = "normal"
EXPECTED_SUITE_LABEL = "C01-NORMAL-PROVIDER-ATTESTATION-V1"
EXPECTED_PROVIDER_TYPE = (
    "GoymenERP.Modules.Counterparty.Infrastructure.Shell."
    "CounterpartyShellCapabilityProvider"
)
EXPECTED_KEYS = frozenset(
    {
        "schemaVersion",
        "providerOrigin",
        "resolvedProviderCount",
        "resolvedProviderType",
        "overrideKeysPresent",
        "processInstanceId",
        "processId",
        "registryRevision",
    }
)
DISPOSABLE_CONFIGURATION_KEYS = (
    "C01E2e__EnableListCapability",
    "GOYMEN_ERP_C01_E2E_DISPOSABLE",
    "GOYMEN_ERP_C01_E2E_TESTED_COMMIT",
    "GOYMEN_ERP_C01_E2E_RUN_ID",
    "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT",
)
EXPECTED_SQLSERVER_IMAGE = (
    "mcr.microsoft.com/mssql/server@"
    "sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89"
)
EXPECTED_ATTESTATION_OUTPUT_PATH = "/run/goymen-attestation/runtime.json"
EXPECTED_API_DOCKERFILE = "src/backend/GoymenERP.Api/Dockerfile"
EXPECTED_VOLUME_NAME = "c01-normal-sqlserver-data"

EXPECTED_API_HEALTHCHECK = {
    "test": [
        "CMD-SHELL",
        "test -s /run/goymen-attestation/runtime.json "
        "&& grep -q ':1F90 ' /proc/net/tcp /proc/net/tcp6",
    ],
    "timeout": "2s",
    "interval": "2s",
    "retries": 30,
    "start_period": "5s",
}
EXPECTED_SQL_HEALTHCHECK = {
    "test": [
        "CMD-SHELL",
        "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa "
        '-P "$${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" || exit 1',
    ],
    "timeout": "5s",
    "interval": "10s",
    "retries": 18,
    "start_period": "20s",
}

_SUITE_LABEL_KEY = "com.goymen.e2e.suite"
_TESTED_COMMIT_LABEL_KEY = "com.goymen.e2e.tested-commit"
_OCI_REVISION_LABEL_KEY = "org.opencontainers.image.revision"
_EXPECTED_COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
_EXPECTED_PROJECT_PATTERN = re.compile(
    r"^goymen-c01-normal-([1-9][0-9]{0,19})-([1-9][0-9]{0,19})$"
)
_DISPOSABLE_ENVIRONMENT_KEYS = frozenset(
    key.casefold() for key in DISPOSABLE_CONFIGURATION_KEYS
)
_ROOT_KEYS = frozenset({"name", "networks", "services", "volumes"})
_SERVICE_KEYS = frozenset({"api", "sqlserver"})
_API_SERVICE_KEYS = frozenset(
    {
        "build",
        "command",
        "depends_on",
        "entrypoint",
        "environment",
        "healthcheck",
        "labels",
        "networks",
        "ports",
        "tmpfs",
    }
)
_SQL_SERVICE_KEYS = frozenset(
    {
        "command",
        "entrypoint",
        "environment",
        "healthcheck",
        "image",
        "labels",
        "networks",
        "ports",
        "volumes",
    }
)
_API_BUILD_KEYS = frozenset({"context", "dockerfile", "labels"})
_PORT_KEYS = frozenset({"mode", "host_ip", "target", "published", "protocol"})
_API_ENVIRONMENT_KEYS = frozenset(
    {
        "ASPNETCORE_ENVIRONMENT",
        "DB_PROVIDER",
        "DB_CONNECTION_STRING",
        "Counterparty__CursorSigningKey",
        "NormalProviderAttestation__OutputPath",
        "BootstrapIdentity__HeaderName",
        "BootstrapIdentity__Users__0__UserId",
        "BootstrapIdentity__Users__0__DisplayName",
    }
)
_SQL_ENVIRONMENT_KEYS = frozenset(
    {"ACCEPT_EULA", "MSSQL_PID", "MSSQL_SA_PASSWORD"}
)

_PROCESS_INSTANCE_ID_PATTERN = re.compile(
    r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
)
_REGISTRY_REVISION_PATTERN = re.compile(r"^sha256:([A-Za-z0-9_-]{43})$")


class ContractError(ValueError):
    """A safe, value-free normal-provider contract failure."""


def require(condition: bool, error_code: str) -> None:
    if not condition:
        raise ContractError(error_code)


def _reject_duplicate_json_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        require(key not in result, "ATTESTATION_JSON_DUPLICATE_KEY")
        result[key] = value
    return result


def _reject_non_finite_number(_value: str) -> None:
    raise ContractError("ATTESTATION_JSON_NON_FINITE_NUMBER")


def _parse_json(raw: str) -> Any:
    try:
        return json.loads(
            raw,
            object_pairs_hook=_reject_duplicate_json_keys,
            parse_constant=_reject_non_finite_number,
        )
    except ContractError:
        raise
    except json.JSONDecodeError as exception:
        raise ContractError("ATTESTATION_JSON_MALFORMED") from exception
    except RecursionError as exception:
        raise ContractError("ATTESTATION_JSON_NESTING_INVALID") from exception
    except ValueError as exception:
        raise ContractError("ATTESTATION_JSON_NUMBER_INVALID") from exception


def load_attestation(input_path: str | pathlib.Path) -> Any:
    """Load an attestation from a UTF-8 path, or from stdin when input is ``-``."""

    try:
        if str(input_path) == "-":
            raw = sys.stdin.read()
        else:
            raw = pathlib.Path(input_path).read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exception:
        raise ContractError("ATTESTATION_INPUT_READ_FAILED") from exception
    return _parse_json(raw)


def _is_canonical_process_instance_id(value: Any) -> bool:
    if not isinstance(value, str) or _PROCESS_INSTANCE_ID_PATTERN.fullmatch(value) is None:
        return False
    try:
        parsed = uuid.UUID(value)
    except ValueError:
        return False
    return parsed.int != 0 and str(parsed) == value


def _is_canonical_registry_revision(value: Any) -> bool:
    if not isinstance(value, str):
        return False
    match = _REGISTRY_REVISION_PATTERN.fullmatch(value)
    if match is None:
        return False

    encoded_digest = match.group(1)
    try:
        digest = base64.b64decode(
            encoded_digest + "=",
            altchars=b"-_",
            validate=True,
        )
    except (ValueError, binascii.Error):
        return False

    canonical_digest = base64.urlsafe_b64encode(digest).rstrip(b"=").decode("ascii")
    return len(digest) == 32 and canonical_digest == encoded_digest


def validate_attestation(attestation: Any) -> None:
    """Validate one exact, versioned normal-provider attestation object."""

    require(isinstance(attestation, dict), "ATTESTATION_ROOT_TYPE_INVALID")
    require(set(attestation) == EXPECTED_KEYS, "ATTESTATION_FIELDS_NOT_EXACT")
    require(
        attestation["schemaVersion"] == EXPECTED_SCHEMA_VERSION,
        "ATTESTATION_SCHEMA_VERSION_INVALID",
    )
    require(
        attestation["providerOrigin"] == EXPECTED_PROVIDER_ORIGIN,
        "ATTESTATION_PROVIDER_ORIGIN_INVALID",
    )
    require(
        type(attestation["resolvedProviderCount"]) is int
        and attestation["resolvedProviderCount"] == 1,
        "ATTESTATION_PROVIDER_COUNT_INVALID",
    )
    require(
        attestation["resolvedProviderType"] == EXPECTED_PROVIDER_TYPE,
        "ATTESTATION_PROVIDER_TYPE_INVALID",
    )
    require(
        isinstance(attestation["overrideKeysPresent"], list)
        and not attestation["overrideKeysPresent"],
        "ATTESTATION_OVERRIDE_KEYS_PRESENT",
    )
    require(
        _is_canonical_process_instance_id(attestation["processInstanceId"]),
        "ATTESTATION_PROCESS_INSTANCE_ID_INVALID",
    )
    require(
        type(attestation["processId"]) is int and attestation["processId"] > 0,
        "ATTESTATION_PROCESS_ID_INVALID",
    )
    require(
        _is_canonical_registry_revision(attestation["registryRevision"]),
        "ATTESTATION_REGISTRY_REVISION_INVALID",
    )


def _validate_compose_port(
    service: dict[str, Any],
    expected_target: int,
    expected_published: str,
    error_code: str,
) -> None:
    ports = service.get("ports")
    require(isinstance(ports, list) and len(ports) == 1, error_code)
    port = ports[0]
    require(isinstance(port, dict) and set(port) == _PORT_KEYS, error_code)
    require(
        port
        == {
            "mode": "ingress",
            "host_ip": "127.0.0.1",
            "target": expected_target,
            "published": expected_published,
            "protocol": "tcp",
        },
        error_code,
    )


def _validate_compose_service_labels(
    service: dict[str, Any],
    expected_commit: str,
    error_code: str,
) -> None:
    labels = service.get("labels")
    require(
        labels
        == {
            _SUITE_LABEL_KEY: EXPECTED_SUITE_LABEL,
            _TESTED_COMMIT_LABEL_KEY: expected_commit,
        },
        error_code,
    )


def _canonical_base64_key(value: Any) -> bool:
    if not isinstance(value, str) or not value or value != value.strip():
        return False
    try:
        decoded = base64.b64decode(value, validate=True)
    except (ValueError, binascii.Error):
        return False
    return len(decoded) >= 32 and base64.b64encode(decoded).decode("ascii") == value


def _database_name_from_project(expected_project: str) -> str:
    match = _EXPECTED_PROJECT_PATTERN.fullmatch(expected_project)
    require(match is not None, "COMPOSE_EXPECTED_PROJECT_INVALID")
    run_id, run_attempt = match.groups()
    return f"GoymenErpC01Normal_{run_id}_{run_attempt}"


def _validate_api_environment(
    api: dict[str, Any],
    expected_database: str,
    sql_password: str,
) -> None:
    environment = api.get("environment")
    require(isinstance(environment, dict), "COMPOSE_API_ENVIRONMENT_INVALID")
    require(
        all(isinstance(key, str) for key in environment),
        "COMPOSE_API_ENVIRONMENT_INVALID",
    )
    environment_keys = {key.casefold() for key in environment}
    require(
        environment_keys.isdisjoint(_DISPOSABLE_ENVIRONMENT_KEYS),
        "COMPOSE_DISPOSABLE_ENVIRONMENT_KEY_PRESENT",
    )
    require(
        set(environment) == _API_ENVIRONMENT_KEYS,
        "COMPOSE_API_ENVIRONMENT_FIELDS_INVALID",
    )
    require(
        environment["ASPNETCORE_ENVIRONMENT"] == "Testing",
        "COMPOSE_API_RUNTIME_ENVIRONMENT_INVALID",
    )
    require(
        environment["DB_PROVIDER"] == "SqlServer",
        "COMPOSE_API_DATABASE_PROVIDER_INVALID",
    )
    expected_connection = (
        f"Server=sqlserver,1433;Database={expected_database};"
        f"User Id=sa;Password={sql_password};"
        "Encrypt=True;TrustServerCertificate=True"
    )
    require(
        environment["DB_CONNECTION_STRING"] == expected_connection,
        "COMPOSE_API_DATABASE_CONNECTION_INVALID",
    )
    require(
        _canonical_base64_key(environment["Counterparty__CursorSigningKey"]),
        "COMPOSE_API_CURSOR_KEY_INVALID",
    )
    require(
        environment["NormalProviderAttestation__OutputPath"]
        == EXPECTED_ATTESTATION_OUTPUT_PATH,
        "COMPOSE_ATTESTATION_OUTPUT_PATH_INVALID",
    )
    require(
        environment["BootstrapIdentity__HeaderName"] == "X-Development-User"
        and environment["BootstrapIdentity__Users__0__UserId"]
        == "group-bootstrap-admin"
        and environment["BootstrapIdentity__Users__0__DisplayName"]
        == "C01 Normal Provider Administrator",
        "COMPOSE_API_BOOTSTRAP_IDENTITY_INVALID",
    )


def _validate_sql_environment(sqlserver: dict[str, Any]) -> str:
    environment = sqlserver.get("environment")
    require(isinstance(environment, dict), "COMPOSE_SQL_ENVIRONMENT_INVALID")
    require(
        set(environment) == _SQL_ENVIRONMENT_KEYS,
        "COMPOSE_SQL_ENVIRONMENT_FIELDS_INVALID",
    )
    password = environment["MSSQL_SA_PASSWORD"]
    require(
        isinstance(password, str)
        and bool(password)
        and password == password.strip()
        and ";" not in password
        and all(character.isprintable() for character in password),
        "COMPOSE_SQL_PASSWORD_INVALID",
    )
    require(
        environment["ACCEPT_EULA"] == "Y"
        and environment["MSSQL_PID"] == "Developer",
        "COMPOSE_SQL_ENVIRONMENT_INVALID",
    )
    return password


def _validate_compose_topology(
    model: dict[str, Any],
    api: dict[str, Any],
    sqlserver: dict[str, Any],
    expected_project: str,
    expected_commit: str,
) -> None:
    expected_networks = {
        "default": {
            "name": f"{expected_project}_default",
            "ipam": {},
        }
    }
    require(model["networks"] == expected_networks, "COMPOSE_NETWORK_TOPOLOGY_INVALID")
    require(
        api["networks"] == {"default": None}
        and sqlserver["networks"] == {"default": None},
        "COMPOSE_SERVICE_NETWORK_TOPOLOGY_INVALID",
    )

    expected_volume_labels = {
        _SUITE_LABEL_KEY: EXPECTED_SUITE_LABEL,
        _TESTED_COMMIT_LABEL_KEY: expected_commit,
    }
    expected_volumes = {
        EXPECTED_VOLUME_NAME: {
            "name": f"{expected_project}_{EXPECTED_VOLUME_NAME}",
            "labels": expected_volume_labels,
        }
    }
    require(model["volumes"] == expected_volumes, "COMPOSE_VOLUME_TOPOLOGY_INVALID")
    require(
        sqlserver["volumes"]
        == [
            {
                "type": "volume",
                "source": EXPECTED_VOLUME_NAME,
                "target": "/var/opt/mssql",
                "volume": {},
            }
        ],
        "COMPOSE_SQL_VOLUME_TOPOLOGY_INVALID",
    )


def validate_compose(
    model: Any,
    expected_commit: str,
    expected_project: str,
) -> None:
    """Validate the rendered normal-provider Docker Compose model."""

    require(
        isinstance(expected_commit, str)
        and _EXPECTED_COMMIT_PATTERN.fullmatch(expected_commit) is not None,
        "COMPOSE_EXPECTED_COMMIT_INVALID",
    )
    require(
        isinstance(expected_project, str)
        and _EXPECTED_PROJECT_PATTERN.fullmatch(expected_project) is not None,
        "COMPOSE_EXPECTED_PROJECT_INVALID",
    )
    expected_database = _database_name_from_project(expected_project)
    require(isinstance(model, dict), "COMPOSE_ROOT_TYPE_INVALID")
    require(set(model) == _ROOT_KEYS, "COMPOSE_ROOT_FIELDS_INVALID")
    require(model.get("name") == expected_project, "COMPOSE_PROJECT_MISMATCH")

    services = model.get("services")
    require(isinstance(services, dict), "COMPOSE_SERVICES_INVALID")
    require(set(services) == _SERVICE_KEYS, "COMPOSE_SERVICE_INVENTORY_INVALID")
    api = services.get("api")
    sqlserver = services.get("sqlserver")
    require(isinstance(api, dict), "COMPOSE_API_SERVICE_INVALID")
    require(isinstance(sqlserver, dict), "COMPOSE_SQL_SERVICE_INVALID")
    require(set(api) == _API_SERVICE_KEYS, "COMPOSE_API_FIELDS_INVALID")
    require(set(sqlserver) == _SQL_SERVICE_KEYS, "COMPOSE_SQL_FIELDS_INVALID")
    require(
        api["command"] is None
        and api["entrypoint"] is None
        and sqlserver["command"] is None
        and sqlserver["entrypoint"] is None,
        "COMPOSE_PROCESS_OVERRIDE_INVALID",
    )

    _validate_compose_port(api, 8080, "16080", "COMPOSE_API_PORT_INVALID")
    _validate_compose_port(sqlserver, 1433, "12433", "COMPOSE_SQL_PORT_INVALID")

    _validate_compose_service_labels(
        api,
        expected_commit,
        "COMPOSE_API_LABELS_INVALID",
    )
    _validate_compose_service_labels(
        sqlserver,
        expected_commit,
        "COMPOSE_SQL_LABELS_INVALID",
    )

    build = api.get("build")
    require(isinstance(build, dict), "COMPOSE_API_BUILD_INVALID")
    require(set(build) == _API_BUILD_KEYS, "COMPOSE_API_BUILD_FIELDS_INVALID")
    build_context = build["context"]
    require(
        isinstance(build_context, str)
        and pathlib.Path(build_context) == REPOSITORY_ROOT,
        "COMPOSE_API_BUILD_CONTEXT_INVALID",
    )
    require(
        build["dockerfile"] == EXPECTED_API_DOCKERFILE,
        "COMPOSE_API_DOCKERFILE_INVALID",
    )
    build_labels = build["labels"]
    require(
        build_labels
        == {
            _SUITE_LABEL_KEY: EXPECTED_SUITE_LABEL,
            _OCI_REVISION_LABEL_KEY: expected_commit,
        },
        "COMPOSE_API_BUILD_LABELS_INVALID",
    )

    require(
        sqlserver.get("image") == EXPECTED_SQLSERVER_IMAGE,
        "COMPOSE_SQL_IMAGE_INVALID",
    )
    dependencies = api.get("depends_on")
    require(isinstance(dependencies, dict), "COMPOSE_API_DEPENDENCY_INVALID")
    sql_dependency = dependencies.get("sqlserver")
    require(isinstance(sql_dependency, dict), "COMPOSE_API_DEPENDENCY_INVALID")
    require(
        set(dependencies) == {"sqlserver"}
        and set(sql_dependency) == {"condition", "required"}
        and sql_dependency["condition"] == "service_healthy"
        and sql_dependency["required"] is True,
        "COMPOSE_API_DEPENDENCY_INVALID",
    )

    require(
        api["tmpfs"] == [f"{EXPECTED_ATTESTATION_OUTPUT_PATH.rsplit('/', 1)[0]}:mode=0700"],
        "COMPOSE_API_TMPFS_INVALID",
    )
    require(api["healthcheck"] == EXPECTED_API_HEALTHCHECK, "COMPOSE_API_HEALTHCHECK_INVALID")
    require(
        sqlserver["healthcheck"] == EXPECTED_SQL_HEALTHCHECK,
        "COMPOSE_SQL_HEALTHCHECK_INVALID",
    )

    sql_password = _validate_sql_environment(sqlserver)
    _validate_api_environment(api, expected_database, sql_password)
    _validate_compose_topology(
        model,
        api,
        sqlserver,
        expected_project,
        expected_commit,
    )


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument(
        "--input",
        default="-",
        help="Attestation JSON path, or '-' for stdin (default: '-').",
    )
    return result


def compose_parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(
        prog=f"{pathlib.Path(sys.argv[0]).name} compose",
        description="Validate the rendered C-01 normal-provider Compose model.",
    )
    result.add_argument(
        "--input",
        default="-",
        help="Rendered Compose JSON path, or '-' for stdin (default: '-').",
    )
    result.add_argument("--expected-commit", required=True)
    result.add_argument("--expected-project", required=True)
    return result


def main(argv: Sequence[str] | None = None) -> int:
    command_line = list(sys.argv[1:] if argv is None else argv)
    compose_mode = bool(command_line) and command_line[0] == "compose"
    if compose_mode:
        arguments = compose_parser().parse_args(command_line[1:])
    else:
        arguments = parser().parse_args(command_line)

    try:
        document = load_attestation(arguments.input)
        if compose_mode:
            validate_compose(
                document,
                arguments.expected_commit,
                arguments.expected_project,
            )
        else:
            validate_attestation(document)
    except ContractError as exception:
        subject = "compose" if compose_mode else "attestation"
        print(
            f"C-01 normal-provider {subject} validation failed: {exception}",
            file=sys.stderr,
        )
        return 1

    subject = "compose" if compose_mode else "attestation"
    print(f"C-01 normal-provider {subject} validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
