from __future__ import annotations

import base64
import copy
import io
import json
import pathlib
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from typing import Any
from unittest import mock


REPOSITORY_ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPOSITORY_ROOT / "tools"))

import validate_c01_normal_provider_contract as validator  # noqa: E402


EXPECTED_COMMIT = "0123456789abcdef0123456789abcdef01234567"
EXPECTED_PROJECT = "goymen-c01-normal-204-1"
EXPECTED_DATABASE = "GoymenErpC01Normal_204_1"
SQL_PASSWORD = "SyntheticTestOnlyPassword123!"
CURSOR_KEY = base64.b64encode(b"c" * 48).decode("ascii")
_DEFAULT_COMPOSE_MODEL = object()


def valid_attestation() -> dict[str, Any]:
    revision = base64.urlsafe_b64encode(bytes(range(32))).rstrip(b"=").decode("ascii")
    return {
        "schemaVersion": validator.EXPECTED_SCHEMA_VERSION,
        "providerOrigin": validator.EXPECTED_PROVIDER_ORIGIN,
        "resolvedProviderCount": 1,
        "resolvedProviderType": validator.EXPECTED_PROVIDER_TYPE,
        "overrideKeysPresent": [],
        "processInstanceId": "53d45729-80f7-4bda-bf7a-b1ec49b22d27",
        "processId": 42,
        "registryRevision": f"sha256:{revision}",
    }


