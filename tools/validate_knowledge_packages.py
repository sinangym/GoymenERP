#!/usr/bin/env python3
"""Validate GöymenERP conversation knowledge packages without external packages."""

from __future__ import annotations

import argparse
import codecs
import csv
import hashlib
import io
import json
import re
import stat
import subprocess
import sys
import unicodedata
from collections import Counter
from datetime import date, datetime
from pathlib import Path, PurePosixPath
from typing import Any, Callable, NamedTuple


REGISTRY_PATH = Path("docs/knowledge/SOURCE_REGISTRY.json")
INBOX_PATH = Path("docs/knowledge/inbox")

SCHEMA_VERSION = "1.0"
RUN_ID = "GOYMENERP-KNOWLEDGE-V4-01"
CANONICAL_REPOSITORY = "https://github.com/sinangym/GoymenERP.git"
DEFAULT_BRANCH = "main"
PROVIDERS = ("codex", "claude", "chatgpt")

REQUIRED_PACKAGE_FILES = frozenset(
    {
        "SOURCE_MANIFEST.json",
        "KNOWLEDGE.md",
        "NORMALIZED_ITEMS.ndjson",
        "CONFLICT_CANDIDATES.md",
        "ATTACHMENT_INDEX.md",
        "EXPORT_REPORT.md",
    }
)
MANIFESTED_PACKAGE_FILES = REQUIRED_PACKAGE_FILES - {"SOURCE_MANIFEST.json"}

READY_CONSOLIDATED_FILES = frozenset(
    {
        "00_SOURCE_COVERAGE.md",
        "01_CURRENT_STATE_EVIDENCE.md",
        "02_MODULE_SCREEN_BACKEND_FRONTEND_MATRIX.md",
        "03_CONFLICTS_GAPS_OPEN_DECISIONS.md",
        "04_TRACEABILITY_MATRIX.csv",
        "GOYMEN_ERP_FINAL_MASTER_RAPOR_V4_CANDIDATE.md",
        "catalog.ndjson",
        "INTEGRATION_REPORT.md",
    }
)
READY_MARKDOWN_FILES = frozenset(
    filename for filename in READY_CONSOLIDATED_FILES if filename.endswith(".md")
)
NON_NORMATIVE_STATUS = "NON_NORMATIVE_CANDIDATE"
READY_METADATA_FIELDS = frozenset({"normative_status", "run_id", "base_main_sha"})
BASE_MAIN_SHA_PATTERN = re.compile(r"^[0-9a-f]{40}$")
MARKDOWN_METADATA_SCAN_LINES = 20
MIN_READY_MARKDOWN_BODY_CHARACTERS = 200

READY_MARKDOWN_REQUIRED_HEADINGS = {
    "00_SOURCE_COVERAGE.md": frozenset(
        {"## Kaynak Envanteri", "## Kapsam Açıkları"}
    ),
    "01_CURRENT_STATE_EVIDENCE.md": frozenset(
        {"## Doğrulanmış Mevcut Durum", "## Kanıtlar"}
    ),
    "02_MODULE_SCREEN_BACKEND_FRONTEND_MATRIX.md": frozenset(
        {"## Modül Matrisi", "## Durum Tanımları"}
    ),
    "03_CONFLICTS_GAPS_OPEN_DECISIONS.md": frozenset(
        {"## Çelişkiler", "## Açık Kararlar"}
    ),
    "GOYMEN_ERP_FINAL_MASTER_RAPOR_V4_CANDIDATE.md": frozenset(
        {
            "## Yönetici Özeti",
            "## Doğrulanmış Mevcut Durum",
            "## Hedef Ürün ve Kapsam",
            "## Modüller ve Ekranlar",
            "## Backend, Frontend, API ve Veri",
            "## Kararlar ve Gereksinimler",
            "## İş Kuralları ve Formüller",
            "## Mimari, Güvenlik ve Entegrasyonlar",
            "## Kabul, Test ve Yayın",
            "## Riskler, Açık Kararlar ve Blokerler",
            "## Öncelikli Teslim Planı",
            "## Kaynak Kapsamı ve İzlenebilirlik",
        }
    ),
    "INTEGRATION_REPORT.md": frozenset(
        {"## Doğrulama Kanıtı", "## Bağımsız İnceleme", "## Kalan Riskler"}
    ),
}

TRACEABILITY_REQUIRED_FIELDS = frozenset(
    {
        "trace_id",
        "source_id",
        "item_id",
        "target_ref",
        "disposition",
        "exclusion_reason",
        "normative_status",
        "run_id",
        "base_main_sha",
    }
)
CATALOG_REQUIRED_FIELDS = frozenset(
    {
        "canonical_id",
        "source_refs",
        "disposition",
        "exclusion_reason",
        "normative_status",
        "run_id",
        "base_main_sha",
    }
)
SOURCE_REF_FIELDS = frozenset({"source_id", "item_id"})
DISPOSITION_VALUES = frozenset({"included", "excluded"})

REGISTRY_FIELDS = frozenset(
    {
        "schema_version",
        "run_id",
        "canonical_repository",
        "default_branch",
        "state",
        "required_providers",
        "source_inventory_attestation",
        "expected_sources",
        "sources",
    }
)
SOURCE_INVENTORY_ATTESTATION_FIELDS = frozenset(
    {"status", "attested_by", "attested_at", "evidence_locator", "scope_note"}
)
EXPECTED_SOURCE_FIELDS = frozenset(
    {"provider", "conversation_id", "required", "status", "source_id", "reason"}
)
REGISTRY_SOURCE_FIELDS = frozenset(
    {
        "provider",
        "source_id",
        "conversation_id",
        "manifest_path",
        "content_fingerprint_sha256",
        "status",
    }
)
MANIFEST_FIELDS = frozenset(
    {
        "schema_version",
        "run_id",
        "provider",
        "source_id",
        "conversation_id",
        "conversation_title",
        "coverage",
        "inaccessible",
        "status",
        "privacy_review",
        "source_snapshot_sha256",
        "content_fingerprint_sha256",
        "files",
        "counts",
    }
)
PRIVACY_REVIEW_FIELDS = frozenset({"secret_scan", "pii_review", "redactions"})
MANIFEST_FILE_FIELDS = frozenset({"path", "bytes", "sha256"})
MANIFEST_COUNT_FIELDS = frozenset({"items", "attachments", "conflicts"})
ITEM_FIELDS = frozenset(
    {
        "schema_version",
        "id",
        "source_id",
        "type",
        "module",
        "statement",
        "current_or_target",
        "status",
        "priority",
        "source_provider",
        "conversation_id",
        "source_locator",
        "source_date",
        "confidence",
        "supersedes",
        "conflicts_with",
        "related_items",
    }
)

COVERAGE_VALUES = frozenset({"complete", "visible_only", "partial"})
MANIFEST_STATUS_VALUES = frozenset({"complete", "partial"})
CURRENT_OR_TARGET_VALUES = frozenset(
    {"CURRENT", "TARGET", "BOTH", "NOT_APPLICABLE"}
)
ITEM_STATUS_VALUES = frozenset(
    {
        "EXPLICIT_USER_DECISION",
        "USER_REQUIREMENT",
        "USER_REJECTION",
        "SOURCE_VERIFIED",
        "VERIFIED_IMPLEMENTED",
        "PROTOTYPE_ONLY",
        "PROPOSED",
        "INFERRED",
        "MISSING",
        "CONFLICT",
        "SUPERSEDED",
    }
)
PRIORITY_VALUES = frozenset({"P0", "P1", "P2", "P3", "UNSPECIFIED"})
CONFIDENCE_VALUES = frozenset({"HIGH", "MEDIUM", "LOW"})
EXPECTED_SOURCE_STATUS_VALUES = frozenset({"pending", "imported", "unavailable"})
REGISTRY_SOURCE_STATUS_VALUES = frozenset(
    {"validated", "integrated", "superseded"}
)
INVENTORY_ATTESTATION_STATUS_VALUES = frozenset(
    {"pending", "user_confirmed_complete"}
)

RUN_COMPONENT_PATTERN = re.compile(r"^[A-Z0-9][A-Z0-9-]{2,63}$")
SOURCE_ID_PATTERN = re.compile(r"^[a-z0-9][a-z0-9-]{2,95}$")
ITEM_ID_PATTERN = re.compile(r"^KN-[0-9A-F]{24}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
SAFE_OUTPUT_COMPONENT_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
SOURCE_DATE_PATTERN = re.compile(
    r"^\d{4}-\d{2}-\d{2}"
    r"(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,6})?)?"
    r"(?:Z|[+-]\d{2}:\d{2})?)?$"
)
ATTESTED_AT_PATTERN = re.compile(
    r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}"
    r"(?::\d{2}(?:\.\d{1,6})?)?(?:Z|[+-]\d{2}:\d{2})$"
)

ITEM_TYPE_VALUES = frozenset(
    {
        "requirement",
        "decision",
        "rejection",
        "proposal",
        "business_rule",
        "formula",
        "screen",
        "workflow",
        "data_rule",
        "integration",
        "architecture",
        "acceptance_criterion",
        "risk",
        "open_question",
        "current_state",
        "evidence",
    }
)

MAX_TEXT_FILE_BYTES = 8 * 1024 * 1024
MAX_NDJSON_LINE_BYTES = 256 * 1024

