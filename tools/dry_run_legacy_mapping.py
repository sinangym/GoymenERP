#!/usr/bin/env python3
"""Read-only legacy source inventory and mapping dry-run (R04.1).

Reads explicitly synthetic CSV fixtures and mapping contracts, and writes one
deterministic report. Never connects to a database or writes an ERP record. Real
LEGACY_EXTRACT access is disabled: accepted E03.1 owner-validation sources are absent
at main b6ed7ca5. A later file or register alone cannot enable this path.
"""

from __future__ import annotations

import argparse
import csv
from datetime import datetime
import decimal
from functools import lru_cache
import hashlib
import io
import json
import os
from pathlib import Path
import re
import stat
import sys
from types import ModuleType
from typing import Any, Sequence


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
ENGINE_PATH = Path("tools/generate_recovery_surface_inventory.py")
ENGINE_SHA256 = "1c5cf0b876c06e7bd1177641e30545b18c092e7a186e4805ad7cd53c4bc51293"
MANIFEST_SCHEMA_PATH = Path("contracts/migration/legacy-source-manifest-v1.schema.json")
PROFILE_SCHEMA_PATH = Path("contracts/migration/legacy-mapping-profile-v1.schema.json")
REPORT_VERSION = "goymenerp.legacy-mapping-dry-run.v1"
INSTANT_FORMAT = "%Y-%m-%dT%H:%M:%SZ"
MAXIMUM_DOCUMENT_BYTES = 16 * 1024 * 1024
MAXIMUM_EXTRACT_BYTES = 512 * 1024 * 1024
MAXIMUM_DECIMAL_TEXT_LENGTH = 60
VALUE_MAP_HEADER = ["sourceValue", "targetKey"]
REASON_ORDER = (
    "MALFORMED_ROW",
    "MISSING_SOURCE_ID",
    "DUPLICATE_SOURCE_ID",
    "MISSING_SOURCE_VERSION",
    "UNTRIMMED_KEY",
    "MISSING_REQUIRED_VALUE",
    "UNKNOWN_LIFECYCLE_VALUE",
    "UNMAPPED_VALUE",
    "AMBIGUOUS_MAPPING",
    "UNRESOLVED_REFERENCE",
    "REFERENCED_ROW_QUARANTINED",
    "INVALID_DECIMAL",
    "SCALE_EXCEEDED",
)
ACTION_ORDER = ("UPSERT", "DEACTIVATE", "CANCEL", "NO_CHANGE", "QUARANTINE")
CHANGE_ORDER = ("NEW", "CHANGED", "UNCHANGED", "ABSENT")
LIFECYCLE_ACTIONS = {"ACTIVE": "UPSERT", "INACTIVE": "DEACTIVATE", "CANCELLED": "CANCEL"}
DECIMAL_PATTERNS = {
    ".": re.compile(r"^-?[0-9]+(?:\.[0-9]+)?$"),
    ",": re.compile(r"^-?[0-9]+(?:,[0-9]+)?$"),
}
# Exact arithmetic only: any rounding, inexact result or overflow raises instead of
# silently changing a reconciliation total.
EXACT = decimal.Context(
    prec=200,
    rounding=decimal.ROUND_HALF_EVEN,
    traps=[decimal.Inexact, decimal.Rounded, decimal.InvalidOperation, decimal.Overflow, decimal.DivisionByZero],
)


class DryRunViolation(ValueError):
    """An input is not a truthful, exactly bound legacy export or mapping profile."""


class InputUnavailable(OSError):
    """An input file is missing, a link, not regular or too large."""


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise DryRunViolation(message)


def sha256_hex(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def canonical_bytes(value: Any) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"),
                       allow_nan=False) + "\n").encode("utf-8")


def _pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        _require(key not in result, "duplicate_json_key")
        result[key] = value
    return result


def _unsupported_number(_: str) -> Any:
    raise DryRunViolation("unsupported_json_number")


def strict_json_bytes(content: bytes) -> Any:
    _require(type(content) is bytes and len(content) <= MAXIMUM_DOCUMENT_BYTES, "json_size_or_type")
    try:
        document = json.loads(content.decode("utf-8"), object_pairs_hook=_pairs,
                              parse_float=_unsupported_number, parse_constant=_unsupported_number)
    except DryRunViolation:
        raise
    except (UnicodeError, ValueError, RecursionError):
        raise DryRunViolation("invalid_json") from None
    pending = [(document, 0)]
    visited = 0
    while pending:
        value, depth = pending.pop()
        visited += 1
        _require(depth <= 64 and visited <= 500000, "json_complexity")
        if isinstance(value, (dict, list)):
            _require(len(value) <= 100000, "json_complexity")
            pending.extend((child, depth + 1) for child in (value.values() if isinstance(value, dict) else value))
    return document


@lru_cache(maxsize=1)
def _engine() -> ModuleType:
    path = REPOSITORY_ROOT / ENGINE_PATH
    content = _read_input_file(path, "frozen schema engine", MAXIMUM_DOCUMENT_BYTES).replace(b"\r\n", b"\n")
    _require(sha256_hex(content) == ENGINE_SHA256, "contract_engine_changed")
    module = ModuleType("_r041_frozen_recovery_schema_engine")
    module.__file__ = str(path)
    sys.modules[module.__name__] = module
    try:
        # Only the fixed, hash-verified source is compiled; no generator is called.
        exec(compile(content, str(path), "exec", dont_inherit=True), module.__dict__)
    except Exception:
        sys.modules.pop(module.__name__, None)
        raise DryRunViolation("contract_engine_unavailable") from None
    return module


