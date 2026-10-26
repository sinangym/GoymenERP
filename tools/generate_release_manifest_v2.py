#!/usr/bin/env python3
"""Add v2 source identities without replacing the accepted v1 producer.

Source-only proof, not a build, applied migration, capability, environment,
restore, source admission or deployment. No environment values are emitted.
"""

from __future__ import annotations

import argparse
from decimal import Decimal
from functools import lru_cache
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys
from types import ModuleType
from typing import Any, Sequence


ROOT = Path(__file__).resolve().parents[1]
GENERATOR = "tools/generate_release_manifest_v2.py"
SCHEMA = "contracts/release/release-manifest-v2.schema.json"
TEST = "tests/release_manifest_v2/test_generate_release_manifest_v2.py"
DOCUMENT = "docs/operations/RELEASE_MANIFEST_V2.md"
API = "contracts/openapi/goymen-erp-v1.json"
V1_GENERATOR = "tools/generate_release_manifest.py"
V1_TEST = "tests/release_manifest/test_generate_release_manifest.py"
RECOVERY_GENERATOR = "tools/generate_recovery_surface_inventory.py"
NEW4 = (SCHEMA, GENERATOR, TEST, DOCUMENT)
EXTRA_INPUTS = (*NEW4, API)
VERSION = "goymenerp.release-manifest.v2"
WEB_SOURCE = "src/frontend/goymen-erp-web/src"
METHODS = frozenset({"get", "put", "post", "delete", "patch", "head", "options", "trace"})
PATH_METADATA = frozenset({"summary", "description", "parameters", "servers"})
MAX_JSON_BYTES = 8 * 1024 * 1024

# Original source provenance: main b6ed7ca5. Current local integration baseline:
# accepted main 8c8559a41fc6d43386ebc0787f28f445651058f0. Pins bind raw bytes;
# reused v1 globals/guards are not patched.
PINS = {
    V1_GENERATOR: "fe0c7dd2bfa49425db6069891976ad53407bbed805b2aaaa93e3c06debe52a67",
    RECOVERY_GENERATOR: "f9d83e4723bac3c55a8f8c9092e044a0542437103c23eb0a2d0415c92ce53e3f",
    "contracts/release/release-manifest-v1.schema.json":
        "d9dfb3c5a18eccafa4be6ef900f6236809afafcaa3711245160138669a0c2db9",
    "contracts/recovery/recovery-surface-inventory-v1.schema.json":
        "58cea67ade4ad1f60cff701bd05dc1f86b97cd94348dbbb3a9224dd3bcf52ed8",
}
PROVENANCE_INPUTS = tuple(sorted({*NEW4, *PINS, V1_TEST}))


class ManifestV2Error(ValueError):
    """Payload-free source/contract refusal."""


def _require(condition: bool, code: str) -> None:
    if not condition:
        raise ManifestV2Error(code)


def _sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def _pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result = {}
    for key, value in pairs:
        _require(key not in result, "duplicate_json_key")
        result[key] = value
    return result


def _invalid_number(_: str) -> Any:
    raise ManifestV2Error("nonfinite_json_number")


def _strict_json(content: bytes) -> Any:
    _require(type(content) is bytes and len(content) <= MAX_JSON_BYTES, "json_size_or_type")
    try:
        value = json.loads(content.decode("utf-8"), object_pairs_hook=_pairs,
                           parse_float=Decimal, parse_constant=_invalid_number)
    except ManifestV2Error:
        raise
    except (UnicodeError, ValueError, ArithmeticError, RecursionError):
        raise ManifestV2Error("invalid_json") from None
    pending, nodes = [(value, 0)], 0
    while pending:
        child, depth = pending.pop()
        nodes += 1
        _require(depth <= 64 and nodes <= 100000, "json_complexity")
        if isinstance(child, (list, dict)):
            _require(len(child) <= 4096, "json_complexity")
            children = child.values() if isinstance(child, dict) else child
            pending.extend((item, depth + 1) for item in children)
    return value


@lru_cache(maxsize=2)
def _frozen_module(relative: str) -> ModuleType:
    _require(relative in {V1_GENERATOR, RECOVERY_GENERATOR}, "invalid_engine")
    try:
        raw = (ROOT / relative).read_bytes()
    except OSError:
        raise ManifestV2Error("v1_source_unavailable") from None
    _require(_sha256(raw) == PINS[relative], "v1_source_changed")
    name = "_r021b_frozen_" + Path(relative).stem
    module = ModuleType(name)
    module.__file__, module.__package__ = str(ROOT / relative), ""
    sys.modules[name] = module
    try:
        exec(compile(raw, module.__file__, "exec", dont_inherit=True), module.__dict__)
    except Exception:
        sys.modules.pop(name, None)
        raise ManifestV2Error("v1_engine_unavailable") from None
    return module


