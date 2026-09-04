#!/usr/bin/env python3
"""Run and bind the C-01 normal-provider runtime attestation in Docker.

The runner is deliberately narrow: it proves the normal provider selection and
configuration boundary. It does not enable a C-01 capability and it does not run
the disposable C-01 browser profile.
"""

from __future__ import annotations

import argparse
import base64
import binascii
import dataclasses
import hashlib
import json
import os
import pathlib
import re
import secrets
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from collections.abc import Mapping, Sequence
from typing import Any

_TOOLS_DIRECTORY = pathlib.Path(__file__).resolve().parent
if str(_TOOLS_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(_TOOLS_DIRECTORY))

import validate_c01_normal_provider_contract as contract_validator


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[1]
COMPOSE_FILE = REPOSITORY_ROOT / "infra" / "docker-compose.c01-normal-provider-e2e.yml"
COMPOSE_FILE_EVIDENCE_PATH = "infra/docker-compose.c01-normal-provider-e2e.yml"
RUNTIME_ATTESTATION_PATH = "/run/goymen-attestation/runtime.json"
NEGATIVE_RUNTIME_ATTESTATION_PATH = "/tmp/goymen-normal-provider-negative.json"
LIVENESS_PATH = "/health/live"

SUITE_ID = "C01-NORMAL-PROVIDER-ATTESTATION-V1"
EVIDENCE_SCHEMA_VERSION = "C01-NORMAL-PROVIDER-RUNNER-EVIDENCE-V1"
PROJECT_PREFIX = "goymen-c01-normal"
DATABASE_PREFIX = "GoymenErpC01Normal"

SUITE_LABEL = "com.goymen.e2e.suite"
TESTED_COMMIT_LABEL = "com.goymen.e2e.tested-commit"
COMPOSE_PROJECT_LABEL = "com.docker.compose.project"
COMPOSE_SERVICE_LABEL = "com.docker.compose.service"
OCI_REVISION_LABEL = "org.opencontainers.image.revision"
COMPOSE_ONEOFF_LABEL = "com.docker.compose.oneoff"

NEGATIVE_START_FAILURE_REASON = (
    "C-01 disposable capability 'C01E2e:EnableListCapability' "
    "exact değerini gerektirir."
)

DISPOSABLE_CONFIGURATION_KEYS = (
    "C01E2e__EnableListCapability",
    "GOYMEN_ERP_C01_E2E_DISPOSABLE",
    "GOYMEN_ERP_C01_E2E_TESTED_COMMIT",
    "GOYMEN_ERP_C01_E2E_RUN_ID",
    "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT",
)

_EXPECTED_COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
_RUN_NUMBER_PATTERN = re.compile(r"^[1-9][0-9]{0,19}$")
_CONTAINER_ID_PATTERN = re.compile(r"^[0-9a-f]{64}$")
_IMAGE_ID_PATTERN = re.compile(r"^sha256:[0-9a-f]{64}$")
_HEX_SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
_BASE64_PATTERN = re.compile(r"^[A-Za-z0-9+/]+={0,2}$")
_DISPOSABLE_KEY_CASEFOLDS = frozenset(
    key.casefold() for key in DISPOSABLE_CONFIGURATION_KEYS
)

_EVIDENCE_ROOT_KEYS = frozenset(
    {
        "schemaVersion",
        "suiteId",
        "testedCommit",
        "runId",
        "runAttempt",
        "composeProject",
        "databaseName",
        "composeFile",
        "source",
        "apiContainer",
        "runtimeArtifact",
        "attestation",
        "negativeStarts",
        "cleanup",
    }
)
_API_CONTAINER_KEYS = frozenset(
    {
        "containerId",
        "imageId",
        "ociRevision",
        "running",
        "state",
        "restartCount",
        "publishedPort",
        "liveness",
        "containerLabels",
        "imageLabels",
    }
)
_SOURCE_KEYS = frozenset(
    {
        "gitHead",
        "trackedAndUntrackedClean",
        "ignoredFilesExcludedFromCleanCheck",
        "dockerignoreSha256",
    }
)
_LIVENESS_KEYS = frozenset({"host", "port", "path", "statusCode", "body"})
_RUNTIME_ARTIFACT_KEYS = frozenset(
    {"containerId", "containerPath", "sha256", "contentBase64"}
)
_NEGATIVE_START_KEYS = frozenset(
    {
        "caseId",
        "configurationKey",
        "containerId",
        "imageId",
        "exitCode",
        "failureReason",
        "injectedAlone",
        "attestationAbsent",
        "secretValuesAbsentFromLogs",
        "containerRemoved",
    }
)
_CLEANUP_KEYS = frozenset(
    {
        "downVolumes",
        "removeOrphans",
        "remainingContainers",
        "remainingNetworks",
        "remainingVolumes",
    }
)


class RunnerError(RuntimeError):
    """A fail-closed, value-free runner error."""


def require(condition: bool, error_code: str) -> None:
    if not condition:
        raise RunnerError(error_code)


@dataclasses.dataclass(frozen=True)
class RunIdentity:
    expected_commit: str
    run_id: str
    run_attempt: int
    compose_project: str
    database_name: str


@dataclasses.dataclass(frozen=True, repr=False)
class SecretMaterial:
    sql_password: str
    cursor_signing_key: str

    def __repr__(self) -> str:
        return "SecretMaterial(<redacted>)"


@dataclasses.dataclass(frozen=True)
class NegativeStartContract:
    case_id: str
    configuration_key: str
    injected_value: str
    expected_outcome: str
    expected_failure_reason: str


@dataclasses.dataclass(frozen=True)
class ApiContainerSnapshot:
    container_id: str
    image_id: str
    state: str
    running: bool
    restart_count: int
    published_port: int
    container_labels: dict[str, str]
    image_labels: dict[str, str]


@dataclasses.dataclass(frozen=True)
class SourceSnapshot:
    git_head: str
    dockerignore_sha256: str


@dataclasses.dataclass(frozen=True)
class LivenessSnapshot:
    host: str
    port: int
    path: str
    status_code: int
    body: str


NEGATIVE_START_CONTRACTS = tuple(
    NegativeStartContract(
        case_id=f"C01-NORMAL-NEGATIVE-START-{index:02d}",
        configuration_key=configuration_key,
        injected_value="C01_NEGATIVE_START_KEY_PRESENT",
        expected_outcome="startup-must-fail-before-attestation",
        expected_failure_reason=NEGATIVE_START_FAILURE_REASON,
    )
    for index, configuration_key in enumerate(DISPOSABLE_CONFIGURATION_KEYS, start=1)
)