def _validate(value: Any, schema: dict[str, Any]) -> None:
    # The frozen engine supports the existing project's schema subset. These two
    # new contracts also use nullable types and maximum; handle those explicitly
    # at every node, then delegate the remaining constraints without changing it.
    engine = _engine()
    _require(isinstance(schema, dict), "invalid_schema")
    _require(not set(schema) - (engine.SUPPORTED_SCHEMA_KEYWORDS | {"maximum"}), "unsupported_schema_keyword")
    expected = schema.get("type")
    types = expected if isinstance(expected, list) else [expected]
    if "object" in types:
        _require(schema.get("additionalProperties") is False, "open_schema_object")
    props = schema.get("properties", {})
    _require(isinstance(props, dict), "invalid_schema")
    # Validate even schemas below absent optional/null values, so unsupported or
    # open definitions cannot become a silently ignored contract branch.
    shallow = dict(schema)
    shallow.pop("maximum", None)
    if isinstance(expected, list):
        _require(expected and len(set(expected)) == len(expected), "invalid_schema_type")
        matches = [kind for kind in expected if (value is None if kind == "null" else engine._is_json_type(value, kind))]
        _require(bool(matches), "schema_type")
        shallow.pop("type", None)
    if isinstance(value, dict):
        shallow["properties"] = {key: {} for key in props}
    if isinstance(value, list) and "items" in shallow:
        shallow["items"] = {}
    try:
        engine._validate_against_schema(value, shallow)
    except engine.InventoryError:
        raise DryRunViolation("schema_violation") from None
    if type(value) is int and "maximum" in schema:
        _require(value <= schema["maximum"], "schema_maximum")
    if isinstance(value, str) and "pattern" in schema:
        _require(re.fullmatch(schema["pattern"], value) is not None, "schema_pattern")
    if isinstance(value, dict):
        for key, child in value.items():
            if key in props:
                _validate(child, props[key])
    if isinstance(value, list) and "items" in schema:
        for child in value:
            _validate(child, schema["items"])


def _check_schema(schema: Any) -> None:
    engine = _engine()
    _require(isinstance(schema, dict), "invalid_schema")
    _require(not set(schema) - (engine.SUPPORTED_SCHEMA_KEYWORDS | {"maximum"}), "unsupported_schema_keyword")
    types = schema.get("type")
    types = types if isinstance(types, list) else [types]
    _require(all(kind in {None, "null", "object", "array", "string", "integer", "boolean"} for kind in types),
             "unsupported_schema_type")
    if "object" in types:
        _require(schema.get("additionalProperties") is False, "open_schema_object")
    for child in schema.get("properties", {}).values():
        _check_schema(child)
    if "items" in schema:
        _check_schema(schema["items"])


def _no_links(path: Path) -> None:
    if str(path).startswith(("\\\\", "//")):
        raise InputUnavailable("remote_share_not_allowed")
    for candidate in (path, *path.absolute().parents):
        try:
            metadata = candidate.lstat()
        except OSError:
            raise InputUnavailable("input_unavailable") from None
        if stat.S_ISLNK(metadata.st_mode) or getattr(metadata, "st_file_attributes", 0) & 0x400:
            raise InputUnavailable("links_or_reparse_points_not_allowed")


def _read_input_file(path: Path, label: str, maximum_bytes: int) -> bytes:
    _no_links(path)
    try:
        metadata = path.lstat()
    except OSError as exc:
        raise InputUnavailable(f"{label} cannot be read: {path}") from exc
    if stat.S_ISLNK(metadata.st_mode) or not stat.S_ISREG(metadata.st_mode):
        raise InputUnavailable(f"{label} must be a regular file, not a link: {path}")
    if metadata.st_size > maximum_bytes:
        raise InputUnavailable(f"{label} exceeds {maximum_bytes} bytes: {path}")
    try:
        with path.open("rb") as handle:
            before = os.fstat(handle.fileno())
            content = handle.read(maximum_bytes + 1)
            after = os.fstat(handle.fileno())
        _require(len(content) <= maximum_bytes, "input_size_limit")
        _require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns)
                 == (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns)
                 == (metadata.st_dev, metadata.st_ino, metadata.st_size, metadata.st_mtime_ns), "input_changed")
        _require(len(content) == before.st_size, "input_changed")
        return content
    except OSError as exc:
        raise InputUnavailable(f"{label} cannot be read: {path}") from exc


def _schema_checked(content: bytes, schema: Any, label: str) -> dict[str, Any]:
    document = strict_json_bytes(content)
    _check_schema(schema)
    _validate(document, schema)
    return document


def _read_csv(content: bytes, label: str, delimiter: str) -> tuple[list[str], list[list[str]]]:
    # A spreadsheet export may start with a UTF-8 BOM; the hash is always over the raw bytes.
    if content.startswith(b"\xef\xbb\xbf"):
        content = content[3:]
    try:
        text = content.decode("utf-8", errors="strict")
    except UnicodeDecodeError as exc:
        raise DryRunViolation(f"{label} is not valid UTF-8") from exc
    _require("\x00" not in text, f"{label} contains a NUL character")
    reader = csv.reader(io.StringIO(text, newline=""), delimiter=delimiter, quotechar='"', strict=True)
    try:
        records = list(reader)
    except csv.Error as exc:
        raise DryRunViolation(f"{label} is not valid CSV: {exc}") from exc
    _require(bool(records), f"{label} has no header row")
    header = records[0]
    _require(all(name != "" and name == name.strip() and len(name) <= 128
                 and not any(ord(char) < 32 or ord(char) == 127 for char in name) for name in header),
             f"{label} header has a blank, invalid or untrimmed name")
    _require(len(set(header)) == len(header), f"{label} header repeats a column name")
    return header, records[1:]