SECRET_PATTERNS = (
    (
        "private key",
        re.compile(
            r"-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----",
            re.IGNORECASE,
        ),
    ),
    (
        "OpenAI token",
        re.compile(r"\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{16,}\b"),
    ),
    (
        "GitHub token",
        re.compile(
            r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b"
        ),
    ),
    (
        "AWS access key",
        re.compile(r"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b"),
    ),
    (
        "Bearer credential",
        re.compile(r"\bAuthorization\s*:\s*Bearer\s+[A-Za-z0-9._~+/=-]{16,}", re.I),
    ),
    (
        "Slack token",
        re.compile(r"\bxox[baprs]-[A-Za-z0-9-]{10,}\b"),
    ),
)
CREDENTIAL_ASSIGNMENT_PATTERN = re.compile(
    r'''(?ix)
    (?<![A-Za-z0-9_-])
    ["']?
    (?P<key>
        password|pwd|api[_-]?key|apikey|client[_-]?secret|secret|
        accountkey|aws[_-]?secret[_-]?access[_-]?key|secretaccesskey|
        sharedaccesskey(?:name)?|sharedaccesssignature|
        access[_-]?token|refresh[_-]?token|cookie|set-cookie|
        session(?:[_-]?(?:id|token|key))?
    )
    (?![A-Za-z0-9_-])
    ["']?\s*(?:=|:)\s*
    (?P<value>
        \[REDACTED(?::[A-Za-z0-9_.-]+:[0-9a-fA-F]{8})?\]|
        \$\{[A-Za-z_][A-Za-z0-9_]*\}|
        \{\{\s*[A-Za-z_][A-Za-z0-9_.-]*\s*\}\}|
        <[A-Za-z][A-Za-z0-9_. -]*>|
        "(?:\\.|[^"\r\n])*"|
        '(?:\\.|[^'\r\n])*'|
        [^,;\s}\]\r\n]+
    )
    (?=$|[\s,;}\]])
    '''
)
REDACTED_VALUE_PATTERN = re.compile(
    r"^\[REDACTED(?::[A-Za-z0-9_.-]+:[0-9a-fA-F]{8})?\]$"
)
ENV_PLACEHOLDER_PATTERN = re.compile(r"^\$\{[A-Za-z_][A-Za-z0-9_]*\}$")
TEMPLATE_PLACEHOLDER_PATTERN = re.compile(
    r"^\{\{\s*[A-Za-z_][A-Za-z0-9_.-]*\s*\}\}$"
)
ANGLE_PLACEHOLDER_PATTERN = re.compile(r"^<[A-Za-z][A-Za-z0-9_. -]*>$")
YOUR_PLACEHOLDER_PATTERN = re.compile(r"^YOUR[_-][A-Z0-9_-]+$", re.I)
EXACT_SAFE_PLACEHOLDERS = frozenset(
    {"redacted", "placeholder", "changeme", "example", "unset", "not-set", "***", "xxx"}
)

EMAIL_PATTERN = re.compile(
    r"(?<![A-Za-z0-9._%+-])"
    r"(?P<local>[A-Za-z0-9._%+-]{1,64})@"
    r"(?P<domain>[A-Za-z0-9.-]+\.[A-Za-z]{2,63})"
    r"(?![A-Za-z0-9.-])"
)
EXAMPLE_EMAIL_DOMAINS = frozenset(
    {"example.com", "example.org", "example.net", "example.invalid"}
)
TURKISH_MOBILE_PATTERN = re.compile(
    r"(?<!\d)(?:\+?90[ .-]?|0)?5\d{2}(?:[ .-]?\d{3})(?:[ .-]?\d{2}){2}(?!\d)"
)
DIGIT_SEQUENCE_PATTERN = re.compile(r"(?<!\d)\d{11}(?!\d)")
TR_IBAN_PATTERN = re.compile(r"(?<![A-Z0-9])TR(?:[ ]?\d){24}(?![A-Z0-9])", re.I)
HEX_DIGEST_PATTERN = re.compile(r"(?i)\b[0-9a-f]{40,64}\b")


class DuplicateJsonKeyError(ValueError):
    def __init__(self, key: str) -> None:
        super().__init__(f"duplicate JSON object key {key!r}")


class NonFiniteJsonNumberError(ValueError):
    def __init__(self, value: str) -> None:
        super().__init__(f"non-standard JSON numeric constant {value!r}")


class PackageLocation(NamedTuple):
    run_id: str
    provider: str
    source_id: str
    path: Path


class ParsedPackage(NamedTuple):
    location: PackageLocation
    manifest: dict[str, Any]
    items: list[dict[str, Any]]


class ValidationStatistics(NamedTuple):
    registry_state: str | None
    package_count: int
    item_count: int


def _reject_duplicate_json_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKeyError(key)
        result[key] = value
    return result


def _reject_non_finite_json_number(value: str) -> None:
    raise NonFiniteJsonNumberError(value)


def _escape_diagnostic_controls(value: str) -> str:
    escaped: list[str] = []
    for character in value:
        codepoint = ord(character)
        category = unicodedata.category(character)
        if character == "\n":
            escaped.append(r"\n")
        elif character == "\r":
            escaped.append(r"\r")
        elif character == "\t":
            escaped.append(r"\t")
        elif category in {"Cc", "Cf", "Cs"} or character in {"\u2028", "\u2029"}:
            if codepoint <= 0xFF:
                escaped.append(f"\\x{codepoint:02x}")
            elif codepoint <= 0xFFFF:
                escaped.append(f"\\u{codepoint:04x}")
            else:
                escaped.append(f"\\U{codepoint:08x}")
        else:
            escaped.append(character)
    return "".join(escaped)


def _safe_errors(errors: list[str]) -> list[str]:
    return sorted({_escape_diagnostic_controls(error) for error in errors})


def _exact_fields(
    value: dict[str, Any], expected: frozenset[str], label: str, errors: list[str]
) -> bool:
    actual = set(value)
    missing = sorted(expected - actual)
    unexpected = sorted(actual - expected)
    if missing:
        errors.append(f"{label} is missing fields: {', '.join(missing)}")
    if unexpected:
        errors.append(f"{label} has unexpected fields: {', '.join(unexpected)}")
    return not missing and not unexpected


def _trimmed_string(
    value: Any,
    label: str,
    errors: list[str],
    *,
    pattern: re.Pattern[str] | None = None,
    max_length: int | None = None,
) -> str | None:
    if not isinstance(value, str) or not value or value != value.strip():
        errors.append(f"{label} must be a non-empty trimmed string")
        return None
    if any(
        unicodedata.category(character) in {"Cc", "Cf", "Cs"}
        or character in {"\u2028", "\u2029"}
        for character in value
    ):
        errors.append(f"{label} contains forbidden control or format characters")
        return None
    if pattern is not None and pattern.fullmatch(value) is None:
        errors.append(f"{label} has an invalid format")
        return None
    if max_length is not None and len(value) > max_length:
        errors.append(f"{label} exceeds the {max_length}-character limit")
        return None
    return value


def _optional_trimmed_string(
    value: Any, label: str, errors: list[str]
) -> str | None:
    if value is None:
        return None
    return _trimmed_string(value, label, errors)


def _enum_string(
    value: Any, allowed: frozenset[str], label: str, errors: list[str]
) -> str | None:
    parsed = _trimmed_string(value, label, errors)
    if parsed is not None and parsed not in allowed:
        errors.append(f"{label} has unsupported value {parsed!r}")
        return None
    return parsed


def _sha256_string(value: Any, label: str, errors: list[str]) -> str | None:
    parsed = _trimmed_string(value, label, errors)
    if parsed is not None and SHA256_PATTERN.fullmatch(parsed) is None:
        errors.append(f"{label} must be a lowercase SHA-256 digest")
        return None
    return parsed


def _string_array(
    value: Any,
    label: str,
    errors: list[str],
    *,
    pattern: re.Pattern[str] | None = None,
) -> list[str]:
    if not isinstance(value, list):
        errors.append(f"{label} must be a JSON array")
        return []
    parsed: list[str] = []
    for index, raw_item in enumerate(value):
        item = _trimmed_string(
            raw_item, f"{label}[{index}]", errors, pattern=pattern
        )
        if item is not None:
            parsed.append(item)
    duplicates = sorted(item for item, count in Counter(parsed).items() if count > 1)
    if duplicates:
        errors.append(f"{label} contains duplicates: {', '.join(duplicates)}")
    return parsed


def _scan_secrets(text: str, label: str, errors: list[str]) -> None:
    for secret_label, pattern in SECRET_PATTERNS:
        if pattern.search(text):
            errors.append(f"{label} contains a possible {secret_label}")

    for match in CREDENTIAL_ASSIGNMENT_PATTERN.finditer(text):
        value = match.group("value").strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in {"'", '"'}:
            value = value[1:-1]
        if _is_safe_placeholder(value):
            continue
        key = match.group("key").casefold()
        if key in {"password", "pwd"}:
            errors.append(f"{label} contains a possible password credential")
        else:
            errors.append(
                f"{label} contains a possible credential assignment for {key!r}"
            )
        break


def _is_safe_placeholder(value: str) -> bool:
    stripped = value.strip()
    return bool(
        stripped.casefold() in EXACT_SAFE_PLACEHOLDERS
        or REDACTED_VALUE_PATTERN.fullmatch(stripped)
        or ENV_PLACEHOLDER_PATTERN.fullmatch(stripped)
        or TEMPLATE_PLACEHOLDER_PATTERN.fullmatch(stripped)
        or ANGLE_PLACEHOLDER_PATTERN.fullmatch(stripped)
        or YOUR_PLACEHOLDER_PATTERN.fullmatch(stripped)
    )


def _is_valid_tckn(value: str) -> bool:
    if len(value) != 11 or not value.isdigit() or value[0] == "0":
        return False
    digits = [int(character) for character in value]
    tenth = (7 * sum(digits[0:9:2]) - sum(digits[1:8:2])) % 10
    eleventh = sum(digits[:10]) % 10
    return digits[9] == tenth and digits[10] == eleventh


def _is_valid_tr_iban(value: str) -> bool:
    compact = value.replace(" ", "").upper()
    if len(compact) != 26 or not compact.startswith("TR") or not compact[2:].isdigit():
        return False
    rearranged = compact[4:] + "2927" + compact[2:4]
    remainder = 0
    for character in rearranged:
        remainder = (remainder * 10 + int(character)) % 97
    return remainder == 1