def derive_run_identity(
    expected_commit: str,
    run_id: str | int,
    run_attempt: str | int,
) -> RunIdentity:
    """Derive injection-safe Docker and database identities from CI run identity."""

    normalized_run_id = str(run_id)
    normalized_attempt = str(run_attempt)
    require(
        isinstance(expected_commit, str)
        and _EXPECTED_COMMIT_PATTERN.fullmatch(expected_commit) is not None,
        "EXPECTED_COMMIT_INVALID",
    )
    require(
        _RUN_NUMBER_PATTERN.fullmatch(normalized_run_id) is not None,
        "RUN_ID_INVALID",
    )
    require(
        _RUN_NUMBER_PATTERN.fullmatch(normalized_attempt) is not None,
        "RUN_ATTEMPT_INVALID",
    )

    attempt_number = int(normalized_attempt)
    return RunIdentity(
        expected_commit=expected_commit,
        run_id=normalized_run_id,
        run_attempt=attempt_number,
        compose_project=f"{PROJECT_PREFIX}-{normalized_run_id}-{attempt_number}",
        database_name=f"{DATABASE_PREFIX}_{normalized_run_id}_{attempt_number}",
    )


def generate_secret_material() -> SecretMaterial:
    """Generate per-run secret material that is never serialized by this runner."""

    return SecretMaterial(
        sql_password=f"Goymen!Aa1-{secrets.token_urlsafe(36)}",
        cursor_signing_key=base64.b64encode(secrets.token_bytes(48)).decode("ascii"),
    )


def sanitized_parent_environment(
    parent_environment: Mapping[str, str] | None = None,
) -> dict[str, str]:
    """Remove all runner-controlled and disposable values from inherited state."""

    source = os.environ if parent_environment is None else parent_environment
    controlled_keys = _DISPOSABLE_KEY_CASEFOLDS | {
        "mssql_sa_password",
        "c01_normal_cursor_signing_key",
        "c01_normal_tested_commit",
        "c01_normal_database_name",
        "c01_normal_sql_port",
        "c01_normal_api_port",
        "git_alternate_object_directories",
        "git_ceiling_directories",
        "git_common_dir",
        "git_dir",
        "git_discovery_across_filesystem",
        "git_index_file",
        "git_object_directory",
        "git_work_tree",
    }
    return {
        key: value
        for key, value in source.items()
        if key.casefold() not in controlled_keys
        and not key.casefold().startswith("git_config_")
    }


def build_child_environment(
    identity: RunIdentity,
    secret_material: SecretMaterial,
    parent_environment: Mapping[str, str] | None = None,
) -> dict[str, str]:
    """Build the positive-profile environment while stripping disposable keys."""

    environment = sanitized_parent_environment(parent_environment)
    environment.update(
        {
            "MSSQL_SA_PASSWORD": secret_material.sql_password,
            "C01_NORMAL_CURSOR_SIGNING_KEY": secret_material.cursor_signing_key,
            "C01_NORMAL_TESTED_COMMIT": identity.expected_commit,
            "C01_NORMAL_DATABASE_NAME": identity.database_name,
            "C01_NORMAL_SQL_PORT": "12433",
            "C01_NORMAL_API_PORT": "16080",
        }
    )
    require(
        {key.casefold() for key in environment}.isdisjoint(
            _DISPOSABLE_KEY_CASEFOLDS
        ),
        "POSITIVE_ENVIRONMENT_CONTAINS_DISPOSABLE_KEY",
    )
    return environment


def _verify_source(
    identity: RunIdentity,
    environment: Mapping[str, str],
) -> SourceSnapshot:
    raw_head = _run_checked(
        [
            "git",
            "-C",
            str(REPOSITORY_ROOT),
            "rev-parse",
            "--verify",
            "HEAD^{commit}",
        ],
        environment,
        "SOURCE_HEAD_READ_FAILED",
        60,
    )
    head_lines = [line.strip() for line in raw_head.splitlines() if line.strip()]
    require(
        head_lines == [identity.expected_commit],
        "SOURCE_HEAD_MISMATCH",
    )
    raw_status = _run_checked(
        [
            "git",
            "-C",
            str(REPOSITORY_ROOT),
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--ignore-submodules=none",
        ],
        environment,
        "SOURCE_STATUS_READ_FAILED",
        60,
    )
    require(_empty_output(raw_status), "SOURCE_WORKTREE_DIRTY")

    dockerignore_path = REPOSITORY_ROOT / ".dockerignore"
    try:
        dockerignore_bytes = dockerignore_path.read_bytes()
    except OSError as exception:
        raise RunnerError("SOURCE_DOCKERIGNORE_READ_FAILED") from exception
    require(bool(dockerignore_bytes), "SOURCE_DOCKERIGNORE_EMPTY")
    return SourceSnapshot(
        git_head=identity.expected_commit,
        dockerignore_sha256=hashlib.sha256(dockerignore_bytes).hexdigest(),
    )


def negative_start_contracts() -> tuple[NegativeStartContract, ...]:
    """Return one isolated negative-start contract for each disposable key."""

    return NEGATIVE_START_CONTRACTS


def build_negative_start_command(
    identity: RunIdentity,
    contract: NegativeStartContract,
) -> list[str]:
    """Build one future container-smoke command with exactly one key mutation.

    This helper intentionally does not execute the command. A higher-level smoke
    can start dependencies, execute each returned command independently, and
    require the API process to fail before an attestation can be accepted.
    """

    matches = [
        candidate
        for candidate in NEGATIVE_START_CONTRACTS
        if candidate == contract
    ]
    require(len(matches) == 1, "NEGATIVE_START_CONTRACT_INVALID")
    case_suffix = contract.case_id.rsplit("-", 1)[-1].lower()
    container_name = f"{identity.compose_project}-negative-{case_suffix}"
    return [
        *_compose_prefix(identity),
        "run",
        "--detach",
        "--no-deps",
        "--name",
        container_name,
        "--env",
        (
            "NormalProviderAttestation__OutputPath="
            f"{NEGATIVE_RUNTIME_ATTESTATION_PATH}"
        ),
        "--env",
        f"{contract.configuration_key}={contract.injected_value}",
        "api",
    ]


def _compose_prefix(identity: RunIdentity) -> list[str]:
    return [
        "docker",
        "compose",
        "--file",
        str(COMPOSE_FILE),
        "--project-name",
        identity.compose_project,
    ]


def _run_raw(
    command: Sequence[str],
    environment: Mapping[str, str],
    error_code: str,
    timeout_seconds: int,
) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(
            list(command),
            cwd=REPOSITORY_ROOT,
            env=dict(environment),
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="strict",
            timeout=timeout_seconds,
        )
    except (OSError, subprocess.SubprocessError, UnicodeError) as exception:
        raise RunnerError(error_code) from exception


def _run_checked(
    command: Sequence[str],
    environment: Mapping[str, str],
    error_code: str,
    timeout_seconds: int,
) -> str:
    completed = _run_raw(command, environment, error_code, timeout_seconds)
    require(completed.returncode == 0, error_code)
    return completed.stdout


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        require(key not in result, "DOCKER_JSON_DUPLICATE_KEY")
        result[key] = value
    return result


