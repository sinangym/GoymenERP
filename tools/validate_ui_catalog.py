#!/usr/bin/env python3
"""Validate the repository UI catalog using only the Python standard library."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import posixpath
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path, PurePosixPath
from typing import Any, Iterable


EXPECTED_SOURCE_RECORDS = 72
EXPECTED_CANONICAL_SCREENS = 159
EXPECTED_ORIGINAL_ASSETS = 77
EXPECTED_PROTOTYPES = 5
EXPECTED_SELECTED_SOURCES = 19
EXPECTED_MANIFEST_ASSETS = (
    EXPECTED_ORIGINAL_ASSETS + EXPECTED_PROTOTYPES + EXPECTED_SELECTED_SOURCES
)
EXPECTED_DECISION_IDS = {f"UI-MAP-{number:03d}" for number in range(1, 7)}
ALLOWED_NON_CANONICAL_RELATED_REFERENCE_IDS = {"GLOBAL-SHELL"}
ARTIFACT_CLASS_ROOTS = {
    "ORIGINAL_ASSET": PurePosixPath("docs/ui/assets/original"),
    "REFERENCE_PROTOTYPE": PurePosixPath("docs/ui/prototypes"),
    "SELECTED_SOURCE": PurePosixPath("docs/ui/sources"),
}
SCREEN_CARD_ROOT = PurePosixPath("docs/ui/screens")

SCREEN_CATALOG_FIELDS = (
    "record_id",
    "source_namespace",
    "source_id",
    "title",
    "domain",
    "canonical_ids",
    "related_reference_ids",
    "conditional_workspace_ids",
    "mapping_status",
    "decision_refs",
    "source_status",
    "visual_status",
    "source_date",
    "source_version",
    "primary_reference",
    "screen_card",
)
CANONICAL_STATUS_FIELDS = (
    "canonical_id",
    "domain",
    "name",
    "scope",
    "reference_coverage",
    "reference_records",
)
ASSET_MANIFEST_FIELDS = (
    "repo_path",
    "artifact_class",
    "source_screen_or_set",
    "bytes",
    "sha256",
    "source_container_sha256",
    "classification",
    "notes",
)
LIST_PARITY_FIELDS = (
    "canonical_ids",
    "related_reference_ids",
    "conditional_workspace_ids",
    "decision_refs",
)
SCALAR_PARITY_FIELDS = (
    "record_id",
    "source_namespace",
    "source_id",
    "mapping_status",
    "source_status",
    "visual_status",
    "source_date",
    "source_version",
)


def _read_csv(
    path: Path,
    expected_fields: tuple[str, ...],
    errors: list[str],
) -> list[dict[str, str]]:
    if not path.is_file():
        errors.append(f"Missing CSV file: {path}")
        return []

    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        actual_fields = tuple(reader.fieldnames or ())
        if actual_fields != expected_fields:
            errors.append(
                f"CSV schema mismatch for {path}: expected {expected_fields}, "
                f"got {actual_fields}"
            )
        return [
            {key: value if value is not None else "" for key, value in row.items() if key}
            for row in reader
        ]


def _parse_json_list(
    raw_value: str,
    label: str,
    errors: list[str],
) -> list[str]:
    try:
        value = json.loads(raw_value or "[]")
    except json.JSONDecodeError as exc:
        errors.append(f"Invalid JSON list for {label}: {exc.msg}")
        return []

    if not isinstance(value, list) or any(not isinstance(item, str) for item in value):
        errors.append(f"Expected a JSON string list for {label}, got {value!r}")
        return []
    if len(value) != len(set(value)):
        errors.append(f"Duplicate values in {label}: {value!r}")
    return value


def _parse_frontmatter(path: Path, errors: list[str]) -> dict[str, Any]:
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exc:
        errors.append(f"Cannot read screen card {path}: {exc}")
        return {}

    if not lines or lines[0].strip() != "---":
        errors.append(f"Screen card has no opening frontmatter delimiter: {path}")
        return {}

    try:
        closing_index = next(
            index for index, line in enumerate(lines[1:], start=1) if line.strip() == "---"
        )
    except StopIteration:
        errors.append(f"Screen card has no closing frontmatter delimiter: {path}")
        return {}

    metadata: dict[str, Any] = {}
    for line_number, line in enumerate(lines[1:closing_index], start=2):
        if not line.strip():
            continue
        if ":" not in line:
            errors.append(f"Invalid frontmatter at {path}:{line_number}: {line!r}")
            continue
        key, raw_value = line.split(":", 1)
        key = key.strip()
        raw_value = raw_value.strip()
        if key in metadata:
            errors.append(f"Duplicate frontmatter key {key!r} in {path}")
            continue
        try:
            metadata[key] = json.loads(raw_value)
        except json.JSONDecodeError:
            metadata[key] = raw_value
    return metadata


def _lexical_repo_path(raw_path: str) -> PurePosixPath | None:
    """Return an exact normalized relative POSIX repo path, or ``None``."""

    if not raw_path or "\\" in raw_path:
        return None
    path = PurePosixPath(raw_path)
    if path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        return None
    if path.as_posix() != raw_path:
        return None
    return path


def _is_under(path: PurePosixPath, expected_root: PurePosixPath) -> bool:
    try:
        relative = path.relative_to(expected_root)
    except ValueError:
        return False
    return bool(relative.parts)


def _has_symlink_component(root: Path, lexical_path: PurePosixPath) -> bool:
    current = root
    for part in lexical_path.parts:
        current = current / part
        if current.is_symlink():
            return True
    return False


def _safe_repo_file(
    root: Path,
    raw_path: str,
    *,
    expected_roots: tuple[PurePosixPath, ...],
) -> tuple[Path | None, PurePosixPath | None, str | None]:
    lexical_path = _lexical_repo_path(raw_path)
    if lexical_path is None:
        return None, None, "path is not an exact lexical repo-relative POSIX path"
    if not any(_is_under(lexical_path, expected_root) for expected_root in expected_roots):
        return None, lexical_path, "path is outside its allowed repository root"
    if _has_symlink_component(root, lexical_path):
        return None, lexical_path, "path or an ancestor is a symbolic link"

    candidate = root.joinpath(*lexical_path.parts)
    try:
        resolved = candidate.resolve(strict=True)
    except OSError:
        return None, lexical_path, "file does not exist"
    if not _is_within(root, resolved):
        return None, lexical_path, "resolved path escapes the repository root"
    if not candidate.is_file():
        return None, lexical_path, "path is not a regular file"
    return candidate, lexical_path, None


def _is_within(root: Path, candidate: Path) -> bool:
    try:
        candidate.relative_to(root)
        return True
    except ValueError:
        return False


def _relative_repo_path(root: Path, path: Path) -> str:
    return path.relative_to(root).as_posix()


def _filesystem_files(
    root: Path,
    lexical_root: PurePosixPath,
    label: str,
    errors: list[str],
) -> set[str]:
    directory = root.joinpath(*lexical_root.parts)
    paths: set[str] = set()
    if _has_symlink_component(root, lexical_root):
        errors.append(
            f"Symbolic link is forbidden at or above {label}: "
            f"{lexical_root.as_posix()}"
        )
        return paths
    for path in directory.rglob("*"):
        lexical_path = _relative_repo_path(root, path)
        if path.is_symlink():
            errors.append(f"Symbolic link is forbidden under {label}: {lexical_path}")
            continue
        if path.is_file():
            paths.add(lexical_path)
    return paths


def _primary_reference_repo_path(
    card_lexical_path: PurePosixPath,
    raw_reference: str,
) -> PurePosixPath | None:
    if not raw_reference or "\\" in raw_reference:
        return None
    reference = PurePosixPath(raw_reference)
    if reference.is_absolute():
        return None
    normalized = posixpath.normpath(
        (card_lexical_path.parent / reference).as_posix()
    )
    if normalized in {"", ".", ".."} or normalized.startswith("../"):
        return None
    return _lexical_repo_path(normalized)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def _duplicates(values: Iterable[str]) -> list[str]:
    return sorted(value for value, count in Counter(values).items() if count > 1)


def _accepted_ui_mapping_decisions(reconciliation_text: str) -> set[str]:
    section_match = re.search(
        r"^## 8\. Karara bağlanan altı uzlaştırma\s*$\n(?P<body>.*?)(?=^##\s|\Z)",
        reconciliation_text,
        flags=re.MULTILINE | re.DOTALL,
    )
    if section_match is None:
        return set()
    lines = section_match.group("body").splitlines()
    for index, line in enumerate(lines[:-1]):
        if not re.fullmatch(
            r"\s*\|\s*Karar\s*\|\s*(?:Kesin sonuç|Sonuç)\s*\|\s*",
            line,
        ):
            continue
        if not re.fullmatch(r"\s*\|\s*:?-+:?\s*\|\s*:?-+:?\s*\|\s*", lines[index + 1]):
            return set()

        accepted: set[str] = set()
        for row in lines[index + 2 :]:
            if not row.strip():
                break
            row_match = re.fullmatch(
                r"\s*\|\s*`(UI-MAP-\d{3})`\s*\|[^\n]*\|\s*",
                row,
            )
            if row_match is None:
                break
            accepted.add(row_match.group(1))
        return accepted
    return set()


def validate_repository(repository_root: Path | str) -> list[str]:
    """Return every catalog validation error found under ``repository_root``."""

    root = Path(repository_root).resolve()
    ui_root = root / "docs" / "ui"
    errors: list[str] = []

    screen_rows = _read_csv(
        ui_root / "SCREEN_CATALOG.csv",
        SCREEN_CATALOG_FIELDS,
        errors,
    )
    canonical_rows = _read_csv(
        ui_root / "CANONICAL_SCREEN_STATUS.csv",
        CANONICAL_STATUS_FIELDS,
        errors,
    )
    asset_rows = _read_csv(
        ui_root / "ASSET_MANIFEST.csv",
        ASSET_MANIFEST_FIELDS,
        errors,
    )

    if len(screen_rows) != EXPECTED_SOURCE_RECORDS:
        errors.append(
            f"Golden source record count mismatch: expected {EXPECTED_SOURCE_RECORDS}, "
            f"got {len(screen_rows)}"
        )
    if len(canonical_rows) != EXPECTED_CANONICAL_SCREENS:
        errors.append(
            f"Golden canonical screen count mismatch: expected {EXPECTED_CANONICAL_SCREENS}, "
            f"got {len(canonical_rows)}"
        )

    record_ids = [row.get("record_id", "") for row in screen_rows]
    duplicate_record_ids = _duplicates(record_ids)
    if duplicate_record_ids:
        errors.append(f"Duplicate record_id values: {', '.join(duplicate_record_ids)}")
    if any(not record_id for record_id in record_ids):
        errors.append("SCREEN_CATALOG.csv contains an empty record_id")
    record_id_set = set(record_ids)

    canonical_ids = [row.get("canonical_id", "") for row in canonical_rows]
    duplicate_canonical_ids = _duplicates(canonical_ids)
    if duplicate_canonical_ids:
        errors.append(
            f"Duplicate canonical_id values: {', '.join(duplicate_canonical_ids)}"
        )
    if any(not canonical_id for canonical_id in canonical_ids):
        errors.append("CANONICAL_SCREEN_STATUS.csv contains an empty canonical_id")
    canonical_id_set = set(canonical_ids)

    reconciliation_path = ui_root / "SOURCE_RECONCILIATION.md"
    try:
        reconciliation_text = reconciliation_path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        errors.append(f"Cannot read decision source {reconciliation_path}: {exc}")
        reconciliation_text = ""
    resolved_decisions = _accepted_ui_mapping_decisions(reconciliation_text)
    missing_golden_decisions = sorted(EXPECTED_DECISION_IDS - resolved_decisions)
    if missing_golden_decisions:
        errors.append(
            "Missing golden UI mapping decisions in SOURCE_RECONCILIATION.md: "
            + ", ".join(missing_golden_decisions)
        )
    unexpected_decisions = sorted(resolved_decisions - EXPECTED_DECISION_IDS)
    if unexpected_decisions:
        errors.append(
            "Unexpected accepted UI mapping decisions in SOURCE_RECONCILIATION.md: "
            + ", ".join(unexpected_decisions)
        )

    expected_reference_records: dict[str, set[str]] = defaultdict(set)
    card_paths: list[str] = []

    for row_number, row in enumerate(screen_rows, start=2):
        record_id = row.get("record_id", "") or f"row-{row_number}"
        parsed_lists = {
            field: _parse_json_list(
                row.get(field, "[]"),
                f"SCREEN_CATALOG.csv:{row_number} {field}",
                errors,
            )
            for field in LIST_PARITY_FIELDS
        }
        if not parsed_lists["canonical_ids"]:
            errors.append(f"{record_id} has no canonical_ids")
        for canonical_id in parsed_lists["canonical_ids"]:
            if canonical_id not in canonical_id_set:
                errors.append(
                    f"Unresolved canonical reference {canonical_id!r} in {record_id}"
                )
            expected_reference_records[canonical_id].add(record_id)
        for workspace_id in parsed_lists["conditional_workspace_ids"]:
            if workspace_id not in canonical_id_set:
                errors.append(
                    f"Unresolved conditional workspace reference {workspace_id!r} "
                    f"in {record_id}"
                )
        for related_id in parsed_lists["related_reference_ids"]:
            if (
                related_id not in canonical_id_set
                and related_id not in ALLOWED_NON_CANONICAL_RELATED_REFERENCE_IDS
            ):
                errors.append(
                    f"Unresolved related reference {related_id!r} in {record_id}"
                )
        for decision_id in parsed_lists["decision_refs"]:
            if (
                decision_id not in EXPECTED_DECISION_IDS
                or decision_id not in resolved_decisions
            ):
                errors.append(
                    f"Unresolved decision reference {decision_id!r} in {record_id}"
                )

        raw_card_path = row.get("screen_card", "")
        card_paths.append(raw_card_path)
        card_path, _, card_error = _safe_repo_file(
            root,
            raw_card_path,
            expected_roots=(SCREEN_CARD_ROOT,),
        )
        if card_error is not None or card_path is None:
            errors.append(
                f"Missing or unsafe screen card for {record_id}: {raw_card_path} "
                f"({card_error})"
            )
            continue

        metadata = _parse_frontmatter(card_path, errors)
        for field in LIST_PARITY_FIELDS:
            card_value = metadata.get(field, [])
            if card_value != parsed_lists[field]:
                errors.append(
                    f"Frontmatter/catalog parity mismatch for {record_id}.{field}: "
                    f"catalog={parsed_lists[field]!r}, card={card_value!r}"
                )
        for field in SCALAR_PARITY_FIELDS:
            catalog_value = row.get(field, "")
            card_value = metadata.get(field, "")
            if card_value != catalog_value:
                errors.append(
                    f"Frontmatter/catalog parity mismatch for {record_id}.{field}: "
                    f"catalog={catalog_value!r}, card={card_value!r}"
                )

    duplicate_card_paths = _duplicates(card_paths)
    if duplicate_card_paths:
        errors.append(f"Duplicate screen_card paths: {', '.join(duplicate_card_paths)}")
    if len(set(card_paths)) != EXPECTED_SOURCE_RECORDS:
        errors.append(
            f"Golden source card count mismatch: expected {EXPECTED_SOURCE_RECORDS}, "
            f"got {len(set(card_paths))}"
        )

    for row in canonical_rows:
        canonical_id = row.get("canonical_id", "")
        actual_records = {
            item.strip()
            for item in row.get("reference_records", "").split(";")
            if item.strip()
        }
        unresolved_records = sorted(actual_records - record_id_set)
        if unresolved_records:
            errors.append(
                f"Unresolved reference_records for {canonical_id}: "
                + ", ".join(unresolved_records)
            )
        expected_records = expected_reference_records.get(canonical_id, set())
        if actual_records != expected_records:
            errors.append(
                f"Canonical reference parity mismatch for {canonical_id}: "
                f"catalog={sorted(expected_records)!r}, "
                f"canonical_status={sorted(actual_records)!r}"
            )

    manifest_paths = [row.get("repo_path", "") for row in asset_rows]
    duplicate_manifest_paths = _duplicates(manifest_paths)
    if duplicate_manifest_paths:
        errors.append(
            f"Duplicate ASSET_MANIFEST repo_path values: {', '.join(duplicate_manifest_paths)}"
        )
    manifest_path_set = set(manifest_paths)
    artifact_counts = Counter(row.get("artifact_class", "") for row in asset_rows)
    if len(asset_rows) != EXPECTED_MANIFEST_ASSETS:
        errors.append(
            f"Golden manifest asset count mismatch: expected {EXPECTED_MANIFEST_ASSETS}, "
            f"got {len(asset_rows)}"
        )
    unknown_artifact_classes = sorted(set(artifact_counts) - set(ARTIFACT_CLASS_ROOTS))
    if unknown_artifact_classes:
        errors.append(
            "Unknown artifact_class values: " + ", ".join(unknown_artifact_classes)
        )
    if artifact_counts["ORIGINAL_ASSET"] != EXPECTED_ORIGINAL_ASSETS:
        errors.append(
            f"Golden ORIGINAL_ASSET count mismatch: expected {EXPECTED_ORIGINAL_ASSETS}, "
            f"got {artifact_counts['ORIGINAL_ASSET']}"
        )
    if artifact_counts["REFERENCE_PROTOTYPE"] != EXPECTED_PROTOTYPES:
        errors.append(
            f"Golden REFERENCE_PROTOTYPE count mismatch: expected {EXPECTED_PROTOTYPES}, "
            f"got {artifact_counts['REFERENCE_PROTOTYPE']}"
        )
    if artifact_counts["SELECTED_SOURCE"] != EXPECTED_SELECTED_SOURCES:
        errors.append(
            f"Golden SELECTED_SOURCE count mismatch: expected {EXPECTED_SELECTED_SOURCES}, "
            f"got {artifact_counts['SELECTED_SOURCE']}"
        )

    for row_number, row in enumerate(asset_rows, start=2):
        raw_path = row.get("repo_path", "")
        artifact_class = row.get("artifact_class", "")
        expected_root = ARTIFACT_CLASS_ROOTS.get(artifact_class)
        path, _, path_error = _safe_repo_file(
            root,
            raw_path,
            expected_roots=(expected_root,) if expected_root else tuple(
                ARTIFACT_CLASS_ROOTS.values()
            ),
        )
        if path_error is not None or path is None:
            errors.append(
                f"Missing or unsafe manifest asset at ASSET_MANIFEST.csv:{row_number}: "
                f"{raw_path} ({path_error})"
            )
            continue

        try:
            expected_size = int(row.get("bytes", ""))
        except ValueError:
            errors.append(
                f"Invalid byte size at ASSET_MANIFEST.csv:{row_number}: "
                f"{row.get('bytes', '')!r}"
            )
        else:
            actual_size = path.stat().st_size
            if actual_size != expected_size:
                errors.append(
                    f"Byte size mismatch for {raw_path}: expected {expected_size}, "
                    f"got {actual_size}"
                )

        expected_hash = row.get("sha256", "").upper()
        if not re.fullmatch(r"[0-9A-F]{64}", expected_hash):
            errors.append(f"Invalid SHA-256 value for {raw_path}: {expected_hash!r}")
        else:
            actual_hash = _sha256(path)
            if actual_hash != expected_hash:
                errors.append(
                    f"SHA-256 mismatch for {raw_path}: expected {expected_hash}, "
                    f"got {actual_hash}"
                )

    actual_original_paths = _filesystem_files(
        root,
        ARTIFACT_CLASS_ROOTS["ORIGINAL_ASSET"],
        "ORIGINAL_ASSET",
        errors,
    )
    manifest_original_paths = {
        row.get("repo_path", "")
        for row in asset_rows
        if row.get("artifact_class") == "ORIGINAL_ASSET"
    }
    if actual_original_paths != manifest_original_paths:
        errors.append(
            "ORIGINAL_ASSET filesystem/manifest mismatch: "
            f"unmanifested={sorted(actual_original_paths - manifest_original_paths)!r}, "
            f"missing={sorted(manifest_original_paths - actual_original_paths)!r}"
        )

    actual_prototype_paths = _filesystem_files(
        root,
        ARTIFACT_CLASS_ROOTS["REFERENCE_PROTOTYPE"],
        "REFERENCE_PROTOTYPE",
        errors,
    )
    manifest_prototype_paths = {
        row.get("repo_path", "")
        for row in asset_rows
        if row.get("artifact_class") == "REFERENCE_PROTOTYPE"
    }
    if len(actual_prototype_paths) != EXPECTED_PROTOTYPES:
        errors.append(
            f"Golden prototype filesystem count mismatch: expected {EXPECTED_PROTOTYPES}, "
            f"got {len(actual_prototype_paths)}"
        )
    if actual_prototype_paths != manifest_prototype_paths:
        errors.append(
            "REFERENCE_PROTOTYPE filesystem/manifest mismatch: "
            f"unmanifested={sorted(actual_prototype_paths - manifest_prototype_paths)!r}, "
            f"missing={sorted(manifest_prototype_paths - actual_prototype_paths)!r}"
        )

    actual_source_paths = _filesystem_files(
        root,
        ARTIFACT_CLASS_ROOTS["SELECTED_SOURCE"],
        "SELECTED_SOURCE",
        errors,
    )
    manifest_source_paths = {
        row.get("repo_path", "")
        for row in asset_rows
        if row.get("artifact_class") == "SELECTED_SOURCE"
    }
    if len(actual_source_paths) != EXPECTED_SELECTED_SOURCES:
        errors.append(
            f"Golden selected source filesystem count mismatch: "
            f"expected {EXPECTED_SELECTED_SOURCES}, got {len(actual_source_paths)}"
        )
    if actual_source_paths != manifest_source_paths:
        errors.append(
            "SELECTED_SOURCE filesystem/manifest mismatch: "
            f"unmanifested={sorted(actual_source_paths - manifest_source_paths)!r}, "
            f"missing={sorted(manifest_source_paths - actual_source_paths)!r}"
        )

    for row_number, row in enumerate(screen_rows, start=2):
        record_id = row.get("record_id", "") or f"row-{row_number}"
        card_path, card_lexical_path, card_error = _safe_repo_file(
            root,
            row.get("screen_card", ""),
            expected_roots=(SCREEN_CARD_ROOT,),
        )
        if card_error is not None or card_path is None or card_lexical_path is None:
            continue
        primary_reference = row.get("primary_reference", "")
        if not primary_reference:
            errors.append(f"Missing primary_reference for {record_id}")
            continue
        for raw_reference in primary_reference.split(" ve "):
            reference_lexical_path = _primary_reference_repo_path(
                card_lexical_path,
                raw_reference,
            )
            if reference_lexical_path is None:
                errors.append(
                    f"Missing or unsafe primary_reference for {record_id}: {raw_reference}"
                )
                continue
            reference_path, _, reference_error = _safe_repo_file(
                root,
                reference_lexical_path.as_posix(),
                expected_roots=tuple(ARTIFACT_CLASS_ROOTS.values()),
            )
            if reference_error is not None or reference_path is None:
                errors.append(
                    f"Missing or unsafe primary_reference for {record_id}: "
                    f"{raw_reference} ({reference_error})"
                )
                continue
            repository_reference = reference_lexical_path.as_posix()
            if repository_reference not in manifest_path_set:
                errors.append(
                    f"Unmanifested primary_reference for {record_id}: "
                    f"{repository_reference}"
                )

    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="Repository root (defaults to the parent of tools/).",
    )
    args = parser.parse_args(argv)

    errors = validate_repository(args.repo_root)
    if errors:
        print("UI catalog golden validation FAILED", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("UI catalog golden validation PASSED")
    print(f"- source records: {EXPECTED_SOURCE_RECORDS}/{EXPECTED_SOURCE_RECORDS}")
    print(f"- canonical screens: {EXPECTED_CANONICAL_SCREENS}/{EXPECTED_CANONICAL_SCREENS}")
    print(f"- source cards: {EXPECTED_SOURCE_RECORDS}/{EXPECTED_SOURCE_RECORDS}")
    print(f"- ORIGINAL_ASSET files: {EXPECTED_ORIGINAL_ASSETS}/{EXPECTED_ORIGINAL_ASSETS}")
    print(f"- reference prototypes: {EXPECTED_PROTOTYPES}/{EXPECTED_PROTOTYPES}")
    print(f"- selected source files: {EXPECTED_SELECTED_SOURCES}/{EXPECTED_SELECTED_SOURCES}")
    print("- catalog/card/reference/hash parity: verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