def _scan_pii(text: str, label: str, errors: list[str]) -> None:
    for match in EMAIL_PATTERN.finditer(text):
        domain = match.group("domain").casefold()
        if domain not in EXAMPLE_EMAIL_DOMAINS and not domain.endswith(".example"):
            errors.append(f"{label} contains a possible personal email address")
            break

    digest_masked = HEX_DIGEST_PATTERN.sub("[HASH]", text)
    if TURKISH_MOBILE_PATTERN.search(digest_masked):
        errors.append(f"{label} contains a possible Turkish mobile phone number")

    if any(_is_valid_tckn(match.group(0)) for match in DIGIT_SEQUENCE_PATTERN.finditer(text)):
        errors.append(f"{label} contains a possible valid TCKN")

    if any(_is_valid_tr_iban(match.group(0)) for match in TR_IBAN_PATTERN.finditer(text)):
        errors.append(f"{label} contains a possible valid TR IBAN")


def _is_link_or_reparse(metadata: Any) -> bool:
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return stat.S_ISLNK(metadata.st_mode) or bool(
        getattr(metadata, "st_file_attributes", 0) & reparse_flag
    )


def _read_utf8_file(
    path: Path,
    label: str,
    errors: list[str],
    *,
    allow_empty: bool,
) -> tuple[bytes, str] | None:
    try:
        metadata = path.lstat()
    except OSError as exc:
        errors.append(f"Cannot inspect {label}: {exc}")
        return None
    if _is_link_or_reparse(metadata):
        errors.append(f"{label} must not be a symbolic link or reparse point")
        return None
    if not stat.S_ISREG(metadata.st_mode):
        errors.append(f"{label} must be a regular file")
        return None
    try:
        raw = path.read_bytes()
    except OSError as exc:
        errors.append(f"Cannot read {label}: {exc}")
        return None
    if len(raw) > MAX_TEXT_FILE_BYTES:
        errors.append(
            f"{label} exceeds the {MAX_TEXT_FILE_BYTES}-byte package file limit"
        )
        return None
    if raw.startswith(codecs.BOM_UTF8):
        errors.append(f"{label} must not contain a UTF-8 BOM")
        return None
    try:
        text = raw.decode("utf-8", errors="strict")
    except UnicodeDecodeError:
        errors.append(f"{label} is not valid UTF-8")
        return None
    if not allow_empty and not text.strip():
        errors.append(f"{label} must not be empty")
    if raw and not raw.endswith(b"\n"):
        errors.append(f"{label} must end with a newline")
    if "\x00" in text:
        errors.append(f"{label} contains a NUL character")
    _scan_secrets(text, label, errors)
    _scan_pii(text, label, errors)
    return raw, text


def _parse_json_object(
    text: str, label: str, errors: list[str]
) -> dict[str, Any] | None:
    try:
        parsed = json.loads(
            text,
            object_pairs_hook=_reject_duplicate_json_keys,
            parse_constant=_reject_non_finite_json_number,
        )
    except json.JSONDecodeError as exc:
        errors.append(f"Invalid JSON in {label}:{exc.lineno}:{exc.colno}: {exc.msg}")
        return None
    except (DuplicateJsonKeyError, NonFiniteJsonNumberError) as exc:
        errors.append(f"Invalid JSON in {label}: {exc}")
        return None
    except RecursionError:
        errors.append(f"JSON nesting exceeds parser capacity in {label}")
        return None
    except ValueError:
        errors.append(f"JSON numeric value exceeds parser capacity in {label}")
        return None
    if not isinstance(parsed, dict):
        errors.append(f"{label} root must be a JSON object")
        return None
    return parsed


def _load_json_object(
    path: Path, label: str, errors: list[str]
) -> dict[str, Any] | None:
    loaded = _read_utf8_file(path, label, errors, allow_empty=False)
    if loaded is None:
        return None
    return _parse_json_object(loaded[1], label, errors)


def _validate_repo_root(repo_root: Path, errors: list[str]) -> Path | None:
    try:
        metadata = repo_root.lstat()
    except OSError as exc:
        errors.append(f"Cannot inspect repository root {repo_root}: {exc}")
        return None
    if _is_link_or_reparse(metadata):
        errors.append(
            f"Repository root must not be a symbolic link or reparse point: {repo_root}"
        )
        return None
    if not stat.S_ISDIR(metadata.st_mode):
        errors.append(f"Repository root must be a directory: {repo_root}")
        return None
    try:
        return repo_root.resolve(strict=True)
    except (OSError, RuntimeError) as exc:
        errors.append(f"Cannot resolve repository root {repo_root}: {exc}")
        return None


def _repo_regular_file(root: Path, relative_path: Path, errors: list[str]) -> Path | None:
    candidate = root / relative_path
    try:
        resolved = candidate.resolve(strict=True)
    except FileNotFoundError:
        errors.append(f"Missing required file: {relative_path.as_posix()}")
        return None
    except (OSError, RuntimeError) as exc:
        errors.append(f"Cannot resolve {relative_path.as_posix()}: {exc}")
        return None
    try:
        resolved.relative_to(root)
    except ValueError:
        errors.append(f"Path resolves outside repository root: {relative_path.as_posix()}")
        return None
    try:
        metadata = candidate.lstat()
    except OSError as exc:
        errors.append(f"Cannot inspect {relative_path.as_posix()}: {exc}")
        return None
    if _is_link_or_reparse(metadata):
        errors.append(
            "Required file must not be a symbolic link or reparse point: "
            f"{relative_path.as_posix()}"
        )
        return None
    if not stat.S_ISREG(metadata.st_mode):
        errors.append(f"Required path must be a regular file: {relative_path.as_posix()}")
        return None
    return resolved


def _case_collisions(entries: list[Path], label: str, errors: list[str]) -> None:
    grouped: dict[str, list[str]] = {}
    for entry in entries:
        grouped.setdefault(entry.name.casefold(), []).append(entry.name)
    for names in grouped.values():
        if len(names) > 1:
            errors.append(f"Case-colliding names under {label}: {', '.join(sorted(names))}")


def _safe_component(
    value: str, label: str, pattern: re.Pattern[str], errors: list[str]
) -> bool:
    if pattern.fullmatch(value) is None or value in {".", ".."}:
        errors.append(f"{label} has an unsafe directory name {value!r}")
        return False
    return True


def _directory_entries(path: Path, label: str, errors: list[str]) -> list[Path]:
    try:
        metadata = path.lstat()
    except OSError as exc:
        errors.append(f"Cannot inspect {label}: {exc}")
        return []
    if _is_link_or_reparse(metadata):
        errors.append(f"{label} must not be a symbolic link or reparse point")
        return []
    if not stat.S_ISDIR(metadata.st_mode):
        errors.append(f"{label} must be a directory")
        return []
    try:
        entries = sorted(path.iterdir(), key=lambda item: (item.name.casefold(), item.name))
    except OSError as exc:
        errors.append(f"Cannot enumerate {label}: {exc}")
        return []
    _case_collisions(entries, label, errors)
    return entries


def _is_empty_gitkeep(entry: Path, label: str, errors: list[str]) -> bool:
    if entry.name != ".gitkeep":
        return False
    try:
        metadata = entry.lstat()
        size = metadata.st_size
    except OSError as exc:
        errors.append(f"Cannot inspect {label}/.gitkeep: {exc}")
        return True
    if _is_link_or_reparse(metadata) or not stat.S_ISREG(metadata.st_mode) or size != 0:
        errors.append(f"{label}/.gitkeep must be an empty regular file")
    return True


def _discover_packages(root: Path, errors: list[str]) -> list[PackageLocation]:
    inbox = root / INBOX_PATH
    if not inbox.exists():
        return []
    packages: list[PackageLocation] = []
    inbox_label = INBOX_PATH.as_posix()
    for run_entry in _directory_entries(inbox, inbox_label, errors):
        if _is_empty_gitkeep(run_entry, inbox_label, errors):
            continue
        if run_entry.name == "README.md":
            # Human guidance belongs to the inbox root, not to a source package.
            try:
                readme_metadata = run_entry.lstat()
            except OSError as exc:
                errors.append(f"Cannot inspect {inbox_label}/README.md: {exc}")
            else:
                if _is_link_or_reparse(readme_metadata) or not stat.S_ISREG(
                    readme_metadata.st_mode
                ):
                    errors.append(f"{inbox_label}/README.md must be a regular file")
            continue
        if not _safe_component(
            run_entry.name, "run_id directory", RUN_COMPONENT_PATTERN, errors
        ):
            continue
        run_entries = _directory_entries(
            run_entry, f"{inbox_label}/{run_entry.name}", errors
        )
        for provider_entry in run_entries:
            parent_label = f"{inbox_label}/{run_entry.name}"
            if _is_empty_gitkeep(provider_entry, parent_label, errors):
                continue
            if provider_entry.name not in PROVIDERS:
                errors.append(
                    f"Unsupported provider directory {provider_entry.name!r} under "
                    f"{parent_label}"
                )
            provider_entries = _directory_entries(
                provider_entry, f"{parent_label}/{provider_entry.name}", errors
            )
            for source_entry in provider_entries:
                provider_label = f"{parent_label}/{provider_entry.name}"
                if _is_empty_gitkeep(source_entry, provider_label, errors):
                    continue
                if not _safe_component(
                    source_entry.name,
                    "source_id directory",
                    SOURCE_ID_PATTERN,
                    errors,
                ):
                    continue
                if not source_entry.name.startswith(f"{provider_entry.name}-"):
                    errors.append(
                        f"source_id directory {source_entry.name!r} must start with "
                        f"provider prefix {provider_entry.name + '-'!r}"
                    )
                try:
                    source_metadata = source_entry.lstat()
                except OSError as exc:
                    errors.append(f"Cannot inspect source package {source_entry}: {exc}")
                    continue
                if _is_link_or_reparse(source_metadata):
                    errors.append(
                        "Source package must not be a symbolic link or reparse point: "
                        f"{source_entry}"
                    )
                    continue
                if not stat.S_ISDIR(source_metadata.st_mode):
                    errors.append(f"Source package must be a directory: {source_entry}")
                    continue
                packages.append(
                    PackageLocation(
                        run_entry.name,
                        provider_entry.name,
                        source_entry.name,
                        source_entry,
                    )
                )
    return packages