def _extra_entries(legacy: ModuleType, snapshot: Any) -> dict[str, Any]:
    result = legacy._run_git(snapshot.root, ("ls-tree", "-r", "-z", "--full-tree", snapshot.commit))
    _require(result.returncode == 0, "extra_tree_unavailable")
    wanted = {path.casefold(): path for path in EXTRA_INPUTS}
    entries, seen = {}, set()
    for record in result.stdout.split(b"\0"):
        if not record:
            continue
        try:
            metadata, path_bytes = record.split(b"\t", 1)
            mode, kind, object_id = metadata.decode("ascii").split(" ")
            path = path_bytes.decode("utf-8")
        except (ValueError, UnicodeError):
            raise ManifestV2Error("invalid_extra_tree") from None
        folded = path.casefold()
        if folded not in wanted:
            continue
        _require(folded not in seen and path == wanted[folded], "extra_path_case_collision")
        seen.add(folded)
        _require(kind == "blob" and mode in {"100644", "100755"}, "extra_not_regular_blob")
        blob = legacy._run_git(snapshot.root, ("cat-file", "blob", object_id))
        _require(blob.returncode == 0, "extra_blob_unavailable")
        entries[path] = legacy.GitEntry(path, mode, object_id, blob.stdout)
    _require(set(entries) == set(EXTRA_INPUTS), "extra_input_missing")
    return entries


def _verify_worktree_and_code(legacy: ModuleType, snapshot: Any) -> None:
    for relative in (*EXTRA_INPUTS, *PINS, V1_TEST):
        entry = snapshot.entries.get(relative)
        _require(entry is not None, "source_input_missing")
        path = snapshot.root / relative
        try:
            _require(not path.is_symlink() and path.resolve().is_relative_to(snapshot.root),
                     "source_input_alias")
            _require(path.read_bytes() == entry.content, "worktree_blob_mismatch")
        except OSError:
            raise ManifestV2Error("source_input_unavailable") from None
        if relative in PINS:
            _require(_sha256(entry.content) == PINS[relative], "v1_source_changed")
    try:
        executed = Path(__file__).resolve(strict=True).read_bytes()
    except OSError:
        raise ManifestV2Error("executed_v2_unavailable") from None
    _require(executed == snapshot.entries[GENERATOR].content, "executed_v2_mismatch")
    legacy._require_repository_identity(snapshot.root)
    legacy._require_no_replace_refs(snapshot.root)
    legacy._require_index_flags_clean(snapshot.root)
    legacy._require_clean(snapshot.root)
    _require(legacy._head_commit(snapshot.root) == snapshot.commit, "source_head_changed")


def _source_snapshot(legacy: ModuleType, root: Path, commit: str) -> Any:
    snapshot = legacy._load_snapshot(root, commit)
    entries = dict(snapshot.entries)
    entries.update(_extra_entries(legacy, snapshot))
    expanded = legacy.SourceSnapshot(snapshot.root, snapshot.commit, snapshot.tree, entries)
    _verify_worktree_and_code(legacy, expanded)
    return expanded


def _frontend_features(legacy: ModuleType, snapshot: Any) -> list[dict[str, Any]]:
    groups: dict[str, list[Any]] = {}
    prefix = WEB_SOURCE + "/"
    for path, entry in snapshot.entries.items():
        if not path.startswith(prefix) or "/" not in path[len(prefix):]:
            continue
        directory = path[len(prefix):].split("/", 1)[0]
        if directory in {"assets", "test"}:
            continue
        _require(re.fullmatch(r"[a-z][A-Za-z0-9]*", directory) is not None,
                 "unsupported_frontend_directory")
        groups.setdefault(directory, []).append(entry)
    _require(bool(groups), "frontend_features_empty")
    result = []
    for directory in sorted(groups):
        count, digest = legacy._source_fingerprint(groups[directory])
        result.append({"id": directory,
                       "kind": {"shell": "SHELL", "ui": "DESIGN_SYSTEM"}.get(directory, "MODULE"),
                       "sourcePath": prefix + directory, "sourceFileCount": count,
                       "sourceSha256": digest, "status": "SOURCE_PRESENT"})
    return result


