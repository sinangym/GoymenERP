#!/usr/bin/env python3
"""Fail-closed validation for the repository's .NET bootstrap surface.

This script intentionally uses only the Python standard library so it can run
immediately after checkout, before setup-dotnet or any dotnet command.
"""

from __future__ import annotations

import hashlib
import json
import os
import stat
import sys
from pathlib import Path
from typing import Any, NoReturn


EXPECTED_ROOT_KEYS = frozenset({"sdk"})
EXPECTED_SDK_VALUES: dict[str, object] = {
    "version": "10.0.300",
    "rollForward": "latestFeature",
    "allowPrerelease": False,
}
EXPECTED_DIRECTORY_BUILD_PROPS_SHA256 = (
    "5F331370A343903CD6349064FAE749D40C101C87F30C4DA9351FE2BB285968BE"
)
EXPECTED_DIRECTORY_BUILD_PROPS_BYTES = 214
EXPECTED_NUGET_CONFIG_SHA256 = (
    "CB93C65E28718AA9075FCE221E9F9CEA9E72D3FE1B30462383448A591F5B3AA1"
)
EXPECTED_NUGET_CONFIG_BYTES = 355
EXPECTED_DOTNET_TOOLS_SHA256 = (
    "BDD198CFEB9F036E9B75C18C2E72F786524973CFD5B55807C060DD2DA0308DAB"
)
EXPECTED_DOTNET_TOOLS_BYTES = 182
FORBIDDEN_BOOTSTRAP_NAMES = frozenset(
    name.casefold()
    for name in (
        "Directory.Build.rsp",
        "MSBuild.rsp",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "Directory.Solution.props",
        "Directory.Solution.targets",
    )
)
FORBIDDEN_INHERITED_ANCESTOR_NAMES = frozenset(
    name.casefold()
    for name in (
        "Directory.Build.rsp",
        "MSBuild.rsp",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "Directory.Solution.props",
        "Directory.Solution.targets",
    )
)
MAX_GLOBAL_JSON_BYTES = 1024 * 1024


class BootstrapIntegrityError(RuntimeError):
    """Raised when the .NET bootstrap surface cannot be proven safe."""


def _fail(message: str) -> NoReturn:
    raise BootstrapIntegrityError(message)


def _is_symlink_or_reparse_point(path_stat: os.stat_result) -> bool:
    if stat.S_ISLNK(path_stat.st_mode):
        return True

    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
    file_attributes = getattr(path_stat, "st_file_attributes", 0)
    return bool(reparse_flag and file_attributes & reparse_flag)


def _repository_root(repository_root: Path | str) -> Path:
    raw_root = Path(repository_root)
    if ".." in raw_root.parts:
        _fail("Repository root must not contain parent-directory traversal ('..').")

    root = Path(os.path.abspath(os.fspath(raw_root)))
    try:
        root_stat = root.lstat()
    except OSError as exc:
        _fail(f"Cannot inspect repository root: {exc}")

    if _is_symlink_or_reparse_point(root_stat):
        _fail("Repository root must not be a symlink or reparse point.")
    if not stat.S_ISDIR(root_stat.st_mode):
        _fail("Repository root is not a directory.")

    try:
        resolved_root = root.resolve(strict=True)
    except OSError as exc:
        _fail(f"Cannot resolve repository root without indirection: {exc}")
    if os.path.normcase(os.fspath(root)) != os.path.normcase(os.fspath(resolved_root)):
        _fail("Repository root path must not traverse a symlink or reparse point.")
    return root