def _validate_consolidated_outputs(root: Path, errors: list[str]) -> None:
    relative_run_path = Path("docs/knowledge/consolidated") / RUN_ID
    run_path = root / relative_run_path
    if not run_path.exists() and not run_path.is_symlink():
        return

    pending: list[tuple[Path, str]] = [(run_path, relative_run_path.as_posix())]
    while pending:
        directory, directory_label = pending.pop()
        entries = _directory_entries(directory, directory_label, errors)
        for entry in entries:
            entry_label = f"{directory_label}/{entry.name}"
            if (
                SAFE_OUTPUT_COMPONENT_PATTERN.fullmatch(entry.name) is None
                or entry.name in {".", ".."}
            ):
                errors.append(
                    f"Consolidated output has unsafe path component {entry.name!r} "
                    f"under {directory_label}"
                )
                continue
            try:
                metadata = entry.lstat()
            except OSError as exc:
                errors.append(f"Cannot inspect consolidated output {entry_label}: {exc}")
                continue
            if _is_link_or_reparse(metadata):
                errors.append(
                    "Consolidated output must not be a symbolic link or reparse point: "
                    f"{entry_label}"
                )
            elif stat.S_ISDIR(metadata.st_mode):
                pending.append((entry, entry_label))
            elif stat.S_ISREG(metadata.st_mode):
                _read_utf8_file(
                    entry,
                    f"consolidated output {entry_label}",
                    errors,
                    allow_empty=False,
                )
            else:
                errors.append(
                    f"Consolidated output must be a regular file or directory: "
                    f"{entry_label}"
                )


def _validate_ready_metadata_values(
    value: dict[str, Any],
    label: str,
    base_main_shas: set[str],
    errors: list[str],
) -> None:
    missing = sorted(READY_METADATA_FIELDS - set(value))
    if missing:
        errors.append(f"{label} is missing metadata fields: {', '.join(missing)}")
    if value.get("normative_status") != NON_NORMATIVE_STATUS:
        errors.append(
            f"{label} normative_status must be {NON_NORMATIVE_STATUS!r}"
        )
    if value.get("run_id") != RUN_ID:
        errors.append(f"{label} run_id must be {RUN_ID!r}")
    base_main_sha = value.get("base_main_sha")
    if not isinstance(base_main_sha, str) or BASE_MAIN_SHA_PATTERN.fullmatch(
        base_main_sha
    ) is None:
        errors.append(f"{label} base_main_sha must be 40 lowercase hex characters")
    else:
        base_main_shas.add(base_main_sha)


def _validate_ready_markdown(
    text: str,
    filename: str,
    label: str,
    base_main_shas: set[str],
    errors: list[str],
) -> None:
    all_lines = text.splitlines()
    lines = all_lines[:MARKDOWN_METADATA_SCAN_LINES]
    status_line = f"Status: {NON_NORMATIVE_STATUS}"
    run_line = f"Run-ID: {RUN_ID}"
    if lines.count(status_line) != 1:
        errors.append(
            f"{label} must contain exactly one {status_line!r} near the top"
        )
    if lines.count(run_line) != 1:
        errors.append(f"{label} must contain exactly one {run_line!r} near the top")
    base_lines = [line for line in lines if line.startswith("Base-Main-SHA:")]
    if len(base_lines) != 1:
        errors.append(
            f"{label} must contain exactly one Base-Main-SHA line near the top"
        )
        return
    match = re.fullmatch(r"Base-Main-SHA: ([0-9a-f]{40})", base_lines[0])
    if match is None:
        errors.append(f"{label} Base-Main-SHA must be 40 lowercase hex characters")
    else:
        base_main_shas.add(match.group(1))

    required_headings = READY_MARKDOWN_REQUIRED_HEADINGS.get(filename, frozenset())
    missing_headings = sorted(required_headings - set(all_lines))
    if missing_headings:
        errors.append(
            f"{label} is missing required headings: {', '.join(missing_headings)}"
        )
    metadata_prefixes = (
        "Status:",
        "Run-ID:",
        "Base-Main-SHA:",
        "Secret-Scan:",
        "PII-Review:",
        "Redactions:",
    )
    substantive = "".join(
        line.strip()
        for line in all_lines
        if line.strip()
        and not line.startswith("#")
        and not line.startswith(metadata_prefixes)
    )
    if len(substantive) < MIN_READY_MARKDOWN_BODY_CHARACTERS:
        errors.append(
            f"{label} must contain at least {MIN_READY_MARKDOWN_BODY_CHARACTERS} "
            "substantive body characters"
        )

    if filename == "INTEGRATION_REPORT.md":
        for exact_line in ("Secret-Scan: PASS", "PII-Review: PASS"):
            if lines.count(exact_line) != 1:
                errors.append(
                    f"{label} must contain exactly one {exact_line!r} near the top"
                )
        redaction_lines = [line for line in lines if line.startswith("Redactions:")]
        if len(redaction_lines) != 1 or re.fullmatch(
            r"Redactions: [0-9]+", redaction_lines[0]
        ) is None:
            errors.append(
                f"{label} must contain exactly one non-negative Redactions line "
                "near the top"
            )


def _validate_ready_csv(
    text: str, label: str, base_main_shas: set[str], errors: list[str]
) -> set[tuple[str, str]]:
    references: set[tuple[str, str]] = set()
    try:
        reader = csv.DictReader(io.StringIO(text), strict=True)
        fieldnames = reader.fieldnames
        rows = list(reader)
    except csv.Error as exc:
        errors.append(f"Invalid CSV in {label}: {exc}")
        return references
    if fieldnames is None:
        errors.append(f"{label} must contain a CSV header")
        return references
    duplicate_fields = sorted(
        field for field, count in Counter(fieldnames).items() if count > 1
    )
    if duplicate_fields:
        errors.append(f"{label} has duplicate CSV fields: {', '.join(duplicate_fields)}")
    missing_fields = sorted(TRACEABILITY_REQUIRED_FIELDS - set(fieldnames))
    if missing_fields:
        errors.append(
            f"{label} CSV header is missing: {', '.join(missing_fields)}"
        )
    if not rows:
        errors.append(f"{label} must contain at least one CSV data row")
    for row_number, row in enumerate(rows, start=2):
        row_label = f"{label}:{row_number}"
        if None in row:
            errors.append(f"{row_label} has values beyond the CSV header")
        if any(value is None for key, value in row.items() if key is not None):
            errors.append(f"{row_label} has fewer values than the CSV header")
        _validate_ready_metadata_values(row, row_label, base_main_shas, errors)
        trace_id = _trimmed_string(row.get("trace_id"), f"{row_label}.trace_id", errors)
        source_id = _trimmed_string(
            row.get("source_id"),
            f"{row_label}.source_id",
            errors,
            pattern=SOURCE_ID_PATTERN,
        )
        item_id = _trimmed_string(
            row.get("item_id"),
            f"{row_label}.item_id",
            errors,
            pattern=ITEM_ID_PATTERN,
        )
        _trimmed_string(row.get("target_ref"), f"{row_label}.target_ref", errors)
        disposition = _enum_string(
            row.get("disposition"),
            DISPOSITION_VALUES,
            f"{row_label}.disposition",
            errors,
        )
        exclusion_reason = row.get("exclusion_reason")
        if disposition == "excluded":
            _trimmed_string(
                exclusion_reason, f"{row_label}.exclusion_reason", errors
            )
        elif disposition == "included" and exclusion_reason not in {"", None}:
            errors.append(
                f"{row_label}.exclusion_reason must be empty when included"
            )
        if trace_id is not None and source_id is not None and item_id is not None:
            reference = (source_id, item_id)
            if reference in references:
                errors.append(
                    f"{label} contains duplicate source/item reference {reference!r}"
                )
            references.add(reference)
    return references


def _validate_ready_catalog(
    text: str, label: str, base_main_shas: set[str], errors: list[str]
) -> set[tuple[str, str]]:
    references: set[tuple[str, str]] = set()
    canonical_ids: set[str] = set()
    object_count = 0
    for line_number, line in enumerate(text.splitlines(), start=1):
        line_label = f"{label}:{line_number}"
        if not line.strip():
            errors.append(f"{line_label} must not be blank")
            continue
        if len(line.encode("utf-8")) > MAX_NDJSON_LINE_BYTES:
            errors.append(f"{line_label} exceeds the NDJSON line size limit")
            continue
        value = _parse_json_object(line, line_label, errors)
        if value is None:
            continue
        object_count += 1
        _validate_ready_metadata_values(value, line_label, base_main_shas, errors)
        missing_fields = sorted(CATALOG_REQUIRED_FIELDS - set(value))
        if missing_fields:
            errors.append(
                f"{line_label} is missing catalog fields: {', '.join(missing_fields)}"
            )
        canonical_id = _trimmed_string(
            value.get("canonical_id"), f"{line_label}.canonical_id", errors
        )
        if canonical_id is not None:
            if canonical_id in canonical_ids:
                errors.append(f"{label} contains duplicate canonical_id {canonical_id!r}")
            canonical_ids.add(canonical_id)
        disposition = _enum_string(
            value.get("disposition"),
            DISPOSITION_VALUES,
            f"{line_label}.disposition",
            errors,
        )
        exclusion_reason = value.get("exclusion_reason")
        if disposition == "excluded":
            _trimmed_string(
                exclusion_reason, f"{line_label}.exclusion_reason", errors
            )
        elif disposition == "included" and exclusion_reason is not None:
            errors.append(
                f"{line_label}.exclusion_reason must be null when included"
            )
        raw_refs = value.get("source_refs")
        if not isinstance(raw_refs, list) or not raw_refs:
            errors.append(f"{line_label}.source_refs must be a non-empty JSON array")
            continue
        for ref_index, raw_ref in enumerate(raw_refs):
            ref_label = f"{line_label}.source_refs[{ref_index}]"
            if not isinstance(raw_ref, dict):
                errors.append(f"{ref_label} must be a JSON object")
                continue
            _exact_fields(raw_ref, SOURCE_REF_FIELDS, ref_label, errors)
            source_id = _trimmed_string(
                raw_ref.get("source_id"),
                f"{ref_label}.source_id",
                errors,
                pattern=SOURCE_ID_PATTERN,
            )
            item_id = _trimmed_string(
                raw_ref.get("item_id"),
                f"{ref_label}.item_id",
                errors,
                pattern=ITEM_ID_PATTERN,
            )
            if source_id is not None and item_id is not None:
                reference = (source_id, item_id)
                if reference in references:
                    errors.append(
                        f"{label} contains duplicate source/item reference {reference!r}"
                    )
                references.add(reference)
    if object_count == 0:
        errors.append(f"{label} must contain at least one JSON object")
    return references