def _reject_non_finite_number(_value: str) -> None:
    raise RunnerError("DOCKER_JSON_NON_FINITE_NUMBER")


def _parse_json(raw: str, error_code: str) -> Any:
    try:
        return json.loads(
            raw,
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_non_finite_number,
        )
    except RunnerError:
        raise
    except (json.JSONDecodeError, RecursionError, ValueError) as exception:
        raise RunnerError(error_code) from exception


def _render_and_validate_compose(
    identity: RunIdentity,
    environment: Mapping[str, str],
) -> None:
    raw_model = _run_checked(
        [*_compose_prefix(identity), "config", "--format", "json"],
        environment,
        "COMPOSE_CONFIG_COMMAND_FAILED",
        120,
    )
    model = _parse_json(raw_model, "COMPOSE_CONFIG_JSON_INVALID")
    try:
        contract_validator.validate_compose(
            model,
            identity.expected_commit,
            identity.compose_project,
        )
    except contract_validator.ContractError as exception:
        raise RunnerError("COMPOSE_CONTRACT_FAILED") from exception


def _single_container_id(raw: str, error_code: str) -> str:
    identifiers = [line.strip() for line in raw.splitlines() if line.strip()]
    require(
        len(identifiers) == 1
        and _CONTAINER_ID_PATTERN.fullmatch(identifiers[0]) is not None,
        error_code,
    )
    return identifiers[0]


def _resolve_api_container_id(
    identity: RunIdentity,
    environment: Mapping[str, str],
) -> str:
    raw = _run_checked(
        [*_compose_prefix(identity), "ps", "--quiet", "api"],
        environment,
        "API_CONTAINER_RESOLUTION_FAILED",
        60,
    )
    return _single_container_id(raw, "API_CONTAINER_ID_INVALID")


def _resolve_api_port(
    identity: RunIdentity,
    environment: Mapping[str, str],
) -> int:
    raw = _run_checked(
        [*_compose_prefix(identity), "port", "api", "8080"],
        environment,
        "API_PORT_RESOLUTION_FAILED",
        60,
    )
    lines = [line.strip() for line in raw.splitlines() if line.strip()]
    require(len(lines) == 1, "API_PORT_BINDING_INVALID")
    match = re.fullmatch(r"127\.0\.0\.1:([0-9]{1,5})", lines[0])
    require(match is not None, "API_PORT_BINDING_INVALID")
    port = int(match.group(1))
    require(1 <= port <= 65_535, "API_PORT_BINDING_INVALID")
    return port


def _wait_for_liveness(port: int) -> LivenessSnapshot:
    request = urllib.request.Request(
        f"http://127.0.0.1:{port}{LIVENESS_PATH}",
        headers={"User-Agent": "goymen-c01-normal-provider-runner/1"},
        method="GET",
    )
    for attempt in range(30):
        try:
            with urllib.request.urlopen(request, timeout=1) as response:
                status_code = response.status
                body = response.read(65)
            if status_code == 200 and body == b"Healthy":
                return LivenessSnapshot(
                    host="127.0.0.1",
                    port=port,
                    path=LIVENESS_PATH,
                    status_code=200,
                    body="Healthy",
                )
        except urllib.error.HTTPError as exception:
            exception.close()
        except (urllib.error.URLError, TimeoutError, OSError):
            pass
        if attempt < 29:
            time.sleep(0.5)
    raise RunnerError("API_LIVENESS_FAILED")


def _require_mapping(value: Any, error_code: str) -> dict[str, Any]:
    require(isinstance(value, dict), error_code)
    return value


def _selected_labels(
    labels: Any,
    expected: Mapping[str, str],
    error_code: str,
) -> dict[str, str]:
    label_map = _require_mapping(labels, error_code)
    selected: dict[str, str] = {}
    for key, expected_value in expected.items():
        actual_value = label_map.get(key)
        require(actual_value == expected_value, error_code)
        selected[key] = actual_value
    return selected


def _container_environment_keys(value: Any, error_code: str) -> frozenset[str]:
    require(
        isinstance(value, list)
        and all(isinstance(entry, str) and "=" in entry for entry in value),
        error_code,
    )
    return frozenset(entry.split("=", 1)[0].casefold() for entry in value)


def _published_api_port(container: Mapping[str, Any]) -> int:
    network_settings = _require_mapping(
        container.get("NetworkSettings"),
        "API_CONTAINER_PORT_BINDING_INVALID",
    )
    ports = _require_mapping(
        network_settings.get("Ports"),
        "API_CONTAINER_PORT_BINDING_INVALID",
    )
    bindings = ports.get("8080/tcp")
    require(
        isinstance(bindings, list) and len(bindings) == 1,
        "API_CONTAINER_PORT_BINDING_INVALID",
    )
    binding = _require_mapping(bindings[0], "API_CONTAINER_PORT_BINDING_INVALID")
    host_port = binding.get("HostPort")
    require(
        binding.get("HostIp") == "127.0.0.1"
        and isinstance(host_port, str)
        and host_port.isascii()
        and host_port.isdecimal(),
        "API_CONTAINER_PORT_BINDING_INVALID",
    )
    port = int(host_port)
    require(1 <= port <= 65_535, "API_CONTAINER_PORT_BINDING_INVALID")
    return port


def _inspect_image_labels(
    image_id: str,
    identity: RunIdentity,
    environment: Mapping[str, str],
) -> dict[str, str]:
    raw = _run_checked(
        ["docker", "image", "inspect", image_id],
        environment,
        "API_IMAGE_INSPECT_FAILED",
        60,
    )
    documents = _parse_json(raw, "API_IMAGE_INSPECT_JSON_INVALID")
    require(
        isinstance(documents, list) and len(documents) == 1,
        "API_IMAGE_INSPECT_SHAPE_INVALID",
    )
    image = _require_mapping(documents[0], "API_IMAGE_INSPECT_SHAPE_INVALID")
    require(image.get("Id") == image_id, "API_IMAGE_ID_MISMATCH")
    config = _require_mapping(image.get("Config"), "API_IMAGE_CONFIG_INVALID")
    return _selected_labels(
        config.get("Labels"),
        {
            SUITE_LABEL: SUITE_ID,
            OCI_REVISION_LABEL: identity.expected_commit,
        },
        "API_IMAGE_LABELS_INVALID",
    )