def _api_contract(legacy: ModuleType, snapshot: Any) -> dict[str, Any]:
    entry = snapshot.entries.get(API)
    _require(entry is not None, "api_input_missing")
    document = _strict_json(entry.content)
    _require(isinstance(document, dict), "api_document_invalid")
    version, info, paths = document.get("openapi"), document.get("info"), document.get("paths")
    _require(isinstance(version, str) and re.fullmatch(r"3\.[0-9]+\.[0-9]+", version) is not None,
             "api_version_invalid")
    _require(isinstance(info, dict) and isinstance(info.get("version"), str)
             and re.fullmatch(r"v[1-9][0-9]*", info["version"]) is not None, "api_version_invalid")
    _require(isinstance(paths, dict) and bool(paths), "api_paths_invalid")
    identities = []
    for route, item in paths.items():
        _require(route.startswith("/") and all(ord(c) >= 32 for c in route)
                 and isinstance(item, dict), "api_path_invalid")
        for method, operation in item.items():
            if method in PATH_METADATA or method.startswith("x-"):
                continue
            _require(method in METHODS, "api_path_member_unsupported")
            _require(isinstance(operation, dict) and "$ref" not in operation, "api_operation_invalid")
            identity = operation.get("operationId")
            _require(isinstance(identity, str)
                     and re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", identity) is not None,
                     "api_operation_id_invalid")
            identities.append(identity)
    _require(bool(identities) and len(identities) == len(set(identities)), "api_operation_ids_ambiguous")
    return {**legacy._source_record(entry), "openapiVersion": version,
            "documentVersion": info["version"], "operationCount": len(identities),
            "operationIdsSha256": _sha256("\n".join(sorted(identities)).encode("utf-8"))}


def _structural(recovery: ModuleType, content: bytes) -> str:
    try:
        # Strip comments first: apostrophes in comments are not char literals.
        return recovery._live_csharp_code(recovery._strip_csharp_comments(content.decode("utf-8")))
    except Exception:
        raise ManifestV2Error("csharp_source_unsupported") from None


def _context_types(snapshot: Any, recovery: ModuleType, inventory: dict) -> dict[str, str]:
    result = {}
    for row in inventory["dbContexts"]:
        entry = snapshot.entries.get(row["path"])
        _require(entry is not None and _sha256(entry.content) == row["sha256"], "context_source_mismatch")
        live = _structural(recovery, entry.content)
        names = re.findall(r"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]", live)
        _require(len(names) == 1 and row["name"] not in result, "context_namespace_ambiguous")
        _require(len(re.findall(r"\bclass\s+" + re.escape(row["name"]) + r"\b", live)) == 1,
                 "context_declaration_ambiguous")
        result[row["name"]] = names[0] + "." + row["name"]
    return result


ATTRIBUTE = re.compile(
    r"\[\s*((?:global::)?[A-Za-z_][A-Za-z0-9_.]*)\s*\(\s*typeof\s*\(\s*"
    r"((?:global::)?[A-Za-z_][A-Za-z0-9_.]*)\s*\)\s*\)\s*\]"
)


def _migration_context(snapshot: Any, recovery: ModuleType, path: str,
                       context_types: dict[str, str]) -> str:
    class_name = PurePosixPath(path).stem.split("_", 1)[-1]
    bindings = []
    for candidate in (path, path[:-3] + ".Designer.cs"):
        entry = snapshot.entries.get(candidate)
        if entry is None:
            continue
        live = _structural(recovery, entry.content)
        declarations = list(re.finditer(r"\bclass\s+" + re.escape(class_name) + r"\b", live))
        _require(len(declarations) == 1, "migration_class_ambiguous")
        offset = declarations[0].start()
        start = max(live.rfind(";", 0, offset), live.rfind("{", 0, offset), live.rfind("}", 0, offset)) + 1
        header = live[start:offset]
        matches = [(name.removeprefix("global::"), target.removeprefix("global::"))
                   for name, target in ATTRIBUTE.findall(header)
                   if name.split(".")[-1] in {"DbContext", "DbContextAttribute"}]
        _require(len(re.findall(r"\bDbContext(?:Attribute)?\b", header)) == len(matches),
                 "context_attribute_unsupported")
        for attribute, target in matches:
            _require(attribute in {"DbContext", "DbContextAttribute",
                     "Microsoft.EntityFrameworkCore.Infrastructure.DbContext",
                     "Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute"},
                     "context_attribute_unsupported")
            name = target.split(".")[-1]
            _require(name in context_types and target in {name, context_types[name]},
                     "migration_context_unknown")
            bindings.append(name)
    _require(len(bindings) == 1, "migration_context_ambiguous")
    return bindings[0]


