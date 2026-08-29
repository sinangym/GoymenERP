from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
import re
import stat
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


DOCS_RELATIVE_ROOT = Path("docs/evidence/erp-000m")
README_TEMPLATE = DOCS_RELATIVE_ROOT / "README.md"
ENVIRONMENT_TEMPLATE = DOCS_RELATIVE_ROOT / "ENVIRONMENT_INVENTORY_TEMPLATE.md"
WORKLOAD_TEMPLATE = DOCS_RELATIVE_ROOT / "WORKLOAD_PROFILE_TEMPLATE.json"
EVIDENCE_TEMPLATE = DOCS_RELATIVE_ROOT / "EVIDENCE_MANIFEST_TEMPLATE.json"

EXPECTED_DOC_FILE_NAMES = {
    "README.md",
    "ENVIRONMENT_INVENTORY_TEMPLATE.md",
    "WORKLOAD_PROFILE_TEMPLATE.json",
    "EVIDENCE_MANIFEST_TEMPLATE.json",
}

README_MARKER = "<!-- ERP000M_EVIDENCE_INPUT_PACK_README_V1 -->"
README_REQUIRED_LINES = (
    "- Implementation state: `NO-GO / BLOCKED`",
    "- Decision closure: `NONE`",
    "- Artifact purpose: `DECISION-NEUTRAL INPUT TEMPLATE`",
    "Varsayılan doğrulama yalnız boş ve karar-nötr şablon bütünlüğünü kanıtlar. Doldurulmuş",
    "paket modu da yalnız byte bütünlüğünü ve hash bağlarını kanıtlar. Kanıtın işsel",
    "doğruluğu, ortam parity'si, test sonucu veya herhangi bir kabul kapısının kapanması",
    "anlamına gelmez.",
)
README_CANONICAL_SHA256 = (
    "253484D5CF85393DE16BFD3B6F81BE947F7FECA9EB70187F07DF29F850BE6190"
)
ENVIRONMENT_CANONICAL_SHA256 = (
    "31972E106E1F3C9A0912AA2A33F2311A5EE2377D477657BECF735B77D4FBCC23"
)

PLACEHOLDER_PATTERN = re.compile(
    r"^__REQUIRED_INPUT__:[A-Z0-9_-]+(?::[A-Z0-9_-]+)*__$"
)
SELF_HASH_HEX_PATTERN = re.compile(r"^[0-9A-F]{64}$")


@dataclass(frozen=True)
class SelfHashSpec:
    artifact_name: str
    template_id: str | None
    json_path: tuple[str, ...] | None
    field_name: str
    sentinel: str
    markdown_marker: str | None = None


@dataclass(frozen=True)
class SelfHashComputation:
    stored_value: str
    computed_sha256: str


@dataclass(frozen=True)
class CapturedArtifact:
    path: Path
    raw_bytes: bytes
    text: str


ENVIRONMENT_SELF_HASH_SPEC = SelfHashSpec(
    artifact_name="environment inventory",
    template_id=None,
    json_path=None,
    field_name="inventory_hash_sha256",
    sentinel="__REQUIRED_INPUT__:ENVIRONMENT_INVENTORY_HASH_SHA256__",
    markdown_marker="<!-- ERP000M_ENVIRONMENT_INVENTORY_TEMPLATE_V1 -->",
)
WORKLOAD_SELF_HASH_SPEC = SelfHashSpec(
    artifact_name="workload profile",
    template_id="ERP-000M-WORKLOAD-PROFILE-TEMPLATE-V1",
    json_path=("evidence", "workload_profile_hash_sha256"),
    field_name="workload_profile_hash_sha256",
    sentinel="__REQUIRED_INPUT__:WORKLOAD_PROFILE_HASH_SHA256__",
)
MANIFEST_SELF_HASH_SPEC = SelfHashSpec(
    artifact_name="evidence manifest",
    template_id="ERP-000M-EVIDENCE-MANIFEST-TEMPLATE-V1",
    json_path=("reproducibility", "manifest_hash_sha256"),
    field_name="manifest_hash_sha256",
    sentinel="__REQUIRED_INPUT__:MANIFEST_HASH_SHA256__",
)
SELF_HASH_SPECS = (
    ENVIRONMENT_SELF_HASH_SPEC,
    WORKLOAD_SELF_HASH_SPEC,
    MANIFEST_SELF_HASH_SPEC,
)

SENSITIVE_KEY_PATTERN = re.compile(
    r"(?:password|passwd|pwd|secret|token|api[_-]?key|access[_-]?key|"
    r"private[_-]?key|connection[_-]?string|client[_-]?secret|credential)",
    re.IGNORECASE,
)

SENSITIVE_VALUE_PATTERNS = (
    re.compile(
        r"(?:password|passwd|pwd|client[_-]?secret|api[_-]?key|access[_-]?key|"
        r"accountkey)\s*[:=]\s*[^\s,;}{]{4,}",
        re.IGNORECASE,
    ),
    re.compile(r"\bBearer\s+[A-Za-z0-9._~+/-]{12,}", re.IGNORECASE),
    re.compile(r"\bgh[pousr]_[A-Za-z0-9]{20,}\b"),
    re.compile(r"\bsk-[A-Za-z0-9_-]{20,}\b"),
    re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
    re.compile(
        r"\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b"
    ),
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    re.compile(
        r"(?:(?:^|[?&;\s])(?:sig|signature)=|x-amz-signature\s*=)"
        r"[A-Za-z0-9%+/_=-]{8,}",
        re.IGNORECASE,
    ),
)

SENSITIVE_RAW_NAME_PATTERN = re.compile(
    r"(?<![A-Za-z0-9])(?:password|passwd|pwd|secret|token|credential|"
    r"connection[_ -]?string|api[_ -]?key|access[_ -]?key|private[_ -]?key|"
    r"client[_ -]?secret)(?![A-Za-z0-9])",
    re.IGNORECASE,
)

DECISION_ASSIGNMENT_PATTERN = re.compile(
    r"(?i)(?:[\"'`]?)\b(?:provider|edition|collation|isolation(?:_level)?|"
    r"topology|threshold)\b(?:[\"'`]?)\s*[:=]\s*"
    r"(?![\"'`]?__REQUIRED_INPUT__:)[\"'`]?[A-Za-z0-9]"
)

CLOSED_GATE_PATTERN = re.compile(
    r"(?i)\b(?:gate|gates|decision(?:_|\s)?closure)\b\s*[:=]\s*"
    r"[\"'`]?(?:closed|done|accepted|yes|true)\b"
)

IMPLEMENTATION_GO_PATTERN = re.compile(
    r"(?i)\bimplementation(?:_|\s)?(?:state|status)?\b\s*[:=]\s*"
    r"[\"'`]?go\b"
)

FORBIDDEN_DEFAULT_PATTERNS = (
    re.compile(r"\b(?:SQL\s+Server|PostgreSQL|MySQL|MariaDB|Oracle)\b", re.IGNORECASE),
    re.compile(r"\b(?:Developer|Express|Standard|Enterprise)\s+Edition\b", re.IGNORECASE),
    re.compile(r"\b(?:Turkish|Latin1)_[A-Za-z0-9_]+_(?:CI|CS)_(?:AS|AI)\b", re.IGNORECASE),
    re.compile(
        r"\b(?:read\s+committed|read\s+uncommitted|repeatable\s+read|snapshot|serializable)\b",
        re.IGNORECASE,
    ),
    re.compile(
        r"\b(?:on[- ]prem(?:ises)?|single[- ]node|active[- ]passive|"
        r"availability\s+group|failover\s+cluster)\b",
        re.IGNORECASE,
    ),
    re.compile(
        r"\b(?:p\d{2}|rpo|rto|latency|throughput|timeout)\b[^\r\n]{0,48}"
        r"(?:<=|>=|=|:)\s*\d+(?:\.\d+)?\s*(?:ms|s|sec(?:ond)?s?|"
        r"min(?:ute)?s?|hours?|tps|rps|%)\b",
        re.IGNORECASE,
    ),
)

PROTECTED_INPUT_KEYS = {
    "provider",
    "provider_candidate",
    "provider_version",
    "edition",
    "compatibility_level",
    "collation",
    "database_collation",
    "full_text_language",
    "full_text_capability",
    "isolation_level",
    "topology",
    "disaster_topology",
    "rpo",
    "rto",
    "record_count",
    "duration",
    "concurrent_actor_count",
    "read_concurrency",
    "write_concurrency",
    "long_running_operation_concurrency",
    "mix_share",
    "arrival_pattern",
    "volume_per_window",
    "threshold",
    "measured_value",
    "measurement_window",
    "byte_count",
}