def valid_compose_model() -> dict[str, Any]:
    service_labels = {
        "com.goymen.e2e.suite": "C01-NORMAL-PROVIDER-ATTESTATION-V1",
        "com.goymen.e2e.tested-commit": EXPECTED_COMMIT,
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
                        "com.goymen.e2e.suite": (
                            "C01-NORMAL-PROVIDER-ATTESTATION-V1"
                        ),
                        "org.opencontainers.image.revision": EXPECTED_COMMIT,
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
                        f"User Id=sa;Password={SQL_PASSWORD};"
                        "Encrypt=True;TrustServerCertificate=True"
                    ),
                    "Counterparty__CursorSigningKey": CURSOR_KEY,
                    "NormalProviderAttestation__OutputPath": (
                        "/run/goymen-attestation/runtime.json"
                    ),
                    "BootstrapIdentity__HeaderName": "X-Development-User",
                    "BootstrapIdentity__Users__0__UserId": "group-bootstrap-admin",
                    "BootstrapIdentity__Users__0__DisplayName": (
                        "C01 Normal Provider Administrator"
                    ),
                },
                "healthcheck": copy.deepcopy(validator.EXPECTED_API_HEALTHCHECK),
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
                    "MSSQL_SA_PASSWORD": SQL_PASSWORD,
                },
                "healthcheck": copy.deepcopy(validator.EXPECTED_SQL_HEALTHCHECK),
                "image": (
                    "mcr.microsoft.com/mssql/server@"
                    "sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89"
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


class NormalProviderAttestationContractTests(unittest.TestCase):
    def test_contract_literals_are_pinned(self) -> None:
        self.assertEqual(
            "C01-NORMAL-PROVIDER-ATTESTATION-V1",
            validator.EXPECTED_SCHEMA_VERSION,
        )
        self.assertEqual("normal", validator.EXPECTED_PROVIDER_ORIGIN)
        self.assertEqual(
            "GoymenERP.Modules.Counterparty.Infrastructure.Shell."
            "CounterpartyShellCapabilityProvider",
            validator.EXPECTED_PROVIDER_TYPE,
        )
        self.assertEqual(
            {
                "schemaVersion",
                "providerOrigin",
                "resolvedProviderCount",
                "resolvedProviderType",
                "overrideKeysPresent",
                "processInstanceId",
                "processId",
                "registryRevision",
            },
            validator.EXPECTED_KEYS,
        )
        self.assertEqual(
            (
                "C01E2e__EnableListCapability",
                "GOYMEN_ERP_C01_E2E_DISPOSABLE",
                "GOYMEN_ERP_C01_E2E_TESTED_COMMIT",
                "GOYMEN_ERP_C01_E2E_RUN_ID",
                "GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT",
            ),
            validator.DISPOSABLE_CONFIGURATION_KEYS,
        )
        self.assertEqual(
            "C01-NORMAL-PROVIDER-ATTESTATION-V1",
            validator.EXPECTED_SUITE_LABEL,
        )
        self.assertEqual(
            "mcr.microsoft.com/mssql/server@"
            "sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89",
            validator.EXPECTED_SQLSERVER_IMAGE,
        )
        self.assertEqual(
            "/run/goymen-attestation/runtime.json",
            validator.EXPECTED_ATTESTATION_OUTPUT_PATH,
        )
        self.assertEqual(
            {
                "test": [
                    "CMD-SHELL",
                    "test -s /run/goymen-attestation/runtime.json "
                    "&& grep -q ':1F90 ' /proc/net/tcp /proc/net/tcp6",
                ],
                "timeout": "2s",
                "interval": "2s",
                "retries": 30,
                "start_period": "5s",
            },
            validator.EXPECTED_API_HEALTHCHECK,
        )
        self.assertEqual(
            {
                "test": [
                    "CMD-SHELL",
                    "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa "
                    '-P "$${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" || exit 1',
                ],
                "timeout": "5s",
                "interval": "10s",
                "retries": 18,
                "start_period": "20s",
            },
            validator.EXPECTED_SQL_HEALTHCHECK,
        )

    def test_exact_normal_provider_attestation_passes(self) -> None:
        validator.validate_attestation(valid_attestation())

    def test_root_must_be_an_object(self) -> None:
        for value in (None, [], "attestation", 1, True):
            with self.subTest(value_type=type(value).__name__):
                with self.assertRaisesRegex(
                    validator.ContractError, "ATTESTATION_ROOT_TYPE_INVALID"
                ):
                    validator.validate_attestation(value)

    def test_each_missing_field_fails_closed(self) -> None:
        for field in sorted(validator.EXPECTED_KEYS):
            with self.subTest(field=field):
                attestation = valid_attestation()
                del attestation[field]
                with self.assertRaisesRegex(
                    validator.ContractError, "ATTESTATION_FIELDS_NOT_EXACT"
                ):
                    validator.validate_attestation(attestation)

    def test_extra_field_fails_closed(self) -> None:
        attestation = valid_attestation()
        attestation["unexpected"] = "not-authoritative"

        with self.assertRaisesRegex(
            validator.ContractError, "ATTESTATION_FIELDS_NOT_EXACT"
        ):
            validator.validate_attestation(attestation)

    def test_each_field_rejects_the_wrong_json_type(self) -> None:
        invalid_values = {
            "schemaVersion": 1,
            "providerOrigin": True,
            "resolvedProviderCount": "1",
            "resolvedProviderType": [],
            "overrideKeysPresent": {},
            "processInstanceId": 123,
            "processId": "42",
            "registryRevision": 123,
        }
        for field, invalid_value in invalid_values.items():
            with self.subTest(field=field):
                attestation = valid_attestation()
                attestation[field] = invalid_value
                with self.assertRaises(validator.ContractError):
                    validator.validate_attestation(attestation)

    def test_each_disposable_configuration_key_fails_closed(self) -> None:
        for configuration_key in validator.DISPOSABLE_CONFIGURATION_KEYS:
            with self.subTest(configuration_key=configuration_key):
                attestation = valid_attestation()
                attestation["overrideKeysPresent"] = [configuration_key]
                with self.assertRaisesRegex(
                    validator.ContractError, "ATTESTATION_OVERRIDE_KEYS_PRESENT"
                ):
                    validator.validate_attestation(attestation)

    def test_empty_override_field_must_be_an_array(self) -> None:
        for value in (None, "", {}, 0, False):
            with self.subTest(value_type=type(value).__name__):
                attestation = valid_attestation()
                attestation["overrideKeysPresent"] = value
                with self.assertRaisesRegex(
                    validator.ContractError, "ATTESTATION_OVERRIDE_KEYS_PRESENT"
                ):
                    validator.validate_attestation(attestation)

    def test_spoofed_normal_label_on_disposable_provider_fails(self) -> None:
        attestation = valid_attestation()
        attestation["providerOrigin"] = "normal"
        attestation["resolvedProviderType"] = "GoymenERP.Api.Shell.C01E2eShellCapabilityProvider"

        with self.assertRaisesRegex(
            validator.ContractError, "ATTESTATION_PROVIDER_TYPE_INVALID"
        ):
            validator.validate_attestation(attestation)

    def test_disposable_provider_origin_fails(self) -> None:
        attestation = valid_attestation()
        attestation["providerOrigin"] = "c01-disposable"
        attestation["resolvedProviderType"] = "GoymenERP.Api.Shell.C01E2eShellCapabilityProvider"

        with self.assertRaisesRegex(
            validator.ContractError, "ATTESTATION_PROVIDER_ORIGIN_INVALID"
        ):
            validator.validate_attestation(attestation)

    def test_unknown_provider_fails(self) -> None:
        attestation = valid_attestation()
        attestation["resolvedProviderType"] = "UnknownProvider"

        with self.assertRaisesRegex(
            validator.ContractError, "ATTESTATION_PROVIDER_TYPE_INVALID"
        ):
            validator.validate_attestation(attestation)

    def test_missing_or_multiple_provider_fails(self) -> None:
        for provider_count in (0, 2, -1, True, 1.0):
            with self.subTest(provider_count=provider_count):
                attestation = valid_attestation()
                attestation["resolvedProviderCount"] = provider_count
                with self.assertRaisesRegex(
                    validator.ContractError, "ATTESTATION_PROVIDER_COUNT_INVALID"
                ):
                    validator.validate_attestation(attestation)

    def test_schema_version_is_exact(self) -> None:
        for schema_version in ("", "C01-NORMAL-PROVIDER-ATTESTATION-V2", "1"):
            with self.subTest(schema_version=schema_version):
                attestation = valid_attestation()
                attestation["schemaVersion"] = schema_version
                with self.assertRaisesRegex(
                    validator.ContractError, "ATTESTATION_SCHEMA_VERSION_INVALID"
                ):
                    validator.validate_attestation(attestation)

    def test_process_instance_id_must_be_canonical_lowercase_non_nil_guid(self) -> None:
        invalid_ids = (
            "",
            "00000000-0000-0000-0000-000000000000",
            "53D45729-80F7-4BDA-BF7A-B1EC49B22D27",
            "{53d45729-80f7-4bda-bf7a-b1ec49b22d27}",
            "53d4572980f74bdabf7ab1ec49b22d27",
            " 53d45729-80f7-4bda-bf7a-b1ec49b22d27",
            "not-a-guid",
        )
        for process_instance_id in invalid_ids:
            with self.subTest(process_instance_id=process_instance_id):
                attestation = valid_attestation()
                attestation["processInstanceId"] = process_instance_id
                with self.assertRaisesRegex(
                    validator.ContractError,
                    "ATTESTATION_PROCESS_INSTANCE_ID_INVALID",
                ):
                    validator.validate_attestation(attestation)

    def test_process_id_must_be_a_positive_json_integer(self) -> None:
        for process_id in (0, -1, True, False, 1.0, "1", None):
            with self.subTest(process_id=process_id):
                attestation = valid_attestation()
                attestation["processId"] = process_id
                with self.assertRaisesRegex(
                    validator.ContractError, "ATTESTATION_PROCESS_ID_INVALID"
                ):
                    validator.validate_attestation(attestation)

    def test_registry_revision_must_be_canonical_sha256_base64url(self) -> None:
        valid_suffix = valid_attestation()["registryRevision"].removeprefix("sha256:")
        noncanonical_suffix = valid_suffix[:-1] + (
            "B" if valid_suffix[-1] != "B" else "C"
        )
        invalid_revisions = (
            "",
            valid_suffix,
            f"sha256:{valid_suffix[:-1]}",
            f"sha256:{valid_suffix}=",
            f"sha256:{valid_suffix[:-1]}+",
            f"sha512:{valid_suffix}",
            f"sha256:{noncanonical_suffix}",
        )
        for registry_revision in invalid_revisions:
            with self.subTest(registry_revision=registry_revision):
                attestation = valid_attestation()
                attestation["registryRevision"] = registry_revision
                with self.assertRaisesRegex(
                    validator.ContractError,
                    "ATTESTATION_REGISTRY_REVISION_INVALID",
                ):
                    validator.validate_attestation(attestation)


class NormalProviderAttestationJsonTests(unittest.TestCase):
    def test_malformed_json_fails_closed(self) -> None:
        with self.assertRaisesRegex(
            validator.ContractError, "ATTESTATION_JSON_MALFORMED"
        ):
            validator._parse_json('{"schemaVersion":')

    def test_duplicate_json_key_fails_closed(self) -> None:
        raw = json.dumps(valid_attestation())
        duplicated = raw.replace(
            '"schemaVersion":',
            f'"schemaVersion":"{validator.EXPECTED_SCHEMA_VERSION}","schemaVersion":',
            1,
        )
        with self.assertRaisesRegex(
            validator.ContractError, "ATTESTATION_JSON_DUPLICATE_KEY"
        ):
            validator._parse_json(duplicated)

    def test_non_finite_json_number_fails_closed(self) -> None:
        raw = json.dumps(valid_attestation()).replace('"processId": 42', '"processId": NaN')
        with self.assertRaisesRegex(
            validator.ContractError, "ATTESTATION_JSON_NON_FINITE_NUMBER"
        ):
            validator._parse_json(raw)


class NormalProviderComposeContractTests(unittest.TestCase):
    def validate(
        self,
        model: Any = _DEFAULT_COMPOSE_MODEL,
        *,
        expected_commit: Any = EXPECTED_COMMIT,
        expected_project: Any = EXPECTED_PROJECT,
    ) -> None:
        validator.validate_compose(
            valid_compose_model() if model is _DEFAULT_COMPOSE_MODEL else model,
            expected_commit,
            expected_project,
        )

    def assert_compose_error(
        self,
        model: Any,
        error_code: str,
        *,
        expected_commit: Any = EXPECTED_COMMIT,
        expected_project: Any = EXPECTED_PROJECT,
    ) -> None:
        with self.assertRaisesRegex(validator.ContractError, error_code):
            self.validate(
                model,
                expected_commit=expected_commit,
                expected_project=expected_project,
            )

    def test_rendered_synthetic_compose_model_passes(self) -> None:
        self.validate()

    def test_root_fields_and_service_inventory_are_exact(self) -> None:
        for mutation in ("missing-networks", "missing-volumes", "extra-root"):
            with self.subTest(mutation=mutation):
                model = valid_compose_model()
                if mutation == "missing-networks":
                    del model["networks"]
                elif mutation == "missing-volumes":
                    del model["volumes"]
                else:
                    model["secrets"] = {"forged": {}}
                self.assert_compose_error(model, "COMPOSE_ROOT_FIELDS_INVALID")

        for service_name in ("web", "attacker", "api-copy"):
            with self.subTest(extra_service=service_name):
                model = valid_compose_model()
                model["services"][service_name] = copy.deepcopy(
                    model["services"]["api"]
                )
                self.assert_compose_error(
                    model,
                    "COMPOSE_SERVICE_INVENTORY_INVALID",
                )

    def test_service_allowed_key_sets_are_exact(self) -> None:
        service_mutations = (
            ("api", "privileged", True, "COMPOSE_API_FIELDS_INVALID"),
            ("api", "pid", "host", "COMPOSE_API_FIELDS_INVALID"),
            ("api", "volumes", ["forged:/run"], "COMPOSE_API_FIELDS_INVALID"),
            ("sqlserver", "privileged", True, "COMPOSE_SQL_FIELDS_INVALID"),
            ("sqlserver", "pid", "host", "COMPOSE_SQL_FIELDS_INVALID"),
            ("sqlserver", "tmpfs", ["/tmp"], "COMPOSE_SQL_FIELDS_INVALID"),
        )
        for service, field, value, error_code in service_mutations:
            with self.subTest(service=service, field=field):
                model = valid_compose_model()
                model["services"][service][field] = value
                self.assert_compose_error(model, error_code)

        for service, error_code in (
            ("api", "COMPOSE_API_FIELDS_INVALID"),
            ("sqlserver", "COMPOSE_SQL_FIELDS_INVALID"),
        ):
            with self.subTest(service=service, mutation="missing-command"):
                model = valid_compose_model()
                del model["services"][service]["command"]
                self.assert_compose_error(model, error_code)

    def test_command_and_entrypoint_shell_overrides_fail_closed(self) -> None:
        shell_overrides: tuple[Any, ...] = (
            "/bin/sh",
            ["/bin/sh", "-c", "forge-attestation"],
            ["CMD-SHELL", "forge-attestation"],
            [],
        )
        for service in ("api", "sqlserver"):
            for field in ("command", "entrypoint"):
                for override in shell_overrides:
                    with self.subTest(
                        service=service,
                        field=field,
                        value_type=type(override).__name__,
                    ):
                        model = valid_compose_model()
                        model["services"][service][field] = override
                        self.assert_compose_error(
                            model,
                            "COMPOSE_PROCESS_OVERRIDE_INVALID",
                        )

    def test_api_build_context_dockerfile_and_fields_are_exact(self) -> None:
        for context in (
            "..",
            ".",
            str(REPOSITORY_ROOT / "src"),
            str(REPOSITORY_ROOT.parent),
            None,
        ):
            with self.subTest(field="context", value_type=type(context).__name__):
                model = valid_compose_model()
                model["services"]["api"]["build"]["context"] = context
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_BUILD_CONTEXT_INVALID",
                )

        for dockerfile in (
            None,
            "Dockerfile",
            "src/backend/GoymenERP.Api/Forged.Dockerfile",
            "../Dockerfile",
        ):
            with self.subTest(
                field="dockerfile", value_type=type(dockerfile).__name__
            ):
                model = valid_compose_model()
                model["services"]["api"]["build"]["dockerfile"] = dockerfile
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_DOCKERFILE_INVALID",
                )

        for field in ("context", "dockerfile", "labels"):
            with self.subTest(missing_build_field=field):
                model = valid_compose_model()
                del model["services"]["api"]["build"][field]
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_BUILD_FIELDS_INVALID",
                )

        model = valid_compose_model()
        model["services"]["api"]["build"]["args"] = {"FORGE": "true"}
        self.assert_compose_error(model, "COMPOSE_API_BUILD_FIELDS_INVALID")

    def test_api_requires_exact_private_attestation_tmpfs_and_no_mounts(self) -> None:
        for tmpfs in (
            None,
            [],
            ["/run/goymen-attestation"],
            ["/run/goymen-attestation:mode=0777"],
            ["/tmp:mode=0700"],
            [
                "/run/goymen-attestation:mode=0700",
                "/tmp:mode=0700",
            ],
        ):
            with self.subTest(value_type=type(tmpfs).__name__):
                model = valid_compose_model()
                model["services"]["api"]["tmpfs"] = tmpfs
                self.assert_compose_error(model, "COMPOSE_API_TMPFS_INVALID")

        for volume in (
            ["attestation:/run/goymen-attestation"],
            ["./forged:/run/goymen-attestation"],
            [
                {
                    "type": "bind",
                    "source": "/tmp/forged",
                    "target": "/run/goymen-attestation",
                }
            ],
        ):
            with self.subTest(api_volume=volume):
                model = valid_compose_model()
                model["services"]["api"]["volumes"] = volume
                self.assert_compose_error(model, "COMPOSE_API_FIELDS_INVALID")

    def test_healthchecks_are_exact_and_shell_forgery_fails(self) -> None:
        for service, error_code in (
            ("api", "COMPOSE_API_HEALTHCHECK_INVALID"),
            ("sqlserver", "COMPOSE_SQL_HEALTHCHECK_INVALID"),
        ):
            mutations: tuple[Any, ...] = (
                None,
                {},
                {"test": ["NONE"]},
                {
                    **copy.deepcopy(
                        valid_compose_model()["services"][service]["healthcheck"]
                    ),
                    "test": ["CMD-SHELL", "touch /tmp/forged && exit 0"],
                },
                {
                    **copy.deepcopy(
                        valid_compose_model()["services"][service]["healthcheck"]
                    ),
                    "retries": 1,
                },
            )
            for healthcheck in mutations:
                with self.subTest(
                    service=service,
                    value_type=type(healthcheck).__name__,
                ):
                    model = valid_compose_model()
                    model["services"][service]["healthcheck"] = healthcheck
                    self.assert_compose_error(model, error_code)

    def test_expected_commit_must_be_exact_lowercase_sha(self) -> None:
        invalid_commits: tuple[Any, ...] = (
            "",
            "a" * 39,
            "a" * 41,
            "A" * 40,
            "g" * 40,
            f" {'a' * 40}",
            123,
            None,
        )
        for expected_commit in invalid_commits:
            with self.subTest(value_type=type(expected_commit).__name__):
                self.assert_compose_error(
                    valid_compose_model(),
                    "COMPOSE_EXPECTED_COMMIT_INVALID",
                    expected_commit=expected_commit,
                )

    def test_expected_project_must_be_nonempty_and_trimmed(self) -> None:
        for expected_project in (
            "",
            " project",
            "project ",
            "project",
            "goymen-c01-normal-0-1",
            "goymen-c01-normal-1-01",
            123,
            None,
        ):
            with self.subTest(value_type=type(expected_project).__name__):
                self.assert_compose_error(
                    valid_compose_model(),
                    "COMPOSE_EXPECTED_PROJECT_INVALID",
                    expected_project=expected_project,
                )

    def test_root_project_and_services_fail_closed(self) -> None:
        for invalid_root in (None, [], "compose", 1, True):
            with self.subTest(root_type=type(invalid_root).__name__):
                self.assert_compose_error(
                    invalid_root,
                    "COMPOSE_ROOT_TYPE_INVALID",
                )

        for invalid_name in (None, "wrong-project", 123):
            with self.subTest(field="name", value_type=type(invalid_name).__name__):
                model = valid_compose_model()
                model["name"] = invalid_name
                self.assert_compose_error(model, "COMPOSE_PROJECT_MISMATCH")

        for invalid_services in (None, [], "services"):
            with self.subTest(
                field="services", value_type=type(invalid_services).__name__
            ):
                model = valid_compose_model()
                model["services"] = invalid_services
                self.assert_compose_error(model, "COMPOSE_SERVICES_INVALID")

    def test_api_and_sql_services_are_both_required_objects(self) -> None:
        for service_name, error_code in (
            ("api", "COMPOSE_API_SERVICE_INVALID"),
            ("sqlserver", "COMPOSE_SQL_SERVICE_INVALID"),
        ):
            for mutation in ("missing", "null", "array"):
                with self.subTest(service=service_name, mutation=mutation):
                    model = valid_compose_model()
                    if mutation == "missing":
                        del model["services"][service_name]
                    elif mutation == "null":
                        model["services"][service_name] = None
                    else:
                        model["services"][service_name] = []
                    self.assert_compose_error(
                        model,
                        (
                            "COMPOSE_SERVICE_INVENTORY_INVALID"
                            if mutation == "missing"
                            else error_code
                        ),
                    )

    def test_api_and_sql_ports_must_be_nonempty_loopback_bindings(self) -> None:
        mutations = (
            None,
            [],
            "127.0.0.1:8080:8080",
            ["127.0.0.1:8080:8080"],
            [{"host_ip": "0.0.0.0", "target": 8080}],
            [{"host_ip": "::", "target": 8080}],
            [{"target": 8080}],
        )
        for service_name, error_code in (
            ("api", "COMPOSE_API_PORT_INVALID"),
            ("sqlserver", "COMPOSE_SQL_PORT_INVALID"),
        ):
            for ports in mutations:
                with self.subTest(service=service_name, ports_type=type(ports).__name__):
                    model = valid_compose_model()
                    model["services"][service_name]["ports"] = ports
                    self.assert_compose_error(model, error_code)

    def test_api_and_sql_port_contract_rejects_udp_and_wrong_ports(self) -> None:
        for service_name, error_code in (
            ("api", "COMPOSE_API_PORT_INVALID"),
            ("sqlserver", "COMPOSE_SQL_PORT_INVALID"),
        ):
            for field, value in (
                ("protocol", "udp"),
                ("host_ip", "0.0.0.0"),
                ("mode", "host"),
                ("target", 1),
                ("target", "8080"),
                ("published", "1"),
                ("published", 16080),
            ):
                with self.subTest(service=service_name, field=field, value=value):
                    model = valid_compose_model()
                    model["services"][service_name]["ports"][0][field] = value
                    self.assert_compose_error(model, error_code)

            model = valid_compose_model()
            model["services"][service_name]["ports"][0]["name"] = "forged"
            self.assert_compose_error(model, error_code)

            model = valid_compose_model()
            model["services"][service_name]["ports"].append(
                copy.deepcopy(model["services"][service_name]["ports"][0])
            )
            self.assert_compose_error(model, error_code)

    def test_api_environment_must_be_an_object(self) -> None:
        for environment in (None, [], "environment"):
            with self.subTest(value_type=type(environment).__name__):
                model = valid_compose_model()
                model["services"]["api"]["environment"] = environment
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_ENVIRONMENT_INVALID",
                )

    def test_api_environment_key_set_is_exact(self) -> None:
        environment = valid_compose_model()["services"]["api"]["environment"]
        for field in tuple(environment):
            with self.subTest(missing_field=field):
                model = valid_compose_model()
                del model["services"]["api"]["environment"][field]
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_ENVIRONMENT_FIELDS_INVALID",
                )

        for extra_field in ("EXTRA", "LD_PRELOAD", "ASPNETCORE_URLS"):
            with self.subTest(extra_field=extra_field):
                model = valid_compose_model()
                model["services"]["api"]["environment"][extra_field] = "forged"
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_ENVIRONMENT_FIELDS_INVALID",
                )

    def test_each_disposable_environment_alias_is_rejected_case_insensitively(self) -> None:
        for configuration_key in validator.DISPOSABLE_CONFIGURATION_KEYS:
            for environment_key in (configuration_key, configuration_key.swapcase()):
                with self.subTest(environment_key=environment_key):
                    model = valid_compose_model()
                    model["services"]["api"]["environment"][environment_key] = (
                        "secret-disposable-value"
                    )
                    self.assert_compose_error(
                        model,
                        "COMPOSE_DISPOSABLE_ENVIRONMENT_KEY_PRESENT",
                    )

    def test_api_runtime_environment_must_be_exact_testing(self) -> None:
        for runtime_environment in (None, "", "testing", "Development", True):
            with self.subTest(value_type=type(runtime_environment).__name__):
                model = valid_compose_model()
                model["services"]["api"]["environment"][
                    "ASPNETCORE_ENVIRONMENT"
                ] = runtime_environment
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_RUNTIME_ENVIRONMENT_INVALID",
                )

    def test_attestation_output_path_must_be_exact(self) -> None:
        for output_path in (
            None,
            "",
            "/tmp/runtime.json",
            "/run/goymen-attestation/runtime.json ",
            True,
        ):
            with self.subTest(value_type=type(output_path).__name__):
                model = valid_compose_model()
                model["services"]["api"]["environment"][
                    "NormalProviderAttestation__OutputPath"
                ] = output_path
                self.assert_compose_error(
                    model,
                    "COMPOSE_ATTESTATION_OUTPUT_PATH_INVALID",
                )

    def test_database_provider_and_connection_are_exact_and_project_bound(self) -> None:
        for provider in (None, "", "Postgres", "sqlserver", True):
            with self.subTest(field="provider", value_type=type(provider).__name__):
                model = valid_compose_model()
                model["services"]["api"]["environment"]["DB_PROVIDER"] = provider
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_DATABASE_PROVIDER_INVALID",
                )

        connection_mutations = (
            "",
            (
                "Server=localhost,1433;"
                f"Database={EXPECTED_DATABASE};User Id=sa;Password={SQL_PASSWORD};"
                "Encrypt=True;TrustServerCertificate=True"
            ),
            (
                "Server=sqlserver,1433;Database=Forged;"
                f"User Id=sa;Password={SQL_PASSWORD};"
                "Encrypt=True;TrustServerCertificate=True"
            ),
            (
                f"Server=sqlserver,1433;Database={EXPECTED_DATABASE};"
                "User Id=sa;Password=different-password;"
                "Encrypt=True;TrustServerCertificate=True"
            ),
            (
                f"Server=sqlserver,1433;Database={EXPECTED_DATABASE};"
                f"User Id=sa;Password={SQL_PASSWORD};Encrypt=False;"
                "TrustServerCertificate=True"
            ),
        )
        for connection_string in connection_mutations:
            with self.subTest(connection_length=len(connection_string)):
                model = valid_compose_model()
                model["services"]["api"]["environment"][
                    "DB_CONNECTION_STRING"
                ] = connection_string
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_DATABASE_CONNECTION_INVALID",
                )

    def test_cursor_signing_key_must_be_canonical_base64(self) -> None:
        for cursor_key in (
            None,
            "",
            "not-base64!",
            base64.b64encode(b"short").decode("ascii"),
            f"{CURSOR_KEY}=",
            f" {CURSOR_KEY}",
        ):
            with self.subTest(value_type=type(cursor_key).__name__):
                model = valid_compose_model()
                model["services"]["api"]["environment"][
                    "Counterparty__CursorSigningKey"
                ] = cursor_key
                self.assert_compose_error(model, "COMPOSE_API_CURSOR_KEY_INVALID")

    def test_bootstrap_identity_values_are_exact(self) -> None:
        for field in (
            "BootstrapIdentity__HeaderName",
            "BootstrapIdentity__Users__0__UserId",
            "BootstrapIdentity__Users__0__DisplayName",
        ):
            with self.subTest(field=field):
                model = valid_compose_model()
                model["services"]["api"]["environment"][field] = "forged"
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_BOOTSTRAP_IDENTITY_INVALID",
                )

    def test_api_and_sql_service_labels_bind_suite_and_commit(self) -> None:
        for service_name, error_code in (
            ("api", "COMPOSE_API_LABELS_INVALID"),
            ("sqlserver", "COMPOSE_SQL_LABELS_INVALID"),
        ):
            for label_name in (
                "com.goymen.e2e.suite",
                "com.goymen.e2e.tested-commit",
            ):
                for mutation in ("missing", "wrong"):
                    with self.subTest(
                        service=service_name,
                        label=label_name,
                        mutation=mutation,
                    ):
                        model = valid_compose_model()
                        labels = model["services"][service_name]["labels"]
                        if mutation == "missing":
                            del labels[label_name]
                        else:
                            labels[label_name] = "untrusted"
                        self.assert_compose_error(model, error_code)

            model = valid_compose_model()
            model["services"][service_name]["labels"] = []
            self.assert_compose_error(model, error_code)

    def test_api_build_and_labels_are_required_objects(self) -> None:
        for build in (None, ".", []):
            with self.subTest(field="build", value_type=type(build).__name__):
                model = valid_compose_model()
                model["services"]["api"]["build"] = build
                self.assert_compose_error(model, "COMPOSE_API_BUILD_INVALID")

        for labels in (None, [], "labels"):
            with self.subTest(field="labels", value_type=type(labels).__name__):
                model = valid_compose_model()
                model["services"]["api"]["build"]["labels"] = labels
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_BUILD_LABELS_INVALID",
                )

    def test_api_build_labels_bind_suite_and_oci_revision(self) -> None:
        mutations = (
            ("com.goymen.e2e.suite", "missing"),
            ("com.goymen.e2e.suite", "wrong"),
            ("org.opencontainers.image.revision", "missing"),
            ("org.opencontainers.image.revision", "wrong"),
        )
        for label_name, mutation in mutations:
            with self.subTest(label=label_name, mutation=mutation):
                model = valid_compose_model()
                labels = model["services"]["api"]["build"]["labels"]
                if mutation == "missing":
                    del labels[label_name]
                else:
                    labels[label_name] = "untrusted"
                error_code = (
                    "COMPOSE_API_BUILD_LABELS_INVALID"
                )
                self.assert_compose_error(model, error_code)

    def test_sql_image_must_match_the_exact_digest(self) -> None:
        for image in (
            None,
            "mcr.microsoft.com/mssql/server:latest",
            "mcr.microsoft.com/mssql/server@sha256:" + "0" * 64,
        ):
            with self.subTest(value_type=type(image).__name__):
                model = valid_compose_model()
                model["services"]["sqlserver"]["image"] = image
                self.assert_compose_error(model, "COMPOSE_SQL_IMAGE_INVALID")

    def test_sql_environment_key_set_and_fixed_values_are_exact(self) -> None:
        for field in ("ACCEPT_EULA", "MSSQL_PID", "MSSQL_SA_PASSWORD"):
            with self.subTest(missing_field=field):
                model = valid_compose_model()
                del model["services"]["sqlserver"]["environment"][field]
                self.assert_compose_error(
                    model,
                    "COMPOSE_SQL_ENVIRONMENT_FIELDS_INVALID",
                )

        model = valid_compose_model()
        model["services"]["sqlserver"]["environment"]["EXTRA"] = "forged"
        self.assert_compose_error(
            model,
            "COMPOSE_SQL_ENVIRONMENT_FIELDS_INVALID",
        )

        for field, value in (
            ("ACCEPT_EULA", "N"),
            ("MSSQL_PID", "Express"),
        ):
            with self.subTest(field=field):
                model = valid_compose_model()
                model["services"]["sqlserver"]["environment"][field] = value
                self.assert_compose_error(model, "COMPOSE_SQL_ENVIRONMENT_INVALID")

    def test_sql_password_is_nonempty_safe_and_matches_api_connection(self) -> None:
        for password in (None, "", " password", "password ", "bad;password", "bad\nvalue"):
            with self.subTest(value_type=type(password).__name__):
                model = valid_compose_model()
                model["services"]["sqlserver"]["environment"][
                    "MSSQL_SA_PASSWORD"
                ] = password
                self.assert_compose_error(model, "COMPOSE_SQL_PASSWORD_INVALID")

        model = valid_compose_model()
        model["services"]["sqlserver"]["environment"][
            "MSSQL_SA_PASSWORD"
        ] = "DifferentSyntheticPassword123!"
        self.assert_compose_error(
            model,
            "COMPOSE_API_DATABASE_CONNECTION_INVALID",
        )

    def test_network_topology_is_exact_and_project_derived(self) -> None:
        network_mutations = (
            {},
            {"default": None},
            {
                "default": {
                    "name": "forged_default",
                    "ipam": {},
                }
            },
            {
                "default": {
                    "name": f"{EXPECTED_PROJECT}_default",
                    "ipam": {},
                },
                "host": {"name": "forged_host", "ipam": {}},
            },
        )
        for networks in network_mutations:
            with self.subTest(root_networks=networks):
                model = valid_compose_model()
                model["networks"] = networks
                self.assert_compose_error(
                    model,
                    "COMPOSE_NETWORK_TOPOLOGY_INVALID",
                )

        for service in ("api", "sqlserver"):
            for networks in ({}, {"host": None}, {"default": {}}):
                with self.subTest(service=service, service_networks=networks):
                    model = valid_compose_model()
                    model["services"][service]["networks"] = networks
                    self.assert_compose_error(
                        model,
                        "COMPOSE_SERVICE_NETWORK_TOPOLOGY_INVALID",
                    )

    def test_volume_topology_is_exact_and_project_derived(self) -> None:
        volume_mutations = (
            {},
            {
                "c01-normal-sqlserver-data": {
                    "name": "forged-volume",
                    "labels": valid_compose_model()["services"]["sqlserver"][
                        "labels"
                    ],
                }
            },
            {
                **valid_compose_model()["volumes"],
                "extra": {"name": f"{EXPECTED_PROJECT}_extra", "labels": {}},
            },
        )
        for volumes in volume_mutations:
            with self.subTest(root_volumes=tuple(volumes)):
                model = valid_compose_model()
                model["volumes"] = volumes
                self.assert_compose_error(
                    model,
                    "COMPOSE_VOLUME_TOPOLOGY_INVALID",
                )

        sql_volume_mutations = (
            [],
            [
                {
                    "type": "bind",
                    "source": "/tmp/forged",
                    "target": "/var/opt/mssql",
                    "bind": {},
                }
            ],
            [
                {
                    "type": "volume",
                    "source": "forged",
                    "target": "/var/opt/mssql",
                    "volume": {},
                }
            ],
            [
                {
                    "type": "volume",
                    "source": "c01-normal-sqlserver-data",
                    "target": "/tmp/sql",
                    "volume": {},
                }
            ],
        )
        for volumes in sql_volume_mutations:
            with self.subTest(sql_volumes=volumes):
                model = valid_compose_model()
                model["services"]["sqlserver"]["volumes"] = volumes
                self.assert_compose_error(
                    model,
                    "COMPOSE_SQL_VOLUME_TOPOLOGY_INVALID",
                )

    def test_api_must_depend_on_healthy_sql_service(self) -> None:
        invalid_dependencies = (
            None,
            [],
            {},
            {"sqlserver": None},
            {"sqlserver": {}},
            {"sqlserver": {"condition": "service_started"}},
            {"sqlserver": {"condition": "service_completed_successfully"}},
            {"sqlserver": {"condition": "service_healthy", "required": False}},
            {"sqlserver": {"condition": "service_healthy", "required": 1}},
            {"sqlserver": {"condition": "service_healthy", "required": "true"}},
            {
                "sqlserver": {"condition": "service_healthy", "required": True},
                "forged": {"condition": "service_started", "required": True},
            },
        )
        for dependencies in invalid_dependencies:
            with self.subTest(value_type=type(dependencies).__name__):
                model = valid_compose_model()
                model["services"]["api"]["depends_on"] = dependencies
                self.assert_compose_error(
                    model,
                    "COMPOSE_API_DEPENDENCY_INVALID",
                )