def _inspect_api_container(
    container_id: str,
    identity: RunIdentity,
    environment: Mapping[str, str],
    *,
    inspect_image: bool,
) -> ApiContainerSnapshot:
    raw = _run_checked(
        ["docker", "container", "inspect", container_id],
        environment,
        "API_CONTAINER_INSPECT_FAILED",
        60,
    )
    documents = _parse_json(raw, "API_CONTAINER_INSPECT_JSON_INVALID")
    require(
        isinstance(documents, list) and len(documents) == 1,
        "API_CONTAINER_INSPECT_SHAPE_INVALID",
    )
    container = _require_mapping(
        documents[0],
        "API_CONTAINER_INSPECT_SHAPE_INVALID",
    )
    require(container.get("Id") == container_id, "API_CONTAINER_ID_MISMATCH")

    image_id = container.get("Image")
    require(
        isinstance(image_id, str)
        and _IMAGE_ID_PATTERN.fullmatch(image_id) is not None,
        "API_IMAGE_ID_INVALID",
    )
    state = _require_mapping(container.get("State"), "API_CONTAINER_STATE_INVALID")
    running = state.get("Running")
    state_name = state.get("Status")
    require(
        running is True
        and state_name == "running"
        and state.get("Restarting") is False
        and state.get("Dead") is False,
        "API_CONTAINER_NOT_RUNNING",
    )
    restart_count = container.get("RestartCount")
    require(
        type(restart_count) is int and restart_count == 0,
        "API_CONTAINER_RESTARTED",
    )
    require(
        type(state.get("Pid")) is int and state["Pid"] > 0,
        "API_CONTAINER_PROCESS_MISSING",
    )

    config = _require_mapping(container.get("Config"), "API_CONTAINER_CONFIG_INVALID")
    environment_keys = _container_environment_keys(
        config.get("Env"),
        "API_CONTAINER_ENVIRONMENT_INVALID",
    )
    require(
        environment_keys.isdisjoint(_DISPOSABLE_KEY_CASEFOLDS),
        "API_CONTAINER_DISPOSABLE_KEY_PRESENT",
    )
    container_labels = _selected_labels(
        config.get("Labels"),
        {
            SUITE_LABEL: SUITE_ID,
            TESTED_COMMIT_LABEL: identity.expected_commit,
            COMPOSE_PROJECT_LABEL: identity.compose_project,
            COMPOSE_SERVICE_LABEL: "api",
        },
        "API_CONTAINER_LABELS_INVALID",
    )
    image_labels = (
        _inspect_image_labels(image_id, identity, environment)
        if inspect_image
        else {}
    )
    return ApiContainerSnapshot(
        container_id=container_id,
        image_id=image_id,
        state=state_name,
        running=running,
        restart_count=restart_count,
        published_port=_published_api_port(container),
        container_labels=container_labels,
        image_labels=image_labels,
    )


def _inspect_negative_container(
    container_id: str,
    expected_exit_code: int,
    expected_image_id: str,
    identity: RunIdentity,
    contract: NegativeStartContract,
    environment: Mapping[str, str],
) -> None:
    raw = _run_checked(
        ["docker", "container", "inspect", container_id],
        environment,
        "NEGATIVE_CONTAINER_INSPECT_FAILED",
        60,
    )
    documents = _parse_json(raw, "NEGATIVE_CONTAINER_INSPECT_JSON_INVALID")
    require(
        isinstance(documents, list) and len(documents) == 1,
        "NEGATIVE_CONTAINER_INSPECT_SHAPE_INVALID",
    )
    container = _require_mapping(
        documents[0],
        "NEGATIVE_CONTAINER_INSPECT_SHAPE_INVALID",
    )
    require(container.get("Id") == container_id, "NEGATIVE_CONTAINER_ID_MISMATCH")
    require(container.get("Image") == expected_image_id, "NEGATIVE_IMAGE_ID_MISMATCH")
    require(container.get("RestartCount") == 0, "NEGATIVE_CONTAINER_RESTARTED")

    state = _require_mapping(container.get("State"), "NEGATIVE_CONTAINER_STATE_INVALID")
    require(
        state.get("Running") is False
        and state.get("Status") == "exited"
        and type(state.get("ExitCode")) is int
        and state["ExitCode"] == expected_exit_code
        and expected_exit_code != 0,
        "NEGATIVE_CONTAINER_DID_NOT_FAIL",
    )
    config = _require_mapping(container.get("Config"), "NEGATIVE_CONTAINER_CONFIG_INVALID")
    environment_keys = _container_environment_keys(
        config.get("Env"),
        "NEGATIVE_CONTAINER_ENVIRONMENT_INVALID",
    )
    require(
        environment_keys & _DISPOSABLE_KEY_CASEFOLDS
        == {contract.configuration_key.casefold()},
        "NEGATIVE_CONTAINER_MUTATION_NOT_ISOLATED",
    )
    negative_labels = config.get("Labels")
    _selected_labels(
        negative_labels,
        {
            SUITE_LABEL: SUITE_ID,
            TESTED_COMMIT_LABEL: identity.expected_commit,
            COMPOSE_PROJECT_LABEL: identity.compose_project,
            COMPOSE_SERVICE_LABEL: "api",
        },
        "NEGATIVE_CONTAINER_LABELS_INVALID",
    )
    oneoff_label = _require_mapping(
        negative_labels,
        "NEGATIVE_CONTAINER_LABELS_INVALID",
    ).get(COMPOSE_ONEOFF_LABEL)
    require(
        isinstance(oneoff_label, str) and oneoff_label.casefold() == "true",
        "NEGATIVE_CONTAINER_LABELS_INVALID",
    )


