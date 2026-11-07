#!/usr/bin/env python3
"""Build/validate a source-only R05.1 contract from v1 manifest/inventory bytes.

Validation proves input contracts and byte links, not source admission, runtime
isolation, a backup, a consistent recovery point, or a completed rehearsal.
Only fixed local contract sources are read. No generator, Git command, network,
database, provider, worker or restore operation is invoked.
"""

from __future__ import annotations

import argparse
import copy
from functools import lru_cache
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys
from types import ModuleType
from typing import Any, Sequence


ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = Path("contracts/recovery/current-recovery-envelope-v1.schema.json")
MANIFEST_SCHEMA = "contracts/release/release-manifest-v1.schema.json"
INVENTORY_SCHEMA = "contracts/recovery/recovery-surface-inventory-v1.schema.json"
ENGINE_PATH = "tools/generate_recovery_surface_inventory.py"
MANIFEST_VERSION = "goymenerp.release-manifest.v1"
ENVELOPE_VERSION = "goymenerp.current-recovery-envelope.v1"
RESULT = "SOURCE_ONLY_NOT_REHEARSED"
MAX_JSON_BYTES = 8 * 1024 * 1024
MAX_JSON_DEPTH = 64
MAX_CONTAINER_ITEMS = 4096
MAX_JSON_NODES = 100000

# Git-blob SHA256 at accepted main 8c8559a4; checkout CRLF is normalized to LF.
# This freezes the reused schema API. A changed shared source needs a new review,
# not a fallback parser, an installed package, or execution of its generator.
TRUSTED_CONTRACT_SOURCES = {
    ENGINE_PATH: "f9d83e4723bac3c55a8f8c9092e044a0542437103c23eb0a2d0415c92ce53e3f",
    INVENTORY_SCHEMA: "58cea67ade4ad1f60cff701bd05dc1f86b97cd94348dbbb3a9224dd3bcf52ed8",
    MANIFEST_SCHEMA: "d9dfb3c5a18eccafa4be6ef900f6236809afafcaa3711245160138669a0c2db9",
}
RECOVERY_RULES = {
    "idempotency": "RESTORE_WITH_DATABASE_REPLAY_NOT_REEXECUTE",
    "outbox": "RESTORE_AND_HOLD_UNDISPATCHED",
    "inbox": "RESTORE_AND_KEEP_DEDUP",
    "delivery": "RESTORE_AND_RECONCILE_DELIVERY",
}
EXTERNAL_EFFECTS = (
    "scheduledWorkers", "dispatcher", "email", "webhook", "bankOrEDocument",
    "objectCallbacks", "otherEgress",
)
UNSAFE_TEXT = (
    re.compile(r"(?i)\b(?:password|passwd|pwd|secret|token|apikey|api_key)\s*[=:]"),
    re.compile(r"(?i)\b(?:server|data source|user id)\s*="),
    re.compile(r"(?i)\b(?:RESTORE|BACKUP)\s+(?:DATABASE|LOG)\b"),
    re.compile(r"(?i)\b(?:sqlcmd|sqlpackage|kubectl|robocopy|rsync)\b"),
    re.compile(r"(?i)\b(?:aws\s+s3|az\s+storage|gsutil|docker\s+(?:run|compose))\b"),
    re.compile(r"^(?:[A-Za-z]:[\\/]|\\\\|/)"),
)


class EnvelopeViolation(ValueError):
    """Stable, payload-free diagnostic; it carries no supplied key/value/path."""


def _require(condition: bool, code: str) -> None:
    if not condition:
        raise EnvelopeViolation(code)


