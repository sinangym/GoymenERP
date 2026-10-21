from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/generate_release_manifest_v2.py"
spec = importlib.util.spec_from_file_location("r021b_test_target", TOOL)
assert spec is not None and spec.loader is not None
tool = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = tool
spec.loader.exec_module(tool)
CTX = "src/backend/Modules/Inventory/InventoryDbContext.cs"
MIG = "src/backend/Modules/Inventory/Persistence/Migrations/20260101000000_First.cs"
DESIGNER = MIG[:-3] + ".Designer.cs"


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


class ManifestV2UnitSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.legacy = tool._frozen_module(tool.V1_GENERATOR)
        cls.recovery = tool._frozen_module(tool.RECOVERY_GENERATOR)
        cls.schema = json.loads((ROOT / tool.SCHEMA).read_text(encoding="utf-8"))

    def entry(self, path, raw):
        if isinstance(raw, str):
            raw = raw.encode("utf-8")
        return self.legacy.GitEntry(path, "100644", "1" * 40, raw)

    def snapshot(self, entries):
        # Structural unit fixture only; not evidence of a real accepted source.
        return self.legacy.SourceSnapshot(ROOT, "2" * 40, "3" * 40, entries)

    def migrations(self, body=None, designer=None):
        entries = {
            CTX: self.entry(CTX, "namespace Example; public sealed class InventoryDbContext { }"),
            MIG: self.entry(MIG, body if body is not None else
                            "namespace Example.Migrations; [DbContext(typeof(InventoryDbContext))] "
                            "[Migration(\"20260101000000_First\")] public sealed class First : Migration { }"),
        }
        if designer is not None:
            entries[DESIGNER] = self.entry(DESIGNER, designer)
        inventory = {"dbContexts": [{"name": "InventoryDbContext", "path": CTX,
                                      "sha256": digest(entries[CTX].content)}],
                     "migrations": [{"path": MIG, "sha256": digest(entries[MIG].content)}],
                     "counts": {"dbContexts": 1, "migrations": 1}}
        return self.snapshot(entries), inventory

    def api(self, document=None, raw=None):
        if raw is None:
            document = document or {"openapi": "3.1.1", "info": {"version": "v1"},
                                    "paths": {"/a": {"get": {"operationId": "ReadA"}}}}
            raw = json.dumps(document).encode()
        entry = self.entry(tool.API, raw)
        return tool._api_contract(self.legacy, self.snapshot({tool.API: entry}))

    def test_api_identity_binds_exact_blob_and_sorted_unique_operation_ids(self):
        document = {"openapi": "3.1.1", "info": {"version": "v1"},
                    "paths": {"/b": {"post": {"operationId": "WriteB"}},
                              "/a": {"get": {"operationId": "ReadA"}}}}
        raw = json.dumps(document, indent=2).encode()
        result = self.api(raw=raw)
        self.assertEqual(digest(raw), result["sha256"])
        self.assertEqual("1" * 40, result["gitBlob"])
        self.assertEqual(2, result["operationCount"])
        self.assertEqual(digest(b"ReadA\nWriteB"), result["operationIdsSha256"])

    def test_duplicate_missing_empty_and_invalid_operation_ids_fail_closed(self):
        for identity in ("ReadA", None, "", "bad identity", "bad\nidentity"):
            with self.subTest(identity=identity):
                document = {"openapi": "3.1.1", "info": {"version": "v1"},
                            "paths": {"/a": {"get": {"operationId": "ReadA"}},
                                      "/b": {"post": {"operationId": identity}}}}
                with self.assertRaises(tool.ManifestV2Error):
                    self.api(document)

    def test_unresolved_ref_unknown_method_empty_paths_and_invalid_versions_fail_closed(self):
        variants = [
            {"/a": {"$ref": "https://example.invalid/path"}},
            {"/a": {"GET": {"operationId": "ReadA"}}}, {},
        ]
        for paths in variants:
            with self.subTest(paths=paths):
                with self.assertRaises(tool.ManifestV2Error):
                    self.api({"openapi": "3.1.1", "info": {"version": "v1"}, "paths": paths})
        for version in ("", "2.0", "3.1.1\n"):
            with self.assertRaises(tool.ManifestV2Error):
                self.api({"openapi": version, "info": {"version": "v1"},
                          "paths": {"/a": {"get": {"operationId": "ReadA"}}}})

    def test_strict_json_rejects_duplicates_nonfinite_invalid_utf8_and_bad_numbers(self):
        for raw in (b'{"a":1,"a":2}', b'{"a":NaN}', b'{"a":Infinity}', b'{', b'\xff'):
            with self.subTest(raw=raw):
                with self.assertRaises(tool.ManifestV2Error):
                    tool._strict_json(raw)
        # OpenAPI schema numbers are not copied into identity fields or rounded.
        self.assertEqual("0.1234567890123456789",
                         str(tool._strict_json(b'{"example":0.1234567890123456789}')["example"]))

    def test_frontend_features_cover_directory_bytes_without_claiming_capabilities(self):
        paths = {tool.WEB_SOURCE + "/sales/Page.tsx": b"sales",
                 tool.WEB_SOURCE + "/sales/Other.ts": b"other",
                 tool.WEB_SOURCE + "/shell/Layout.tsx": b"shell",
                 tool.WEB_SOURCE + "/ui/Table.tsx": b"ui",
                 tool.WEB_SOURCE + "/App.tsx": b"root",
                 tool.WEB_SOURCE + "/assets/logo.svg": b"asset",
                 tool.WEB_SOURCE + "/test/setup.ts": b"test"}
        entries = {path: self.entry(path, raw) for path, raw in paths.items()}
        rows = tool._frontend_features(self.legacy, self.snapshot(entries))
        self.assertEqual(["sales", "shell", "ui"], [row["id"] for row in rows])
        self.assertEqual(["MODULE", "SHELL", "DESIGN_SYSTEM"], [row["kind"] for row in rows])
        expected = self.legacy._source_fingerprint([entries[p] for p in paths if "/sales/" in p])
        self.assertEqual(expected, (rows[0]["sourceFileCount"], rows[0]["sourceSha256"]))
        changed = dict(entries)
        path = tool.WEB_SOURCE + "/sales/Page.tsx"
        changed[path] = self.entry(path, b"changed")
        self.assertNotEqual(rows[0]["sourceSha256"],
                            tool._frontend_features(self.legacy, self.snapshot(changed))[0]["sourceSha256"])

    def test_missing_or_unsupported_frontend_directory_fails_closed(self):
        with self.assertRaises(tool.ManifestV2Error):
            tool._frontend_features(self.legacy, self.snapshot({}))
        path = tool.WEB_SOURCE + "/bad-name/Page.tsx"
        with self.assertRaises(tool.ManifestV2Error):
            tool._frontend_features(self.legacy, self.snapshot({path: self.entry(path, b"source")}))

    def test_handwritten_and_designer_context_bindings_are_supported(self):
        snapshot, inventory = self.migrations()
        self.assertEqual([{"dbContext": "InventoryDbContext",
                           "migrationsDirectory": str(Path(MIG).parent).replace("\\", "/"),
                           "migrationCount": 1, "latestMigration": "20260101000000_First"}],
                         tool._schema_heads(snapshot, self.recovery, inventory))
        snapshot, inventory = self.migrations(
            "namespace Example.Migrations; public partial class First : Migration { }",
            "namespace Example.Migrations { [DbContext(typeof(global::Example.InventoryDbContext))] "
            "partial class First { } }")
        self.assertEqual("InventoryDbContext", tool._schema_heads(snapshot, self.recovery, inventory)[0]["dbContext"])

    def test_comments_strings_and_disabled_code_cannot_supply_context_attribute(self):
        bodies = [
            "namespace Example; // user's [DbContext(typeof(InventoryDbContext))]\npublic class First : Migration { }",
            'namespace Example; public class First : Migration { string x = "[DbContext(typeof(InventoryDbContext))]"; }',
            "namespace Example;\n#if false\n[DbContext(typeof(InventoryDbContext))]\n#endif\npublic class First : Migration { }",
        ]
        for body in bodies:
            with self.subTest(body=body):
                snapshot, inventory = self.migrations(body)
                with self.assertRaises(tool.ManifestV2Error):
                    tool._schema_heads(snapshot, self.recovery, inventory)

    def test_comment_apostrophe_does_not_break_real_context_binding(self):
        snapshot, inventory = self.migrations(
            "namespace Example; // user's comment\n[DbContext(typeof(InventoryDbContext))] "
            "public class First : Migration { }")
        self.assertEqual("InventoryDbContext", tool._schema_heads(snapshot, self.recovery, inventory)[0]["dbContext"])

    def test_attribute_on_another_class_is_not_a_migration_binding(self):
        snapshot, inventory = self.migrations(
            "namespace Example; [DbContext(typeof(InventoryDbContext))] public class Other { } "
            "public class First : Migration { }")
        with self.assertRaisesRegex(tool.ManifestV2Error, "migration_context_ambiguous"):
            tool._schema_heads(snapshot, self.recovery, inventory)

    def test_conflicting_duplicate_unknown_and_wrong_namespace_bindings_fail_closed(self):
        bodies = [
            "[DbContext(typeof(WrongDbContext))] public class First : Migration { }",
            "[DbContext(typeof(Wrong.InventoryDbContext))] public class First : Migration { }",
            "[DbContext(typeof(InventoryDbContext))] [DbContext(typeof(InventoryDbContext))] public class First : Migration { }",
        ]
        for body in bodies:
            snapshot, inventory = self.migrations(body)
            with self.assertRaises(tool.ManifestV2Error):
                tool._schema_heads(snapshot, self.recovery, inventory)
        snapshot, inventory = self.migrations(designer="[DbContext(typeof(InventoryDbContext))] partial class First { }")
        with self.assertRaises(tool.ManifestV2Error):
            tool._schema_heads(snapshot, self.recovery, inventory)

    def test_duplicate_missing_hash_mismatched_and_uncovered_migrations_fail_closed(self):
        for mutation in ("duplicate", "missing", "hash", "coverage"):
            snapshot, inventory = self.migrations()
            if mutation == "duplicate":
                inventory["migrations"].append(copy.deepcopy(inventory["migrations"][0]))
            elif mutation == "missing":
                del snapshot.entries[MIG]
            elif mutation == "hash":
                inventory["migrations"][0]["sha256"] = "0" * 64
            else:
                inventory["counts"]["migrations"] = 2
            with self.subTest(mutation=mutation):
                with self.assertRaises(tool.ManifestV2Error):
                    tool._schema_heads(snapshot, self.recovery, inventory)

    def test_extra_input_tree_requires_all_canonical_regular_noncolliding_paths(self):
        for problem in ("missing", "symlink", "case", "collision"):
            paths = list(tool.EXTRA_INPUTS)
            if problem == "missing":
                paths.pop()
            if problem == "collision":
                paths.append(paths[0].upper())
            records = []
            for index, path in enumerate(paths):
                mode = "120000" if problem == "symlink" and index == 0 else "100644"
                if problem == "case" and index == 0:
                    path = path.upper()
                records.append(f"{mode} blob {'1' * 40}\t{path}".encode())
            raw = b"\0".join(records) + b"\0"
            def run(root, args):
                return SimpleNamespace(returncode=0, stdout=raw if args[0] == "ls-tree" else b"source")
            with self.subTest(problem=problem), patch.object(self.legacy, "_run_git", side_effect=run):
                with self.assertRaises(tool.ManifestV2Error):
                    tool._extra_entries(self.legacy, self.snapshot({}))

    def test_v2_schema_retains_all_v1_negative_evidence_constants(self):
        old = json.loads((ROOT / "contracts/release/release-manifest-v1.schema.json").read_text(encoding="utf-8"))
        for key in ("compatibility", "evidence", "assertions", "result", "deployable"):
            self.assertEqual(old["properties"][key], self.schema["properties"][key])
        self.assertEqual("goymenerp.release-manifest.v2", self.schema["properties"]["schemaVersion"]["const"])
        self.assertEqual(len(tool.PROVENANCE_INPUTS), self.schema["properties"]["v2SourceInputs"]["minItems"])

    def test_every_schema_object_is_closed_and_unknown_nested_fields_are_refused(self):
        def check(schema):
            if schema.get("type") == "object":
                self.assertIs(False, schema["additionalProperties"])
            for child in schema.get("properties", {}).values():
                check(child)
            if "items" in schema:
                check(schema["items"])
        check(self.schema)
        item = self.schema["properties"]["apiContract"]
        with self.assertRaises(self.recovery.InventoryError):
            self.recovery._validate_against_schema({"secret": "synthetic"}, item)

    def test_readonly_v1_sources_are_exactly_the_accepted_bytes(self):
        for path, sha in tool.PINS.items():
            with self.subTest(path=path):
                self.assertEqual(sha, digest((ROOT / path).read_bytes()))
        self.assertEqual("7650dc4106a3b2a61f5ce4e68a39b90c3ceb94c6339a3d0c00a19901dd0031c6",
                         digest((ROOT / tool.V1_TEST).read_bytes()))

    def test_executed_v2_copy_and_changed_worktree_input_are_refused(self):
        paths = set(tool.EXTRA_INPUTS) | set(tool.PINS) | {tool.V1_TEST}
        entries = {path: self.entry(path, (ROOT / path).read_bytes()) for path in paths}
        snapshot = self.snapshot(entries)
        path = tool.API
        changed = dict(entries)
        changed[path] = self.entry(path, b"changed committed bytes")
        with self.assertRaisesRegex(tool.ManifestV2Error, "worktree_blob_mismatch"):
            tool._verify_worktree_and_code(self.legacy, self.snapshot(changed))
        with tempfile.TemporaryDirectory() as directory:
            different = Path(directory) / "producer.py"
            different.write_bytes(b"# different executed producer\n")
            with patch.object(tool, "__file__", str(different)):
                with self.assertRaisesRegex(tool.ManifestV2Error, "executed_v2_mismatch"):
                    tool._verify_worktree_and_code(self.legacy, snapshot)

    def test_cli_argument_failure_does_not_echo_private_values(self):
        run = subprocess.run([sys.executable, "-I", "-S", "-B", str(TOOL),
                              "Password=synthetic-private-marker"], capture_output=True, timeout=30)
        self.assertEqual(2, run.returncode)
        self.assertEqual(b"", run.stdout)
        self.assertNotIn(b"synthetic-private-marker", run.stderr)
        self.assertNotIn(b"Traceback", run.stderr)