def _scan_repository(root: Path) -> tuple[Path, Path, Path, Path]:
    """Scan without following links and return all locked root policies."""

    pending = [root]
    root_global_json_variants: list[Path] = []
    root_directory_build_props: list[Path] = []
    root_nuget_config: list[Path] = []
    root_dotnet_tools: list[Path] = []

    while pending:
        current = pending.pop()
        try:
            with os.scandir(current) as directory:
                entries = sorted(list(directory), key=lambda item: item.name.casefold())
        except OSError as exc:
            _fail(f"Cannot enumerate repository path '{current}': {exc}")

        for entry in entries:
            entry_path = Path(entry.path)
            try:
                entry_path.relative_to(root)
            except ValueError:
                _fail(f"Repository scan escaped its root at '{entry_path}'.")

            try:
                entry_stat = entry.stat(follow_symlinks=False)
            except OSError as exc:
                _fail(f"Cannot inspect repository entry '{entry_path}': {exc}")

            if _is_symlink_or_reparse_point(entry_stat):
                _fail(f"Symlink or reparse point is not allowed: '{entry_path}'.")

            casefold_name = entry.name.casefold()
            if casefold_name == "global.json":
                if current != root:
                    _fail(f"Nested global.json policy is not allowed: '{entry_path}'.")
                root_global_json_variants.append(entry_path)

            if casefold_name == "directory.build.props":
                if current != root or entry.name != "Directory.Build.props":
                    _fail(
                        "Directory.Build.props is allowed only with exact casing at "
                        f"the repository root: '{entry_path}'."
                    )
                if not stat.S_ISREG(entry_stat.st_mode):
                    _fail("Root Directory.Build.props must be a regular file.")
                root_directory_build_props.append(entry_path)

            if casefold_name == "nuget.config":
                if current != root or entry.name != "NuGet.Config":
                    _fail(
                        "NuGet.Config is allowed only with exact casing at the "
                        f"repository root: '{entry_path}'."
                    )
                if not stat.S_ISREG(entry_stat.st_mode):
                    _fail("Root NuGet.Config must be a regular file.")
                root_nuget_config.append(entry_path)

            if casefold_name == "dotnet-tools.json":
                if current != root or entry.name != "dotnet-tools.json":
                    _fail(
                        "dotnet-tools.json is allowed only with exact casing at the "
                        f"repository root: '{entry_path}'."
                    )
                if not stat.S_ISREG(entry_stat.st_mode):
                    _fail("Root dotnet-tools.json must be a regular file.")
                root_dotnet_tools.append(entry_path)

            if casefold_name in FORBIDDEN_BOOTSTRAP_NAMES:
                _fail(f"Forbidden .NET bootstrap file found: '{entry_path}'.")
            if casefold_name.endswith(".csproj.user"):
                _fail(f"Forbidden per-user MSBuild project file found: '{entry_path}'.")

            # Git metadata is not repository content. Inspect its own entry above,
            # but never traverse the implementation-specific metadata directory.
            if current == root and entry.name == ".git":
                continue

            if stat.S_ISDIR(entry_stat.st_mode):
                pending.append(entry_path)
            elif not stat.S_ISREG(entry_stat.st_mode):
                _fail(f"Unsupported non-regular repository entry: '{entry_path}'.")

    if len(root_global_json_variants) != 1:
        _fail(
            "Repository root must contain exactly one case-insensitive "
            f"global.json entry; found {len(root_global_json_variants)}."
        )

    global_json_path = root_global_json_variants[0]
    if global_json_path.name != "global.json":
        _fail("The root SDK policy file must use the exact name 'global.json'.")

    if len(root_directory_build_props) != 1:
        _fail(
            "Repository root must contain exactly one exact-case regular "
            f"Directory.Build.props; found {len(root_directory_build_props)}."
        )
    if len(root_nuget_config) != 1:
        _fail(
            "Repository root must contain exactly one exact-case regular "
            f"NuGet.Config; found {len(root_nuget_config)}."
        )
    if len(root_dotnet_tools) != 1:
        _fail(
            "Repository root must contain exactly one exact-case regular "
            f"dotnet-tools.json; found {len(root_dotnet_tools)}."
        )
    return (
        global_json_path,
        root_directory_build_props[0],
        root_nuget_config[0],
        root_dotnet_tools[0],
    )


def _scan_inherited_ancestor_chain(root: Path) -> None:
    """Reject inherited bootstrap files without scanning unrelated subtrees."""

    current = root.parent
    while True:
        try:
            with os.scandir(current) as directory:
                entries = list(directory)
        except OSError as exc:
            _fail(f"Cannot enumerate repository ancestor '{current}': {exc}")

        for entry in entries:
            if entry.name.casefold() not in FORBIDDEN_INHERITED_ANCESTOR_NAMES:
                continue
            entry_path = Path(entry.path)
            try:
                entry.stat(follow_symlinks=False)
            except OSError as exc:
                _fail(f"Cannot inspect inherited bootstrap entry '{entry_path}': {exc}")
            _fail(f"Forbidden inherited .NET bootstrap entry found: '{entry_path}'.")

        parent = current.parent
        if parent == current:
            break
        current = parent