def _run_negative_start(
    identity: RunIdentity,
    contract: NegativeStartContract,
    expected_image_id: str,
    secret_material: SecretMaterial,
    environment: Mapping[str, str],
) -> dict[str, Any]:
    container_id: str | None = None
    result: dict[str, Any] | None = None
    primary_error: BaseException | None = None
    removal_error: RunnerError | None = None
    try:
        try:
            raw_container_id = _run_checked(
                build_negative_start_command(identity, contract),
                environment,
                "NEGATIVE_START_COMMAND_FAILED",
                120,
            )
            container_id = _single_container_id(
                raw_container_id,
                "NEGATIVE_CONTAINER_ID_INVALID",
            )
            raw_exit_code = _run_checked(
                ["docker", "container", "wait", container_id],
                environment,
                "NEGATIVE_CONTAINER_WAIT_FAILED",
                120,
            )
            exit_lines = [
                line.strip()
                for line in raw_exit_code.splitlines()
                if line.strip()
            ]
            require(
                len(exit_lines) == 1
                and exit_lines[0].isascii()
                and exit_lines[0].isdecimal(),
                "NEGATIVE_CONTAINER_EXIT_CODE_INVALID",
            )
            exit_code = int(exit_lines[0])
            require(exit_code != 0, "NEGATIVE_CONTAINER_DID_NOT_FAIL")
            _inspect_negative_container(
                container_id,
                exit_code,
                expected_image_id,
                identity,
                contract,
                environment,
            )

            logs = _run_raw(
                ["docker", "container", "logs", container_id],
                environment,
                "NEGATIVE_CONTAINER_LOGS_FAILED",
                60,
            )
            require(logs.returncode == 0, "NEGATIVE_CONTAINER_LOGS_FAILED")
            combined_logs = f"{logs.stdout}\n{logs.stderr}"
            require(
                contract.expected_failure_reason in combined_logs,
                "NEGATIVE_FAILURE_REASON_MISSING",
            )
            for forbidden_value in (
                secret_material.sql_password,
                secret_material.cursor_signing_key,
                contract.injected_value,
            ):
                require(
                    forbidden_value not in combined_logs,
                    "NEGATIVE_LOG_SECRET_LEAK",
                )

            with tempfile.TemporaryDirectory(
                prefix="goymen-c01-normal-negative-"
            ) as temporary:
                negative_artifact = pathlib.Path(temporary) / "runtime.json"
                copy_result = _run_raw(
                    [
                        "docker",
                        "cp",
                        f"{container_id}:{NEGATIVE_RUNTIME_ATTESTATION_PATH}",
                        str(negative_artifact),
                    ],
                    environment,
                    "NEGATIVE_ATTESTATION_CHECK_FAILED",
                    60,
                )
                require(
                    copy_result.returncode != 0 and not negative_artifact.exists(),
                    "NEGATIVE_ATTESTATION_PRESENT",
                )

            result = {
                "caseId": contract.case_id,
                "configurationKey": contract.configuration_key,
                "containerId": container_id,
                "imageId": expected_image_id,
                "exitCode": exit_code,
                "failureReason": contract.expected_failure_reason,
                "injectedAlone": True,
                "attestationAbsent": True,
                "secretValuesAbsentFromLogs": True,
                "containerRemoved": True,
            }
        except BaseException as exception:
            primary_error = exception
    finally:
        if container_id is not None:
            try:
                removed = _run_raw(
                    ["docker", "container", "rm", "--force", container_id],
                    environment,
                    "NEGATIVE_CONTAINER_REMOVE_FAILED",
                    60,
                )
                require(
                    removed.returncode == 0,
                    "NEGATIVE_CONTAINER_REMOVE_FAILED",
                )
                residue = _run_raw(
                    [
                        "docker",
                        "container",
                        "ls",
                        "--all",
                        "--quiet",
                        "--filter",
                        f"id={container_id}",
                    ],
                    environment,
                    "NEGATIVE_CONTAINER_REMOVE_CHECK_FAILED",
                    60,
                )
                require(
                    residue.returncode == 0 and _empty_output(residue.stdout),
                    "NEGATIVE_CONTAINER_REMOVE_CHECK_FAILED",
                )
            except RunnerError as exception:
                removal_error = exception

    if removal_error is not None:
        raise removal_error from primary_error
    if primary_error is not None:
        raise primary_error
    require(result is not None, "NEGATIVE_START_INTERNAL_STATE_INVALID")
    return result


def _run_negative_start_matrix(
    identity: RunIdentity,
    expected_image_id: str,
    secret_material: SecretMaterial,
    environment: Mapping[str, str],
) -> list[dict[str, Any]]:
    return [
        _run_negative_start(
            identity,
            contract,
            expected_image_id,
            secret_material,
            environment,
        )
        for contract in NEGATIVE_START_CONTRACTS
    ]


def _copy_runtime_artifact(
    container_id: str,
    destination: pathlib.Path,
    environment: Mapping[str, str],
) -> None:
    """Export the tmpfs-backed runtime artifact without Docker's archive API.

    Docker Desktop on Windows can execute and health-check a file mounted on a
    Linux tmpfs while ``docker cp`` reports that same file as missing.  Read a
    Base64 representation inside the container instead, then strictly decode
    and persist the exact bytes on the host.  The command contains no shell and
    the fixed artifact path cannot be influenced by caller input.
    """

    for attempt in range(20):
        completed = _run_raw(
            [
                "docker",
                "container",
                "exec",
                container_id,
                "base64",
                "-w",
                "0",
                RUNTIME_ATTESTATION_PATH,
            ],
            environment,
            "RUNTIME_ARTIFACT_COPY_FAILED",
            60,
        )
        if completed.returncode == 0:
            encoded = completed.stdout
            try:
                artifact_bytes = (
                    base64.b64decode(encoded, validate=True)
                    if len(encoded) % 4 == 0
                    and _BASE64_PATTERN.fullmatch(encoded) is not None
                    else b""
                )
            except (ValueError, binascii.Error):
                artifact_bytes = b""
            if 0 < len(artifact_bytes) <= 65_536:
                try:
                    with destination.open("xb") as stream:
                        stream.write(artifact_bytes)
                        stream.flush()
                        os.fsync(stream.fileno())
                except OSError as exception:
                    raise RunnerError("RUNTIME_ARTIFACT_COPY_FAILED") from exception
                if destination.is_file() and not destination.is_symlink():
                    return
        if attempt < 19:
            time.sleep(0.5)
    raise RunnerError("RUNTIME_ARTIFACT_COPY_FAILED")


def _read_and_validate_attestation(
    path: pathlib.Path,
    secret_material: SecretMaterial,
) -> tuple[dict[str, Any], str, str]:
    try:
        raw_bytes = path.read_bytes()
        raw = raw_bytes.decode("utf-8", errors="strict")
    except (OSError, UnicodeError) as exception:
        raise RunnerError("RUNTIME_ARTIFACT_READ_FAILED") from exception

    for value in (
        secret_material.sql_password,
        secret_material.cursor_signing_key,
    ):
        require(value.encode("utf-8") not in raw_bytes, "RUNTIME_ARTIFACT_SECRET_LEAK")

    attestation = _parse_json(raw, "RUNTIME_ATTESTATION_JSON_INVALID")
    try:
        contract_validator.validate_attestation(attestation)
    except contract_validator.ContractError as exception:
        raise RunnerError("RUNTIME_ATTESTATION_CONTRACT_FAILED") from exception
    require(isinstance(attestation, dict), "RUNTIME_ATTESTATION_ROOT_INVALID")
    return (
        attestation,
        hashlib.sha256(raw_bytes).hexdigest(),
        base64.b64encode(raw_bytes).decode("ascii"),
    )


def _empty_output(raw: str) -> bool:
    return not any(line.strip() for line in raw.splitlines())


def _project_residue_commands(identity: RunIdentity) -> tuple[list[str], ...]:
    return (
        [
            "docker",
            "container",
            "ls",
            "--all",
            "--quiet",
            "--filter",
            f"label={COMPOSE_PROJECT_LABEL}={identity.compose_project}",
        ],
        [
            "docker",
            "network",
            "ls",
            "--quiet",
            "--filter",
            f"label={COMPOSE_PROJECT_LABEL}={identity.compose_project}",
        ],
        [
            "docker",
            "volume",
            "ls",
            "--quiet",
            "--filter",
            f"label={COMPOSE_PROJECT_LABEL}={identity.compose_project}",
        ],
    )