def _read_value_map(content: bytes, label: str) -> dict[str, frozenset[str]]:
    header, records = _read_csv(content, label, ",")
    _require(header == VALUE_MAP_HEADER, f"{label} header must be exactly sourceValue,targetKey")
    targets: dict[str, set[str]] = {}
    for number, record in enumerate(records, start=1):
        _require(len(record) == len(VALUE_MAP_HEADER), f"{label} record {number} does not have two fields")
        source_value, target_key = record
        _require(source_value != "" and source_value == source_value.strip(),
                 f"{label} record {number} has a blank or untrimmed sourceValue")
        _require(target_key != "" and target_key == target_key.strip(),
                 f"{label} record {number} has a blank or untrimmed targetKey")
        targets.setdefault(source_value, set()).add(target_key)
    return {source_value: frozenset(keys) for source_value, keys in targets.items()}


def _parse_decimal(text: str, separator: str) -> decimal.Decimal | None:
    if len(text) > MAXIMUM_DECIMAL_TEXT_LENGTH or DECIMAL_PATTERNS[separator].fullmatch(text) is None:
        return None
    return decimal.Decimal(text.replace(separator, ".") if separator == "," else text, context=EXACT)


def _significant_scale(text: str, separator: str) -> int:
    if separator not in text:
        return 0
    return len(text.split(separator, 1)[1].rstrip("0"))


def _decimal_text(value: decimal.Decimal) -> str:
    text = format(value, "f")
    if "." in text:
        text = text.rstrip("0").rstrip(".")
    return "0" if text in {"-0", ""} else text


def _check_manifest(manifest: dict[str, Any]) -> None:
    names = [extract["name"] for extract in manifest["extracts"]]
    files = [extract["fileName"] for extract in manifest["extracts"]]
    _require(len(set(names)) == len(names), "manifest extract names must be unique")
    _require(len({name.casefold() for name in files}) == len(files), "manifest extract file names must be unique")
    for extract in manifest["extracts"]:
        _require(not (extract["delimiter"] == "," and extract["decimalSeparator"] == ","),
                 f"extract {extract['name']} uses ',' as both delimiter and decimal separator")
    try:
        datetime.strptime(manifest["cutoffUtc"], INSTANT_FORMAT)
    except ValueError as exc:
        raise DryRunViolation("manifest cutoffUtc is not a UTC instant") from exc


def _check_profile(profile: dict[str, Any], manifest: dict[str, Any]) -> list[dict[str, Any]]:
    extract_names = {extract["name"] for extract in manifest["extracts"]}
    entities = profile["entities"]
    entity_names = [entity["entity"] for entity in entities]
    used_extracts = [entity["extract"] for entity in entities]
    _require(len(set(entity_names)) == len(entity_names), "profile entity names must be unique")
    _require(len(set(used_extracts)) == len(used_extracts), "an extract maps to one entity only")
    _require(set(used_extracts) <= extract_names, "profile names an extract the manifest does not list")
    map_names = [value_map["name"] for value_map in profile["valueMaps"]]
    map_files = [value_map["fileName"] for value_map in profile["valueMaps"]]
    _require(len(set(map_names)) == len(map_names), "profile value map names must be unique")
    _require(len({name.casefold() for name in map_files}) == len(map_files), "profile value map file names must be unique")
    for entity in entities:
        label = entity["entity"]
        for collection in ("valueColumns", "references", "measures"):
            columns = [item["column"] for item in entity[collection]]
            _require(len(columns) == len(set(columns)), f"{label} repeats a {collection} column")
        for value_column in entity["valueColumns"]:
            _require(value_column["valueMap"] in map_names, f"{label} names an unknown value map")
        if entity["lifecycle"] is not None:
            sources = [item["sourceValue"] for item in entity["lifecycle"]["values"]]
            _require(len(set(sources)) == len(sources), f"{label} lifecycle repeats a source value")
        for reference in entity["references"]:
            _require(reference["entity"] in entity_names, f"{label} references an unknown entity")
            _require(reference["entity"] != label, f"{label} references itself")
        for measure in entity["measures"]:
            column = measure["column"]
            _require((measure["unitCode"] is None) != (measure["unitColumn"] is None),
                     f"{label}.{column} needs exactly one of unitCode and unitColumn")
            if measure["unitColumn"] is None:
                _require(measure["unitValueMap"] is None, f"{label}.{column} has a unit map but no unit column")
            else:
                _require(measure["unitValueMap"] in map_names, f"{label}.{column} needs a known unit value map")
            scale_units = [scale["unitCode"] for scale in measure["scales"]]
            _require(len(set(scale_units)) == len(scale_units), f"{label}.{column} repeats a scale unit")
            for scale in measure["scales"]:
                text = scale["scaleSource"]
                _require(text.strip() != "" and text == text.strip(), "scaleSource must be nonblank and trimmed")
                _require(not any(ord(char) < 32 or ord(char) == 127 for char in text), "scaleSource contains controls")
    ordered: list[dict[str, Any]] = []
    placed: set[str] = set()
    while len(ordered) < len(entities):
        progressed = False
        for entity in entities:
            if entity["entity"] in placed:
                continue
            if {reference["entity"] for reference in entity["references"]} <= placed:
                ordered.append(entity)
                placed.add(entity["entity"])
                progressed = True
        _require(progressed, "profile entity references form a cycle")
    return ordered