def _validate_ready_consolidated_outputs(
    root: Path,
    sources_by_id: dict[str, dict[str, Any]],
    packages: list[ParsedPackage],
    errors: list[str],
) -> str | None:
    relative_run_path = Path("docs/knowledge/consolidated") / RUN_ID
    run_path = root / relative_run_path
    if not run_path.exists() and not run_path.is_symlink():
        errors.append(
            f"Ready registry requires consolidated directory {relative_run_path.as_posix()}"
        )
        return None
    entries = _directory_entries(run_path, relative_run_path.as_posix(), errors)
    actual_names = {entry.name for entry in entries}
    missing = sorted(READY_CONSOLIDATED_FILES - actual_names)
    unexpected = sorted(actual_names - READY_CONSOLIDATED_FILES)
    if missing:
        errors.append(
            "Ready consolidated outputs are missing: " + ", ".join(missing)
        )
    if unexpected:
        errors.append(
            "Ready consolidated outputs are unexpected: " + ", ".join(unexpected)
        )

    contents: dict[str, str] = {}
    for filename in sorted(READY_CONSOLIDATED_FILES):
        path = run_path / filename
        if not path.exists() and not path.is_symlink():
            continue
        loaded = _read_utf8_file(
            path,
            f"ready consolidated output {relative_run_path.as_posix()}/{filename}",
            errors,
            allow_empty=False,
        )
        if loaded is not None:
            contents[filename] = loaded[1]

    base_main_shas: set[str] = set()
    for filename in sorted(READY_MARKDOWN_FILES):
        text = contents.get(filename)
        if text is not None:
            _validate_ready_markdown(
                text,
                filename,
                f"ready consolidated output {filename}",
                base_main_shas,
                errors,
            )
    csv_references: set[tuple[str, str]] = set()
    csv_text = contents.get("04_TRACEABILITY_MATRIX.csv")
    if csv_text is not None:
        csv_references = _validate_ready_csv(
            csv_text,
            "ready consolidated output 04_TRACEABILITY_MATRIX.csv",
            base_main_shas,
            errors,
        )
    catalog_references: set[tuple[str, str]] = set()
    catalog_text = contents.get("catalog.ndjson")
    if catalog_text is not None:
        catalog_references = _validate_ready_catalog(
            catalog_text,
            "ready consolidated output catalog.ndjson",
            base_main_shas,
            errors,
        )
    integrated_source_ids = {
        source_id
        for source_id, source in sources_by_id.items()
        if source.get("status") == "integrated"
    }
    expected_references = {
        (package.location.source_id, item["id"])
        for package in packages
        if package.location.source_id in integrated_source_ids
        for item in package.items
        if isinstance(item.get("id"), str)
    }
    for surface, actual_references in (
        ("traceability CSV", csv_references),
        ("catalog NDJSON", catalog_references),
    ):
        missing_references = sorted(expected_references - actual_references)
        unexpected_references = sorted(actual_references - expected_references)
        if missing_references:
            errors.append(
                f"Ready {surface} is missing integrated source/item references: "
                + ", ".join(f"{source_id}/{item_id}" for source_id, item_id in missing_references)
            )
        if unexpected_references:
            errors.append(
                f"Ready {surface} has unknown or non-integrated source/item references: "
                + ", ".join(
                    f"{source_id}/{item_id}"
                    for source_id, item_id in unexpected_references
                )
            )
    if len(base_main_shas) != 1:
        errors.append(
            "Ready consolidated outputs must use exactly one common Base-Main-SHA"
        )
        return None
    return next(iter(base_main_shas))


def _validate_inventory_attestation(
    raw_attestation: Any, state: str | None, errors: list[str]
) -> None:
    label = "SOURCE_REGISTRY.json source_inventory_attestation"
    if not isinstance(raw_attestation, dict):
        errors.append(f"{label} must be a JSON object")
        return
    _exact_fields(
        raw_attestation, SOURCE_INVENTORY_ATTESTATION_FIELDS, label, errors
    )
    status_value = _enum_string(
        raw_attestation.get("status"),
        INVENTORY_ATTESTATION_STATUS_VALUES,
        f"{label}.status",
        errors,
    )
    detail_fields = ("attested_by", "attested_at", "evidence_locator", "scope_note")
    if status_value == "pending":
        for field in detail_fields:
            if raw_attestation.get(field) is not None:
                errors.append(f"{label}.{field} must be null while status is 'pending'")
    elif status_value == "user_confirmed_complete":
        _trimmed_string(
            raw_attestation.get("attested_by"), f"{label}.attested_by", errors
        )
        attested_at = _trimmed_string(
            raw_attestation.get("attested_at"), f"{label}.attested_at", errors
        )
        if attested_at is not None:
            try:
                if ATTESTED_AT_PATTERN.fullmatch(attested_at) is None:
                    raise ValueError
                datetime.fromisoformat(attested_at.replace("Z", "+00:00"))
            except ValueError:
                errors.append(
                    f"{label}.attested_at must be an ISO-8601 date-time with timezone"
                )
        _trimmed_string(
            raw_attestation.get("evidence_locator"),
            f"{label}.evidence_locator",
            errors,
        )
        _trimmed_string(
            raw_attestation.get("scope_note"), f"{label}.scope_note", errors
        )
    if state == "ready" and status_value != "user_confirmed_complete":
        errors.append(
            "Ready registry source_inventory_attestation status must be "
            "'user_confirmed_complete'"
        )


