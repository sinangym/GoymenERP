#!/usr/bin/env python3
"""Fail closed when the delivery backlog drifts from canonical repository truth."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Any, NamedTuple


BACKLOG_PATH = Path("docs/backlog/DELIVERY_BACKLOG_v1.json")
TASK_MANIFEST_PATH = Path("docs/backlog/task_manifest.json")
DECISION_REGISTRY_PATH = Path("docs/decisions/KARAR_SICILI.md")
DECISION_REGISTRY_CSV_PATH = Path("docs/decisions/KARAR_SICILI.csv")
OPEN_DECISIONS_PATH = Path("docs/decisions/ACIK_KARARLAR.md")
ACCEPTANCE_PATH = Path("docs/acceptance/ACCEPTANCE_70.md")
SCREEN_CATALOG_PATH = Path("docs/ui/CANONICAL_SCREEN_STATUS.csv")

DECISION_PATTERN = re.compile(r"\bDEC-[A-Z]+-[0-9]{3}\b")
OPEN_PATTERN = re.compile(r"\bOPEN-[0-9]{3}\b")
ACCEPTANCE_PATTERN = re.compile(r"\bAT-[0-9]{3}\b")

EXPECTED_BACKLOG_VERSION = "1.1"
EXPECTED_BACKLOG_STATUS = "ACTIVE_GATED"
EXPECTED_BACKLOG_OWNER = "CHATGPT-BUSINESS"
EXPECTED_BACKLOG_PROTOCOL = "docs/governance/GOYMEN_ERP_OTONOM_TESLIM_PLANI_v2.md"
EXPECTED_DECISION_COUNT = 117
EXPECTED_LOCKED_DECISION_COUNT = 114
EXPECTED_TEMPORARY_DECISION_COUNT = 3
EXPECTED_DECISION_ROWS_SHA256 = (
    "af615a1f2b3d01e8b07c8d63643e52c3a6c452b1eea8e5c4aa4bedea62bac1c9"
)
EXPECTED_DECISION_ORDER_SHA256 = (
    "94c1b852e421288bb110f005bdf5e77a35046c3e9729f8eb6a044440d7da085f"
)
EXPECTED_OPEN_IDS = {
    *(f"OPEN-{number:03d}" for number in range(1, 22)),
    "OPEN-030",
}
# OPEN identifiers are append-only identities, not reusable sequence slots. These
# reservations were established by canonical open-decision rows in PR #10/#11;
# neither a closed/unmerged branch nor later catalog reconciliation releases them.
# Reintroducing even the original subject requires an explicit reviewed promotion
# from this hard-reserved set into EXPECTED_OPEN_IDS and the pinned baseline.
RESERVED_OPEN_ID_METADATA = {
    "OPEN-022": {
        "provenance": "PR #10 / ERP-000F",
        "subject": (
            "Outbox, inbox ve başarısız mesaj kanıtlarının saklama, partition, "
            "arşiv, legal hold ve yetkili imha politikası"
        ),
    },
    "OPEN-023": {
        "provenance": "PR #10 / ERP-000F",
        "subject": (
            "Mesaj sıralama kapsamı, ordering key ve gap/geç/out-of-order "
            "davranışı"
        ),
    },
    "OPEN-024": {
        "provenance": "PR #10 / ERP-000F",
        "subject": (
            "Revocable workload kimliği ile dispatcher/consumer claim, "
            "lease/fencing, timeout, retry/backoff, circuit-breaker, "
            "backpressure, poison/dead-letter, belirsiz gönderim, manuel "
            "replay/re-drive yetki/onay/audit ve alarm politikaları"
        ),
    },
    "OPEN-025": {
        "provenance": "PR #10 / ERP-000F",
        "subject": (
            "İlk pilot producer, server-authoritative cause, event adı/schema v1 "
            "payload ve veri sınıfı, emission/no-change/failure koşulları, "
            "consumer ve business/dış etki sözleşmesi"
        ),
    },
    "OPEN-026": {
        "provenance": "PR #11 / ERP-000G",
        "subject": (
            "Şirketler arası fiziksel blob dedup kapsamı; encryption/key sınırı; "
            "reference ownership, son referans silme, şirket export/restore ve "
            "hash existence-oracle politikası"
        ),
    },
    "OPEN-027": {
        "provenance": "PR #11 / ERP-000G",
        "subject": (
            "Upload MIME/extension/size/archive sınırları; malware scanner, "
            "quarantine, timeout/outage/rescan ve rejected içerik politikası"
        ),
    },
    "OPEN-028": {
        "provenance": "PR #11 / ERP-000G",
        "subject": (
            "Signed access/export/share izinleri; TTL, tek/çok kullanım, revoke, "
            "filename/content-disposition ve hassas veri indirme politikası"
        ),
    },
    "OPEN-029": {
        "provenance": "PR #11 / ERP-000G",
        "subject": (
            "İlk pilot belge türü ve owner modül; document/version/link metadata, "
            "zorunlu sınıflama, permission–ABAC, business/security lifecycle ve "
            "DB–object-store commit/reconciliation sözleşmesi"
        ),
    },
}
EXPECTED_ACCEPTANCE_IDS = {f"AT-{number:03d}" for number in range(1, 71)}
EXPECTED_OPEN_ROWS_SHA256 = "2dba55e20f351209fe65b1b3eab8f16cc72004ce4ba094d6a19c2c16a24ac3d8"
EXPECTED_ACCEPTANCE_ROWS_SHA256 = "47aff75e79336af7a7abe1a82c5ecfbef308941ee5cdda7b7239cdd6d015958f"
EXPECTED_SCREEN_COUNT = 159
ALLOWED_LANES = {"codex", "claude", "chatgpt", "codex+claude"}
ALLOWED_MANIFEST_STATUSES = {"done", "ready", "blocked"}
ALLOWED_BACKLOG_FIELDS = {
    "version",
    "status",
    "owner",
    "source",
    "protocol",
    "notes",
    "lanes",
    "global_decisions",
    "global_acceptance",
    "waves",
}
ALLOWED_WAVE_FIELDS = {"id", "title", "faz", "slices"}
ALLOWED_SLICE_FIELDS = {
    "id",
    "task",
    "manifest_task",
    "manifest_status",
    "manifest_depends_on",
    "manifest_blocked_by",
    "execution_state",
    "protocol_gate",
    "lane",
    "depends_on",
    "scope",
    "decisions",
    "open",
    "resolved_open_history",
    "production_gate",
    "decision_only",
    "acceptance",
    "acceptance_notes",
    "screens",
    "issues",
    "critical_analysis",
}
EXPECTED_LANE_KEYS = {"codex", "claude", "chatgpt"}
MANIFEST_SNAPSHOT_FIELDS = {
    "manifest_task",
    "manifest_status",
    "manifest_depends_on",
    "manifest_blocked_by",
}

# Updated only through a reviewed K3 mapping change. This catches semantically
# wrong moves that a global set-equality check cannot see.
EXPECTED_COVERAGE_MAPPING_SHA256 = (
    "4454ee3f8131cda338722d06a9c5170598e09b8608a764ac598051a8aacd6cd0"
)
EXPECTED_CANONICAL_SCREEN_IDS_SHA256 = (
    "acd2033d4a3a9448e22b2ccd7a1e4d2d5e356f3a298da1309f1c3caa27b14aaf"
)

DECISION_FIELDS = (
    "id",
    "area",
    "decision",
    "status",
    "priority",
    "source",
    "owner",
    "acceptance_evidence",
)
DECISION_CSV_HEADERS = (
    "DecisionId",
    "Alan",
    "Karar",
    "Durum",
    "Oncelik",
    "Kaynak",
    "Sahip",
    "KabulKaniti",
)
DECISION_MARKDOWN_HEADER = (
    "| ID | Alan | Karar | Durum | Öncelik | Kaynak | Sahip | Kabul kanıtı |"
)
DECISION_MARKDOWN_SEPARATOR = "|---|---|---|---|---|---|---|---|"
OPEN_MARKDOWN_HEADER = (
    "| ID | Durum | Kullanıcı tarafından kapanan / kabul edilen bölüm | "
    "Gerçekten açık kalan bölüm | Karar kanıtı | Kod / seed kanıtı |"
)
OPEN_MARKDOWN_SEPARATOR = "|---|---|---|---|---|---|"


class BacklogStatistics(NamedTuple):
    wave_count: int
    slice_count: int
    dependency_count: int
    decision_count: int
    open_count: int
    resolved_open_count: int
    acceptance_count: int
    screen_count: int
    manifest_task_count: int

    @property
    def resolved_open_history_count(self) -> int:
        """Backward-compatible explicit name for resolved OPEN history coverage."""
        return self.resolved_open_count


class DuplicateJsonKeyError(ValueError):
    pass


class NonFiniteJsonNumberError(ValueError):
    pass


class OpenDecisionCatalog(NamedTuple):
    all_ids: set[str]
    active_ids: set[str]
    resolved_ids: set[str]
    statuses: dict[str, str]


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKeyError(f"Duplicate JSON object key: {key!r}")
        result[key] = value
    return result


def _reject_non_finite(value: str) -> None:
    raise NonFiniteJsonNumberError(f"Non-standard JSON numeric constant: {value}")


def _parse_finite_float(value: str) -> float:
    parsed = float(value)
    if not math.isfinite(parsed):
        raise NonFiniteJsonNumberError(f"Non-finite JSON number: {value}")
    return parsed


def _load_json(path: Path, label: str, errors: list[str]) -> dict[str, Any] | None:
    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_non_finite,
            parse_float=_parse_finite_float,
        )
    except FileNotFoundError:
        errors.append(f"Missing {label}: {path}")
        return None
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exc:
        errors.append(f"Invalid {label} {path}: {exc}")
        return None
    if not isinstance(value, dict):
        errors.append(f"{label} root must be a JSON object")
        return None
    return value


def _is_in_inactive_markdown_context(lines: list[str], line_index: int) -> bool:
    fence_char: str | None = None
    fence_length = 0
    opening_fence = re.compile(r"^ {0,3}(`{3,}|~{3,})(.*)$")
    closing_fence = re.compile(r"^ {0,3}(`{3,}|~{3,})[ \t]*$")
    prefix = lines[:line_index]
    # These two governance catalogs intentionally contain no raw HTML. Reject
    # every literal '<' before the canonical table, which fail-closes all
    # CommonMark raw HTML/comment block types without a partial HTML parser.
    if any("<" in line for line in prefix):
        return True
    for line in prefix:
        if fence_char is not None:
            closing_match = closing_fence.fullmatch(line)
            if (
                closing_match is not None
                and closing_match.group(1)[0] == fence_char
                and len(closing_match.group(1)) >= fence_length
            ):
                fence_char = None
                fence_length = 0
            continue
        opening_match = opening_fence.match(line)
        if opening_match is not None:
            run = opening_match.group(1)
            suffix = opening_match.group(2)
            if run[0] != "`" or "`" not in suffix:
                fence_char = run[0]
                fence_length = len(run)
                continue
    return fence_char is not None


def _markdown_table_candidate_id(line: str, pattern: re.Pattern[str]) -> str | None:
    candidate = line.strip()
    if candidate.startswith("|"):
        candidate = candidate[1:].lstrip()
    first_cell, separator, _ = candidate.partition("|")
    if not separator:
        return None
    first_cell = first_cell.strip()
    match = pattern.search(first_cell)
    return match.group(0) if match is not None else None


def _looks_like_markdown_table_row(line: str, expected_columns: int) -> bool:
    return line.count("|") >= expected_columns - 1


def _extract_canonical_markdown_table(
    lines: list[str],
    *,
    header: str,
    separator: str,
    pattern: re.Pattern[str],
    expected_columns: int,
    label: str,
    errors: list[str],
) -> tuple[list[list[str]], list[str]]:
    raw_html_lines = [index + 1 for index, line in enumerate(lines) if "<" in line]
    if raw_html_lines:
        errors.append(
            f"{label.capitalize()} raw HTML/comment markup is not allowed at lines "
            + ", ".join(str(line) for line in raw_html_lines)
        )
    header_indices = [index for index, line in enumerate(lines) if line == header]
    if len(header_indices) != 1:
        errors.append(
            f"{label.capitalize()} must contain exactly one canonical Markdown header; "
            f"found {len(header_indices)}"
        )
        return [], []
    header_index = header_indices[0]
    if _is_in_inactive_markdown_context(lines, header_index):
        errors.append(
            f"{label.capitalize()} canonical Markdown table is in an inactive "
            "fence/comment/raw-HTML context"
        )
    if header_index > 0 and lines[header_index - 1].strip():
        errors.append(
            f"{label.capitalize()} canonical Markdown header must be preceded "
            "by a blank line to terminate blockquote/list context"
        )
    if header_index + 1 >= len(lines) or lines[header_index + 1] != separator:
        errors.append(f"{label.capitalize()} canonical Markdown separator drift")

    rows: list[list[str]] = []
    raw_rows: list[str] = []
    row_indices: set[int] = set()
    row_index = header_index + 2
    while row_index < len(lines) and lines[row_index].strip():
        line = lines[row_index]
        if not line.startswith("|"):
            errors.append(
                f"{label.capitalize()} contains a non-canonical row at line {row_index + 1}"
            )
        else:
            cells = [cell.strip() for cell in line.split("|")[1:-1]]
            if not cells or pattern.fullmatch(cells[0]) is None:
                errors.append(
                    f"{label.capitalize()} contains a non-canonical row at "
                    f"line {row_index + 1}"
                )
            else:
                row_indices.add(row_index)
                raw_rows.append(line.strip())
                if len(cells) != expected_columns:
                    errors.append(
                        f"{label.capitalize()} row {cells[0]!r} must contain exactly "
                        f"{expected_columns} columns, got {len(cells)}"
                    )
                else:
                    rows.append(cells)
        row_index += 1
    if not raw_rows:
        errors.append(f"{label.capitalize()} canonical Markdown table has no data rows")

    matching_outside_table = []
    canonical_indices = row_indices | {header_index, header_index + 1}
    for index, line in enumerate(lines):
        if index in canonical_indices:
            continue
        if (
            _markdown_table_candidate_id(line, pattern) is not None
            or _looks_like_markdown_table_row(line, expected_columns)
        ):
            matching_outside_table.append(index + 1)
    if matching_outside_table:
        errors.append(
            f"{label.capitalize()} has table-shaped or matching rows outside the "
            "canonical table at lines "
            + ", ".join(str(line) for line in matching_outside_table)
        )
    return rows, raw_rows


def _read_markdown_decision_records(
    path: Path, errors: list[str]
) -> tuple[list[dict[str, str]], list[str]]:
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exc:
        errors.append(f"Cannot read decision registry {path}: {exc}")
        return [], []
    rows, decision_rows = _extract_canonical_markdown_table(
        lines,
        header=DECISION_MARKDOWN_HEADER,
        separator=DECISION_MARKDOWN_SEPARATOR,
        pattern=DECISION_PATTERN,
        expected_columns=len(DECISION_FIELDS),
        label="decision registry",
        errors=errors,
    )
    records = [dict(zip(DECISION_FIELDS, cells, strict=True)) for cells in rows]
    return records, decision_rows


def _read_csv_decision_records(path: Path, errors: list[str]) -> list[dict[str, str]]:
    try:
        with path.open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            if tuple(reader.fieldnames or ()) != DECISION_CSV_HEADERS:
                errors.append(
                    "Decision registry CSV headers must exactly equal "
                    f"{DECISION_CSV_HEADERS!r}, got {tuple(reader.fieldnames or ())!r}: {path}"
                )
                return []
            records: list[dict[str, str]] = []
            for row_number, row in enumerate(reader, start=2):
                if None in row:
                    errors.append(f"Decision registry CSV row {row_number} has extra columns")
                    continue
                values = [(row.get(header) or "").strip() for header in DECISION_CSV_HEADERS]
                if not any(values):
                    continue
                if DECISION_PATTERN.fullmatch(values[0]) is None:
                    errors.append(
                        f"Decision registry CSV row {row_number} has invalid id {values[0]!r}"
                    )
                    continue
                records.append(dict(zip(DECISION_FIELDS, values, strict=True)))
            return records
    except (OSError, UnicodeError, csv.Error) as exc:
        errors.append(f"Cannot read decision registry CSV {path}: {exc}")
        return []


def _read_decision_ids(path: Path, csv_path: Path, errors: list[str]) -> set[str]:
    records, decision_rows = _read_markdown_decision_records(path, errors)
    ids = [record["id"] for record in records]
    statuses = Counter(record["status"] for record in records)
    _report_duplicate_ids(ids, "decision registry", errors)
    if len(ids) != EXPECTED_DECISION_COUNT:
        errors.append(
            f"Decision registry baseline drift: expected {EXPECTED_DECISION_COUNT}, got {len(ids)}"
        )
    if statuses["KILITLI"] != EXPECTED_LOCKED_DECISION_COUNT:
        errors.append(
            "Decision registry locked baseline drift: "
            f"expected {EXPECTED_LOCKED_DECISION_COUNT}, got {statuses['KILITLI']}"
        )
    if statuses["GECICI_VARSAYIM"] != EXPECTED_TEMPORARY_DECISION_COUNT:
        errors.append(
            "Decision registry temporary baseline drift: "
            f"expected {EXPECTED_TEMPORARY_DECISION_COUNT}, got {statuses['GECICI_VARSAYIM']}"
        )
    unexpected_statuses = sorted(
        status for status in statuses if status not in {"KILITLI", "GECICI_VARSAYIM"}
    )
    if unexpected_statuses:
        errors.append(f"Decision registry has unsupported statuses: {', '.join(unexpected_statuses)}")
    rows_digest = hashlib.sha256(
        "\n".join(sorted(decision_rows)).encode("utf-8")
    ).hexdigest()
    if rows_digest != EXPECTED_DECISION_ROWS_SHA256:
        errors.append(
            "Decision registry row baseline hash drift: "
            f"expected {EXPECTED_DECISION_ROWS_SHA256}, got {rows_digest}"
        )

    order_digest = hashlib.sha256("\n".join(ids).encode("utf-8")).hexdigest()
    if order_digest != EXPECTED_DECISION_ORDER_SHA256:
        errors.append(
            "Decision registry order baseline hash drift: "
            f"expected {EXPECTED_DECISION_ORDER_SHA256}, got {order_digest}"
        )

    csv_records = _read_csv_decision_records(csv_path, errors)
    csv_ids = [record["id"] for record in csv_records]
    _report_duplicate_ids(csv_ids, "decision registry CSV", errors)
    markdown_id_set = set(ids)
    csv_id_set = set(csv_ids)
    missing_in_csv = sorted(markdown_id_set - csv_id_set)
    extra_in_csv = sorted(csv_id_set - markdown_id_set)
    if missing_in_csv or extra_in_csv:
        errors.append(
            "Decision registry Markdown/CSV id drift: "
            f"missing_in_csv={missing_in_csv}, extra_in_csv={extra_in_csv}"
        )
    if ids != csv_ids:
        errors.append("Decision registry Markdown/CSV row order drift")

    markdown_by_id: dict[str, dict[str, str]] = {}
    csv_by_id: dict[str, dict[str, str]] = {}
    for record in records:
        markdown_by_id.setdefault(record["id"], record)
    for record in csv_records:
        csv_by_id.setdefault(record["id"], record)
    for decision_id in sorted(markdown_id_set & csv_id_set):
        mismatched_fields = [
            field
            for field in DECISION_FIELDS[1:]
            if markdown_by_id[decision_id][field] != csv_by_id[decision_id][field]
        ]
        if mismatched_fields:
            errors.append(
                f"Decision registry Markdown/CSV semantic drift for {decision_id!r}: "
                + ", ".join(mismatched_fields)
            )
    return set(ids)


def _read_open_ids(path: Path, errors: list[str]) -> OpenDecisionCatalog:
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exc:
        errors.append(f"Cannot read open decision catalog {path}: {exc}")
        return OpenDecisionCatalog(set(), set(), set(), {})
    rows, raw_rows = _extract_canonical_markdown_table(
        lines,
        header=OPEN_MARKDOWN_HEADER,
        separator=OPEN_MARKDOWN_SEPARATOR,
        pattern=OPEN_PATTERN,
        expected_columns=6,
        label="open decision catalog",
        errors=errors,
    )
    ids = [cells[0] for cells in rows]
    _report_duplicate_ids(ids, "open decision catalog", errors)
    allowed_statuses = {"AÇIK", "KISMEN", "KAPANDI"}
    statuses: dict[str, str] = {}
    for cells in rows:
        open_id, raw_status = cells[0], cells[1]
        status = (
            raw_status[2:-2]
            if raw_status.startswith("**") and raw_status.endswith("**")
            else ""
        )
        if status not in allowed_statuses or raw_status != f"**{status}**":
            errors.append(
                f"Open decision catalog {open_id!r} has unsupported status "
                f"{raw_status!r}; expected exactly **AÇIK**, **KISMEN**, or **KAPANDI**"
            )
            continue
        statuses.setdefault(open_id, status)
    rows_digest = hashlib.sha256(
        "\n".join(sorted(raw_rows)).encode("utf-8")
    ).hexdigest()
    if rows_digest != EXPECTED_OPEN_ROWS_SHA256:
        errors.append(
            "Open decision catalog row baseline hash drift: "
            f"expected {EXPECTED_OPEN_ROWS_SHA256}, got {rows_digest}"
        )
    result = set(ids)
    for reserved_id in sorted(result & RESERVED_OPEN_ID_METADATA.keys()):
        reservation = RESERVED_OPEN_ID_METADATA[reserved_id]
        errors.append(
            "Open decision catalog stable id reserved/cannot be reassigned: "
            f"{reserved_id} is reserved by {reservation['provenance']} for "
            f"{reservation['subject']}"
        )
    if result != EXPECTED_OPEN_IDS:
        errors.append(
            "Open decision baseline drift: expected OPEN-001..OPEN-021 plus "
            "OPEN-030, got "
            + ", ".join(sorted(result))
        )
    active_ids = {
        open_id for open_id, status in statuses.items() if status in {"AÇIK", "KISMEN"}
    }
    resolved_ids = {
        open_id for open_id, status in statuses.items() if status == "KAPANDI"
    }
    return OpenDecisionCatalog(result, active_ids, resolved_ids, statuses)


def _read_acceptance_ids(path: Path, errors: list[str]) -> set[str]:
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exc:
        errors.append(f"Cannot read acceptance catalog {path}: {exc}")
        return set()
    raw_html_lines = [index + 1 for index, line in enumerate(lines) if "<" in line]
    if raw_html_lines:
        errors.append(
            "Acceptance catalog raw HTML/comment markup is not allowed at lines "
            + ", ".join(str(line) for line in raw_html_lines)
        )
    ids: list[str] = []
    acceptance_rows: list[str] = []
    row_pattern = re.compile(r"^- \*\*(AT-[0-9]{3}):\*\*")
    for line_index, line in enumerate(lines):
        match = row_pattern.match(line)
        if match:
            if _is_in_inactive_markdown_context(lines, line_index):
                errors.append(
                    f"Acceptance catalog canonical row {match.group(1)!r} at line "
                    f"{line_index + 1} is in an inactive fence/comment/raw-HTML "
                    "context"
                )
            ids.append(match.group(1))
            acceptance_rows.append(line.strip())
    _report_duplicate_ids(ids, "acceptance catalog", errors)
    result = set(ids)
    if result != EXPECTED_ACCEPTANCE_IDS:
        errors.append(
            "Acceptance baseline drift: expected AT-001..AT-070, got "
            + ", ".join(sorted(result))
        )
    rows_digest = hashlib.sha256(
        "\n".join(sorted(acceptance_rows)).encode("utf-8")
    ).hexdigest()
    if rows_digest != EXPECTED_ACCEPTANCE_ROWS_SHA256:
        errors.append(
            "Acceptance catalog row baseline hash drift: "
            f"expected {EXPECTED_ACCEPTANCE_ROWS_SHA256}, got {rows_digest}"
        )
    return result


def _report_duplicate_ids(ids: list[str], label: str, errors: list[str]) -> None:
    for item, count in sorted(Counter(ids).items()):
        if count > 1:
            errors.append(f"Duplicate {label} id: {item!r}")


def _read_screen_ids(path: Path, errors: list[str]) -> set[str]:
    try:
        # Excel and some Windows CSV exporters preserve an UTF-8 BOM. Treat it
        # as encoding metadata instead of silently changing the first header.
        with path.open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            if reader.fieldnames is None or "canonical_id" not in reader.fieldnames:
                errors.append(f"Screen catalog must contain canonical_id column: {path}")
                return set()
            ids = [
                row["canonical_id"].strip()
                for row in reader
                if row.get("canonical_id") and row["canonical_id"].strip()
            ]
            _report_duplicate_ids(ids, "screen catalog", errors)
            result = set(ids)
            if len(result) != EXPECTED_SCREEN_COUNT:
                errors.append(
                    f"Screen catalog baseline drift: expected {EXPECTED_SCREEN_COUNT}, got {len(result)}"
                )
            digest = hashlib.sha256("\n".join(sorted(result)).encode("utf-8")).hexdigest()
            if digest != EXPECTED_CANONICAL_SCREEN_IDS_SHA256:
                errors.append(
                    "Canonical screen id baseline hash drift: "
                    f"expected {EXPECTED_CANONICAL_SCREEN_IDS_SHA256}, got {digest}"
                )
            return result
    except (OSError, UnicodeError, csv.Error) as exc:
        errors.append(f"Cannot read screen catalog {path}: {exc}")
        return set()


def _string_list(
    owner: dict[str, Any], field: str, label: str, errors: list[str], *, required: bool = False
) -> list[str]:
    value = owner.get(field)
    if value is None and not required:
        return []
    if not isinstance(value, list):
        errors.append(f"{label} field {field!r} must be a JSON array")
        return []
    result: list[str] = []
    for index, item in enumerate(value):
        if not isinstance(item, str) or not item or item != item.strip():
            errors.append(
                f"{label} field {field!r} item {index} must be a non-empty trimmed string"
            )
        else:
            result.append(item)
    duplicates = sorted(item for item, count in Counter(result).items() if count > 1)
    for item in duplicates:
        errors.append(f"{label} field {field!r} contains duplicate {item!r}")
    return result


def _reject_unknown_fields(
    owner: dict[str, Any], allowed: set[str], label: str, errors: list[str]
) -> None:
    unknown = sorted(set(owner) - allowed)
    if unknown:
        errors.append(f"{label} has unknown fields: {', '.join(unknown)}")


def _find_cycles(graph: dict[str, list[str]]) -> list[str]:
    state = {node: 0 for node in graph}
    path: list[str] = []
    positions: dict[str, int] = {}
    cycles: set[tuple[str, ...]] = set()
    for root in sorted(graph):
        if state[root] != 0:
            continue
        state[root] = 1
        positions[root] = 0
        path.append(root)
        frames: list[tuple[str, int]] = [(root, 0)]
        while frames:
            node, index = frames[-1]
            dependencies = graph[node]
            if index >= len(dependencies):
                frames.pop()
                finished = path.pop()
                positions.pop(finished)
                state[finished] = 2
                continue
            dependency = dependencies[index]
            frames[-1] = (node, index + 1)
            if dependency not in graph:
                continue
            if state[dependency] == 0:
                state[dependency] = 1
                positions[dependency] = len(path)
                path.append(dependency)
                frames.append((dependency, 0))
            elif state[dependency] == 1:
                ring = path[positions[dependency] :] + [dependency]
                body = ring[:-1]
                minimum = min(range(len(body)), key=lambda i: body[i])
                canonical = tuple(body[minimum:] + body[:minimum])
                cycles.add(canonical + (canonical[0],))
    return [" -> ".join(cycle) for cycle in sorted(cycles)]


def validate_repository_with_statistics(
    root: Path,
) -> tuple[list[str], BacklogStatistics | None]:
    errors: list[str] = []
    backlog = _load_json(root / BACKLOG_PATH, "delivery backlog", errors)
    manifest = _load_json(root / TASK_MANIFEST_PATH, "task manifest", errors)
    if backlog is None or manifest is None:
        return errors, None
    _reject_unknown_fields(backlog, ALLOWED_BACKLOG_FIELDS, "delivery backlog", errors)

    source = backlog.get("source")
    if not isinstance(source, str) or not source or source != source.strip():
        errors.append("Delivery backlog field 'source' must be a non-empty trimmed string")
    notes = _string_list(backlog, "notes", "delivery backlog", errors, required=True)
    lanes = backlog.get("lanes")
    if not isinstance(lanes, dict):
        errors.append("Delivery backlog field 'lanes' must be a JSON object")
        lanes = {}
    else:
        missing_lanes = sorted(EXPECTED_LANE_KEYS - set(lanes))
        extra_lanes = sorted(set(lanes) - EXPECTED_LANE_KEYS)
        if missing_lanes or extra_lanes:
            errors.append(
                "Delivery backlog lanes must contain exactly codex/claude/chatgpt; "
                f"missing={missing_lanes}, extra={extra_lanes}"
            )
        for lane_key, description in lanes.items():
            if (
                not isinstance(description, str)
                or not description
                or description != description.strip()
            ):
                errors.append(
                    f"Delivery backlog lane {lane_key!r} must have a non-empty trimmed description"
                )

    canonical_decisions = _read_decision_ids(
        root / DECISION_REGISTRY_PATH,
        root / DECISION_REGISTRY_CSV_PATH,
        errors,
    )
    open_catalog = _read_open_ids(root / OPEN_DECISIONS_PATH, errors)
    canonical_open = open_catalog.active_ids
    canonical_resolved_open = open_catalog.resolved_ids
    canonical_acceptance = _read_acceptance_ids(root / ACCEPTANCE_PATH, errors)
    canonical_screens = _read_screen_ids(root / SCREEN_CATALOG_PATH, errors)

    for field, expected in (
        ("version", EXPECTED_BACKLOG_VERSION),
        ("status", EXPECTED_BACKLOG_STATUS),
        ("owner", EXPECTED_BACKLOG_OWNER),
        ("protocol", EXPECTED_BACKLOG_PROTOCOL),
    ):
        if backlog.get(field) != expected:
            errors.append(
                f"Delivery backlog field {field!r} must equal {expected!r}, "
                f"got {backlog.get(field)!r}"
            )

    tasks = manifest.get("tasks")
    if not isinstance(tasks, list):
        errors.append("Task manifest field 'tasks' must be a JSON array")
        return errors, None
    manifest_ids: list[str] = []
    manifest_tasks: dict[str, dict[str, Any]] = {}
    for index, item in enumerate(tasks):
        if not isinstance(item, dict):
            errors.append(f"Task manifest task {index} must be a JSON object")
            continue
        task_id = item.get("id")
        if not isinstance(task_id, str) or not task_id or task_id != task_id.strip():
            errors.append(f"Task manifest task {index} has invalid id")
            continue
        manifest_ids.append(task_id)
        manifest_tasks.setdefault(task_id, item)
        status = item.get("status")
        if status not in ALLOWED_MANIFEST_STATUSES:
            errors.append(f"Task manifest task {task_id!r} has unsupported status {status!r}")
        _string_list(item, "depends_on", f"manifest task {task_id!r}", errors, required=True)
        _string_list(item, "blocked_by", f"manifest task {task_id!r}", errors)
    _report_duplicate_ids(manifest_ids, "task manifest", errors)

    global_decisions = _string_list(
        backlog, "global_decisions", "delivery backlog", errors, required=True
    )
    global_acceptance = _string_list(
        backlog, "global_acceptance", "delivery backlog", errors, required=True
    )
    waves = backlog.get("waves")
    if not isinstance(waves, list) or not waves:
        errors.append("Delivery backlog field 'waves' must be a non-empty JSON array")
        return errors, None

    slice_ids: list[str] = []
    graph: dict[str, list[str]] = {}
    used_decisions = set(global_decisions)
    used_open: set[str] = set()
    used_resolved_open: set[str] = set()
    used_acceptance: set[str] = set(global_acceptance)
    used_screens: set[str] = set()
    manifest_slice_count = 0
    mapped_manifest_tasks: list[str] = []
    wave_ids: list[str] = []
    coverage_mapping: dict[str, Any] = {
        "source": source,
        "notes": notes,
        "lanes": lanes,
        "global_decisions": sorted(global_decisions),
        "global_acceptance": sorted(global_acceptance),
        "wave_order": [],
        "waves": {},
        "slice_order": {},
        "slices": {},
    }

    for wave_index, wave in enumerate(waves):
        if not isinstance(wave, dict):
            errors.append(f"Wave at index {wave_index} must be a JSON object")
            continue
        _reject_unknown_fields(wave, ALLOWED_WAVE_FIELDS, f"wave at index {wave_index}", errors)
        wave_id = wave.get("id")
        if not isinstance(wave_id, str) or not wave_id or wave_id != wave_id.strip():
            errors.append(
                f"Wave at index {wave_index} field 'id' must be a non-empty trimmed string"
            )
            wave_label = f"wave {wave_index}"
        else:
            wave_ids.append(wave_id)
            wave_label = f"wave {wave_id!r}"
        wave_key = wave_id if isinstance(wave_id, str) else f"@index:{wave_index}"
        coverage_mapping["wave_order"].append(wave_key)
        coverage_mapping["slice_order"][wave_key] = []
        for field in ("title", "faz"):
            value = wave.get(field)
            if not isinstance(value, str) or not value or value != value.strip():
                errors.append(
                    f"{wave_label} field {field!r} must be a non-empty trimmed string"
                )
        coverage_mapping["waves"][wave_key] = {
            "title": wave.get("title"),
            "faz": wave.get("faz"),
        }
        slices = wave.get("slices")
        if not isinstance(slices, list) or not slices:
            errors.append(f"{wave_label} field 'slices' must be a non-empty JSON array")
            continue
        for slice_index, item in enumerate(slices):
            if not isinstance(item, dict):
                errors.append(f"{wave_label} slice {slice_index} must be a JSON object")
                continue
            _reject_unknown_fields(
                item,
                ALLOWED_SLICE_FIELDS,
                f"{wave_label} slice {slice_index}",
                errors,
            )
            slice_id = item.get("id")
            if not isinstance(slice_id, str) or not slice_id or slice_id != slice_id.strip():
                errors.append(
                    f"{wave_label} slice {slice_index} field 'id' must be a non-empty trimmed string"
                )
                continue
            label = f"slice {slice_id!r}"
            slice_ids.append(slice_id)
            coverage_mapping["slice_order"][wave_key].append(slice_id)
            lane = item.get("lane")
            if lane not in ALLOWED_LANES:
                errors.append(
                    f"{label} field 'lane' must be one of {sorted(ALLOWED_LANES)!r}, "
                    f"got {lane!r}"
                )
            scope = item.get("scope")
            if not isinstance(scope, str) or not scope or scope != scope.strip():
                errors.append(f"{label} field 'scope' must be a non-empty trimmed string")
            dependencies = _string_list(item, "depends_on", label, errors, required=True)
            graph.setdefault(slice_id, dependencies)
            decisions = _string_list(item, "decisions", label, errors)
            open_ids = _string_list(item, "open", label, errors)
            resolved_open_history = _string_list(
                item, "resolved_open_history", label, errors
            )
            acceptance = _string_list(item, "acceptance", label, errors, required=True)
            acceptance_notes = _string_list(item, "acceptance_notes", label, errors)
            screens = _string_list(item, "screens", label, errors, required=True)
            issues = _string_list(item, "issues", label, errors)
            critical_analysis = item.get("critical_analysis")
            if critical_analysis is not None and (
                not isinstance(critical_analysis, str)
                or not critical_analysis
                or critical_analysis != critical_analysis.strip()
            ):
                errors.append(
                    f"{label} field 'critical_analysis' must be a non-empty trimmed string"
                )
            used_decisions.update(decisions)
            used_open.update(open_ids)
            used_resolved_open.update(resolved_open_history)
            used_acceptance.update(acceptance)
            used_screens.update(screens)
            coverage_mapping["slices"][slice_id] = {
                "wave_id": wave_id,
                "task": item.get("task"),
                "lane": lane,
                "scope": scope,
                "depends_on": sorted(dependencies),
                "decisions": sorted(decisions),
                "open": sorted(open_ids),
                "resolved_open_history": sorted(resolved_open_history),
                "acceptance": sorted(acceptance),
                "acceptance_notes": sorted(acceptance_notes),
                "screens": sorted(screens),
                "issues": sorted(issues),
                "critical_analysis": critical_analysis,
                "manifest_task": item.get("manifest_task"),
                "manifest_status": item.get("manifest_status"),
                "manifest_depends_on": item.get("manifest_depends_on"),
                "manifest_blocked_by": item.get("manifest_blocked_by"),
                "execution_state": item.get("execution_state"),
                "protocol_gate": item.get("protocol_gate"),
                "production_gate": item.get("production_gate"),
                "decision_only": item.get("decision_only", False),
            }

            if "flag" in item:
                errors.append(f"{label} uses legacy ambiguous 'flag'; use production_gate")
            production_gate = item.get("production_gate")
            decision_only = item.get("decision_only", False)
            if not isinstance(decision_only, bool):
                errors.append(f"{label} field 'decision_only' must be boolean")
                decision_only = False
            if decision_only and (
                slice_id != "W5-DECISIONPACK"
                or lane != "chatgpt"
                or bool(open_ids)
                or item.get("manifest_task") is not None
            ):
                errors.append(
                    f"{label} decision_only is reserved for the non-executable "
                    "W5-DECISIONPACK chatgpt slice"
                )
            if production_gate is not None:
                if not isinstance(production_gate, dict):
                    errors.append(f"{label} field 'production_gate' must be a JSON object")
                else:
                    if set(production_gate) != {"key", "default", "scope"}:
                        errors.append(
                            f"{label} production_gate must contain exactly key/default/scope"
                        )
                    for gate_field in ("key", "scope"):
                        value = production_gate.get(gate_field)
                        if (
                            not isinstance(value, str)
                            or not value
                            or value != value.strip()
                        ):
                            errors.append(
                                f"{label} production_gate field {gate_field!r} "
                                "must be a non-empty trimmed string"
                            )
                    if production_gate.get("default") is not False:
                        errors.append(f"{label} production_gate default must be false")

            for field, values, canonical in (
                ("decisions", decisions, canonical_decisions),
                ("acceptance", acceptance, canonical_acceptance),
                ("screens", screens, canonical_screens),
            ):
                for value in values:
                    if value not in canonical:
                        errors.append(f"{label} references unknown {field} id {value!r}")
            for value in open_ids:
                if value not in canonical_open:
                    status = open_catalog.statuses.get(value)
                    if status is None:
                        errors.append(f"{label} references unknown open id {value!r}")
                    else:
                        errors.append(
                            f"{label} references resolved OPEN id {value!r} "
                            "in active 'open' field"
                        )
            for value in resolved_open_history:
                if value not in canonical_resolved_open:
                    status = open_catalog.statuses.get(value)
                    if status is None:
                        errors.append(
                            f"{label} references unknown resolved_open_history id {value!r}"
                        )
                    else:
                        errors.append(
                            f"{label} references active OPEN id {value!r} "
                            "in 'resolved_open_history' field"
                        )

            task_value = item.get("task")
            if task_value is not None and (
                not isinstance(task_value, str)
                or not task_value
                or task_value != task_value.strip()
            ):
                errors.append(f"{label} field 'task' must be a non-empty trimmed string")
            snapshot_present = any(field in item for field in MANIFEST_SNAPSHOT_FIELDS)
            canonical_task_reference = (
                isinstance(task_value, str) and task_value in manifest_tasks
            )
            if canonical_task_reference or snapshot_present:
                missing_snapshot_fields = sorted(MANIFEST_SNAPSHOT_FIELDS - set(item))
                if missing_snapshot_fields:
                    errors.append(
                        f"{label} incomplete manifest snapshot; missing "
                        + ", ".join(missing_snapshot_fields)
                    )

            manifest_task_id = item.get("manifest_task")
            if manifest_task_id is not None:
                manifest_slice_count += 1
                if not isinstance(manifest_task_id, str) or not manifest_task_id:
                    errors.append(f"{label} field 'manifest_task' must be a non-empty string")
                    continue
                task = manifest_tasks.get(manifest_task_id)
                if task is None:
                    errors.append(f"{label} references unknown manifest task {manifest_task_id!r}")
                    continue
                mapped_manifest_tasks.append(manifest_task_id)
                if item.get("task") != manifest_task_id:
                    errors.append(
                        f"{label} field 'task' must equal manifest_task {manifest_task_id!r}"
                    )
                expected_status = task.get("status")
                if item.get("manifest_status") != expected_status:
                    errors.append(
                        f"{label} manifest_status drift: backlog={item.get('manifest_status')!r}, "
                        f"task_manifest={expected_status!r}"
                    )
                expected_dependencies = task.get("depends_on", [])
                snapshot_dependencies = item.get("manifest_depends_on")
                if snapshot_dependencies != expected_dependencies:
                    errors.append(
                        f"{label} manifest_depends_on drift: backlog={snapshot_dependencies!r}, "
                        f"task_manifest={expected_dependencies!r}"
                    )
                expected_blockers = task.get("blocked_by", [])
                snapshot_blockers = item.get("manifest_blocked_by")
                if snapshot_blockers != expected_blockers:
                    errors.append(
                        f"{label} manifest_blocked_by drift: backlog={snapshot_blockers!r}, "
                        f"task_manifest={expected_blockers!r}"
                    )
                blocker_values = (
                    snapshot_blockers if isinstance(snapshot_blockers, list) else []
                )
                manifest_open_blockers: set[str] = set()
                for blocker in blocker_values:
                    if not isinstance(blocker, str) or not blocker.startswith("OPEN-"):
                        continue
                    if OPEN_PATTERN.fullmatch(blocker) is None or blocker not in open_catalog.all_ids:
                        errors.append(
                            f"{label} manifest_blocked_by references unknown OPEN "
                            f"blocker {blocker!r}"
                        )
                        continue
                    if blocker in canonical_resolved_open:
                        errors.append(
                            f"{label} manifest_blocked_by references resolved OPEN "
                            f"blocker {blocker!r}"
                        )
                        continue
                    manifest_open_blockers.add(blocker)
                    if blocker not in open_ids:
                        errors.append(
                            f"{label} manifest OPEN blocker {blocker!r} is missing "
                            "from active 'open' field"
                        )
                if open_ids and not decision_only and production_gate is None:
                    missing_manifest_open_blockers = sorted(
                        set(open_ids) - manifest_open_blockers
                    )
                    if item.get("manifest_status") != "blocked":
                        errors.append(
                            f"{label} has OPEN-* dependencies but no default-false "
                            "production_gate"
                        )
                    elif missing_manifest_open_blockers:
                        errors.append(
                            f"{label} has active OPEN ids without production_gate or "
                            "matching manifest_blocked_by: "
                            + ", ".join(missing_manifest_open_blockers)
                        )
                if manifest_task_id == "ERP-000F":
                    if item.get("execution_state") != "SPEC_ONLY":
                        errors.append(
                            "slice 'W0-OUTBOX' must remain execution_state 'SPEC_ONLY'"
                        )
                    if (
                        item.get("protocol_gate")
                        != "CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md §11"
                    ):
                        errors.append(
                            "slice 'W0-OUTBOX' must preserve the ERP-000F protocol gate"
                        )
            elif open_ids and not decision_only and production_gate is None:
                errors.append(
                    f"{label} has OPEN-* dependencies but no default-false production_gate"
                )

    duplicates = sorted(item for item, count in Counter(slice_ids).items() if count > 1)
    for item in duplicates:
        errors.append(f"Duplicate slice id: {item!r}")
    _report_duplicate_ids(wave_ids, "wave", errors)
    _report_duplicate_ids(mapped_manifest_tasks, "delivery backlog manifest mapping", errors)
    active_manifest_tasks = {
        task_id
        for task_id, task in manifest_tasks.items()
        if task.get("status") != "done"
    }
    mapped_manifest_set = set(mapped_manifest_tasks)
    missing_manifest_mappings = sorted(active_manifest_tasks - mapped_manifest_set)
    extra_manifest_mappings = sorted(mapped_manifest_set - active_manifest_tasks)
    if missing_manifest_mappings:
        errors.append(
            "Delivery backlog is missing active manifest task mappings: "
            + ", ".join(missing_manifest_mappings)
        )
    if extra_manifest_mappings:
        errors.append(
            "Delivery backlog maps non-active manifest tasks: "
            + ", ".join(extra_manifest_mappings)
        )
    known_slices = set(slice_ids)
    for slice_id, dependencies in graph.items():
        for dependency in dependencies:
            if dependency == slice_id:
                errors.append(f"Slice {slice_id!r} cannot depend on itself")
            elif dependency not in known_slices:
                errors.append(f"Slice {slice_id!r} depends on unknown slice {dependency!r}")
    if not duplicates:
        for cycle in _find_cycles(graph):
            errors.append(f"Delivery backlog cycle detected: {cycle}")

    status_laundering = sorted(used_open & used_resolved_open)
    if status_laundering:
        errors.append(
            "Delivery backlog launders OPEN status across active and resolved history: "
            + ", ".join(status_laundering)
        )

    for label, used, canonical in (
        ("decision", used_decisions, canonical_decisions),
        ("active open decision", used_open, canonical_open),
        ("resolved open history", used_resolved_open, canonical_resolved_open),
        ("acceptance", used_acceptance, canonical_acceptance),
        ("screen", used_screens, canonical_screens),
    ):
        missing = sorted(canonical - used)
        extra = sorted(used - canonical)
        if missing:
            errors.append(f"Delivery backlog is missing {label} ids: {', '.join(missing)}")
        if extra:
            errors.append(f"Delivery backlog has unknown {label} ids: {', '.join(extra)}")

    coverage_digest = hashlib.sha256(
        json.dumps(
            coverage_mapping,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
        ).encode("utf-8")
    ).hexdigest()
    if coverage_digest != EXPECTED_COVERAGE_MAPPING_SHA256:
        errors.append(
            "Delivery backlog coverage mapping hash drift: "
            f"expected {EXPECTED_COVERAGE_MAPPING_SHA256}, got {coverage_digest}"
        )

    if errors:
        return errors, None
    return errors, BacklogStatistics(
        wave_count=len(waves),
        slice_count=len(slice_ids),
        dependency_count=sum(len(items) for items in graph.values()),
        decision_count=len(used_decisions),
        open_count=len(used_open),
        resolved_open_count=len(used_resolved_open),
        acceptance_count=len(used_acceptance),
        screen_count=len(used_screens),
        manifest_task_count=manifest_slice_count,
    )


def validate_repository(root: Path) -> list[str]:
    errors, _ = validate_repository_with_statistics(root)
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="Repository root",
    )
    args = parser.parse_args(argv)
    errors, statistics = validate_repository_with_statistics(args.root.resolve())
    if errors:
        print("Delivery backlog validation FAILED", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    if statistics is None:
        print("Delivery backlog validation FAILED", file=sys.stderr)
        return 1
    print("Delivery backlog validation PASSED")
    print(f"- waves: {statistics.wave_count}")
    print(f"- slices: {statistics.slice_count}")
    print(f"- dependency edges: {statistics.dependency_count}")
    print(f"- decisions covered: {statistics.decision_count}")
    print(f"- open decisions covered: {statistics.open_count}")
    print(f"- resolved open history covered: {statistics.resolved_open_count}")
    print(f"- acceptance ids covered: {statistics.acceptance_count}")
    print(f"- canonical screens covered: {statistics.screen_count}")
    print(f"- manifest-backed slices: {statistics.manifest_task_count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
