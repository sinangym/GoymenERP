from __future__ import annotations

import base64
import copy
import hashlib
import io
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from typing import Any
from unittest import mock


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPOSITORY_ROOT / "tools"))

import run_c01_normal_provider_attestation as runner  # noqa: E402


EXPECTED_COMMIT = "0123456789abcdef0123456789abcdef01234567"
RUN_ID = "204001"
RUN_ATTEMPT = 3
EXPECTED_PROJECT = "goymen-c01-normal-204001-3"
EXPECTED_DATABASE = "GoymenErpC01Normal_204001_3"
CONTAINER_ID = "a" * 64
OTHER_CONTAINER_ID = "b" * 64
IMAGE_ID = "sha256:" + "c" * 64
NEGATIVE_CONTAINER_IDS = tuple(str(index) * 64 for index in range(1, 6))
API_PORT = 16080
SQL_SECRET = "Goymen!Aa1-unit-test-sql-secret-never-serialize"
CURSOR_SECRET = base64.b64encode(b"c" * 48).decode("ascii")


def valid_attestation() -> dict[str, Any]:
    revision = base64.urlsafe_b64encode(bytes(range(32))).rstrip(b"=").decode("ascii")
    return {
        "schemaVersion": "C01-NORMAL-PROVIDER-ATTESTATION-V1",
        "providerOrigin": "normal",
        "resolvedProviderCount": 1,
        "resolvedProviderType": (
            "GoymenERP.Modules.Counterparty.Infrastructure.Shell."
            "CounterpartyShellCapabilityProvider"
        ),
        "overrideKeysPresent": [],
        "processInstanceId": "53d45729-80f7-4bda-bf7a-b1ec49b22d27",
        "processId": 42,
        "registryRevision": f"sha256:{revision}",
    }


def valid_compose_model() -> dict[str, Any]:
    service_labels = {
        runner.SUITE_LABEL: runner.SUITE_ID,
        runner.TESTED_COMMIT_LABEL: EXPECTED_COMMIT,
    }
    return {
        "name": EXPECTED_PROJECT,
        "networks": {
            "default": {
                "name": f"{EXPECTED_PROJECT}_default",
                "ipam": {},
            }
        },
        "services": {
            "api": {
                "build": {
                    "context": str(REPOSITORY_ROOT),
                    "dockerfile": "src/backend/GoymenERP.Api/Dockerfile",
                    "labels": {
                        runner.SUITE_LABEL: runner.SUITE_ID,
                        runner.OCI_REVISION_LABEL: EXPECTED_COMMIT,
                    },
                },
                "command": None,
                "depends_on": {
                    "sqlserver": {
                        "condition": "service_healthy",
                        "required": True,
                    }
                },
                "entrypoint": None,
                "environment": {
                    "ASPNETCORE_ENVIRONMENT": "Testing",
                    "DB_PROVIDER": "SqlServer",
                    "DB_CONNECTION_STRING": (
                        "Server=sqlserver,1433;"
                        f"Database={EXPECTED_DATABASE};"
                        f"User Id=sa;Password={SQL_SECRET};"
                        "Encrypt=True;TrustServerCertificate=True"
                    ),
                    "Counterparty__CursorSigningKey": CURSOR_SECRET,
                    "NormalProviderAttestation__OutputPath": (
                        runner.RUNTIME_ATTESTATION_PATH
                    ),
                    "BootstrapIdentity__HeaderName": "X-Development-User",
                    "BootstrapIdentity__Users__0__UserId": (
                        "group-bootstrap-admin"
                    ),
                    "BootstrapIdentity__Users__0__DisplayName": (
                        "C01 Normal Provider Administrator"
                    ),
                },
                "healthcheck": copy.deepcopy(
                    runner.contract_validator.EXPECTED_API_HEALTHCHECK
                ),
                "labels": copy.deepcopy(service_labels),
                "networks": {"default": None},
                "ports": [
                    {
                        "mode": "ingress",
                        "host_ip": "127.0.0.1",
                        "protocol": "tcp",
                        "published": "16080",
                        "target": 8080,
                    }
                ],
                "tmpfs": ["/run/goymen-attestation:mode=0700"],
            },
            "sqlserver": {
                "command": None,
                "entrypoint": None,
                "environment": {
                    "ACCEPT_EULA": "Y",
                    "MSSQL_PID": "Developer",
                    "MSSQL_SA_PASSWORD": SQL_SECRET,
                },
                "healthcheck": copy.deepcopy(
                    runner.contract_validator.EXPECTED_SQL_HEALTHCHECK
                ),
                "image": (
                    "mcr.microsoft.com/mssql/server@"
                    "sha256:ba4c8329f48fb8f02e1416be6a930ebf"
                    "d71268caee78aa985f3af4315e457c89"
                ),
                "labels": copy.deepcopy(service_labels),
                "networks": {"default": None},
                "ports": [
                    {
                        "mode": "ingress",
                        "host_ip": "127.0.0.1",
                        "protocol": "tcp",
                        "published": "12433",
                        "target": 1433,
                    }
                ],
                "volumes": [
                    {
                        "type": "volume",
                        "source": "c01-normal-sqlserver-data",
                        "target": "/var/opt/mssql",
                        "volume": {},
                    }
                ],
            },
        },
        "volumes": {
            "c01-normal-sqlserver-data": {
                "name": f"{EXPECTED_PROJECT}_c01-normal-sqlserver-data",
                "labels": copy.deepcopy(service_labels),
            }
        },
    }