def _validate_registry(
    root: Path, errors: list[str]
) -> tuple[dict[str, Any] | None, dict[str, dict[str, Any]]]:
    registry_path = _repo_regular_file(root, REGISTRY_PATH, errors)
    if registry_path is None:
        return None, {}
    registry = _load_json_object(registry_path, REGISTRY_PATH.as_posix(), errors)
    if registry is None:
        return None, {}
    _exact_fields(registry, REGISTRY_FIELDS, "SOURCE_REGISTRY.json", errors)

    if registry.get("schema_version") != SCHEMA_VERSION:
        errors.append(f"SOURCE_REGISTRY.json schema_version must be {SCHEMA_VERSION!r}")
    if registry.get("run_id") != RUN_ID:
        errors.append(f"SOURCE_REGISTRY.json run_id must be {RUN_ID!r}")
    if registry.get("canonical_repository") != CANONICAL_REPOSITORY:
        errors.append(
            "SOURCE_REGISTRY.json canonical_repository must identify the canonical repo"
        )
    if registry.get("default_branch") != DEFAULT_BRANCH:
        errors.append(f"SOURCE_REGISTRY.json default_branch must be {DEFAULT_BRANCH!r}")

    state = _enum_string(
        registry.get("state"),
        frozenset({"collecting", "ready"}),
        "SOURCE_REGISTRY.json state",
        errors,
    )
    _validate_inventory_attestation(
        registry.get("source_inventory_attestation"), state, errors
    )
    required_providers = _string_array(
        registry.get("required_providers"),
        "SOURCE_REGISTRY.json required_providers",
        errors,
    )
    if required_providers != list(PROVIDERS):
        errors.append(
            "SOURCE_REGISTRY.json required_providers must be "
            "['codex', 'claude', 'chatgpt'] in that order"
        )

    raw_expected = registry.get("expected_sources")
    if not isinstance(raw_expected, list):
        errors.append("SOURCE_REGISTRY.json expected_sources must be a JSON array")
        raw_expected = []
    expected_sources: list[dict[str, Any]] = []
    expected_keys: list[tuple[str, str]] = []
    for index, raw_item in enumerate(raw_expected):
        label = f"SOURCE_REGISTRY.json expected_sources[{index}]"
        if not isinstance(raw_item, dict):
            errors.append(f"{label} must be a JSON object")
            continue
        _exact_fields(raw_item, EXPECTED_SOURCE_FIELDS, label, errors)
        provider = _enum_string(
            raw_item.get("provider"), frozenset(PROVIDERS), f"{label}.provider", errors
        )
        conversation_id = _trimmed_string(
            raw_item.get("conversation_id"),
            f"{label}.conversation_id",
            errors,
            max_length=256,
        )
        if not isinstance(raw_item.get("required"), bool):
            errors.append(f"{label}.required must be a boolean")
        status_value = _enum_string(
            raw_item.get("status"),
            EXPECTED_SOURCE_STATUS_VALUES,
            f"{label}.status",
            errors,
        )
        source_id = _optional_trimmed_string(
            raw_item.get("source_id"), f"{label}.source_id", errors
        )
        reason = _optional_trimmed_string(raw_item.get("reason"), f"{label}.reason", errors)
        if source_id is not None and SOURCE_ID_PATTERN.fullmatch(source_id) is None:
            errors.append(f"{label}.source_id has an invalid format")
        if status_value == "pending":
            if source_id is not None:
                errors.append(f"{label}.source_id must be null when status is 'pending'")
            if reason is not None:
                errors.append(f"{label}.reason must be null when status is 'pending'")
        elif status_value == "imported":
            if source_id is None:
                errors.append(f"{label}.source_id is required when status is 'imported'")
            if reason is not None:
                errors.append(f"{label}.reason must be null when status is 'imported'")
        elif status_value == "unavailable":
            if source_id is not None:
                errors.append(
                    f"{label}.source_id must be null when status is 'unavailable'"
                )
            if reason is None:
                errors.append(f"{label}.reason is required when status is 'unavailable'")
        if provider is not None and conversation_id is not None:
            expected_keys.append((provider, conversation_id))
        expected_sources.append(raw_item)

    duplicate_expected = sorted(
        key for key, count in Counter(expected_keys).items() if count > 1
    )
    for provider, conversation_id in duplicate_expected:
        errors.append(
            "SOURCE_REGISTRY.json has duplicate expected source "
            f"({provider!r}, {conversation_id!r})"
        )

    raw_sources = registry.get("sources")
    if not isinstance(raw_sources, list):
        errors.append("SOURCE_REGISTRY.json sources must be a JSON array")
        raw_sources = []
    sources_by_id: dict[str, dict[str, Any]] = {}
    fingerprints: list[str] = []
    for index, raw_item in enumerate(raw_sources):
        label = f"SOURCE_REGISTRY.json sources[{index}]"
        if not isinstance(raw_item, dict):
            errors.append(f"{label} must be a JSON object")
            continue
        _exact_fields(raw_item, REGISTRY_SOURCE_FIELDS, label, errors)
        provider = _enum_string(
            raw_item.get("provider"), frozenset(PROVIDERS), f"{label}.provider", errors
        )
        source_id = _trimmed_string(
            raw_item.get("source_id"),
            f"{label}.source_id",
            errors,
            pattern=SOURCE_ID_PATTERN,
        )
        _trimmed_string(
            raw_item.get("conversation_id"),
            f"{label}.conversation_id",
            errors,
            max_length=256,
        )
        _enum_string(
            raw_item.get("status"),
            REGISTRY_SOURCE_STATUS_VALUES,
            f"{label}.status",
            errors,
        )
        fingerprint = _sha256_string(
            raw_item.get("content_fingerprint_sha256"),
            f"{label}.content_fingerprint_sha256",
            errors,
        )
        manifest_path = _trimmed_string(
            raw_item.get("manifest_path"), f"{label}.manifest_path", errors
        )
        if source_id is not None:
            if source_id in sources_by_id:
                errors.append(f"SOURCE_REGISTRY.json has duplicate source_id {source_id!r}")
            else:
                sources_by_id[source_id] = raw_item
        if fingerprint is not None:
            fingerprints.append(fingerprint)
        if provider is not None and source_id is not None and manifest_path is not None:
            expected_path = (
                INBOX_PATH / RUN_ID / provider / source_id / "SOURCE_MANIFEST.json"
            ).as_posix()
            if manifest_path != expected_path:
                errors.append(f"{label}.manifest_path must be {expected_path!r}")
            pure_path = PurePosixPath(manifest_path)
            if pure_path.is_absolute() or ".." in pure_path.parts or "\\" in manifest_path:
                errors.append(f"{label}.manifest_path is unsafe")

    duplicate_fingerprints = sorted(
        value for value, count in Counter(fingerprints).items() if count > 1
    )
    if duplicate_fingerprints:
        errors.append("SOURCE_REGISTRY.json contains duplicate content fingerprints")

    for index, expected in enumerate(expected_sources):
        if expected.get("status") != "imported":
            continue
        source_id = expected.get("source_id")
        source = sources_by_id.get(source_id) if isinstance(source_id, str) else None
        if source is None:
            errors.append(
                f"SOURCE_REGISTRY.json expected_sources[{index}] imports unknown "
                f"source_id {source_id!r}"
            )
            continue
        if source.get("provider") != expected.get("provider"):
            errors.append(
                f"SOURCE_REGISTRY.json expected_sources[{index}] provider does not "
                "match its source"
            )
        if source.get("conversation_id") != expected.get("conversation_id"):
            errors.append(
                f"SOURCE_REGISTRY.json expected_sources[{index}] conversation_id does "
                "not match its source"
            )

    if state == "ready":
        if not expected_sources:
            errors.append("Ready registry expected_sources must not be empty")
        for provider in PROVIDERS:
            if not any(
                expected.get("provider") == provider
                and expected.get("required") is True
                for expected in expected_sources
            ):
                errors.append(
                    "Ready registry must declare at least one required expected "
                    f"source for provider {provider!r}"
                )
        integrated_providers = {
            source.get("provider")
            for source in sources_by_id.values()
            if source.get("status") == "integrated"
        }
        for provider in PROVIDERS:
            if provider not in integrated_providers:
                errors.append(
                    f"Ready registry has no integrated source for provider {provider!r}"
                )
        for source_id, source in sources_by_id.items():
            if source.get("status") not in {"integrated", "superseded"}:
                errors.append(
                    f"Ready registry source {source_id!r} must be integrated or superseded"
                )
        for index, expected in enumerate(expected_sources):
            if expected.get("status") != "imported":
                continue
            source = sources_by_id.get(expected.get("source_id"))
            if source is None or source.get("status") not in {
                "integrated",
                "superseded",
            }:
                errors.append(
                    f"Ready registry imported expected_sources[{index}] must resolve "
                    "to an integrated or superseded source"
                )
        for index, expected in enumerate(expected_sources):
            if expected.get("required") is not True:
                continue
            if expected.get("status") != "imported":
                errors.append(
                    f"Ready registry required expected_sources[{index}] must be imported"
                )
                continue
            source = sources_by_id.get(expected.get("source_id"))
            if source is None or source.get("status") != "integrated":
                errors.append(
                    f"Ready registry required expected_sources[{index}] must resolve "
                    "to an integrated source"
                )

    return registry, sources_by_id


def _validate_manifest(
    manifest: dict[str, Any], location: PackageLocation, errors: list[str]
) -> tuple[dict[str, dict[str, Any]], dict[str, int]]:
    label = f"{location.path}/SOURCE_MANIFEST.json"
    _exact_fields(manifest, MANIFEST_FIELDS, label, errors)
    if manifest.get("schema_version") != SCHEMA_VERSION:
        errors.append(f"{label} schema_version must be {SCHEMA_VERSION!r}")
    if manifest.get("run_id") != location.run_id:
        errors.append(f"{label} run_id does not match its directory")
    if manifest.get("provider") != location.provider:
        errors.append(f"{label} provider does not match its directory")
    if manifest.get("source_id") != location.source_id:
        errors.append(f"{label} source_id does not match its directory")
    if not location.source_id.startswith(f"{location.provider}-"):
        errors.append(
            f"{label} source_id must start with provider prefix "
            f"{location.provider + '-'!r}"
        )
    _trimmed_string(
        manifest.get("conversation_id"),
        f"{label}.conversation_id",
        errors,
        max_length=256,
    )
    _trimmed_string(
        manifest.get("conversation_title"),
        f"{label}.conversation_title",
        errors,
        max_length=512,
    )
    _enum_string(manifest.get("coverage"), COVERAGE_VALUES, f"{label}.coverage", errors)
    _string_array(manifest.get("inaccessible"), f"{label}.inaccessible", errors)
    _enum_string(
        manifest.get("status"), MANIFEST_STATUS_VALUES, f"{label}.status", errors
    )
    privacy_review = manifest.get("privacy_review")
    if not isinstance(privacy_review, dict):
        errors.append(f"{label}.privacy_review must be a JSON object")
    else:
        _exact_fields(
            privacy_review, PRIVACY_REVIEW_FIELDS, f"{label}.privacy_review", errors
        )
        if privacy_review.get("secret_scan") != "passed":
            errors.append(f"{label}.privacy_review.secret_scan must be 'passed'")
        if privacy_review.get("pii_review") != "passed":
            errors.append(f"{label}.privacy_review.pii_review must be 'passed'")
        redactions = privacy_review.get("redactions")
        if isinstance(redactions, bool) or not isinstance(redactions, int) or redactions < 0:
            errors.append(
                f"{label}.privacy_review.redactions must be a non-negative integer"
            )
    source_snapshot = _sha256_string(
        manifest.get("source_snapshot_sha256"),
        f"{label}.source_snapshot_sha256",
        errors,
    )
    manifest_source_id = manifest.get("source_id")
    if (
        source_snapshot is not None
        and isinstance(manifest_source_id, str)
        and not manifest_source_id.endswith(f"-{source_snapshot[:12]}")
    ):
        errors.append(
            f"{label} source_id must end with the first 12 characters of "
            "source_snapshot_sha256"
        )
    _sha256_string(
        manifest.get("content_fingerprint_sha256"),
        f"{label}.content_fingerprint_sha256",
        errors,
    )

    raw_files = manifest.get("files")
    files_by_path: dict[str, dict[str, Any]] = {}
    if not isinstance(raw_files, list):
        errors.append(f"{label}.files must be a JSON array")
        raw_files = []
    for index, raw_file in enumerate(raw_files):
        file_label = f"{label}.files[{index}]"
        if not isinstance(raw_file, dict):
            errors.append(f"{file_label} must be a JSON object")
            continue
        _exact_fields(raw_file, MANIFEST_FILE_FIELDS, file_label, errors)
        path_value = _trimmed_string(raw_file.get("path"), f"{file_label}.path", errors)
        byte_count = raw_file.get("bytes")
        if isinstance(byte_count, bool) or not isinstance(byte_count, int) or byte_count < 1:
            errors.append(f"{file_label}.bytes must be a positive integer")
        _sha256_string(raw_file.get("sha256"), f"{file_label}.sha256", errors)
        if path_value is not None:
            if path_value not in MANIFESTED_PACKAGE_FILES:
                errors.append(f"{file_label}.path is not an allowed package filename")
            if path_value in files_by_path:
                errors.append(f"{label}.files contains duplicate path {path_value!r}")
            else:
                files_by_path[path_value] = raw_file
    if set(files_by_path) != set(MANIFESTED_PACKAGE_FILES):
        missing = sorted(MANIFESTED_PACKAGE_FILES - set(files_by_path))
        extra = sorted(set(files_by_path) - MANIFESTED_PACKAGE_FILES)
        if missing:
            errors.append(f"{label}.files is missing: {', '.join(missing)}")
        if extra:
            errors.append(f"{label}.files has unexpected paths: {', '.join(extra)}")

    raw_counts = manifest.get("counts")
    counts: dict[str, int] = {}
    if not isinstance(raw_counts, dict):
        errors.append(f"{label}.counts must be a JSON object")
    else:
        _exact_fields(raw_counts, MANIFEST_COUNT_FIELDS, f"{label}.counts", errors)
        for key in sorted(MANIFEST_COUNT_FIELDS):
            value = raw_counts.get(key)
            if isinstance(value, bool) or not isinstance(value, int) or value < 0:
                errors.append(f"{label}.counts.{key} must be a non-negative integer")
            else:
                counts[key] = value
    return files_by_path, counts