def _validate_directory_build_props(path: Path) -> None:
    try:
        path_stat = path.lstat()
        if _is_symlink_or_reparse_point(path_stat) or not stat.S_ISREG(path_stat.st_mode):
            _fail("Root Directory.Build.props must remain a regular non-link file.")
        if path_stat.st_size != EXPECTED_DIRECTORY_BUILD_PROPS_BYTES:
            _fail(
                "Root Directory.Build.props byte length drifted; expected "
                f"{EXPECTED_DIRECTORY_BUILD_PROPS_BYTES}, found {path_stat.st_size}."
            )
        content = path.read_bytes()
    except BootstrapIntegrityError:
        raise
    except OSError as exc:
        _fail(f"Cannot read root Directory.Build.props: {exc}")

    actual_hash = hashlib.sha256(content).hexdigest().upper()
    if actual_hash != EXPECTED_DIRECTORY_BUILD_PROPS_SHA256:
        _fail(
            "Root Directory.Build.props SHA-256 drifted; expected "
            f"{EXPECTED_DIRECTORY_BUILD_PROPS_SHA256}, found {actual_hash}."
        )


def _read_policy_bytes(path: Path, *, label: str) -> bytes:
    try:
        path_stat = path.lstat()
        if _is_symlink_or_reparse_point(path_stat) or not stat.S_ISREG(path_stat.st_mode):
            _fail(f"Root {label} must remain a regular non-link file.")
        if path_stat.st_size > MAX_GLOBAL_JSON_BYTES:
            _fail(f"Root {label} exceeds the policy-file safety limit.")
        content = path.read_bytes()
    except BootstrapIntegrityError:
        raise
    except OSError as exc:
        _fail(f"Cannot read root {label}: {exc}")

    if len(content) != path_stat.st_size:
        _fail(f"Root {label} changed while it was being read.")
    return content


def _require_locked_bytes(
    content: bytes,
    *,
    label: str,
    expected_bytes: int,
    expected_sha256: str,
) -> None:
    if len(content) != expected_bytes:
        _fail(
            f"Root {label} byte length drifted; expected {expected_bytes}, "
            f"found {len(content)}."
        )
    actual_hash = hashlib.sha256(content).hexdigest().upper()
    if actual_hash != expected_sha256:
        _fail(
            f"Root {label} SHA-256 drifted; expected {expected_sha256}, "
            f"found {actual_hash}."
        )


def _validate_nuget_config(path: Path) -> None:
    content = _read_policy_bytes(path, label="NuGet.Config")
    _require_locked_bytes(
        content,
        label="NuGet.Config",
        expected_bytes=EXPECTED_NUGET_CONFIG_BYTES,
        expected_sha256=EXPECTED_NUGET_CONFIG_SHA256,
    )


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            _fail(f"Duplicate JSON key is not allowed: '{key}'.")
        result[key] = value
    return result


def _reject_non_finite_json(constant: str) -> NoReturn:
    _fail(f"Non-finite JSON constant is not allowed: '{constant}'.")


def _parse_strict_json(raw_content: str, *, label: str) -> Any:
    try:
        return json.loads(
            raw_content,
            object_pairs_hook=_reject_duplicate_keys,
            parse_constant=_reject_non_finite_json,
        )
    except BootstrapIntegrityError:
        raise
    except (json.JSONDecodeError, RecursionError, ValueError) as exc:
        _fail(f"{label} is not valid strict JSON: {exc}")


def _load_global_json(path: Path) -> Any:
    try:
        file_size = path.stat().st_size
        if file_size > MAX_GLOBAL_JSON_BYTES:
            _fail(
                f"global.json exceeds the {MAX_GLOBAL_JSON_BYTES}-byte safety limit."
            )
        raw_content = path.read_text(encoding="utf-8")
    except BootstrapIntegrityError:
        raise
    except (OSError, UnicodeError) as exc:
        _fail(f"Cannot read global.json as strict UTF-8: {exc}")

    return _parse_strict_json(raw_content, label="global.json")