def _schema_heads(snapshot: Any, recovery: ModuleType, inventory: dict) -> list[dict[str, Any]]:
    types = _context_types(snapshot, recovery, inventory)
    _require(len(types) == inventory["counts"]["dbContexts"], "context_coverage_mismatch")
    groups: dict[tuple[str, str], list[str]] = {}
    seen = set()
    for row in inventory["migrations"]:
        path = row["path"]
        _require(".." not in PurePosixPath(path).parts and "\\" not in path,
                 "migration_path_unsupported")
        _require(path not in seen, "migration_duplicate")
        seen.add(path)
        entry = snapshot.entries.get(path)
        _require(entry is not None and _sha256(entry.content) == row["sha256"], "migration_source_mismatch")
        match = re.fullmatch(r"(src/backend/(?:[A-Za-z0-9._-]+/)+Migrations)/([0-9]{14}_[A-Za-z0-9]+)\.cs", path)
        _require(match is not None, "migration_path_unsupported")
        context = _migration_context(snapshot, recovery, path, types)
        groups.setdefault((context, match[1]), []).append(match[2])
    heads = [{"dbContext": context, "migrationsDirectory": directory,
              "migrationCount": len(names), "latestMigration": sorted(names)[-1]}
             for (context, directory), names in sorted(groups.items())]
    _require(sum(row["migrationCount"] for row in heads) == inventory["counts"]["migrations"]
             and {row["dbContext"] for row in heads} == set(types), "schema_coverage_mismatch")
    return heads


def build_manifest(root: Path, commit: str) -> dict[str, Any]:
    legacy = _frozen_module(V1_GENERATOR)
    try:
        snapshot = _source_snapshot(legacy, root, commit)
        recovery = legacy._load_recovery_module(snapshot)
        manifest = legacy._build_manifest_from_snapshot(snapshot, recovery)
        legacy._validate_schema(recovery, manifest, legacy._load_schema(snapshot))
        # The existing upstream producer validates its complete recovery surface.
        _, inventory = legacy._recovery_inventory(snapshot, recovery)
        manifest.update(schemaVersion=VERSION,
                        frontendFeatures=_frontend_features(legacy, snapshot),
                        apiContract=_api_contract(legacy, snapshot),
                        schemaHeads=_schema_heads(snapshot, recovery, inventory),
                        v2SourceInputs=[legacy._source_record(snapshot.entries[path])
                                        for path in PROVENANCE_INPUTS])
        schema = _strict_json(snapshot.entries[SCHEMA].content)
        legacy._validate_schema(recovery, manifest, schema)
        _verify_worktree_and_code(legacy, snapshot)
        return manifest
    except ManifestV2Error:
        raise
    except Exception:
        raise ManifestV2Error("upstream_v1_source_refused") from None


def validate_manifest(root: Path, commit: str, raw: bytes) -> None:
    candidate = _strict_json(raw)
    expected = build_manifest(root, commit)
    recovery = _frozen_module(RECOVERY_GENERATOR)
    _require(recovery._json_equal(candidate, expected), "manifest_source_mismatch")


class _Parser(argparse.ArgumentParser):
    def error(self, message: str) -> None:
        self.exit(2, "release manifest v2 error: invalid_arguments\n")


def main(argv: Sequence[str] | None = None) -> int:
    parser = _Parser(description=__doc__)
    parser.add_argument("command", choices=("build", "validate"))
    parser.add_argument("--repository-root", type=Path, default=ROOT)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--manifest", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "build":
            _require(args.manifest is None, "unexpected_manifest_input")
            output = build_manifest(args.repository_root, args.source_commit)
            sys.stdout.buffer.write(_frozen_module(V1_GENERATOR).canonical_bytes(output))
        else:
            _require(args.manifest is not None, "manifest_input_required")
            with args.manifest.open("rb") as stream:
                raw = stream.read(MAX_JSON_BYTES + 1)
            validate_manifest(args.repository_root, args.source_commit, raw)
            sys.stdout.buffer.write(b'{"result":"UNVERIFIED","validation":"EXACT_SOURCE_ONLY","deployable":false}\n')
        return 0
    except ManifestV2Error as exc:
        sys.stderr.write("release manifest v2 error: " + str(exc) + "\n")
        return 2
    except OSError:
        sys.stderr.write("release manifest v2 error: source_or_output_unavailable\n")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