def _entity_columns(entity: dict[str, Any]) -> set[str]:
    columns = {entity["sourceIdColumn"], *entity["requiredColumns"]}
    if entity["sourceVersionColumn"] is not None:
        columns.add(entity["sourceVersionColumn"])
    if entity["lifecycle"] is not None:
        columns.add(entity["lifecycle"]["column"])
    columns.update(value_column["column"] for value_column in entity["valueColumns"])
    columns.update(reference["column"] for reference in entity["references"])
    for measure in entity["measures"]:
        columns.add(measure["column"])
        if measure["unitColumn"] is not None:
            columns.add(measure["unitColumn"])
    return columns


def _synthetic_only(manifest: dict[str, Any], synthetic_fixture: bool,
                    register_bytes: bytes | None, register_schema: Any | None) -> None:
    # No accepted E03.1 validator, schema or owner register exists at main b6ed7ca5.
    # Installing a file later cannot silently activate real data access: this
    # source needs a separately reviewed integration before this branch can open.
    _require(synthetic_fixture and manifest["sourceClass"] == "SYNTHETIC_FIXTURE",
             "INPUT_DEPENDENCY_UNAVAILABLE:E03.1")
    _require(register_bytes is None and register_schema is None, "synthetic_fixture_takes_no_owner_register")
    _require(manifest["dataOwnerRegisterSha256"] is None, "synthetic_fixture_names_no_owner_register")


def _fields(value: Any, keys: str) -> None:
    _require(type(value) is dict and set(value) == set(keys.split()), "previous_report_shape")


def _digest(value: Any) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def _check_previous(previous: Any) -> None:
    _fields(previous, "contractVersion writesErp productionReady sourceClass source mapping dataOwnerRegisterSha256 "
            "previousReportSha256 readiness notReadyReasons entities unprofiledExtracts")
    _require(previous["contractVersion"] == REPORT_VERSION and previous["writesErp"] is False
             and previous["productionReady"] is False and previous["sourceClass"] == "SYNTHETIC_FIXTURE"
             and previous["readiness"] == "NOT_READY" and previous["dataOwnerRegisterSha256"] is None,
             "previous_report_fence")
    reasons = previous["notReadyReasons"]
    _require(type(reasons) is list and all(type(x) is str for x in reasons)
             and reasons == sorted(set(reasons)) and "SYNTHETIC_FIXTURE" in reasons, "previous_report_readiness")
    prior_hash = previous["previousReportSha256"]
    _require(prior_hash is None or _digest(prior_hash), "previous_report_hash")
    _fields(previous["source"], "sourceSystem snapshotId sourceVersion cutoffUtc manifestSha256")
    _require(all(type(x) is str and x.strip() for x in previous["source"].values()), "previous_report_source")
    _require(_digest(previous["source"]["manifestSha256"]), "previous_report_hash")
    _require(re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z",
                          previous["source"]["cutoffUtc"]) is not None, "previous_report_cutoff")
    datetime.strptime(previous["source"]["cutoffUtc"], INSTANT_FORMAT)
    _fields(previous["mapping"], "mappingId mappingVersion profileSha256 valueMaps")
    mapping = previous["mapping"]
    _require(type(mapping["mappingVersion"]) is int and mapping["mappingVersion"] > 0
             and _digest(mapping["profileSha256"]), "previous_report_mapping")
    _require(type(mapping["valueMaps"]) is list, "previous_report_mapping")
    map_names = []
    for item in mapping["valueMaps"]:
        _fields(item, "name sha256")
        _require(type(item["name"]) is str and _digest(item["sha256"]), "previous_report_mapping")
        map_names.append(item["name"])
    _require(len(map_names) == len(set(map_names)), "previous_report_duplicate_map")
    _require(type(previous["entities"]) is list and type(previous["unprofiledExtracts"]) is list, "previous_report_entities")
    names: set[str] = set()
    for entity in previous["entities"]:
        _fields(entity, "entity extract ownerModule dataGate sourceIdColumn sourceVersionColumn rowCount actions "
                "quarantineReasons changes absentSourceIds measureTotals rows")
        _require(type(entity["entity"]) is str and entity["entity"] not in names, "previous_report_duplicate_entity")
        names.add(entity["entity"])
        _require(type(entity["rows"]) is list and type(entity["rowCount"]) is int
                 and entity["rowCount"] == len(entity["rows"]), "previous_report_row_count")
        actions = dict.fromkeys(ACTION_ORDER, 0)
        reasons_count = dict.fromkeys(REASON_ORDER, 0)
        changes = dict.fromkeys(CHANGE_ORDER, 0)
        ids: dict[str, list[dict[str, Any]]] = {}
        for index, row in enumerate(entity["rows"], start=1):
            _fields(row, "locator sourceId mappedAction action reasons rowSha256 change")
            _require(row["locator"] == f"{entity['extract']}:{index}" and _digest(row["rowSha256"]), "previous_report_row")
            _require(row["sourceId"] is None or (type(row["sourceId"]) is str and bool(row["sourceId"].strip())),
                     "previous_report_source_id")
            reasons_row = row["reasons"]
            _require(type(reasons_row) is list and all(type(x) is str for x in reasons_row)
                     and reasons_row == [x for x in REASON_ORDER if x in reasons_row], "previous_report_reasons")
            _require(row["mappedAction"] == "QUARANTINE" if reasons_row else row["mappedAction"] in LIFECYCLE_ACTIONS.values(),
                     "previous_report_action")
            _require(row["action"] == row["mappedAction"] or (row["action"] == "NO_CHANGE"
                     and not reasons_row and row["change"] == "UNCHANGED"), "previous_report_action")
            actions[row["action"]] += 1
            for reason in reasons_row:
                reasons_count[reason] += 1
            _require(row["change"] is None if prior_hash is None else row["change"] in {None, "NEW", "CHANGED", "UNCHANGED"},
                     "previous_report_change")
            if row["change"] is not None:
                changes[row["change"]] += 1
            if row["sourceId"] is not None:
                ids.setdefault(row["sourceId"], []).append(row)
        for group in ids.values():
            _require(all(("DUPLICATE_SOURCE_ID" in row["reasons"]) == (len(group) > 1) for row in group),
                     "previous_report_duplicate_source_id")
        _require(type(entity["actions"]) is dict and type(entity["quarantineReasons"]) is dict
                 and all(type(x) is int for x in [*entity["actions"].values(), *entity["quarantineReasons"].values()])
                 and entity["actions"] == actions and entity["quarantineReasons"] == reasons_count, "previous_report_counts")
        absent = entity["absentSourceIds"]
        _require(type(absent) is list and all(type(x) is str for x in absent)
                 and absent == sorted(set(absent)) and not set(absent) & set(ids), "previous_report_absent")
        changes["ABSENT"] = len(absent)
        _require(entity["changes"] == (None if prior_hash is None else changes), "previous_report_changes")
        if entity["changes"] is not None:
            _require(type(entity["changes"]) is dict and all(type(x) is int for x in entity["changes"].values()),
                     "previous_report_changes")
        _require(not absent or f"ABSENT_ROWS_NEED_DECISION:{entity['entity']}" in previous["notReadyReasons"],
                 "previous_report_readiness")
        _require(not actions["QUARANTINE"] or f"QUARANTINE:{entity['entity']}" in previous["notReadyReasons"],
                 "previous_report_readiness")
        _require(type(entity["measureTotals"]) is list, "previous_report_totals")
        total_keys: set[tuple[str, str | None]] = set()
        for total in entity["measureTotals"]:
            _fields(total, "column unitCode kind rows all applicable quarantined")
            key = (total["column"], total["unitCode"])
            _require(key not in total_keys, "previous_report_totals")
            total_keys.add(key)
            numbers = [_parse_decimal(total[k], ".") for k in ("all", "applicable", "quarantined")]
            _require(all(x is not None for x in numbers) and EXACT.add(numbers[1], numbers[2]) == numbers[0],
                     "previous_report_totals")
    unprofiled: set[str] = set()
    for item in previous["unprofiledExtracts"]:
        _fields(item, "extract rowCount")
        _require(type(item["extract"]) is str and item["extract"] not in unprofiled
                 and type(item["rowCount"]) is int and item["rowCount"] >= 0, "previous_report_unprofiled")
        unprofiled.add(item["extract"])


