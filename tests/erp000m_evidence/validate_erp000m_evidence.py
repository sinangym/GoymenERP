from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
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
    "Bu doğrulama yalnız boş ve karar-nötr şablon bütünlüğünü kanıtlar. Doldurulmuş",
    "kanıtın doğruluğu, ortam parity'si, test sonucu veya herhangi bir kabul kapısının",
    "kapanması anlamına gelmez.",
)
README_CANONICAL_SHA256 = (
    "71BD6A6211C5029EE39E0E41F43C769C706765E9149616FBAA535E01A927D0F5"
)
ENVIRONMENT_CANONICAL_SHA256 = (
    "31972E106E1F3C9A0912AA2A33F2311A5EE2377D477657BECF735B77D4FBCC23"
)

PLACEHOLDER_PATTERN = re.compile(
    r"^__REQUIRED_INPUT__:[A-Z0-9_-]+(?::[A-Z0-9_-]+)*__$"
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
    args = parser.parse_args(argv)

    errors = validate_repository(args.repo_root)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print("ERP-000M evidence input templates are valid.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