ENVIRONMENT_SECTIONS = (
    (
        "## Sahiplik ve kaynak",
        (
            ("owner", "OWNER", "OWNER_EVIDENCE_REF"),
            ("source", "SOURCE", "SOURCE_EVIDENCE_REF"),
            ("captured_at_utc", "CAPTURED_AT_UTC", "TIMESTAMP_EVIDENCE_REF"),
            ("source_commit", "SOURCE_COMMIT", "COMMIT_EVIDENCE_REF"),
            (
                "collector_tool_name",
                "COLLECTOR_TOOL_NAME",
                "COLLECTOR_TOOL_EVIDENCE_REF",
            ),
            (
                "collector_tool_version",
                "COLLECTOR_TOOL_VERSION",
                "COLLECTOR_TOOL_VERSION_EVIDENCE_REF",
            ),
            (
                "database_client_tool_name",
                "DATABASE_CLIENT_TOOL_NAME",
                "DATABASE_CLIENT_TOOL_EVIDENCE_REF",
            ),
            (
                "database_client_tool_version",
                "DATABASE_CLIENT_TOOL_VERSION",
                "DATABASE_CLIENT_TOOL_VERSION_EVIDENCE_REF",
            ),
            (
                "collection_runbook_ref",
                "COLLECTION_RUNBOOK_REF",
                "RUNBOOK_EVIDENCE_REF",
            ),
        ),
    ),
    (
        "## Anonim ortam ve capability envanteri",
        (
            (
                "anonymized_environment_id",
                "ANONYMIZED_ENVIRONMENT_ID",
                "ENVIRONMENT_ID_EVIDENCE_REF",
            ),
            (
                "environment_class",
                "ENVIRONMENT_CLASS",
                "ENVIRONMENT_CLASS_EVIDENCE_REF",
            ),
            (
                "provider_candidate",
                "ADR-003:PROVIDER",
                "PROVIDER_EVIDENCE_REF",
            ),
            (
                "provider_version",
                "ADR-003:PROVIDER_VERSION",
                "PROVIDER_VERSION_EVIDENCE_REF",
            ),
            ("edition", "ADR-003:EDITION", "EDITION_EVIDENCE_REF"),
            (
                "compatibility_level",
                "ADR-003:COMPATIBILITY_LEVEL",
                "COMPATIBILITY_EVIDENCE_REF",
            ),
            (
                "database_collation",
                "ADR-003:COLLATION",
                "COLLATION_EVIDENCE_REF",
            ),
            (
                "full_text_language",
                "ADR-003:FULL_TEXT_LANGUAGE",
                "FULL_TEXT_LANGUAGE_EVIDENCE_REF",
            ),
            (
                "full_text_capability",
                "ADR-003:FULL_TEXT_CAPABILITY",
                "FULL_TEXT_CAPABILITY_EVIDENCE_REF",
            ),
            (
                "isolation_level",
                "ADR-003:ISOLATION_LEVEL",
                "ISOLATION_EVIDENCE_REF",
            ),
            ("topology", "OPEN-017:TOPOLOGY", "TOPOLOGY_EVIDENCE_REF"),
            (
                "operating_system",
                "OPEN-014:OPERATING_SYSTEM",
                "OPERATING_SYSTEM_EVIDENCE_REF",
            ),
            ("cpu_inventory", "OPEN-014:CPU_INVENTORY", "CPU_EVIDENCE_REF"),
            (
                "memory_inventory",
                "OPEN-014:MEMORY_INVENTORY",
                "MEMORY_EVIDENCE_REF",
            ),
            ("disk_inventory", "OPEN-014:DISK_INVENTORY", "DISK_EVIDENCE_REF"),
            (
                "network_inventory",
                "OPEN-014:NETWORK_INVENTORY",
                "NETWORK_EVIDENCE_REF",
            ),
            ("ups_inventory", "OPEN-014:UPS_INVENTORY", "UPS_EVIDENCE_REF"),
            (
                "backup_inventory",
                "OPEN-014:BACKUP_INVENTORY",
                "BACKUP_EVIDENCE_REF",
            ),
            (
                "environment_parity_notes",
                "ENVIRONMENT_PARITY_NOTES",
                "PARITY_EVIDENCE_REF",
            ),
            (
                "capability_drift_status",
                "CAPABILITY_DRIFT_STATUS",
                "DRIFT_EVIDENCE_REF",
            ),
        ),
    ),
    (
        "## Kimlik ve güven sınırları",
        (
            (
                "runtime_identity_boundary",
                "RUNTIME_IDENTITY_BOUNDARY",
                "RUNTIME_IDENTITY_EVIDENCE_REF",
            ),
            (
                "migration_identity_boundary",
                "ADR-010:MIGRATION_IDENTITY_BOUNDARY",
                "MIGRATION_IDENTITY_EVIDENCE_REF",
            ),
            (
                "restore_identity_boundary",
                "OPEN-017:RESTORE_IDENTITY_BOUNDARY",
                "RESTORE_IDENTITY_EVIDENCE_REF",
            ),
            (
                "grant_revoke_audit_ref",
                "GRANT_REVOKE_AUDIT_REF",
                "GRANT_REVOKE_EVIDENCE_REF",
            ),
        ),
    ),
    (
        "## Recovery girdileri",
        (
            ("rpo", "OPEN-017:RPO", "RPO_DECISION_EVIDENCE_REF"),
            ("rto", "OPEN-017:RTO", "RTO_DECISION_EVIDENCE_REF"),
            (
                "disaster_topology",
                "OPEN-017:DISASTER_TOPOLOGY",
                "DISASTER_TOPOLOGY_EVIDENCE_REF",
            ),
            (
                "restore_target_isolation",
                "OPEN-017:RESTORE_TARGET_ISOLATION",
                "RESTORE_ISOLATION_EVIDENCE_REF",
            ),
        ),
    ),
    (
        "## Redaksiyon ve tekrar üretim",
        (
            (
                "anonymization_method",
                "ANONYMIZATION_METHOD",
                "ANONYMIZATION_EVIDENCE_REF",
            ),
            (
                "sensitive_material_redaction_status",
                "SENSITIVE_MATERIAL_REDACTION_STATUS",
                "SENSITIVE_MATERIAL_SCAN_EVIDENCE_REF",
            ),
            (
                "redaction_reviewer",
                "REDACTION_REVIEW_OWNER",
                "REDACTION_REVIEW_EVIDENCE_REF",
            ),
            (
                "redaction_reviewed_at_utc",
                "REDACTION_REVIEWED_AT_UTC",
                "REDACTION_TIMESTAMP_EVIDENCE_REF",
            ),
            (
                "inventory_hash_sha256",
                "ENVIRONMENT_INVENTORY_HASH_SHA256",
                "ENVIRONMENT_INVENTORY_HASH_EVIDENCE_REF",
            ),
            (
                "evidence_bundle_ref",
                "EVIDENCE_BUNDLE_REF",
                "BUNDLE_INDEX_EVIDENCE_REF",
            ),
        ),
    ),
)

ENVIRONMENT_FIELD_EXPECTATIONS = {
    field_id: (value_identity, evidence_identity, section)
    for section, fields in ENVIRONMENT_SECTIONS
    for field_id, value_identity, evidence_identity in fields
}
ENVIRONMENT_FIELD_IDS = set(ENVIRONMENT_FIELD_EXPECTATIONS)


class _RequiredPlaceholder:
    def __init__(self, identity: str) -> None:
        self.value = f"__REQUIRED_INPUT__:{identity}__"


def required(identity: str) -> _RequiredPlaceholder:
    return _RequiredPlaceholder(identity)


