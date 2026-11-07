from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
TOOL_PATH = ROOT / "tools/validate_current_recovery_envelope.py"
spec = importlib.util.spec_from_file_location("r051_envelope_test_target", TOOL_PATH)
assert spec is not None and spec.loader is not None
tool = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = tool
spec.loader.exec_module(tool)

INVENTORY_SCHEMA = json.loads((ROOT / tool.INVENTORY_SCHEMA).read_text(encoding="utf-8"))
MANIFEST_SCHEMA = json.loads((ROOT / tool.MANIFEST_SCHEMA).read_text(encoding="utf-8"))
COMMIT = "1" * 40
TREE = "2" * 40


def content(value) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, allow_nan=False) + "\n").encode()


def digest(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def synthetic_inventory() -> dict:
    """Complete v1-shaped synthetic references, never real source-admission proof.

    Counts/required shapes come from the existing v1 schema, not a copied report
    count or a partial four-store fixture that bypasses its real contract.
    """
    properties = INVENTORY_SCHEMA["properties"]
    counts = {key: value["const"] for key, value in properties["counts"]["properties"].items()}
    contexts = [
        {"name": f"Synthetic{index}DbContext",
         "path": f"src/backend/Modules/Accounting/Synthetic{index}DbContext.cs", "sha256": "a" * 64}
        for index in range(counts["dbContexts"])
    ]
    migrations = [
        {"path": f"src/backend/Modules/Accounting/Migrations/20260101{index:06d}_Synthetic{index}.cs",
         "sha256": "b" * 64}
        for index in range(counts["migrations"])
    ]
    kinds = ("idempotency", "outbox", "inbox", "delivery")
    stores = [
        {"id": f"inventory.synthetic-{index}", "module": "Inventory",
         "kind": kinds[index % len(kinds)], "dbContext": contexts[0]["name"],
         "entityType": f"Synthetic{index}", "schema": "inventory", "table": f"Synthetic{index}",
         "contracts": [], "sourceFiles": [
             {"path": f"src/backend/Modules/Inventory/Synthetic{index}.cs", "sha256": "c" * 64}]}
        for index in range(counts["durableStores"])
    ]
    document_schema = properties["documents"]["properties"]
    documents = {
        key: copy.deepcopy(document_schema[key]["const"])
        for key in ("configurationKeys", "objectKeyPattern", "pathClassifications")
    }
    documents["objectStoreAdapters"] = [
        {"name": f"SyntheticAdapter{index}",
         "path": f"src/backend/Modules/Documents/SyntheticAdapter{index}.cs"}
        for index in range(document_schema["objectStoreAdapters"]["minItems"])
    ]
    documents["sourceFiles"] = [
        {"path": f"src/backend/Modules/Documents/Synthetic{index}.cs", "sha256": "d" * 64}
        for index in range(document_schema["sourceFiles"]["minItems"])
    ]
    return {
        "schemaVersion": properties["schemaVersion"]["const"], "inventoryMode": "SOURCE_ONLY",
        "sourceCommit": COMMIT, "counts": counts, "dbContexts": contexts,
        "migrations": migrations, "documents": documents, "durableStores": stores,
        "assertions": {key: row["const"] for key, row in properties["assertions"]["properties"].items()},
    }


def synthetic_manifest(inventory_bytes: bytes) -> dict:
    properties = MANIFEST_SCHEMA["properties"]
    records = []
    for path in (tool.INVENTORY_SCHEMA, tool.ENGINE_PATH,
                 "tests/recovery_surface_inventory/test_generate_recovery_surface_inventory.py"):
        records.append({"path": path, "gitBlob": "3" * 40,
                        "sha256": tool.TRUSTED_CONTRACT_SOURCES.get(path, "e" * 64)})
    return {
        "schemaVersion": tool.MANIFEST_VERSION, "manifestMode": "SOURCE_ONLY",
        "releaseId": "source:" + COMMIT,
        "source": {"repository": "https://github.com/sinangym/GoymenERP", "commit": COMMIT,
                   "tree": TREE, "cleanCheckout": True},
        "components": [
            {"id": "backend-api", "kind": "BACKEND_API", "sourceRoot": "src/backend",
             "sourceFileCount": 1, "sourceSha256": "f" * 64, "artifactStatus": "NOT_BUILT"},
            {"id": "frontend-web", "kind": "FRONTEND_WEB", "sourceRoot": "src/frontend/goymen-erp-web",
             "sourceFileCount": 1, "sourceSha256": "e" * 64, "artifactStatus": "NOT_BUILT"},
        ],
        "recoveryInventory": {"schemaVersion": "goymenerp.recovery-surface-inventory.v1",
                              "inventoryMode": "SOURCE_ONLY", "inventorySha256": digest(inventory_bytes),
                              "counts": json.loads(inventory_bytes)["counts"], "sources": records},
        "configuration": {"classification": "KEY_NAMES_ONLY", "backendKeys": ["Documents:Scanner:Mode"],
                          "frontendKeys": ["VITE_API_BASE_URL"], "sourceFiles": [
                              {"path": "src/backend/Synthetic.cs", "gitBlob": "3" * 40, "sha256": "a" * 64}]},
        "features": [{"id": "Inventory", "sourcePath": "src/backend/Modules/Inventory",
                      "sourceFileCount": 1, "sourceSha256": "c" * 64, "status": "SOURCE_PRESENT"}],
        "compatibility": {key: row["const"] for key, row in properties["compatibility"]["properties"].items()},
        "evidence": {key: row["const"] for key, row in properties["evidence"]["properties"].items()},
        "result": "UNVERIFIED", "deployable": False,
        "assertions": {key: row["const"] for key, row in properties["assertions"]["properties"].items()},
    }


class RecoveryEnvelopeContractTests(unittest.TestCase):
    def setUp(self):
        self.inventory = synthetic_inventory()
        self.inventory_bytes = content(self.inventory)
        self.manifest = synthetic_manifest(self.inventory_bytes)
        self.manifest_bytes = content(self.manifest)
        self.envelope = tool.build_envelope(self.manifest_bytes, self.inventory_bytes)

    def validate(self, envelope=None):
        return tool.validate_envelope(content(self.envelope if envelope is None else envelope),
                                      self.manifest_bytes, self.inventory_bytes)

    def refused_input(self, inventory=None, manifest=None):
        inventory_bytes = content(self.inventory if inventory is None else inventory)
        candidate = synthetic_manifest(inventory_bytes) if manifest is None else manifest
        with self.assertRaises(tool.EnvelopeViolation):
            tool.build_envelope(content(candidate), inventory_bytes)

    def test_contract_success_never_claims_source_admission_or_recovery_eligibility(self):
        result = self.validate()
        self.assertEqual("SOURCE_ONLY_NOT_REHEARSED", result["result"])
        self.assertEqual("CONTRACTS_AND_BYTE_LINKS_ONLY", result["inputValidation"])
        self.assertEqual("NOT_VERIFIED", result["sourceAdmission"])
        self.assertEqual("NOT_VERIFIED", result["runtimeIsolation"])
        self.assertIs(False, result["deployable"])
        self.assertIs(False, result["recoveryEligible"])
        self.assertEqual(len(self.inventory["durableStores"]), result["durableStoreCount"])

    def test_output_is_deterministic_and_contains_every_declared_store_identity(self):
        second = tool.build_envelope(self.manifest_bytes, self.inventory_bytes)
        self.assertEqual(tool.canonical_bytes(self.envelope), tool.canonical_bytes(second))
        self.assertEqual({row["id"] for row in self.inventory["durableStores"]},
                         {row["id"] for row in self.envelope["durableStores"]})
        for row in self.envelope["durableStores"]:
            self.assertEqual({"status": "NOT_CAPTURED", "highWaterMark": None, "keySetSha256": None},
                             row["reconciliation"])

    def test_missing_extra_and_duplicated_envelope_store_are_refused(self):
        changes = (lambda rows: rows.pop(), lambda rows: rows.append(copy.deepcopy(rows[0])),
                   lambda rows: rows.append({**rows[0], "id": "inventory.unrecognized"}))
        for change in changes:
            with self.subTest(change=change):
                candidate = copy.deepcopy(self.envelope)
                change(candidate["durableStores"])
                with self.assertRaises(tool.EnvelopeViolation):
                    self.validate(candidate)

    def test_altered_store_rule_or_identity_and_captured_checkpoint_are_refused(self):
        for field, value in (("recoveryRule", "RESTORE_AND_KEEP_DEDUP"),
                             ("table", "AnotherTable"), ("dbContext", "AnotherDbContext"),
                             ("reconciliation", {"status": "CAPTURED", "highWaterMark": "123", "keySetSha256": "a" * 64})):
            with self.subTest(field=field):
                candidate = copy.deepcopy(self.envelope)
                row = next(row for row in candidate["durableStores"] if row["kind"] == "outbox")
                row[field] = value
                with self.assertRaises(tool.EnvelopeViolation):
                    self.validate(candidate)

    def test_running_unknown_disabled_and_not_present_are_not_verified_effect_evidence(self):
        for field in tool.EXTERNAL_EFFECTS:
            for state in ("RUNNING", "UNKNOWN", "DISABLED", "NOT_PRESENT", "NOT_STARTED"):
                with self.subTest(field=field, state=state):
                    candidate = copy.deepcopy(self.envelope)
                    candidate["externalEffects"][field] = state
                    with self.assertRaises(tool.EnvelopeViolation):
                        self.validate(candidate)

    def test_fake_rehearsal_backup_source_admission_and_deployment_are_refused(self):
        mutations = [
            ("rehearsal", {"status": "REHEARSED", "plan": self.envelope["rehearsal"]["plan"]}),
            ("result", "RESTORED"), ("deployable", True), ("recoveryEligible", True),
            ("evidence", {**self.envelope["evidence"], "sourceAdmission": "ACCEPTED"}),
            ("evidence", {**self.envelope["evidence"], "backupSet": "CAPTURED"}),
        ]
        for field, value in mutations:
            with self.subTest(field=field):
                candidate = copy.deepcopy(self.envelope)
                candidate[field] = value
                with self.assertRaises(tool.EnvelopeViolation):
                    self.validate(candidate)

    def test_object_manifest_hash_and_new_fields_are_not_unearned_evidence(self):
        for target in ("objectInventory", "extra"):
            candidate = copy.deepcopy(self.envelope)
            if target == "objectInventory":
                candidate["documents"][target]["manifestSha256"] = "a" * 64
            else:
                candidate["documents"][target] = "unreviewed"
            with self.assertRaises(tool.EnvelopeViolation):
                self.validate(candidate)

    def test_omitted_inventory_surface_is_refused_even_with_a_relinked_hash(self):
        for key in ("dbContexts", "migrations", "durableStores"):
            with self.subTest(key=key):
                inventory = copy.deepcopy(self.inventory)
                inventory[key].pop()
                inventory["counts"][key] -= 1
                self.refused_input(inventory=inventory)

    def test_duplicate_identity_with_distinct_metadata_is_refused(self):
        for key, field in (("dbContexts", "name"), ("migrations", "path"), ("durableStores", "id")):
            with self.subTest(key=key):
                inventory = copy.deepcopy(self.inventory)
                inventory[key][1][field] = inventory[key][0][field]
                if key == "migrations":
                    inventory[key][1]["sha256"] = "f" * 64
                self.refused_input(inventory=inventory)

    def test_unknown_store_context_and_conflicting_source_fingerprint_are_refused(self):
        inventory = copy.deepcopy(self.inventory)
        inventory["durableStores"][0]["dbContext"] = "UnknownDbContext"
        self.refused_input(inventory=inventory)
        inventory = copy.deepcopy(self.inventory)
        inventory["durableStores"][1]["sourceFiles"][0]["path"] = inventory["durableStores"][0]["sourceFiles"][0]["path"]
        inventory["durableStores"][1]["sourceFiles"][0]["sha256"] = "f" * 64
        self.refused_input(inventory=inventory)

    def test_source_release_identity_hash_counts_and_contract_source_set_must_match(self):
        changes = [
            lambda m: m["source"].update(commit="9" * 40),
            lambda m: m.update(releaseId="source:" + "9" * 40),
            lambda m: m["recoveryInventory"].update(inventorySha256="0" * 64),
            lambda m: m["recoveryInventory"]["counts"].update(durableStores=1),
            lambda m: m["recoveryInventory"]["sources"][0].update(path="tools/unknown.py"),
        ]
        for change in changes:
            with self.subTest(change=change):
                manifest = copy.deepcopy(self.manifest)
                change(manifest)
                self.refused_input(manifest=manifest)

    def test_non_source_only_unverified_and_v2_manifest_claims_are_refused(self):
        for field, value in (("schemaVersion", "goymenerp.release-manifest.v2"),
                             ("deployable", True), ("result", "VERIFIED"), ("manifestMode", "DEPLOYMENT")):
            with self.subTest(field=field):
                manifest = copy.deepcopy(self.manifest)
                manifest[field] = value
                self.refused_input(manifest=manifest)

    def test_component_feature_and_frozen_contract_identities_cannot_be_relabelled(self):
        changes = [
            lambda m: m["components"][0].update(kind="FRONTEND_WEB"),
            lambda m: m["features"][0].update(sourcePath="src/backend/Modules/Sales"),
            lambda m: m["recoveryInventory"]["sources"][0].update(sha256="0" * 64),
        ]
        for change in changes:
            with self.subTest(change=change):
                manifest = copy.deepcopy(self.manifest)
                change(manifest)
                self.refused_input(manifest=manifest)

    def test_full_input_schema_rejects_missing_sections_and_unknown_nested_fields(self):
        manifest = copy.deepcopy(self.manifest)
        del manifest["compatibility"]
        self.refused_input(manifest=manifest)
        inventory = copy.deepcopy(self.inventory)
        inventory["documents"]["sourceFiles"][0]["content"] = "unreviewed"
        self.refused_input(inventory=inventory)
        inventory = copy.deepcopy(self.inventory)
        inventory["documents"]["pathClassifications"].pop()
        self.refused_input(inventory=inventory)

    def test_bool_integer_and_numeric_const_aliases_are_refused(self):
        for key, value in (("deployable", 0),):
            candidate = copy.deepcopy(self.envelope)
            candidate[key] = value
            with self.assertRaises(tool.EnvelopeViolation):
                self.validate(candidate)
        inventory = copy.deepcopy(self.inventory)
        inventory["counts"]["dbContexts"] = True
        self.refused_input(inventory=inventory)

    def test_duplicate_keys_nonfinite_float_malformed_utf8_and_size_are_refused(self):
        invalid = (b'{"schemaVersion":1,"schemaVersion":2}', b'{"x":NaN}',
                   b'{"x":Infinity}', b'{"x":1.0}', b'{"x":', b'\xff',
                   b' ' * (tool.MAX_JSON_BYTES + 1))
        for raw in invalid:
            with self.subTest(prefix=raw[:20]):
                with self.assertRaises(tool.EnvelopeViolation):
                    tool.build_envelope(raw, self.inventory_bytes)
                with self.assertRaises(tool.EnvelopeViolation):
                    tool.validate_envelope(raw, self.manifest_bytes, self.inventory_bytes)

    def test_untrusted_values_paths_commands_and_control_text_are_refused_without_echo(self):
        for text in ("Server=example;Password=synthetic;", "token=synthetic",
                     "RESTORE DATABASE Synthetic", "aws s3 cp synthetic .",
                     "D:\\Synthetic\\Objects", "../Synthetic", "bad\nkey"):
            with self.subTest(text=text):
                manifest = copy.deepcopy(self.manifest)
                manifest["configuration"]["backendKeys"] = [text]
                with self.assertRaises(tool.EnvelopeViolation) as raised:
                    tool.build_envelope(content(manifest), self.inventory_bytes)
                self.assertNotIn(text, str(raised.exception))

    def test_input_byte_hash_changes_even_for_semantically_equal_json(self):
        spaced = json.dumps(self.inventory, indent=2).encode()
        self.assertNotEqual(digest(spaced), digest(self.inventory_bytes))
        with self.assertRaisesRegex(tool.EnvelopeViolation, "inventory_hash_mismatch"):
            tool.build_envelope(self.manifest_bytes, spaced)
        envelope = tool.build_envelope(content(synthetic_manifest(spaced)), spaced)
        self.assertEqual(digest(spaced), envelope["source"]["recoveryInventorySha256"])

    def test_changed_fixed_validator_is_rejected_before_any_source_execution(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / tool.ENGINE_PATH
            path.parent.mkdir(parents=True)
            path.write_text("raise AssertionError('must not execute')\n", encoding="utf-8")
            with patch.object(tool, "ROOT", root):
                with self.assertRaisesRegex(tool.EnvelopeViolation, "contract_source_changed"):
                    tool._trusted_bytes(tool.ENGINE_PATH)

    def test_cli_failure_has_no_stdout_or_sensitive_payload_echo(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "manifest.json"
            inventory = root / "inventory.json"
            bad = copy.deepcopy(self.manifest)
            bad["configuration"]["backendKeys"] = ["Password=synthetic-private-marker"]
            manifest.write_bytes(content(bad))
            inventory.write_bytes(self.inventory_bytes)
            run = subprocess.run([sys.executable, "-I", "-S", "-B", str(TOOL_PATH), "build",
                                  "--release-manifest", str(manifest), "--recovery-inventory", str(inventory)],
                                 capture_output=True, timeout=30)
            self.assertNotEqual(0, run.returncode)
            self.assertEqual(b"", run.stdout)
            self.assertNotIn(b"synthetic-private-marker", run.stderr)
            self.assertNotIn(b"Traceback", run.stderr)

    def test_cli_argument_errors_do_not_echo_unrecognized_values(self):
        run = subprocess.run([sys.executable, "-I", "-S", "-B", str(TOOL_PATH),
                              "Password=synthetic-private-marker"], capture_output=True, timeout=30)
        self.assertEqual(2, run.returncode)
        self.assertEqual(b"", run.stdout)
        self.assertNotIn(b"synthetic-private-marker", run.stderr)
        self.assertNotIn(b"Traceback", run.stderr)


if __name__ == "__main__":
    unittest.main()