def _previous_rows(previous: Any, manifest: dict[str, Any], profile: dict[str, Any]) -> dict[str, dict[str, tuple[str, str]]]:
    try:
        _check_previous(previous)
        _require(previous["contractVersion"] == REPORT_VERSION, "previous report is not a dry-run report")
        _require(previous["sourceClass"] == manifest["sourceClass"], "previous report has another source class")
        _require(previous["source"]["sourceSystem"] == manifest["sourceSystem"], "previous report is for another source")
        _require(previous["source"]["cutoffUtc"] < manifest["cutoffUtc"], "previous report must have an earlier cutoff")
        _require(previous["mapping"]["mappingId"] == profile["mappingId"], "previous report used another mapping")
        _require({entity["entity"] for entity in previous["entities"]}
                 == {entity["entity"] for entity in profile["entities"]}, "previous_report_entity_set_changed")
        bindings = {entity["entity"]: entity for entity in profile["entities"]}
        result: dict[str, dict[str, tuple[str, str]]] = {}
        for entity in previous["entities"]:
            _require(all(entity[key] == bindings[entity["entity"]][key] for key in
                         ("extract", "ownerModule", "dataGate", "sourceIdColumn", "sourceVersionColumn")),
                     "previous_report_identity_binding_changed")
            rows: dict[str, tuple[str, str]] = {}
            for row in entity["rows"]:
                if row["sourceId"] is not None and "DUPLICATE_SOURCE_ID" not in row["reasons"]:
                    rows[row["sourceId"]] = (row["rowSha256"], row["mappedAction"])
            result[entity["entity"]] = rows
        return result
    except (KeyError, TypeError, ValueError, decimal.DecimalException) as exc:
        if isinstance(exc, DryRunViolation):
            raise
        raise DryRunViolation("previous report is not a dry-run report") from exc


