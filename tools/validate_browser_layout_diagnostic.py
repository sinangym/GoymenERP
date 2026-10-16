#!/usr/bin/env python3
"""Validate the closed, privacy-safe browser layout diagnostic envelope.

The record contains measurements and bounded aliases only. It deliberately has
no channel for DOM/text, URLs, storage, network payloads, images or credentials.
It is a contract validator; admitting the record to a workflow upload remains a
separate, reviewed browser-owner change.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
from typing import NoReturn


SCHEMA_VERSION = "browser-layout-diagnostic-v1"
MAX_INPUT_BYTES = 16_384
TOP_LEVEL_KEYS = frozenset({
    "schemaVersion",
    "suite",
    "caseId",
    "testedCommit",
    "selectorAlias",
    "boundaryClass",
    "viewport",
    "page",
    "target",
    "seed",
})
VIEWPORT_KEYS = frozenset({"widthCssPx", "heightCssPx"})
WIDTH_KEYS = frozenset({"clientWidthCssPx", "scrollWidthCssPx"})
TARGET_KEYS = WIDTH_KEYS | {"present", "visible"}
SEED_KEYS = frozenset({"fixtureId", "labelCharacterCount", "wideCharacterCount"})

COMMIT = re.compile(r"^[0-9a-f]{40}$")

# These names represent data channels, not substrings of safe aliases. The
# closed property sets below are the primary boundary; this list documents the
# categories which may never be added without a new reviewed contract version.
FORBIDDEN_CHANNELS = frozenset({
    "html",
    "text",
    "token",
    "body",
    "screenshot",
    "image",
    "cookie",
    "header",
    "storage",
    "url",
    "path",
    "request",
    "response",
    "value",
})


class ContractError(ValueError):
    """A diagnostic does not satisfy the closed privacy contract."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ContractError(message)


def _duplicate_refusal(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        require(key not in result, "diagnostic contains a duplicate property")
        result[key] = value
    return result


def _constant_refusal(_: str) -> NoReturn:
    raise ContractError("diagnostic contains a non-finite number")


def _object(value: object, keys: frozenset[str], label: str) -> dict[str, object]:
    require(isinstance(value, dict), f"{label} must be an object")
    require(set(value) == keys, f"{label} property set is not exact")
    return value


def _string(value: object, pattern: re.Pattern[str], label: str) -> str:
    require(isinstance(value, str) and pattern.fullmatch(value) is not None,
            f"{label} is not a bounded safe identifier")
    return value


def _integer(value: object, minimum: int, maximum: int, label: str) -> int:
    require(type(value) is int and minimum <= value <= maximum,
            f"{label} is outside its integer bounds")
    return value


def _widths(value: object, keys: frozenset[str], label: str) -> dict[str, object]:
    result = _object(value, keys, label)
    client = _integer(result["clientWidthCssPx"], 0, 65_535, f"{label}.clientWidthCssPx")
    scroll = _integer(result["scrollWidthCssPx"], 0, 65_535, f"{label}.scrollWidthCssPx")
    require(scroll >= client, f"{label} scroll width cannot be smaller than client width")
    return result


def validate(document: object, forbidden_values: tuple[str, ...] | list[str] = ()) -> None:
    root = _object(document, TOP_LEVEL_KEYS, "diagnostic")
    require(root["schemaVersion"] == SCHEMA_VERSION, "diagnostic schema version is unknown")
    require(root["suite"] == "r14", "suite is outside the reviewed R14 contract")
    require(root["caseId"] == "E05-R14-LONG-LABEL-320", "caseId is outside the reviewed E05 contract")
    _string(root["testedCommit"], COMMIT, "testedCommit")
    require(root["selectorAlias"] == "stock-owner-label", "selectorAlias is outside the reviewed E05 contract")
    require(root["boundaryClass"] == "long-wide-label", "boundaryClass is outside the reviewed E05 contract")

    viewport = _object(root["viewport"], VIEWPORT_KEYS, "viewport")
    viewport_width = _integer(viewport["widthCssPx"], 1, 16_384, "viewport.widthCssPx")
    _integer(viewport["heightCssPx"], 1, 16_384, "viewport.heightCssPx")
    page = _widths(root["page"], WIDTH_KEYS, "page")
    target = _widths(root["target"], TARGET_KEYS, "target")
    require(type(target["present"]) is bool and type(target["visible"]) is bool,
            "target presence fields must be boolean")
    seed = _object(root["seed"], SEED_KEYS, "seed")
    require(seed["fixtureId"] == "e05-wide-label-v1", "seed.fixtureId is outside the reviewed E05 contract")
    characters = _integer(seed["labelCharacterCount"], 1, 512, "seed.labelCharacterCount")
    wide_characters = _integer(seed["wideCharacterCount"], 0, 512, "seed.wideCharacterCount")
    require(wide_characters <= characters, "seed wide-character count exceeds label length")

    require(page["clientWidthCssPx"] <= viewport_width,
            "page client width exceeds the measured viewport")
    require(viewport_width == 320, "long-wide-label requires the 320px boundary")
    require(characters >= 80 and wide_characters >= 80,
            "long-wide-label seed summary is not a wide-character boundary")
    require(target["present"] is True and target["visible"] is True,
            "long-wide-label target must be present and visible")

    encoded = json.dumps(root, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    for index, value in enumerate(forbidden_values):
        require(isinstance(value, str) and len(value.encode("utf-8")) >= 8,
                f"runtime forbidden value {index} is missing or too short")
        require(value.encode("utf-8") not in encoded,
                f"diagnostic contains runtime forbidden value {index}")


def read_and_validate(path: str | pathlib.Path, forbidden_values: tuple[str, ...] | list[str] = ()) -> None:
    candidate = pathlib.Path(path)
    require(candidate.is_file(), "diagnostic input is missing")
    require(not candidate.is_symlink(), "diagnostic input must not be a symlink")
    content = candidate.read_bytes()
    require(0 < len(content) <= MAX_INPUT_BYTES, "diagnostic input size is outside the bound")
    try:
        document = json.loads(
            content.decode("utf-8"),
            object_pairs_hook=_duplicate_refusal,
            parse_constant=_constant_refusal,
        )
    except UnicodeDecodeError as error:
        raise ContractError("diagnostic is not UTF-8") from error
    except json.JSONDecodeError as error:
        raise ContractError("diagnostic is malformed JSON") from error
    validate(document, forbidden_values)


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    result.add_argument("--input", required=True)
    result.add_argument("--forbidden-value", action="append", default=[])
    return result


def main(argv: list[str] | None = None) -> int:
    arguments = parser().parse_args(argv)
    try:
        read_and_validate(arguments.input, arguments.forbidden_value)
    except (ContractError, OSError) as error:
        print(f"browser layout diagnostic contract failed: {error}", file=sys.stderr)
        return 1
    print("browser layout diagnostic contract validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