def valid_container_inspect() -> list[dict[str, Any]]:
    return [
        {
            "Id": CONTAINER_ID,
            "Image": IMAGE_ID,
            "RestartCount": 0,
            "Config": {
                "Env": [
                    f"MSSQL_SA_PASSWORD={SQL_SECRET}",
                    f"C01_NORMAL_CURSOR_SIGNING_KEY={CURSOR_SECRET}",
                    f"C01_NORMAL_TESTED_COMMIT={EXPECTED_COMMIT}",
                ],
                "Labels": {
                    runner.SUITE_LABEL: runner.SUITE_ID,
                    runner.TESTED_COMMIT_LABEL: EXPECTED_COMMIT,
                    runner.COMPOSE_PROJECT_LABEL: EXPECTED_PROJECT,
                    runner.COMPOSE_SERVICE_LABEL: "api",
                }
            },
            "State": {
                "Running": True,
                "Status": "running",
                "Restarting": False,
                "Dead": False,
                "Pid": 7654,
            },
            "NetworkSettings": {
                "Ports": {
                    "8080/tcp": [
                        {
                            "HostIp": "127.0.0.1",
                            "HostPort": str(API_PORT),
                        }
                    ]
                }
            },
        }
    ]


def negative_container_inspect(
    container_id: str,
    configuration_key: str,
) -> list[dict[str, Any]]:
    return [
        {
            "Id": container_id,
            "Image": IMAGE_ID,
            "RestartCount": 0,
            "Config": {
                "Env": [
                    f"MSSQL_SA_PASSWORD={SQL_SECRET}",
                    f"C01_NORMAL_CURSOR_SIGNING_KEY={CURSOR_SECRET}",
                    (
                        f"{configuration_key}="
                        "C01_NEGATIVE_START_KEY_PRESENT"
                    ),
                ],
                "Labels": {
                    runner.SUITE_LABEL: runner.SUITE_ID,
                    runner.TESTED_COMMIT_LABEL: EXPECTED_COMMIT,
                    runner.COMPOSE_PROJECT_LABEL: EXPECTED_PROJECT,
                    runner.COMPOSE_SERVICE_LABEL: "api",
                    runner.COMPOSE_ONEOFF_LABEL: "True",
                },
            },
            "State": {
                "Running": False,
                "Status": "exited",
                "ExitCode": 1,
            },
        }
    ]


def valid_image_inspect(revision: str = EXPECTED_COMMIT) -> list[dict[str, Any]]:
    return [
        {
            "Id": IMAGE_ID,
            "Config": {
                "Labels": {
                    runner.SUITE_LABEL: runner.SUITE_ID,
                    runner.OCI_REVISION_LABEL: revision,
                }
            },
        }
    ]