def _sha256(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def canonical_bytes(value: Any) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True,
                       separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")


def _pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, child in pairs:
        _require(key not in value, "duplicate_json_key")
        value[key] = child
    return value


def _unsupported_number(_: str) -> Any:
    # These source contracts contain integers, never floating-point quantities.
    raise EnvelopeViolation("unsupported_json_number")


def _strict_json(content: bytes) -> Any:
    _require(type(content) is bytes and len(content) <= MAX_JSON_BYTES, "json_size_or_type")
    try:
        value = json.loads(content.decode("utf-8"), object_pairs_hook=_pairs,
                           parse_constant=_unsupported_number, parse_float=_unsupported_number)
    except EnvelopeViolation:
        raise
    except (UnicodeError, ValueError, RecursionError):
        raise EnvelopeViolation("invalid_json") from None
    pending = [(value, 0)]
    visited = 0
    while pending:
        child, depth = pending.pop()
        visited += 1
        _require(depth <= MAX_JSON_DEPTH and visited <= MAX_JSON_NODES, "json_complexity")
        if isinstance(child, (list, dict)):
            _require(len(child) <= MAX_CONTAINER_ITEMS, "json_complexity")
            children = child.values() if isinstance(child, dict) else child
            pending.extend((item, depth + 1) for item in children)
    return value


def _reject_unsafe_text(value: Any) -> None:
    pending = [value]
    while pending:
        child = pending.pop()
        if isinstance(child, str):
            _require(not any(ord(char) < 32 or ord(char) == 127 for char in child), "unsafe_text")
            _require(not any(pattern.search(child) for pattern in UNSAFE_TEXT), "unsafe_text")
            _require("\\" not in child and ".." not in PurePosixPath(child).parts, "unsafe_text")
        elif isinstance(child, dict):
            pending.extend(child.values())
        elif isinstance(child, list):
            pending.extend(child)


def _trusted_bytes(relative: str) -> bytes:
    try:
        content = (ROOT / relative).read_bytes().replace(b"\r\n", b"\n")
    except OSError:
        raise EnvelopeViolation("contract_source_unavailable") from None
    _require(_sha256(content) == TRUSTED_CONTRACT_SOURCES[relative], "contract_source_changed")
    return content


@lru_cache(maxsize=1)
def _engine() -> ModuleType:
    name = "_r051_frozen_recovery_schema_engine"
    module = ModuleType(name)
    module.__file__ = str(ROOT / ENGINE_PATH)
    content = _trusted_bytes(ENGINE_PATH)
    sys.modules[name] = module
    try:
        # Compile the verified source bytes, never a pyc or an input-supplied module.
        # __name__ is not __main__: no generator entry point is called.
        exec(compile(content, module.__file__, "exec", dont_inherit=True), module.__dict__)
    except Exception:
        sys.modules.pop(name, None)
        raise EnvelopeViolation("contract_engine_unavailable") from None
    return module


def _closed_objects(schema: Any) -> None:
    _require(isinstance(schema, dict), "invalid_schema")
    if schema.get("type") == "object":
        _require(schema.get("additionalProperties") is False, "open_schema_object")
    properties = schema.get("properties", {})
    _require(isinstance(properties, dict), "invalid_schema")
    for child in properties.values():
        _closed_objects(child)
    if "items" in schema:
        _closed_objects(schema["items"])


def _schema(relative: str) -> dict[str, Any]:
    if relative in TRUSTED_CONTRACT_SOURCES:
        content = _trusted_bytes(relative)
    else:
        _require(relative == SCHEMA_PATH.as_posix(), "invalid_schema_source")
        try:
            content = (ROOT / SCHEMA_PATH).read_bytes()
        except OSError:
            raise EnvelopeViolation("contract_source_unavailable") from None
    schema = _strict_json(content)
    _closed_objects(schema)
    return schema


def _validate(value: Any, schema: dict[str, Any]) -> None:
    engine = _engine()
    try:
        engine._validate_against_schema(value, schema)
    except engine.InventoryError:
        raise EnvelopeViolation("schema_violation") from None


def _unique(rows: list[dict[str, Any]], field: str) -> None:
    values = [row[field] for row in rows]
    _require(len(values) == len(set(values)), "duplicate_identity")


def _read_inputs(manifest_bytes: bytes, inventory_bytes: bytes) -> tuple[dict, dict]:
    manifest, inventory = _strict_json(manifest_bytes), _strict_json(inventory_bytes)
    _require(isinstance(manifest, dict) and manifest.get("schemaVersion") == MANIFEST_VERSION,
             "unsupported_manifest_version")
    _validate(manifest, _schema(MANIFEST_SCHEMA))
    _validate(inventory, _schema(INVENTORY_SCHEMA))
    _reject_unsafe_text(manifest)
    _reject_unsafe_text(inventory)
    source, recovery = manifest["source"], manifest["recoveryInventory"]
    _require(manifest["releaseId"] == "source:" + source["commit"], "source_identity_mismatch")
    _require(source["commit"] == inventory["sourceCommit"], "source_identity_mismatch")
    _require(recovery["inventorySha256"] == _sha256(inventory_bytes), "inventory_hash_mismatch")
    counts = inventory["counts"]
    _require(counts == recovery["counts"], "inventory_count_mismatch")
    for key in ("dbContexts", "migrations", "durableStores"):
        _require(counts[key] == len(inventory[key]), "inventory_count_mismatch")
    for key, field in (("dbContexts", "name"), ("dbContexts", "path"),
                       ("migrations", "path"), ("durableStores", "id")):
        _unique(inventory[key], field)
    contexts = {context["name"] for context in inventory["dbContexts"]}
    _require(all(store["dbContext"] in contexts for store in inventory["durableStores"]),
             "unknown_store_context")
    _unique(manifest["components"], "id")
    _require({row["id"]: (row["kind"], row["sourceRoot"]) for row in manifest["components"]} == {
        "backend-api": ("BACKEND_API", "src/backend"),
        "frontend-web": ("FRONTEND_WEB", "src/frontend/goymen-erp-web"),
    }, "component_identity_mismatch")
    _unique(manifest["features"], "id")
    _require(all(row["sourcePath"] == "src/backend/Modules/" + row["id"]
                 for row in manifest["features"]), "feature_identity_mismatch")
    _unique(recovery["sources"], "path")
    _require({row["path"] for row in recovery["sources"]} == {
        INVENTORY_SCHEMA, ENGINE_PATH,
        "tests/recovery_surface_inventory/test_generate_recovery_surface_inventory.py",
    }, "inventory_contract_sources_mismatch")
    _require(all(row["path"] not in TRUSTED_CONTRACT_SOURCES
                 or row["sha256"] == TRUSTED_CONTRACT_SOURCES[row["path"]]
                 for row in recovery["sources"]), "inventory_contract_sources_mismatch")
    # Repeated source references may share a path but never disagree on its bytes.
    references = [*inventory["dbContexts"], *inventory["migrations"],
                  *inventory["documents"]["sourceFiles"]]
    for store in inventory["durableStores"]:
        references.extend(store["sourceFiles"])
        _unique(store["sourceFiles"], "path")
    _unique(inventory["documents"]["sourceFiles"], "path")
    fingerprints: dict[str, str] = {}
    for row in references:
        path, digest = row["path"], row["sha256"]
        _require(path not in fingerprints or fingerprints[path] == digest,
                 "source_fingerprint_conflict")
        fingerprints[path] = digest
    return manifest, inventory


def build_envelope(manifest_bytes: bytes, inventory_bytes: bytes) -> dict[str, Any]:
    manifest, inventory = _read_inputs(manifest_bytes, inventory_bytes)
    declarations = "".join(row["path"] + "\t" + row["sha256"] + "\n"
                           for row in sorted(inventory["migrations"], key=lambda row: row["path"]))
    stores = []
    for row in sorted(inventory["durableStores"], key=lambda row: row["id"]):
        store = copy.deepcopy(row)
        store["sourceFiles"].sort(key=lambda item: item["path"])
        store["contracts"].sort()
        store["recoveryRule"] = RECOVERY_RULES[row["kind"]]
        store["reconciliation"] = {
            "status": "NOT_CAPTURED", "highWaterMark": None, "keySetSha256": None,
        }
        stores.append(store)
    output = {
        "schemaVersion": ENVELOPE_VERSION,
        "envelopeMode": "SOURCE_ONLY",
        "decision": {
            "code": "D-RESTORE=A",
            "package": "https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6207345638",
            "record": "https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6210091477",
        },
        "source": {
            "commit": manifest["source"]["commit"], "tree": manifest["source"]["tree"],
            "releaseManifestVersion": MANIFEST_VERSION,
            "releaseManifestSha256": _sha256(manifest_bytes),
            "recoveryInventorySha256": _sha256(inventory_bytes),
        },
        "database": {
            "dbContexts": sorted(copy.deepcopy(inventory["dbContexts"]), key=lambda row: row["name"]),
            "migrationCount": len(inventory["migrations"]),
            "migrationDeclarationSha256": _sha256(declarations.encode("utf-8")),
            "restoreTarget": "UNIQUE_ISOLATED_TARGET_REQUIRED",
        },
        "durableStores": stores,
        "documents": {
            "configurationKeys": list(inventory["documents"]["configurationKeys"]),
            "objectKeyPattern": inventory["documents"]["objectKeyPattern"],
            "pathClassifications": copy.deepcopy(inventory["documents"]["pathClassifications"]),
            "objectInventory": {"status": "NOT_CAPTURED", "manifestSha256": None},
        },
        "externalEffects": {name: "REQUIRED_DISABLED_NOT_VERIFIED" for name in EXTERNAL_EFFECTS},
        "recoveryObjectives": {"source": "OPEN-017", "status": "UNMEASURED_NOT_ACCEPTED"},
        "evidence": {
            "inputValidation": "CONTRACTS_AND_BYTE_LINKS_ONLY",
            "sourceAdmission": "NOT_VERIFIED", "runtimeIsolation": "NOT_VERIFIED",
            "backupSet": "NOT_CAPTURED", "consistentRecoveryPoint": "NOT_VERIFIED",
        },
        "rehearsal": {"status": "NOT_REHEARSED",
                      "plan": "docs/operations/CURRENT_RECOVERY_ENVELOPE_V1.md"},
        "result": RESULT, "deployable": False, "recoveryEligible": False,
        "assertions": {
            "containsBusinessData": False, "containsConfigurationValues": False,
            "containsSecrets": False, "selectsProvider": False,
            "performsBackupOrRestore": False, "mutatesData": False, "deploys": False,
        },
    }
    _validate(output, _schema(SCHEMA_PATH.as_posix()))
    _reject_unsafe_text(output)
    return output


def validate_envelope(envelope_bytes: bytes, manifest_bytes: bytes,
                      inventory_bytes: bytes) -> dict[str, Any]:
    envelope = _strict_json(envelope_bytes)
    _validate(envelope, _schema(SCHEMA_PATH.as_posix()))
    _reject_unsafe_text(envelope)
    expected = build_envelope(manifest_bytes, inventory_bytes)
    _require(_engine()._json_equal(envelope, expected), "envelope_input_mismatch")
    return {
        "summaryVersion": "goymenerp.current-recovery-envelope-summary.v1",
        "result": RESULT, "inputValidation": "CONTRACTS_AND_BYTE_LINKS_ONLY",
        "commit": envelope["source"]["commit"],
        "durableStoreCount": len(envelope["durableStores"]),
        "sourceAdmission": "NOT_VERIFIED", "runtimeIsolation": "NOT_VERIFIED",
        "deployable": False, "recoveryEligible": False,
    }


def _read_file(path: Path) -> bytes:
    with path.open("rb") as stream:
        content = stream.read(MAX_JSON_BYTES + 1)
    _require(len(content) <= MAX_JSON_BYTES, "json_size_or_type")
    return content


class _ArgumentParser(argparse.ArgumentParser):
    def error(self, message: str) -> None:
        # argparse's ordinary error includes unrecognized argument values.
        self.exit(2, "recovery envelope error: invalid_arguments\n")


def main(argv: Sequence[str] | None = None) -> int:
    parser = _ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("build", "validate"))
    parser.add_argument("--release-manifest", required=True, type=Path)
    parser.add_argument("--recovery-inventory", required=True, type=Path)
    parser.add_argument("--envelope", type=Path)
    arguments = parser.parse_args(argv)
    try:
        manifest = _read_file(arguments.release_manifest)
        inventory = _read_file(arguments.recovery_inventory)
        if arguments.command == "build":
            _require(arguments.envelope is None, "build_has_no_envelope_input")
            output = build_envelope(manifest, inventory)
        else:
            _require(arguments.envelope is not None, "envelope_input_required")
            output = validate_envelope(_read_file(arguments.envelope), manifest, inventory)
        sys.stdout.buffer.write(canonical_bytes(output))
        return 0
    except EnvelopeViolation as exc:
        sys.stderr.write("recovery envelope error: " + str(exc) + "\n")
        return 1
    except OSError:
        sys.stderr.write("recovery envelope error: input_or_output_unavailable\n")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
