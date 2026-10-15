from __future__ import annotations

import ast
import copy
import hashlib
import importlib.util
import json
import os
import re
import subprocess
import sys
import tempfile
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


def accounting_writer_in_deferred_lambda(text: str) -> str:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    method = text[start:end]
    _, call = method.split("\n", 1)
    replacement = (
        signature.removesuffix(" =>")
        + "\n    {\n        Action deferredWrite = () =>\n        {\n"
        + call
        + "\n        };\n        GC.KeepAlive(deferredWrite);\n    }"
    )
    return text[:start] + replacement + text[end:]


def accounting_writer_in_anonymous_delegate(text: str) -> str:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    method = text[start:end]
    _, call = method.split("\n", 1)
    replacement = (
        signature.removesuffix(" =>")
        + "\n    {\n        Action deferredWrite = delegate\n        {\n"
        + call
        + "\n        };\n        GC.KeepAlive(deferredWrite);\n    }"
    )
    return text[:start] + replacement + text[end:]


def accounting_writer_in_local_function(text: str) -> str:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    method = text[start:end]
    _, call = method.split("\n", 1)
    replacement = (
        signature.removesuffix(" =>")
        + "\n    {\n        void DeferredWrite()\n        {\n"
        + call
        + "\n        }\n        GC.KeepAlive((Action)DeferredWrite);\n    }"
    )
    return text[:start] + replacement + text[end:]


def accounting_writer_in_attributed_local_function(text: str) -> str:
    return accounting_writer_in_local_function(text).replace(
        "        void DeferredWrite()",
        "        [System.Diagnostics.DebuggerStepThrough] void DeferredWrite()",
        1,
    )


def accounting_writer_in_escaped_local_function(text: str) -> str:
    return accounting_writer_in_local_function(text).replace(
        "void DeferredWrite()",
        "void @DeferredWrite()",
        1,
    )


def accounting_writer_in_tuple_local_function(text: str) -> str:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    method = text[start:end]
    _, call = method.split("\n", 1)
    replacement = (
        signature.removesuffix(" =>")
        + "\n    {\n        (string Proof, int Count) DeferredWrite()\n        {\n"
        + call
        + '\n            return ("deferred", 1);\n'
        + "        }\n        GC.KeepAlive(DeferredWrite);\n    }"
    )
    return text[:start] + replacement + text[end:]


def accounting_writer_in_same_line_local_function(text: str) -> str:
    return accounting_writer_in_local_function(text).replace(
        "\n        void DeferredWrite()",
        " GC.KeepAlive(record); void DeferredWrite()",
        1,
    )


def accounting_writer_in_expression_lambda_with_initializer(text: str) -> str:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    method = text[start:end]
    _, call = method.split("\n", 1)
    replacement = (
        signature.removesuffix(" =>")
        + "\n    {\n        Action deferredWrite = () =>\n"
        + call
        + "\n        GC.KeepAlive(deferredWrite);\n    }"
    )
    return text[:start] + replacement + text[end:]


def accounting_writer_in_global_return_local_function(text: str) -> str:
    return (
        accounting_writer_in_local_function(text)
        .replace("void DeferredWrite()", "global::System.Int32 DeferredWrite()", 1)
        .replace(
            "\n        }\n        GC.KeepAlive((Action)DeferredWrite);",
            "\n            return 0;\n        }\n        GC.KeepAlive(DeferredWrite);",
            1,
        )
    )


def accounting_writer_in_unicode_escaped_local_function(text: str) -> str:
    return accounting_writer_in_local_function(text).replace(
        "void DeferredWrite()",
        "void \\u0044eferredWrite()",
        1,
    )


def accounting_writer_in_default_parameter_local_function(text: str) -> str:
    return (
        accounting_writer_in_local_function(text)
        .replace("void DeferredWrite()", "void DeferredWrite(int ignored = 0)", 1)
        .replace(
            "GC.KeepAlive((Action)DeferredWrite);",
            "GC.KeepAlive((Action)(() => DeferredWrite()));",
            1,
        )
    )


def accounting_writer_in_multiline_attributed_local_function(text: str) -> str:
    return accounting_writer_in_local_function(text).replace(
        "void DeferredWrite()",
        "[System.Diagnostics.DebuggerStepThrough(\n        )] void DeferredWrite()",
        1,
    )


def accounting_writer_in_literal_unicode_local_function(text: str) -> str:
    return accounting_writer_in_local_function(text).replace("DeferredWrite", "Yazıcı")


def _accounting_writer_expression(text: str) -> tuple[int, int, str, str]:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    method = text[start:end]
    _, call = method.split("\n", 1)
    return start, end, signature.removesuffix(" =>"), call.strip().removesuffix(";")


def _accounting_writer_in_block(text: str, statements: str, return_type: str = "void") -> str:
    start, end, signature, expression = _accounting_writer_expression(text)
    signature = signature.replace("private void AddCommand", f"private {return_type} AddCommand", 1)
    replacement = signature + "\n    {\n" + statements.format(expression=expression) + "\n    }"
    return text[:start] + replacement + text[end:]


def accounting_writer_in_deferred_linq(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        var deferredWrites =\n"
        "            from ignored in new[] {{ 0 }}\n"
        "            select {expression};\n"
        "        GC.KeepAlive(deferredWrites);",
    )


def accounting_writer_in_conditional_call(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        System.Diagnostics.Debug.WriteLine(\n            {expression});",
    )


def accounting_writer_in_dead_ternary(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        _ = false ? ({expression}) : null;",
    )


def accounting_writer_in_dead_and(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        _ = false && ({expression}) is not null;",
    )


def accounting_writer_in_dead_or(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        _ = true || ({expression}) is not null;",
    )


def accounting_writer_in_dead_switch(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        _ = 0 switch {{ 1 => ({expression}), _ => null }};",
    )


def accounting_writer_in_dead_coalesce(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        object existing = new();\n        _ = existing ?? ({expression});",
    )


def accounting_writer_on_conditional_method(text: str) -> str:
    changed = _accounting_writer_in_block(text, "        {expression};")
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record)"
    return changed.replace(
        signature,
        '    [System.Diagnostics.Conditional("R05_NEVER_DEFINED")]\n' + signature,
        1,
    )


def accounting_writer_in_iterator(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        {expression};\n        yield break;",
        return_type="IEnumerable<int>",
    )


def accounting_writer_then_clear_change_tracker(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        {expression};\n        dbContext.ChangeTracker.Clear();",
    )


def accounting_writer_in_false_runtime_branch(text: str) -> str:
    return _accounting_writer_in_block(
        text,
        "        var persist = false;\n"
        "        if (persist)\n"
        "        {{\n"
        "            {expression};\n"
        "        }}",
    )


def accounting_writer_call_sites_removed(text: str) -> str:
    return (
        text.replace(
            "AddCommand(RequireResolvedCompany(), record);",
            "GC.KeepAlive(record);",
            1,
        )
        .replace(
            "AddCommand(companyId, completion(result));",
            "GC.KeepAlive(completion(result));",
            1,
        )
    )


def accounting_writer_in_unused_method(text: str) -> str:
    signature = "    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>"
    start = text.index(signature)
    end = text.index("        });", start) + len("        });")
    method = text[start:end]
    _, call = method.split("\n", 1)
    replacement = (
        signature.removesuffix(" =>")
        + "\n    {\n        GC.KeepAlive(record);\n    }\n\n"
        + "    private void UnusedRecoveryWriter(Guid companyId, AccountingCommandRecord record)\n"
        + "    {\n"
        + call
        + "\n    }"
    )
    return text[:start] + replacement + text[end:]


class RecoverySurfaceInventoryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.snapshot = generator._load_source_snapshot(REPOSITORY_ROOT, SOURCE_COMMIT)
        cls.inventory = generator._build_inventory_from_snapshot(cls.snapshot)

    def test_current_source_inventory_is_exact_and_schema_valid(self) -> None:
        self.assertEqual(
            {"dbContexts": 18, "migrations": 95, "durableStores": 20},
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
        replay_tables = set(generator._discover_replay_enforcement_tables(self.snapshot))
        specified = {
            (spec.schema, spec.table)
            for spec in generator.DURABLE_STORE_SPECS
            if spec.kind == "idempotency"
        }
        self.assertEqual(15, len(replay_tables))
        self.assertEqual(specified, replay_tables)
        self.assertEqual((("audit", "AuditEvents"),), generator.METADATA_ONLY_IDEMPOTENCY_KEY_TABLES)

        audit_snapshot = next(
            path for path in self.snapshot.files if path.endswith("AuditDbContextModelSnapshot.cs")
        )
        promoted = mutate_snapshot(
            self.snapshot,
            audit_snapshot,
            lambda text: text.replace(
                'b.ToTable("AuditEvents", "audit"',
                'b.HasIndex("CompanyId", "IdempotencyKeyHash").IsUnique();\n\n'
                '                    b.ToTable("AuditEvents", "audit"',
                1,
            ),
        )
        with self.assertRaisesRegex(generator.InventoryError, "classification drifted"):
            generator._require_discovered_durable_surfaces(promoted)

        audit_context = next(
            path for path in self.snapshot.files if path.endswith("/AuditDbContext.cs")
        )
        def add_method(text: str, method: str) -> str:
            before_close, close = text.rsplit("}", 1)
            return before_close + method + "\n}" + close

        replay_forms = (
            (
                "direct-set",
                "\nprivate Task<AuditEvent?> ReplayViaSet(string key) => "
                "Set<AuditEvent>().SingleOrDefaultAsync ("
                "row => row.IdempotencyKeyHash == key);\n",
                None,
            ),
            (
                "typed-split-query",
                "\nprivate Task<AuditEvent?> ReplayViaVariable(string key)\n"
                "{\n    DbSet<AuditEvent> rows = Set<AuditEvent>();\n"
                "    return rows.SingleOrDefaultAsync("
                "row => row.IdempotencyKeyHash == key);\n}\n",
                None,
            ),
            (
                "namespace-alias",
                "\nprivate Task<AuditEvent?> ReplayViaAlias(string key) => "
                "Set<A.AuditEvent>().SingleOrDefaultAsync("
                "row => row.IdempotencyKeyHash == key);\n",
                (
                    "using GoymenERP.Modules.Audit.Domain.Events;\n",
                    "using GoymenERP.Modules.Audit.Domain.Events;\n"
                    "using A = GoymenERP.Modules.Audit.Domain.Events;\n",
                ),
            ),
            (
                "linq-query",
                "\nprivate IQueryable<AuditEvent> ReplayViaLinq(string key) =>\n"
                "    from row in Set<AuditEvent>()\n"
                "    where row.IdempotencyKeyHash == key\n"
                "    select row;\n",
                None,
            ),
            (
                "helper-return",
                "\nprivate IQueryable<AuditEvent> AuditRows() => Set<AuditEvent>();\n"
                "private Task<AuditEvent?> ReplayViaHelper(string key) => "
                "AuditRows().SingleOrDefaultAsync(row => row.IdempotencyKeyHash == key);\n",
                None,
            ),
            (
                "context-property",
                "\nprivate Task<AuditEvent?> ReplayViaProperty(string key) => "
                "AuditEvents.SingleOrDefaultAsync(row => row.IdempotencyKeyHash == key);\n",
                None,
            ),
            (
                "raw-sql",
                "\nprivate IQueryable<AuditEvent> ReplayViaSql(string key) => "
                "Database.SqlQueryRaw<AuditEvent>(\"SELECT * FROM [audit].[AuditEvents] "
                "WHERE [IdempotencyKeyHash] = {0}\", key);\n",
                None,
            ),
            (
                "raw-sql-concatenated-member",
                "\nprivate IQueryable<AuditEvent> ReplayViaSplitSql(string key) => "
                "Database.SqlQueryRaw<AuditEvent>(\"SELECT * FROM [audit].[AuditEvents] "
                "WHERE [Idempotency\" + \"KeyHash] = {0}\", key);\n",
                None,
            ),
            (
                "ef-property",
                "\nprivate IQueryable<AuditEvent> ReplayViaEfProperty(string key) => "
                "AuditEvents.Where(row => EF.Property<string>(row, \"IdempotencyKeyHash\") == key);\n",
                None,
            ),
            (
                "ef-property-concatenated-member",
                "\nprivate IQueryable<AuditEvent> ReplayViaSplitEfProperty(string key) => "
                "AuditEvents.Where(row => EF.Property<string>(row, "
                "\"Idempotency\" + \"KeyHash\") == key);\n",
                None,
            ),
        )
        for label, method, using_replacement in replay_forms:
            with self.subTest(replay_form=label):
                changed = mutate_snapshot(
                    self.snapshot,
                    audit_context,
                    lambda text, method=method, using_replacement=using_replacement: add_method(
                        text.replace(*using_replacement, 1)
                        if using_replacement is not None
                        else text,
                        method,
                    ),
                )
                with self.assertRaisesRegex(generator.InventoryError, "live-source surface drifted"):
                    generator._require_discovered_durable_surfaces(changed)

        unrelated = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Test/ForeignAuditLookup.cs",
            "namespace Foreign;\n"
            "internal sealed class AuditEvent { public string IdempotencyKeyHash => string.Empty; }\n"
            "internal sealed class Reader { AuditEvent Read(AuditEvent value) => value; }\n",
        )
        generator._require_discovered_durable_surfaces(unrelated)

        unrelated_audit = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Services/Diagnostic.cs",
            "namespace GoymenERP.Modules.Audit.Infrastructure.Services;\n"
            "internal static class Diagnostic\n{\n"
            "    internal const string Label = \"IdempotencyKeyHash\";\n}\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "live-source surface drifted"):
            generator._require_discovered_durable_surfaces(unrelated_audit)

        dead_mutations = (
            (
                "comment",
                lambda text: add_method(
                    text,
                    "\n// AuditEvents.Where(row => row.IdempotencyKeyHash == key);\n",
                ),
            ),
            (
                "literal-false",
                lambda text: text.replace(
                    "ArgumentNullException.ThrowIfNull(modelBuilder);",
                    "if (false)\n        {\n"
                    "            _ = modelBuilder.Entity<AuditEvent>().Where("
                    "row => row.IdempotencyKeyHash == key);\n"
                    "        }\n        ArgumentNullException.ThrowIfNull(modelBuilder);",
                    1,
                ),
            ),
            (
                "preprocessor-false",
                lambda text: add_method(
                    text,
                    "\n#if false\nprivate Task<AuditEvent?> DeadReplay(string key) => "
                    "AuditEvents.SingleOrDefaultAsync(row => row.IdempotencyKeyHash == key);\n"
                    "#endif\n",
                ),
            ),
        )
        audit_model = next(
            path for path in self.snapshot.files
            if path.endswith("/AuditEventModelBuilderExtensions.cs")
        )
        for label, mutation in dead_mutations:
            with self.subTest(dead_form=label):
                changed = mutate_snapshot(
                    self.snapshot,
                    audit_model if label == "literal-false" else audit_context,
                    mutation,
                )
                generator._require_discovered_durable_surfaces(changed)

        self.assertEqual(
            (
                generator.EXPECTED_AUDIT_LIVE_SOURCE_FILE_COUNT,
                generator.EXPECTED_AUDIT_LIVE_SOURCE_FINGERPRINT,
            ),
            generator._audit_live_source_fingerprint(self.snapshot),
        )
        projection_path = next(
            path for path in self.snapshot.files if path.endswith("/GroupSharedAuditSession.cs")
        )
        changed_projection = mutate_snapshot(
            self.snapshot,
            projection_path,
            lambda text: text.replace("Count: > 0", "Count: >= 1", 1),
        )
        with self.assertRaisesRegex(generator.InventoryError, "live-source surface drifted"):
            generator._require_discovered_durable_surfaces(changed_projection)

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
            'if (false) { modelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema); }',
        ):
            with self.subTest(non_live_source=non_live_source):
                changed = mutate_snapshot(
                    self.snapshot,
                    generator.ACCOUNTING_CONTEXT,
                    lambda text, non_live_source=non_live_source: text + f"\n{non_live_source}\n",
                )
                self.assertEqual(discovered, set(generator._discover_message_delivery_tables(changed)))

        unknown_conditional = mutate_snapshot(
            self.snapshot,
            generator.ACCOUNTING_CONTEXT,
            lambda text: text
            + '\n#if R05_NEVER_DEFINED\nmodelBuilder.Entity<X>().ToTable("RecoveryDeliveries", Schema);\n#endif\n',
        )
        with self.assertRaisesRegex(generator.InventoryError, "non-literal conditional"):
            generator._discover_message_delivery_tables(unknown_conditional)

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
            b.HasKey("MessageId");
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

        conditional_decoy = mutate_snapshot(
            self.snapshot,
            snapshot_path,
            lambda text: text + "\n#if R05_NEVER_DEFINED\n" + mapping + "\n#endif\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "non-literal conditional"):
            generator._discover_snapshot_table_mappings(conditional_decoy)

    def test_neutral_message_and_dedup_semantics_fail_closed(self) -> None:
        snapshot_path = next(
            path
            for path in self.snapshot.files
            if path.endswith("AccountingDbContextModelSnapshot.cs")
        )
        mutations = (
            '''modelBuilder.Entity("Test.ConsumerReceipt", b =>
        {
            b.Property<string>("Consumer");
            b.Property<string>("MessageId");
            b.Property<DateTimeOffset>("HandledAtUtc");
            b.Property<string>("PayloadHash");
            b.HasKey("Consumer", "MessageId");
            b.ToTable("ConsumerReceipts", "accounting");
        });''',
            '''modelBuilder.Entity("Test.ReceiverRecord", b =>
        {
            b.Property<Guid>("Id");
            b.Property<string>("EnvelopeBody");
            b.Property<DateTimeOffset>("HandledOnUtc");
            b.HasKey("Id");
            b.ToTable("Receipts", "accounting");
        });''',
        )
        for mapping in mutations:
            with self.subTest(table=mapping.split('b.ToTable("', 1)[1].split('"', 1)[0]):
                changed = mutate_snapshot(
                    self.snapshot,
                    snapshot_path,
                    lambda text, mapping=mapping: text + "\n" + mapping + "\n",
                )
                with self.assertRaisesRegex(generator.InventoryError, "source surface drifted"):
                    generator._require_discovered_durable_surfaces(changed)

        sales_snapshot = next(
            path for path in self.snapshot.files if path.endswith("SalesDbContextModelSnapshot.cs")
        )
        business_table_with_message_semantics = '''modelBuilder.Entity("Test.ScheduleProjection", b =>
        {
            b.Property<Guid>("Id");
            b.Property<string>("MessageId");
            b.Property<DateTimeOffset>("ProcessedAtUtc");
            b.HasKey("Id");
            b.ToTable("DraftDeliverySchedules", "sales");
        });'''
        changed_business = mutate_snapshot(
            self.snapshot,
            sales_snapshot,
            lambda text: text + "\n" + business_table_with_message_semantics + "\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "gained durable message semantics"):
            generator._require_discovered_durable_surfaces(changed_business)

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

        def cleanup_in_anonymous_delegate(text: str) -> str:
            return text.replace(
                "finally\n        {\n            TryDelete(copy);\n            gate.Release();\n        }",
                "finally\n        {\n            Action deferredCleanup = delegate\n"
                "            {\n                TryDelete(copy);\n"
                "                gate.Release();\n            };\n"
                "            GC.KeepAlive(deferredCleanup);\n        }",
                1,
            )

        def local_try_delete_shadow(text: str) -> str:
            signature = (
                "    public async Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, "
                "DocumentFileKind kind, CancellationToken cancellationToken = default)"
            )
            method = extract_csharp_method(text, signature)
            changed_method = method.replace(
                "\n    {",
                "\n    {\n        static void TryDelete(string path) => GC.KeepAlive(path);",
                1,
            )
            return text.replace(method, changed_method, 1)

        def local_try_delete_delegate_shadow(text: str) -> str:
            signature = (
                "    public async Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, "
                "DocumentFileKind kind, CancellationToken cancellationToken = default)"
            )
            method = extract_csharp_method(text, signature)
            changed_method = method.replace(
                "\n    {",
                "\n    {\n        Action<string> TryDelete = static path => GC.KeepAlive(path);",
                1,
            )
            return text.replace(method, changed_method, 1)

        def tuple_try_delete_shadow(text: str) -> str:
            signature = (
                "    public async Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, "
                "DocumentFileKind kind, CancellationToken cancellationToken = default)"
            )
            method = extract_csharp_method(text, signature)
            changed_method = method.replace(
                "\n    {",
                "\n    {\n        (bool Removed, string Path) TryDelete(string path)\n"
                "        {\n            GC.KeepAlive(path);\n"
                "            return (false, path);\n        }",
                1,
            )
            return text.replace(method, changed_method, 1)

        def deconstructed_try_delete_shadow(text: str) -> str:
            signature = (
                "    public async Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, "
                "DocumentFileKind kind, CancellationToken cancellationToken = default)"
            )
            method = extract_csharp_method(text, signature)
            changed_method = method.replace(
                "\n    {",
                "\n    {\n        (Action<string> TryDelete, int ignored) = "
                "(static path => GC.KeepAlive(path), 0);",
                1,
            )
            return text.replace(method, changed_method, 1)

        def unicode_try_delete_shadow(text: str) -> str:
            signature = (
                "    public async Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, "
                "DocumentFileKind kind, CancellationToken cancellationToken = default)"
            )
            method = extract_csharp_method(text, signature)
            changed_method = method.replace(
                "\n    {",
                "\n    {\n        Action<string> \\u0054ryDelete = static path => GC.KeepAlive(path);",
                1,
            )
            return text.replace(method, changed_method, 1)

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
            lambda text: text.replace(
                "File.Delete(path);",
                "File.Delete(path);\n                File.WriteAllBytes(path, Array.Empty<byte>());",
                1,
            ),
            lambda text: text.replace(
                "if (File.Exists(path))\n            {\n                File.Delete(path);\n            }",
                "Action deferredDelete = () =>\n            {\n"
                "                if (File.Exists(path))\n                {\n"
                "                    File.Delete(path);\n                }\n"
                "            };\n            GC.KeepAlive(deferredDelete);",
                1,
            ),
            cleanup_in_anonymous_delegate,
            local_try_delete_shadow,
            local_try_delete_delegate_shadow,
            tuple_try_delete_shadow,
            deconstructed_try_delete_shadow,
            unicode_try_delete_shadow,
        )
        for transform in mutations:
            with self.subTest(transform=transform):
                changed = mutate_snapshot(self.snapshot, scanner_path, transform)
                with self.assertRaises(generator.InventoryError):
                    generator._documents_inventory(changed)

    def test_documents_scanner_cleanup_binds_exact_interface_method(self) -> None:
        scanner_path = generator.DOCUMENT_SCANNER_PATH
        signature = (
            "    public async Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, "
            "DocumentFileKind kind, CancellationToken cancellationToken = default)"
        )

        def overload_decoy(text: str) -> str:
            method = extract_csharp_method(text, signature)
            overload = method.replace(
                "DocumentFileKind kind, CancellationToken cancellationToken = default)",
                "DocumentFileKind kind, bool proofOnly, CancellationToken cancellationToken = default)",
                1,
            )
            stub = (
                signature
                + "\n    {\n        await Task.Yield();\n"
                + "        return DocumentScanVerdictV1.Unavailable;\n    }"
            )
            return text.replace(method, overload + "\n\n" + stub, 1)

        changed = mutate_snapshot(self.snapshot, scanner_path, overload_decoy)
        with self.assertRaisesRegex(generator.InventoryError, "missing or ambiguous"):
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
        with self.assertRaisesRegex(generator.InventoryError, "compilation source surface drifted"):
            generator._build_inventory_from_snapshot(changed)

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

        active_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/ActiveArchive.cs"
        )
        active_source = f'''namespace Test;
#if true
internal sealed partial class ActiveArchive : IDocumentObjectStoreV1
{{
    [GeneratedRegex("{generator.DOCUMENT_OBJECT_KEY_PATTERN}")]
    private static partial Regex KeyPattern();
    private static string Hash(byte[] content) => DocumentContentHash.Compute(content);
}}
#endif
'''
        active = snapshot_with_file(self.snapshot, active_path, active_source)
        self.assertIn(
            {"name": "ActiveArchive", "path": active_path},
            generator._documents_inventory(active)["objectStoreAdapters"],
        )

        conditional_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/ConditionalArchive.cs"
        )
        conditional_source = active_source.replace("#if true", "#if DEBUG", 1).replace(
            "ActiveArchive", "ConditionalArchive"
        )
        conditional = snapshot_with_file(self.snapshot, conditional_path, conditional_source)
        with self.assertRaisesRegex(generator.InventoryError, "non-literal conditional"):
            generator._documents_inventory(conditional)

        derived_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/DerivedArchive.cs"
        )
        derived_source = f'''namespace Test;
internal interface ICloudArchive : IDocumentObjectStoreV1 {{ }}
internal sealed partial class DerivedArchive : ICloudArchive
{{
    [GeneratedRegex("{generator.DOCUMENT_OBJECT_KEY_PATTERN}")]
    private static partial Regex KeyPattern();
    private static string Hash(byte[] content) => DocumentContentHash.Compute(content);
}}
'''
        derived = snapshot_with_file(self.snapshot, derived_path, derived_source)
        self.assertIn(
            {"name": "DerivedArchive", "path": derived_path},
            generator._documents_inventory(derived)["objectStoreAdapters"],
        )

        alias_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/AliasArchive.cs"
        )
        alias_source = f'''using StoreAlias = GoymenERP.Modules.Documents.Application.Contracts.IDocumentObjectStoreV1;
namespace Test;
internal sealed partial class AliasArchive : StoreAlias
{{
    [GeneratedRegex("{generator.DOCUMENT_OBJECT_KEY_PATTERN}")]
    private static partial Regex KeyPattern();
    private static string Hash(byte[] content) => DocumentContentHash.Compute(content);
}}
'''
        aliased = snapshot_with_file(self.snapshot, alias_path, alias_source)
        self.assertIn(
            {"name": "AliasArchive", "path": alias_path},
            generator._documents_inventory(aliased)["objectStoreAdapters"],
        )

        global_alias_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/RecoveryGlobalAlias.cs"
        )
        global_adapter_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/GlobalAliasArchive.cs"
        )
        with_global_alias = snapshot_with_file(
            self.snapshot,
            global_alias_path,
            "global using StoreAlias = GoymenERP.Modules.Documents.Application.Contracts.IDocumentObjectStoreV1;\n",
        )
        global_adapter_source = alias_source.replace(
            "using StoreAlias = GoymenERP.Modules.Documents.Application.Contracts.IDocumentObjectStoreV1;\n", "", 1
        ).replace("AliasArchive", "GlobalAliasArchive")
        globally_aliased = snapshot_with_file(
            with_global_alias,
            global_adapter_path,
            global_adapter_source,
        )
        self.assertIn(
            {"name": "GlobalAliasArchive", "path": global_adapter_path},
            generator._documents_inventory(globally_aliased)["objectStoreAdapters"],
        )

        compact_global_alias_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/RecoveryCompactGlobalAlias.cs"
        )
        compact_adapter_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/CompactAliasArchive.cs"
        )
        compact_alias_snapshot = snapshot_with_file(
            self.snapshot,
            compact_global_alias_path,
            "global using HarmlessAlias = System.String; "
            "global using FinalStoreAlias = GoymenERP.Modules.Documents.Application.Contracts.IDocumentObjectStoreV1;\n",
        )
        compact_adapter_source = alias_source.replace(
            "using StoreAlias = GoymenERP.Modules.Documents.Application.Contracts.IDocumentObjectStoreV1;\n", "", 1
        ).replace("AliasArchive : StoreAlias", "CompactAliasArchive : FinalStoreAlias")
        compact_alias_snapshot = snapshot_with_file(
            compact_alias_snapshot,
            compact_adapter_path,
            compact_adapter_source,
        )
        self.assertIn(
            {"name": "CompactAliasArchive", "path": compact_adapter_path},
            generator._documents_inventory(compact_alias_snapshot)["objectStoreAdapters"],
        )

        inherited_adapter_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/InheritedArchive.cs"
        )
        inherited_adapter_source = f'''namespace Test;
internal abstract partial class ArchiveBase : IDocumentObjectStoreV1
{{
    [GeneratedRegex("{generator.DOCUMENT_OBJECT_KEY_PATTERN}")]
    private static partial Regex KeyPattern();
    private static string Hash(byte[] content) => DocumentContentHash.Compute(content);
}}
internal sealed class InheritedArchive : ArchiveBase {{ }}
'''
        inherited = snapshot_with_file(
            self.snapshot,
            inherited_adapter_path,
            inherited_adapter_source,
        )
        with self.assertRaisesRegex(generator.InventoryError, "(?:hash|key) contract drifted"):
            generator._documents_inventory(inherited)

        value_adapter_template = f'''namespace Test;
internal {{declaration}} : IDocumentObjectStoreV1
{{
    [GeneratedRegex("{generator.DOCUMENT_OBJECT_KEY_PATTERN}")]
    private static partial Regex KeyPattern();
    private static string Hash(byte[] content) => DocumentContentHash.Compute(content);
    public Task PutAsync(string key, ReadOnlyMemory<byte> content, string sha256,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);
}}
'''
        for filename, declaration, expected_name in (
            ("StructArchive.cs", "readonly partial struct StructArchive", "StructArchive"),
            ("RecordArchive.cs", "sealed partial record RecordArchive", "RecordArchive"),
        ):
            value_adapter_path = (
                "src/backend/Modules/Documents/"
                f"GoymenERP.Modules.Documents.Infrastructure/Storage/{filename}"
            )
            value_adapter = snapshot_with_file(
                self.snapshot,
                value_adapter_path,
                value_adapter_template.replace("{declaration}", declaration),
            )
            self.assertIn(
                {"name": expected_name, "path": value_adapter_path},
                generator._documents_inventory(value_adapter)["objectStoreAdapters"],
            )

        uppercase_adapter_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/UppercaseArchive.CS"
        )
        uppercase_adapter = snapshot_with_file(
            self.snapshot,
            uppercase_adapter_path,
            active_source.replace("ActiveArchive", "UppercaseArchive"),
        )
        self.assertIn(
            {"name": "UppercaseArchive", "path": uppercase_adapter_path},
            generator._documents_inventory(uppercase_adapter)["objectStoreAdapters"],
        )

        interpolated_adapter_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/InterpolatedArchive.cs"
        )
        interpolated_adapter_source = active_source.replace(
            "namespace Test;",
            'namespace Test; internal static class Carrier { internal static string M(string x) '
            '=> x; internal static string S => $"{M("//")}"; }',
            1,
        ).replace("ActiveArchive", "InterpolatedArchive")
        interpolated_adapter = snapshot_with_file(
            self.snapshot,
            interpolated_adapter_path,
            interpolated_adapter_source,
        )
        self.assertIn(
            {"name": "InterpolatedArchive", "path": interpolated_adapter_path},
            generator._documents_inventory(interpolated_adapter)["objectStoreAdapters"],
        )

        for partial_kind, partial_name in (
            ("record", "PartialRecordArchive"),
            ("record struct", "PartialRecordStructArchive"),
        ):
            partial_path = (
                "src/backend/Modules/Documents/"
                f"GoymenERP.Modules.Documents.Infrastructure/Storage/{partial_name}.cs"
            )
            partial_source = value_adapter_template.replace(
                "{declaration} : IDocumentObjectStoreV1",
                f"partial {partial_kind} {partial_name} : IDocumentObjectStoreV1;\n"
                f"internal partial {partial_kind} {partial_name}",
            )
            partial_adapter = snapshot_with_file(self.snapshot, partial_path, partial_source)
            self.assertIn(
                {"name": partial_name, "path": partial_path},
                generator._documents_inventory(partial_adapter)["objectStoreAdapters"],
            )

        unicode_adapter_path = (
            "src/backend/Modules/Documents/"
            "GoymenERP.Modules.Documents.Infrastructure/Storage/UnicodeArchive.cs"
        )
        unicode_adapter_source = alias_source.replace(
            "AliasArchive", "\\u0055nicodeArchive"
        )
        unicode_adapter = snapshot_with_file(
            self.snapshot,
            unicode_adapter_path,
            unicode_adapter_source,
        )
        with self.assertRaisesRegex(generator.InventoryError, "escaped identifier"):
            generator._documents_inventory(unicode_adapter)

        literal_unicode_adapter = snapshot_with_file(
            self.snapshot,
            unicode_adapter_path,
            alias_source.replace("AliasArchive", "Şrchive"),
        )
        with self.assertRaisesRegex(generator.InventoryError, "non-ASCII"):
            generator._documents_inventory(literal_unicode_adapter)

        extern_alias_adapter = snapshot_with_file(
            self.snapshot,
            unicode_adapter_path,
            active_source.replace(
                "namespace Test;",
                "extern alias DocAlias;\nnamespace Test;",
                1,
            ).replace(
                ": IDocumentObjectStoreV1",
                ": DocAlias::GoymenERP.Modules.Documents.Application.Contracts."
                "IDocumentObjectStoreV1",
                1,
            ),
        )
        with self.assertRaisesRegex(generator.InventoryError, "extern alias"):
            generator._documents_inventory(extern_alias_adapter)

        suffix_alias_adapter = snapshot_with_file(
            self.snapshot,
            unicode_adapter_path,
            active_source.replace(
                "namespace Test;",
                "using Documentglobal = Test;\nnamespace Test;",
                1,
            ).replace(
                ": IDocumentObjectStoreV1",
                ": Documentglobal::IDocumentObjectStoreV1",
                1,
            ),
        )
        with self.assertRaisesRegex(generator.InventoryError, "alias qualifier"):
            generator._documents_inventory(suffix_alias_adapter)

        namespace_poisoned_adapter = snapshot_with_file(
            self.snapshot,
            unicode_adapter_path,
            "namespace Poison { using IDocumentObjectStoreV1 = System.IDisposable; "
            "internal sealed class Decoy : IDocumentObjectStoreV1 { } } "
            "namespace Real { internal sealed class ScopeHiddenArchive : "
            "global::GoymenERP.Modules.Documents.Application.Contracts.IDocumentObjectStoreV1 { } }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "namespace-local using alias"):
            generator._documents_inventory(namespace_poisoned_adapter)

        for alias_prefix in ("\ufeffusing", "using unsafe", "global using unsafe"):
            alias_variant_path = (
                "src/backend/Modules/Documents/"
                "GoymenERP.Modules.Documents.Infrastructure/Storage/AliasVariantArchive.cs"
            )
            alias_variant_source = alias_source.replace(
                "using StoreAlias",
                f"{alias_prefix} StoreAlias",
                1,
            ).replace("AliasArchive", "AliasVariantArchive")
            alias_variant = snapshot_with_file(
                self.snapshot,
                alias_variant_path,
                alias_variant_source,
            )
            self.assertIn(
                {"name": "AliasVariantArchive", "path": alias_variant_path},
                generator._documents_inventory(alias_variant)["objectStoreAdapters"],
            )

    def test_documents_recovery_root_is_bound_to_operative_adapters(self) -> None:
        file_store = next(
            adapter["path"]
            for adapter in self.inventory["documents"]["objectStoreAdapters"]
            if adapter["name"] == "FileSystemDocumentObjectStore"
        )
        bypassed_resolve = mutate_snapshot(
            self.snapshot,
            file_store,
            lambda text: text.replace(
                "Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar))",
                "Path.Combine(Path.GetTempPath(), key.Replace('/', Path.DirectorySeparatorChar))",
                1,
            ),
        )
        with self.assertRaisesRegex(generator.InventoryError, "configured-root use drifted"):
            generator._documents_inventory(bypassed_resolve)

        def configured_root_in_delegate(text: str) -> str:
            original = (
                "var path = Path.GetFullPath(Path.Combine(root, "
                "key.Replace('/', Path.DirectorySeparatorChar)));"
            )
            replacement = (
                "Func<string> configuredRootProof = delegate\n        {\n"
                "            return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));\n"
                "        };\n        GC.KeepAlive(configuredRootProof);\n"
                "        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "
                "key.Replace('/', Path.DirectorySeparatorChar)));"
            )
            return text.replace(original, replacement, 1)

        deferred_root = mutate_snapshot(self.snapshot, file_store, configured_root_in_delegate)
        with self.assertRaisesRegex(generator.InventoryError, "configured-root use drifted"):
            generator._documents_inventory(deferred_root)

        live_decoy_root = mutate_snapshot(
            self.snapshot,
            file_store,
            lambda text: text.replace(
                "var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));",
                "GC.KeepAlive(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));\n"
                "        var alternateRoot = Path.GetTempPath();\n"
                "        var path = Path.GetFullPath(Path.Combine(alternateRoot, "
                "key.Replace('/', Path.DirectorySeparatorChar)));",
                1,
            ),
        )
        with self.assertRaisesRegex(generator.InventoryError, "configured-root use drifted"):
            generator._documents_inventory(live_decoy_root)

        bypassed_registration = mutate_snapshot(
            self.snapshot,
            generator.DOCUMENT_MODULE_PATH,
            lambda text: text.replace(
                "ServerDirectoryDocumentObjectStore.Open(root!)",
                "ServerDirectoryDocumentObjectStore.Open(Path.GetTempPath())",
                1,
            ),
        )
        with self.assertRaisesRegex(generator.InventoryError, "operative-method anchor drifted"):
            generator._documents_inventory(bypassed_registration)

        server_store = next(
            adapter["path"]
            for adapter in self.inventory["documents"]["objectStoreAdapters"]
            if adapter["name"] == "ServerDirectoryDocumentObjectStore"
        )

        def replace_and_append(
            text: str,
            original: str,
            replacement: str,
            helper: str,
        ) -> str:
            changed = text.replace(original, replacement, 1)
            if changed == text:
                raise AssertionError(f"Mutation anchor not found: {original}")
            closing = changed.rfind("}")
            return changed[:closing] + "\n\n    " + helper + "\n" + changed[closing:]

        def decoy_resolved_path(text: str) -> str:
            signature = (
                "    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, "
                "string sha256, CancellationToken cancellationToken = default)"
            )
            method = extract_csharp_method(text, signature)
            anchor = "        var path = Resolve(key);"
            before, after = method.split(anchor, 1)
            after = re.sub(r"\bpath\b", "alternatePath", after)
            replacement = (
                before
                + anchor
                + "\n        GC.KeepAlive(path);\n"
                + "        var alternatePath = Path.GetFullPath(Path.Combine("
                + "Path.GetTempPath(), key.Replace('/', Path.DirectorySeparatorChar)));"
                + after
            )
            return text.replace(method, replacement, 1)

        root_chain_mutations = (
            (
                generator.DOCUMENT_MODULE_PATH,
                lambda text: text.replace(
                    "var root = configuration[ObjectStoreRootPathKey];",
                    "var root = configuration[ObjectStoreRootPathKey];\n"
                    "            root = Path.GetTempPath();",
                    1,
                ),
            ),
            (
                generator.DOCUMENT_MODULE_PATH,
                lambda text: text.replace(
                    "var root = configuration[ObjectStoreRootPathKey];",
                    "var root = configuration[ObjectStoreRootPathKey];\n"
                    "        root = Path.GetTempPath();",
                    2,
                ),
            ),
            (
                file_store,
                lambda text: text.replace(
                    "root = Path.GetFullPath(rootPath);",
                    "GC.KeepAlive(Path.GetFullPath(rootPath));\n"
                    "        root = Path.GetTempPath();",
                    1,
                ),
            ),
            (
                server_store,
                lambda text: text.replace(
                    "return new ServerDirectoryDocumentObjectStore(fullRoot);",
                    "GC.KeepAlive(new ServerDirectoryDocumentObjectStore(fullRoot));\n"
                    "        return new ServerDirectoryDocumentObjectStore(Path.GetTempPath());",
                    1,
                ),
            ),
            (
                server_store,
                lambda text: text.replace(
                    "root = fullRoot;",
                    "GC.KeepAlive(fullRoot);\n        root = Path.GetTempPath();",
                    1,
                ),
            ),
            (
                file_store,
                lambda text: text.replace(
                    "var path = Resolve(key);",
                    "var path = Path.Combine(Path.GetTempPath(), key);",
                ),
            ),
            (
                server_store,
                lambda text: text.replace(
                    "var path = Resolve(key);",
                    "var path = Path.Combine(Path.GetTempPath(), key);",
                ),
            ),
            (
                file_store,
                lambda text: text.replace(
                    "var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));",
                    "(string root, int ignored) = (Path.GetTempPath(), 0);\n"
                    "        var path = Path.GetFullPath(Path.Combine(root, "
                    "key.Replace('/', Path.DirectorySeparatorChar)));",
                    1,
                ),
            ),
            (
                generator.DOCUMENT_MODULE_PATH,
                lambda text: text.replace(
                    "var root = configuration[ObjectStoreRootPathKey];\n            var store",
                    "var root = configuration[ObjectStoreRootPathKey];\n"
                    "            (root, _) = (Path.GetTempPath(), 0);\n            var store",
                    1,
                ),
            ),
            (
                generator.DOCUMENT_MODULE_PATH,
                lambda text: text.replace(
                    "var root = configuration[ObjectStoreRootPathKey];\n        var rootConfigured",
                    "var root = configuration[ObjectStoreRootPathKey];\n"
                    "        (root, _) = (Path.GetTempPath(), 0);\n        var rootConfigured",
                    1,
                ),
            ),
            (
                file_store,
                lambda text: text.replace(
                    "root = Path.GetFullPath(rootPath);",
                    "root = Path.GetFullPath(rootPath);\n"
                    "        (root, _) = (Path.GetTempPath(), 0);",
                    1,
                ),
            ),
            (
                server_store,
                lambda text: text.replace(
                    "var fullRoot = Path.GetFullPath(rootPath);",
                    "var fullRoot = Path.GetFullPath(rootPath);\n"
                    "        (fullRoot, _) = (Path.GetTempPath(), 0);",
                    1,
                ),
            ),
            (
                server_store,
                lambda text: text.replace(
                    "root = fullRoot;",
                    "root = fullRoot;\n        (root, _) = (Path.GetTempPath(), 0);",
                    1,
                ),
            ),
            (
                file_store,
                lambda text: text.replace(
                    "var path = Resolve(key);",
                    "var path = Resolve(key);\n"
                    "        (path, _) = (Path.Combine(Path.GetTempPath(), key), 0);",
                ),
            ),
            (
                file_store,
                lambda text: text.replace(
                    "var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));",
                    "((string root, int inner), int outer) = ((Path.GetTempPath(), 0), 0);\n"
                    "        var path = Path.GetFullPath(Path.Combine(root, "
                    "key.Replace('/', Path.DirectorySeparatorChar)));",
                    1,
                ),
            ),
            (
                generator.DOCUMENT_MODULE_PATH,
                lambda text: text.replace(
                    "var root = configuration[ObjectStoreRootPathKey];\n            var store",
                    "var root = configuration[ObjectStoreRootPathKey];\n"
                    "            ((root, _), _) = ((Path.GetTempPath(), 0), 0);\n"
                    "            var store",
                    1,
                ),
            ),
            (
                server_store,
                lambda text: text.replace(
                    "var fullRoot = Path.GetFullPath(rootPath);",
                    "var fullRoot = Path.GetFullPath(rootPath);\n"
                    "        ((fullRoot, _), _) = ((Path.GetTempPath(), 0), 0);",
                    1,
                ),
            ),
            (
                file_store,
                lambda text: text.replace(
                    "var path = Resolve(key);",
                    "var path = Resolve(key);\n"
                    "        ((path, _), _) = ((Path.Combine(Path.GetTempPath(), key), 0), 0);",
                ),
            ),
            (
                generator.DOCUMENT_MODULE_PATH,
                lambda text: text.replace(
                    "var root = configuration[ObjectStoreRootPathKey];\n            var store",
                    "var root = configuration[ObjectStoreRootPathKey];\n"
                    "            \\u0072oot = Path.GetTempPath();\n            var store",
                    1,
                ),
            ),
            (
                file_store,
                lambda text: replace_and_append(
                    text,
                    "var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));",
                    "UseTemporaryRoot(out var root);\n"
                    "        var path = Path.GetFullPath(Path.Combine(root, "
                    "key.Replace('/', Path.DirectorySeparatorChar)));",
                    "private static void UseTemporaryRoot(out string value) => "
                    "value = Path.GetTempPath();",
                ),
            ),
            (
                generator.DOCUMENT_MODULE_PATH,
                lambda text: replace_and_append(
                    text,
                    "var root = configuration[ObjectStoreRootPathKey];\n            var store",
                    "var root = configuration[ObjectStoreRootPathKey];\n"
                    "            UseTemporaryRoot(ref root);\n            var store",
                    "private static void UseTemporaryRoot(ref string? value) => "
                    "value = Path.GetTempPath();",
                ),
            ),
            (
                generator.DOCUMENT_MODULE_PATH,
                lambda text: replace_and_append(
                    text,
                    "var root = configuration[ObjectStoreRootPathKey];\n        var rootConfigured",
                    "var root = configuration[ObjectStoreRootPathKey];\n"
                    "        UseTemporaryRoot(ref root);\n        var rootConfigured",
                    "private static void UseTemporaryRoot(ref string? value) => "
                    "value = Path.GetTempPath();",
                ),
            ),
            (
                file_store,
                lambda text: replace_and_append(
                    text,
                    "root = Path.GetFullPath(rootPath);",
                    "root = Path.GetFullPath(rootPath);\n        UseTemporaryRoot(out root);",
                    "private static void UseTemporaryRoot(out string value) => "
                    "value = Path.GetTempPath();",
                ),
            ),
            (
                server_store,
                lambda text: replace_and_append(
                    text,
                    "var fullRoot = Path.GetFullPath(rootPath);",
                    "var fullRoot = Path.GetFullPath(rootPath);\n"
                    "        UseTemporaryRoot(ref fullRoot);",
                    "private static void UseTemporaryRoot(ref string value) => "
                    "value = Path.GetTempPath();",
                ),
            ),
            (
                server_store,
                lambda text: replace_and_append(
                    text,
                    "root = fullRoot;",
                    "root = fullRoot;\n        UseTemporaryRoot(out root);",
                    "private static void UseTemporaryRoot(out string value) => "
                    "value = Path.GetTempPath();",
                ),
            ),
            (
                file_store,
                lambda text: replace_and_append(
                    text,
                    "var path = Resolve(key);",
                    "var path = Resolve(key);\n        UseTemporaryPath(ref path, key);",
                    "private static void UseTemporaryPath(ref string value, string key) => "
                    "value = Path.Combine(Path.GetTempPath(), key);",
                ),
            ),
            (
                server_store,
                lambda text: replace_and_append(
                    text,
                    "var path = Resolve(key);",
                    "var path = Resolve(key);\n        UseTemporaryPath(ref path, key);",
                    "private static void UseTemporaryPath(ref string value, string key) => "
                    "value = Path.Combine(Path.GetTempPath(), key);",
                ),
            ),
            (file_store, decoy_resolved_path),
        )
        for path, transform in root_chain_mutations:
            with self.subTest(path=path, transform=transform):
                changed = mutate_snapshot(self.snapshot, path, transform)
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

    def test_non_live_writer_anchor_fails_closed(self) -> None:
        token = "dbContext.Commands.Add(new AccountingCommandRow"
        transforms = (
            lambda text: text.replace(token, f"// {token}", 1),
            lambda text: text.replace(token, f"/* {token} */", 1),
            accounting_writer_in_disabled_region,
            accounting_writer_in_unknown_preprocessor_region,
            accounting_writer_in_string_only,
            accounting_writer_in_deferred_lambda,
            accounting_writer_in_anonymous_delegate,
            accounting_writer_in_local_function,
            accounting_writer_in_attributed_local_function,
            accounting_writer_in_escaped_local_function,
            accounting_writer_in_tuple_local_function,
            accounting_writer_in_same_line_local_function,
            accounting_writer_in_expression_lambda_with_initializer,
            accounting_writer_in_global_return_local_function,
            accounting_writer_in_unicode_escaped_local_function,
            accounting_writer_in_default_parameter_local_function,
            accounting_writer_in_multiline_attributed_local_function,
            accounting_writer_in_literal_unicode_local_function,
            accounting_writer_in_deferred_linq,
            accounting_writer_in_conditional_call,
            accounting_writer_in_dead_ternary,
            accounting_writer_in_dead_and,
            accounting_writer_in_dead_or,
            accounting_writer_in_dead_switch,
            accounting_writer_in_dead_coalesce,
            accounting_writer_on_conditional_method,
            accounting_writer_in_iterator,
            accounting_writer_then_clear_change_tracker,
            accounting_writer_in_false_runtime_branch,
            accounting_writer_call_sites_removed,
            accounting_writer_in_unused_method,
        )
        for transform in transforms:
            with self.subTest(transform=transform):
                changed = mutate_snapshot(
                    self.snapshot,
                    generator.ACCOUNTING_STORE,
                    transform,
                )
                with self.assertRaises(generator.InventoryError):
                    generator._durable_store_inventory(changed)

    def test_context_and_migration_declaration_sets_fail_closed(self) -> None:
        contexts = generator._discover_csharp_subclasses(self.snapshot, "DbContext")
        migrations = generator._discover_csharp_subclasses(self.snapshot, "Migration")
        self.assertEqual(18, len(contexts))
        self.assertEqual(95, len(migrations))
        accepted_main_migrations = {
            "20261014120000_AddManufacturingPlanCutLines.cs",
            "20261015110000_ReleaseHotRolledCutSheetPlateTechnicalDefinition.cs",
            "20261015123000_AddManufacturingPlanTechnicalLengthSnapshots.cs",
            "20261015150000_SerializeManufacturingTolerancePolicyWindows.cs",
        }
        self.assertTrue(
            accepted_main_migrations.issubset(
                {Path(declaration.path).name for declaration in migrations}
            )
        )
        self.assertEqual(
            generator.EXPECTED_DB_CONTEXT_DECLARATION_FINGERPRINT,
            generator._declaration_set_fingerprint(contexts),
        )
        self.assertEqual(
            generator.EXPECTED_MIGRATION_DECLARATION_FINGERPRINT,
            generator._declaration_set_fingerprint(migrations),
        )

        second_context_same_file = mutate_snapshot(
            self.snapshot,
            generator.ACCOUNTING_CONTEXT,
            lambda text: text
            + "\ninternal sealed class ShadowDbContext(DbContextOptions<ShadowDbContext> options) "
            + ": DbContext(options) { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext declaration surface drifted"):
            generator._db_context_inventory(second_context_same_file)

        aliased_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowAliasContext.cs",
            "using EfDbContext = Microsoft.EntityFrameworkCore.DbContext;\n"
            "internal sealed class ShadowAliasContext : EfDbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext declaration surface drifted"):
            generator._db_context_inventory(aliased_context)

        block_namespace_alias_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowBlockAliasContext.cs",
            "namespace Probe { using EfDbContext = Microsoft.EntityFrameworkCore.DbContext; "
            "internal sealed class ShadowBlockAliasContext : EfDbContext { } }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "namespace-local using alias"):
            generator._db_context_inventory(block_namespace_alias_context)

        wrong_qualified_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowQualifiedContext.cs",
            "internal sealed class ShadowQualifiedContext : Shadow.DbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "unexpected namespace"):
            generator._db_context_inventory(wrong_qualified_context)

        target_shadow_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowDbContextType.cs",
            "namespace Microsoft.EntityFrameworkCore; internal abstract class DbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "target type declaration surface drifted"):
            generator._db_context_inventory(target_shadow_context)

        escaped_aliased_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowEscapedAliasContext.cs",
            "using @EfContext = Microsoft.EntityFrameworkCore.DbContext;\n"
            "internal sealed class @ShadowEscapedAliasContext : @EfContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext declaration surface drifted"):
            generator._db_context_inventory(escaped_aliased_context)

        compact_aliased_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowCompactAliasContext.cs",
            "using HarmlessAlias = System.String; "
            "using FinalContext = Microsoft.EntityFrameworkCore.DbContext; "
            "internal sealed class ShadowCompactAliasContext : FinalContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext declaration surface drifted"):
            generator._db_context_inventory(compact_aliased_context)

        unicode_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowUnicodeContext.cs",
            "internal sealed class \\u0053hadowUnicodeContext : DbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "escaped identifier"):
            generator._db_context_inventory(unicode_context)

        for name in ("ŞhadowUnicodeContext", "S\u0301hadowCombiningContext"):
            literal_unicode_context = snapshot_with_file(
                self.snapshot,
                "src/backend/Modules/Audit/ShadowLiteralUnicodeContext.cs",
                f"internal sealed class {name} : DbContext {{ }}\n",
            )
            with self.assertRaisesRegex(generator.InventoryError, "non-ASCII"):
                generator._db_context_inventory(literal_unicode_context)

        extern_alias_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowExternAliasContext.cs",
            "extern alias EfAlias;\n"
            "internal sealed class ShadowExternAliasContext : "
            "EfAlias::Microsoft.EntityFrameworkCore.DbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "extern alias"):
            generator._db_context_inventory(extern_alias_context)

        suffix_alias_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowSuffixAliasContext.cs",
            "using Notglobal = Microsoft.EntityFrameworkCore;\n"
            "internal sealed class ShadowSuffixAliasContext : Notglobal::DbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "alias qualifier"):
            generator._db_context_inventory(suffix_alias_context)

        uppercase_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowUppercaseContext.CS",
            "internal sealed class ShadowUppercaseContext : DbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext declaration surface drifted"):
            generator._db_context_inventory(uppercase_context)

        interpolated_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowInterpolatedContext.cs",
            'namespace Test; internal static class Carrier { internal static string M(string x) '
            '=> x; internal static string S => $"{M("//")}"; } '
            "internal sealed class ShadowInterpolatedStringContext : DbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext declaration surface drifted"):
            generator._db_context_inventory(interpolated_context)

        namespace_poisoned_context = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowNamespaceAliasContext.cs",
            "namespace Poison { using DbContext = System.Object; "
            "internal sealed class Decoy : DbContext { } } "
            "namespace Real { internal sealed class ScopeHiddenContext : "
            "global::Microsoft.EntityFrameworkCore.DbContext { } }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "namespace-local using alias"):
            generator._db_context_inventory(namespace_poisoned_context)

        for alias_prefix in ("\ufeffusing", "using unsafe", "global using unsafe"):
            alias_variant_context = snapshot_with_file(
                self.snapshot,
                "src/backend/Modules/Audit/ShadowAliasVariantContext.cs",
                f"{alias_prefix} EfContext = Microsoft.EntityFrameworkCore.DbContext;\n"
                "internal sealed class ShadowAliasVariantContext : EfContext { }\n",
            )
            with self.assertRaisesRegex(
                generator.InventoryError,
                "DbContext declaration surface drifted",
            ):
                generator._db_context_inventory(alias_variant_context)

        derived_context = mutate_snapshot(
            self.snapshot,
            generator.ACCOUNTING_CONTEXT,
            lambda text: text.replace("sealed class AccountingDbContext", "class AccountingDbContext", 1)
            + "\ninternal sealed class ShadowDerivedContext : AccountingDbContext { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "DbContext declaration surface drifted"):
            generator._db_context_inventory(derived_context)

        migration_outside_directory = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowRecoveryStep.cs",
            "internal sealed class ShadowRecoveryStep : Migration "
            "{ protected override void Up(MigrationBuilder migrationBuilder) { } "
            "protected override void Down(MigrationBuilder migrationBuilder) { } }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "Migration declaration surface drifted"):
            generator._migration_inventory(migration_outside_directory)

        aliased_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowAliasMigration.cs",
            "using EfMigration = Microsoft.EntityFrameworkCore.Migrations.Migration;\n"
            "internal sealed class ShadowAliasMigration : EfMigration "
            "{ protected override void Up(MigrationBuilder migrationBuilder) { } "
            "protected override void Down(MigrationBuilder migrationBuilder) { } }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "Migration declaration surface drifted"):
            generator._migration_inventory(aliased_migration)

        block_namespace_alias_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowBlockAliasMigration.cs",
            "namespace Probe { using EfMigration = "
            "Microsoft.EntityFrameworkCore.Migrations.Migration; "
            "internal sealed class ShadowBlockAliasMigration : EfMigration { } }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "namespace-local using alias"):
            generator._migration_inventory(block_namespace_alias_migration)

        wrong_qualified_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowQualifiedMigration.cs",
            "internal sealed class ShadowQualifiedMigration : Shadow.Migration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "unexpected namespace"):
            generator._migration_inventory(wrong_qualified_migration)

        target_shadow_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowMigrationType.cs",
            "namespace Microsoft.EntityFrameworkCore.Migrations; "
            "internal abstract class Migration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "target type declaration surface drifted"):
            generator._migration_inventory(target_shadow_migration)

        escaped_aliased_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowEscapedAliasMigration.cs",
            "using @EfMigration = Microsoft.EntityFrameworkCore.Migrations.Migration;\n"
            "internal sealed class @ShadowEscapedAliasMigration : @EfMigration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "Migration declaration surface drifted"):
            generator._migration_inventory(escaped_aliased_migration)

        compact_aliased_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowCompactAliasMigration.cs",
            "using HarmlessAlias = System.String; "
            "using FinalMigration = Microsoft.EntityFrameworkCore.Migrations.Migration; "
            "internal sealed class ShadowCompactAliasMigration : FinalMigration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "Migration declaration surface drifted"):
            generator._migration_inventory(compact_aliased_migration)

        unicode_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowUnicodeMigration.cs",
            "internal sealed class \\u0053hadowUnicodeMigration : Migration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "escaped identifier"):
            generator._migration_inventory(unicode_migration)

        for name in ("ŞhadowUnicodeMigration", "S\u0301hadowCombiningMigration"):
            literal_unicode_migration = snapshot_with_file(
                self.snapshot,
                "src/backend/Modules/Audit/ShadowLiteralUnicodeMigration.cs",
                f"internal sealed class {name} : Migration {{ }}\n",
            )
            with self.assertRaisesRegex(generator.InventoryError, "non-ASCII"):
                generator._migration_inventory(literal_unicode_migration)

        extern_alias_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowExternAliasMigration.cs",
            "extern alias EfAlias;\n"
            "internal sealed class ShadowExternAliasMigration : "
            "EfAlias::Microsoft.EntityFrameworkCore.Migrations.Migration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "extern alias"):
            generator._migration_inventory(extern_alias_migration)

        suffix_alias_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowSuffixAliasMigration.cs",
            "using Migrationglobal = Microsoft.EntityFrameworkCore.Migrations;\n"
            "internal sealed class ShadowSuffixAliasMigration : Migrationglobal::Migration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "alias qualifier"):
            generator._migration_inventory(suffix_alias_migration)

        uppercase_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowUppercaseMigration.CS",
            "internal sealed class ShadowUppercaseMigration : Migration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "Migration declaration surface drifted"):
            generator._migration_inventory(uppercase_migration)

        interpolated_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowInterpolatedMigration.cs",
            'namespace Test; internal static class Carrier { internal static string M(string x) '
            '=> x; internal static string S => $"{M("//")}"; } '
            "internal sealed class ShadowInterpolatedStringMigration : Migration { }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "Migration declaration surface drifted"):
            generator._migration_inventory(interpolated_migration)

        namespace_poisoned_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowNamespaceAliasMigration.cs",
            "namespace Poison { using Migration = System.Object; "
            "internal sealed class Decoy : Migration { } } "
            "namespace Real { internal sealed class ScopeHiddenMigration : "
            "global::Microsoft.EntityFrameworkCore.Migrations.Migration { } }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "namespace-local using alias"):
            generator._migration_inventory(namespace_poisoned_migration)

        for alias_prefix in ("\ufeffusing", "using unsafe", "global using unsafe"):
            alias_variant_migration = snapshot_with_file(
                self.snapshot,
                "src/backend/Modules/Audit/ShadowAliasVariantMigration.cs",
                f"{alias_prefix} EfMigration = "
                "Microsoft.EntityFrameworkCore.Migrations.Migration;\n"
                "internal sealed class ShadowAliasVariantMigration : EfMigration { }\n",
            )
            with self.assertRaisesRegex(
                generator.InventoryError,
                "Migration declaration surface drifted",
            ):
                generator._migration_inventory(alias_variant_migration)

        migration_path = self.inventory["migrations"][0]["path"]
        second_migration_same_file = mutate_snapshot(
            self.snapshot,
            migration_path,
            lambda text: text
            + "\ninternal sealed class ShadowSecondMigration : Migration "
            + "{ protected override void Up(MigrationBuilder migrationBuilder) { } "
            + "protected override void Down(MigrationBuilder migrationBuilder) { } }\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "Migration declaration surface drifted"):
            generator._migration_inventory(second_migration_same_file)

        migration_class = Path(migration_path).stem.split("_", 1)[-1]
        derived_migration = snapshot_with_file(
            self.snapshot,
            "src/backend/Modules/Audit/ShadowDerivedMigration.cs",
            f"internal sealed class ShadowDerivedMigration : {migration_class} {{ }}\n",
        )
        with self.assertRaisesRegex(generator.InventoryError, "Migration declaration surface drifted"):
            generator._migration_inventory(derived_migration)

    def test_reviewed_compilation_surface_rejects_build_and_symbol_injection(self) -> None:
        source_paths = generator._source_surface_paths(self.snapshot)
        self.assertEqual(1063, len(source_paths))
        self.assertIn(
            "src/backend/Modules/Inventory/"
            "GoymenERP.Modules.Inventory.Application/InventoryTransferDocumentProofContractsV1.cs",
            source_paths,
        )
        self.assertEqual(
            (
                generator.EXPECTED_SOURCE_SURFACE_FILE_COUNT,
                generator.EXPECTED_SOURCE_SURFACE_FINGERPRINT,
            ),
            generator._source_surface_fingerprint(self.snapshot),
        )
        api_project = "src/backend/GoymenERP.Api/GoymenERP.Api.csproj"
        mutations = (
            mutate_snapshot(
                self.snapshot,
                api_project,
                lambda text: text.replace(
                    "</Project>",
                    '<ItemGroup><Compile Include="..\\..\\HiddenDeclarations.source" />'
                    "</ItemGroup></Project>",
                    1,
                ),
            ),
            mutate_snapshot(
                self.snapshot,
                api_project,
                lambda text: text.replace(
                    "</Project>",
                    '<ItemGroup><Using Include="Microsoft.EntityFrameworkCore.DbContext" '
                    'Alias="Ctx" /></ItemGroup></Project>',
                    1,
                ),
            ),
            mutate_snapshot(
                self.snapshot,
                api_project,
                lambda text: text.replace(
                    "</Project>",
                    '<ItemGroup><Analyzer Include="..\\..\\Generator.dll" />'
                    "</ItemGroup></Project>",
                    1,
                ),
            ),
            snapshot_with_file(
                self.snapshot,
                "src/backend/GoymenERP.Api/GoymenERP.Api.csproj.user",
                '<Project><ItemGroup><Compile Include="..\\..\\Hidden.cs" />'
                "</ItemGroup></Project>\n",
            ),
            snapshot_with_file(
                self.snapshot,
                "src/backend/Modules/Documents/"
                "GoymenERP.Modules.Documents.Infrastructure/Storage/Path.cs",
                "namespace GoymenERP.Modules.Documents.Infrastructure.Storage; "
                "internal static class Path { }\n",
            ),
            snapshot_with_file(
                self.snapshot,
                "Directory.Build.targets",
                '<Project><ItemGroup><Compile Include="Hidden.cs" /></ItemGroup></Project>\n',
            ),
            snapshot_with_file(
                self.snapshot,
                "MSBuild.rsp",
                "/p:CustomAfterMicrosoftCommonTargets=Hidden.targets\n",
            ),
            snapshot_with_file(
                self.snapshot,
                "src/Directory.Build.props",
                '<Project><ItemGroup><Analyzer Include="Generator.dll" /></ItemGroup></Project>\n',
            ),
        )
        for changed in mutations:
            with self.subTest(paths=set(changed.files) - set(self.snapshot.files)):
                with self.assertRaisesRegex(
                    generator.InventoryError,
                    "compilation source surface drifted",
                ):
                    generator._build_inventory_from_snapshot(changed)

        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            backend = repository / "src/backend/GoymenERP.Api"
            backend.mkdir(parents=True)
            ignored_user = backend / "GoymenERP.Api.csproj.user"
            ignored_user.write_text("<Project />\n", encoding="utf-8")
            with self.assertRaisesRegex(generator.InventoryError, "source shadow"):
                generator._require_no_worktree_source_shadows(repository, {})
            ignored_user.unlink()
            lowercase_control = repository / "directory.build.targets"
            lowercase_control.write_text("<Project />\n", encoding="utf-8")
            with self.assertRaisesRegex(generator.InventoryError, "build-control shadow"):
                generator._require_no_worktree_source_shadows(repository, {})

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

    def test_provenance_rejects_skip_worktree_altered_generator_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            sources = {
                "src/backend/Proof.cs": b"internal sealed class Proof { }\n",
                generator.SCHEMA_PATH.as_posix(): b"{}\n",
                generator.GENERATOR_PATH.as_posix(): GENERATOR_PATH.read_bytes(),
            }
            for relative_path, content in sources.items():
                path = repository / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(content)

            def git(*arguments: str) -> subprocess.CompletedProcess[bytes]:
                return subprocess.run(
                    ["git", *arguments],
                    cwd=repository,
                    capture_output=True,
                    check=True,
                )

            git("init")
            git("config", "user.name", "Recovery Inventory Test")
            git("config", "user.email", "recovery-inventory@example.invalid")
            git("add", ".")
            git("commit", "-m", "fixture")
            head = git("rev-parse", "HEAD").stdout.decode("ascii").strip()
            exact_generator = git(
                "cat-file",
                "blob",
                f"{head}:{generator.GENERATOR_PATH.as_posix()}",
            ).stdout
            git("update-index", "--skip-worktree", generator.GENERATOR_PATH.as_posix())
            with (repository / generator.GENERATOR_PATH).open("ab") as altered:
                altered.write(b"\n# altered executed generator\n")

            self.assertEqual(b"", git("status", "--porcelain=v1").stdout)
            with tempfile.TemporaryDirectory() as trusted_directory:
                trusted = Path(trusted_directory) / "generate_recovery_surface_inventory.py"
                trusted.write_bytes(exact_generator)
                result = subprocess.run(
                    [
                        sys.executable,
                        "-I",
                        "-S",
                        "-B",
                        str(trusted),
                        "--repository-root",
                        str(repository),
                        "--source-commit",
                        head,
                    ],
                    cwd=repository,
                    check=False,
                    capture_output=True,
                )
            self.assertEqual(2, result.returncode)
            self.assertEqual(b"", result.stdout)
            self.assertRegex(
                result.stderr.decode("utf-8"),
                "hidden Git index flags",
            )

    def test_provenance_rejects_hidden_generator_index_flags_with_exact_bytes(self) -> None:
        for flag, clear_flag in (
            ("--skip-worktree", "--no-skip-worktree"),
            ("--assume-unchanged", "--no-assume-unchanged"),
        ):
            with self.subTest(flag=flag), tempfile.TemporaryDirectory() as directory:
                repository = Path(directory)
                sources = {
                    "src/backend/Proof.cs": b"internal sealed class Proof { }\n",
                    generator.SCHEMA_PATH.as_posix(): b"{}\n",
                    generator.GENERATOR_PATH.as_posix(): GENERATOR_PATH.read_bytes(),
                }
                for relative_path, content in sources.items():
                    path = repository / relative_path
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(content)

                def git(*arguments: str) -> subprocess.CompletedProcess[bytes]:
                    return subprocess.run(
                        ["git", *arguments],
                        cwd=repository,
                        capture_output=True,
                        check=True,
                    )

                git("init")
                git("config", "user.name", "Recovery Inventory Test")
                git("config", "user.email", "recovery-inventory@example.invalid")
                git("add", ".")
                git("commit", "-m", "fixture")
                head = git("rev-parse", "HEAD").stdout.decode("ascii").strip()
                exact_generator = git(
                    "cat-file",
                    "blob",
                    f"{head}:{generator.GENERATOR_PATH.as_posix()}",
                ).stdout
                git("update-index", flag, generator.GENERATOR_PATH.as_posix())
                try:
                    self.assertEqual(b"", git("status", "--porcelain=v1").stdout)
                    with tempfile.TemporaryDirectory() as trusted_directory:
                        trusted = Path(trusted_directory) / "generate_recovery_surface_inventory.py"
                        trusted.write_bytes(exact_generator)
                        result = subprocess.run(
                            [
                                sys.executable,
                                "-I",
                                "-S",
                                "-B",
                                str(trusted),
                                "--repository-root",
                                str(repository),
                                "--source-commit",
                                head,
                            ],
                            cwd=repository,
                            check=False,
                            capture_output=True,
                        )
                    self.assertEqual(2, result.returncode)
                    self.assertEqual(b"", result.stdout)
                    self.assertRegex(
                        result.stderr.decode("utf-8"),
                        "hidden Git index flags",
                    )
                finally:
                    git("update-index", clear_flag, generator.GENERATOR_PATH.as_posix())

    def test_provenance_ignores_git_replace_refs_at_clean_head(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            sources = {
                "src/backend/Proof.cs": b"original exact-commit bytes\n",
                generator.SCHEMA_PATH.as_posix(): b"{}\n",
                generator.GENERATOR_PATH.as_posix(): b"# proof\n",
            }
            for relative_path, content in sources.items():
                path = repository / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(content)

            def git(*arguments: str, input_bytes: bytes | None = None) -> subprocess.CompletedProcess[bytes]:
                return subprocess.run(
                    ["git", *arguments],
                    cwd=repository,
                    input=input_bytes,
                    capture_output=True,
                    check=True,
                )

            git("init")
            git("config", "user.name", "Recovery Inventory Test")
            git("config", "user.email", "recovery-inventory@example.invalid")
            git("add", ".")
            git("commit", "-m", "fixture")
            head = git("rev-parse", "HEAD").stdout.decode("ascii").strip()
            target = "src/backend/Proof.cs"
            original_object = git("rev-parse", f"HEAD:{target}").stdout.decode("ascii").strip()
            replacement = b"replacement bytes that must never be inventoried\n"
            replacement_object = git("hash-object", "-w", "--stdin", input_bytes=replacement).stdout.decode("ascii").strip()
            git("replace", original_object, replacement_object)

            self.assertEqual(b"", git("status", "--porcelain=v1").stdout)
            self.assertEqual(head, git("rev-parse", "HEAD").stdout.decode("ascii").strip())
            self.assertEqual(replacement, git("cat-file", "blob", original_object).stdout)
            with mock.patch.dict(os.environ, {"GIT_NO_REPLACE_OBJECTS": "0"}):
                snapshot = generator._load_source_snapshot(repository, head)
            self.assertEqual("1", generator._git_environment()["GIT_NO_REPLACE_OBJECTS"])
            self.assertEqual(sources[target], snapshot.files[target])

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

        exact_generator = subprocess.run(
            [
                "git",
                "cat-file",
                "blob",
                f"{SOURCE_COMMIT}:{generator.GENERATOR_PATH.as_posix()}",
            ],
            cwd=REPOSITORY_ROOT,
            check=True,
            capture_output=True,
        ).stdout
        with tempfile.TemporaryDirectory() as trusted_directory:
            trusted = Path(trusted_directory) / "generate_recovery_surface_inventory.py"
            trusted.write_bytes(exact_generator)
            trusted_result = subprocess.run(
                [
                    sys.executable,
                    "-I",
                    "-S",
                    "-B",
                    str(trusted),
                    "--repository-root",
                    str(REPOSITORY_ROOT),
                    "--source-commit",
                    SOURCE_COMMIT,
                ],
                cwd=REPOSITORY_ROOT,
                check=False,
                capture_output=True,
            )
        self.assertEqual(
            0,
            trusted_result.returncode,
            trusted_result.stderr.decode("utf-8", errors="replace"),
        )
        self.assertEqual(first, trusted_result.stdout)
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