def _require_exact_keys(
    value: object,
    *,
    expected_keys: frozenset[str],
    location: str,
) -> dict[str, Any]:
    if type(value) is not dict:
        _fail(f"{location} must be a JSON object.")

    actual = set(value)
    if actual != expected_keys:
        missing = sorted(expected_keys - actual)
        unknown = sorted(actual - expected_keys)
        _fail(
            f"{location} keys do not match policy; missing={missing}, unknown={unknown}."
        )
    return value


def _validate_global_json(document: object) -> None:
    root = _require_exact_keys(
        document,
        expected_keys=EXPECTED_ROOT_KEYS,
        location="global.json root",
    )
    sdk = _require_exact_keys(
        root["sdk"],
        expected_keys=frozenset(EXPECTED_SDK_VALUES),
        location="global.json sdk",
    )

    for key, expected in EXPECTED_SDK_VALUES.items():
        actual = sdk[key]
        if type(actual) is not type(expected) or actual != expected:
            _fail(
                f"global.json sdk.{key} must be exactly {expected!r}; "
                f"found {actual!r}."
            )


def _validate_dotnet_tools_document(document: object) -> None:
    root = _require_exact_keys(
        document,
        expected_keys=frozenset({"version", "isRoot", "tools"}),
        location="dotnet-tools.json root",
    )
    if type(root["version"]) is not int or root["version"] != 1:
        _fail("dotnet-tools.json version must be exactly integer 1.")
    if type(root["isRoot"]) is not bool or root["isRoot"] is not True:
        _fail("dotnet-tools.json isRoot must be exactly true.")

    tools = _require_exact_keys(
        root["tools"],
        expected_keys=frozenset({"dotnet-ef"}),
        location="dotnet-tools.json tools",
    )
    dotnet_ef = _require_exact_keys(
        tools["dotnet-ef"],
        expected_keys=frozenset({"version", "commands", "rollForward"}),
        location="dotnet-tools.json dotnet-ef",
    )
    if type(dotnet_ef["version"]) is not str or dotnet_ef["version"] != "10.0.8":
        _fail("dotnet-tools.json dotnet-ef.version must be exactly '10.0.8'.")
    commands = dotnet_ef["commands"]
    if type(commands) is not list or commands != ["dotnet-ef"]:
        _fail(
            "dotnet-tools.json dotnet-ef.commands must be exactly ['dotnet-ef']."
        )
    if (
        type(dotnet_ef["rollForward"]) is not bool
        or dotnet_ef["rollForward"] is not False
    ):
        _fail("dotnet-tools.json dotnet-ef.rollForward must be exactly false.")


def _validate_dotnet_tools(path: Path) -> None:
    content = _read_policy_bytes(path, label="dotnet-tools.json")
    try:
        raw_content = content.decode("utf-8")
    except UnicodeError as exc:
        _fail(f"Cannot read root dotnet-tools.json as strict UTF-8: {exc}")

    document = _parse_strict_json(raw_content, label="dotnet-tools.json")
    _validate_dotnet_tools_document(document)
    _require_locked_bytes(
        content,
        label="dotnet-tools.json",
        expected_bytes=EXPECTED_DOTNET_TOOLS_BYTES,
        expected_sha256=EXPECTED_DOTNET_TOOLS_SHA256,
    )


def validate_repository(repository_root: Path | str) -> None:
    """Validate the complete .NET bootstrap integrity policy for a repository."""

    try:
        root = _repository_root(repository_root)
        _scan_inherited_ancestor_chain(root)
        (
            global_json_path,
            directory_build_props_path,
            nuget_config_path,
            dotnet_tools_path,
        ) = _scan_repository(root)
        _validate_directory_build_props(directory_build_props_path)
        _validate_nuget_config(nuget_config_path)
        _validate_dotnet_tools(dotnet_tools_path)
        document = _load_global_json(global_json_path)
        _validate_global_json(document)
    except BootstrapIntegrityError:
        raise
    except (OSError, UnicodeError) as exc:
        # Any unanticipated filesystem/decoding failure remains fail-closed.
        _fail(f"Unexpected repository validation failure: {exc}")


def main() -> int:
    repository_root = Path(__file__).resolve().parents[1]
    try:
        validate_repository(repository_root)
    except BootstrapIntegrityError as exc:
        print(f".NET bootstrap integrity validation failed: {exc}", file=sys.stderr)
        return 1

    print(".NET bootstrap integrity validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