class ExactCommittedSourceTests(unittest.TestCase):
    """Future authorized slot: actual clean Git proof, never environment execution."""

    @classmethod
    def setUpClass(cls):
        cls.legacy = tool._frozen_module(tool.V1_GENERATOR)
        cls.commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()

    def test_exact_source_v2_is_deterministic_and_v1_still_produces_v1(self):
        manifest = tool.build_manifest(ROOT, self.commit)
        again = tool.build_manifest(ROOT, self.commit)
        self.assertEqual(self.legacy.canonical_bytes(manifest), self.legacy.canonical_bytes(again))
        self.assertEqual(tool.VERSION, manifest["schemaVersion"])
        self.assertEqual(set(tool.PROVENANCE_INPUTS), {row["path"] for row in manifest["v2SourceInputs"]})
        self.assertEqual(tool.API, manifest["apiContract"]["path"])
        self.assertEqual(manifest["recoveryInventory"]["counts"]["migrations"],
                         sum(row["migrationCount"] for row in manifest["schemaHeads"]))
        self.assertEqual("goymenerp.release-manifest.v1",
                         self.legacy.build_manifest(ROOT, self.commit)["schemaVersion"])
        self.assertIs(False, manifest["deployable"])
        self.assertEqual("UNVERIFIED", manifest["result"])
        tool.validate_manifest(ROOT, self.commit, self.legacy.canonical_bytes(manifest))

    def test_wrong_head_and_hidden_index_source_are_refused(self):
        with self.assertRaises(tool.ManifestV2Error):
            tool.build_manifest(ROOT, "0" * 40)
        with patch.object(self.legacy, "_require_index_flags_clean", side_effect=self.legacy.ReleaseManifestError("hidden")):
            with self.assertRaises(tool.ManifestV2Error):
                tool.build_manifest(ROOT, self.commit)
        with patch.object(self.legacy, "_require_clean", side_effect=self.legacy.ReleaseManifestError("dirty")):
            with self.assertRaises(tool.ManifestV2Error):
                tool.build_manifest(ROOT, self.commit)


if __name__ == "__main__":
    unittest.main()