def _validate_source_date(value: Any, label: str, errors: list[str]) -> None:
    if value is None:
        return
    parsed = _trimmed_string(value, label, errors, max_length=64)
    if parsed is None:
        return
    try:
        if SOURCE_DATE_PATTERN.fullmatch(parsed) is None:
            raise ValueError
        if re.fullmatch(r"\d{4}-\d{2}-\d{2}", parsed):
            date.fromisoformat(parsed)
        elif "T" in parsed:
            datetime.fromisoformat(parsed.replace("Z", "+00:00"))
        else:
            raise ValueError
    except ValueError:
        errors.append(f"{label} must be an ISO-8601 date/date-time or null")


def _validate_item(
    item: dict[str, Any],
    item_label: str,
    manifest: dict[str, Any],
    errors: list[str],
) -> None:
    _exact_fields(item, ITEM_FIELDS, item_label, errors)
    if item.get("schema_version") != SCHEMA_VERSION:
        errors.append(f"{item_label}.schema_version must be {SCHEMA_VERSION!r}")
    item_id = _trimmed_string(
        item.get("id"), f"{item_label}.id", errors, pattern=ITEM_ID_PATTERN
    )
    source_id = _trimmed_string(
        item.get("source_id"),
        f"{item_label}.source_id",
        errors,
        pattern=SOURCE_ID_PATTERN,
    )
    if source_id is not None and source_id != manifest.get("source_id"):
        errors.append(f"{item_label}.source_id does not match its manifest")
    item_type = _enum_string(
        item.get("type"), ITEM_TYPE_VALUES, f"{item_label}.type", errors
    )
    module = _trimmed_string(
        item.get("module"), f"{item_label}.module", errors, max_length=128
    )
    statement = _trimmed_string(
        item.get("statement"), f"{item_label}.statement", errors, max_length=20000
    )
    current_or_target = _enum_string(
        item.get("current_or_target"),
        CURRENT_OR_TARGET_VALUES,
        f"{item_label}.current_or_target",
        errors,
    )
    status_value = _enum_string(
        item.get("status"), ITEM_STATUS_VALUES, f"{item_label}.status", errors
    )
    _enum_string(item.get("priority"), PRIORITY_VALUES, f"{item_label}.priority", errors)
    provider = _enum_string(
        item.get("source_provider"),
        frozenset(PROVIDERS),
        f"{item_label}.source_provider",
        errors,
    )
    if provider is not None and provider != manifest.get("provider"):
        errors.append(f"{item_label}.source_provider does not match its manifest")
    conversation_id = _trimmed_string(
        item.get("conversation_id"),
        f"{item_label}.conversation_id",
        errors,
        max_length=256,
    )
    if conversation_id is not None and conversation_id != manifest.get("conversation_id"):
        errors.append(f"{item_label}.conversation_id does not match its manifest")
    source_locator = _trimmed_string(
        item.get("source_locator"),
        f"{item_label}.source_locator",
        errors,
        max_length=1024,
    )
    identity_parts = (
        source_id,
        source_locator,
        item_type,
        module,
        current_or_target,
        statement,
    )
    if item_id is not None and all(part is not None for part in identity_parts):
        identity_text = "\n".join(part for part in identity_parts if part is not None)
        expected_item_id = (
            "KN-"
            + hashlib.sha256(identity_text.encode("utf-8")).hexdigest()[:24].upper()
        )
        if item_id != expected_item_id:
            errors.append(
                f"{item_label}.id does not match its deterministic content identity"
            )
    _validate_source_date(item.get("source_date"), f"{item_label}.source_date", errors)
    confidence = _enum_string(
        item.get("confidence"), CONFIDENCE_VALUES, f"{item_label}.confidence", errors
    )
    relation_values: dict[str, list[str]] = {}
    for field in ("supersedes", "conflicts_with", "related_items"):
        relation_values[field] = _string_array(
            item.get(field), f"{item_label}.{field}", errors, pattern=ITEM_ID_PATTERN
        )
    if status_value == "CONFLICT" and not relation_values["conflicts_with"]:
        errors.append(f"{item_label} with CONFLICT status must name conflicts_with")
    if status_value == "INFERRED" and confidence == "HIGH":
        errors.append(f"{item_label} with INFERRED status cannot have HIGH confidence")


def _parse_ndjson(
    text: str,
    label: str,
    manifest: dict[str, Any],
    errors: list[str],
) -> list[dict[str, Any]]:
    items: list[dict[str, Any]] = []
    for line_number, line in enumerate(text.splitlines(), start=1):
        line_label = f"{label}:{line_number}"
        if not line.strip():
            errors.append(f"{line_label} must not be blank")
            continue
        if len(line.encode("utf-8")) > MAX_NDJSON_LINE_BYTES:
            errors.append(f"{line_label} exceeds the NDJSON line size limit")
            continue
        item = _parse_json_object(line, line_label, errors)
        if item is None:
            continue
        _validate_item(item, line_label, manifest, errors)
        items.append(item)
    return items


def _validate_attachment_index(
    text: str, label: str, errors: list[str]
) -> int | None:
    lines = text.splitlines()
    count_line_indexes = [
        index for index, line in enumerate(lines) if line.startswith("Attachment-Count:")
    ]
    if len(count_line_indexes) != 1 or count_line_indexes[0] >= MARKDOWN_METADATA_SCAN_LINES:
        errors.append(
            f"{label} must contain exactly one Attachment-Count metadata line "
            f"within the first {MARKDOWN_METADATA_SCAN_LINES} lines"
        )
        declared_count: int | None = None
    else:
        count_line = lines[count_line_indexes[0]]
        match = re.fullmatch(r"Attachment-Count: (0|[1-9][0-9]*)", count_line)
        if match is None:
            errors.append(
                f"{label} Attachment-Count must be an exact non-negative integer"
            )
            declared_count = None
        else:
            declared_count = int(match.group(1))

    heading_numbers: list[int] = []
    for line_number, line in enumerate(lines, start=1):
        if not line.startswith("## ATTACHMENT"):
            continue
        match = re.fullmatch(r"## ATTACHMENT ([1-9][0-9]*)", line)
        if match is None:
            errors.append(
                f"{label}:{line_number} attachment heading must have exact form "
                "'## ATTACHMENT <positive integer>'"
            )
            continue
        heading_numbers.append(int(match.group(1)))

    duplicates = sorted(
        number for number, count in Counter(heading_numbers).items() if count > 1
    )
    if duplicates:
        errors.append(
            f"{label} contains duplicate attachment headings: "
            + ", ".join(str(number) for number in duplicates)
        )
    if declared_count is not None:
        expected_numbers = list(range(1, declared_count + 1))
        if heading_numbers != expected_numbers:
            errors.append(
                f"{label} attachment headings must be exactly contiguous from 1 "
                f"through {declared_count} in order"
            )
    return declared_count