def _assert_no_preexisting_project_resources(
    identity: RunIdentity,
    environment: Mapping[str, str],
) -> None:
    for command in _project_residue_commands(identity):
        output = _run_checked(
            command,
            environment,
            "PREFLIGHT_RESIDUE_CHECK_FAILED",
            60,
        )
        require(_empty_output(output), "PREFLIGHT_RESIDUE_DETECTED")


def _cleanup(
    identity: RunIdentity,
    environment: Mapping[str, str],
) -> dict[str, Any]:
    down_failed = False
    residue_check_failed = False
    residue_detected = False

    try:
        down = _run_raw(
            [*_compose_prefix(identity), "down", "-v", "--remove-orphans"],
            environment,
            "CLEANUP_DOWN_FAILED",
            300,
        )
        down_failed = down.returncode != 0
    except RunnerError:
        down_failed = True

    for command in _project_residue_commands(identity):
        try:
            completed = _run_raw(
                command,
                environment,
                "CLEANUP_RESIDUE_CHECK_FAILED",
                60,
            )
            if completed.returncode != 0:
                residue_check_failed = True
            elif not _empty_output(completed.stdout):
                residue_detected = True
        except RunnerError:
            residue_check_failed = True

    require(not residue_detected, "CLEANUP_RESIDUE_DETECTED")
    require(not residue_check_failed, "CLEANUP_RESIDUE_CHECK_FAILED")
    require(not down_failed, "CLEANUP_DOWN_FAILED")
    return {
        "downVolumes": True,
        "removeOrphans": True,
        "remainingContainers": 0,
        "remainingNetworks": 0,
        "remainingVolumes": 0,
    }


def _build_evidence(
    identity: RunIdentity,
    source: SourceSnapshot,
    snapshot: ApiContainerSnapshot,
    liveness: LivenessSnapshot,
    attestation: dict[str, Any],
    attestation_sha256: str,
    attestation_base64: str,
    negative_starts: list[dict[str, Any]],
    cleanup: dict[str, Any],
) -> dict[str, Any]:
    return {
        "schemaVersion": EVIDENCE_SCHEMA_VERSION,
        "suiteId": SUITE_ID,
        "testedCommit": identity.expected_commit,
        "runId": identity.run_id,
        "runAttempt": identity.run_attempt,
        "composeProject": identity.compose_project,
        "databaseName": identity.database_name,
        "composeFile": COMPOSE_FILE_EVIDENCE_PATH,
        "source": {
            "gitHead": source.git_head,
            "trackedAndUntrackedClean": True,
            "ignoredFilesExcludedFromCleanCheck": True,
            "dockerignoreSha256": source.dockerignore_sha256,
        },
        "apiContainer": {
            "containerId": snapshot.container_id,
            "imageId": snapshot.image_id,
            "ociRevision": snapshot.image_labels[OCI_REVISION_LABEL],
            "running": snapshot.running,
            "state": snapshot.state,
            "restartCount": snapshot.restart_count,
            "publishedPort": snapshot.published_port,
            "liveness": {
                "host": liveness.host,
                "port": liveness.port,
                "path": liveness.path,
                "statusCode": liveness.status_code,
                "body": liveness.body,
            },
            "containerLabels": snapshot.container_labels,
            "imageLabels": snapshot.image_labels,
        },
        "runtimeArtifact": {
            "containerId": snapshot.container_id,
            "containerPath": RUNTIME_ATTESTATION_PATH,
            "sha256": attestation_sha256,
            "contentBase64": attestation_base64,
        },
        "attestation": attestation,
        "negativeStarts": negative_starts,
        "cleanup": cleanup,
    }


