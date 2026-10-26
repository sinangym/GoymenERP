#!/usr/bin/env python3
"""Generate the deterministic, source-only Göymen ERP release manifest v1.

The output describes one clean exact Git commit.  It never builds, publishes,
signs, deploys, migrates, restores, or reads environment configuration values.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
from dataclasses import dataclass
from types import ModuleType
from typing import Any, Iterable, Sequence


SCHEMA_VERSION = "goymenerp.release-manifest.v1"
MANIFEST_MODE = "SOURCE_ONLY"
REPOSITORY_ID = "https://github.com/sinangym/GoymenERP"
SCHEMA_PATH = Path("contracts/release/release-manifest-v1.schema.json")
GENERATOR_PATH = Path("tools/generate_release_manifest.py")
TEST_PATH = Path("tests/release_manifest/test_generate_release_manifest.py")
CI_PATH = Path(".github/workflows/ci.yml")
RECOVERY_SCHEMA_PATH = Path("contracts/recovery/recovery-surface-inventory-v1.schema.json")
RECOVERY_GENERATOR_PATH = Path("tools/generate_recovery_surface_inventory.py")
RECOVERY_TEST_PATH = Path(
    "tests/recovery_surface_inventory/test_generate_recovery_surface_inventory.py"
)
BACKEND_ROOT = "src/backend"
WEB_ROOT = "src/frontend/goymen-erp-web"
MODULE_ROOT = "src/backend/Modules"
COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
MODULE_PATTERN = re.compile(r"^[A-Z][A-Za-z0-9]*$")
VITE_KEY_PATTERN = re.compile(r"\bVITE_[A-Z][A-Z0-9_]*\b")
CONFIG_INDEXER_PATTERN = re.compile(
    r"(?:\bconfiguration|\bConfiguration|\bbuilder\.Configuration)\s*"
    r"\[\s*\"([^\"\r\n]+)\"\s*\]"
)
GET_SECTION_PATTERN = re.compile(
    r"\.(?:GetSection|GetConnectionString)\(\s*\"([^\"\r\n]+)\"\s*\)"
)
CONFIG_CONSTANT_PATTERN = re.compile(
    r"\b(?:const|static\s+readonly)\s+string\s+"
    r"(?:(?:[A-Za-z_][A-Za-z0-9_]*)?ConfigurationKey|SectionName)\s*"
    r"=\s*\"([^\"\r\n]+)\""
)


class ReleaseManifestError(RuntimeError):
    """Raised when an exact source-only manifest cannot be proven."""


@dataclass(frozen=True)
class GitEntry:
    path: str
    mode: str
    object_id: str
    content: bytes


@dataclass(frozen=True)
class SourceSnapshot:
    root: Path
    commit: str
    tree: str
    entries: dict[str, GitEntry]


def _git_environment() -> dict[str, str]:
    environment = os.environ.copy()
    environment["GIT_NO_REPLACE_OBJECTS"] = "1"
    return environment


def _run_git(
    repository_root: Path,
    arguments: Sequence[str],
    *,
    input_bytes: bytes | None = None,
) -> subprocess.CompletedProcess[bytes]:
    try:
        return subprocess.run(
            ["git", "-C", str(repository_root), *arguments],
            check=False,
            capture_output=True,
            input=input_bytes,
            env=_git_environment(),
        )
    except OSError as exc:
        raise ReleaseManifestError("Git is unavailable for exact source identification.") from exc


def _git_text(repository_root: Path, arguments: Sequence[str]) -> str:
    result = _run_git(repository_root, arguments)
    if result.returncode != 0:
        raise ReleaseManifestError("Git command failed while proving exact source.")
    try:
        return result.stdout.decode("utf-8").strip()
    except UnicodeError as exc:
        raise ReleaseManifestError("Git returned non-UTF-8 source metadata.") from exc


def _require_git_toplevel(repository_root: Path) -> Path:
    try:
        root = repository_root.resolve(strict=True)
    except OSError as exc:
        raise ReleaseManifestError("Repository root does not exist.") from exc
    if not root.is_dir():
        raise ReleaseManifestError("Repository root is not a directory.")
    top = Path(_git_text(root, ("rev-parse", "--show-toplevel"))).resolve(strict=True)
    if os.path.normcase(os.path.realpath(root)) != os.path.normcase(os.path.realpath(top)):
        raise ReleaseManifestError("Repository root must be the real Git top-level directory.")
    return root


def _head_commit(repository_root: Path) -> str:
    commit = _git_text(repository_root, ("rev-parse", "--verify", "HEAD^{commit}"))
    if COMMIT_PATTERN.fullmatch(commit) is None:
        raise ReleaseManifestError("Exact source commit cannot be resolved.")
    return commit


def _require_clean(repository_root: Path) -> None:
    result = _run_git(
        repository_root,
        ("status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"),
    )
    if result.returncode != 0 or result.stdout:
        raise ReleaseManifestError("Release manifest worktree must be clean.")


def _require_repository_identity(repository_root: Path) -> None:
    remote = _git_text(repository_root, ("remote", "get-url", "origin"))
    accepted = {
        "https://github.com/sinangym/GoymenERP",
        "https://github.com/sinangym/GoymenERP.git",
        "git@github.com:sinangym/GoymenERP.git",
        "ssh://git@github.com/sinangym/GoymenERP.git",
    }
    if remote not in accepted:
        raise ReleaseManifestError("Git origin does not identify the GöymenERP repository.")


def _require_no_replace_refs(repository_root: Path) -> None:
    result = _run_git(repository_root, ("replace", "-l"))
    if result.returncode != 0 or result.stdout.strip():
        raise ReleaseManifestError("Git replace refs are not allowed for release source proof.")


def _require_index_flags_clean(repository_root: Path) -> None:
    result = _run_git(repository_root, ("ls-files", "-v"))
    if result.returncode != 0:
        raise ReleaseManifestError("Git index flags cannot be verified.")
    records = result.stdout.splitlines()
    if not records or any(not record.startswith(b"H ") for record in records):
        raise ReleaseManifestError("Tracked source has hidden Git index flags.")


def _require_repo_path(path: str) -> None:
    if not path or "\\" in path or "\x00" in path or path.startswith("/"):
        raise ReleaseManifestError(f"Repository path is not canonical: {path!r}.")
    parts = PurePosixPath(path).parts
    if any(part in {"", ".", ".."} for part in parts) or PurePosixPath(path).as_posix() != path:
        raise ReleaseManifestError(f"Repository path is not canonical: {path!r}.")


def _is_release_input(path: str) -> bool:
    exact = {
        SCHEMA_PATH.as_posix(),
        GENERATOR_PATH.as_posix(),
        TEST_PATH.as_posix(),
        CI_PATH.as_posix(),
        RECOVERY_SCHEMA_PATH.as_posix(),
        RECOVERY_GENERATOR_PATH.as_posix(),
        RECOVERY_TEST_PATH.as_posix(),
    }
    return path in exact or path.startswith(f"{BACKEND_ROOT}/") or path.startswith(f"{WEB_ROOT}/")


def _read_commit_entries(repository_root: Path, source_commit: str) -> dict[str, GitEntry]:
    tree_result = _run_git(repository_root, ("ls-tree", "-r", "-z", "--full-tree", source_commit))
    if tree_result.returncode != 0:
        raise ReleaseManifestError("Exact commit tree cannot be enumerated.")
    pending: list[tuple[str, str, str]] = []
    folded: set[str] = set()
    for raw in tree_result.stdout.split(b"\x00"):
        if not raw:
            continue
        try:
            metadata, raw_path = raw.split(b"\t", 1)
            mode, object_type, object_id = metadata.decode("ascii").split(" ")
            path = raw_path.decode("utf-8")
        except (ValueError, UnicodeError) as exc:
            raise ReleaseManifestError("Exact commit tree contains an invalid path record.") from exc
        _require_repo_path(path)
        if not _is_release_input(path):
            continue
        if object_type != "blob" or mode not in {"100644", "100755"}:
            raise ReleaseManifestError(f"Release input is not a regular Git blob: {path}.")
        key = path.casefold()
        if key in folded:
            raise ReleaseManifestError("Exact commit contains case-colliding release paths.")
        folded.add(key)
        pending.append((path, mode, object_id))
    if not pending:
        raise ReleaseManifestError("Exact commit contains no release source inputs.")
    batch = _run_git(
        repository_root,
        ("cat-file", "--batch"),
        input_bytes=b"".join(object_id.encode("ascii") + b"\n" for _, _, object_id in pending),
    )
    if batch.returncode != 0:
        raise ReleaseManifestError("Exact commit blobs cannot be read.")
    entries: dict[str, GitEntry] = {}
    cursor = 0
    for path, mode, object_id in pending:
        header_end = batch.stdout.find(b"\n", cursor)
        if header_end < 0:
            raise ReleaseManifestError("Git blob batch response is truncated.")
        header = batch.stdout[cursor:header_end].split()
        if len(header) != 3 or header[0] != object_id.encode("ascii") or header[1] != b"blob":
            raise ReleaseManifestError("Git blob batch response does not match the exact tree.")
        try:
            size = int(header[2])
        except ValueError as exc:
            raise ReleaseManifestError("Git blob batch response has an invalid size.") from exc
        start = header_end + 1
        end = start + size
        if end >= len(batch.stdout) or batch.stdout[end : end + 1] != b"\n":
            raise ReleaseManifestError("Git blob batch response is truncated.")
        entries[path] = GitEntry(path, mode, object_id, batch.stdout[start:end])
        cursor = end + 1
    if cursor != len(batch.stdout):
        raise ReleaseManifestError("Git blob batch response contains trailing bytes.")
    return entries


def _require_executed_generator(snapshot: SourceSnapshot) -> None:
    entry = snapshot.entries.get(GENERATOR_PATH.as_posix())
    if entry is None:
        raise ReleaseManifestError("Exact commit is missing the release manifest generator.")
    try:
        executed = Path(__file__).resolve(strict=True).read_bytes()
        worktree = (snapshot.root / GENERATOR_PATH).read_bytes()
    except OSError as exc:
        raise ReleaseManifestError("Executed release generator bytes cannot be verified.") from exc
    if executed != entry.content or worktree != entry.content:
        raise ReleaseManifestError("Executed release generator bytes do not match the exact commit.")


def _load_snapshot(repository_root: Path, source_commit: str) -> SourceSnapshot:
    if COMMIT_PATTERN.fullmatch(source_commit) is None:
        raise ReleaseManifestError("Source commit must be a lowercase 40-character Git SHA.")
    root = _require_git_toplevel(repository_root)
    _require_repository_identity(root)
    _require_no_replace_refs(root)
    head = _head_commit(root)
    resolved_result = _run_git(root, ("rev-parse", "--verify", f"{source_commit}^{{commit}}"))
    try:
        resolved = resolved_result.stdout.decode("ascii").strip()
    except UnicodeError:
        resolved = ""
    if resolved_result.returncode != 0 or resolved != source_commit or head != source_commit:
        raise ReleaseManifestError("Source commit must resolve exactly to current Git HEAD.")
    _require_clean(root)
    _require_index_flags_clean(root)
    tree = _git_text(root, ("rev-parse", "--verify", f"{source_commit}^{{tree}}"))
    if COMMIT_PATTERN.fullmatch(tree) is None:
        raise ReleaseManifestError("Exact source tree cannot be resolved.")
    entries = _read_commit_entries(root, source_commit)
    snapshot = SourceSnapshot(root, source_commit, tree, entries)
    required = (
        SCHEMA_PATH,
        GENERATOR_PATH,
        TEST_PATH,
        CI_PATH,
        RECOVERY_SCHEMA_PATH,
        RECOVERY_GENERATOR_PATH,
        RECOVERY_TEST_PATH,
    )
    for path in required:
        if path.as_posix() not in entries:
            raise ReleaseManifestError(f"Exact commit is missing required source: {path.as_posix()}.")
    _require_executed_generator(snapshot)
    _require_clean(root)
    if _head_commit(root) != source_commit:
        raise ReleaseManifestError("Git HEAD changed while exact source blobs were read.")
    return snapshot


def _load_recovery_module(snapshot: SourceSnapshot) -> ModuleType:
    path = snapshot.root / RECOVERY_GENERATOR_PATH
    entry = snapshot.entries[RECOVERY_GENERATOR_PATH.as_posix()]
    try:
        if path.read_bytes() != entry.content:
            raise ReleaseManifestError("Recovery generator worktree bytes do not match the exact commit.")
    except OSError as exc:
        raise ReleaseManifestError("Recovery generator bytes cannot be read.") from exc
    module_name = "_goymenerp_release_recovery_inventory"
    module = ModuleType(module_name)
    module.__file__ = str(path)
    module.__package__ = ""
    sys.modules[module_name] = module
    try:
        exact_code = compile(entry.content, str(path), "exec", dont_inherit=True)
        exec(exact_code, module.__dict__)
    except Exception as exc:
        if sys.modules.get(module_name) is module:
            del sys.modules[module_name]
        raise ReleaseManifestError("Recovery inventory generator cannot be loaded.") from exc
    return module


def _sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _source_record(entry: GitEntry) -> dict[str, str]:
    return {"path": entry.path, "gitBlob": entry.object_id, "sha256": _sha256(entry.content)}


def _source_fingerprint(entries: Iterable[GitEntry]) -> tuple[int, str]:
    selected = sorted(entries, key=lambda item: item.path)
    digest = hashlib.sha256()
    for entry in selected:
        digest.update(entry.path.encode("utf-8"))
        digest.update(b"\x00")
        digest.update(_sha256(entry.content).encode("ascii"))
        digest.update(b"\n")
    return len(selected), digest.hexdigest()


def _component(snapshot: SourceSnapshot, component_id: str, kind: str, root: str) -> dict[str, Any]:
    entries = [entry for path, entry in snapshot.entries.items() if path.startswith(f"{root}/")]
    count, digest = _source_fingerprint(entries)
    if count == 0:
        raise ReleaseManifestError(f"Release source component is empty: {component_id}.")
    return {
        "id": component_id,
        "kind": kind,
        "sourceRoot": root,
        "sourceFileCount": count,
        "sourceSha256": digest,
        "artifactStatus": "NOT_BUILT",
    }


def _reject_duplicate_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ReleaseManifestError(f"JSON input contains duplicate key: {key}.")
        result[key] = value
    return result


def _flatten_config_keys(value: Any, prefix: str = "") -> set[str]:
    keys: set[str] = set()
    if isinstance(value, dict):
        for key, child in value.items():
            if not isinstance(key, str) or not key or ":" in key:
                raise ReleaseManifestError("Configuration JSON contains a non-canonical key.")
            child_prefix = f"{prefix}:{key}" if prefix else key
            keys.update(_flatten_config_keys(child, child_prefix))
    elif prefix:
        keys.add(prefix)
    return keys


def _decode_text(entry: GitEntry) -> str:
    try:
        return entry.content.decode("utf-8")
    except UnicodeError as exc:
        raise ReleaseManifestError(f"Release source is not UTF-8: {entry.path}.") from exc


def _configuration_inventory(
    snapshot: SourceSnapshot,
    recovery_module: ModuleType,
    recovery_inventory: dict[str, Any],
) -> dict[str, Any]:
    backend_keys: set[str] = set()
    frontend_keys: set[str] = set()
    contributing: dict[str, GitEntry] = {}
    for path, entry in snapshot.entries.items():
        if path.startswith("src/backend/GoymenERP.Api/appsettings") and path.endswith(".json"):
            try:
                document = json.loads(_decode_text(entry), object_pairs_hook=_reject_duplicate_pairs)
            except (json.JSONDecodeError, ReleaseManifestError) as exc:
                raise ReleaseManifestError(f"Configuration JSON cannot be parsed: {path}.") from exc
            if not isinstance(document, dict):
                raise ReleaseManifestError(f"Configuration JSON root must be an object: {path}.")
            backend_keys.update(_flatten_config_keys(document))
            contributing[path] = entry
        elif path.startswith(f"{BACKEND_ROOT}/") and path.endswith(".cs"):
            content = _decode_text(entry)
            try:
                live = recovery_module._live_csharp_code_with_literals(content)
            except Exception as exc:
                raise ReleaseManifestError(f"C# configuration source cannot be parsed: {path}.") from exc
            found = set(CONFIG_INDEXER_PATTERN.findall(live))
            for match in GET_SECTION_PATTERN.finditer(live):
                name = match.group(1)
                if "GetConnectionString" in match.group(0):
                    name = f"ConnectionStrings:{name}"
                found.add(name)
            found.update(CONFIG_CONSTANT_PATTERN.findall(live))
            if found:
                if any(not key or any(ch in key for ch in "\r\n\x00") for key in found):
                    raise ReleaseManifestError(f"Backend configuration key is invalid: {path}.")
                backend_keys.update(found)
                contributing[path] = entry
        elif path.startswith(f"{WEB_ROOT}/"):
            try:
                content = entry.content.decode("utf-8")
            except UnicodeError:
                continue
            found = set(VITE_KEY_PATTERN.findall(content))
            if found:
                frontend_keys.update(found)
                contributing[path] = entry
    backend_keys.update(recovery_inventory["documents"]["configurationKeys"])
    if not backend_keys or not frontend_keys:
        raise ReleaseManifestError("Source configuration key inventory is empty.")
    return {
        "classification": "KEY_NAMES_ONLY",
        "backendKeys": sorted(backend_keys),
        "frontendKeys": sorted(frontend_keys),
        "sourceFiles": [_source_record(contributing[path]) for path in sorted(contributing)],
    }


def _feature_inventory(snapshot: SourceSnapshot) -> list[dict[str, Any]]:
    modules: dict[str, list[GitEntry]] = {}
    prefix = f"{MODULE_ROOT}/"
    for path, entry in snapshot.entries.items():
        if not path.startswith(prefix):
            continue
        remainder = path[len(prefix) :]
        module = remainder.split("/", 1)[0]
        if MODULE_PATTERN.fullmatch(module) is None:
            raise ReleaseManifestError(f"Backend module path is not canonical: {path}.")
        modules.setdefault(module, []).append(entry)
    if not modules:
        raise ReleaseManifestError("Source feature list is empty.")
    features: list[dict[str, Any]] = []
    for module in sorted(modules):
        count, digest = _source_fingerprint(modules[module])
        features.append(
            {
                "id": module,
                "sourcePath": f"{MODULE_ROOT}/{module}",
                "sourceFileCount": count,
                "sourceSha256": digest,
                "status": "SOURCE_PRESENT",
            }
        )
    return features


def _recovery_inventory(
    snapshot: SourceSnapshot, recovery_module: ModuleType
) -> tuple[dict[str, Any], dict[str, Any]]:
    try:
        inventory = recovery_module.build_inventory(snapshot.root, snapshot.commit)
        canonical = recovery_module.canonical_bytes(inventory)
    except Exception as exc:
        raise ReleaseManifestError("Accepted recovery inventory could not be reproduced.") from exc
    sources = [
        _source_record(snapshot.entries[path.as_posix()])
        for path in (RECOVERY_SCHEMA_PATH, RECOVERY_GENERATOR_PATH, RECOVERY_TEST_PATH)
    ]
    record = {
        "schemaVersion": inventory["schemaVersion"],
        "inventoryMode": inventory["inventoryMode"],
        "inventorySha256": _sha256(canonical),
        "counts": {
            "dbContexts": inventory["counts"]["dbContexts"],
            "migrations": inventory["counts"]["migrations"],
            "durableStores": inventory["counts"]["durableStores"],
        },
        "sources": sources,
    }
    return record, inventory


def _load_schema(snapshot: SourceSnapshot) -> dict[str, Any]:
    try:
        schema = json.loads(
            snapshot.entries[SCHEMA_PATH.as_posix()].content.decode("utf-8"),
            object_pairs_hook=_reject_duplicate_pairs,
        )
    except (UnicodeError, json.JSONDecodeError, ReleaseManifestError) as exc:
        raise ReleaseManifestError("Release manifest schema cannot be parsed.") from exc
    if not isinstance(schema, dict):
        raise ReleaseManifestError("Release manifest schema root must be an object.")
    return schema


def _build_manifest_from_snapshot(
    snapshot: SourceSnapshot, recovery_module: ModuleType
) -> dict[str, Any]:
    recovery_record, recovery_inventory = _recovery_inventory(snapshot, recovery_module)
    return {
        "schemaVersion": SCHEMA_VERSION,
        "manifestMode": MANIFEST_MODE,
        "releaseId": f"source:{snapshot.commit}",
        "source": {
            "repository": REPOSITORY_ID,
            "commit": snapshot.commit,
            "tree": snapshot.tree,
            "cleanCheckout": True,
        },
        "components": [
            _component(snapshot, "backend-api", "BACKEND_API", BACKEND_ROOT),
            _component(snapshot, "frontend-web", "FRONTEND_WEB", WEB_ROOT),
        ],
        "recoveryInventory": recovery_record,
        "configuration": _configuration_inventory(snapshot, recovery_module, recovery_inventory),
        "features": _feature_inventory(snapshot),
        "compatibility": {
            "binarySchema": "UNVERIFIED",
            "configuration": "UNVERIFIED",
            "rollback": "UNVERIFIED",
            "forwardFix": "REVIEW_REQUIRED",
        },
        "evidence": {
            "applicationBuild": "NOT_RUN",
            "frontendBuild": "NOT_RUN",
            "tests": "NOT_RUN",
            "realSql": "NOT_RUN",
            "securityScan": "NOT_RUN",
            "sbom": "MISSING",
            "provenance": "UNVERIFIED",
            "attestation": "MISSING",
            "restore": "NOT_RUN",
            "uat": "NOT_RUN",
        },
        "result": "UNVERIFIED",
        "deployable": False,
        "assertions": {
            "sourceOnly": True,
            "containsSecrets": False,
            "containsConfigurationValues": False,
            "selectsProvider": False,
            "buildsArtifacts": False,
            "publishesArtifacts": False,
            "writesRegistry": False,
            "signsOrAttests": False,
            "appliesMigrations": False,
            "performsBackupOrRestore": False,
            "mutatesInfrastructure": False,
            "mutatesData": False,
            "deploys": False,
            "promotes": False,
            "activatesEnvironment": False,
        },
    }


def _validate_schema(
    recovery_module: ModuleType, manifest: dict[str, Any], schema: dict[str, Any]
) -> None:
    try:
        recovery_module._validate_against_schema(manifest, schema)
    except Exception as exc:
        raise ReleaseManifestError("Release manifest violates its JSON schema.") from exc


def build_manifest(repository_root: Path, source_commit: str) -> dict[str, Any]:
    snapshot = _load_snapshot(repository_root, source_commit)
    recovery_module = _load_recovery_module(snapshot)
    manifest = _build_manifest_from_snapshot(snapshot, recovery_module)
    _validate_schema(recovery_module, manifest, _load_schema(snapshot))
    _require_clean(snapshot.root)
    if _head_commit(snapshot.root) != source_commit:
        raise ReleaseManifestError("Git HEAD changed while the release manifest was built.")
    _require_executed_generator(snapshot)
    return manifest


def validate_manifest(repository_root: Path, manifest: dict[str, Any]) -> None:
    if not isinstance(manifest, dict):
        raise ReleaseManifestError("Release manifest must be a JSON object.")
    source = manifest.get("source")
    if not isinstance(source, dict) or not isinstance(source.get("commit"), str):
        raise ReleaseManifestError("Release manifest source commit is missing.")
    snapshot = _load_snapshot(repository_root, source["commit"])
    recovery_module = _load_recovery_module(snapshot)
    schema = _load_schema(snapshot)
    _validate_schema(recovery_module, manifest, schema)
    expected = _build_manifest_from_snapshot(snapshot, recovery_module)
    if canonical_bytes(manifest) != canonical_bytes(expected):
        raise ReleaseManifestError("Release manifest does not match the exact source snapshot.")


def canonical_json(manifest: dict[str, Any]) -> str:
    return json.dumps(
        manifest,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    ) + "\n"


def canonical_bytes(manifest: dict[str, Any]) -> bytes:
    return canonical_json(manifest).encode("utf-8")


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=Path.cwd())
    parser.add_argument("--source-commit")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    try:
        root = _require_git_toplevel(args.repository_root)
        source_commit = args.source_commit or _head_commit(root)
        manifest = build_manifest(root, source_commit)
    except (ReleaseManifestError, ValueError, TypeError) as exc:
        print(f"release manifest error: {exc}", file=sys.stderr)
        return 2
    sys.stdout.buffer.write(canonical_bytes(manifest))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
