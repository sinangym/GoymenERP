#!/usr/bin/env python3
"""Compare generated and canonical OpenAPI contracts byte for byte, fail closed."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import stat
import sys
from pathlib import Path
from typing import Any, NamedTuple, Sequence


MAX_CONTRACT_BYTES = 16 * 1024 * 1024


class ContractValidationError(ValueError):
    """A safe-to-report contract validation failure."""


class ContractSnapshot(NamedTuple):
    sha256: str
    size: int
    device: int
    inode: int
    content: bytes


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ContractValidationError("JSON contains a duplicate object key.")
        result[key] = value
    return result


def _reject_non_finite(value: str) -> None:
    raise ContractValidationError("JSON contains a non-finite number.")


def _parse_finite_float(value: str) -> float:
    parsed = float(value)
    if not math.isfinite(parsed):
        raise ContractValidationError("JSON contains a non-finite number.")
    return parsed


def _is_link_or_reparse(file_stat: os.stat_result) -> bool:
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return stat.S_ISLNK(file_stat.st_mode) or bool(
        getattr(file_stat, "st_file_attributes", 0) & reparse_flag
    )


def _absolute_lexical_path(path: Path, label: str) -> Path:
    if ".." in path.parts:
        raise ContractValidationError(
            f"{label} path must not contain parent-directory traversal."
        )
    return Path(os.path.abspath(os.fspath(path)))


def _assert_no_link_components(path: Path, label: str) -> os.stat_result:
    current = Path(path.anchor)
    final_stat: os.stat_result | None = None
    for part in path.parts[1:]:
        current /= part
        try:
            final_stat = os.lstat(current)
        except FileNotFoundError as exc:
            raise ContractValidationError(f"{label} is missing.") from exc
        except OSError as exc:
            raise ContractValidationError(f"{label} metadata cannot be read.") from exc
        if _is_link_or_reparse(final_stat):
            raise ContractValidationError(
                f"{label} path must not traverse a symlink or reparse point."
            )
    if final_stat is None:
        raise ContractValidationError(f"{label} must be a regular file.")
    return final_stat


def _parse_json_object(data: bytes, label: str) -> None:
    try:
        decoded = data.decode("utf-8")
        document = json.loads(
            decoded,
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_non_finite,
            parse_float=_parse_finite_float,
        )
    except ContractValidationError as exc:
        raise ContractValidationError(f"{label} is not valid JSON: {exc}") from exc
    except (UnicodeError, json.JSONDecodeError, ValueError) as exc:
        raise ContractValidationError(f"{label} is not valid UTF-8 JSON.") from exc
    if not isinstance(document, dict):
        raise ContractValidationError(f"{label} JSON root must be an object.")


def _read_snapshot(path: Path, label: str) -> ContractSnapshot:
    absolute_path = _absolute_lexical_path(path, label)
    path_stat = _assert_no_link_components(absolute_path, label)
    if not stat.S_ISREG(path_stat.st_mode):
        raise ContractValidationError(f"{label} must be a regular file.")
    if path_stat.st_size == 0:
        raise ContractValidationError(f"{label} must not be empty.")
    if path_stat.st_size > MAX_CONTRACT_BYTES:
        raise ContractValidationError(
            f"{label} exceeds the {MAX_CONTRACT_BYTES}-byte safety limit."
        )

    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(absolute_path, flags)
    except OSError as exc:
        raise ContractValidationError(f"{label} cannot be opened safely.") from exc
    try:
        opened_stat = os.fstat(descriptor)
        if not stat.S_ISREG(opened_stat.st_mode):
            raise ContractValidationError(f"{label} must be a regular file.")
        if (
            opened_stat.st_dev != path_stat.st_dev
            or opened_stat.st_ino != path_stat.st_ino
        ):
            raise ContractValidationError(f"{label} changed while being opened.")
        with os.fdopen(descriptor, "rb", closefd=False) as stream:
            data = stream.read(MAX_CONTRACT_BYTES + 1)
    except OSError as exc:
        raise ContractValidationError(f"{label} cannot be read safely.") from exc
    finally:
        os.close(descriptor)

    if len(data) != opened_stat.st_size:
        raise ContractValidationError(f"{label} changed while being read.")
    if len(data) > MAX_CONTRACT_BYTES:
        raise ContractValidationError(
            f"{label} exceeds the {MAX_CONTRACT_BYTES}-byte safety limit."
        )
    _parse_json_object(data, label)
    return ContractSnapshot(
        hashlib.sha256(data).hexdigest(),
        len(data),
        opened_stat.st_dev,
        opened_stat.st_ino,
        data,
    )


def compare_contracts(baseline_path: Path, generated_path: Path) -> ContractSnapshot:
    """Validate both files and return their shared snapshot when byte-identical."""

    baseline = _read_snapshot(baseline_path, "Canonical contract")
    generated = _read_snapshot(generated_path, "Generated contract")
    if (
        baseline.device == generated.device
        and baseline.inode == generated.inode
    ):
        raise ContractValidationError(
            "Canonical and generated contracts must be different files."
        )
    if baseline.content != generated.content:
        raise ContractValidationError(
            "OpenAPI contract mismatch: "
            f"canonical sha256={baseline.sha256} size={baseline.size}; "
            f"generated sha256={generated.sha256} size={generated.size}."
        )
    return baseline


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Fail closed when generated OpenAPI bytes drift from baseline."
    )
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--generated", required=True, type=Path)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _build_parser().parse_args(argv)
    try:
        snapshot = compare_contracts(arguments.baseline, arguments.generated)
    except ContractValidationError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2
    except Exception:
        print("ERROR: OpenAPI contract validation failed closed.", file=sys.stderr)
        return 2
    print(
        "OpenAPI contract matches canonical baseline: "
        f"sha256={snapshot.sha256} size={snapshot.size}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