def validate_evidence_bundle(
    evidence: Any,
    identity: RunIdentity,
) -> None:
    """Validate the exact local runner bundle before it becomes an artifact."""

    require(isinstance(evidence, dict), "EVIDENCE_ROOT_INVALID")
    require(set(evidence) == _EVIDENCE_ROOT_KEYS, "EVIDENCE_FIELDS_NOT_EXACT")
    require(
        evidence["schemaVersion"] == EVIDENCE_SCHEMA_VERSION,
        "EVIDENCE_SCHEMA_VERSION_INVALID",
    )
    require(evidence["suiteId"] == SUITE_ID, "EVIDENCE_SUITE_INVALID")
    require(evidence["testedCommit"] == identity.expected_commit, "EVIDENCE_COMMIT_INVALID")
    require(evidence["runId"] == identity.run_id, "EVIDENCE_RUN_ID_INVALID")
    require(
        type(evidence["runAttempt"]) is int
        and evidence["runAttempt"] == identity.run_attempt,
        "EVIDENCE_ATTEMPT_INVALID",
    )
    require(
        evidence["composeProject"] == identity.compose_project,
        "EVIDENCE_PROJECT_INVALID",
    )
    require(
        evidence["databaseName"] == identity.database_name,
        "EVIDENCE_DATABASE_INVALID",
    )
    require(
        evidence["composeFile"] == COMPOSE_FILE_EVIDENCE_PATH,
        "EVIDENCE_COMPOSE_FILE_INVALID",
    )

    source = _require_mapping(evidence["source"], "EVIDENCE_SOURCE_INVALID")
    require(set(source) == _SOURCE_KEYS, "EVIDENCE_SOURCE_FIELDS_NOT_EXACT")
    require(source["gitHead"] == identity.expected_commit, "EVIDENCE_SOURCE_HEAD_INVALID")
    require(
        source["trackedAndUntrackedClean"] is True,
        "EVIDENCE_SOURCE_CLEANLINESS_INVALID",
    )
    require(
        source["ignoredFilesExcludedFromCleanCheck"] is True,
        "EVIDENCE_SOURCE_IGNORED_POLICY_INVALID",
    )
    dockerignore_sha256 = source["dockerignoreSha256"]
    require(
        isinstance(dockerignore_sha256, str)
        and _HEX_SHA256_PATTERN.fullmatch(dockerignore_sha256) is not None,
        "EVIDENCE_DOCKERIGNORE_SHA256_INVALID",
    )
    try:
        current_dockerignore_sha256 = hashlib.sha256(
            (REPOSITORY_ROOT / ".dockerignore").read_bytes()
        ).hexdigest()
    except OSError as exception:
        raise RunnerError("EVIDENCE_DOCKERIGNORE_READ_FAILED") from exception
    require(
        dockerignore_sha256 == current_dockerignore_sha256,
        "EVIDENCE_DOCKERIGNORE_SHA256_INVALID",
    )

    api = _require_mapping(evidence["apiContainer"], "EVIDENCE_API_INVALID")
    require(set(api) == _API_CONTAINER_KEYS, "EVIDENCE_API_FIELDS_NOT_EXACT")
    require(
        isinstance(api["containerId"], str)
        and _CONTAINER_ID_PATTERN.fullmatch(api["containerId"]) is not None,
        "EVIDENCE_CONTAINER_ID_INVALID",
    )
    require(
        isinstance(api["imageId"], str)
        and _IMAGE_ID_PATTERN.fullmatch(api["imageId"]) is not None,
        "EVIDENCE_IMAGE_ID_INVALID",
    )
    require(api["ociRevision"] == identity.expected_commit, "EVIDENCE_OCI_REVISION_INVALID")
    require(api["running"] is True and api["state"] == "running", "EVIDENCE_STATE_INVALID")
    require(
        type(api["restartCount"]) is int and api["restartCount"] == 0,
        "EVIDENCE_RESTART_COUNT_INVALID",
    )
    require(
        type(api["publishedPort"]) is int
        and 1 <= api["publishedPort"] <= 65_535,
        "EVIDENCE_API_PORT_INVALID",
    )
    liveness = _require_mapping(api["liveness"], "EVIDENCE_LIVENESS_INVALID")
    require(set(liveness) == _LIVENESS_KEYS, "EVIDENCE_LIVENESS_FIELDS_NOT_EXACT")
    require(
        liveness
        == {
            "host": "127.0.0.1",
            "port": api["publishedPort"],
            "path": LIVENESS_PATH,
            "statusCode": 200,
            "body": "Healthy",
        },
        "EVIDENCE_LIVENESS_INVALID",
    )
    require(
        api["containerLabels"]
        == {
            SUITE_LABEL: SUITE_ID,
            TESTED_COMMIT_LABEL: identity.expected_commit,
            COMPOSE_PROJECT_LABEL: identity.compose_project,
            COMPOSE_SERVICE_LABEL: "api",
        },
        "EVIDENCE_CONTAINER_LABELS_INVALID",
    )
    require(
        api["imageLabels"]
        == {
            SUITE_LABEL: SUITE_ID,
            OCI_REVISION_LABEL: identity.expected_commit,
        },
        "EVIDENCE_IMAGE_LABELS_INVALID",
    )

    artifact = _require_mapping(
        evidence["runtimeArtifact"],
        "EVIDENCE_RUNTIME_ARTIFACT_INVALID",
    )
    require(
        set(artifact) == _RUNTIME_ARTIFACT_KEYS,
        "EVIDENCE_RUNTIME_ARTIFACT_FIELDS_NOT_EXACT",
    )
    require(
        artifact["containerId"] == api["containerId"],
        "EVIDENCE_RUNTIME_CONTAINER_BINDING_INVALID",
    )
    require(
        artifact["containerPath"] == RUNTIME_ATTESTATION_PATH,
        "EVIDENCE_RUNTIME_PATH_INVALID",
    )
    require(
        isinstance(artifact["sha256"], str)
        and _HEX_SHA256_PATTERN.fullmatch(artifact["sha256"]) is not None,
        "EVIDENCE_RUNTIME_SHA256_INVALID",
    )
    encoded_content = artifact["contentBase64"]
    require(
        isinstance(encoded_content, str) and bool(encoded_content),
        "EVIDENCE_RUNTIME_CONTENT_INVALID",
    )
    try:
        runtime_bytes = base64.b64decode(encoded_content, validate=True)
    except (ValueError, TypeError, binascii.Error) as exception:
        raise RunnerError("EVIDENCE_RUNTIME_CONTENT_INVALID") from exception
    require(
        0 < len(runtime_bytes) <= 65_536
        and base64.b64encode(runtime_bytes).decode("ascii") == encoded_content,
        "EVIDENCE_RUNTIME_CONTENT_INVALID",
    )
    require(
        hashlib.sha256(runtime_bytes).hexdigest() == artifact["sha256"],
        "EVIDENCE_RUNTIME_HASH_BINDING_INVALID",
    )
    try:
        runtime_text = runtime_bytes.decode("utf-8", errors="strict")
    except UnicodeError as exception:
        raise RunnerError("EVIDENCE_RUNTIME_CONTENT_INVALID") from exception
    parsed_runtime_attestation = _parse_json(
        runtime_text,
        "EVIDENCE_RUNTIME_CONTENT_INVALID",
    )
    require(
        parsed_runtime_attestation == evidence["attestation"],
        "EVIDENCE_RUNTIME_ATTESTATION_BINDING_INVALID",
    )
    try:
        contract_validator.validate_attestation(parsed_runtime_attestation)
    except contract_validator.ContractError as exception:
        raise RunnerError("EVIDENCE_ATTESTATION_INVALID") from exception

    negative_starts = evidence["negativeStarts"]
    require(
        isinstance(negative_starts, list)
        and len(negative_starts) == len(NEGATIVE_START_CONTRACTS),
        "EVIDENCE_NEGATIVE_STARTS_INVALID",
    )
    negative_container_ids: set[str] = set()
    for result, contract in zip(
        negative_starts,
        NEGATIVE_START_CONTRACTS,
        strict=True,
    ):
        negative = _require_mapping(result, "EVIDENCE_NEGATIVE_START_INVALID")
        require(
            set(negative) == _NEGATIVE_START_KEYS,
            "EVIDENCE_NEGATIVE_START_FIELDS_NOT_EXACT",
        )
        require(
            negative["caseId"] == contract.case_id
            and negative["configurationKey"] == contract.configuration_key,
            "EVIDENCE_NEGATIVE_START_IDENTITY_INVALID",
        )
        negative_container_id = negative["containerId"]
        require(
            isinstance(negative_container_id, str)
            and _CONTAINER_ID_PATTERN.fullmatch(negative_container_id) is not None
            and negative_container_id != api["containerId"]
            and negative_container_id not in negative_container_ids,
            "EVIDENCE_NEGATIVE_CONTAINER_ID_INVALID",
        )
        negative_container_ids.add(negative_container_id)
        require(
            negative["imageId"] == api["imageId"],
            "EVIDENCE_NEGATIVE_IMAGE_BINDING_INVALID",
        )
        require(
            type(negative["exitCode"]) is int and negative["exitCode"] != 0,
            "EVIDENCE_NEGATIVE_EXIT_CODE_INVALID",
        )
        require(
            negative["failureReason"] == contract.expected_failure_reason,
            "EVIDENCE_NEGATIVE_REASON_INVALID",
        )
        for proof_field in (
            "injectedAlone",
            "attestationAbsent",
            "secretValuesAbsentFromLogs",
            "containerRemoved",
        ):
            require(
                negative[proof_field] is True,
                "EVIDENCE_NEGATIVE_PROOF_INVALID",
            )

    cleanup = _require_mapping(evidence["cleanup"], "EVIDENCE_CLEANUP_INVALID")
    require(set(cleanup) == _CLEANUP_KEYS, "EVIDENCE_CLEANUP_FIELDS_NOT_EXACT")
    require(
        cleanup.get("downVolumes") is True
        and cleanup.get("removeOrphans") is True
        and type(cleanup.get("remainingContainers")) is int
        and cleanup["remainingContainers"] == 0
        and type(cleanup.get("remainingNetworks")) is int
        and cleanup["remainingNetworks"] == 0
        and type(cleanup.get("remainingVolumes")) is int
        and cleanup["remainingVolumes"] == 0,
        "EVIDENCE_CLEANUP_INVALID",
    )


