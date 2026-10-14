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


def snapshot_with_file(snapshot: object, relative_path: str, content: str) -> object:
    files = dict(snapshot.files)  # type: ignore[attr-defined]
    if relative_path in files:
        raise AssertionError(f"Test path already exists: {relative_path}.")
    files[relative_path] = content.encode("utf-8")
    return generator.SourceSnapshot(snapshot.repository_root, snapshot.source_commit, files)  # type: ignore[attr-defined]


def extract_csharp_method(text: str, signature: str) -> str:
    start = text.index(signature)
    structural = generator._mask_csharp_strings(generator._strip_csharp_comments(text))
    opening = structural.index("{", start)
    closing = generator._matching_brace(structural, opening)
    return text[start : closing + 1]


def accounting_writer_in_disabled_region(text: str) -> str:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    method = text[start:end]
    _, call = method.split("\n", 1)
    replacement = (
        signature.removesuffix(" =>")
        + "\n    {\n#if false\n"
        + call
        + "\n#endif\n    }"
    )
    return text[:start] + replacement + text[end:]


def accounting_writer_in_unknown_preprocessor_region(text: str) -> str:
    return accounting_writer_in_disabled_region(text).replace(
        "#if false", "#if R05_NEVER_DEFINED", 1
    )


def accounting_writer_in_string_only(text: str) -> str:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    token = "dbContext.Commands.Add(new AccountingCommandRow"
    replacement = (
        signature.removesuffix(" =>")
        + f'\n    {{\n        _ = "{token}";\n    }}'
    )
    return text[:start] + replacement + text[end:]


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
            'modelBuilder.Entity<ProcessedMessageRow>().ToTable("Receipts", Schema);',
            'modelBuilder.Entity<X>().ToTable("ProcessedMessages", Schema);',
            'm.Entity<X>().ToTable("ProcessedMessages", Schema);',
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

        for non_live_source in (
            '// modelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema);',
            '/* modelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema); */',
            '_ = "modelBuilder.Entity<X>().ToTable(\\\"RecoveryDeliveries\\\", Schema);";',
            '_ = """modelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema);""";',
            '#if R05_NEVER_DEFINED\nmodelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema);\n#endif',
            'if (false) { modelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema); }',
        ):
            with self.subTest(non_live_source=non_live_source):
                changed = mutate_snapshot(
                    self.snapshot,
                    generator.ACCOUNTING_CONTEXT,
                    lambda text, non_live_source=non_live_source: text + f"\n{non_live_source}\n",
                )
                self.assertEqual(discovered, set(generator._discover_message_delivery_tables(changed)))

        renamed_with_string_decoy = mutate_snapshot(
            self.snapshot,
            generator.ACCOUNTING_CONTEXT,
            lambda text: text.replace(
                'entity.ToTable("Deliveries", Schema',
                'entity.ToTable("Receipts", Schema',
                1,
            )
            + '\n_ = """entity.ToTable("Deliveries", Schema""";\n',
        )
        with self.assertRaisesRegex(generator.InventoryError, "source surface drifted"):
            generator._durable_store_inventory(renamed_with_string_decoy)

    def test_snapshot_message_and_dedup_mappings_use_live_model_evidence(self) -> None:
        snapshot_path = next(
            path
            for path in self.snapshot.files
            if path.endswith("AccountingDbContextModelSnapshot.cs")
        )
        mapping = '''modelBuilder.Entity("Test.ProcessedMessageRow", b =>
        {
            b.Property<string>("IdempotencyKeyHash");
            b.Property<string>("MessageId");
            b.Property<DateTimeOffset>("ProcessedAtUtc");
            b.ToTable("ProcessedMessages", "accounting");
        });'''
        live = mutate_snapshot(
            self.snapshot,
            snapshot_path,
            lambda text: text + "\n" + mapping + "\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "surface drifted"):
            generator._require_discovered_durable_surfaces(live)

        baseline = generator._discover_snapshot_table_mappings(self.snapshot)
        for decoy in (
            '_ = """\n' + mapping + '\n""";',
            "#if R05_NEVER_DEFINED\n" + mapping + "\n#endif",
            "if (false) {\n" + mapping + "\n}",
        ):
            with self.subTest(decoy=decoy.splitlines()[0]):
                changed = mutate_snapshot(
                    self.snapshot,
                    snapshot_path,
                    lambda text, decoy=decoy: text + "\n" + decoy + "\n",
                )
                self.assertEqual(
                    baseline,
                    generator._discover_snapshot_table_mappings(changed),
                )

    def test_documents_scanner_hash_and_transient_cleanup_are_bound(self) -> None:
        scanner_path = generator.DOCUMENT_SCANNER_PATH
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
            lambda text: text.replace(
                "File.Delete(path);", "if (false) { File.Delete(path); }", 1
            ),
            lambda text: text.replace(
                "File.Delete(path);", '_ = "File.Delete(path);";', 1
            ),
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

    def test_documents_scanner_whole_declaration_decoys_fail_closed(self) -> None:
        scanner_path = generator.DOCUMENT_SCANNER_PATH
        scan_signature = (
            "    public async Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, "
            "DocumentFileKind kind, CancellationToken cancellationToken = default)"
        )
        delete_signature = "    private static void TryDelete(string path)"
        scan_stub = (
            scan_signature
            + "\n    {\n        await Task.Yield();\n"
            + "        return DocumentScanVerdictV1.Unavailable;\n    }"
        )
        delete_stub = delete_signature + "\n    {\n        GC.KeepAlive(path);\n    }"

        def raw_scan_decoy(text: str) -> str:
            method = extract_csharp_method(text, scan_signature)
            replacement = (
                '    private const string RecoveryScanDecoy = """\n'
                + method
                + '\n""";\n\n'
                + scan_stub
            )
            return text.replace(method, replacement, 1)

        def conditional_scan_decoy(text: str) -> str:
            method = extract_csharp_method(text, scan_signature)
            replacement = (
                "#if R05_NEVER_DEFINED\n"
                + method
                + "\n#endif\n"
                + scan_stub
            )
            return text.replace(method, replacement, 1)

        def raw_delete_decoy(text: str) -> str:
            method = extract_csharp_method(text, delete_signature)
            replacement = (
                '    private const string RecoveryDeleteDecoy = """\n'
                + method
                + '\n""";\n\n'
                + delete_stub
            )
            return text.replace(method, replacement, 1)

        def conditional_delete_decoy(text: str) -> str:
            method = extract_csharp_method(text, delete_signature)
            replacement = (
                "#if R05_NEVER_DEFINED\n"
                + method
                + "\n#endif\n"
                + delete_stub
            )
            return text.replace(method, replacement, 1)

        for transform in (
            raw_scan_decoy,
            conditional_scan_decoy,
            raw_delete_decoy,
            conditional_delete_decoy,
        ):
            with self.subTest(transform=transform):
                changed = mutate_snapshot(self.snapshot, scanner_path, transform)
                with self.assertRaises(generator.InventoryError):
                    generator._documents_inventory(changed)

    def test_document_object_store_implementers_are_recursive_and_hash_bound(self) -> None:
        adapter_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/Cloud/S3BlobStore.cs"
        )
        adapter_source = f'''namespace Test;

internal sealed partial class S3BlobStore : IDocumentObjectStoreV1
{{
    [GeneratedRegex("{generator.DOCUMENT_OBJECT_KEY_PATTERN}")]
    private static partial Regex KeyPattern();
    private static string Hash(byte[] content) => DocumentContentHash.Compute(content);
}}
'''
        changed = snapshot_with_file(self.snapshot, adapter_path, adapter_source)
        documents = generator._documents_inventory(changed)
        self.assertIn(
            {"name": "S3BlobStore", "path": adapter_path},
            documents["objectStoreAdapters"],
        )
        sources = {record["path"]: record["sha256"] for record in documents["sourceFiles"]}
        self.assertEqual(hashlib.sha256(adapter_source.encode("utf-8")).hexdigest(), sources[adapter_path])
        self.assertEqual(7, len(sources))
        generated = generator._build_inventory_from_snapshot(changed)
        self.assertEqual(documents, generated["documents"])
        self.assertEqual(20, generated["counts"]["durableStores"])

        second_adapter = adapter_source + f'''
internal sealed partial class ColdArchive : IDocumentObjectStoreV1
{{
    [GeneratedRegex("{generator.DOCUMENT_OBJECT_KEY_PATTERN}")]
    private static partial Regex KeyPattern();
    private static string Hash(byte[] content) => DocumentContentHash.Compute(content);
}}
'''
        shared_file = snapshot_with_file(self.snapshot, adapter_path, second_adapter)
        shared_documents = generator._documents_inventory(shared_file)
        shared_paths = [record["path"] for record in shared_documents["sourceFiles"]]
        self.assertEqual(1, shared_paths.count(adapter_path))
        self.assertEqual(7, len(shared_paths))
        self.assertEqual(4, len(shared_documents["objectStoreAdapters"]))

        dead_adapter = snapshot_with_file(
            self.snapshot,
            adapter_path,
            '''namespace Test;
internal sealed class S3BlobStore
{
    private const string Fake = """class S3BlobStore : IDocumentObjectStoreV1""";
}
''',
        )
        self.assertEqual(
            self.inventory["documents"]["objectStoreAdapters"],
            generator._documents_inventory(dead_adapter)["objectStoreAdapters"],
        )

        existing_path = next(
            adapter["path"]
            for adapter in self.inventory["documents"]["objectStoreAdapters"]
            if adapter["name"] == "FileSystemDocumentObjectStore"
        )
        removed_live_interface = mutate_snapshot(
            self.snapshot,
            existing_path,
            lambda text: text.replace(
                "FileSystemDocumentObjectStore : IDocumentObjectStoreV1",
                "FileSystemDocumentObjectStore",
                1,
            )
            + '\n_ = """class FileSystemDocumentObjectStore : IDocumentObjectStoreV1""";\n',
        )
        with self.assertRaisesRegex(generator.InventoryError, "adapter surface drifted"):
            generator._documents_inventory(removed_live_interface)

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

    def test_non_live_writer_anchor_fails_closed(self) -> None:
        token = "dbContext.Commands.Add(new AccountingCommandRow"
        transforms = (
            lambda text: text.replace(token, f"// {token}", 1),
            lambda text: text.replace(token, f"/* {token} */", 1),
            accounting_writer_in_disabled_region,
            accounting_writer_in_unknown_preprocessor_region,
            accounting_writer_in_string_only,
        )
        for transform in transforms:
            with self.subTest(transform=transform):
                changed = mutate_snapshot(
                    self.snapshot,
                    generator.ACCOUNTING_STORE,
                    transform,
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

    def test_provenance_rechecks_head_after_commit_blobs_are_read(self) -> None:
        with mock.patch.object(
            generator,
            "_git_source_commit",
            side_effect=(SOURCE_COMMIT, "0" * 40),
        ) as read_head:
            with self.assertRaisesRegex(generator.InventoryError, "HEAD changed"):
                generator._load_source_snapshot(REPOSITORY_ROOT, SOURCE_COMMIT)
        self.assertEqual(2, read_head.call_count)

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