def _evaluate_entity(entity: dict[str, Any], extract: dict[str, Any], header: list[str], records: list[list[str]],
                     value_maps: dict[str, dict[str, frozenset[str]]],
                     resolved: dict[str, dict[str, bool]]) -> tuple[list[dict[str, Any]], set[tuple[str, str]]]:
    index = {name: position for position, name in enumerate(header)}
    separator = extract["decimalSeparator"]
    lifecycle = None
    if entity["lifecycle"] is not None:
        lifecycle = {item["sourceValue"]: item["state"] for item in entity["lifecycle"]["values"]}
    missing_policies: set[tuple[str, str]] = set()
    rows: list[dict[str, Any]] = []
    for number, record in enumerate(records, start=1):
        row: dict[str, Any] = {
            "locator": f"{extract['name']}:{number}",
            "sourceId": None,
            "reasons": set(),
            "rowSha256": sha256_hex(canonical_bytes({"columns": header, "values": record})),
            "lifecycleAction": "UPSERT",
            "measures": [],
        }
        rows.append(row)
        if len(record) != len(header):
            row["reasons"].add("MALFORMED_ROW")
            continue

        def value(column: str) -> str:
            # Source identities and lookups are exact; never synthesize a key by
            # trimming, changing case or normalizing Unicode.
            return record[index[column]]

        reasons: set[str] = row["reasons"]
        source_id = value(entity["sourceIdColumn"])
        if source_id.strip() == "":
            reasons.add("MISSING_SOURCE_ID")
        else:
            row["sourceId"] = source_id
            if source_id != source_id.strip():
                reasons.add("UNTRIMMED_KEY")
        if entity["sourceVersionColumn"] is not None and value(entity["sourceVersionColumn"]).strip() == "":
            reasons.add("MISSING_SOURCE_VERSION")
        if any(value(column).strip() == "" for column in entity["requiredColumns"]):
            reasons.add("MISSING_REQUIRED_VALUE")
        if lifecycle is not None:
            state = lifecycle.get(value(entity["lifecycle"]["column"]))
            if state is None:
                reasons.add("UNKNOWN_LIFECYCLE_VALUE")
            else:
                row["lifecycleAction"] = LIFECYCLE_ACTIONS[state]
        for value_column in entity["valueColumns"]:
            text = value(value_column["column"])
            if text == "":
                if value_column["required"]:
                    reasons.add("MISSING_REQUIRED_VALUE")
                continue
            targets = value_maps[value_column["valueMap"]].get(text)
            if not targets:
                reasons.add("UNMAPPED_VALUE")
            elif len(targets) > 1:
                reasons.add("AMBIGUOUS_MAPPING")
        for reference in entity["references"]:
            text = value(reference["column"])
            if text == "":
                if reference["required"]:
                    reasons.add("MISSING_REQUIRED_VALUE")
                continue
            target_quarantined = resolved[reference["entity"]].get(text)
            if target_quarantined is None:
                reasons.add("UNRESOLVED_REFERENCE")
            elif target_quarantined:
                reasons.add("REFERENCED_ROW_QUARANTINED")
        for measure in entity["measures"]:
            text = value(measure["column"])
            if text == "":
                if measure["required"]:
                    reasons.add("MISSING_REQUIRED_VALUE")
                continue
            unit = measure["unitCode"]
            if measure["unitColumn"] is not None:
                unit = None
                unit_text = value(measure["unitColumn"])
                if unit_text == "":
                    reasons.add("MISSING_REQUIRED_VALUE")
                else:
                    targets = value_maps[measure["unitValueMap"]].get(unit_text)
                    if not targets:
                        reasons.add("UNMAPPED_VALUE")
                    elif len(targets) > 1:
                        reasons.add("AMBIGUOUS_MAPPING")
                    else:
                        unit = next(iter(targets))
                        if re.fullmatch(r"[A-Za-z][A-Za-z0-9._-]{0,31}", unit) is None:
                            reasons.add("UNMAPPED_VALUE")
                            unit = None
            amount = _parse_decimal(text, separator)
            if amount is None:
                reasons.add("INVALID_DECIMAL")
                continue
            if unit is not None:
                scales = {scale["unitCode"]: scale["approvedScale"] for scale in measure["scales"]}
                approved = scales.get(unit)
                if approved is None:
                    missing_policies.add((measure["column"], unit))
                elif _significant_scale(text, separator) > approved:
                    reasons.add("SCALE_EXCEEDED")
            row["measures"].append((measure["column"], measure["kind"], unit, amount))

    counts: dict[str, int] = {}
    for row in rows:
        if row["sourceId"] is not None:
            counts[row["sourceId"]] = counts.get(row["sourceId"], 0) + 1
    for row in rows:
        if row["sourceId"] is not None and counts[row["sourceId"]] > 1:
            row["reasons"].add("DUPLICATE_SOURCE_ID")
    return rows, missing_policies


def _entity_report(entity: dict[str, Any], rows: list[dict[str, Any]],
                   previous: dict[str, tuple[str, str]] | None, *, same_mapping: bool) -> dict[str, Any]:
    actions = dict.fromkeys(ACTION_ORDER, 0)
    reasons = dict.fromkeys(REASON_ORDER, 0)
    changes = dict.fromkeys(CHANGE_ORDER, 0) if previous is not None else None
    totals: dict[tuple[str, str | None], dict[str, Any]] = {}
    report_rows: list[dict[str, Any]] = []
    for row in rows:
        quarantined = bool(row["reasons"])
        mapped_action = "QUARANTINE" if quarantined else row["lifecycleAction"]
        action = mapped_action
        change = None
        duplicate = "DUPLICATE_SOURCE_ID" in row["reasons"]
        if previous is not None and row["sourceId"] is not None and not duplicate:
            earlier = previous.get(row["sourceId"])
            if earlier is None:
                change = "NEW"
            elif earlier[0] == row["rowSha256"]:
                change = "UNCHANGED"
                if same_mapping and not quarantined and earlier[1] == mapped_action:
                    action = "NO_CHANGE"
            else:
                change = "CHANGED"
            changes[change] += 1
        actions[action] += 1
        for reason in row["reasons"]:
            reasons[reason] += 1
        for column, kind, unit, amount in row["measures"]:
            total = totals.setdefault((column, unit), {
                "kind": kind,
                "all": decimal.Decimal(0),
                "applicable": decimal.Decimal(0),
                "quarantined": decimal.Decimal(0),
                "rows": 0,
            })
            total["all"] = EXACT.add(total["all"], amount)
            bucket = "quarantined" if quarantined else "applicable"
            total[bucket] = EXACT.add(total[bucket], amount)
            total["rows"] += 1
        report_rows.append({
            "locator": row["locator"],
            "sourceId": row["sourceId"],
            "mappedAction": mapped_action,
            "action": action,
            "reasons": [reason for reason in REASON_ORDER if reason in row["reasons"]],
            "rowSha256": row["rowSha256"],
            "change": change,
        })
    absent: list[str] = []
    if previous is not None:
        current_ids = {row["sourceId"] for row in rows if row["sourceId"] is not None}
        absent = sorted(source_id for source_id in previous if source_id not in current_ids)
        changes["ABSENT"] = len(absent)
    return {
        "entity": entity["entity"],
        "extract": entity["extract"],
        "ownerModule": entity["ownerModule"],
        "dataGate": entity["dataGate"],
        "sourceIdColumn": entity["sourceIdColumn"],
        "sourceVersionColumn": entity["sourceVersionColumn"],
        "rowCount": len(rows),
        "actions": actions,
        "quarantineReasons": reasons,
        "changes": changes,
        "absentSourceIds": absent,
        "measureTotals": [
            {
                "column": column,
                "unitCode": unit,
                "kind": total["kind"],
                "rows": total["rows"],
                "all": _decimal_text(total["all"]),
                "applicable": _decimal_text(total["applicable"]),
                "quarantined": _decimal_text(total["quarantined"]),
            }
            for (column, unit), total in sorted(totals.items(), key=lambda item: (item[0][0], item[0][1] or ""))
        ],
        "rows": report_rows,
    }


