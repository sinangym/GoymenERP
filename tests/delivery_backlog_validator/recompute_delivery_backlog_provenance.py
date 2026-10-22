#!/usr/bin/env python3
"""Independently recompute delivery backlog provenance without validator imports."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Any


DECISION_PATTERN = re.compile(r"(?:DEC-[A-Z]+-[0-9]{3}|DELEGE-[0-9]{8}-[1-9][0-9]*)")
OPEN_PATTERN = re.compile(r"OPEN-[0-9]{3}")
DECISION_FIELDS = (
    "id",
    "area",
    "decision",
    "status",
    "priority",
    "source",
    "owner",
    "acceptanceEvidence",
)
CSV_HEADERS = (
    "DecisionId",
    "Alan",
    "Karar",
    "Durum",
    "Oncelik",
    "Kaynak",
    "Sahip",
    "KabulKaniti",
)
DECISION_ROUND_IDS = (
    "DEC-AI-009",
    "DEC-CAR-004",
    "DEC-DATA-001",
    "DEC-DOC-010",
    "DEC-INT-001",
    "DEC-ORG-005",
    "DEC-PERF-001",
    "DEC-SET-001",
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
ACTIVE_OPEN_STATUSES = frozenset({"AÇIK", "KISMEN"})
RESOLVED_OPEN_STATUS = "KAPANDI"
OPEN_STATUS_BY_CELL = {
    "**AÇIK**": "AÇIK",
    "**KISMEN**": "KISMEN",
    "**KAPANDI**": "KAPANDI",
}
REQUIRED_OPEN_IDS = frozenset(
    {
        "OPEN-001",
        "OPEN-002",
        "OPEN-003",
        "OPEN-004",
        "OPEN-005",
        "OPEN-006",
        "OPEN-007",
        "OPEN-008",
        "OPEN-009",
        "OPEN-010",
        "OPEN-011",
        "OPEN-012",
        "OPEN-013",
        "OPEN-014",
        "OPEN-015",
        "OPEN-016",
        "OPEN-017",
        "OPEN-018",
        "OPEN-019",
        "OPEN-020",
        "OPEN-021",
        "OPEN-030",
    }
)
RESERVED_OPEN_IDS = frozenset(
    {
        "OPEN-022",
        "OPEN-023",
        "OPEN-024",
        "OPEN-025",
        "OPEN-026",
        "OPEN-027",
        "OPEN-028",
        "OPEN-029",
    }
)


def _sha256(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def _canonical_row_bytes(rows: list[str]) -> bytes:
    return "\n".join(sorted(rows)).encode("utf-8")


def _duplicates(values: list[str]) -> list[str]:
    return sorted(value for value, count in Counter(values).items() if count > 1)


def _contains_markup(line: str) -> bool:
    # Only the reviewed literal notation is outside the raw-markup prohibition.
    notation = ("`Karar=<tam kalan metin>`", "`EK-38: <Karar>`",
                "`DELEGE-20261021-<No>`", "fark < blok")
    remainder = line
    for token in notation:
        if token[0] == "`":
            remainder = re.sub("(?<!`)" + re.escape(token) + "(?!`)", "", remainder)
        else:
            remainder = remainder.replace(token, "")
    return "<" in remainder


def _is_in_inactive_markdown_context(lines: list[str], line_index: int) -> bool:
    fence_char: str | None = None
    fence_length = 0
    opening_fence = re.compile(r"^ {0,3}(`{3,}|~{3,})(.*)$")
    closing_fence = re.compile(r"^ {0,3}(`{3,}|~{3,})[ \t]*$")
    prefix = lines[:line_index]
    if any(_contains_markup(line) for line in prefix):
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


def _read_active_markdown_table(
    path: Path,
    *,
    header: str,
    separator: str,
    pattern: re.Pattern[str],
    expected_columns: int,
) -> tuple[list[list[str]], list[str]]:
    lines = path.read_text(encoding="utf-8").splitlines()
    raw_html_lines = [index + 1 for index, line in enumerate(lines) if _contains_markup(line)]
    if raw_html_lines:
        raise ValueError(
            f"{path} raw HTML/comment markup is not allowed at lines "
            + ", ".join(str(line) for line in raw_html_lines)
        )
    header_indices = [index for index, line in enumerate(lines) if line == header]
    if len(header_indices) != 1:
        raise ValueError(
            f"{path} must contain exactly one canonical Markdown header; "
            f"found {len(header_indices)}"
        )
    header_index = header_indices[0]
    if _is_in_inactive_markdown_context(lines, header_index):
        raise ValueError(
            f"{path} canonical table is in an inactive fence/comment/raw-HTML context"
        )
    if header_index > 0 and lines[header_index - 1].strip():
        raise ValueError(
            f"{path} canonical Markdown header must be at document root: "
            "the preceding line must be blank to terminate blockquote/list context"
        )
    if header_index + 1 >= len(lines) or lines[header_index + 1] != separator:
        raise ValueError(f"{path} canonical Markdown separator drift")

    rows: list[list[str]] = []
    raw_rows: list[str] = []
    row_indices: set[int] = set()
    index = header_index + 2
    while index < len(lines) and lines[index].strip():
        if not lines[index].startswith("|"):
            raise ValueError(f"{path} has a non-canonical table row at line {index + 1}")
        cells = [cell.strip() for cell in lines[index].split("|")[1:-1]]
        if not cells or pattern.fullmatch(cells[0]) is None:
            raise ValueError(f"{path} has a non-canonical table row at line {index + 1}")
        if len(cells) != expected_columns:
            raise ValueError(
                f"{path} row {cells[0]!r} has {len(cells)} cells; "
                f"expected {expected_columns}"
            )
        row_indices.add(index)
        rows.append(cells)
        raw_rows.append(lines[index].strip())
        index += 1
    canonical_indices = row_indices | {header_index, header_index + 1}
    for outside_index, line in enumerate(lines):
        if outside_index in canonical_indices:
            continue
        if (
            _markdown_table_candidate_id(line, pattern) is not None
            or _looks_like_markdown_table_row(line, expected_columns)
        ):
            raise ValueError(
                f"{path} has a table-shaped or matching row outside the canonical table at "
                f"line {outside_index + 1}"
            )
    return rows, raw_rows


def _read_markdown_decisions(path: Path) -> tuple[list[dict[str, str]], list[str]]:
    rows, raw_rows = _read_active_markdown_table(
        path,
        header=DECISION_MARKDOWN_HEADER,
        separator=DECISION_MARKDOWN_SEPARATOR,
        pattern=DECISION_PATTERN,
        expected_columns=len(DECISION_FIELDS),
    )
    records = [dict(zip(DECISION_FIELDS, cells, strict=True)) for cells in rows]
    return records, raw_rows


def _read_csv_decisions(path: Path) -> list[dict[str, str]]:
    with path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        if tuple(reader.fieldnames or ()) != CSV_HEADERS:
            raise ValueError(
                f"Decision CSV headers are {tuple(reader.fieldnames or ())!r}; "
                f"expected {CSV_HEADERS!r}"
            )
        records: list[dict[str, str]] = []
        for row_number, row in enumerate(reader, start=2):
            if None in row:
                raise ValueError(f"Decision CSV row {row_number} has extra columns")
            values = [(row.get(header) or "").strip() for header in CSV_HEADERS]
            if not any(values):
                continue
            if DECISION_PATTERN.fullmatch(values[0]) is None:
                raise ValueError(f"Decision CSV row {row_number} has invalid id {values[0]!r}")
            records.append(dict(zip(DECISION_FIELDS, values, strict=True)))
        return records


def _read_open_rows(
    path: Path,
) -> tuple[list[str], list[str], Counter[str], set[str], set[str]]:
    rows, raw_rows = _read_active_markdown_table(
        path,
        header=OPEN_MARKDOWN_HEADER,
        separator=OPEN_MARKDOWN_SEPARATOR,
        pattern=OPEN_PATTERN,
        expected_columns=6,
    )
    ids = [cells[0] for cells in rows]
    statuses: Counter[str] = Counter()
    active_ids: set[str] = set()
    resolved_ids: set[str] = set()
    for cells in rows:
        open_id = cells[0]
        status_cell = cells[1]
        status = OPEN_STATUS_BY_CELL.get(status_cell)
        if status is None:
            allowed_cells = ", ".join(sorted(OPEN_STATUS_BY_CELL))
            raise ValueError(
                f"{path} row {open_id!r} has invalid status {status_cell!r}; "
                f"allowed cells are {allowed_cells}"
            )
        statuses[status] += 1
        if status in ACTIVE_OPEN_STATUSES:
            active_ids.add(open_id)
        elif status == RESOLVED_OPEN_STATUS:
            resolved_ids.add(open_id)
        else:
            raise ValueError(f"{path} row {open_id!r} has unclassified status {status!r}")
    return ids, raw_rows, statuses, active_ids, resolved_ids


def _coverage_mapping(backlog: dict[str, Any]) -> dict[str, Any]:
    mapping: dict[str, Any] = {
        "source": backlog.get("source"),
        "notes": backlog.get("notes", []),
        "lanes": backlog.get("lanes"),
        "global_decisions": sorted(backlog.get("global_decisions", [])),
        "global_acceptance": sorted(backlog.get("global_acceptance", [])),
        "wave_order": [],
        "waves": {},
        "slice_order": {},
        "slices": {},
    }
    for wave in backlog.get("waves", []):
        wave_id = wave["id"]
        mapping["wave_order"].append(wave_id)
        mapping["slice_order"][wave_id] = []
        mapping["waves"][wave_id] = {
            "title": wave.get("title"),
            "faz": wave.get("faz"),
        }
        for item in wave.get("slices", []):
            slice_id = item["id"]
            mapping["slice_order"][wave_id].append(slice_id)
            mapping["slices"][slice_id] = {
                "wave_id": wave_id,
                "task": item.get("task"),
                "lane": item.get("lane"),
                "scope": item.get("scope"),
                "depends_on": sorted(item.get("depends_on", [])),
                "decisions": sorted(item.get("decisions", [])),
                "open": sorted(item.get("open", [])),
                "resolved_open_history": sorted(item.get("resolved_open_history", [])),
                "acceptance": sorted(item.get("acceptance", [])),
                "acceptance_notes": sorted(item.get("acceptance_notes", [])),
                "screens": sorted(item.get("screens", [])),
                "issues": sorted(item.get("issues", [])),
                "critical_analysis": item.get("critical_analysis"),
                "manifest_task": item.get("manifest_task"),
                "manifest_status": item.get("manifest_status"),
                "manifest_depends_on": item.get("manifest_depends_on"),
                "manifest_blocked_by": item.get("manifest_blocked_by"),
                "execution_state": item.get("execution_state"),
                "protocol_gate": item.get("protocol_gate"),
                "production_gate": item.get("production_gate"),
                "decision_only": item.get("decision_only", False),
            }
    return mapping


def _records_by_id(records: list[dict[str, str]]) -> dict[str, dict[str, str]]:
    result: dict[str, dict[str, str]] = {}
    for record in records:
        result.setdefault(record["id"], record)
    return result


def _delegation_source(root: Path) -> tuple[list[dict[str, str]], str]:
    document = (root / "docs/decisions/SINAN_KARARLARI_2026-10-21.md").read_text(encoding="utf-8")
    digest = _sha256(document.encode("utf-8"))
    if digest != "acb180768b01402a520216564f7ef615077da6c45917e2034b054698fc7ed674":
        raise ValueError("Delegation authority source digest changed")
    start = document.index("\n## 3.")
    end = document.index("\n## 4.", start)
    parsed = []
    for match in re.finditer(r"(?m)^\| ([1-9][0-9]*) \| (.+) \|$", document[start:end]):
        number, tail = match.groups()
        columns = tail.split(" | ")
        if number == "133":
            if len(columns) != 1 or not columns[0].startswith("EK-38: "):
                raise ValueError("No133 delegation representation is not reversible")
            area, decision = "EK-38", columns[0][7:]
            if area + ": " + decision != columns[0]:
                raise ValueError("No133 delegation normalization lost source bytes")
        elif len(columns) == 3:
            area, decision, _place = columns
        else:
            raise ValueError(f"Invalid delegated source row: {number}")
        values = (
            "DELEGE-20261021-" + number, area, decision, "GECICI_VARSAYIM", "-",
            "[2026-10-21 §3 No " + number + "](SINAN_KARARLARI_2026-10-21.md) + "
            "[2026-10-22 uzlaştırması](SINAN_KARARLARI_2026-10-22.md)",
            "Sinan Göymen", "Kaynak kaydıdır; kullanıcı tarafından geri alınabilir ve geçicidir; "
            "runtime/CI/grant/production kabulü değildir.",
        )
        parsed.append(dict(zip(DECISION_FIELDS, values, strict=True)))
    ordered = [row["id"] for row in parsed]
    if len(ordered) != 96 or _duplicates(ordered) or _sha256("\n".join(ordered).encode()) != (
        "535081bccd044dfaa41a10cbcbb832811e10e9e1374a231eb38cba52069d282d"
    ):
        raise ValueError("Delegation source identity/order changed")
    return parsed, digest


def recompute(root: Path) -> dict[str, Any]:
    decision_md, decision_rows = _read_markdown_decisions(
        root / "docs/decisions/KARAR_SICILI.md"
    )
    decision_csv = _read_csv_decisions(root / "docs/decisions/KARAR_SICILI.csv")
    source_delegations, delegation_digest = _delegation_source(root)
    legacy_records = [row for row in decision_md if row["id"].startswith("DEC-")]
    delegation_records = [row for row in decision_md if row["id"].startswith("DELEGE-")]
    legacy_ids = [row["id"] for row in legacy_records]
    legacy_rows = [row for row in decision_rows if row.startswith("| DEC-")]
    reviewed_legacy = (
        len(legacy_ids) == 117
        and Counter(row["status"] for row in legacy_records) == {"KILITLI": 114, "GECICI_VARSAYIM": 3}
        and _sha256(_canonical_row_bytes(legacy_rows)) == "97be4cad7d4ffe9a4c5cebedf797c32a4fca6d983cee98831350c881ccf9a8d2"
        and _sha256("\n".join(legacy_ids).encode()) == "94c1b852e421288bb110f005bdf5e77a35046c3e9729f8eb6a044440d7da085f"
    )
    open_ids, open_rows, open_statuses, active_open_ids, resolved_open_ids = _read_open_rows(
        root / "docs/decisions/ACIK_KARARLAR.md"
    )
    backlog = json.loads(
        (root / "docs/backlog/DELIVERY_BACKLOG_v1.json").read_text(encoding="utf-8")
    )

    md_ids = [record["id"] for record in decision_md]
    csv_ids = [record["id"] for record in decision_csv]
    md_by_id = _records_by_id(decision_md)
    csv_by_id = _records_by_id(decision_csv)
    mismatches: list[dict[str, Any]] = []
    for decision_id in sorted(set(md_by_id) & set(csv_by_id)):
        fields = [
            field
            for field in DECISION_FIELDS[1:]
            if md_by_id[decision_id][field] != csv_by_id[decision_id][field]
        ]
        if fields:
            mismatches.append({"id": decision_id, "fields": fields})

    decision_payload = _canonical_row_bytes(decision_rows)
    decision_order_payload = "\n".join(md_ids).encode("utf-8")
    open_payload = _canonical_row_bytes(open_rows)
    mapping = _coverage_mapping(backlog)
    coverage_payload = json.dumps(
        mapping,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")

    used_decisions = set(backlog.get("global_decisions", []))
    used_active_open: set[str] = set()
    used_resolved_open_history: set[str] = set()
    slice_count = 0
    for wave in backlog.get("waves", []):
        for item in wave.get("slices", []):
            slice_count += 1
            used_decisions.update(item.get("decisions", []))
            used_active_open.update(item.get("open", []))
            used_resolved_open_history.update(item.get("resolved_open_history", []))

    canonical_decisions = set(md_ids)
    canonical_open = set(open_ids)
    required_open_ids = set(REQUIRED_OPEN_IDS)
    md_duplicates = _duplicates(md_ids)
    csv_duplicates = _duplicates(csv_ids)
    missing_in_csv = sorted(canonical_decisions - set(csv_ids))
    extra_in_csv = sorted(set(csv_ids) - canonical_decisions)

    return {
        "sourceTree": "repository-root",
        "legacyDecisionRegistry": {
            "count": len(legacy_ids),
            "orderedIds": legacy_ids,
            "statusHistogram": dict(sorted(Counter(row["status"] for row in legacy_records).items())),
            "canonicalRowsSha256": _sha256(_canonical_row_bytes(legacy_rows)),
            "isReviewed": reviewed_legacy,
        },
        "delegatedSourceAdmission": {
            "count": len(delegation_records),
            "orderedIds": [row["id"] for row in delegation_records],
            "sourceSha256": delegation_digest,
            "isExact": delegation_records == source_delegations
            and md_ids == legacy_ids + [row["id"] for row in source_delegations],
        },
        "decisionRegistry": {
            "orderedIds": md_ids,
            "sortedUniqueIds": sorted(canonical_decisions),
            "duplicates": md_duplicates,
            "statusHistogram": dict(sorted(Counter(r["status"] for r in decision_md).items())),
            "count": len(md_ids),
            "canonicalRowsByteLength": len(decision_payload),
            "canonicalRowsSha256": _sha256(decision_payload),
            "orderedIdsByteLength": len(decision_order_payload),
            "orderedIdsSha256": _sha256(decision_order_payload),
            "firstCanonicalRows": sorted(decision_rows)[:3],
            "lastCanonicalRows": sorted(decision_rows)[-3:],
        },
        "decisionRegistryParity": {
            "markdownCount": len(md_ids),
            "csvCount": len(csv_ids),
            "markdownDuplicates": md_duplicates,
            "csvDuplicates": csv_duplicates,
            "missingInCsv": missing_in_csv,
            "extraInCsv": extra_in_csv,
            "orderedIdsEqual": md_ids == csv_ids,
            "semanticMismatches": mismatches,
            "isExact": not (
                md_duplicates
                or csv_duplicates
                or missing_in_csv
                or extra_in_csv
                or md_ids != csv_ids
                or mismatches
            ),
        },
        "decisionRoundParity": {
            "ids": list(DECISION_ROUND_IDS),
            "missingInMarkdown": sorted(set(DECISION_ROUND_IDS) - canonical_decisions),
            "missingInCsv": sorted(set(DECISION_ROUND_IDS) - set(csv_ids)),
            "semanticMismatches": [
                mismatch for mismatch in mismatches if mismatch["id"] in DECISION_ROUND_IDS
            ],
        },
        "openDecisionCatalog": {
            "orderedIds": open_ids,
            "sortedUniqueIds": sorted(canonical_open),
            "activeIds": sorted(active_open_ids),
            "resolvedIds": sorted(resolved_open_ids),
            "duplicates": _duplicates(open_ids),
            "missingExpectedIds": sorted(required_open_ids - canonical_open),
            "extraExpectedIds": sorted(canonical_open - required_open_ids),
            "statusHistogram": dict(sorted(open_statuses.items())),
            "count": len(open_ids),
            "canonicalRowsByteLength": len(open_payload),
            "canonicalRowsSha256": _sha256(open_payload),
            "firstCanonicalRows": sorted(open_rows)[:3],
            "lastCanonicalRows": sorted(open_rows)[-3:],
            "isExpectedExact": not (
                _duplicates(open_ids)
                or required_open_ids - canonical_open
                or canonical_open - required_open_ids
            ),
        },
        "deliveryBacklog": {
            "waveCount": len(backlog.get("waves", [])),
            "sliceCount": slice_count,
            "usedDecisionIds": sorted(used_decisions),
            "usedActiveOpenIds": sorted(used_active_open),
            "usedResolvedOpenHistoryIds": sorted(used_resolved_open_history),
            "missingDecisionIds": sorted(set(legacy_ids) - used_decisions),
            "extraDecisionIds": sorted(used_decisions - set(legacy_ids)),
            "missingActiveOpenIds": sorted(active_open_ids - used_active_open),
            "extraActiveOpenIds": sorted(used_active_open - active_open_ids),
            "missingResolvedOpenHistoryIds": sorted(
                resolved_open_ids - used_resolved_open_history
            ),
            "extraResolvedOpenHistoryIds": sorted(
                used_resolved_open_history - resolved_open_ids
            ),
            "canonicalMappingByteLength": len(coverage_payload),
            "canonicalMappingSha256": _sha256(coverage_payload),
            "serialization": {
                "encoding": "utf-8",
                "ensureAscii": False,
                "sortKeys": True,
                "separators": [",", ":"],
                "trailingNewline": False,
            },
        },
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[2],
        help="Repository root",
    )
    args = parser.parse_args(argv)
    try:
        result = recompute(args.root.resolve())
    except (OSError, UnicodeError, csv.Error, json.JSONDecodeError, ValueError) as exc:
        _write_utf8(f"Independent provenance recomputation FAILED: {exc}")
        return 1
    _write_utf8(json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True))
    assertions_hold = (
        result["decisionRegistryParity"]["isExact"]
        and result["legacyDecisionRegistry"]["isReviewed"]
        and result["delegatedSourceAdmission"]["isExact"]
        and not result["decisionRoundParity"]["missingInMarkdown"]
        and not result["decisionRoundParity"]["missingInCsv"]
        and not result["decisionRoundParity"]["semanticMismatches"]
        and result["openDecisionCatalog"]["isExpectedExact"]
        and not result["deliveryBacklog"]["missingDecisionIds"]
        and not result["deliveryBacklog"]["extraDecisionIds"]
        and not result["deliveryBacklog"]["missingActiveOpenIds"]
        and not result["deliveryBacklog"]["extraActiveOpenIds"]
        and not result["deliveryBacklog"]["missingResolvedOpenHistoryIds"]
        and not result["deliveryBacklog"]["extraResolvedOpenHistoryIds"]
    )
    return 0 if assertions_hold else 1


def _write_utf8(value: str) -> None:
    payload = (value + "\n").encode("utf-8")
    binary_stream = getattr(sys.stdout, "buffer", None)
    if binary_stream is not None:
        binary_stream.write(payload)
    else:
        sys.stdout.write(payload.decode("utf-8"))


if __name__ == "__main__":
    raise SystemExit(main())