WORKLOAD_SCHEMA: dict[str, Any] = {
    "template_id": "ERP-000M-WORKLOAD-PROFILE-TEMPLATE-V1",
    "artifact_state": "INPUT_TEMPLATE",
    "decision_state": "UNDECIDED",
    "metadata": {
        "owner": required("OWNER"),
        "source": required("SOURCE"),
        "captured_at_utc": required("CAPTURED_AT_UTC"),
        "source_commit": required("SOURCE_COMMIT"),
        "tool_versions": [
            {
                "tool_role": required("TOOL_ROLE"),
                "tool_name": required("TOOL_NAME"),
                "tool_version": required("TOOL_VERSION"),
                "evidence_ref": required("TOOL_VERSION_EVIDENCE_REF"),
            }
        ],
    },
    "anonymization": {
        "dataset_class": required("ANONYMIZED_DATASET_CLASS"),
        "anonymization_method": required("ANONYMIZATION_METHOD"),
        "source_approval_ref": required("SOURCE_APPROVAL_REF"),
        "review_owner": required("ANONYMIZATION_REVIEW_OWNER"),
        "reviewed_at_utc": required("ANONYMIZATION_REVIEWED_AT_UTC"),
    },
    "dataset": {
        "generator": {
            "name": required("GENERATOR_NAME"),
            "version": required("GENERATOR_VERSION"),
            "source_commit": required("GENERATOR_SOURCE_COMMIT"),
            "seed": required("GENERATOR_SEED"),
            "configuration_hash_sha256": required(
                "GENERATOR_CONFIGURATION_HASH_SHA256"
            ),
            "dataset_hash_sha256": required("DATASET_HASH_SHA256"),
            "reproduction_instructions_ref": required(
                "REPRODUCTION_INSTRUCTIONS_REF"
            ),
        },
        "volume_classes": [
            {
                "entity_class": required("ANONYMIZED_ENTITY_CLASS"),
                "record_count": required("OPEN-015:RECORD_COUNT"),
                "distribution_description": required(
                    "OPEN-015:DISTRIBUTION_DESCRIPTION"
                ),
                "source_ref": required("VOLUME_SOURCE_REF"),
            }
        ],
    },
    "workload": {
        "window": {
            "window_name": required("ANONYMIZED_WINDOW_NAME"),
            "duration": required("OPEN-015:MEASUREMENT_WINDOW"),
            "warm_cold_condition": required("WARM_COLD_CONDITION"),
            "source_ref": required("WINDOW_SOURCE_REF"),
        },
        "concurrency": {
            "concurrent_actor_count": required("OPEN-015:CONCURRENT_ACTOR_COUNT"),
            "read_concurrency": required("OPEN-015:READ_CONCURRENCY"),
            "write_concurrency": required("OPEN-015:WRITE_CONCURRENCY"),
            "long_running_operation_concurrency": required(
                "OPEN-015:LONG_RUNNING_OPERATION_CONCURRENCY"
            ),
            "source_ref": required("CONCURRENCY_SOURCE_REF"),
        },
        "operation_classes": [
            {
                "operation_class": required("ANONYMIZED_OPERATION_CLASS"),
                "read_write_class": required("READ_WRITE_CLASS"),
                "mix_share": required("OPEN-015:OPERATION_MIX_SHARE"),
                "arrival_pattern": required("OPEN-015:ARRIVAL_PATTERN"),
                "volume_per_window": required(
                    "OPEN-015:OPERATION_VOLUME_PER_WINDOW"
                ),
                "scenario_ref": required("TASK_LOCAL_SCENARIO_REF"),
                "source_ref": required("OPERATION_SOURCE_REF"),
            }
        ],
    },
    "candidate_inputs": {
        "provider": required("ADR-003:PROVIDER"),
        "edition": required("ADR-003:EDITION"),
        "collation": required("ADR-003:COLLATION"),
        "full_text_language": required("ADR-003:FULL_TEXT_LANGUAGE"),
        "isolation_level": required("ADR-003:ISOLATION_LEVEL"),
        "topology": required("OPEN-017:TOPOLOGY"),
    },
    "performance_acceptance_inputs": [
        {
            "metric_name": required("OPEN-015:METRIC_NAME"),
            "threshold": required("OPEN-015:THRESHOLD"),
            "unit": required("OPEN-015:THRESHOLD_UNIT"),
            "decision_ref": required("THRESHOLD_DECISION_REF"),
        }
    ],
    "evidence": {
        "environment_inventory_ref": required("ENVIRONMENT_INVENTORY_REF"),
        "evidence_manifest_ref": required("EVIDENCE_MANIFEST_REF"),
        "workload_profile_hash_sha256": required("WORKLOAD_PROFILE_HASH_SHA256"),
        "evidence_refs": [required("WORKLOAD_EVIDENCE_REF")],
    },
    "redaction_control": {
        "anonymization_attestation": required("ANONYMIZATION_ATTESTATION"),
        "sensitive_material_scan_tool": required("SENSITIVE_MATERIAL_SCAN_TOOL"),
        "sensitive_material_scan_tool_version": required(
            "SENSITIVE_MATERIAL_SCAN_TOOL_VERSION"
        ),
        "scan_evidence_ref": required("SENSITIVE_MATERIAL_SCAN_EVIDENCE_REF"),
        "review_owner": required("REDACTION_REVIEW_OWNER"),
        "reviewed_at_utc": required("REDACTION_REVIEWED_AT_UTC"),
    },
}


EVIDENCE_SCHEMA: dict[str, Any] = {
    "template_id": "ERP-000M-EVIDENCE-MANIFEST-TEMPLATE-V1",
    "artifact_state": "INPUT_TEMPLATE",
    "decision_state": "UNDECIDED",
    "metadata": {
        "owner": required("OWNER"),
        "source": required("SOURCE"),
        "captured_at_utc": required("CAPTURED_AT_UTC"),
        "source_commit": required("SOURCE_COMMIT"),
        "tool_versions": [
            {
                "tool_role": required("TOOL_ROLE"),
                "tool_name": required("TOOL_NAME"),
                "tool_version": required("TOOL_VERSION"),
                "evidence_ref": required("TOOL_VERSION_EVIDENCE_REF"),
            }
        ],
    },
    "environment": {
        "inventory_ref": required("ENVIRONMENT_INVENTORY_REF"),
        "inventory_hash_sha256": required("ENVIRONMENT_INVENTORY_HASH_SHA256"),
        "provider": required("ADR-003:PROVIDER"),
        "edition": required("ADR-003:EDITION"),
        "collation": required("ADR-003:COLLATION"),
        "full_text_language": required("ADR-003:FULL_TEXT_LANGUAGE"),
        "isolation_level": required("ADR-003:ISOLATION_LEVEL"),
        "topology": required("OPEN-017:TOPOLOGY"),
    },
    "workload": {
        "profile_ref": required("WORKLOAD_PROFILE_REF"),
        "profile_hash_sha256": required("WORKLOAD_PROFILE_HASH_SHA256"),
        "generator_version": required("GENERATOR_VERSION"),
        "generator_seed": required("GENERATOR_SEED"),
        "generator_configuration_hash_sha256": required(
            "GENERATOR_CONFIGURATION_HASH_SHA256"
        ),
        "dataset_hash_sha256": required("DATASET_HASH_SHA256"),
        "reproduction_instructions_ref": required("REPRODUCTION_INSTRUCTIONS_REF"),
    },
    "artifacts": [
        {
            "evidence_id": required("EVIDENCE_ID"),
            "evidence_class": required("EVIDENCE_CLASS"),
            "path_or_uri_ref": required("EVIDENCE_LOCATION_REF"),
            "source_ref": required("EVIDENCE_SOURCE_REF"),
            "captured_at_utc": required("EVIDENCE_CAPTURED_AT_UTC"),
            "source_commit": required("EVIDENCE_SOURCE_COMMIT"),
            "producing_tool_name": required("PRODUCING_TOOL_NAME"),
            "producing_tool_version": required("PRODUCING_TOOL_VERSION"),
            "sha256": required("EVIDENCE_SHA256"),
            "byte_count": required("EVIDENCE_BYTE_COUNT"),
            "environment_ref": required("ARTIFACT_ENVIRONMENT_REF"),
            "workload_ref": required("ARTIFACT_WORKLOAD_REF"),
            "redaction_evidence_ref": required("ARTIFACT_REDACTION_EVIDENCE_REF"),
        }
    ],
    "measurements": [
        {
            "measurement_id": required("MEASUREMENT_ID"),
            "operation_class_ref": required("ANONYMIZED_OPERATION_CLASS_REF"),
            "metric_name": required("METRIC_NAME"),
            "measured_value": required("MEASURED_VALUE"),
            "unit": required("MEASUREMENT_UNIT"),
            "measurement_window": required("MEASUREMENT_WINDOW"),
            "warm_cold_condition": required("WARM_COLD_CONDITION"),
            "threshold": required("OPEN-015:THRESHOLD"),
            "threshold_decision_ref": required("THRESHOLD_DECISION_REF"),
            "outcome": required("HUMAN_REVIEWED_OUTCOME"),
            "evidence_refs": [required("MEASUREMENT_EVIDENCE_REF")],
        }
    ],
    "reproducibility": {
        "execution_runbook_ref": required("EXECUTION_RUNBOOK_REF"),
        "cleanup_runbook_ref": required("CLEANUP_RUNBOOK_REF"),
        "failure_result_ref": required("FAILURE_RESULT_REF"),
        "manifest_hash_sha256": required("MANIFEST_HASH_SHA256"),
        "reproduction_review_owner": required("REPRODUCTION_REVIEW_OWNER"),
        "reproduction_reviewed_at_utc": required("REPRODUCTION_REVIEWED_AT_UTC"),
    },
    "redaction_control": {
        "sensitive_material_policy_ref": required("SENSITIVE_MATERIAL_POLICY_REF"),
        "scan_tool_name": required("SENSITIVE_MATERIAL_SCAN_TOOL"),
        "scan_tool_version": required("SENSITIVE_MATERIAL_SCAN_TOOL_VERSION"),
        "scan_evidence_ref": required("SENSITIVE_MATERIAL_SCAN_EVIDENCE_REF"),
        "review_owner": required("REDACTION_REVIEW_OWNER"),
        "reviewed_at_utc": required("REDACTION_REVIEWED_AT_UTC"),
    },
}