def run_dry_run(
    *,
    manifest_bytes: bytes,
    profile_bytes: bytes,
    manifest_schema: Any,
    profile_schema: Any,
    extract_root: Path,
    value_map_root: Path,
    synthetic_fixture: bool,
    register_bytes: bytes | None,
    register_schema: Any | None,
    previous_report_bytes: bytes | None,
) -> dict[str, Any]:
    manifest = _schema_checked(manifest_bytes, manifest_schema, "legacy source manifest")
    profile = _schema_checked(profile_bytes, profile_schema, "legacy mapping profile")
    _check_manifest(manifest)
    ordered_entities = _check_profile(profile, manifest)
    # This fence precedes even value-map and previous-report parsing/reads.
    _synthetic_only(manifest, synthetic_fixture, register_bytes, register_schema)
    previous = None
    previous_sha256 = None
    same_mapping = False
    if previous_report_bytes is not None:
        try:
            previous_document = strict_json_bytes(previous_report_bytes)
        except DryRunViolation as exc:
            raise DryRunViolation(str(exc)) from exc
        previous = _previous_rows(previous_document, manifest, profile)
        same_mapping = previous_document["mapping"]["profileSha256"] == sha256_hex(profile_bytes)
        _require(profile["mappingVersion"] >= previous_document["mapping"]["mappingVersion"], "mapping_version_regression")
        _require(same_mapping or profile["mappingVersion"] > previous_document["mapping"]["mappingVersion"],
                 "mapping_changed_without_version_increment")
        previous_sha256 = sha256_hex(previous_report_bytes)

    value_maps: dict[str, dict[str, frozenset[str]]] = {}
    for value_map in profile["valueMaps"]:
        label = f"value map {value_map['name']}"
        content = _read_input_file(value_map_root / value_map["fileName"], label, MAXIMUM_DOCUMENT_BYTES)
        _require(sha256_hex(content) == value_map["sha256"], f"{label} bytes do not match the profile hash")
        value_maps[value_map["name"]] = _read_value_map(content, label)

    extracts: dict[str, tuple[dict[str, Any], list[str], list[list[str]]]] = {}
    for extract in manifest["extracts"]:
        label = f"extract {extract['name']}"
        content = _read_input_file(extract_root / extract["fileName"], label, MAXIMUM_EXTRACT_BYTES)
        _require(sha256_hex(content) == extract["sha256"], f"{label} bytes do not match the manifest hash")
        header, records = _read_csv(content, label, extract["delimiter"])
        _require(len(records) == extract["rowCount"],
                 f"{label} has {len(records)} records but the manifest says {extract['rowCount']}")
        extracts[extract["name"]] = (extract, header, records)

    evaluated: dict[str, list[dict[str, Any]]] = {}
    resolved: dict[str, dict[str, bool]] = {}
    not_ready: set[str] = set()
    for entity in ordered_entities:
        extract, header, records = extracts[entity["extract"]]
        missing_columns = sorted(_entity_columns(entity) - set(header))
        _require(not missing_columns, f"{entity['entity']} needs columns the extract lacks: {missing_columns}")
        rows, missing_policies = _evaluate_entity(entity, extract, header, records, value_maps, resolved)
        for measure in entity["measures"]:
            known = {scale["unitCode"] for scale in measure["scales"]}
            if measure["unitCode"] is not None and measure["unitCode"] not in known:
                missing_policies.add((measure["column"], measure["unitCode"]))
            elif measure["unitColumn"] is not None and not known:
                not_ready.add(f"SCALE_POLICY_MISSING:{entity['entity']}.{measure['column']}")
        evaluated[entity["entity"]] = rows
        by_id: dict[str, bool] = {}
        for row in rows:
            if row["sourceId"] is not None:
                by_id[row["sourceId"]] = by_id.get(row["sourceId"], False) or bool(row["reasons"])
        resolved[entity["entity"]] = by_id
        for column, unit in missing_policies:
            not_ready.add(f"SCALE_POLICY_MISSING:{entity['entity']}.{column}:{unit}")

    entity_reports = []
    for entity in profile["entities"]:
        entity_previous = None if previous is None else previous.get(entity["entity"], {})
        try:
            report = _entity_report(entity, evaluated[entity["entity"]], entity_previous, same_mapping=same_mapping)
        except decimal.DecimalException:
            raise DryRunViolation("exact_decimal_reconciliation_failed") from None
        if report["actions"]["QUARANTINE"]:
            not_ready.add(f"QUARANTINE:{entity['entity']}")
        if report["absentSourceIds"]:
            not_ready.add(f"ABSENT_ROWS_NEED_DECISION:{entity['entity']}")
        entity_reports.append(report)
    profiled = {entity["extract"] for entity in profile["entities"]}
    unprofiled = [
        {"extract": name, "rowCount": len(records)}
        for name, (_, _, records) in sorted(extracts.items())
        if name not in profiled
    ]
    for item in unprofiled:
        not_ready.add(f"EXTRACT_NOT_PROFILED:{item['extract']}")
    if synthetic_fixture:
        not_ready.add("SYNTHETIC_FIXTURE")

    report = {
        "contractVersion": REPORT_VERSION,
        "writesErp": False,
        "productionReady": False,
        "sourceClass": manifest["sourceClass"],
        "source": {
            "sourceSystem": manifest["sourceSystem"],
            "snapshotId": manifest["snapshotId"],
            "sourceVersion": manifest["sourceVersion"],
            "cutoffUtc": manifest["cutoffUtc"],
            "manifestSha256": sha256_hex(manifest_bytes),
        },
        "mapping": {
            "mappingId": profile["mappingId"],
            "mappingVersion": profile["mappingVersion"],
            "profileSha256": sha256_hex(profile_bytes),
            "valueMaps": [{"name": item["name"], "sha256": item["sha256"]} for item in profile["valueMaps"]],
        },
        "dataOwnerRegisterSha256": None,
        "previousReportSha256": previous_sha256,
        "readiness": "NOT_READY",
        "notReadyReasons": sorted(not_ready),
        "entities": entity_reports,
        "unprofiledExtracts": unprofiled,
    }
    # Every emitted report must fit the same parser limits as --previous-report.
    # Reject before opening an output artifact, rather than emit an unusable delta.
    strict_json_bytes(canonical_bytes(report))
    return report


