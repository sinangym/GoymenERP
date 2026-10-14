from __future__ import annotations

import ast
import copy
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
).stdout.strip().lower()

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


class RecoverySurfaceInventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.inventory = generator.build_inventory(REPOSITORY_ROOT, SOURCE_COMMIT)

    def test_current_source_inventory_is_exact_and_schema_valid(self) -> None:
        inventory = self.inventory
        self.assertEqual(
            {"dbContexts": 18, "migrations": 91, "durableStores": 20},
            inventory["counts"],
        )
        actual = {
            (store["schema"], store["table"]): (
                store["entityType"], store["kind"], store["dbContext"]
            )
            for store in inventory["durableStores"]
        }
        self.assertEqual(EXPECTED_STORES, actual)
        self.assertEqual(20, len(actual))
        generator.validate_inventory(REPOSITORY_ROOT, inventory)

    def test_every_store_has_mapping_migration_and_writer_evidence(self) -> None:
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
            self.assertTrue(all(len(source["sha256"]) == 64 for source in record["sourceFiles"]))

    def test_snapshot_discovery_is_exact_and_excludes_only_audit_metadata(self) -> None:
        discovered = generator._discover_idempotency_key_tables(REPOSITORY_ROOT)
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
        unexpected = tuple(sorted((*generator.EXPECTED_IDEMPOTENCY_KEY_TABLES, ("test", "NewReplay"))))
        with mock.patch.object(
            generator, "_discover_idempotency_key_tables", return_value=unexpected
        ):
            with self.assertRaisesRegex(generator.InventoryError, "surface drifted"):
                generator._require_discovered_durable_surfaces(REPOSITORY_ROOT)

    def test_mapping_anchor_mutation_fails_closed(self) -> None:
        original_read = generator._read_source

        def read_with_mutation(repository_root: Path, relative_path: str) -> str:
            content = original_read(repository_root, relative_path)
            if relative_path == generator.ACCOUNTING_CONTEXT:
                return content.replace(
                    "Entity<AccountingCommandRow>()", "Entity<UnexpectedAccountingCommandRow>()"
                )
            return content

        with mock.patch.object(generator, "_read_source", side_effect=read_with_mutation):
            with self.assertRaisesRegex(generator.InventoryError, "source anchor drifted"):
                generator._durable_store_inventory(REPOSITORY_ROOT)

    def test_schema_anchor_mutation_fails_closed(self) -> None:
        original_read = generator._read_source

        def read_with_mutation(repository_root: Path, relative_path: str) -> str:
            content = original_read(repository_root, relative_path)
            if relative_path == generator.WORK_CONTEXT:
                return content.replace('Schema = "work"', 'Schema = "renamed"')
            return content

        with mock.patch.object(generator, "_read_source", side_effect=read_with_mutation):
            with self.assertRaisesRegex(generator.InventoryError, "source anchor drifted"):
                generator._durable_store_inventory(REPOSITORY_ROOT)

    def test_context_and_migration_path_sets_fail_closed(self) -> None:
        contexts = [record["path"] for record in self.inventory["dbContexts"]]
        migrations = [record["path"] for record in self.inventory["migrations"]]
        generator._require_path_set(
            "DbContext",
            contexts,
            generator.EXPECTED_DB_CONTEXT_COUNT,
            generator.EXPECTED_DB_CONTEXT_PATH_FINGERPRINT,
        )
        generator._require_path_set(
            "Migration",
            migrations,
            generator.EXPECTED_MIGRATION_COUNT,
            generator.EXPECTED_MIGRATION_PATH_FINGERPRINT,
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext surface drifted"):
            generator._require_path_set(
                "DbContext",
                [*contexts, "src/backend/NewDbContext.cs"],
                generator.EXPECTED_DB_CONTEXT_COUNT,
                generator.EXPECTED_DB_CONTEXT_PATH_FINGERPRINT,
            )
        with self.assertRaisesRegex(generator.InventoryError, "Migration surface drifted"):
            generator._require_path_set(
                "Migration",
                migrations[:-1],
                generator.EXPECTED_MIGRATION_COUNT,
                generator.EXPECTED_MIGRATION_PATH_FINGERPRINT,
            )

    def test_inventory_is_deterministic_and_cli_matches(self) -> None:
        first = generator.canonical_json(generator.build_inventory(REPOSITORY_ROOT, SOURCE_COMMIT))
        second = generator.canonical_json(generator.build_inventory(REPOSITORY_ROOT, SOURCE_COMMIT))
        self.assertEqual(first, second)
        result = subprocess.run(
            [sys.executable, "-I", "-S", "-B", str(GENERATOR_PATH)],
            cwd=REPOSITORY_ROOT,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(first, result.stdout)
        self.assertEqual(SOURCE_COMMIT, json.loads(result.stdout)["sourceCommit"])

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
            "D:\\GoymenERP\\Belgeler",
            "\\\\nas01\\erp\\Belgeler",
            "MpCmdRun.exe",
            "%TEMP%",
            "ConnectionStrings",
        ):
            self.assertNotIn(forbidden_value, serialized)

        inventory_with_value = copy.deepcopy(self.inventory)
        inventory_with_value["documents"]["configurationValue"] = "forbidden"
        with self.assertRaisesRegex(generator.InventoryError, "extra fields"):
            generator.validate_inventory(REPOSITORY_ROOT, inventory_with_value)

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
            {"__future__", "argparse", "hashlib", "json", "pathlib", "re", "subprocess", "sys", "typing"},
            imported_roots,
        )


if __name__ == "__main__":
    unittest.main()