class DuplicateKeyError(ValueError):
    pass


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateKeyError(f"duplicate key {key!r}")
        result[key] = value
    return result


def _reject_non_finite(value: str) -> None:
    raise ValueError(f"non-finite JSON number {value!r}")


def _load_strict_json(path: Path) -> tuple[Any | None, list[str]]:
    try:
        raw = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        return None, [f"{path}: cannot read UTF-8 JSON: {exc}"]

    try:
        parsed = json.loads(
            raw,
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_non_finite,
        )
    except (json.JSONDecodeError, DuplicateKeyError, ValueError) as exc:
        return None, [f"{path}: strict JSON parse failed: {exc}"]
    return parsed, []


def _format_path(path: tuple[str, ...]) -> str:
    return ".".join(path) if path else "<root>"


def _is_placeholder(value: Any) -> bool:
    return isinstance(value, str) and PLACEHOLDER_PATTERN.fullmatch(value) is not None


def _validate_schema(
    actual: Any,
    expected: Any,
    path: tuple[str, ...],
    errors: list[str],
) -> None:
    display_path = _format_path(path)

    if isinstance(expected, _RequiredPlaceholder):
        if actual == expected.value:
            return
        if path and path[-1] in PROTECTED_INPUT_KEYS:
            errors.append(
                f"{display_path}: forbidden concrete default; value must remain an "
                f"exact placeholder {expected.value!r}"
            )
        else:
            errors.append(
                f"{display_path}: expected exact placeholder {expected.value!r}"
            )
        return

    if isinstance(expected, str):
        if actual != expected:
            errors.append(f"{display_path}: expected fixed value {expected!r}")
        return

    if isinstance(expected, dict):
        if not isinstance(actual, dict):
            errors.append(f"{display_path}: expected JSON object")
            return
        expected_keys = set(expected)
        actual_keys = set(actual)
        for missing in sorted(expected_keys - actual_keys):
            errors.append(f"{display_path}: missing required key {missing!r}")
        for unknown in sorted(actual_keys - expected_keys):
            errors.append(f"{display_path}: unknown key {unknown!r}")
        for key in sorted(expected_keys & actual_keys):
            _validate_schema(actual[key], expected[key], (*path, key), errors)
        return

    if isinstance(expected, list):
        if not isinstance(actual, list):
            errors.append(f"{display_path}: expected JSON array")
            return
        if len(actual) != len(expected):
            errors.append(
                f"{display_path}: template array must contain exactly {len(expected)} row"
            )
            return
        for index, (item, item_schema) in enumerate(zip(actual, expected)):
            _validate_schema(item, item_schema, (*path, f"[{index}]"), errors)
        return

    errors.append(f"{display_path}: validator schema is unsupported")


def _scan_sensitive_shape(value: Any, path: tuple[str, ...], errors: list[str]) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            child_path = (*path, key)
            if SENSITIVE_KEY_PATTERN.search(key):
                errors.append(
                    f"{_format_path(child_path)}: sensitive-looking key is forbidden"
                )
            _scan_sensitive_shape(child, child_path, errors)
        return

    if isinstance(value, list):
        for index, child in enumerate(value):
            _scan_sensitive_shape(child, (*path, f"[{index}]"), errors)
        return

    if isinstance(value, str):
        for pattern in SENSITIVE_VALUE_PATTERNS:
            if pattern.search(value):
                errors.append(
                    f"{_format_path(path)}: sensitive-looking value is forbidden"
                )
                break


def _scan_forbidden_defaults(value: Any, path: tuple[str, ...], errors: list[str]) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            _scan_forbidden_defaults(child, (*path, key), errors)
        return

    if isinstance(value, list):
        for index, child in enumerate(value):
            _scan_forbidden_defaults(child, (*path, f"[{index}]"), errors)
        return

    if isinstance(value, str) and not _is_placeholder(value):
        for pattern in FORBIDDEN_DEFAULT_PATTERNS:
            if pattern.search(value):
                errors.append(
                    f"{_format_path(path)}: forbidden concrete default detected"
                )
                break


def validate_json_template(path: Path, schema: dict[str, Any]) -> list[str]:
    parsed, errors = _load_strict_json(path)
    if errors:
        return errors
    assert parsed is not None

    _validate_schema(parsed, schema, (), errors)
    _scan_sensitive_shape(parsed, (), errors)
    _scan_forbidden_defaults(parsed, (), errors)
    try:
        raw = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        errors.append(f"{path}: cannot scan UTF-8 content: {exc}")
    else:
        _scan_raw_text(raw, path, errors)
    return errors


def _scan_raw_text(raw: str, path: Path, errors: list[str]) -> None:
    if SENSITIVE_RAW_NAME_PATTERN.search(raw):
        errors.append(f"{path}: sensitive-looking raw key/value name is forbidden")

    for pattern in SENSITIVE_VALUE_PATTERNS:
        if pattern.search(raw):
            errors.append(f"{path}: sensitive-looking raw content is forbidden")
            break

    for pattern in FORBIDDEN_DEFAULT_PATTERNS:
        if pattern.search(raw):
            errors.append(f"{path}: forbidden concrete default detected")
            break

    if DECISION_ASSIGNMENT_PATTERN.search(raw):
        errors.append(f"{path}: decision/default assignment drift is forbidden")
    if CLOSED_GATE_PATTERN.search(raw):
        errors.append(f"{path}: closed-gate drift is forbidden")
    if IMPLEMENTATION_GO_PATTERN.search(raw):
        errors.append(f"{path}: implementation GO drift is forbidden")


def _parse_environment_rows(
    raw: str,
    path: Path,
) -> tuple[dict[str, tuple[str, str, str]], list[str]]:
    rows: dict[str, tuple[str, str, str]] = {}
    errors: list[str] = []
    expected_headings = tuple(section for section, _ in ENVIRONMENT_SECTIONS)
    seen_headings: list[str] = []
    section_structure = {section: [] for section in expected_headings}
    current_section: str | None = None
    fence_character: str | None = None

    for line_number, line in enumerate(raw.splitlines(), start=1):
        stripped = line.strip()
        fence_match = re.match(r"^(`{3,}|~{3,})", stripped)
        if fence_match:
            delimiter_character = fence_match.group(1)[0]
            if fence_character is None:
                fence_character = delimiter_character
            elif delimiter_character == fence_character:
                fence_character = None
            continue
        if fence_character is not None:
            if stripped.startswith("|") and stripped.endswith("|"):
                errors.append(
                    f"{path}:{line_number}: table rows hidden in Markdown fences "
                    "are forbidden"
                )
            continue
        if stripped.startswith("## "):
            seen_headings.append(stripped)
            current_section = stripped if stripped in section_structure else None
            continue
        if not stripped.startswith("|") or not stripped.endswith("|"):
            continue
        if current_section is None:
            errors.append(
                f"{path}:{line_number}: table row is outside a canonical section"
            )
            continue
        if stripped == "| Field ID | Value | Evidence reference |":
            section_structure[current_section].append("<HEADER>")
            continue
        if stripped == "|---|---|---|":
            section_structure[current_section].append("<SEPARATOR>")
            continue
        cells = [cell.strip() for cell in stripped[1:-1].split("|")]
        if len(cells) != 3:
            errors.append(f"{path}:{line_number}: expected a three-column table row")
            continue
        field_id, value, evidence_ref = cells
        if field_id in rows:
            errors.append(f"{path}:{line_number}: duplicate field ID {field_id!r}")
            continue
        rows[field_id] = (value, evidence_ref, current_section)
        section_structure[current_section].append(field_id)

    if fence_character is not None:
        errors.append(f"{path}: unclosed Markdown fence is forbidden")

    if tuple(seen_headings) != expected_headings:
        errors.append(
            f"{path}: canonical section order mismatch; expected "
            f"{expected_headings!r}, got {tuple(seen_headings)!r}"
        )

    for section, expected_fields in ENVIRONMENT_SECTIONS:
        expected_order = (
            "<HEADER>",
            "<SEPARATOR>",
            *(field_id for field_id, _, _ in expected_fields),
        )
        actual_order = tuple(section_structure[section])
        if actual_order != expected_order:
            errors.append(
                f"{path}:{section}: canonical table structure mismatch; expected "
                f"{expected_order!r}, got {actual_order!r}"
            )

    return rows, errors