def _inside(path: Path, root: Path) -> bool:
    try:
        path.resolve().relative_to(root)
    except ValueError:
        return False
    return True


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=REPOSITORY_ROOT)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--extract-root", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--value-map-root", type=Path, required=True)
    owner = parser.add_mutually_exclusive_group(required=True)
    owner.add_argument("--data-owner-register", type=Path)
    owner.add_argument("--synthetic-fixture", action="store_true")
    parser.add_argument("--previous-report", type=Path, default=None)
    parser.add_argument("--output", type=Path, required=True)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _parser().parse_args(argv)
    # Fence before reading any manifest, profile, owner register or data path.
    if not arguments.synthetic_fixture:
        print("legacy mapping dry-run: INPUT_DEPENDENCY_UNAVAILABLE:E03.1", file=sys.stderr)
        return 1
    try:
        _require(arguments.repository_root.resolve() == REPOSITORY_ROOT, "repository_root_mismatch")
        _no_links(arguments.output.absolute().parent)
        if _inside(arguments.output, REPOSITORY_ROOT) or any(
                (parent / ".git").exists() for parent in arguments.output.absolute().parents):
            print("legacy mapping dry-run: report is never written inside the repository", file=sys.stderr)
            return 2
        manifest_schema = strict_json_bytes(_read_input_file(REPOSITORY_ROOT / MANIFEST_SCHEMA_PATH,
                                                            "manifest schema", MAXIMUM_DOCUMENT_BYTES))
        profile_schema = strict_json_bytes(_read_input_file(REPOSITORY_ROOT / PROFILE_SCHEMA_PATH,
                                                           "profile schema", MAXIMUM_DOCUMENT_BYTES))
        manifest_bytes = _read_input_file(arguments.manifest, "manifest", MAXIMUM_DOCUMENT_BYTES)
        profile_bytes = _read_input_file(arguments.profile, "profile", MAXIMUM_DOCUMENT_BYTES)
        manifest = _schema_checked(manifest_bytes, manifest_schema, "manifest")
        _synthetic_only(manifest, True, None, None)
        previous_bytes = None
        if arguments.previous_report is not None:
            previous_bytes = _read_input_file(arguments.previous_report, "previous report", MAXIMUM_DOCUMENT_BYTES)
        report = run_dry_run(
            manifest_bytes=manifest_bytes,
            profile_bytes=profile_bytes,
            manifest_schema=manifest_schema,
            profile_schema=profile_schema,
            extract_root=arguments.extract_root,
            value_map_root=arguments.value_map_root,
            synthetic_fixture=arguments.synthetic_fixture,
            register_bytes=None,
            register_schema=None,
            previous_report_bytes=previous_bytes,
        )
    except (InputUnavailable, OSError, RuntimeError):
        print("legacy mapping dry-run: input_unavailable", file=sys.stderr)
        return 2
    except (DryRunViolation, decimal.DecimalException):
        # No supplied source identity, path, header, or value is echoed to logs.
        print("legacy mapping dry-run: input_contract_violation", file=sys.stderr)
        return 1
    content = canonical_bytes(report)
    try:
        with arguments.output.open("xb") as handle:
            handle.write(content)
    except FileExistsError:
        print("legacy mapping dry-run: report is never overwritten", file=sys.stderr)
        return 2
    except OSError:
        print("legacy mapping dry-run: report_output_unavailable", file=sys.stderr)
        return 2
    summary = {
        "readiness": report["readiness"],
        "notReadyReasons": report["notReadyReasons"],
        "entities": [
            {"entity": item["entity"], "rowCount": item["rowCount"], "quarantined": item["actions"]["QUARANTINE"]}
            for item in report["entities"]
        ],
        "reportSha256": sha256_hex(content),
        "writesErp": False,
    }
    sys.stdout.write(json.dumps(summary, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