class FakeHealthResponse:
    def __init__(self, status: int, body: bytes) -> None:
        self.status = status
        self._body = body

    def __enter__(self) -> FakeHealthResponse:
        return self

    def __exit__(self, *_arguments: object) -> None:
        return None

    def read(self, _size: int = -1) -> bytes:
        return self._body


class FakeDocker:
    def __init__(
        self,
        *,
        image_revision: str = EXPECTED_COMMIT,
        change_container_after_copy: bool = False,
        copy_fails: bool = False,
        runtime_export: str | None = None,
        residue: bool = False,
        git_head: str = EXPECTED_COMMIT,
        git_status: str = "",
        negative_log_secret: bool = False,
    ) -> None:
        self.image_revision = image_revision
        self.change_container_after_copy = change_container_after_copy
        self.copy_fails = copy_fails
        self.runtime_export = runtime_export
        self.residue = residue
        self.git_head = git_head
        self.git_status = git_status
        self.negative_log_secret = negative_log_secret
        self.calls: list[tuple[list[str], dict[str, Any]]] = []
        self.ps_count = 0
        self.negative_containers: dict[str, str] = {}
        self.removed_containers: set[str] = set()
        self.runtime_bytes = (
            json.dumps(valid_attestation(), ensure_ascii=False) + "\n"
        ).encode("utf-8")

    def __call__(self, command: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        arguments = list(command)
        self.calls.append((arguments, kwargs))
        stderr = f"captured-only {SQL_SECRET} {CURSOR_SECRET}"

        if arguments[0] == "git" and "rev-parse" in arguments:
            return self._completed(arguments, self.git_head + "\n", stderr)
        if arguments[0] == "git" and "status" in arguments:
            return self._completed(arguments, self.git_status, stderr)

        if arguments[:2] == ["docker", "compose"]:
            if "config" in arguments:
                return self._completed(arguments, json.dumps(valid_compose_model()), stderr)
            if "up" in arguments:
                return self._completed(arguments, "up output is intentionally captured", stderr)
            if "port" in arguments:
                return self._completed(arguments, f"127.0.0.1:{API_PORT}\n", stderr)
            if "ps" in arguments:
                self.ps_count += 1
                container_id = (
                    OTHER_CONTAINER_ID
                    if self.change_container_after_copy and self.ps_count > 1
                    else CONTAINER_ID
                )
                return self._completed(arguments, container_id + "\n", stderr)
            if "run" in arguments:
                environment_arguments = [
                    arguments[index + 1]
                    for index, token in enumerate(arguments[:-1])
                    if token == "--env"
                ]
                mutations = [
                    value
                    for value in environment_arguments
                    if value.split("=", 1)[0]
                    in runner.DISPOSABLE_CONFIGURATION_KEYS
                ]
                if len(mutations) != 1:
                    raise AssertionError(
                        "negative start must contain one disposable mutation"
                    )
                mutation = mutations[0]
                configuration_key = mutation.split("=", 1)[0]
                negative_id = NEGATIVE_CONTAINER_IDS[len(self.negative_containers)]
                self.negative_containers[negative_id] = configuration_key
                return self._completed(arguments, negative_id + "\n", stderr)
            if "down" in arguments:
                return self._completed(arguments, "down output is intentionally captured", stderr)

        if arguments[:3] == ["docker", "container", "inspect"]:
            container_id = arguments[-1]
            if container_id in self.removed_containers:
                return self._completed(arguments, "", stderr, returncode=1)
            if container_id in self.negative_containers:
                return self._completed(
                    arguments,
                    json.dumps(
                        negative_container_inspect(
                            container_id,
                            self.negative_containers[container_id],
                        )
                    ),
                    stderr,
                )
            return self._completed(arguments, json.dumps(valid_container_inspect()), stderr)
        if arguments[:3] == ["docker", "container", "wait"]:
            return self._completed(arguments, "1\n", stderr)
        if arguments[:3] == ["docker", "container", "logs"]:
            log_output = runner.NEGATIVE_START_FAILURE_REASON + "\n"
            if self.negative_log_secret:
                log_output += SQL_SECRET + "\n"
            return self._completed(
                arguments,
                "",
                log_output,
            )
        if arguments[:3] == ["docker", "container", "rm"]:
            self.removed_containers.add(arguments[-1])
            return self._completed(arguments, arguments[-1] + "\n", stderr)
        if arguments[:3] == ["docker", "image", "inspect"]:
            return self._completed(
                arguments,
                json.dumps(valid_image_inspect(self.image_revision)),
                stderr,
            )
        if arguments[:3] == ["docker", "container", "exec"]:
            source_container = arguments[3]
            if source_container in self.negative_containers:
                return self._completed(arguments, "", stderr, returncode=1)
            if self.copy_fails:
                return self._completed(arguments, "", stderr, returncode=1)
            return self._completed(
                arguments,
                self.runtime_export
                if self.runtime_export is not None
                else base64.b64encode(self.runtime_bytes).decode("ascii"),
                stderr,
            )
        if arguments[:2] == ["docker", "cp"]:
            source_container = arguments[2].split(":", 1)[0]
            if source_container in self.negative_containers:
                return self._completed(arguments, "", stderr, returncode=1)
            return self._completed(arguments, "", stderr, returncode=1)
        if arguments[:3] in (
            ["docker", "container", "ls"],
            ["docker", "network", "ls"],
            ["docker", "volume", "ls"],
        ):
            if arguments[:3] == ["docker", "container", "ls"] and any(
                token.startswith("id=") for token in arguments
            ):
                return self._completed(arguments, "", stderr)
            return self._completed(
                arguments,
                ("d" * 64 + "\n") if self.residue else "",
                stderr,
            )

        raise AssertionError(f"unexpected subprocess command shape: {arguments[:3]}")

    @staticmethod
    def _completed(
        arguments: list[str],
        stdout: str,
        stderr: str,
        *,
        returncode: int = 0,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(arguments, returncode, stdout, stderr)

    def command_with(self, token: str) -> list[list[str]]:
        return [arguments for arguments, _kwargs in self.calls if token in arguments]


class NormalProviderRunnerTests(unittest.TestCase):
    def secret_material(self) -> runner.SecretMaterial:
        return runner.SecretMaterial(SQL_SECRET, CURSOR_SECRET)

    def run_with_fake(
        self,
        fake: FakeDocker,
        output: pathlib.Path,
        *,
        health_status: int = 200,
        health_body: bytes = b"Healthy",
    ) -> dict[str, Any]:
        with (
            mock.patch.object(runner.subprocess, "run", side_effect=fake),
            mock.patch.object(
                runner.urllib.request,
                "urlopen",
                return_value=FakeHealthResponse(health_status, health_body),
            ),
            mock.patch.object(
                runner,
                "generate_secret_material",
                return_value=self.secret_material(),
            ),
            mock.patch.object(runner.time, "sleep"),
        ):
            return runner.run_attestation(
                EXPECTED_COMMIT,
                RUN_ID,
                RUN_ATTEMPT,
                output,
            )

    def test_success_writes_exact_bound_redacted_bundle_and_cleans(self) -> None:
        fake = FakeDocker()
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            evidence = self.run_with_fake(fake, output)

            on_disk = output.read_text(encoding="utf-8")
            self.assertEqual(json.loads(on_disk), evidence)
            self.assertEqual(set(evidence), runner._EVIDENCE_ROOT_KEYS)
            self.assertEqual(evidence["testedCommit"], EXPECTED_COMMIT)
            self.assertEqual(evidence["composeProject"], EXPECTED_PROJECT)
            self.assertEqual(evidence["databaseName"], EXPECTED_DATABASE)
            self.assertEqual(evidence["source"]["gitHead"], EXPECTED_COMMIT)
            self.assertIs(evidence["source"]["trackedAndUntrackedClean"], True)
            self.assertIs(
                evidence["source"]["ignoredFilesExcludedFromCleanCheck"],
                True,
            )
            self.assertEqual(evidence["apiContainer"]["containerId"], CONTAINER_ID)
            self.assertEqual(evidence["apiContainer"]["imageId"], IMAGE_ID)
            self.assertEqual(evidence["apiContainer"]["ociRevision"], EXPECTED_COMMIT)
            self.assertIs(evidence["apiContainer"]["running"], True)
            self.assertEqual(evidence["apiContainer"]["publishedPort"], API_PORT)
            self.assertEqual(
                evidence["apiContainer"]["liveness"],
                {
                    "host": "127.0.0.1",
                    "port": API_PORT,
                    "path": "/health/live",
                    "statusCode": 200,
                    "body": "Healthy",
                },
            )
            self.assertEqual(
                evidence["runtimeArtifact"]["containerId"],
                evidence["apiContainer"]["containerId"],
            )
            self.assertEqual(
                evidence["runtimeArtifact"]["sha256"],
                hashlib.sha256(fake.runtime_bytes).hexdigest(),
            )
            self.assertEqual(
                base64.b64decode(
                    evidence["runtimeArtifact"]["contentBase64"],
                    validate=True,
                ),
                fake.runtime_bytes,
            )
            self.assertEqual(evidence["attestation"], valid_attestation())
            self.assertEqual(len(evidence["negativeStarts"]), 5)
            self.assertEqual(
                [entry["configurationKey"] for entry in evidence["negativeStarts"]],
                list(runner.DISPOSABLE_CONFIGURATION_KEYS),
            )
            self.assertEqual(
                [entry["containerId"] for entry in evidence["negativeStarts"]],
                list(NEGATIVE_CONTAINER_IDS),
            )
            self.assertTrue(
                all(
                    entry["attestationAbsent"]
                    and entry["secretValuesAbsentFromLogs"]
                    and entry["containerRemoved"]
                    for entry in evidence["negativeStarts"]
                )
            )
            self.assertEqual(
                evidence["cleanup"],
                {
                    "downVolumes": True,
                    "removeOrphans": True,
                    "remainingContainers": 0,
                    "remainingNetworks": 0,
                    "remainingVolumes": 0,
                },
            )
            self.assertNotIn(SQL_SECRET, on_disk)
            self.assertNotIn(CURSOR_SECRET, on_disk)

        export_commands = [
            command
            for command in fake.command_with("exec")
            if command[:4] == ["docker", "container", "exec", CONTAINER_ID]
        ]
        self.assertEqual(len(export_commands), 1)
        self.assertEqual(
            export_commands[0],
            [
                "docker",
                "container",
                "exec",
                CONTAINER_ID,
                "base64",
                "-w",
                "0",
                runner.RUNTIME_ATTESTATION_PATH,
            ],
        )
        negative_copy_commands = fake.command_with("cp")
        self.assertEqual(len(negative_copy_commands), 5)
        self.assertTrue(
            all(
                command[2].endswith(
                    ":" + runner.NEGATIVE_RUNTIME_ATTESTATION_PATH
                )
                for command in negative_copy_commands
            )
        )
        down_commands = fake.command_with("down")
        self.assertEqual(len(down_commands), 1)
        self.assertEqual(down_commands[0][-3:], ["down", "-v", "--remove-orphans"])
        global_residue_checks = [
            command
            for command in fake.command_with("ls")
            if any(token.startswith("label=") for token in command)
        ]
        self.assertEqual(len(global_residue_checks), 6)

        for _command, keyword_arguments in fake.calls:
            child_environment = keyword_arguments["env"]
            child_keys = {key.casefold() for key in child_environment}
            for disposable_key in runner.DISPOSABLE_CONFIGURATION_KEYS:
                self.assertNotIn(disposable_key.casefold(), child_keys)

    def test_wrong_oci_revision_fails_without_evidence_and_still_cleans(self) -> None:
        fake = FakeDocker(image_revision="f" * 40)
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            with self.assertRaisesRegex(runner.RunnerError, "API_IMAGE_LABELS_INVALID"):
                self.run_with_fake(fake, output)
            self.assertFalse(output.exists())

        self.assertEqual(len(fake.command_with("down")), 1)
        self.assertEqual(
            len(
                [
                    command
                    for command in fake.command_with("ls")
                    if any(token.startswith("label=") for token in command)
                ]
            ),
            6,
        )

    def test_wrong_git_head_fails_before_compose_start_and_still_cleans(self) -> None:
        fake = FakeDocker(git_head="f" * 40)
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            with self.assertRaisesRegex(runner.RunnerError, "SOURCE_HEAD_MISMATCH"):
                self.run_with_fake(fake, output)
            self.assertFalse(output.exists())

        self.assertFalse(fake.command_with("up"))
        self.assertEqual(len(fake.command_with("down")), 1)

    def test_dirty_tracked_or_untracked_tree_fails_before_start_and_cleans(self) -> None:
        for dirty_status in (
            " M src/backend/GoymenERP.Api/Program.cs\n",
            "?? local-build-input.txt\n",
        ):
            with self.subTest(dirty_status=dirty_status):
                fake = FakeDocker(git_status=dirty_status)
                with tempfile.TemporaryDirectory() as temporary_directory:
                    output = pathlib.Path(temporary_directory) / "evidence.json"
                    with self.assertRaisesRegex(
                        runner.RunnerError,
                        "SOURCE_WORKTREE_DIRTY",
                    ):
                        self.run_with_fake(fake, output)
                    self.assertFalse(output.exists())

                self.assertFalse(fake.command_with("up"))
                self.assertEqual(len(fake.command_with("down")), 1)

    def test_non_healthy_liveness_response_fails_before_attestation_copy(self) -> None:
        fake = FakeDocker()
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            with self.assertRaisesRegex(runner.RunnerError, "API_LIVENESS_FAILED"):
                self.run_with_fake(
                    fake,
                    output,
                    health_status=503,
                    health_body=b"Unhealthy",
                )
            self.assertFalse(output.exists())

        self.assertFalse(fake.command_with("exec"))
        self.assertEqual(len(fake.command_with("down")), 1)

    def test_container_replacement_after_copy_fails_closed_and_cleans(self) -> None:
        fake = FakeDocker(change_container_after_copy=True)
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            with self.assertRaisesRegex(runner.RunnerError, "RUNTIME_CONTAINER_CHANGED"):
                self.run_with_fake(fake, output)
            self.assertFalse(output.exists())

        self.assertEqual(len(fake.command_with("down")), 1)
        self.assertEqual(
            len(
                [
                    command
                    for command in fake.command_with("ls")
                    if any(token.startswith("label=") for token in command)
                ]
            ),
            6,
        )

    def test_compose_validator_failure_never_starts_but_always_cleans(self) -> None:
        fake = FakeDocker()
        validator_secret = "validator-secret-must-not-reach-diagnostics"
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            error = runner.contract_validator.ContractError(
                f"COMPOSE_FAILURE_{validator_secret}"
            )
            with (
                mock.patch.object(runner.subprocess, "run", side_effect=fake),
                mock.patch.object(
                    runner,
                    "generate_secret_material",
                    return_value=self.secret_material(),
                ),
                mock.patch.object(
                    runner.contract_validator,
                    "validate_compose",
                    side_effect=error,
                ),
            ):
                stderr = io.StringIO()
                with redirect_stderr(stderr):
                    exit_code = runner.main(
                        [
                            "--expected-commit",
                            EXPECTED_COMMIT,
                            "--run-id",
                            RUN_ID,
                            "--run-attempt",
                            str(RUN_ATTEMPT),
                            "--output",
                            str(output),
                        ]
                    )

            self.assertEqual(exit_code, 1)
            self.assertEqual(
                stderr.getvalue(),
                "C-01 normal-provider runner failed: COMPOSE_CONTRACT_FAILED\n",
            )
            self.assertNotIn(validator_secret, stderr.getvalue())
            self.assertFalse(output.exists())

        self.assertFalse(fake.command_with("up"))
        self.assertEqual(len(fake.command_with("down")), 1)
        self.assertEqual(len(fake.command_with("ls")), 3)

    def test_copy_failure_is_redacted_and_cleanup_runs_before_failure_returns(self) -> None:
        fake = FakeDocker(copy_fails=True)
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            with (
                mock.patch.object(runner.subprocess, "run", side_effect=fake),
                mock.patch.object(
                    runner,
                    "generate_secret_material",
                    return_value=self.secret_material(),
                ),
                mock.patch.object(
                    runner.urllib.request,
                    "urlopen",
                    return_value=FakeHealthResponse(200, b"Healthy"),
                ),
                mock.patch.object(runner.time, "sleep"),
            ):
                stdout = io.StringIO()
                stderr = io.StringIO()
                with redirect_stdout(stdout), redirect_stderr(stderr):
                    exit_code = runner.main(
                        [
                            "--expected-commit",
                            EXPECTED_COMMIT,
                            "--run-id",
                            RUN_ID,
                            "--run-attempt",
                            str(RUN_ATTEMPT),
                            "--output",
                            str(output),
                        ]
                    )

            self.assertEqual(exit_code, 1)
            diagnostics = stdout.getvalue() + stderr.getvalue()
            self.assertEqual(
                diagnostics,
                "C-01 normal-provider runner failed: "
                "RUNTIME_ARTIFACT_COPY_FAILED\n",
            )
            self.assertNotIn(SQL_SECRET, diagnostics)
            self.assertNotIn(CURSOR_SECRET, diagnostics)
            self.assertFalse(output.exists())

        self.assertEqual(len(fake.command_with("exec")), 20)
        self.assertEqual(len(fake.command_with("down")), 1)
        self.assertEqual(
            len(
                [
                    command
                    for command in fake.command_with("ls")
                    if any(token.startswith("label=") for token in command)
                ]
            ),
            6,
        )

    def test_malformed_runtime_export_fails_closed_and_cleans(self) -> None:
        fake = FakeDocker(runtime_export="not-valid-base64!?")
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            with self.assertRaisesRegex(
                runner.RunnerError,
                "RUNTIME_ARTIFACT_COPY_FAILED",
            ):
                self.run_with_fake(fake, output)
            self.assertFalse(output.exists())

        self.assertEqual(len(fake.command_with("exec")), 20)
        self.assertEqual(len(fake.command_with("down")), 1)

    def test_cleanup_residue_fails_closed_and_suppresses_evidence(self) -> None:
        fake = FakeDocker(residue=True)
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            with self.assertRaisesRegex(
                runner.RunnerError,
                "CLEANUP_RESIDUE_DETECTED",
            ):
                self.run_with_fake(fake, output)
            self.assertFalse(output.exists())

        self.assertEqual(len(fake.command_with("down")), 1)
        self.assertFalse(fake.command_with("up"))
        self.assertEqual(
            len(
                [
                    command
                    for command in fake.command_with("ls")
                    if any(token.startswith("label=") for token in command)
                ]
            ),
            4,
        )

    def test_negative_start_log_secret_fails_and_oneoff_is_removed(self) -> None:
        fake = FakeDocker(negative_log_secret=True)
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            with self.assertRaisesRegex(
                runner.RunnerError,
                "NEGATIVE_LOG_SECRET_LEAK",
            ):
                self.run_with_fake(fake, output)
            self.assertFalse(output.exists())

        self.assertEqual(len(fake.command_with("run")), 1)
        self.assertEqual(len(fake.command_with("rm")), 1)
        self.assertEqual(len(fake.command_with("down")), 1)

    def test_parent_disposable_keys_are_removed_case_insensitively(self) -> None:
        identity = runner.derive_run_identity(EXPECTED_COMMIT, RUN_ID, RUN_ATTEMPT)
        parent_environment = {
            "PATH": "synthetic-path",
            **{
                configuration_key.swapcase(): "must-not-be-forwarded"
                for configuration_key in runner.DISPOSABLE_CONFIGURATION_KEYS
            },
        }
        environment = runner.build_child_environment(
            identity,
            self.secret_material(),
            parent_environment,
        )

        self.assertEqual(environment["PATH"], "synthetic-path")
        self.assertEqual(environment["MSSQL_SA_PASSWORD"], SQL_SECRET)
        self.assertEqual(environment["C01_NORMAL_CURSOR_SIGNING_KEY"], CURSOR_SECRET)
        environment_keys = {key.casefold() for key in environment}
        self.assertTrue(
            environment_keys.isdisjoint(
                key.casefold()
                for key in runner.DISPOSABLE_CONFIGURATION_KEYS
            )
        )

    def test_secret_material_is_generated_locally_from_secrets_module(self) -> None:
        random_bytes = bytes(range(48))
        with (
            mock.patch.object(
                runner.secrets,
                "token_urlsafe",
                return_value="local-random-password-material",
            ) as token_urlsafe,
            mock.patch.object(
                runner.secrets,
                "token_bytes",
                return_value=random_bytes,
            ) as token_bytes,
        ):
            material = runner.generate_secret_material()

        token_urlsafe.assert_called_once_with(36)
        token_bytes.assert_called_once_with(48)
        self.assertEqual(
            material.sql_password,
            "Goymen!Aa1-local-random-password-material",
        )
        self.assertEqual(
            material.cursor_signing_key,
            base64.b64encode(random_bytes).decode("ascii"),
        )
        self.assertEqual(repr(material), "SecretMaterial(<redacted>)")

    def test_five_negative_start_contracts_are_isolated_and_value_free(self) -> None:
        identity = runner.derive_run_identity(EXPECTED_COMMIT, RUN_ID, RUN_ATTEMPT)
        contracts = runner.negative_start_contracts()

        self.assertEqual(len(contracts), 5)
        self.assertEqual(
            {contract.configuration_key for contract in contracts},
            set(runner.DISPOSABLE_CONFIGURATION_KEYS),
        )
        self.assertEqual(len({contract.case_id for contract in contracts}), 5)
        for contract in contracts:
            with self.subTest(configuration_key=contract.configuration_key):
                command = runner.build_negative_start_command(identity, contract)
                mutations = [
                    token
                    for token in command
                    if any(
                        token.startswith(configuration_key + "=")
                        for configuration_key in runner.DISPOSABLE_CONFIGURATION_KEYS
                    )
                ]
                self.assertEqual(
                    mutations,
                    [
                        f"{contract.configuration_key}="
                        f"{contract.injected_value}"
                    ],
                )
                self.assertEqual(command[-1], "api")
                self.assertIn("--no-deps", command)
                self.assertIn(
                    (
                        "NormalProviderAttestation__OutputPath="
                        f"{runner.NEGATIVE_RUNTIME_ATTESTATION_PATH}"
                    ),
                    command,
                )
                self.assertEqual(
                    contract.expected_outcome,
                    "startup-must-fail-before-attestation",
                )
                self.assertEqual(
                    contract.expected_failure_reason,
                    runner.NEGATIVE_START_FAILURE_REASON,
                )

    def test_bundle_rejects_attestation_mutation_with_unchanged_runtime_hash(self) -> None:
        fake = FakeDocker()
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = pathlib.Path(temporary_directory) / "evidence.json"
            evidence = self.run_with_fake(fake, output)

        mutated = copy.deepcopy(evidence)
        mutated["attestation"]["processId"] = 43
        identity = runner.derive_run_identity(
            EXPECTED_COMMIT,
            RUN_ID,
            RUN_ATTEMPT,
        )
        with self.assertRaisesRegex(
            runner.RunnerError,
            "EVIDENCE_RUNTIME_ATTESTATION_BINDING_INVALID",
        ):
            runner.validate_evidence_bundle(mutated, identity)

    def test_identity_rejects_noncanonical_commit_and_run_numbers(self) -> None:
        invalid = (
            (EXPECTED_COMMIT.upper(), RUN_ID, RUN_ATTEMPT),
            (EXPECTED_COMMIT[:-1], RUN_ID, RUN_ATTEMPT),
            (EXPECTED_COMMIT, "0", RUN_ATTEMPT),
            (EXPECTED_COMMIT, "204/1", RUN_ATTEMPT),
            (EXPECTED_COMMIT, RUN_ID, "01"),
        )
        for expected_commit, run_id, run_attempt in invalid:
            with self.subTest(
                expected_commit=expected_commit,
                run_id=run_id,
                run_attempt=run_attempt,
            ):
                with self.assertRaises(runner.RunnerError):
                    runner.derive_run_identity(
                        expected_commit,
                        run_id,
                        run_attempt,
                    )


if __name__ == "__main__":
    unittest.main()
