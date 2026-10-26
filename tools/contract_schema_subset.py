"""Shared, dependency-free strict JSON reader and JSON Schema subset for evidence contracts.

The keywords are the ones tools/generate_recovery_surface_inventory.py accepts, plus
`maximum` and a `type` list (so a field can be an explicit `null`).  Any other keyword
is refused, so a schema can never rely on a rule the validator silently ignores.
Validators in tools/ load this file by compiling its exact source bytes, never through
an import that could pick up stale bytecode.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import stat
from typing import Any


SUPPORTED_KEYWORDS = frozenset(
    {
        "$schema",
        "$id",
        "title",
        "description",
        "type",
        "additionalProperties",
        "required",
        "properties",
        "const",
        "enum",
        "pattern",
        "minLength",
        "maxLength",
        "minimum",
        "maximum",
        "minItems",
        "maxItems",
        "uniqueItems",
        "items",
    }
)
SUPPORTED_TYPES = frozenset({"object", "array", "string", "integer", "boolean", "null"})
DEFAULT_MAXIMUM_BYTES = 16 * 1024 * 1024
MAXIMUM_DEPTH = 64
MAXIMUM_NODES = 500000
MAXIMUM_CONTAINER_ITEMS = 100000


class StrictJsonError(ValueError):
    """The bytes are not one strict UTF-8 JSON document."""


class SchemaViolation(ValueError):
    """The document does not satisfy its schema (or the schema is outside the subset)."""


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if any(0xD800 <= ord(char) <= 0xDFFF for char in key):
            raise StrictJsonError("JSON object key contains an unpaired Unicode surrogate")
        if key in result:
            raise StrictJsonError("duplicate JSON object key")
        result[key] = value
    return result


def _reject_constant(value: str) -> None:
    raise StrictJsonError("unsupported JSON number; evidence contracts use integers or decimal strings")


def load_strict_json_bytes(content: bytes, label: str) -> Any:
    if type(content) is not bytes or len(content) > DEFAULT_MAXIMUM_BYTES:
        raise StrictJsonError("JSON size or type is invalid")
    if content.startswith(b"\xef\xbb\xbf"):
        raise StrictJsonError(f"{label} must not start with a UTF-8 BOM")
    try:
        text = content.decode("utf-8", errors="strict")
    except UnicodeDecodeError as exc:
        raise StrictJsonError(f"{label} is not valid UTF-8") from exc
    try:
        document = json.loads(text, object_pairs_hook=_reject_duplicate_keys,
                              parse_constant=_reject_constant, parse_float=_reject_constant)
    except json.JSONDecodeError as exc:
        raise StrictJsonError(f"{label} is not valid JSON at line {exc.lineno} column {exc.colno}") from exc
    except RecursionError as exc:
        raise StrictJsonError(f"{label} nests deeper than the parser allows") from exc
    except StrictJsonError:
        raise
    except ValueError:
        raise StrictJsonError("JSON number or value is invalid") from None
    pending = [(document, 0)]
    visited = 0
    while pending:
        child, depth = pending.pop()
        visited += 1
        if depth > MAXIMUM_DEPTH or visited > MAXIMUM_NODES:
            raise StrictJsonError("JSON complexity limit exceeded")
        if isinstance(child, str) and any(0xD800 <= ord(char) <= 0xDFFF for char in child):
            raise StrictJsonError("JSON contains an unpaired Unicode surrogate")
        if isinstance(child, (list, dict)):
            if len(child) > MAXIMUM_CONTAINER_ITEMS:
                raise StrictJsonError("JSON container limit exceeded")
            pending.extend((value, depth + 1) for value in (child.values() if isinstance(child, dict) else child))
    return document


def load_strict_json(path: Path, label: str, maximum_bytes: int = DEFAULT_MAXIMUM_BYTES) -> Any:
    if type(maximum_bytes) is not int or maximum_bytes < 0 or maximum_bytes > DEFAULT_MAXIMUM_BYTES:
        raise StrictJsonError("invalid JSON byte limit")
    if str(path).startswith(("\\\\", "//")):
        raise StrictJsonError("remote shares are not local evidence files")
    for ancestor in path.absolute().parents:
        try:
            info = ancestor.lstat()
        except OSError:
            raise StrictJsonError("input ancestor unavailable") from None
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise StrictJsonError("input ancestor is a link or reparse point")
    try:
        metadata = path.lstat()
    except OSError as exc:
        raise StrictJsonError(f"{label} cannot be read: {path}") from exc
    if (stat.S_ISLNK(metadata.st_mode) or not stat.S_ISREG(metadata.st_mode)
            or getattr(metadata, "st_file_attributes", 0) & 0x400):
        raise StrictJsonError(f"{label} must be a regular file, not a link: {path}")
    if metadata.st_size > maximum_bytes:
        raise StrictJsonError(f"{label} exceeds {maximum_bytes} bytes")
    try:
        with path.open("rb") as handle:
            before = os.fstat(handle.fileno())
            content = handle.read(maximum_bytes + 1)
            after = os.fstat(handle.fileno())
        signature = lambda info: (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns)
        if len(content) > maximum_bytes or len(content) != before.st_size or not (
                signature(before) == signature(after) == signature(metadata)):
            raise StrictJsonError("input changed or exceeds the byte limit")
    except OSError as exc:
        raise StrictJsonError(f"{label} cannot be read: {path}") from exc
    return load_strict_json_bytes(content, label)


def json_kind(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, int):
        return "integer"
    if isinstance(value, float):
        return "number"
    if isinstance(value, str):
        return "string"
    if isinstance(value, list):
        return "array"
    if isinstance(value, dict):
        return "object"
    return "unsupported"


def json_equal(left: Any, right: Any) -> bool:
    """JSON value equality: `true` is never `1`, and objects compare key by key."""

    if json_kind(left) != json_kind(right):
        return False
    if isinstance(left, list):
        return len(left) == len(right) and all(json_equal(a, b) for a, b in zip(left, right, strict=True))
    if isinstance(left, dict):
        return left.keys() == right.keys() and all(json_equal(left[key], right[key]) for key in left)
    return bool(left == right)


def _type_matches(value: Any, expected: Any, location: str) -> bool:
    names = expected if isinstance(expected, list) else [expected]
    if not names or not all(isinstance(name, str) and name in SUPPORTED_TYPES for name in names):
        raise SchemaViolation(f"schema has an unsupported type at {location}")
    return json_kind(value) in names


def _check_schema(schema: Any, location: str, *, closed: bool) -> None:
    if not isinstance(schema, dict):
        raise SchemaViolation(f"schema node is not an object at {location}")
    if set(schema) - SUPPORTED_KEYWORDS:
        raise SchemaViolation(f"schema uses unsupported keywords at {location}")
    if "type" in schema:
        names = schema["type"] if isinstance(schema["type"], list) else [schema["type"]]
        if (not names or not all(type(name) is str and name in SUPPORTED_TYPES for name in names)
                or len(set(names)) != len(names)):
            raise SchemaViolation(f"schema has an unsupported type at {location}")
    else:
        names = []
    if "additionalProperties" in schema and type(schema["additionalProperties"]) is not bool:
        raise SchemaViolation(f"schema has invalid additionalProperties at {location}")
    if closed and ("object" in names or "properties" in schema) and schema.get("additionalProperties") is not False:
        raise SchemaViolation(f"schema object is open at {location}")
    properties = schema.get("properties", {})
    if type(properties) is not dict:
        raise SchemaViolation(f"schema has invalid properties at {location}")
    required = schema.get("required", [])
    if (type(required) is not list or not all(type(key) is str for key in required)
            or len(set(required)) != len(required) or not set(required) <= set(properties)):
        raise SchemaViolation(f"schema has invalid required at {location}")
    for key in ("minimum", "maximum", "minItems", "maxItems", "minLength", "maxLength"):
        if key in schema and (type(schema[key]) is not int or (key not in {"minimum", "maximum"} and schema[key] < 0)):
            raise SchemaViolation(f"schema has invalid numeric constraint at {location}")
    for lower, upper in (("minimum", "maximum"), ("minItems", "maxItems"), ("minLength", "maxLength")):
        if lower in schema and upper in schema and schema[lower] > schema[upper]:
            raise SchemaViolation(f"schema bounds are contradictory at {location}")
    if "uniqueItems" in schema and type(schema["uniqueItems"]) is not bool:
        raise SchemaViolation(f"schema has invalid uniqueItems at {location}")
    if "enum" in schema:
        values = schema["enum"]
        if type(values) is not list or not values or any(json_equal(item, old) for index, item in enumerate(values) for old in values[:index]):
            raise SchemaViolation(f"schema has invalid enum at {location}")
    if "pattern" in schema:
        if type(schema["pattern"]) is not str:
            raise SchemaViolation(f"schema has invalid pattern at {location}")
        try:
            re.compile(schema["pattern"])
        except re.error:
            raise SchemaViolation(f"schema has invalid pattern at {location}") from None
    for key, child in properties.items():
        _check_schema(child, f"{location}.{key}", closed=closed)
    if "items" in schema:
        _check_schema(schema["items"], f"{location}[]", closed=closed)


def validate(value: Any, schema: Any, location: str = "$") -> None:
    # Inspect every schema definition, including absent/nullable branches, before
    # validating values. No declared rule may be silently ignored.
    _check_schema(schema, location, closed=False)
    _validate(value, schema, location)


def _validate(value: Any, schema: Any, location: str = "$") -> None:
    if not isinstance(schema, dict):
        raise SchemaViolation(f"schema node is not an object at {location}")
    unsupported = sorted(set(schema) - SUPPORTED_KEYWORDS)
    if unsupported:
        raise SchemaViolation(f"schema uses unsupported keywords {unsupported} at {location}")
    if "const" in schema and not json_equal(value, schema["const"]):
        raise SchemaViolation(f"value violates const at {location}")
    if "enum" in schema and not any(json_equal(value, item) for item in schema["enum"]):
        raise SchemaViolation(f"value violates enum at {location}")
    if "type" in schema and not _type_matches(value, schema["type"], location):
        raise SchemaViolation(f"value has the wrong type at {location}")

    if isinstance(value, dict):
        required = schema.get("required", [])
        if not isinstance(required, list) or not all(isinstance(name, str) for name in required):
            raise SchemaViolation(f"schema has an invalid required list at {location}")
        missing = sorted(set(required) - set(value))
        if missing:
            raise SchemaViolation(f"missing fields {missing} at {location}")
        properties = schema.get("properties", {})
        if not isinstance(properties, dict):
            raise SchemaViolation(f"schema has invalid properties at {location}")
        if schema.get("additionalProperties") is False:
            extras = sorted(set(value) - set(properties))
            if extras:
                raise SchemaViolation(f"unexpected fields {extras} at {location}")
        for key, child in value.items():
            if key in properties:
                _validate(child, properties[key], f"{location}.{key}")

    if isinstance(value, list):
        minimum_items = schema.get("minItems")
        maximum_items = schema.get("maxItems")
        if minimum_items is not None and len(value) < minimum_items:
            raise SchemaViolation(f"too few items at {location}")
        if maximum_items is not None and len(value) > maximum_items:
            raise SchemaViolation(f"too many items at {location}")
        if schema.get("uniqueItems") is True:
            for index, item in enumerate(value):
                if any(json_equal(item, earlier) for earlier in value[:index]):
                    raise SchemaViolation(f"duplicate items at {location}")
        if "items" in schema:
            for index, item in enumerate(value):
                _validate(item, schema["items"], f"{location}[{index}]")

    if isinstance(value, str):
        if "minLength" in schema and len(value) < schema["minLength"]:
            raise SchemaViolation(f"string is too short at {location}")
        if "maxLength" in schema and len(value) > schema["maxLength"]:
            raise SchemaViolation(f"string is too long at {location}")
        if "pattern" in schema:
            try:
                matched = re.search(schema["pattern"], value)
            except re.error as exc:
                raise SchemaViolation(f"schema has an invalid pattern at {location}") from exc
            if matched is None:
                raise SchemaViolation(f"string violates pattern at {location}")

    if json_kind(value) == "integer":
        if "minimum" in schema and value < schema["minimum"]:
            raise SchemaViolation(f"integer is too small at {location}")
        if "maximum" in schema and value > schema["maximum"]:
            raise SchemaViolation(f"integer is too large at {location}")


def require_closed_objects(schema: Any, location: str = "$") -> None:
    """Every object node of a reviewed contract must forbid additional properties."""

    _check_schema(schema, location, closed=True)


def canonical_bytes(value: Any) -> bytes:
    return (
        json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n"
    ).encode("utf-8")


def sha256_hex(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()