def _validate_html_comments(raw: str, path: Path, marker: str) -> list[str]:
    errors: list[str] = []
    comments = re.findall(r"<!--.*?-->", raw, flags=re.DOTALL)
    if raw.count("<!--") != raw.count("-->"):
        errors.append(f"{path}: unbalanced HTML comment markers are forbidden")
    if comments != [marker]:
        errors.append(f"{path}: only the canonical identity comment is allowed")
    for comment in comments:
        if "|" in comment:
            errors.append(f"{path}: table rows hidden in HTML comments are forbidden")
            break
    return errors


def validate_environment_template(path: Path) -> list[str]:
    try:
        raw = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        return [f"{path}: cannot read UTF-8 Markdown: {exc}"]

    errors: list[str] = []
    marker = "<!-- ERP000M_ENVIRONMENT_INVENTORY_TEMPLATE_V1 -->"
    if not raw.startswith(marker + "\n"):
        errors.append(f"{path}: missing template identity marker")
    errors.extend(_validate_html_comments(raw, path, marker))
    _scan_raw_text(raw, path, errors)
    canonical_hash = hashlib.sha256(raw.encode("utf-8")).hexdigest().upper()
    if canonical_hash != ENVIRONMENT_CANONICAL_SHA256:
        errors.append(
            f"{path}: canonical environment hash drift; expected "
            f"{ENVIRONMENT_CANONICAL_SHA256}, got {canonical_hash}"
        )

    rows, row_errors = _parse_environment_rows(raw, path)
    errors.extend(row_errors)
    actual_fields = set(rows)
    for missing in sorted(ENVIRONMENT_FIELD_IDS - actual_fields):
        errors.append(f"{path}: missing required field {missing!r}")
    for unknown in sorted(actual_fields - ENVIRONMENT_FIELD_IDS):
        errors.append(f"{path}: unknown field {unknown!r}")

    for field_id in sorted(ENVIRONMENT_FIELD_IDS & actual_fields):
        value, evidence_ref, section = rows[field_id]
        value_identity, evidence_identity, expected_section = (
            ENVIRONMENT_FIELD_EXPECTATIONS[field_id]
        )
        expected_value = required(value_identity).value
        expected_evidence = required(evidence_identity).value
        if section != expected_section:
            errors.append(
                f"{path}:{field_id}: expected section {expected_section!r}, got {section!r}"
            )
        if value != expected_value:
            if field_id in PROTECTED_INPUT_KEYS:
                errors.append(
                    f"{path}:{field_id}: forbidden concrete default or placeholder swap; "
                    f"value must remain exact placeholder {expected_value!r}"
                )
            else:
                errors.append(
                    f"{path}:{field_id}: expected exact placeholder {expected_value!r}"
                )
        if evidence_ref != expected_evidence:
            errors.append(
                f"{path}:{field_id}: evidence reference must remain exact placeholder "
                f"{expected_evidence!r}"
            )

        if SENSITIVE_KEY_PATTERN.search(field_id):
            errors.append(f"{path}:{field_id}: sensitive-looking field ID is forbidden")
        for candidate in (value, evidence_ref):
            for pattern in SENSITIVE_VALUE_PATTERNS:
                if pattern.search(candidate):
                    errors.append(
                        f"{path}:{field_id}: sensitive-looking value is forbidden"
                    )
                    break

    return errors


def validate_readme(path: Path) -> list[str]:
    try:
        raw = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        return [f"{path}: cannot read UTF-8 Markdown: {exc}"]

    errors: list[str] = []
    if not raw.startswith(README_MARKER + "\n"):
        errors.append(f"{path}: missing README identity marker")
    errors.extend(_validate_html_comments(raw, path, README_MARKER))
    _scan_raw_text(raw, path, errors)

    for required_line in README_REQUIRED_LINES:
        if raw.count(required_line) != 1:
            errors.append(
                f"{path}: required decision-boundary line drifted: {required_line!r}"
            )

    canonical_hash = hashlib.sha256(raw.encode("utf-8")).hexdigest().upper()
    if canonical_hash != README_CANONICAL_SHA256:
        errors.append(
            f"{path}: canonical README hash drift; expected {README_CANONICAL_SHA256}, "
            f"got {canonical_hash}"
        )
    return errors


def _read_environment_rows_for_invariants(
    path: Path,
) -> dict[str, tuple[str, str, str]] | None:
    try:
        raw = path.read_text(encoding="utf-8")
    except (OSError, UnicodeError):
        return None
    rows, errors = _parse_environment_rows(raw, path)
    return None if errors else rows


def _get_nested(value: Any, path: tuple[Any, ...]) -> Any | None:
    try:
        for part in path:
            value = value[part]
    except (KeyError, IndexError, TypeError):
        return None
    return value


def _is_reparse_point(file_stat: os.stat_result) -> bool:
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    file_attributes = getattr(file_stat, "st_file_attributes", 0)
    return bool(file_attributes & reparse_flag)


def _file_identity(file_stat: os.stat_result) -> tuple[int, int]:
    return file_stat.st_dev, file_stat.st_ino


def _stable_file_signature(file_stat: os.stat_result) -> tuple[int, int]:
    return (
        file_stat.st_size,
        file_stat.st_mtime_ns,
    )


def _validate_windows_plain_file_path(artifact_path: Path) -> list[str]:
    if os.name != "nt":
        return []

    lexical_path = os.fspath(artifact_path)
    suffix_after_anchor = lexical_path[len(artifact_path.anchor) :]
    if ":" in suffix_after_anchor:
        return [
            f"{artifact_path}: Windows alternate data streams or named-stream "
            "paths are forbidden for filled artifacts"
        ]
    return []


def _ancestor_paths(artifact_path: Path) -> tuple[Path, ...]:
    ancestors: list[Path] = []
    current = artifact_path.parent
    while True:
        ancestors.append(current)
        parent = current.parent
        if parent == current:
            break
        current = parent
    ancestors.reverse()
    return tuple(ancestors)


def _inspect_ancestor_chain(
    artifact_path: Path,
    stage: str,
) -> tuple[dict[Path, tuple[int, int]] | None, list[str]]:
    identities: dict[Path, tuple[int, int]] = {}
    for ancestor in _ancestor_paths(artifact_path):
        try:
            ancestor_stat = os.lstat(ancestor)
        except OSError as exc:
            return None, [
                f"{artifact_path}: cannot inspect ancestor {ancestor} without "
                f"following links ({stage}): {exc}"
            ]
        if stat.S_ISLNK(ancestor_stat.st_mode) or _is_reparse_point(ancestor_stat):
            return None, [
                f"{artifact_path}: ancestor symbolic link or reparse point is "
                f"forbidden: {ancestor} ({stage})"
            ]
        if not stat.S_ISDIR(ancestor_stat.st_mode):
            return None, [
                f"{artifact_path}: ancestor path component must be a directory: "
                f"{ancestor} ({stage})"
            ]
        if ancestor_stat.st_ino == 0:
            return None, [
                f"{artifact_path}: stable ancestor identity is unavailable: "
                f"{ancestor} ({stage})"
            ]
        identities[ancestor] = _file_identity(ancestor_stat)
    return identities, []