def _write_evidence(
    output_path: pathlib.Path,
    evidence: dict[str, Any],
    secret_material: SecretMaterial,
) -> None:
    serialized = json.dumps(evidence, ensure_ascii=False, indent=2) + "\n"
    for value in (
        secret_material.sql_password,
        secret_material.cursor_signing_key,
    ):
        require(value not in serialized, "EVIDENCE_SECRET_LEAK")

    try:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            newline="\n",
            dir=output_path.parent,
            prefix=f".{output_path.name}.",
            suffix=".tmp",
            delete=False,
        ) as temporary:
            temporary_path = pathlib.Path(temporary.name)
            temporary.write(serialized)
            temporary.flush()
            os.fsync(temporary.fileno())
        temporary_path.replace(output_path)
    except OSError as exception:
        try:
            if "temporary_path" in locals() and temporary_path.exists():
                temporary_path.unlink()
        except OSError:
            pass
        raise RunnerError("EVIDENCE_WRITE_FAILED") from exception


def run_attestation(
    expected_commit: str,
    run_id: str | int,
    run_attempt: str | int,
    output_path: str | pathlib.Path,
) -> dict[str, Any]:
    """Run the positive profile, validate its runtime proof, and clean all residue."""

    identity = derive_run_identity(expected_commit, run_id, run_attempt)
    destination = pathlib.Path(output_path)
    require(not destination.exists(), "EVIDENCE_OUTPUT_ALREADY_EXISTS")
    require(COMPOSE_FILE.is_file(), "COMPOSE_FILE_MISSING")

    source_environment = sanitized_parent_environment()
    secret_material = generate_secret_material()
    environment = build_child_environment(
        identity,
        secret_material,
        source_environment,
    )
    evidence_without_cleanup: tuple[
        SourceSnapshot,
        ApiContainerSnapshot,
        LivenessSnapshot,
        dict[str, Any],
        str,
        str,
        list[dict[str, Any]],
    ] | None = None
    primary_error: BaseException | None = None

    cleanup_error: RunnerError | None = None
    cleanup_evidence: dict[str, Any] | None = None
    try:
        try:
            source_snapshot = _verify_source(identity, source_environment)
            _render_and_validate_compose(identity, environment)
            _assert_no_preexisting_project_resources(identity, environment)
            _run_checked(
                [
                    *_compose_prefix(identity),
                    "up",
                    "--detach",
                    "--build",
                    "--wait",
                    "--wait-timeout",
                    "300",
                    "api",
                ],
                environment,
                "COMPOSE_UP_FAILED",
                1_800,
            )

            container_id = _resolve_api_container_id(identity, environment)
            published_port = _resolve_api_port(identity, environment)
            first_snapshot = _inspect_api_container(
                container_id,
                identity,
                environment,
                inspect_image=True,
            )
            require(
                first_snapshot.published_port == published_port,
                "API_PORT_CONTAINER_BINDING_INVALID",
            )
            liveness = _wait_for_liveness(published_port)
            with tempfile.TemporaryDirectory(
                prefix="goymen-c01-normal-attestation-"
            ) as temporary:
                runtime_path = pathlib.Path(temporary) / "runtime.json"
                _copy_runtime_artifact(container_id, runtime_path, environment)
                (
                    attestation,
                    attestation_sha256,
                    attestation_base64,
                ) = _read_and_validate_attestation(runtime_path, secret_material)

            rebound_container_id = _resolve_api_container_id(identity, environment)
            require(rebound_container_id == container_id, "RUNTIME_CONTAINER_CHANGED")
            second_snapshot = _inspect_api_container(
                container_id,
                identity,
                environment,
                inspect_image=False,
            )
            require(
                dataclasses.replace(
                    second_snapshot,
                    image_labels=first_snapshot.image_labels,
                )
                == first_snapshot,
                "RUNTIME_CONTAINER_CHANGED",
            )
            negative_starts = _run_negative_start_matrix(
                identity,
                first_snapshot.image_id,
                secret_material,
                environment,
            )
            final_container_id = _resolve_api_container_id(identity, environment)
            require(final_container_id == container_id, "RUNTIME_CONTAINER_CHANGED")
            final_snapshot = _inspect_api_container(
                container_id,
                identity,
                environment,
                inspect_image=False,
            )
            require(
                dataclasses.replace(
                    final_snapshot,
                    image_labels=first_snapshot.image_labels,
                )
                == first_snapshot,
                "RUNTIME_CONTAINER_CHANGED",
            )
            final_liveness = _wait_for_liveness(published_port)
            require(final_liveness == liveness, "API_LIVENESS_CHANGED")
            evidence_without_cleanup = (
                source_snapshot,
                first_snapshot,
                liveness,
                attestation,
                attestation_sha256,
                attestation_base64,
                negative_starts,
            )
        except BaseException as exception:
            primary_error = exception
    finally:
        try:
            cleanup_evidence = _cleanup(identity, environment)
        except RunnerError as exception:
            cleanup_error = exception

    if cleanup_error is not None:
        raise cleanup_error from primary_error
    if primary_error is not None:
        raise primary_error
    require(
        evidence_without_cleanup is not None and cleanup_evidence is not None,
        "RUNNER_INTERNAL_STATE_INVALID",
    )

    (
        source_snapshot,
        snapshot,
        liveness,
        attestation,
        attestation_sha256,
        attestation_base64,
        negative_starts,
    ) = evidence_without_cleanup
    evidence = _build_evidence(
        identity,
        source_snapshot,
        snapshot,
        liveness,
        attestation,
        attestation_sha256,
        attestation_base64,
        negative_starts,
        cleanup_evidence,
    )
    validate_evidence_bundle(evidence, identity)
    _write_evidence(destination, evidence, secret_material)
    return evidence


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--expected-commit", required=True)
    result.add_argument("--run-id", required=True)
    result.add_argument("--run-attempt", required=True)
    result.add_argument("--output", required=True)
    return result


def main(argv: Sequence[str] | None = None) -> int:
    arguments = parser().parse_args(argv)
    try:
        run_attestation(
            arguments.expected_commit,
            arguments.run_id,
            arguments.run_attempt,
            arguments.output,
        )
    except RunnerError as exception:
        print(f"C-01 normal-provider runner failed: {exception}", file=sys.stderr)
        return 1

    print("C-01 normal-provider runner passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
