from __future__ import annotations

import ast
import copy
import hashlib
import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path
from unittest import mock


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
GENERATOR_PATH = REPOSITORY_ROOT / "tools/generate_recovery_surface_inventory.py"
SOURCE_COMMIT = subprocess.run(
    ["git", "rev-parse", "--verify", "HEAD^{commit}"],
    cwd=REPOSITORY_ROOT,
    check=True,
    capture_output=True,
    text=True,
    encoding="utf-8",
).stdout.strip()

MODULE_SPEC = importlib.util.spec_from_file_location(
    "generate_recovery_surface_inventory", GENERATOR_PATH
)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError("Recovery inventory generator cannot be loaded.")
generator = importlib.util.module_from_spec(MODULE_SPEC)
sys.modules[MODULE_SPEC.name] = generator
MODULE_SPEC.loader.exec_module(generator)


EXPECTED_STORES = {
    ("accounting", "Commands"): ("AccountingCommandRow", "idempotency", "AccountingDbContext"),
    ("accounting", "Deliveries"): ("DeliveryRow", "inbox", "AccountingDbContext"),
    ("documents", "DocumentAttempts"): ("DocumentAttemptRow", "idempotency", "DocumentsDbContext"),
    ("finance", "Commands"): ("FinanceCommandRow", "idempotency", "FinanceDbContext"),
    ("finance", "OfficialInvoiceImportCommands"): (
        "FinanceOfficialInvoiceImportCommandRow", "idempotency", "FinanceDbContext"
    ),
    ("finance", "OfficialInvoiceImportWorkDeliveries"): (
        "FinanceOfficialInvoiceImportWorkDeliveryRow", "delivery", "FinanceDbContext"
    ),
    ("finance", "OpenItemSourceBatches"): (
        "FinanceOpenItemSourceBatchRow", "idempotency", "FinanceDbContext"
    ),
    ("idempotency", "CommandExecutions"): (
        "CommandExecution", "idempotency", "CommandExecutionDbContext"
    ),
    ("idempotency", "GroupSharedCommandTerminals"): (
        "GroupSharedCommandTerminalRecord", "idempotency", "GroupSharedCommandDbContext"
    ),
    ("inventory", "Commands"): ("InventoryCommandRow", "idempotency", "InventoryDbContext"),
    ("inventory", "OutboxMessages"): ("InventoryOutboxRow", "outbox", "InventoryDbContext"),
    ("logistics", "CustomerReturnAttempts"): (
        "CustomerReturnAttemptRow", "idempotency", "LogisticsDbContext"
    ),
    ("logistics", "LoadingAttempts"): (
        "LoadingAttemptRow", "idempotency", "LogisticsDbContext"
    ),
    ("manufacturing", "Commands"): ("CommandRow", "idempotency", "ManufacturingDbContext"),
    ("product", "PriceListAttempts"): (
        "PriceListAttemptRow", "idempotency", "ProductPriceListDbContext"
    ),
    ("purchasing", "PurchaseAcceptanceShares"): (
        "PurchaseAcceptanceRow", "idempotency", "PurchasingDbContext"
    ),
    ("purchasing", "PurchaseAttempts"): (
        "PurchaseAttemptRow", "idempotency", "PurchasingDbContext"
    ),
    ("sales", "DraftAttempts"): ("DraftAttemptRow", "idempotency", "SalesDbContext"),
    ("work", "ObservationInbox"): ("WorkObservationInboxRow", "inbox", "WorkDbContext"),
    ("work", "OutboxMessages"): ("WorkOutboxRow", "outbox", "WorkDbContext"),
}


def mutate_snapshot(snapshot: object, relative_path: str, transform: object) -> object:
    files = dict(snapshot.files)  # type: ignore[attr-defined]
    original = files[relative_path].decode("utf-8")
    updated = transform(original)  # type: ignore[operator]
    if updated == original:
        raise AssertionError(f"Test mutation did not change {relative_path}.")
    files[relative_path] = updated.encode("utf-8")
    return generator.SourceSnapshot(snapshot.repository_root, snapshot.source_commit, files)  # type: ignore[attr-defined]


class RecoverySurfaceInventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.snapshot = generator._load_source_snapshot(REPOSITORY_ROOT, SOURCE_COMMIT)
        cls.inventory = generator._build_inventory_from_snapshot(cls.snapshot)

    def test_current_source_inventory_is_exact_and_schema_valid(self) -> None:
        self.assertEqual(
            {"dbContexts": 18, "migrations": 91, "durableStores": 20},
            self.inventory["counts"],
        )
        actual = {
            (store["schema"], store["table"]): (
                store["entityType"], store["kind"], store["dbContext"]
            )
            for store in self.inventory["durableStores"]
        }
        self.assertEqual(EXPECTED_STORES, actual)
        self.assertEqual(20, len(actual))
        self.assertEqual(self.inventory, generator.build_inventory(REPOSITORY_ROOT, SOURCE_COMMIT))
        generator.validate_inventory(REPOSITORY_ROOT, self.inventory)

    def test_every_store_has_mapping_migration_and_live_writer_evidence(self) -> None:
        specs_by_id = {spec.identifier: spec for spec in generator.DURABLE_STORE_SPECS}
        self.assertEqual(20, len(specs_by_id))
        for record in self.inventory["durableStores"]:
            spec = specs_by_id[record["id"]]
            tokens = [token for evidence in spec.evidence for token in evidence.required_tokens]
            paths = {source["path"] for source in record["sourceFiles"]}
            self.assertEqual({evidence.path for evidence in spec.evidence}, paths)
            self.assertTrue(any("/Migrations/" in path for path in paths), record["id"])
            self.assertTrue(any("ToTable" in token for token in tokens), record["id"])
            self.assertTrue(
                any(
                    token == f'Schema = "{record["schema"]}"'
                    or token == f'ToTable("{record["table"]}", "{record["schema"]}"'
                    or (
                        record["id"] == "idempotency.command-executions"
                        and token == f'"{record["schema"]}",'
                    )
                    for token in tokens
                ),
                record["id"],
            )
            self.assertTrue(
                any(".Add" in token or "InsertAsync" in token for token in tokens),
                record["id"],
            )

    def test_snapshot_discovery_is_exact_and_excludes_only_audit_metadata(self) -> None:
        discovered = generator._discover_idempotency_key_tables(self.snapshot)
        self.assertEqual(generator.EXPECTED_IDEMPOTENCY_KEY_TABLES, discovered)
        self.assertEqual(16, len(discovered))
        replay_tables = set(discovered) - set(generator.METADATA_ONLY_IDEMPOTENCY_KEY_TABLES)
        specified = {
            (spec.schema, spec.table)
            for spec in generator.DURABLE_STORE_SPECS
            if spec.kind == "idempotency"
        }
        self.assertEqual(15, len(replay_tables))
        self.assertEqual(specified, replay_tables)
        self.assertEqual((("audit", "AuditEvents"),), generator.METADATA_ONLY_IDEMPOTENCY_KEY_TABLES)

    def test_new_snapshot_replay_table_fails_closed(self) -> None:
        unexpected = tuple(
            sorted((*generator.EXPECTED_IDEMPOTENCY_KEY_TABLES, ("test", "NewReplay")))
        )
        with mock.patch.object(
            generator, "_discover_idempotency_key_tables", return_value=unexpected
        ):
            with self.assertRaisesRegex(generator.InventoryError, "surface drifted"):
                generator._require_discovered_durable_surfaces(self.snapshot)

    def test_generic_message_delivery_discovery_is_exhaustive_and_fail_closed(self) -> None:
        expected_message = set(generator.EXPECTED_MESSAGE_DELIVERY_TABLES)
        expected_business = set(generator.NON_MESSAGE_DELIVERY_TABLES)
        discovered = set(generator._discover_message_delivery_tables(self.snapshot))
        self.assertTrue(expected_message.isdisjoint(expected_business))
        self.assertEqual(8, len(discovered))
        self.assertEqual(expected_message | expected_business, discovered)

        live_snippets = (
            'modelBuilder.Entity<RecoveryDeliveryRow>().ToTable("RecoveryDeliveries", Schema);',
            'modelBuilder.Entity<RecoveryDeliveryRow>().ToTable("RecoveryDeliveries", "accounting");',
            'modelBuilder.Entity<RecoveryDeliveryRow>().ToTable("RecoveryDeliveries");',
        )
        for snippet in live_snippets:
            with self.subTest(snippet=snippet):
                changed = mutate_snapshot(
                    self.snapshot,
                    generator.ACCOUNTING_CONTEXT,
                    lambda text, snippet=snippet: text + f"\n{snippet}\n",
                )
                with self.assertRaisesRegex(generator.InventoryError, "source surface drifted"):
                    generator._require_discovered_durable_surfaces(changed)

        for comment in (
            '// modelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema);',
            '/* modelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema); */',
        ):
            with self.subTest(comment=comment):
                changed = mutate_snapshot(
                    self.snapshot,
                    generator.ACCOUNTING_CONTEXT,
                    lambda text, comment=comment: text + f"\n{comment}\n",
                )
                self.assertEqual(discovered, set(generator._discover_message_delivery_tables(changed)))

    def test_documents_scanner_hash_and_transient_cleanup_are_bound(self) -> None:
        scanner_path = generator.DOCUMENT_SOURCE_PATHS[4]
        document_sources = {
            record["path"]: record["sha256"]
            for record in self.inventory["documents"]["sourceFiles"]
        }
        self.assertEqual(6, len(document_sources))
        self.assertEqual(
            hashlib.sha256(self.snapshot.files[scanner_path]).hexdigest(),
            document_sources[scanner_path],
        )

        mutations = (
            lambda text: text.replace(
                "var copy = Path.Combine(options.QuarantineDirectory",
                "var copy = Path.Combine(options.OtherDirectory",
                1,
            ),
            lambda text: text.replace("TryDelete(copy);", "// TryDelete(copy);", 1),
            lambda text: text.replace("File.Delete(path);", "// File.Delete(path);", 1),
            lambda text: text.replace("File.Delete(path);", "GC.KeepAlive(path);", 1).replace(
                "private static void TryDelete(string path)",
                "private static void OtherDelete(string path) { File.Delete(path); }\n\n"
                "    private static void TryDelete(string path)",
                1,
            ),
        )
        for transform in mutations:
            with self.subTest(transform=transform):
                changed = mutate_snapshot(self.snapshot, scanner_path, transform)
                with self.assertRaises(generator.InventoryError):
                    generator._documents_inventory(changed)

    def test_mapping_and_schema_anchor_mutations_fail_closed(self) -> None:
        mutations = (
            (
                generator.ACCOUNTING_CONTEXT,
                lambda text: text.replace(
                    "Entity<AccountingCommandRow>()", "Entity<UnexpectedAccountingCommandRow>()", 1
                ),
            ),
            (
                generator.WORK_CONTEXT,
                lambda text: text.replace('Schema = "work"', 'Schema = "renamed"', 1),
            ),
        )
        for path, transform in mutations:
            with self.subTest(path=path):
                changed = mutate_snapshot(self.snapshot, path, transform)
                with self.assertRaisesRegex(generator.InventoryError, "source (?:anchor|surface) drifted"):
                    generator._durable_store_inventory(changed)

    def test_comment_only_writer_anchor_fails_closed(self) -> None:
        token = "dbContext.Commands.Add(new AccountingCommandRow"
        for replacement in (f"// {token}", f"/* {token} */"):
            with self.subTest(replacement=replacement[:2]):
                changed = mutate_snapshot(
                    self.snapshot,
                    generator.ACCOUNTING_STORE,
                    lambda text, replacement=replacement: text.replace(token, replacement, 1),
                )
                with self.assertRaisesRegex(generator.InventoryError, "source anchor drifted"):
                    generator._durable_store_inventory(changed)

    def test_context_and_migration_path_sets_fail_closed(self) -> None:
        contexts = [record["path"] for record in self.inventory["dbContexts"]]
        migrations = [record["path"] for record in self.inventory["migrations"]]
        generator._require_path_set(
            "DbContext", contexts, generator.EXPECTED_DB_CONTEXT_COUNT,
            generator.EXPECTED_DB_CONTEXT_PATH_FINGERPRINT,
        )
        generator._require_path_set(
            "Migration", migrations, generator.EXPECTED_MIGRATION_COUNT,
            generator.EXPECTED_MIGRATION_PATH_FINGERPRINT,
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext surface drifted"):
            generator._require_path_set(
                "DbContext", [*contexts, "src/backend/NewDbContext.cs"],
                generator.EXPECTED_DB_CONTEXT_COUNT,
                generator.EXPECTED_DB_CONTEXT_PATH_FINGERPRINT,
            )
        with self.assertRaisesRegex(generator.InventoryError, "Migration surface drifted"):
            generator._require_path_set(
                "Migration", migrations[:-1], generator.EXPECTED_MIGRATION_COUNT,
                generator.EXPECTED_MIGRATION_PATH_FINGERPRINT,
            )

    def test_provenance_rejects_arbitrary_commit_and_nested_root(self) -> None:
        with self.assertRaisesRegex(generator.InventoryError, "current Git HEAD"):
            generator.build_inventory(REPOSITORY_ROOT, "0" * 40)
        with self.assertRaisesRegex(generator.InventoryError, "Git top-level"):
            generator.build_inventory(REPOSITORY_ROOT / "src", SOURCE_COMMIT)
        changed = copy.deepcopy(self.inventory)
        changed["sourceCommit"] = "0" * 40
        with self.assertRaisesRegex(generator.InventoryError, "current Git HEAD"):
            generator.validate_inventory(REPOSITORY_ROOT, changed)

    def test_provenance_rejects_untracked_inventory_shadow(self) -> None:
        original = generator._run_git_text

        def fake_git(root: Path, arguments: object) -> subprocess.CompletedProcess[str]:
            if tuple(arguments)[0] == "status":  # type: ignore[arg-type]
                return subprocess.CompletedProcess(
                    ["git", "status"], 0,
                    stdout="?? src/backend/Nested/ShadowDbContext.cs\x00", stderr="",
                )
            return original(root, arguments)  # type: ignore[arg-type]

        with mock.patch.object(generator, "_run_git_text", side_effect=fake_git):
            with self.assertRaisesRegex(generator.InventoryError, "tracked or untracked"):
                generator.build_inventory(REPOSITORY_ROOT, SOURCE_COMMIT)

    def test_all_hashes_use_the_same_exact_commit_blob_snapshot(self) -> None:
        records = [
            *self.inventory["dbContexts"],
            *self.inventory["migrations"],
            *self.inventory["documents"]["sourceFiles"],
            *(
                source
                for store in self.inventory["durableStores"]
                for source in store["sourceFiles"]
            ),
        ]
        for record in records:
            self.assertEqual(
                hashlib.sha256(self.snapshot.files[record["path"]]).hexdigest(),
                record["sha256"],
                record["path"],
            )
        with mock.patch.object(Path, "read_text", side_effect=AssertionError("worktree read")):
            self.assertEqual(self.inventory, generator._build_inventory_from_snapshot(self.snapshot))

    def test_inventory_is_deterministic_and_cli_emits_utf8_lf_bytes(self) -> None:
        first = generator.canonical_bytes(generator._build_inventory_from_snapshot(self.snapshot))
        second = generator.canonical_bytes(generator._build_inventory_from_snapshot(self.snapshot))
        self.assertEqual(first, second)
        result = subprocess.run(
            [sys.executable, "-I", "-S", "-B", str(GENERATOR_PATH)],
            cwd=REPOSITORY_ROOT,
            check=False,
            capture_output=True,
        )
        self.assertEqual(0, result.returncode, result.stderr.decode("utf-8", errors="replace"))
        self.assertEqual(first, result.stdout)
        self.assertNotIn(b"\r\n", result.stdout)
        self.assertTrue(result.stdout.endswith(b"\n"))
        self.assertFalse(result.stdout.endswith(b"\n\n"))
        self.assertEqual(SOURCE_COMMIT, json.loads(result.stdout.decode("utf-8"))["sourceCommit"])

    def test_canonical_bytes_have_a_fixed_portable_hash(self) -> None:
        fixture = {"z": "Göymen", "a": [True, False, 0, 1]}
        encoded = generator.canonical_bytes(fixture)
        self.assertEqual(
            "eed84727c9ed34494146c003a161860247f93cf283f1141a900066baaea59083",
            hashlib.sha256(encoded).hexdigest(),
        )
        self.assertNotIn(b"\r\n", encoded)
        self.assertEqual(encoded, encoded.decode("utf-8").encode("utf-8"))

    def test_json_const_and_enum_comparisons_are_type_aware(self) -> None:
        invalid_cases = (
            (1, {"const": True}),
            (True, {"const": 1}),
            (0, {"const": False}),
            (False, {"const": 0}),
            (True, {"enum": [1]}),
            (1, {"enum": [True]}),
        )
        for value, schema in invalid_cases:
            with self.subTest(value=value, schema=schema):
                with self.assertRaises(generator.InventoryError):
                    generator._validate_against_schema(value, schema)
        generator._validate_against_schema(True, {"type": "boolean", "const": True})
        generator._validate_against_schema(False, {"type": "boolean", "enum": [False]})
        with self.assertRaisesRegex(generator.InventoryError, "duplicate items"):
            generator._validate_against_schema([1, 1.0], {"type": "array", "uniqueItems": True})

    def test_source_only_assertions_reject_integer_boolean_aliases(self) -> None:
        schema = generator._load_schema(self.snapshot)
        assertion_schema = schema["properties"]["assertions"]["properties"]
        for key in self.inventory["assertions"]:
            self.assertEqual("boolean", assertion_schema[key]["type"])
            changed = copy.deepcopy(self.inventory)
            changed["assertions"][key] = 1 if key == "sourceOnly" else 0
            with self.subTest(key=key):
                with self.assertRaisesRegex(generator.InventoryError, "const|wrong type"):
                    generator._validate_inventory_for_snapshot(self.snapshot, changed)

    def test_documents_and_durable_identity_semantics_fail_closed(self) -> None:
        swap = copy.deepcopy(self.inventory)
        swap["documents"]["pathClassifications"][0]["classification"] = "TRANSIENT_EXCLUDED"
        swap["documents"]["pathClassifications"][1]["classification"] = "RECOVERY_SET_CONTENT"

        fake_key = copy.deepcopy(self.inventory)
        fake_key["documents"]["configurationKeys"][-1] = "Documents:Scanner:Fake"

        fake_id = copy.deepcopy(self.inventory)
        fake_id["durableStores"][0]["id"] = "accounting.fake-replay"

        wrong_module = copy.deepcopy(self.inventory)
        wrong_module["durableStores"][0]["module"] = "Finance"

        wrong_kind = copy.deepcopy(self.inventory)
        inbox = next(
            store for store in wrong_kind["durableStores"] if store["kind"] == "inbox"
        )
        inbox["kind"] = "delivery"

        for label, changed in (
            ("classification-swap", swap),
            ("fake-key", fake_key),
            ("fake-id", fake_id),
            ("wrong-module", wrong_module),
            ("wrong-kind", wrong_kind),
        ):
            with self.subTest(label=label):
                with self.assertRaises(generator.InventoryError):
                    generator._validate_inventory_for_snapshot(self.snapshot, changed)

    def test_segment_and_duplicate_semantics_fail_closed(self) -> None:
        unsafe_paths = (
            "../x", "/x", "C:/x", "C:\\x", "\\\\server\\share\\x",
            "src/backend/../x", "./x", "src/backend//x", "src/backend/x\x00y",
        )
        for path in unsafe_paths:
            with self.subTest(path=repr(path)):
                with self.assertRaises(generator.InventoryError):
                    generator._require_repo_relative_path(path)

        traversal = copy.deepcopy(self.inventory)
        traversal["dbContexts"][0]["path"] = "src/backend/../FakeDbContext.cs"

        duplicate_context = copy.deepcopy(self.inventory)
        duplicate_context["dbContexts"][1]["path"] = duplicate_context["dbContexts"][0]["path"]

        duplicate_migration = copy.deepcopy(self.inventory)
        duplicate_migration["migrations"][1]["path"] = duplicate_migration["migrations"][0]["path"]

        duplicate_document_source = copy.deepcopy(self.inventory)
        duplicate_document_source["documents"]["sourceFiles"][1]["path"] = (
            duplicate_document_source["documents"]["sourceFiles"][0]["path"]
        )

        duplicate_physical = copy.deepcopy(self.inventory)
        duplicate_physical["durableStores"][1]["schema"] = duplicate_physical["durableStores"][0]["schema"]
        duplicate_physical["durableStores"][1]["table"] = duplicate_physical["durableStores"][0]["table"]

        duplicate_id = copy.deepcopy(self.inventory)
        duplicate_id["durableStores"][1]["id"] = duplicate_id["durableStores"][0]["id"]

        duplicate_classification = copy.deepcopy(self.inventory)
        duplicate_classification["documents"]["pathClassifications"][1]["configurationKey"] = (
            duplicate_classification["documents"]["pathClassifications"][0]["configurationKey"]
        )

        for label, changed in (
            ("traversal", traversal),
            ("duplicate-context", duplicate_context),
            ("duplicate-migration", duplicate_migration),
            ("duplicate-document-source", duplicate_document_source),
            ("duplicate-physical", duplicate_physical),
            ("duplicate-id", duplicate_id),
            ("duplicate-classification", duplicate_classification),
        ):
            with self.subTest(label=label):
                with self.assertRaises(generator.InventoryError):
                    generator._validate_inventory_for_snapshot(self.snapshot, changed)

    def test_source_only_output_contains_names_and_negative_assertions(self) -> None:
        documents = self.inventory["documents"]
        self.assertEqual(list(generator.DOCUMENT_CONFIGURATION_KEYS), documents["configurationKeys"])
        self.assertEqual(
            {
                "sourceOnly": True,
                "containsSecrets": False,
                "containsConfigurationValues": False,
                "selectsProvider": False,
                "performsBackupOrRestore": False,
                "mutatesInfrastructure": False,
                "mutatesData": False,
                "deploys": False,
            },
            self.inventory["assertions"],
        )
        serialized = generator.canonical_json(self.inventory)
        for forbidden_value in (
            "D:\\GoymenERP\\Belgeler", "\\\\nas01\\erp\\Belgeler", "MpCmdRun.exe",
            "%TEMP%", "ConnectionStrings",
        ):
            self.assertNotIn(forbidden_value, serialized)

        inventory_with_value = copy.deepcopy(self.inventory)
        inventory_with_value["documents"]["configurationValue"] = "forbidden"
        with self.assertRaisesRegex(generator.InventoryError, "extra fields"):
            generator._validate_inventory_for_snapshot(self.snapshot, inventory_with_value)

    def test_inventory_lists_both_inventory_outbox_contracts(self) -> None:
        outbox = next(
            store for store in self.inventory["durableStores"] if store["id"] == "inventory.outbox"
        )
        self.assertEqual(
            ["InventoryCustomerReturnOutboxV1", "InventoryDefiniteShipmentOutboxV1"],
            outbox["contracts"],
        )

    def test_generator_imports_only_python_standard_library(self) -> None:
        tree = ast.parse(GENERATOR_PATH.read_text(encoding="utf-8"))
        imported_roots: set[str] = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imported_roots.update(alias.name.split(".")[0] for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                imported_roots.add(node.module.split(".")[0])
        self.assertEqual(
            {
                "__future__", "argparse", "hashlib", "json", "os", "pathlib", "re",
                "subprocess", "sys", "typing",
            },
            imported_roots,
        )


if __name__ == "__main__":
    unittest.main()