def _validate_package(
    root: Path, location: PackageLocation, errors: list[str]
) -> ParsedPackage | None:
    package_relative = location.path.relative_to(root).as_posix()
    entries = _directory_entries(location.path, package_relative, errors)
    actual_names = {entry.name for entry in entries}
    missing = sorted(REQUIRED_PACKAGE_FILES - actual_names)
    extra = sorted(actual_names - REQUIRED_PACKAGE_FILES)
    if missing:
        errors.append(f"{package_relative} is missing required files: {', '.join(missing)}")
    if extra:
        errors.append(f"{package_relative} has unexpected files: {', '.join(extra)}")

    contents: dict[str, tuple[bytes, str]] = {}
    for filename in sorted(REQUIRED_PACKAGE_FILES):
        path = location.path / filename
        if not path.exists() and not path.is_symlink():
            continue
        loaded = _read_utf8_file(
            path,
            f"{package_relative}/{filename}",
            errors,
            allow_empty=filename == "NORMALIZED_ITEMS.ndjson",
        )
        if loaded is not None:
            contents[filename] = loaded

    manifest_content = contents.get("SOURCE_MANIFEST.json")
    if manifest_content is None:
        return None
    manifest = _parse_json_object(
        manifest_content[1], f"{package_relative}/SOURCE_MANIFEST.json", errors
    )
    if manifest is None:
        return None
    files_by_path, counts = _validate_manifest(manifest, location, errors)

    for filename in sorted(MANIFESTED_PACKAGE_FILES):
        file_entry = files_by_path.get(filename)
        content = contents.get(filename)
        if file_entry is None or content is None:
            continue
        raw = content[0]
        if file_entry.get("bytes") != len(raw):
            errors.append(f"{package_relative}/{filename} byte count does not match manifest")
        actual_hash = hashlib.sha256(raw).hexdigest()
        if file_entry.get("sha256") != actual_hash:
            errors.append(f"{package_relative}/{filename} SHA-256 does not match manifest")
        if (
            filename == "NORMALIZED_ITEMS.ndjson"
            and manifest.get("content_fingerprint_sha256") != actual_hash
        ):
            errors.append(
                f"{package_relative} content fingerprint does not match "
                "NORMALIZED_ITEMS.ndjson"
            )

    ndjson_content = contents.get("NORMALIZED_ITEMS.ndjson")
    items = (
        _parse_ndjson(
            ndjson_content[1],
            f"{package_relative}/NORMALIZED_ITEMS.ndjson",
            manifest,
            errors,
        )
        if ndjson_content is not None
        else []
    )
    if counts.get("items") != len(items):
        errors.append(f"{package_relative} item count does not match manifest")
    conflict_count = sum(item.get("status") == "CONFLICT" for item in items)
    if counts.get("conflicts") != conflict_count:
        errors.append(f"{package_relative} conflict count does not match manifest")
    attachment_content = contents.get("ATTACHMENT_INDEX.md")
    attachment_count = (
        _validate_attachment_index(
            attachment_content[1],
            f"{package_relative}/ATTACHMENT_INDEX.md",
            errors,
        )
        if attachment_content is not None
        else None
    )
    if attachment_count is not None and counts.get("attachments") != attachment_count:
        errors.append(f"{package_relative} attachment count does not match manifest")
    return ParsedPackage(location, manifest, items)


def _validate_global_items(packages: list[ParsedPackage], errors: list[str]) -> None:
    ids: dict[str, tuple[str, dict[str, Any]]] = {}
    fingerprints: list[str] = []
    for package in packages:
        fingerprint = package.manifest.get("content_fingerprint_sha256")
        if isinstance(fingerprint, str) and SHA256_PATTERN.fullmatch(fingerprint):
            fingerprints.append(fingerprint)
        for item in package.items:
            item_id = item.get("id")
            if not isinstance(item_id, str) or ITEM_ID_PATTERN.fullmatch(item_id) is None:
                continue
            prior = ids.get(item_id)
            if prior is not None:
                errors.append(
                    f"Duplicate knowledge item id {item_id!r} in "
                    f"{prior[0]} and {package.location.path}"
                )
            else:
                ids[item_id] = (str(package.location.path), item)
    if any(count > 1 for count in Counter(fingerprints).values()):
        errors.append("Knowledge packages contain duplicate content fingerprints")

    known_ids = set(ids)
    for item_id, (package_path, item) in ids.items():
        relation_membership: dict[str, str] = {}
        for field in ("supersedes", "conflicts_with", "related_items"):
            raw_references = item.get(field)
            if not isinstance(raw_references, list):
                continue
            for reference in raw_references:
                if not isinstance(reference, str):
                    continue
                if reference == item_id:
                    errors.append(
                        f"Knowledge item {item_id!r} in {package_path} has a self-reference"
                    )
                elif reference not in known_ids:
                    errors.append(
                        f"Knowledge item {item_id!r} in {package_path} references unknown "
                        f"item {reference!r} via {field}"
                    )
                prior_field = relation_membership.get(reference)
                if prior_field is not None and prior_field != field:
                    errors.append(
                        f"Knowledge item {item_id!r} relates to {reference!r} through both "
                        f"{prior_field} and {field}"
                    )
                else:
                    relation_membership[reference] = field


def _validate_registry_package_parity(
    registry: dict[str, Any] | None,
    sources_by_id: dict[str, dict[str, Any]],
    packages: list[ParsedPackage],
    errors: list[str],
) -> None:
    package_by_id = {package.location.source_id: package for package in packages}
    for source_id, source in sources_by_id.items():
        package = package_by_id.get(source_id)
        if package is None:
            errors.append(f"Registry source {source_id!r} has no source package")
            continue
        manifest = package.manifest
        for field in ("provider", "source_id", "conversation_id", "content_fingerprint_sha256"):
            if source.get(field) != manifest.get(field):
                errors.append(f"Registry source {source_id!r} {field} does not match manifest")

    if registry is not None and registry.get("state") == "ready":
        # Every package must be accounted for when the integrator claims readiness.
        unregistered = sorted(set(package_by_id) - set(sources_by_id))
        if unregistered:
            errors.append(
                "Ready registry omits source packages: " + ", ".join(unregistered)
            )
        if not any(package.items for package in packages):
            errors.append("Ready registry requires at least one normalized item overall")
        for source_id, source in sources_by_id.items():
            if source.get("status") != "integrated":
                continue
            package = package_by_id.get(source_id)
            if package is not None and not package.items:
                errors.append(
                    f"Ready integrated source {source_id!r} must contain at least one "
                    "normalized item"
                )
        raw_expected = registry.get("expected_sources")
        if isinstance(raw_expected, list):
            for index, expected in enumerate(raw_expected):
                if not isinstance(expected, dict) or expected.get("required") is not True:
                    continue
                source_id = expected.get("source_id")
                if not isinstance(source_id, str):
                    continue
                package = package_by_id.get(source_id)
                if package is None:
                    continue
                manifest = package.manifest
                label = f"Ready registry required expected_sources[{index}]"
                if manifest.get("status") != "complete":
                    errors.append(f"{label} manifest status must be 'complete'")
                if manifest.get("coverage") != "complete":
                    errors.append(f"{label} manifest coverage must be 'complete'")
                if manifest.get("inaccessible") != []:
                    errors.append(f"{label} manifest inaccessible must be empty")


GitHeadResolver = Callable[[Path], tuple[str | None, str | None]]


def _resolve_origin_main_head(root: Path) -> tuple[str | None, str | None]:
    try:
        completed = subprocess.run(
            [
                "git",
                "-C",
                str(root),
                "rev-parse",
                "--verify",
                "refs/remotes/origin/main",
            ],
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=10,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        return None, f"cannot execute git: {exc}"
    if completed.returncode != 0:
        detail = completed.stderr.strip() or "git rev-parse returned nonzero"
        return None, detail
    value = completed.stdout.strip()
    if BASE_MAIN_SHA_PATTERN.fullmatch(value) is None:
        return None, "origin/main did not resolve to one 40-character commit SHA"
    return value, None


def validate_repository_with_statistics(
    repository_root: Path | str,
    *,
    verify_git_head: bool = False,
    git_head_resolver: GitHeadResolver | None = None,
) -> tuple[list[str], ValidationStatistics]:
    errors: list[str] = []
    root = _validate_repo_root(Path(repository_root), errors)
    if root is None:
        return _safe_errors(errors), ValidationStatistics(None, 0, 0)

    registry, sources_by_id = _validate_registry(root, errors)
    locations = _discover_packages(root, errors)
    _validate_consolidated_outputs(root, errors)
    packages: list[ParsedPackage] = []
    for location in locations:
        if location.run_id != RUN_ID:
            errors.append(
                f"Source package run_id directory must be {RUN_ID!r}: {location.path}"
            )
        package = _validate_package(root, location, errors)
        if package is not None:
            packages.append(package)

    source_ids = [package.location.source_id for package in packages]
    duplicate_source_ids = sorted(
        source_id for source_id, count in Counter(source_ids).items() if count > 1
    )
    for source_id in duplicate_source_ids:
        errors.append(f"Duplicate source package source_id {source_id!r}")

    _validate_global_items(packages, errors)
    _validate_registry_package_parity(registry, sources_by_id, packages, errors)
    state = registry.get("state") if isinstance(registry, dict) else None
    consolidated_base_main_sha: str | None = None
    if state == "ready":
        consolidated_base_main_sha = _validate_ready_consolidated_outputs(
            root, sources_by_id, packages, errors
        )
        if verify_git_head:
            if consolidated_base_main_sha is None:
                errors.append(
                    "Cannot verify git head without one valid consolidated Base-Main-SHA"
                )
            else:
                resolver = git_head_resolver or _resolve_origin_main_head
                git_head, git_error = resolver(root)
                if git_error is not None or git_head is None:
                    errors.append(
                        "Cannot resolve live refs/remotes/origin/main for git-head "
                        f"verification: {git_error or 'unknown error'}"
                    )
                elif git_head != consolidated_base_main_sha:
                    errors.append(
                        "Consolidated Base-Main-SHA does not match live "
                        "refs/remotes/origin/main"
                    )
    return _safe_errors(errors), ValidationStatistics(
        state if isinstance(state, str) else None,
        len(packages),
        sum(len(package.items) for package in packages),
    )


def validate_repository(
    repository_root: Path | str,
    *,
    verify_git_head: bool = False,
    git_head_resolver: GitHeadResolver | None = None,
) -> list[str]:
    errors, _ = validate_repository_with_statistics(
        repository_root,
        verify_git_head=verify_git_head,
        git_head_resolver=git_head_resolver,
    )
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="Repository root (defaults to the parent of tools/).",
    )
    parser.add_argument(
        "--verify-git-head",
        action="store_true",
        help=(
            "When registry state is ready, require consolidated Base-Main-SHA to "
            "match refs/remotes/origin/main."
        ),
    )
    args = parser.parse_args(argv)
    errors, statistics = validate_repository_with_statistics(
        args.root, verify_git_head=args.verify_git_head
    )
    if errors:
        print("Knowledge package validation FAILED", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print("Knowledge package validation PASSED")
    print(f"- registry state: {statistics.registry_state}")
    print(f"- source packages: {statistics.package_count}")
    print(f"- normalized items: {statistics.item_count}")
    print("- package fields, paths, hashes and references: verified")
    print("- automated credential/PII pattern checks: passed (not a full privacy audit)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