class NormalProviderAttestationCliTests(unittest.TestCase):
    def test_cli_accepts_an_input_path(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = pathlib.Path(temporary_directory) / "attestation.json"
            path.write_text(json.dumps(valid_attestation()), encoding="utf-8")
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                result = validator.main(["--input", str(path)])

        self.assertEqual(0, result)
        self.assertEqual(
            "C-01 normal-provider attestation validation passed.\n",
            stdout.getvalue(),
        )

    def test_cli_accepts_stdin(self) -> None:
        stdout = io.StringIO()
        with mock.patch.object(sys, "stdin", io.StringIO(json.dumps(valid_attestation()))):
            with redirect_stdout(stdout):
                result = validator.main(["--input", "-"])

        self.assertEqual(0, result)
        self.assertIn("validation passed", stdout.getvalue())

    def test_cli_defaults_to_stdin(self) -> None:
        with mock.patch.object(sys, "stdin", io.StringIO(json.dumps(valid_attestation()))):
            with redirect_stdout(io.StringIO()):
                result = validator.main([])

        self.assertEqual(0, result)

    def test_compose_cli_accepts_an_input_path(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = pathlib.Path(temporary_directory) / "compose.json"
            path.write_text(json.dumps(valid_compose_model()), encoding="utf-8")
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                result = validator.main(
                    [
                        "compose",
                        "--input",
                        str(path),
                        "--expected-commit",
                        EXPECTED_COMMIT,
                        "--expected-project",
                        EXPECTED_PROJECT,
                    ]
                )

        self.assertEqual(0, result)
        self.assertEqual(
            "C-01 normal-provider compose validation passed.\n",
            stdout.getvalue(),
        )

    def test_compose_cli_accepts_stdin(self) -> None:
        stdout = io.StringIO()
        with mock.patch.object(
            sys,
            "stdin",
            io.StringIO(json.dumps(valid_compose_model())),
        ):
            with redirect_stdout(stdout):
                result = validator.main(
                    [
                        "compose",
                        "--input",
                        "-",
                        "--expected-commit",
                        EXPECTED_COMMIT,
                        "--expected-project",
                        EXPECTED_PROJECT,
                    ]
                )

        self.assertEqual(0, result)
        self.assertIn("compose validation passed", stdout.getvalue())

    def test_cli_malformed_json_returns_failure_without_echoing_input(self) -> None:
        secret = "do-not-log-this-attestation-value"
        stderr = io.StringIO()
        with mock.patch.object(sys, "stdin", io.StringIO(f'{{"secret":"{secret}"')):
            with redirect_stderr(stderr):
                result = validator.main(["--input", "-"])

        self.assertEqual(1, result)
        self.assertNotIn(secret, stderr.getvalue())
        self.assertIn("ATTESTATION_JSON_MALFORMED", stderr.getvalue())

    def test_cli_contract_failure_does_not_log_values_or_extra_field_names(self) -> None:
        secret_value = "secret-runtime-value"
        secret_field_name = "secret-field-name"
        attestation = copy.deepcopy(valid_attestation())
        attestation["overrideKeysPresent"] = [
            validator.DISPOSABLE_CONFIGURATION_KEYS[0],
            secret_value,
        ]
        attestation[secret_field_name] = secret_value
        stdout = io.StringIO()
        stderr = io.StringIO()
        with mock.patch.object(sys, "stdin", io.StringIO(json.dumps(attestation))):
            with redirect_stdout(stdout), redirect_stderr(stderr):
                result = validator.main(["--input", "-"])

        output = stdout.getvalue() + stderr.getvalue()
        self.assertEqual(1, result)
        self.assertNotIn(secret_value, output)
        self.assertNotIn(secret_field_name, output)
        self.assertNotIn(validator.DISPOSABLE_CONFIGURATION_KEYS[0], output)
        self.assertIn("ATTESTATION_FIELDS_NOT_EXACT", output)

    def test_cli_override_failure_does_not_log_configuration_values(self) -> None:
        secret_value = "secret-effective-configuration-value"
        attestation = valid_attestation()
        attestation["overrideKeysPresent"] = [
            validator.DISPOSABLE_CONFIGURATION_KEYS[0],
            secret_value,
        ]
        stdout = io.StringIO()
        stderr = io.StringIO()
        with mock.patch.object(sys, "stdin", io.StringIO(json.dumps(attestation))):
            with redirect_stdout(stdout), redirect_stderr(stderr):
                result = validator.main(["--input", "-"])

        output = stdout.getvalue() + stderr.getvalue()
        self.assertEqual(1, result)
        self.assertNotIn(secret_value, output)
        self.assertNotIn(validator.DISPOSABLE_CONFIGURATION_KEYS[0], output)
        self.assertIn("ATTESTATION_OVERRIDE_KEYS_PRESENT", output)

    def test_compose_cli_failure_does_not_log_environment_keys_or_values(self) -> None:
        secret_value = "secret-compose-environment-value"
        disposable_key = validator.DISPOSABLE_CONFIGURATION_KEYS[0]
        model = valid_compose_model()
        model["services"]["api"]["environment"][disposable_key] = secret_value
        stdout = io.StringIO()
        stderr = io.StringIO()
        with mock.patch.object(sys, "stdin", io.StringIO(json.dumps(model))):
            with redirect_stdout(stdout), redirect_stderr(stderr):
                result = validator.main(
                    [
                        "compose",
                        "--input",
                        "-",
                        "--expected-commit",
                        EXPECTED_COMMIT,
                        "--expected-project",
                        EXPECTED_PROJECT,
                    ]
                )

        output = stdout.getvalue() + stderr.getvalue()
        self.assertEqual(1, result)
        self.assertNotIn(secret_value, output)
        self.assertNotIn(disposable_key, output)
        self.assertIn("COMPOSE_DISPOSABLE_ENVIRONMENT_KEY_PRESENT", output)


if __name__ == "__main__":
    unittest.main()