def _validate_no_follow_regular_stat(
    path: Path,
    file_stat: os.stat_result,
    stage: str,
) -> list[str]:
    if stat.S_ISLNK(file_stat.st_mode) or _is_reparse_point(file_stat):
        return [
            f"{path}: symbolic link or reparse point is forbidden for a filled "
            f"artifact ({stage})"
        ]
    if not stat.S_ISREG(file_stat.st_mode):
        return [
            f"{path}: filled artifact must be a regular file ({stage})"
        ]
    if file_stat.st_ino == 0:
        return [
            f"{path}: stable no-follow file identity is unavailable ({stage})"
        ]
    return []


def _open_windows_read_locked_descriptor(artifact_path: Path) -> int:
    import ctypes
    import msvcrt
    from ctypes import wintypes

    generic_read = 0x80000000
    file_share_read = 0x00000001
    open_existing = 3
    file_attribute_normal = 0x00000080
    file_flag_open_reparse_point = 0x00200000
    invalid_handle_value = ctypes.c_void_p(-1).value

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    create_file = kernel32.CreateFileW
    create_file.argtypes = (
        wintypes.LPCWSTR,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.LPVOID,
        wintypes.DWORD,
        wintypes.DWORD,
        wintypes.HANDLE,
    )
    create_file.restype = wintypes.HANDLE
    close_handle = kernel32.CloseHandle
    close_handle.argtypes = (wintypes.HANDLE,)
    close_handle.restype = wintypes.BOOL

    handle = create_file(
        str(artifact_path),
        generic_read,
        file_share_read,
        None,
        open_existing,
        file_attribute_normal | file_flag_open_reparse_point,
        None,
    )
    if handle == invalid_handle_value:
        error_code = ctypes.get_last_error()
        raise OSError(
            error_code,
            ctypes.FormatError(error_code),
            str(artifact_path),
        )
    try:
        return msvcrt.open_osfhandle(
            handle,
            os.O_RDONLY | getattr(os, "O_BINARY", 0),
        )
    except (OSError, ValueError):
        close_handle(handle)
        raise


def _open_capture_descriptor(artifact_path: Path) -> int:
    if os.name == "nt":
        return _open_windows_read_locked_descriptor(artifact_path)

    flags = os.O_RDONLY
    flags |= getattr(os, "O_CLOEXEC", 0)
    flags |= getattr(os, "O_NOFOLLOW", 0)
    flags |= getattr(os, "O_NONBLOCK", 0)
    return os.open(artifact_path, flags)


def _capture_exact_utf8(path: Path) -> tuple[CapturedArtifact | None, list[str]]:
    artifact_path = Path(os.path.abspath(os.fspath(path)))
    path_errors = _validate_windows_plain_file_path(artifact_path)
    if path_errors:
        return None, path_errors
    before_ancestor_identities, errors = _inspect_ancestor_chain(
        artifact_path,
        "before open",
    )
    if errors:
        return None, errors
    assert before_ancestor_identities is not None
    try:
        before_path_stat = os.lstat(artifact_path)
    except OSError as exc:
        return None, [
            f"{artifact_path}: cannot inspect persisted artifact without following "
            f"links: {exc}"
        ]
    errors = _validate_no_follow_regular_stat(
        artifact_path,
        before_path_stat,
        "before open",
    )
    if errors:
        return None, errors

    descriptor: int | None = None
    try:
        descriptor = _open_capture_descriptor(artifact_path)
        before_read_stat = os.fstat(descriptor)
        errors.extend(
            _validate_no_follow_regular_stat(
                artifact_path,
                before_read_stat,
                "opened descriptor",
            )
        )
        if errors:
            return None, errors
        if _file_identity(before_path_stat) != _file_identity(before_read_stat):
            return None, [
                f"{artifact_path}: artifact identity changed between no-follow "
                "inspection and open"
            ]

        chunks: list[bytes] = []
        while True:
            chunk = os.read(descriptor, 1024 * 1024)
            if not chunk:
                break
            chunks.append(chunk)
        raw_bytes = b"".join(chunks)
        after_read_stat = os.fstat(descriptor)
        errors.extend(
            _validate_no_follow_regular_stat(
                artifact_path,
                after_read_stat,
                "after descriptor read",
            )
        )
        try:
            after_path_stat = os.lstat(artifact_path)
        except OSError as exc:
            errors.append(
                f"{artifact_path}: cannot re-inspect persisted artifact without "
                f"following links after read: {exc}"
            )
            return None, errors
        errors.extend(
            _validate_no_follow_regular_stat(
                artifact_path,
                after_path_stat,
                "after read",
            )
        )
        if errors:
            return None, errors

        after_ancestor_identities, ancestor_errors = _inspect_ancestor_chain(
            artifact_path,
            "after read",
        )
        errors.extend(ancestor_errors)
        if errors:
            return None, errors
        if after_ancestor_identities != before_ancestor_identities:
            return None, [
                f"{artifact_path}: ancestor identity changed while artifact bytes "
                "were captured"
            ]

        identities = {
            _file_identity(before_path_stat),
            _file_identity(before_read_stat),
            _file_identity(after_read_stat),
            _file_identity(after_path_stat),
        }
        if len(identities) != 1:
            return None, [
                f"{artifact_path}: artifact identity changed while bytes were read"
            ]
        signatures = {
            _stable_file_signature(before_path_stat),
            _stable_file_signature(before_read_stat),
            _stable_file_signature(after_read_stat),
            _stable_file_signature(after_path_stat),
        }
        if len(signatures) != 1 or len(raw_bytes) != after_read_stat.st_size:
            return None, [
                f"{artifact_path}: artifact metadata or size changed while bytes "
                "were read"
            ]

        if raw_bytes.startswith(b"\xef\xbb\xbf"):
            return None, [
                f"{artifact_path}: UTF-8 BOM is forbidden by "
                "ERP000M-SELF-SHA256-V1"
            ]
        try:
            text = raw_bytes.decode("utf-8", errors="strict")
        except UnicodeError as exc:
            return None, [
                f"{artifact_path}: persisted artifact must be strict UTF-8: {exc}"
            ]
        return CapturedArtifact(artifact_path, raw_bytes, text), []
    except OSError as exc:
        return None, [
            f"{artifact_path}: cannot read persisted artifact through a no-follow "
            f"descriptor: {exc}"
        ]
    finally:
        if descriptor is not None:
            os.close(descriptor)


def _parse_strict_json_text(raw: str, path: Path) -> tuple[Any | None, list[str]]:
    try:
        parsed = json.loads(
            raw,
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_non_finite,
        )
    except (json.JSONDecodeError, DuplicateKeyError, ValueError, RecursionError) as exc:
        return None, [f"{path}: strict JSON parse failed: {exc}"]
    return parsed, []


def _find_json_key_paths(
    value: Any,
    field_name: str,
    path: tuple[str, ...] = (),
) -> list[tuple[str, ...]]:
    matches: list[tuple[str, ...]] = []
    pending: list[tuple[Any, tuple[str, ...]]] = [(value, path)]
    while pending:
        current, current_path = pending.pop()
        children: list[tuple[Any, tuple[str, ...]]] = []
        if isinstance(current, dict):
            for key, child in current.items():
                child_path = (*current_path, key)
                if key == field_name:
                    matches.append(child_path)
                children.append((child, child_path))
        elif isinstance(current, list):
            for index, child in enumerate(current):
                children.append((child, (*current_path, f"[{index}]")))
        pending.extend(reversed(children))
    return matches


def _locate_json_self_hash_slot(
    raw: str,
    path: Path,
    spec: SelfHashSpec,
) -> tuple[tuple[int, int, str] | None, list[str]]:
    assert spec.template_id is not None
    assert spec.json_path is not None

    parsed, errors = _parse_strict_json_text(raw, path)
    if errors:
        return None, errors
    if not isinstance(parsed, dict):
        return None, [f"{path}: persisted {spec.artifact_name} must be a JSON object"]
    if parsed.get("template_id") != spec.template_id:
        errors.append(
            f"{path}: expected template_id {spec.template_id!r} for "
            f"{spec.artifact_name}"
        )

    field_paths = _find_json_key_paths(parsed, spec.field_name)
    if field_paths != [spec.json_path]:
        display_paths = ["/" + "/".join(field_path) for field_path in field_paths]
        errors.append(
            f"{path}: self-hash field {spec.field_name!r} must appear exactly once "
            f"globally at /{'/'.join(spec.json_path)}; got {display_paths!r}"
        )

    structured_value = _get_nested(parsed, spec.json_path)
    if not isinstance(structured_value, str):
        errors.append(
            f"{path}: canonical self-hash path {'/'.join(spec.json_path)!r} "
            "must contain a string"
        )

    field_pattern = re.compile(
        rf'(?P<prefix>"{re.escape(spec.field_name)}"[ \t\r\n]*:[ \t\r\n]*")'
        rf'(?P<value>[^"\\\r\n]*)(?P<suffix>")'
    )
    matches = list(field_pattern.finditer(raw))
    if len(matches) != 1:
        errors.append(
            f"{path}: expected exactly one literal canonical self-hash field "
            f"{spec.field_name!r}, got {len(matches)}"
        )
        return None, errors

    match = matches[0]
    raw_value = match.group("value")
    if structured_value != raw_value:
        errors.append(
            f"{path}: canonical self-hash field must use an unescaped literal value"
        )
    if errors:
        return None, errors
    return (match.start("value"), match.end("value"), raw_value), []


def _locate_markdown_self_hash_slot(
    raw: str,
    path: Path,
    spec: SelfHashSpec,
) -> tuple[tuple[int, int, str] | None, list[str]]:
    assert spec.markdown_marker is not None

    errors: list[str] = []
    if not (
        raw.startswith(spec.markdown_marker + "\n")
        or raw.startswith(spec.markdown_marker + "\r\n")
    ):
        errors.append(
            f"{path}: missing {spec.artifact_name} identity marker for "
            "ERP000M-SELF-SHA256-V1"
        )

    rows, row_errors = _parse_environment_rows(raw, path)
    errors.extend(row_errors)
    structured_row = rows.get(spec.field_name)
    if structured_row is None:
        errors.append(
            f"{path}: missing canonical self-hash row {spec.field_name!r}"
        )

    row_pattern = re.compile(
        rf"(?m)^(?P<prefix>\| {re.escape(spec.field_name)} \| )"
        r"(?P<value>[^|\r\n]+?)(?P<suffix> \| [^|\r\n]+ \|)(?P<cr>\r?)$"
    )
    matches = list(row_pattern.finditer(raw))
    if len(matches) != 1:
        errors.append(
            f"{path}: expected exactly one literal canonical self-hash row "
            f"{spec.field_name!r}, got {len(matches)}"
        )
        return None, errors

    match = matches[0]
    raw_value = match.group("value")
    if structured_row is not None and structured_row[0] != raw_value:
        errors.append(
            f"{path}: canonical Markdown self-hash row value is ambiguous"
        )
    if errors:
        return None, errors
    return (match.start("value"), match.end("value"), raw_value), []


def _compute_captured_self_hash_sha256(
    captured: CapturedArtifact,
    spec: SelfHashSpec,
) -> tuple[SelfHashComputation | None, list[str]]:
    if spec.json_path is None:
        slot, slot_errors = _locate_markdown_self_hash_slot(
            captured.text,
            captured.path,
            spec,
        )
    else:
        slot, slot_errors = _locate_json_self_hash_slot(
            captured.text,
            captured.path,
            spec,
        )
    if slot_errors:
        return None, slot_errors
    assert slot is not None

    start, end, stored_value = slot
    if stored_value != spec.sentinel and not SELF_HASH_HEX_PATTERN.fullmatch(
        stored_value
    ):
        return None, [
            f"{captured.path}: {spec.field_name} must be the canonical sentinel or "
            "64 uppercase hexadecimal characters"
        ]

    canonical_preimage = (
        captured.text[:start] + spec.sentinel + captured.text[end:]
    ).encode("utf-8")
    computed_sha256 = hashlib.sha256(canonical_preimage).hexdigest().upper()
    return SelfHashComputation(stored_value, computed_sha256), []


def compute_self_hash_sha256(
    path: Path,
    spec: SelfHashSpec,
) -> tuple[SelfHashComputation | None, list[str]]:
    captured, errors = _capture_exact_utf8(path)
    if errors:
        return None, errors
    assert captured is not None
    return _compute_captured_self_hash_sha256(captured, spec)


def _validate_captured_self_hash(
    captured: CapturedArtifact,
    spec: SelfHashSpec,
) -> tuple[SelfHashComputation | None, list[str]]:
    computation, errors = _compute_captured_self_hash_sha256(captured, spec)
    if errors:
        return computation, errors
    assert computation is not None

    if computation.stored_value == spec.sentinel:
        errors.append(
            f"{captured.path}: {spec.field_name} is unsealed; persisted evidence must contain "
            "its computed SHA-256"
        )
    elif not hmac.compare_digest(
        computation.stored_value,
        computation.computed_sha256,
    ):
        errors.append(
            f"{captured.path}: persisted self-hash mismatch; stored "
            f"{computation.stored_value}, recomputed {computation.computed_sha256}"
        )
    return computation, errors


def _validate_persisted_self_hash(
    path: Path,
    spec: SelfHashSpec,
) -> tuple[SelfHashComputation | None, list[str]]:
    captured, errors = _capture_exact_utf8(path)
    if errors:
        return None, errors
    assert captured is not None
    return _validate_captured_self_hash(captured, spec)


def validate_persisted_self_hash(path: Path, spec: SelfHashSpec) -> list[str]:
    _, errors = _validate_persisted_self_hash(path, spec)
    return errors


def validate_persisted_hash_pack(
    environment_path: Path,
    workload_path: Path,
    manifest_path: Path,
) -> list[str]:
    paths_and_specs = (
        (environment_path, ENVIRONMENT_SELF_HASH_SPEC),
        (workload_path, WORKLOAD_SELF_HASH_SPEC),
        (manifest_path, MANIFEST_SELF_HASH_SPEC),
    )
    computations: dict[str, SelfHashComputation] = {}
    captures: dict[str, CapturedArtifact] = {}
    errors: list[str] = []
    for artifact_path, spec in paths_and_specs:
        captured, capture_errors = _capture_exact_utf8(artifact_path)
        errors.extend(capture_errors)
        if captured is None:
            continue
        captures[spec.artifact_name] = captured
        computation, artifact_errors = _validate_captured_self_hash(captured, spec)
        errors.extend(artifact_errors)
        if computation is not None:
            computations[spec.artifact_name] = computation

    manifest_capture = captures.get("evidence manifest")
    if manifest_capture is None:
        return errors
    manifest, manifest_errors = _parse_strict_json_text(
        manifest_capture.text,
        manifest_capture.path,
    )
    errors.extend(manifest_errors)
    if not isinstance(manifest, dict):
        return errors

    cross_hashes = (
        (
            "environment.inventory_hash_sha256",
            _get_nested(manifest, ("environment", "inventory_hash_sha256")),
            computations.get("environment inventory"),
        ),
        (
            "workload.profile_hash_sha256",
            _get_nested(manifest, ("workload", "profile_hash_sha256")),
            computations.get("workload profile"),
        ),
    )
    for field_path, manifest_value, source_computation in cross_hashes:
        if source_computation is None:
            continue
        if not isinstance(manifest_value, str) or not SELF_HASH_HEX_PATTERN.fullmatch(
            manifest_value
        ):
            errors.append(
                f"{manifest_capture.path}: {field_path} must contain 64 uppercase "
                "hexadecimal characters"
            )
        elif not hmac.compare_digest(
            manifest_value,
            source_computation.computed_sha256,
        ):
            errors.append(
                f"{manifest_capture.path}: {field_path} must equal recomputed source artifact "
                f"SHA-256 {source_computation.computed_sha256}"
            )
    return errors


def _validate_cross_template_invariants(root: Path) -> list[str]:
    environment_rows = _read_environment_rows_for_invariants(root / ENVIRONMENT_TEMPLATE)
    workload, workload_errors = _load_strict_json(root / WORKLOAD_TEMPLATE)
    evidence, evidence_errors = _load_strict_json(root / EVIDENCE_TEMPLATE)
    if environment_rows is None or workload_errors or evidence_errors:
        return []

    env_value = lambda field_id: environment_rows[field_id][0]
    groups: tuple[tuple[str, tuple[Any, ...]], ...] = (
        (
            "owner",
            (
                env_value("owner"),
                _get_nested(workload, ("metadata", "owner")),
                _get_nested(evidence, ("metadata", "owner")),
            ),
        ),
        (
            "source",
            (
                env_value("source"),
                _get_nested(workload, ("metadata", "source")),
                _get_nested(evidence, ("metadata", "source")),
            ),
        ),
        (
            "captured_at_utc",
            (
                env_value("captured_at_utc"),
                _get_nested(workload, ("metadata", "captured_at_utc")),
                _get_nested(evidence, ("metadata", "captured_at_utc")),
            ),
        ),
        (
            "source_commit",
            (
                env_value("source_commit"),
                _get_nested(workload, ("metadata", "source_commit")),
                _get_nested(evidence, ("metadata", "source_commit")),
            ),
        ),
        (
            "provider",
            (
                env_value("provider_candidate"),
                _get_nested(workload, ("candidate_inputs", "provider")),
                _get_nested(evidence, ("environment", "provider")),
            ),
        ),
        (
            "edition",
            (
                env_value("edition"),
                _get_nested(workload, ("candidate_inputs", "edition")),
                _get_nested(evidence, ("environment", "edition")),
            ),
        ),
        (
            "collation",
            (
                env_value("database_collation"),
                _get_nested(workload, ("candidate_inputs", "collation")),
                _get_nested(evidence, ("environment", "collation")),
            ),
        ),
        (
            "full_text_language",
            (
                env_value("full_text_language"),
                _get_nested(workload, ("candidate_inputs", "full_text_language")),
                _get_nested(evidence, ("environment", "full_text_language")),
            ),
        ),
        (
            "isolation_level",
            (
                env_value("isolation_level"),
                _get_nested(workload, ("candidate_inputs", "isolation_level")),
                _get_nested(evidence, ("environment", "isolation_level")),
            ),
        ),
        (
            "topology",
            (
                env_value("topology"),
                _get_nested(workload, ("candidate_inputs", "topology")),
                _get_nested(evidence, ("environment", "topology")),
            ),
        ),
        (
            "environment_inventory_hash",
            (
                env_value("inventory_hash_sha256"),
                _get_nested(evidence, ("environment", "inventory_hash_sha256")),
            ),
        ),
        (
            "environment_inventory_ref",
            (
                _get_nested(workload, ("evidence", "environment_inventory_ref")),
                _get_nested(evidence, ("environment", "inventory_ref")),
            ),
        ),
        (
            "workload_profile_hash",
            (
                _get_nested(workload, ("evidence", "workload_profile_hash_sha256")),
                _get_nested(evidence, ("workload", "profile_hash_sha256")),
            ),
        ),
        (
            "generator_version",
            (
                _get_nested(workload, ("dataset", "generator", "version")),
                _get_nested(evidence, ("workload", "generator_version")),
            ),
        ),
        (
            "generator_seed",
            (
                _get_nested(workload, ("dataset", "generator", "seed")),
                _get_nested(evidence, ("workload", "generator_seed")),
            ),
        ),
        (
            "generator_configuration_hash",
            (
                _get_nested(
                    workload,
                    ("dataset", "generator", "configuration_hash_sha256"),
                ),
                _get_nested(
                    evidence,
                    ("workload", "generator_configuration_hash_sha256"),
                ),
            ),
        ),
        (
            "dataset_hash",
            (
                _get_nested(workload, ("dataset", "generator", "dataset_hash_sha256")),
                _get_nested(evidence, ("workload", "dataset_hash_sha256")),
            ),
        ),
        (
            "reproduction_instructions_ref",
            (
                _get_nested(
                    workload,
                    ("dataset", "generator", "reproduction_instructions_ref"),
                ),
                _get_nested(evidence, ("workload", "reproduction_instructions_ref")),
            ),
        ),
        (
            "threshold",
            (
                _get_nested(workload, ("performance_acceptance_inputs", 0, "threshold")),
                _get_nested(evidence, ("measurements", 0, "threshold")),
            ),
        ),
        (
            "threshold_decision_ref",
            (
                _get_nested(
                    workload,
                    ("performance_acceptance_inputs", 0, "decision_ref"),
                ),
                _get_nested(evidence, ("measurements", 0, "threshold_decision_ref")),
            ),
        ),
        (
            "redaction_scan_tool",
            (
                _get_nested(workload, ("redaction_control", "sensitive_material_scan_tool")),
                _get_nested(evidence, ("redaction_control", "scan_tool_name")),
            ),
        ),
        (
            "redaction_scan_tool_version",
            (
                _get_nested(
                    workload,
                    ("redaction_control", "sensitive_material_scan_tool_version"),
                ),
                _get_nested(evidence, ("redaction_control", "scan_tool_version")),
            ),
        ),
        (
            "redaction_scan_evidence_ref",
            (
                environment_rows["sensitive_material_redaction_status"][1],
                _get_nested(workload, ("redaction_control", "scan_evidence_ref")),
                _get_nested(evidence, ("redaction_control", "scan_evidence_ref")),
            ),
        ),
        (
            "redaction_review_owner",
            (
                env_value("redaction_reviewer"),
                _get_nested(workload, ("redaction_control", "review_owner")),
                _get_nested(evidence, ("redaction_control", "review_owner")),
            ),
        ),
        (
            "redaction_reviewed_at_utc",
            (
                env_value("redaction_reviewed_at_utc"),
                _get_nested(workload, ("redaction_control", "reviewed_at_utc")),
                _get_nested(evidence, ("redaction_control", "reviewed_at_utc")),
            ),
        ),
    )

    errors: list[str] = []
    for name, values in groups:
        present = [value for value in values if value is not None]
        if len(present) != len(values) or len(set(present)) != 1:
            errors.append(
                f"cross-template invariant {name!r} mismatch: {values!r}"
            )
    return errors


def _validate_exact_doc_file_set(pack_root: Path) -> list[str]:
    errors: list[str] = []
    if not pack_root.is_dir() or pack_root.is_symlink():
        return [f"{pack_root}: evidence input directory is missing or unsafe"]

    entries = list(pack_root.iterdir())
    actual_names = {entry.name for entry in entries}
    for missing in sorted(EXPECTED_DOC_FILE_NAMES - actual_names):
        errors.append(f"{pack_root}: missing reviewed file {missing!r}")
    for unknown in sorted(actual_names - EXPECTED_DOC_FILE_NAMES):
        errors.append(f"{pack_root}: unreviewed file is forbidden: {unknown!r}")

    for entry in entries:
        if entry.is_symlink() or not entry.is_file():
            errors.append(f"{entry}: only regular reviewed files are allowed")
            continue
        try:
            raw = entry.read_text(encoding="utf-8")
        except (OSError, UnicodeError) as exc:
            errors.append(f"{entry}: cannot scan UTF-8 content: {exc}")
            continue
        _scan_raw_text(raw, entry, errors)
    return errors


def validate_repository(repo_root: Path) -> list[str]:
    root = repo_root.resolve()
    expected_files = {
        README_TEMPLATE: validate_readme,
        ENVIRONMENT_TEMPLATE: validate_environment_template,
        WORKLOAD_TEMPLATE: lambda path: validate_json_template(path, WORKLOAD_SCHEMA),
        EVIDENCE_TEMPLATE: lambda path: validate_json_template(path, EVIDENCE_SCHEMA),
    }

    errors = _validate_exact_doc_file_set(root / DOCS_RELATIVE_ROOT)
    for relative_path, validator in expected_files.items():
        path = root / relative_path
        if not path.is_file():
            errors.append(f"{relative_path}: required template file is missing")
            continue
        errors.extend(validator(path))
    errors.extend(_validate_cross_template_invariants(root))
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Validate decision-neutral ERP-000M evidence input templates."
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parents[2],
        help="Repository root containing docs/evidence/erp-000m.",
    )
    parser.add_argument(
        "--verify-filled-pack",
        nargs=3,
        type=Path,
        metavar=("ENVIRONMENT_MD", "WORKLOAD_JSON", "MANIFEST_JSON"),
        help=(
            "Verify ERP000M-SELF-SHA256-V1 for an external filled environment, "
            "workload and evidence-manifest artifact pack. Files are never modified."
        ),
    )
    args = parser.parse_args(argv)

    if args.verify_filled_pack is None:
        errors = validate_repository(args.repo_root)
        success_message = "ERP-000M evidence input templates are valid."
    else:
        errors = validate_persisted_hash_pack(*args.verify_filled_pack)
        success_message = (
            "Captured ERP-000M evidence bytes and hash links are valid. "
            "Revalidate before every consumption boundary."
        )
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print(success_message)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
