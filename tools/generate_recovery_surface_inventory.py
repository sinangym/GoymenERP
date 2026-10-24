#!/usr/bin/env python3
"""Generate a deterministic, source-only recovery surface inventory."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path, PurePosixPath
from typing import Any, NamedTuple, Sequence


SCHEMA_VERSION = "goymenerp.recovery-surface-inventory.v1"
INVENTORY_MODE = "SOURCE_ONLY"
SCHEMA_PATH = Path("contracts/recovery/recovery-surface-inventory-v1.schema.json")
GENERATOR_PATH = Path("tools/generate_recovery_surface_inventory.py")
BACKEND_ROOT = Path("src/backend")
REPOSITORY_BUILD_CONTROL_PATHS = (
    ".editorconfig",
    ".globalconfig",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Build.rsp",
    "Directory.Packages.props",
    "Directory.Packages.targets",
    "Directory.Solution.props",
    "Directory.Solution.targets",
    "MSBuild.rsp",
    "NuGet.Config",
    "global.json",
    "src/.editorconfig",
    "src/.globalconfig",
    "src/Directory.Build.props",
    "src/Directory.Build.targets",
    "src/Directory.Packages.props",
    "src/Directory.Packages.targets",
    "src/Directory.Solution.props",
    "src/Directory.Solution.targets",
    "src/MSBuild.rsp",
    "src/NuGet.Config",
    "src/global.json",
)

EXPECTED_DB_CONTEXT_COUNT = 18
EXPECTED_DB_CONTEXT_DECLARATION_FINGERPRINT = (
    "2ee597539af89dc1882f755be2371a6567cd14ddbd5b7f0fbcb79f1ef6b3affa"
)
EXPECTED_MIGRATION_COUNT = 96
EXPECTED_MIGRATION_DECLARATION_FINGERPRINT = (
    "27ac7349e65da82402f838b812e279ebcef7a5b8d76dba1e67839aef19ad7c9f"
)
EXPECTED_DURABLE_STORE_COUNT = 21
# The source-only inventory is reviewed for one frozen backend compilation surface.  A
# project/build edit or any compiled source byte change must be deliberately re-inventoried;
# otherwise linked files, generators, global aliases, or symbol-shadow files could evade a
# lexical inventory while leaving its visible declaration counts unchanged.
EXPECTED_SOURCE_SURFACE_FILE_COUNT = 1076
EXPECTED_SOURCE_SURFACE_FINGERPRINT = (
    "012dbb2c007246cc4854eab2d113c77ca8e5e18e996cf217e0dd45dfc62368af"
)
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
MIGRATION_NAME_PATTERN = re.compile(r"^[0-9]{14}_[A-Za-z0-9]+\.cs$")
CSHARP_IDENTIFIER_PATTERN = r"@?[^\W\d]\w*"
CSHARP_QUALIFIED_IDENTIFIER_PATTERN = (
    rf"(?:global::)?{CSHARP_IDENTIFIER_PATTERN}"
    rf"(?:\.{CSHARP_IDENTIFIER_PATTERN})*"
)
CSHARP_TARGET_TYPE_NAMES = {
    "DbContext": "Microsoft.EntityFrameworkCore.DbContext",
    "Migration": "Microsoft.EntityFrameworkCore.Migrations.Migration",
    "IDocumentObjectStoreV1": (
        "GoymenERP.Modules.Documents.Application.Contracts.IDocumentObjectStoreV1"
    ),
}

DOCUMENT_CONFIGURATION_KEYS = (
    "Documents:MaximumUploadBytes",
    "Documents:ObjectStore:RootPath",
    "Documents:Scanner:Defender:ExecutablePath",
    "Documents:Scanner:Defender:MaximumConcurrentScans",
    "Documents:Scanner:Defender:QuarantinePath",
    "Documents:Scanner:Defender:TimeoutSeconds",
    "Documents:Scanner:Mode",
)
DOCUMENT_RUNTIME_PATH = "src/backend/GoymenERP.Api/DocumentsRuntime/DocumentsRuntimeExtensions.cs"
DOCUMENT_MODULE_PATH = (
    "src/backend/Modules/Documents/"
    "GoymenERP.Modules.Documents.Infrastructure/DocumentsModuleExtensions.cs"
)
DOCUMENT_SCANNER_PATH = (
    "src/backend/Modules/Documents/"
    "GoymenERP.Modules.Documents.Infrastructure/Scanning/WindowsDefenderDocumentScanner.cs"
)
DOCUMENT_README_PATH = "src/backend/Modules/Documents/README.md"
DOCUMENT_SOURCE_PATHS = (
    DOCUMENT_RUNTIME_PATH,
    DOCUMENT_MODULE_PATH,
    DOCUMENT_SCANNER_PATH,
    DOCUMENT_README_PATH,
)
DOCUMENTS_SOURCE_ROOT = "src/backend/Modules/Documents/"
DOCUMENT_OBJECT_STORE_INTERFACE_PATH = (
    "src/backend/Modules/Documents/"
    "GoymenERP.Modules.Documents.Application/Contracts/DocumentStoragePortsV1.cs"
)
DOCUMENT_OBJECT_KEY_PATTERN = "^companies/[0-9a-f]{32}/sha256/[0-9a-f]{64}$"
MESSAGE_ENTITY_NAME_PATTERN = re.compile(
    r"(?:outbox|inbox|deliver|message|dedup)",
    re.IGNORECASE,
)
MESSAGE_TABLE_NAME_PATTERN = re.compile(
    r"(?:outbox|inbox|deliver|message|dedup)",
    re.IGNORECASE,
)
MESSAGE_MODEL_ANCHORS = (
    "ConsumedAtUtc",
    "DispatchedAtUtc",
    "MessageId",
    "ObservationOrdinal",
    "PayloadJson",
    "PayloadSha256",
    "PayloadUtf8",
    "ProcessedAtUtc",
)
EXPECTED_OUTBOX_CONTRACT_PATHS = (
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryCustomerReturnOutboxV1.cs",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryDefiniteShipmentOutboxV1.cs",
)
EXPECTED_IDEMPOTENCY_KEY_TABLES = (
    ("accounting", "Commands"),
    ("audit", "AuditEvents"),
    ("documents", "DocumentAttempts"),
    ("finance", "Commands"),
    ("finance", "OfficialInvoiceImportCommands"),
    ("finance", "OpenItemSourceBatches"),
    ("idempotency", "CommandExecutions"),
    ("idempotency", "GroupSharedCommandTerminals"),
    ("inventory", "Commands"),
    ("logistics", "CustomerReturnAttempts"),
    ("logistics", "LoadingAttempts"),
    ("logistics", "TransferOutgoingDocumentAttempts"),
    ("manufacturing", "Commands"),
    ("product", "PriceListAttempts"),
    ("purchasing", "PurchaseAcceptanceShares"),
    ("purchasing", "PurchaseAttempts"),
    ("sales", "DraftAttempts"),
)
METADATA_ONLY_IDEMPOTENCY_KEY_TABLES = (("audit", "AuditEvents"),)
AUDIT_SOURCE_ROOT = "src/backend/Modules/Audit/"
AUDIT_LIVE_SOURCE_EXCLUDED_SUFFIXES = (".designer.cs", ".g.cs", ".generated.cs")
EXPECTED_AUDIT_LIVE_SOURCE_FILE_COUNT = 21
EXPECTED_AUDIT_LIVE_SOURCE_FINGERPRINT = (
    "9e9c9d3a1380426e69aa0f883b0b0292fb84ac23c6a27af6a929cb6e3b56fc6d"
)
EXPECTED_MESSAGE_DELIVERY_TABLES = (
    ("accounting", "Deliveries"),
    ("finance", "OfficialInvoiceImportWorkDeliveries"),
    ("inventory", "OutboxMessages"),
    ("work", "ObservationInbox"),
    ("work", "OutboxMessages"),
)
NON_MESSAGE_DELIVERY_TABLES = (
    ("sales", "DraftDeliveryScheduleDates"),
    ("sales", "DraftDeliverySchedules"),
    ("sales", "DraftDeliverySlices"),
)
DOCUMENT_PATH_CLASSIFICATIONS = (
    ("Documents:ObjectStore:RootPath", "RECOVERY_SET_CONTENT"),
    ("Documents:Scanner:Defender:QuarantinePath", "TRANSIENT_EXCLUDED"),
)
INVENTORY_INPUT_PATHS = (
    BACKEND_ROOT.as_posix(),
    *REPOSITORY_BUILD_CONTROL_PATHS,
    SCHEMA_PATH.as_posix(),
    GENERATOR_PATH.as_posix(),
)


class InventoryError(ValueError):
    """A fail-closed source inventory error that is safe to report."""


class SourceSnapshot(NamedTuple):
    repository_root: Path
    source_commit: str
    files: dict[str, bytes]


class SourceEvidence(NamedTuple):
    path: str
    required_tokens: tuple[str, ...]
    live_code: bool = False


class ModelTableMapping(NamedTuple):
    entity_type: str
    schema: str
    table: str
    property_names: tuple[str, ...]
    primary_key: tuple[str, ...]
    unique_indexes: tuple[tuple[str, ...], ...]


class CSharpTypeDeclaration(NamedTuple):
    name: str
    path: str
    base_types: tuple[str, ...]


class DurableStoreSpec(NamedTuple):
    identifier: str
    module: str
    kind: str
    db_context: str
    entity_type: str
    schema: str
    table: str
    contracts: tuple[str, ...]
    evidence: tuple[SourceEvidence, ...]


def _evidence(path: str, *tokens: str, live_code: bool = False) -> SourceEvidence:
    return SourceEvidence(path, tuple(tokens), live_code)


def _store(
    identifier: str,
    module: str,
    kind: str,
    db_context: str,
    entity_type: str,
    schema: str,
    table: str,
    mapping_path: str,
    migration_path: str,
    writer_path: str,
    writer_tokens: tuple[str, ...],
    *,
    mapping_tokens: tuple[str, ...] = (),
    contracts: tuple[str, ...] = (),
    extra_evidence: tuple[SourceEvidence, ...] = (),
    uses_schema_constant: bool = True,
) -> DurableStoreSpec:
    if uses_schema_constant:
        schema_tokens = (f'Schema = "{schema}"', f'ToTable("{table}", Schema')
    else:
        schema_tokens = (f'ToTable("{table}", "{schema}"',)
    evidence = (
        _evidence(
            mapping_path,
            f"Entity<{entity_type}>()",
            *schema_tokens,
            *mapping_tokens,
        ),
        _evidence(migration_path, f'name: "{table}"'),
        _evidence(writer_path, *writer_tokens, live_code=True),
        *extra_evidence,
    )
    return DurableStoreSpec(
        identifier,
        module,
        kind,
        db_context,
        entity_type,
        schema,
        table,
        contracts,
        evidence,
    )


ACCOUNTING_CONTEXT = (
    "src/backend/Modules/Accounting/"
    "GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingDbContext.cs"
)
ACCOUNTING_STORE = (
    "src/backend/Modules/Accounting/"
    "GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingStore.cs"
)
DOCUMENTS_CONTEXT = (
    "src/backend/Modules/Documents/"
    "GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentsDbContext.cs"
)
DOCUMENTS_STORE = (
    "src/backend/Modules/Documents/"
    "GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentsStore.cs"
)
FINANCE_CONTEXT = (
    "src/backend/Modules/Finance/"
    "GoymenERP.Modules.Finance.Infrastructure/Persistence/FinanceDbContext.cs"
)
IDEMPOTENCY_ROOT = (
    "src/backend/Modules/Idempotency/"
    "GoymenERP.Modules.Idempotency.Infrastructure/Persistence"
)
INVENTORY_CONTEXT = (
    "src/backend/Modules/Inventory/"
    "GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDbContext.cs"
)
LOGISTICS_CONTEXT = (
    "src/backend/Modules/Logistics/"
    "GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsDbContext.cs"
)
PURCHASING_CONTEXT = (
    "src/backend/Modules/Purchasing/"
    "GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingDbContext.cs"
)
WORK_CONTEXT = (
    "src/backend/Modules/Work/"
    "GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDbContext.cs"
)


DURABLE_STORE_SPECS = (
    _store(
        "accounting.command-replay", "Accounting", "idempotency", "AccountingDbContext",
        "AccountingCommandRow", "accounting", "Commands", ACCOUNTING_CONTEXT,
        "src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20260919140000_AddPeriodApproval.cs",
        ACCOUNTING_STORE, ("dbContext.Commands.Add(new AccountingCommandRow",),
        mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "accounting.delivery-inbox", "Accounting", "inbox", "AccountingDbContext",
        "DeliveryRow", "accounting", "Deliveries", ACCOUNTING_CONTEXT,
        "src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20260918180000_InitialAccounting.cs",
        ACCOUNTING_STORE, ("dbContext.Deliveries.Add(new DeliveryRow",),
        mapping_tokens=("Consumer", "MessageId"),
    ),
    _store(
        "documents.document-attempts", "Documents", "idempotency", "DocumentsDbContext",
        "DocumentAttemptRow", "documents", "DocumentAttempts", DOCUMENTS_CONTEXT,
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20260930120000_InitialDocuments.cs",
        DOCUMENTS_STORE, ("dbContext.Attempts.Add(new DocumentAttemptRow",),
        mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.command-replay", "Finance", "idempotency", "FinanceDbContext",
        "FinanceCommandRow", "finance", "Commands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260918200000_InitialFinanceDrafts.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ReceiptDraftService.cs",
        ("dbContext.Commands.Add(new FinanceCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.official-invoice-import-commands", "Finance", "idempotency", "FinanceDbContext",
        "FinanceOfficialInvoiceImportCommandRow", "finance", "OfficialInvoiceImportCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260930185113_AddOfficialInvoiceImportApprovalV1.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs",
        ("dbContext.OfficialInvoiceImportCommands.Add(row)",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.official-invoice-import-work-deliveries", "Finance", "delivery", "FinanceDbContext",
        "FinanceOfficialInvoiceImportWorkDeliveryRow", "finance", "OfficialInvoiceImportWorkDeliveries", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261005213000_AddOfficialInvoiceImportWorkDeliveryV1.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs",
        ("dbContext.OfficialInvoiceImportWorkDeliveries.Add(",),
        mapping_tokens=("ObservationOrdinal", "CommandReceiptId"),
        extra_evidence=(
            _evidence(
                "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportWorkObservationFactoryV1.cs",
                "FinanceOfficialInvoiceImportWorkDeliveryRow",
                "PayloadSha256",
            ),
        ),
    ),
    _store(
        "finance.open-item-source-batches", "Finance", "idempotency", "FinanceDbContext",
        "FinanceOpenItemSourceBatchRow", "finance", "OpenItemSourceBatches", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260924113000_AddFinanceAllocations.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpenItemSourceAdmissionService.cs",
        ("dbContext.OpenItemSourceBatches.Add(batchRow)",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "idempotency.group-shared-command-terminals", "Idempotency", "idempotency",
        "GroupSharedCommandDbContext", "GroupSharedCommandTerminalRecord", "idempotency",
        "GroupSharedCommandTerminals", f"{IDEMPOTENCY_ROOT}/GroupSharedCommandDbContext.cs",
        f"{IDEMPOTENCY_ROOT}/Migrations/20260912175124_InitialGroupSharedTerminals.cs",
        "src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Executions/SqlGroupSharedCommandTerminalStore.cs",
        ("database.InsertAsync(GroupSharedCommandTerminalRecord.FromTerminal",),
        mapping_tokens=("IdempotencyKeyHash",), uses_schema_constant=False,
    ),
    DurableStoreSpec(
        "idempotency.command-executions", "Idempotency", "idempotency", "CommandExecutionDbContext",
        "CommandExecution", "idempotency", "CommandExecutions", (),
        (
            _evidence(f"{IDEMPOTENCY_ROOT}/CommandExecutionDbContext.cs", "DbSet<CommandExecution> CommandExecutions", "ConfigureCommandExecution(excludeFromMigrations: false)"),
            _evidence(f"{IDEMPOTENCY_ROOT}/CommandExecutionModelBuilderExtensions.cs", "Entity<CommandExecution>()", "entity.ToTable(", '"CommandExecutions",', '"idempotency",', "IdempotencyKeyHash"),
            _evidence(f"{IDEMPOTENCY_ROOT}/Migrations/20260827175405_InitialIdempotency.cs", 'name: "CommandExecutions"'),
            _evidence(
                "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationService.cs",
                "dbContext.CommandExecutions.Add(CommandExecution.Complete(",
                live_code=True,
            ),
        ),
    ),
    _store(
        "inventory.command-replay", "Inventory", "idempotency", "InventoryDbContext",
        "InventoryCommandRow", "inventory", "Commands", INVENTORY_CONTEXT,
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260919140000_AddInventoryCommands.cs",
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/InventoryOpeningCommandService.cs",
        ("dbContext.Commands.Add(CommandRow(",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "inventory.outbox", "Inventory", "outbox", "InventoryDbContext", "InventoryOutboxRow",
        "inventory", "OutboxMessages", INVENTORY_CONTEXT,
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924001222_AddInventoryDefiniteShipmentRuntime.cs",
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs",
        ("context.OutboxMessages.Add(new InventoryOutboxRow", "EventType = InventoryDefiniteShipmentOutboxV1.EventType"),
        mapping_tokens=("AggregateId", "Sequence", "IsUnique()"),
        contracts=("InventoryCustomerReturnOutboxV1", "InventoryDefiniteShipmentOutboxV1"),
        extra_evidence=(
            _evidence(
                "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryCustomerReturnOutboxV1.cs",
                "public static class InventoryCustomerReturnOutboxV1",
                'EventType = "inventory.customer-return.recorded.v1"',
            ),
            _evidence(
                "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryDefiniteShipmentOutboxV1.cs",
                "public static class InventoryDefiniteShipmentOutboxV1",
                'EventType = "inventory.definite-shipment.recorded.v1"',
            ),
            _evidence(
                "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryCustomerReturnParticipant.cs",
                "context.OutboxMessages.Add(new InventoryOutboxRow",
                "EventType = InventoryCustomerReturnOutboxV1.EventType",
                live_code=True,
            ),
        ),
    ),
    _store(
        "logistics.customer-return-attempts", "Logistics", "idempotency", "LogisticsDbContext",
        "CustomerReturnAttemptRow", "logistics", "CustomerReturnAttempts", LOGISTICS_CONTEXT,
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261003230000_AddCustomerReturns.cs",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsCustomerReturnStore.cs",
        ("dbContext.CustomerReturnAttempts.Add(attempt)",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "logistics.loading-attempts", "Logistics", "idempotency", "LogisticsDbContext",
        "LoadingAttemptRow", "logistics", "LoadingAttempts", LOGISTICS_CONTEXT,
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20260919150000_InitialLogistics.cs",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs",
        ("dbContext.Attempts.Add(Attempt(",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "logistics.transfer-outgoing-document-attempts", "Logistics", "idempotency", "LogisticsDbContext",
        "TransferOutgoingDocumentAttemptRow", "logistics", "TransferOutgoingDocumentAttempts", LOGISTICS_CONTEXT,
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261015163000_AddTransferOutgoingDocuments.cs",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferOutgoingDocumentStore.cs",
        ("dbContext.TransferOutgoingDocumentAttempts.Add(Attempt(",),
        mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "manufacturing.command-replay", "Manufacturing", "idempotency", "ManufacturingDbContext",
        "CommandRow", "manufacturing", "Commands",
        "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingDbContext.cs",
        "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260918230000_AddManufacturingCommands.cs",
        "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.cs",
        ("dbContext.Commands.Add(new CommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "product.price-list-attempts", "Product", "idempotency", "ProductPriceListDbContext",
        "PriceListAttemptRow", "product", "PriceListAttempts",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDbContext.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20260930113416_AddProductPriceLists.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListStore.cs",
        ("dbContext.Attempts.Add(row)",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "purchasing.purchase-acceptance-shares", "Purchasing", "idempotency", "PurchasingDbContext",
        "PurchaseAcceptanceRow", "purchasing", "PurchaseAcceptanceShares", PURCHASING_CONTEXT,
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20260924102017_AddPurchaseAcceptanceParticipant.cs",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseAcceptanceParticipant.cs",
        ("_dbContext.Acceptances.Add(new PurchaseAcceptanceRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "purchasing.purchase-attempts", "Purchasing", "idempotency", "PurchasingDbContext",
        "PurchaseAttemptRow", "purchasing", "PurchaseAttempts", PURCHASING_CONTEXT,
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20260919170000_InitialPurchasing.cs",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs",
        ("dbContext.Attempts.Add(Attempt(",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "sales.draft-attempts", "Sales", "idempotency", "SalesDbContext", "DraftAttemptRow",
        "sales", "DraftAttempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260918210000_InitialSales.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs",
        ("dbContext.DraftAttempts.Add(new DraftAttemptRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "work.observation-inbox", "Work", "inbox", "WorkDbContext", "WorkObservationInboxRow",
        "work", "ObservationInbox", WORK_CONTEXT,
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261005190000_InitialWorkDesiredStateStorage.cs",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
        ("dbContext.ObservationInbox.Add(inbox)",), mapping_tokens=("SourceKeyDigest", "ObservationOrdinal"),
    ),
    _store(
        "work.outbox", "Work", "outbox", "WorkDbContext", "WorkOutboxRow", "work",
        "OutboxMessages", WORK_CONTEXT,
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261005190000_InitialWorkDesiredStateStorage.cs",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
        ("dbContext.OutboxMessages.Add(outbox)",), mapping_tokens=("SourceKeyDigest", "ObservationOrdinal"),
    ),
)


# A lexical write token is evidence only inside the operative method named here.  The
# binding is intentionally explicit: moving a write into an unused helper or a deferred
# lambda must invalidate the inventory instead of silently preserving its source hash.
WRITER_METHOD_SCOPES = {
    ("accounting.command-replay", ACCOUNTING_STORE): "AddCommand",
    ("accounting.delivery-inbox", ACCOUNTING_STORE): "AddDelivery",
    ("documents.document-attempts", DOCUMENTS_STORE): "StoreUploadAsync",
    (
        "finance.command-replay",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ReceiptDraftService.cs",
    ): "DraftSaveAsync",
    (
        "finance.official-invoice-import-commands",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs",
    ): "AddCommand",
    (
        "finance.official-invoice-import-work-deliveries",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs",
    ): "UploadAsync",
    (
        "finance.open-item-source-batches",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpenItemSourceAdmissionService.cs",
    ): "AdmitAsync",
    (
        "idempotency.group-shared-command-terminals",
        "src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Executions/SqlGroupSharedCommandTerminalStore.cs",
    ): "FlushAsync",
    (
        "idempotency.command-executions",
        "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationService.cs",
    ): "CompleteCommandAsync",
    (
        "inventory.command-replay",
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/InventoryOpeningCommandService.cs",
    ): "RecordAsync",
    (
        "inventory.outbox",
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs",
    ): "StageAsync",
    (
        "inventory.outbox",
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryCustomerReturnParticipant.cs",
    ): "StageAsync",
    (
        "logistics.customer-return-attempts",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsCustomerReturnStore.cs",
    ): "RecordRefusalAsync",
    (
        "logistics.loading-attempts",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs",
    ): "StoreInstructionAsync",
    (
        "logistics.transfer-outgoing-document-attempts",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferOutgoingDocumentStore.cs",
    ): "StoreDraftAsync",
    (
        "manufacturing.command-replay",
        "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.cs",
    ): "AddCommand",
    (
        "product.price-list-attempts",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListStore.cs",
    ): "RecordRefusalAsync",
    (
        "purchasing.purchase-acceptance-shares",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseAcceptanceParticipant.cs",
    ): "StageAsync",
    (
        "purchasing.purchase-attempts",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs",
    ): "StoreRequestAsync",
    (
        "sales.draft-attempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs",
    ): "StoreDraftAsync",
    (
        "work.observation-inbox",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
    ): "ReconcileAsync",
    (
        "work.outbox",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
    ): "ReconcileAsync",
}


# These declaration-and-body fingerprints bind recovery evidence to the reviewed live
# methods.  Any semantic or execution-shape change must be deliberately re-inventoried.
REVIEWED_OPERATIVE_METHOD_FINGERPRINTS = {
    (ACCOUNTING_STORE, "AddCommand"): "db718449f8e13e277eb0aa197b20d7210130ab1c0d2091dd6011e43e781e47a1",
    (ACCOUNTING_STORE, "AddDelivery"): "b24889ce6551605bb62723f3bce1a2baa59c3680894167c71b3fd51ce0be1cb0",
    (DOCUMENT_MODULE_PATH, "AddDocumentsModule"): "178f03a58d3a9ef775bb91f777db991fd935e3d776d7d91112cb5179e4f9f57e",
    (DOCUMENT_MODULE_PATH, "RegisterProductionStorageAndScanner"): "bfea0db0f27c66dcdab7d411b5e5e6d2b986a25d157d0693fc70cbd3d5d0b19b",
    (DOCUMENTS_STORE, "StoreUploadAsync"): "c39dd3771fb6c7eae12d913f53e16e124860fccbf134e0acb3a1776b000fe95a",
    (DOCUMENT_SCANNER_PATH, "ScanAsync"): "2d21059fce81195b63a8fd1f7a64787a787c0146283254b41676564a586fa60f",
    (DOCUMENT_SCANNER_PATH, "TryDelete"): "50e4d401a2f79fc83b981e1edd48e77af65a84732e0024156ea10f366a18e31e",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/FileSystemDocumentObjectStore.cs",
        "FileSystemDocumentObjectStore",
    ): "be0aa7ce2a4ee8014f432dd5f489a75a490d8d9d8567feb5887196adc68b94b4",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/FileSystemDocumentObjectStore.cs",
        "PutAsync",
    ): "a530211788962fb851e8b9287f757967faf22e8c7c74065d928ebd898d8079dc",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/FileSystemDocumentObjectStore.cs",
        "ReadAsync",
    ): "ffba8afad399b8b8d2b0a7e8dac359d271232a72fcdae934cda95960e80c3ab1",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/FileSystemDocumentObjectStore.cs",
        "Resolve",
    ): "623ef7bd31856f426f705a971309c53ff1bef89c07f4ef24a803a58ae0bab863",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs",
        "Open",
    ): "a7b249638756cbf65df81a59124a0655c1ef765130344aad4cad41742c229bb4",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs",
        "PutAsync",
    ): "2fc38d66c026faf6bce8e0812178d111437e121d53f9a3d37553fc2396c9fe08",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs",
        "ReadAsync",
    ): "5857008b7adc3e7259e36c987071d94c8928ca6b64d9835b6152f0b6783b095c",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs",
        "Resolve",
    ): "876d414f61c56be7216a1c034243d43e144c6660d809156fcc913279ce515d10",
    (
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs",
        "ServerDirectoryDocumentObjectStore",
    ): "0a1caad7c78f7f3963a2c24ad5a74f31b7fef7268ab3ce609e823b6097792656",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs",
        "AddCommand",
    ): "e3f6861c99214dff75b488a5484ebef19042d5f5814d2095c05a6719f5e2e333",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs",
        "UploadAsync",
    ): "f401c2b93e88f45b8146402ac33cf7b96ca81790e01db1c635320fc2f02a5fa1",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpenItemSourceAdmissionService.cs",
        "AdmitAsync",
    ): "e6434a2096ab345532fbfc7869f76c9f4338bf0718dc7984a7a7f673f939ade2",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ReceiptDraftService.cs",
        "DraftSaveAsync",
    ): "311c64245720060345c07e28e212ed66443c9b8517722e75e45b495e232e3f9a",
    (
        "src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Executions/SqlGroupSharedCommandTerminalStore.cs",
        "FlushAsync",
    ): "2f9de9abb7ba734d27f5e396fe13d21527d5f7917744151497ffe68fbafb2c70",
    (
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/InventoryOpeningCommandService.cs",
        "RecordAsync",
    ): "74827fe2fdcc02391bc5571323f07031d9fb4b2de37cd71eea746fd884c32b61",
    (
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryCustomerReturnParticipant.cs",
        "StageAsync",
    ): "779af299ec8352d1ab17e8eb0df4598d85754af076eaf947e812e3031f7b10b4",
    (
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs",
        "StageAsync",
    ): "e330a9c77a13f6ed44c5d5dc4c7b848f798cfec725f6292202ec4c138e1bfa22",
    (
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsCustomerReturnStore.cs",
        "RecordRefusalAsync",
    ): "502fcbf1b14fca92c1e4d34bad0e96121a27a120c963499ceb2b9b1b83093d4e",
    (
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs",
        "StoreInstructionAsync",
    ): "b20047f6fd00f51dade9e63d521876ae53e58786c428f2634e631be438f64c5b",
    (
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferOutgoingDocumentStore.cs",
        "StoreDraftAsync",
    ): "b25fe77df6b48a6c508d8247c26a920ad8af3fbc22a4155efa7428533a4eeb0b",
    (
        "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.cs",
        "AddCommand",
    ): "a916e16fe247ded30e0b038e9489e897bfa54e2eeebb26a8a524f13a2e5e4683",
    (
        "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationService.cs",
        "CompleteCommandAsync",
    ): "3f1923b73f92016d3776500605a0414c117dda2fbfc0113f58a9c3005cab7fa9",
    (
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListStore.cs",
        "RecordRefusalAsync",
    ): "e7244c5871505ce3c02b5b4d0cfec9a476dadcdcd94e0870e408b741b5b3b117",
    (
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseAcceptanceParticipant.cs",
        "StageAsync",
    ): "8fee79232dec4e0ddbb84f3693d11bcad7d8e79538a3d023ebe9f4429b377f6b",
    (
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs",
        "StoreRequestAsync",
    ): "4633eed9c0696cfaad609f2e4b7f043d33831078ca3593c536ce234bbddc37b8",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs",
        "StoreDraftAsync",
    ): "6706dc3ddcb2dd03fbfd7ac47fa64b3a8db2964451e7741bf8ef722a4c769a68",
    (
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
        "ReconcileAsync",
    ): "0c760ebfe36fce642faa069b72637e699f16e1d14a04f0d154f43357be542657",
}


# The full blobs retain reachability evidence around the pinned methods (for example,
# private writer call sites) and bind every emitted source record to reviewed bytes.
REVIEWED_LIVE_SOURCE_SHA256 = {
    ACCOUNTING_STORE: "9b6e088b496de1fc3c9e912e2d83397ef341e9eed6f54b79dfa1eb1c6deb8041",
    DOCUMENT_MODULE_PATH: "8e6108de29ca7aae5133131abf8deb981d097bc33ea7964403652f04f0ebc610",
    DOCUMENTS_STORE: "076a0c5b55a961716d99f2d407bf0ae7f1d48c835de7b82b9cf64ce3a3c822ea",
    DOCUMENT_SCANNER_PATH: "39686c28f47ddb2f65085e7587241141426641b069ab8079ac1a93542d144189",
    "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/FileSystemDocumentObjectStore.cs": "8c7a2eed1888d36d0b61c60b1296d904adaf0338c34f361236befc8e1c1d6274",
    "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs": "0f79216993cdbbc20d7c92d830ad1c08f3ff2ab5b0ab9084aaae03f765784c1a",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs": "573d1b13dafb963b6521a099600130a759711d016f22c62529c3da0b2227a1af",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpenItemSourceAdmissionService.cs": "6811c3396e99bf54f0a686c11b1f6436df8df46bf004eacdc4e29e421b7b5250",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ReceiptDraftService.cs": "589f03fddc02ed44c23149eac8358f69805e00a7a28cd867c98eecc9e469c88e",
    "src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Executions/SqlGroupSharedCommandTerminalStore.cs": "5ecf927a5505ff79b5d2f3d79e436dc674d1b98aa05db28c8ae6e6669ac9b301",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/InventoryOpeningCommandService.cs": "fd384d4cdc34ec642db71049b1dbd0180f2d317f38acfd3a6758d943d571bebb",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryCustomerReturnParticipant.cs": "241e1e850db26e13c0b480d3c1ebb42f7b7ac031041c342ee00968065c1b085d",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs": "9aec0b823987c573ffd9d94e55192d5c80d49e29ef6ac713c02e07583ee00df7",
    "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsCustomerReturnStore.cs": "06808d6fe56293d16cf04d41fdff20505dcc0ed1862471ceae600e48a30c91df",
    "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs": "2e19c8adc7f83d82a3685adb47572f00681c4e2434e00567c6aa379c46df64b9",
    "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferOutgoingDocumentStore.cs": "9dd1ad58f60b05d3213c8ca8c08f7678e91d99efde65b4412486cebc08e89cd3",
    "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.cs": "39b3481ad59125465d1b94d3bb2219372e907e5a5fe1128d66eb1bac8bce908f",
    "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationService.cs": "c8225e56247f665a1aed783aca150d4f8d8a06ff33fa22eebf0b7812cc6c8389",
    "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListStore.cs": "ea3481d674648b893796c93b15dab32e0a7a49b7c9ba0a1ef686f2d8a6afac2d",
    "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseAcceptanceParticipant.cs": "c1932860818df6628368bee24a17a3e48d892c6f513313d953e94adf0e39358d",
    "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs": "72c1ab9720210ec278472130d7600650f941fe300a2e7bc2fa182d85b8acaef3",
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs": "dcd91635cea0a3eaf85e19590b5168e6b0da20a647576ba3ce98b130803a9d69",
    "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs": "c8b78e13e5a6b5d2075730346a718dbf48672ca47356de212271240a7f76b472",
}


SUPPORTED_SCHEMA_KEYWORDS = {
    "$schema",
    "$id",
    "title",
    "description",
    "type",
    "additionalProperties",
    "required",
    "properties",
    "const",
    "enum",
    "pattern",
    "minLength",
    "maxLength",
    "minimum",
    "minItems",
    "maxItems",
    "uniqueItems",
    "items",
}


def _require_repo_relative_path(relative_path: str, *, backend_only: bool = True) -> None:
    if (
        not relative_path
        or "\\" in relative_path
        or ":" in relative_path
        or "\x00" in relative_path
        or relative_path.startswith("/")
        or relative_path.endswith("/")
    ):
        raise InventoryError(f"Repository path is not canonical: {relative_path!r}.")
    segments = relative_path.split("/")
    if any(segment in {"", ".", ".."} for segment in segments):
        raise InventoryError(f"Repository path is not segment-safe: {relative_path!r}.")
    pure_path = PurePosixPath(relative_path)
    if pure_path.is_absolute() or pure_path.as_posix() != relative_path:
        raise InventoryError(f"Repository path is not canonical: {relative_path!r}.")
    if backend_only and not relative_path.startswith(f"{BACKEND_ROOT.as_posix()}/"):
        raise InventoryError(f"Repository source path is outside src/backend: {relative_path!r}.")


def _read_source(snapshot: SourceSnapshot, relative_path: str) -> str:
    _require_repo_relative_path(relative_path, backend_only=False)
    content = snapshot.files.get(relative_path)
    if content is None:
        raise InventoryError(f"Required source blob is missing: {relative_path}.")
    try:
        return content.decode("utf-8")
    except UnicodeError as exc:
        raise InventoryError(f"Required source blob is not readable UTF-8: {relative_path}.") from exc


def _source_file_record(snapshot: SourceSnapshot, relative_path: str) -> dict[str, str]:
    _require_repo_relative_path(relative_path)
    content = snapshot.files.get(relative_path)
    if content is None:
        raise InventoryError(f"Required source blob is missing: {relative_path}.")
    return {"path": relative_path, "sha256": hashlib.sha256(content).hexdigest()}


def _require_reviewed_live_source(snapshot: SourceSnapshot, relative_path: str) -> None:
    expected = REVIEWED_LIVE_SOURCE_SHA256.get(relative_path)
    if expected is None:
        raise InventoryError(f"Live recovery evidence has no reviewed blob hash: {relative_path}.")
    record = _source_file_record(snapshot, relative_path)
    if record["sha256"] != expected:
        raise InventoryError(f"Live recovery evidence blob drifted: {relative_path}.")


def _csharp_string_end(content: str, quote_index: int) -> int:
    """Return the first index after a C# string, including interpolated expressions."""

    quote_count = 1
    while quote_index + quote_count < len(content) and content[quote_index + quote_count] == '"':
        quote_count += 1
    if quote_count >= 3:
        delimiter = '"' * quote_count
        end = content.find(delimiter, quote_index + quote_count)
        if end < 0:
            raise InventoryError("C# source contains an unterminated raw string.")
        return end + quote_count

    prefix = content[max(0, quote_index - 2) : quote_index]
    interpolated = prefix.endswith("$") or prefix in {"$@", "@$"}
    verbatim = prefix.endswith("@") or prefix == "@$"
    index = quote_index + 1
    interpolation_depth = 0
    while index < len(content):
        if interpolation_depth == 0:
            if verbatim and content.startswith('""', index):
                index += 2
                continue
            if content[index] == '"':
                return index + 1
            if not verbatim and content[index] == "\\" and index + 1 < len(content):
                index += 2
                continue
            if interpolated and content[index] == "{":
                if content.startswith("{{", index):
                    index += 2
                    continue
                interpolation_depth = 1
            elif interpolated and content.startswith("}}", index):
                index += 2
                continue
            index += 1
            continue

        if content.startswith("//", index):
            newline = content.find("\n", index + 2)
            index = len(content) if newline < 0 else newline
            continue
        if content.startswith("/*", index):
            end = content.find("*/", index + 2)
            if end < 0:
                raise InventoryError("C# source contains an unterminated block comment.")
            index = end + 2
            continue
        if content[index] == '"':
            index = _csharp_string_end(content, index)
            continue
        if content[index] == "'":
            index += 1
            while index < len(content):
                if content[index] == "\\" and index + 1 < len(content):
                    index += 2
                    continue
                if content[index] == "'":
                    index += 1
                    break
                index += 1
            continue
        if content[index] == "{":
            interpolation_depth += 1
        elif content[index] == "}":
            interpolation_depth -= 1
        index += 1
    raise InventoryError("C# source contains an unterminated interpolated string.")


def _strip_csharp_comments(content: str) -> str:
    """Remove C# comments while preserving strings and line structure."""

    result: list[str] = []
    index = 0
    length = len(content)
    while index < length:
        if content.startswith("//", index):
            newline = content.find("\n", index + 2)
            if newline < 0:
                result.append(" " * (length - index))
                break
            result.append(" " * (newline - index))
            index = newline
            continue
        if content.startswith("/*", index):
            end = content.find("*/", index + 2)
            if end < 0:
                raise InventoryError("C# source contains an unterminated block comment.")
            comment = content[index : end + 2]
            result.append("".join("\n" if character == "\n" else " " for character in comment))
            index = end + 2
            continue

        character = content[index]
        if character == '"':
            end = _csharp_string_end(content, index)
            result.append(content[index:end])
            index = end
            continue
        if character == "'":
            start = index
            index += 1
            while index < length:
                if content[index] == "\\" and index + 1 < length:
                    index += 2
                    continue
                if content[index] == "'":
                    index += 1
                    break
                index += 1
            result.append(content[start:index])
            continue

        result.append(character)
        index += 1
    return "".join(result)


def _read_csharp(snapshot: SourceSnapshot, relative_path: str) -> str:
    return _strip_csharp_comments(_read_source(snapshot, relative_path))


def _blank_csharp_region(content: str) -> str:
    return "".join("\n" if character == "\n" else " " for character in content)


def _mask_csharp_strings(content: str) -> str:
    result: list[str] = []
    index = 0
    while index < len(content):
        character = content[index]
        if character not in {'"', "'"}:
            result.append(character)
            index += 1
            continue

        start = index
        delimiter = character
        if delimiter == '"':
            index = _csharp_string_end(content, index)
            result.append(_blank_csharp_region(content[start:index]))
            continue

        verbatim = False
        index += 1
        while index < len(content):
            if verbatim and content.startswith('""', index):
                index += 2
                continue
            if content[index] == delimiter:
                index += 1
                break
            if not verbatim and content[index] == "\\" and index + 1 < len(content):
                index += 2
            else:
                index += 1
        else:
            raise InventoryError("C# source contains an unterminated string or character literal.")
        result.append(_blank_csharp_region(content[start:index]))
    return "".join(result)


def _mask_conditional_preprocessor_regions(content: str) -> str:
    """Keep literal-active branches and reject build-dependent source evidence."""

    stack: list[dict[str, bool]] = []
    active = True
    result: list[str] = []
    directive_pattern = re.compile(r"^\s*#\s*(if|elif|else|endif)\b(.*)$", re.IGNORECASE)
    for line in content.splitlines(keepends=True):
        directive = directive_pattern.match(line)
        if directive is not None:
            command = directive.group(1).casefold()
            expression = directive.group(2).strip().casefold()
            if command == "if":
                value = {"true": True, "false": False}.get(expression)
                if value is None:
                    raise InventoryError(
                        "C# source contains a non-literal conditional compilation branch."
                    )
                frame = {
                    "parent": active,
                    "known": value is not None,
                    "taken": value is True,
                    "active": active and value is True,
                }
                stack.append(frame)
                active = frame["active"]
            elif not stack:
                raise InventoryError("C# source contains an unmatched conditional directive.")
            elif command == "elif":
                frame = stack[-1]
                if not frame["known"] or frame["taken"]:
                    frame["active"] = False
                else:
                    value = {"true": True, "false": False}.get(expression)
                    if value is None:
                        raise InventoryError(
                            "C# source contains a non-literal conditional compilation branch."
                        )
                    else:
                        frame["taken"] = value
                        frame["active"] = frame["parent"] and value
                active = frame["active"]
            elif command == "else":
                frame = stack[-1]
                frame["active"] = frame["parent"] and frame["known"] and not frame["taken"]
                frame["taken"] = True
                active = frame["active"]
            elif command == "endif":
                frame = stack.pop()
                active = frame["parent"]
            result.append(_blank_csharp_region(line))
            continue
        if not active:
            result.append(_blank_csharp_region(line))
        else:
            result.append(line)
    if stack:
        raise InventoryError("C# source contains an unterminated conditional directive.")
    return "".join(result)


def _matching_brace(content: str, opening: int) -> int:
    depth = 0
    for index in range(opening, len(content)):
        if content[index] == "{":
            depth += 1
        elif content[index] == "}":
            depth -= 1
            if depth == 0:
                return index
    raise InventoryError("C# source contains an unterminated literal-false block.")


def _mask_literal_false_if_blocks(content: str) -> str:
    block_pattern = re.compile(r"\bif\s*\(\s*false\s*\)\s*\{", re.IGNORECASE)
    while (match := block_pattern.search(content)) is not None:
        opening = content.find("{", match.start(), match.end())
        closing = _matching_brace(content, opening)
        content = (
            content[: match.start()]
            + _blank_csharp_region(content[match.start() : closing + 1])
            + content[closing + 1 :]
        )
    statement_pattern = re.compile(
        r"\bif\s*\(\s*false\s*\)\s*(?!\{)[^;{}]*;",
        re.IGNORECASE | re.DOTALL,
    )
    return statement_pattern.sub(lambda match: _blank_csharp_region(match.group(0)), content)


def _live_csharp_code(content: str) -> str:
    """Mask literals and statically disabled regions before live-code anchor checks."""

    without_literals = _mask_csharp_strings(content)
    if without_literals.startswith("\ufeff"):
        without_literals = " " + without_literals[1:]
    if re.search(r"\\(?:u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})", without_literals) is not None:
        raise InventoryError("C# source contains an escaped identifier or code token.")
    identifier_code = without_literals
    if any(ord(character) > 127 for character in identifier_code):
        raise InventoryError("C# source contains a non-ASCII identifier or code token.")
    if re.search(r"\bextern\s+alias\b", identifier_code) is not None:
        raise InventoryError("C# source contains an unsupported extern alias.")
    alias_qualifiers = re.findall(r"(?<![A-Za-z0-9_@])(@?[A-Za-z_][A-Za-z0-9_]*)::", identifier_code)
    if any(qualifier != "global" for qualifier in alias_qualifiers):
        raise InventoryError("C# source contains an unsupported alias qualifier.")
    without_disabled_directives = _mask_conditional_preprocessor_regions(without_literals)
    return _mask_literal_false_if_blocks(without_disabled_directives)


def _live_csharp_code_with_literals(content: str) -> str:
    """Mask comments and statically dead code while retaining literal contents.

    Recovery metadata includes SQL and EF.Property member names inside string
    literals.  The structural scanner masks those strings, so this companion
    representation applies the same literal-false ranges to the comment-free
    source without losing their reviewed contents.
    """

    active = _mask_conditional_preprocessor_regions(_strip_csharp_comments(content))
    structural = _mask_csharp_strings(active)
    result = active

    block_pattern = re.compile(r"\bif\s*\(\s*false\s*\)\s*\{", re.IGNORECASE)
    while (match := block_pattern.search(structural)) is not None:
        opening = structural.find("{", match.start(), match.end())
        closing = _matching_brace(structural, opening)
        result = (
            result[: match.start()]
            + _blank_csharp_region(result[match.start() : closing + 1])
            + result[closing + 1 :]
        )
        structural = (
            structural[: match.start()]
            + _blank_csharp_region(structural[match.start() : closing + 1])
            + structural[closing + 1 :]
        )

    statement_pattern = re.compile(
        r"\bif\s*\(\s*false\s*\)\s*(?!\{)[^;{}]*;",
        re.IGNORECASE | re.DOTALL,
    )
    for match in reversed(tuple(statement_pattern.finditer(structural))):
        result = (
            result[: match.start()]
            + _blank_csharp_region(result[match.start() : match.end()])
            + result[match.end() :]
        )
    return result


def _mask_nested_callable_bodies(
    content: str,
    relative_path: str,
    method_name: str,
) -> str:
    """Remove deferred callables and reject local-function ambiguity."""

    local_function = re.compile(
        r"(?m)(?:^|(?<=[;{}]))\s*"
        r"(?:\[[^\]]+\]\s*)*"
        r"(?!(?:if|else|for|foreach|while|switch|using|lock|catch|finally|try|do|"
        r"return|throw|await|yield|checked|unchecked|fixed)\b)"
        r"(?:(?:static|async|unsafe|extern)\s+)*"
        r"(?:ref\s+(?:readonly\s+)?)?"
        r"(?:(?:global::)?@?[^\W\d][\w@.:*<>,?\[\] \t]*|\([^;{}=\r\n]+\))\s+"
        r"(?P<name>@?[^\W\d]\w*)\s*"
        r"(?:<[^;{}=]+>)?\s*\([^;{}]*\)\s*"
        r"(?:where\s+[^;{}=]+\s*)?(?:\{|=>)",
        re.DOTALL,
    )
    match = local_function.search(content)
    if match is not None:
        raise InventoryError(
            "Nested local function makes operative-method evidence ambiguous: "
            f"{relative_path}:{method_name}:{match.group('name')}."
        )

    block_pattern = re.compile(r"=>\s*\{")
    while (match := block_pattern.search(content)) is not None:
        opening = content.find("{", match.start(), match.end())
        closing = _matching_brace(content, opening)
        content = (
            content[: match.start()]
            + _blank_csharp_region(content[match.start() : closing + 1])
            + content[closing + 1 :]
        )
    expression_pattern = re.compile(r"=>")
    while (match := expression_pattern.search(content)) is not None:
        cursor = match.end()
        while cursor < len(content) and content[cursor].isspace():
            cursor += 1
        if cursor < len(content) and content[cursor] == "{":
            raise InventoryError("Nested callable masking left an unexpected block lambda.")
        depth = 0
        while cursor < len(content):
            character = content[cursor]
            if character in "([{":
                depth += 1
            elif character in ")]}":
                if depth > 0:
                    depth -= 1
            elif character == ";" and depth == 0:
                content = (
                    content[: match.start()]
                    + _blank_csharp_region(content[match.start() : cursor + 1])
                    + content[cursor + 1 :]
                )
                break
            cursor += 1
        else:
            raise InventoryError("C# source contains an unterminated expression lambda.")

    delegate_pattern = re.compile(r"\bdelegate\s*(?:\([^;{}]*\))?\s*\{")
    while (match := delegate_pattern.search(content)) is not None:
        opening = content.find("{", match.start(), match.end())
        closing = _matching_brace(content, opening)
        content = (
            content[: match.start()]
            + _blank_csharp_region(content[match.start() : closing + 1])
            + content[closing + 1 :]
        )
    return content


def _csharp_method_body(
    content: str,
    method_name: str,
    relative_path: str,
    *,
    mask_nested: bool = True,
) -> str:
    """Return one live operative method body, rejecting missing or overloaded declarations."""

    live = _live_csharp_code(content)
    declaration = re.compile(
        rf"(?m)^[ \t]*(?:public|private|protected|internal)\b[^;{{}}=]*?"
        rf"\b{re.escape(method_name)}\s*(?:<[^;{{}}]+>)?\s*\([^;{{}}]*\)\s*"
        rf"(?:where\s+[^{{}}=]+\s*)?(?P<body>\{{|=>)",
        re.DOTALL,
    )
    matches = list(declaration.finditer(live))
    if len(matches) != 1:
        raise InventoryError(
            f"Required C# method is missing or ambiguous: {relative_path}:{method_name}."
        )
    match = matches[0]
    marker = match.group("body")
    marker_start = match.start("body")
    if marker == "{":
        closing = _matching_brace(live, marker_start)
        body = live[marker_start + 1 : closing]
    else:
        semicolon = live.find(";", match.end("body"))
        if semicolon < 0:
            raise InventoryError(f"Required C# method is unterminated: {relative_path}:{method_name}.")
        body = live[match.end("body") : semicolon]
    if not mask_nested:
        return body
    return _mask_nested_callable_bodies(body, relative_path, method_name)


def _csharp_method_fingerprint(content: str, method_name: str, relative_path: str) -> str:
    """Fingerprint the exact operative declaration and body from live source."""

    live = _live_csharp_code(content)
    declaration = re.compile(
        rf"(?m)^[ \t]*(?:public|private|protected|internal)\b[^;{{}}=]*?"
        rf"\b{re.escape(method_name)}\s*(?:<[^;{{}}]+>)?\s*\([^;{{}}]*\)\s*"
        rf"(?:where\s+[^{{}}=]+\s*)?(?P<body>\{{|=>)",
        re.DOTALL,
    )
    matches = list(declaration.finditer(live))
    if len(matches) != 1:
        raise InventoryError(
            f"Required C# method is missing or ambiguous: {relative_path}:{method_name}."
        )
    match = matches[0]
    prefix = live[: match.start()].rstrip()
    if prefix.endswith("]"):
        raise InventoryError(
            f"Operative C# method attributes are not part of the reviewed shape: "
            f"{relative_path}:{method_name}."
        )
    marker = match.group("body")
    marker_start = match.start("body")
    if marker == "{":
        end = _matching_brace(live, marker_start) + 1
    else:
        semicolon = live.find(";", match.end("body"))
        if semicolon < 0:
            raise InventoryError(
                f"Required C# method is unterminated: {relative_path}:{method_name}."
            )
        end = semicolon + 1
    reviewed_region = content[match.start() : end].encode("utf-8")
    return hashlib.sha256(reviewed_region).hexdigest()


def _require_reviewed_method_fingerprint(
    content: str,
    relative_path: str,
    method_name: str,
) -> None:
    expected = REVIEWED_OPERATIVE_METHOD_FINGERPRINTS.get((relative_path, method_name))
    if expected is None:
        raise InventoryError(
            f"Operative C# method has no reviewed fingerprint: {relative_path}:{method_name}."
        )
    actual = _csharp_method_fingerprint(content, method_name, relative_path)
    if actual != expected:
        raise InventoryError(
            f"Operative C# method fingerprint drifted: {relative_path}:{method_name}."
        )


def _require_direct_statement_anchor(
    body: str,
    relative_path: str,
    method_name: str,
    token: str,
) -> None:
    """Require the writer call to be a directly executed statement in the method."""

    positions: list[int] = []
    start = 0
    while (index := body.find(token, start)) >= 0:
        positions.append(index)
        start = index + 1
    if not positions:
        raise InventoryError(
            f"Required operative writer anchor is missing: "
            f"{relative_path}:{method_name}."
        )

    for position in positions:
        cursor = position - 1
        while cursor >= 0 and body[cursor].isspace():
            cursor -= 1
        word_end = cursor + 1
        while cursor >= 0 and (body[cursor].isalnum() or body[cursor] == "_"):
            cursor -= 1
        if body[cursor + 1 : word_end] == "await":
            while cursor >= 0 and body[cursor].isspace():
                cursor -= 1
        if cursor >= 0 and body[cursor] not in ";{}":
            raise InventoryError(
                f"Required operative writer is not a direct statement: "
                f"{relative_path}:{method_name}."
            )


def _require_method_tokens(
    content: str,
    relative_path: str,
    method_name: str,
    tokens: Sequence[str],
) -> None:
    raw_body = _csharp_method_body(
        content,
        method_name,
        relative_path,
        mask_nested=False,
    )
    body = _mask_nested_callable_bodies(raw_body, relative_path, method_name)
    for index, token in enumerate(tokens):
        if token not in body:
            raise InventoryError(
                f"Required operative-method anchor drifted: {relative_path}:{method_name}."
            )
        if index == 0:
            _require_direct_statement_anchor(raw_body, relative_path, method_name, token)


def _csharp_assignment_count(content: str, identifier: str) -> int:
    identifier_pattern = rf"(?<![A-Za-z0-9_])@?{re.escape(identifier)}(?![A-Za-z0-9_])"
    assignments = 0
    for index, character in enumerate(content):
        if character != "=":
            continue
        previous = content[index - 1] if index > 0 else ""
        following = content[index + 1] if index + 1 < len(content) else ""
        if previous in "=!<>" or following in "=>":
            continue
        boundary = max(
            content.rfind(";", 0, index),
            content.rfind("{", 0, index),
            content.rfind("}", 0, index),
        )
        left_hand_side = content[boundary + 1 : index]
        if re.search(identifier_pattern, left_hand_side) is not None:
            assignments += 1
    by_reference = re.compile(
        rf"\b(?:ref|out)\s+"
        rf"(?:(?:var|{CSHARP_QUALIFIED_IDENTIFIER_PATTERN})\s+)?"
        rf"@?{re.escape(identifier)}(?![\w])"
    )
    return assignments + len(by_reference.findall(content))


def _csharp_identifier_count(content: str, identifier: str) -> int:
    return len(
        re.findall(
            rf"(?<![A-Za-z0-9_])@?{re.escape(identifier)}(?![A-Za-z0-9_])",
            content,
        )
    )


def _csharp_braced_body(content: str, declaration_pattern: str, relative_path: str) -> str:
    declaration = re.search(declaration_pattern, content)
    if declaration is None:
        raise InventoryError(f"Required C# declaration drifted: {relative_path}.")
    opening = content.find("{", declaration.end())
    if opening < 0:
        raise InventoryError(f"Required C# block drifted: {relative_path}.")

    depth = 0
    index = opening
    while index < len(content):
        character = content[index]
        if character in {'"', "'"}:
            delimiter = character
            verbatim = delimiter == '"' and index > 0 and content[index - 1] == "@"
            quote_count = 1
            if delimiter == '"':
                while index + quote_count < len(content) and content[index + quote_count] == '"':
                    quote_count += 1
            if delimiter == '"' and quote_count >= 3:
                raw_delimiter = '"' * quote_count
                end = content.find(raw_delimiter, index + quote_count)
                if end < 0:
                    raise InventoryError(f"Required C# block contains an unterminated string: {relative_path}.")
                index = end + quote_count
                continue
            index += 1
            while index < len(content):
                if verbatim and content.startswith('""', index):
                    index += 2
                    continue
                if content[index] == delimiter:
                    index += 1
                    break
                if not verbatim and content[index] == "\\" and index + 1 < len(content):
                    index += 2
                else:
                    index += 1
            continue
        if character == "{":
            depth += 1
        elif character == "}":
            depth -= 1
            if depth == 0:
                return content[opening + 1 : index]
        index += 1
    raise InventoryError(f"Required C# block is unterminated: {relative_path}.")


def _declaration_set_fingerprint(declarations: Sequence[CSharpTypeDeclaration]) -> str:
    canonical = "".join(
        f"{declaration.path}\t{declaration.name}\n"
        for declaration in sorted(declarations, key=lambda item: (item.path, item.name))
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


_CSHARP_USING_ALIAS_PATTERN = re.compile(
    r"(?m)(?:^|(?<=[;{]))\s*(?P<global>global\s+)?using\s+(?:unsafe\s+)?"
    rf"(?P<alias>{CSHARP_IDENTIFIER_PATTERN})\s*=\s*"
    rf"(?P<target>{CSHARP_QUALIFIED_IDENTIFIER_PATTERN})\s*;"
)


def _normalize_csharp_identifier(name: str) -> str:
    return name[1:] if name.startswith("@") else name


def _alias_target_name(target: str) -> str:
    return _normalize_csharp_identifier(target.replace("global::", "").rsplit(".", 1)[-1])


def _normalize_csharp_qualified_identifier(name: str) -> str:
    normalized = name.removeprefix("global::")
    return ".".join(_normalize_csharp_identifier(part) for part in normalized.split("."))


def _merge_csharp_alias(
    aliases: dict[str, str],
    alias: str,
    target: str,
    relative_path: str,
) -> None:
    alias = _normalize_csharp_identifier(alias)
    target_name = _normalize_csharp_qualified_identifier(target)
    previous = aliases.get(alias)
    if previous is not None and previous != target_name:
        raise InventoryError(f"C# using alias is ambiguous: {relative_path}:{alias}.")
    aliases[alias] = target_name


def _global_csharp_aliases(
    snapshot: SourceSnapshot,
    paths: Sequence[str],
) -> dict[str, str]:
    aliases: dict[str, str] = {}
    for relative_path in paths:
        live = _live_csharp_code(_read_csharp(snapshot, relative_path))
        for match in _CSHARP_USING_ALIAS_PATTERN.finditer(live):
            if live[: match.start()].count("{") != live[: match.start()].count("}"):
                raise InventoryError(f"C# namespace-local using alias is unsupported: {relative_path}.")
            if match.group("global") is not None:
                _merge_csharp_alias(
                    aliases,
                    match.group("alias"),
                    match.group("target"),
                    relative_path,
                )
    return aliases


def _file_csharp_aliases(
    live_content: str,
    global_aliases: dict[str, str],
    relative_path: str,
) -> dict[str, str]:
    aliases = dict(global_aliases)
    for match in _CSHARP_USING_ALIAS_PATTERN.finditer(live_content):
        if live_content[: match.start()].count("{") != live_content[: match.start()].count("}"):
            raise InventoryError(f"C# namespace-local using alias is unsupported: {relative_path}.")
        _merge_csharp_alias(
            aliases,
            match.group("alias"),
            match.group("target"),
            relative_path,
        )
    return aliases


def _resolve_csharp_alias(name: str, aliases: dict[str, str], relative_path: str) -> str:
    name = _normalize_csharp_identifier(name)
    seen: set[str] = set()
    while name in aliases:
        if name in seen:
            raise InventoryError(f"C# using alias cycle detected: {relative_path}:{name}.")
        seen.add(name)
        name = aliases[name]
        if "." in name:
            return name
    return name


def _csharp_base_type_names(
    base_list: str,
    aliases: dict[str, str],
    relative_path: str,
) -> tuple[str, ...]:
    parts: list[str] = []
    start = 0
    depth = 0
    for index, character in enumerate(base_list):
        if character in "<([":
            depth += 1
        elif character in ">)]":
            depth -= 1
            if depth < 0:
                raise InventoryError(f"C# base type list is unbalanced: {relative_path}.")
        elif character == "," and depth == 0:
            parts.append(base_list[start:index])
            start = index + 1
    if depth != 0:
        raise InventoryError(f"C# base type list is unbalanced: {relative_path}.")
    parts.append(base_list[start:])

    names: list[str] = []
    qualified_name = re.compile(
        rf"\s*(?P<name>{CSHARP_QUALIFIED_IDENTIFIER_PATTERN})"
    )
    for part in parts:
        token = qualified_name.match(part)
        if token is None:
            raise InventoryError(f"C# base type list drifted: {relative_path}.")
        qualified = token.group("name")
        simple = _alias_target_name(qualified)
        if qualified.startswith("global::") or "." in qualified:
            names.append(_normalize_csharp_qualified_identifier(qualified))
        else:
            names.append(_resolve_csharp_alias(simple, aliases, relative_path))
    return tuple(names)


def _require_target_declaration_surface(
    snapshot: SourceSnapshot,
    paths: Sequence[str],
    base_type: str,
) -> None:
    declaration = re.compile(
        rf"\b(?P<kind>class|struct|interface|record(?:\s+(?:class|struct))?)\s+"
        rf"@?{re.escape(base_type)}(?![A-Za-z0-9_])"
    )
    matches: list[tuple[str, str]] = []
    for relative_path in paths:
        live = _live_csharp_code(_read_csharp(snapshot, relative_path))
        matches.extend(
            (relative_path, match.group("kind")) for match in declaration.finditer(live)
        )
    if base_type == "IDocumentObjectStoreV1":
        expected = [(DOCUMENT_OBJECT_STORE_INTERFACE_PATH, "interface")]
        if matches == expected:
            return
    elif not matches:
        return
    raise InventoryError(f"C# target type declaration surface drifted: {base_type}.")


def _discover_csharp_subclasses(
    snapshot: SourceSnapshot,
    base_type: str,
) -> tuple[CSharpTypeDeclaration, ...]:
    declaration_pattern = re.compile(
        rf"\bclass\s+(?P<name>{CSHARP_IDENTIFIER_PATTERN})"
        r"(?:\s*<[^;{}]+>)?\s*(?:\([^;{}]*\))?\s*:\s*(?P<bases>[^;{}]+)(?:\{|;)",
        re.DOTALL,
    )
    declarations: list[CSharpTypeDeclaration] = []
    paths = tuple(sorted(
        path
        for path in snapshot.files
        if path.startswith(f"{BACKEND_ROOT.as_posix()}/") and path.casefold().endswith(".cs")
    ))
    _require_target_declaration_surface(snapshot, paths, base_type)
    global_aliases = _global_csharp_aliases(snapshot, paths)
    for relative_path in paths:
        _require_repo_relative_path(relative_path)
        live_content = _live_csharp_code(_read_csharp(snapshot, relative_path))
        aliases = _file_csharp_aliases(live_content, global_aliases, relative_path)
        for match in declaration_pattern.finditer(live_content):
            base_names = _csharp_base_type_names(match.group("bases"), aliases, relative_path)
            declarations.append(
                CSharpTypeDeclaration(
                    _normalize_csharp_identifier(match.group("name")),
                    relative_path,
                    base_names,
                )
            )

    declarations_by_name: dict[str, list[CSharpTypeDeclaration]] = {}
    for declaration in declarations:
        declarations_by_name.setdefault(declaration.name, []).append(declaration)

    def derives_from_target(
        declaration: CSharpTypeDeclaration,
        seen: frozenset[str] = frozenset(),
    ) -> bool:
        for parent in declaration.base_types:
            if parent in {base_type, CSHARP_TARGET_TYPE_NAMES[base_type]}:
                return True
            if _alias_target_name(parent) == base_type:
                raise InventoryError(
                    f"C# target base type is bound to an unexpected namespace: "
                    f"{declaration.path}:{parent}."
                )
            if parent in seen:
                raise InventoryError(f"C# inheritance cycle detected while discovering {base_type}.")
            parent_name = _alias_target_name(parent)
            for parent_declaration in declarations_by_name.get(parent_name, ()):
                if derives_from_target(parent_declaration, seen | {parent}):
                    return True
        return False

    result = [declaration for declaration in declarations if derives_from_target(declaration)]
    return tuple(sorted(result, key=lambda item: (item.path, item.name)))


def _require_declaration_set(
    label: str,
    declarations: Sequence[CSharpTypeDeclaration],
    expected_count: int,
    expected_fingerprint: str,
) -> None:
    actual_count = len(declarations)
    actual_fingerprint = _declaration_set_fingerprint(declarations)
    if actual_count != expected_count or actual_fingerprint != expected_fingerprint:
        raise InventoryError(
            f"{label} declaration surface drifted: expected count={expected_count} "
            f"fingerprint={expected_fingerprint}; actual count={actual_count} "
            f"fingerprint={actual_fingerprint}."
        )


def _db_context_inventory(snapshot: SourceSnapshot) -> list[dict[str, str]]:
    declarations = _discover_csharp_subclasses(snapshot, "DbContext")
    _require_declaration_set(
        "DbContext",
        declarations,
        EXPECTED_DB_CONTEXT_COUNT,
        EXPECTED_DB_CONTEXT_DECLARATION_FINGERPRINT,
    )
    paths = [declaration.path for declaration in declarations]
    if len(paths) != len(set(paths)):
        raise InventoryError("A source file declares more than one DbContext.")
    result: list[dict[str, str]] = []
    for declaration in declarations:
        relative_path = declaration.path
        record = _source_file_record(snapshot, relative_path)
        result.append(
            {"name": declaration.name, "path": record["path"], "sha256": record["sha256"]}
        )
    return result


def _migration_inventory(snapshot: SourceSnapshot) -> list[dict[str, str]]:
    declarations = _discover_csharp_subclasses(snapshot, "Migration")
    _require_declaration_set(
        "Migration",
        declarations,
        EXPECTED_MIGRATION_COUNT,
        EXPECTED_MIGRATION_DECLARATION_FINGERPRINT,
    )
    paths = [declaration.path for declaration in declarations]
    if len(paths) != len(set(paths)):
        raise InventoryError("A source file declares more than one Migration.")
    for declaration in declarations:
        path = declaration.path
        _require_repo_relative_path(path)
        name = PurePosixPath(path).name
        if MIGRATION_NAME_PATTERN.fullmatch(name) is None:
            raise InventoryError(f"Migration filename is not canonical: {name}.")
        if declaration.name != PurePosixPath(path).stem.split("_", 1)[-1]:
            raise InventoryError(f"Migration declaration does not match its canonical file: {path}.")
    return [_source_file_record(snapshot, path) for path in paths]


def _require_tokens(content: str, relative_path: str, tokens: Sequence[str]) -> None:
    for token in tokens:
        if token not in content:
            raise InventoryError(f"Required source anchor drifted: {relative_path}.")


def _is_live_csharp_span(
    content: str,
    live_content: str,
    start: int,
    end: int,
) -> bool:
    return live_content[start:end] == _live_csharp_code(content[start:end])


def _require_live_csharp_tokens(
    content: str,
    relative_path: str,
    tokens: Sequence[str],
) -> None:
    live_content = _live_csharp_code(content)
    for token in tokens:
        start = 0
        while (index := content.find(token, start)) >= 0:
            end = index + len(token)
            if _is_live_csharp_span(content, live_content, index, end):
                break
            start = index + 1
        else:
            raise InventoryError(f"Required source anchor drifted: {relative_path}.")


def _csharp_type_body(
    content: str,
    declaration_kind: str,
    type_name: str,
    relative_path: str,
) -> str:
    live = _live_csharp_code(content)
    declaration = re.compile(
        rf"\b{re.escape(declaration_kind)}\s+{re.escape(type_name)}\b[^;{{}}]*\{{",
        re.DOTALL,
    )
    matches = list(declaration.finditer(live))
    if len(matches) != 1:
        raise InventoryError(f"Required C# type is missing or ambiguous: {relative_path}:{type_name}.")
    opening = live.find("{", matches[0].start(), matches[0].end())
    closing = _matching_brace(live, opening)
    return content[opening + 1 : closing]


def _document_type_declarations(
    snapshot: SourceSnapshot,
) -> tuple[tuple[str, str, str, tuple[str, ...]], ...]:
    declaration = re.compile(
        rf"\b(?P<kind>class|struct|interface|record(?:\s+(?:class|struct))?)\s+"
        rf"(?P<name>{CSHARP_IDENTIFIER_PATTERN})"
        r"(?:\s*<[^;{}]+>)?\s*(?:\([^;{}]*\))?\s*:\s*(?P<bases>[^;{}]+)(?:\{|;)",
        re.DOTALL,
    )
    result: list[tuple[str, str, str, tuple[str, ...]]] = []
    paths = tuple(sorted(
        path
        for path in snapshot.files
        if path.startswith(DOCUMENTS_SOURCE_ROOT) and path.casefold().endswith(".cs")
    ))
    _require_target_declaration_surface(snapshot, paths, "IDocumentObjectStoreV1")
    global_aliases = _global_csharp_aliases(snapshot, paths)
    for relative_path in paths:
        _require_repo_relative_path(relative_path)
        live = _live_csharp_code(_read_csharp(snapshot, relative_path))
        aliases = _file_csharp_aliases(live, global_aliases, relative_path)
        for match in declaration.finditer(live):
            bases = _csharp_base_type_names(match.group("bases"), aliases, relative_path)
            result.append(
                (
                    match.group("kind"),
                    _normalize_csharp_identifier(match.group("name")),
                    relative_path,
                    bases,
                )
            )
    return tuple(result)


def _discover_document_object_store_adapters(
    snapshot: SourceSnapshot,
) -> tuple[tuple[str, str, str], ...]:
    declarations = _document_type_declarations(snapshot)
    type_bases: dict[str, set[str]] = {}
    for kind, name, _, bases in declarations:
        type_bases.setdefault(name, set()).update(bases)

    def is_object_store_type(name: str, seen: frozenset[str] = frozenset()) -> bool:
        if name in {
            "IDocumentObjectStoreV1",
            CSHARP_TARGET_TYPE_NAMES["IDocumentObjectStoreV1"],
        }:
            return True
        if _alias_target_name(name) == "IDocumentObjectStoreV1":
            raise InventoryError(
                f"Documents object-store interface is bound to an unexpected namespace: {name}."
            )
        if name in seen:
            raise InventoryError("Documents object-store type inheritance contains a cycle.")
        return any(
            is_object_store_type(base, seen | {name})
            for base in type_bases.get(name, ())
        )

    adapters = [
        (kind, name, relative_path)
        for kind, name, relative_path, bases in declarations
        if kind != "interface" and any(is_object_store_type(base) for base in bases)
    ]

    if len(adapters) < 2:
        raise InventoryError("Documents object-store adapter surface drifted.")
    folded_names = [name.casefold() for _, name, _ in adapters]
    if len(folded_names) != len(set(folded_names)):
        raise InventoryError("Documents object-store adapters duplicate a class name.")
    return tuple(sorted(adapters, key=lambda item: (item[1].casefold(), item[2])))


def _documents_inventory(snapshot: SourceSnapshot) -> dict[str, Any]:
    module_path = DOCUMENT_MODULE_PATH
    module_content = _read_csharp(snapshot, module_path)
    live_module_content = _live_csharp_code(module_content)
    configuration_key_pattern = re.compile(
        r'public const string [A-Za-z][A-Za-z0-9]*Key\s*=\s*"(Documents:[^"]+)";'
    )
    discovered_keys = tuple(
        sorted(
            match.group(1)
            for match in configuration_key_pattern.finditer(module_content)
            if _is_live_csharp_span(
                module_content, live_module_content, match.start(), match.end()
            )
        )
    )
    if discovered_keys != DOCUMENT_CONFIGURATION_KEYS:
        raise InventoryError("Documents configuration-key surface drifted.")

    discovered_adapters = _discover_document_object_store_adapters(snapshot)
    adapter_records: list[dict[str, str]] = []
    for adapter_kind, adapter_name, relative_path in discovered_adapters:
        content = _read_csharp(snapshot, relative_path)
        adapter_body = _csharp_type_body(content, adapter_kind, adapter_name, relative_path)
        live_adapter_body = _live_csharp_code(adapter_body)
        if "DocumentContentHash.Compute" not in live_adapter_body:
            raise InventoryError(f"Documents object-store hash contract drifted: {relative_path}.")
        key_pattern = re.compile(
            rf"\bGeneratedRegex\s*\(\s*{re.escape(json.dumps(DOCUMENT_OBJECT_KEY_PATTERN))}"
        )
        if not any(
            _is_live_csharp_span(
                adapter_body,
                live_adapter_body,
                match.start(),
                match.end(),
            )
            for match in key_pattern.finditer(adapter_body)
        ):
            raise InventoryError(f"Documents object-key contract drifted: {relative_path}.")
        if adapter_name in {"FileSystemDocumentObjectStore", "ServerDirectoryDocumentObjectStore"}:
            resolve_body = _csharp_method_body(content, "Resolve", relative_path)
            resolved_path = re.compile(
                r"\bvar\s+path\s*=\s*Path\.GetFullPath\s*\(\s*"
                r"Path\.Combine\s*\(\s*root\s*,\s*key\.Replace\s*\("
            )
            if (
                resolved_path.search(resolve_body) is None
                or _csharp_assignment_count(resolve_body, "path") != 1
                or len(re.findall(r"\breturn\s+path\s*;", resolve_body)) != 1
                or _csharp_assignment_count(resolve_body, "root") != 0
            ):
                raise InventoryError(f"Documents configured-root use drifted: {relative_path}.")
            for operation in ("PutAsync", "ReadAsync"):
                operation_body = _csharp_method_body(content, operation, relative_path)
                if (
                    "var path = Resolve(key);" not in operation_body
                    or _csharp_assignment_count(operation_body, "path") != 1
                    or _csharp_identifier_count(operation_body, "Resolve") != 1
                ):
                    raise InventoryError(
                        f"Documents operative object path drifted: {relative_path}:{operation}."
                    )
            for method_name in ("Resolve", "PutAsync", "ReadAsync"):
                _require_reviewed_method_fingerprint(content, relative_path, method_name)

        if adapter_name == "FileSystemDocumentObjectStore":
            constructor_body = _csharp_method_body(content, adapter_name, relative_path)
            if (
                "root = Path.GetFullPath(rootPath);" not in constructor_body
                or _csharp_assignment_count(constructor_body, "root") != 1
            ):
                raise InventoryError(f"Documents configured-root constructor drifted: {relative_path}.")
            _require_reviewed_method_fingerprint(content, relative_path, adapter_name)
        elif adapter_name == "ServerDirectoryDocumentObjectStore":
            open_body = _csharp_method_body(content, "Open", relative_path)
            constructor_body = _csharp_method_body(content, adapter_name, relative_path)
            if (
                "var fullRoot = Path.GetFullPath(rootPath);" not in open_body
                or "return new ServerDirectoryDocumentObjectStore(fullRoot);" not in open_body
                or _csharp_assignment_count(open_body, "fullRoot") != 1
                or open_body.count("new ServerDirectoryDocumentObjectStore") != 1
                or "root = fullRoot;" not in constructor_body
                or _csharp_assignment_count(constructor_body, "root") != 1
            ):
                raise InventoryError(f"Documents configured-root constructor drifted: {relative_path}.")
            _require_reviewed_method_fingerprint(content, relative_path, "Open")
            _require_reviewed_method_fingerprint(content, relative_path, adapter_name)
        adapter_records.append({"name": adapter_name, "path": relative_path})

    runtime_content = _read_csharp(snapshot, DOCUMENT_RUNTIME_PATH)
    _require_live_csharp_tokens(
        runtime_content,
        DOCUMENT_RUNTIME_PATH,
        (
            "AddDocumentsRuntime",
            "allowDevelopmentStorageAndScanner",
            "services.AddDocumentsModule(",
        ),
    )
    _require_method_tokens(
        module_content,
        module_path,
        "AddDocumentsModule",
        (
            "var root = configuration[ObjectStoreRootPathKey]",
            "new FileSystemDocumentObjectStore(string.IsNullOrWhiteSpace(root)",
        ),
    )
    development_registration = _csharp_method_body(
        module_content, "AddDocumentsModule", module_path
    )
    if (
        _csharp_assignment_count(development_registration, "root") != 1
        or _csharp_assignment_count(development_registration, "store") != 1
        or "services.TryAddSingleton<IDocumentObjectStoreV1>(store);"
        not in development_registration
    ):
        raise InventoryError("Documents development root registration drifted.")
    _require_reviewed_method_fingerprint(module_content, module_path, "AddDocumentsModule")
    _require_method_tokens(
        module_content,
        module_path,
        "RegisterProductionStorageAndScanner",
        (
            "var root = configuration[ObjectStoreRootPathKey]",
            "ServerDirectoryDocumentObjectStore.Open(root!)",
        ),
    )
    production_registration = _csharp_method_body(
        module_content, "RegisterProductionStorageAndScanner", module_path
    )
    if (
        _csharp_assignment_count(production_registration, "root") != 1
        or _csharp_assignment_count(production_registration, "store") != 1
        or "services.TryAddSingleton<IDocumentObjectStoreV1>(store);"
        not in production_registration
    ):
        raise InventoryError("Documents production root registration drifted.")
    _require_reviewed_method_fingerprint(
        module_content,
        module_path,
        "RegisterProductionStorageAndScanner",
    )

    readme_path = DOCUMENT_README_PATH
    scanner_path = DOCUMENT_SCANNER_PATH
    scanner = _read_csharp(snapshot, scanner_path)
    _require_live_csharp_tokens(
        scanner,
        scanner_path,
        (
            "WindowsDefenderDocumentScanner : IDocumentContentScannerV1",
            "private static void TryDelete(string path)",
        ),
    )
    scan_body = _csharp_method_body(scanner, "ScanAsync", scanner_path)
    if _csharp_identifier_count(scan_body, "TryDelete") != 1:
        raise InventoryError(f"Documents transient cleanup binding drifted: {scanner_path}.")
    exact_scan_signature = re.compile(
        r"\bpublic\s+async\s+Task<DocumentScanVerdictV1>\s+ScanAsync\s*\(\s*"
        r"ReadOnlyMemory<byte>\s+content\s*,\s*DocumentFileKind\s+kind\s*,\s*"
        r"CancellationToken\s+cancellationToken\s*=\s*default\s*\)"
    )
    if len(exact_scan_signature.findall(_live_csharp_code(scanner))) != 1:
        raise InventoryError(f"Documents scanner interface implementation drifted: {scanner_path}.")
    if "var copy = Path.Combine(options.QuarantineDirectory" not in scan_body:
        raise InventoryError(f"Documents quarantine-copy lifecycle drifted: {scanner_path}.")
    cleanup = re.compile(
        r"finally\s*\{\s*TryDelete\(copy\);\s*gate\.Release\(\);\s*\}",
        re.DOTALL,
    )
    if cleanup.search(scan_body) is None:
        raise InventoryError(f"Documents transient cleanup lifecycle drifted: {scanner_path}.")
    try_delete_body = _csharp_method_body(scanner, "TryDelete", scanner_path)
    delete_sequence = re.compile(
        r"\bif\s*\(\s*File\.Exists\(path\)\s*\)\s*\{\s*File\.Delete\(path\);\s*\}",
        re.DOTALL,
    )
    if delete_sequence.search(try_delete_body) is None:
        raise InventoryError(f"Documents transient deletion implementation drifted: {scanner_path}.")
    _require_reviewed_method_fingerprint(scanner, scanner_path, "ScanAsync")
    _require_reviewed_method_fingerprint(scanner, scanner_path, "TryDelete")

    reviewed_document_paths = {module_path, scanner_path}
    reviewed_document_paths.update(
        relative_path
        for _, adapter_name, relative_path in discovered_adapters
        if adapter_name in {"FileSystemDocumentObjectStore", "ServerDirectoryDocumentObjectStore"}
    )
    for relative_path in sorted(reviewed_document_paths):
        _require_reviewed_live_source(snapshot, relative_path)

    readme = " ".join(_read_source(snapshot, readme_path).split())
    _require_tokens(
        readme,
        readme_path,
        (
            "same backup window",
            "Local disk, not the NAS, not backed up",
        ),
    )

    return {
        "configurationKeys": list(DOCUMENT_CONFIGURATION_KEYS),
        "objectStoreAdapters": adapter_records,
        "objectKeyPattern": DOCUMENT_OBJECT_KEY_PATTERN,
        "pathClassifications": [
            {"configurationKey": key, "classification": classification}
            for key, classification in DOCUMENT_PATH_CLASSIFICATIONS
        ],
        "sourceFiles": [
            _source_file_record(snapshot, path)
            for path in (
                *DOCUMENT_SOURCE_PATHS,
                *sorted({path for _, _, path in discovered_adapters}),
            )
        ],
    }


def _csharp_call_arguments(
    content: str,
    live_content: str,
    opening_parenthesis: int,
) -> tuple[str, ...]:
    if live_content[opening_parenthesis : opening_parenthesis + 1] != "(":
        raise InventoryError("C# call shape drifted while discovering durable mappings.")
    arguments: list[str] = []
    start = opening_parenthesis + 1
    parenthesis_depth = 1
    brace_depth = 0
    bracket_depth = 0
    for index in range(start, len(live_content)):
        character = live_content[index]
        if character == "(":
            parenthesis_depth += 1
        elif character == ")":
            parenthesis_depth -= 1
            if parenthesis_depth == 0:
                arguments.append(content[start:index].strip())
                return tuple(argument for argument in arguments if argument)
        elif character == "{":
            brace_depth += 1
        elif character == "}":
            brace_depth -= 1
        elif character == "[":
            bracket_depth += 1
        elif character == "]":
            bracket_depth -= 1
        elif (
            character == ","
            and parenthesis_depth == 1
            and brace_depth == 0
            and bracket_depth == 0
        ):
            arguments.append(content[start:index].strip())
            if len(arguments) == 2:
                return tuple(arguments)
            start = index + 1
        if parenthesis_depth < 1 or brace_depth < 0 or bracket_depth < 0:
            break
    raise InventoryError("C# call is unterminated while discovering durable mappings.")


def _csharp_literal_identifier(argument: str) -> str | None:
    match = re.fullmatch(r'\s*"([A-Za-z][A-Za-z0-9_]*)"\s*', argument)
    return match.group(1) if match is not None else None


def _context_schema_constant(content: str, live_content: str, relative_path: str) -> str | None:
    values: set[str] = set()
    declaration = re.compile(r"\bconst\s+string\s+Schema\s*=")
    for match in declaration.finditer(live_content):
        literal = re.match(r'\s*"([a-z][a-z0-9_]*)"\s*;', content[match.end() :])
        if literal is None:
            raise InventoryError(f"DbContext Schema constant drifted: {relative_path}.")
        values.add(literal.group(1))
    if len(values) > 1:
        raise InventoryError(f"DbContext has ambiguous Schema constants: {relative_path}.")
    return next(iter(values), None)


def _is_message_or_dedup_mapping(
    entity_type: str,
    table: str,
    property_names: Sequence[str],
    primary_key: Sequence[str] = (),
    unique_indexes: Sequence[Sequence[str]] = (),
    model_region: str = "",
) -> bool:
    if (
        MESSAGE_ENTITY_NAME_PATTERN.search(entity_type) is not None
        or MESSAGE_TABLE_NAME_PATTERN.search(table) is not None
    ):
        return True
    properties = set(property_names)
    if sum(anchor in properties or anchor in model_region for anchor in MESSAGE_MODEL_ANCHORS) >= 2:
        return True
    identity_sets = [set(primary_key), *(set(index) for index in unique_indexes)]
    if any({"Consumer", "MessageId"}.issubset(identity) for identity in identity_sets):
        return True
    has_payload = any(
        re.fullmatch(
            r"(?:Payload(?:Json|Utf8|Body|Content)?|Envelope(?:Body|Json|Payload)?|MessageBody|Body|Content)",
            property_name,
            re.IGNORECASE,
        )
        for property_name in properties
    )
    has_terminal_time = any(
        re.search(r"(?:Handled|Processed|Consumed|Dispatched).*(?:At|Utc)$", property_name, re.IGNORECASE)
        for property_name in properties
    )
    return has_payload and has_terminal_time


def _resolve_table_mapping(
    content: str,
    live_content: str,
    opening_parenthesis: int,
    schema_constant: str | None,
    relative_path: str,
) -> tuple[str, str] | None:
    arguments = _csharp_call_arguments(content, live_content, opening_parenthesis)
    if not arguments:
        return None
    table = _csharp_literal_identifier(arguments[0])
    if table is None:
        return None

    schema: str | None = None
    if len(arguments) >= 2:
        schema = _csharp_literal_identifier(arguments[1])
        if schema is None and re.fullmatch(r"\s*Schema\s*", arguments[1]) is None:
            if "=>" not in arguments[1]:
                raise InventoryError(f"Message/dedup mapping schema drifted: {relative_path}.")
    if schema is None:
        schema = schema_constant
    if schema is None:
        raise InventoryError(f"Message/dedup mapping schema cannot be resolved: {relative_path}.")
    return schema, table


def _discover_snapshot_table_mappings(snapshot: SourceSnapshot) -> tuple[ModelTableMapping, ...]:
    entity_pattern = re.compile(
        r'modelBuilder\.Entity\("(?P<entity>[^"]+)",\s*b\s*=>\s*\{'
        r'(?P<body>.*?)\n\s*\}\);',
        re.DOTALL,
    )
    property_pattern = re.compile(
        r'\bb\.Property(?:<[^>]+>)?\(\s*"(?P<property>[A-Za-z_][A-Za-z0-9_]*)"\s*\)'
    )
    table_pattern = re.compile(
        r'\bb\.ToTable\(\s*"(?P<table>[A-Za-z][A-Za-z0-9_]*)"\s*,'
        r'\s*"(?P<schema>[a-z][a-z0-9_]*)"'
    )
    key_pattern = re.compile(r"\bb\.HasKey\((?P<arguments>[^)]*)\)\s*;")
    index_pattern = re.compile(
        r"\bb\.HasIndex\((?P<arguments>[^)]*)\)(?P<chain>.*?)\s*;",
        re.DOTALL,
    )
    quoted_property_pattern = re.compile(r'"([A-Za-z_][A-Za-z0-9_]*)"')
    mappings: list[ModelTableMapping] = []
    snapshot_paths = sorted(path for path in snapshot.files if path.endswith("ModelSnapshot.cs"))
    for relative_path in snapshot_paths:
        _require_repo_relative_path(relative_path)
        content = _read_csharp(snapshot, relative_path)
        live_content = _live_csharp_code(content)
        for entity_match in entity_pattern.finditer(content):
            if not _is_live_csharp_span(
                content, live_content, entity_match.start(), entity_match.end()
            ):
                continue
            body = entity_match.group("body")
            body_start = entity_match.start("body")
            live_tables = [
                match
                for match in table_pattern.finditer(body)
                if _is_live_csharp_span(
                    content,
                    live_content,
                    body_start + match.start(),
                    body_start + match.end(),
                )
            ]
            if not live_tables:
                continue
            if len(live_tables) != 1:
                raise InventoryError(f"Model snapshot table mapping is ambiguous: {relative_path}.")
            properties = tuple(
                sorted(
                    {
                        match.group("property")
                        for match in property_pattern.finditer(body)
                        if _is_live_csharp_span(
                            content,
                            live_content,
                            body_start + match.start(),
                            body_start + match.end(),
                        )
                    }
                )
            )
            table_match = live_tables[0]
            live_body = live_content[body_start : body_start + len(body)]
            keys = [
                tuple(quoted_property_pattern.findall(match.group("arguments")))
                for match in key_pattern.finditer(body)
                if _is_live_csharp_span(
                    content,
                    live_content,
                    body_start + match.start(),
                    body_start + match.end(),
                )
            ]
            if len(keys) != 1 or not keys[0]:
                raise InventoryError(f"Model snapshot primary-key mapping drifted: {relative_path}.")
            unique_indexes = tuple(
                sorted(
                    tuple(quoted_property_pattern.findall(match.group("arguments")))
                    for match in index_pattern.finditer(body)
                    if _is_live_csharp_span(
                        content,
                        live_content,
                        body_start + match.start(),
                        body_start + match.end(),
                    )
                    if ".IsUnique()" in match.group("chain")
                )
            )
            mappings.append(
                ModelTableMapping(
                    entity_match.group("entity").rsplit(".", 1)[-1],
                    table_match.group("schema"),
                    table_match.group("table"),
                    properties,
                    keys[0],
                    unique_indexes,
                )
            )
    return tuple(
        sorted(
            mappings,
            key=lambda item: (
                item.schema,
                item.table,
                item.entity_type,
                item.property_names,
                item.primary_key,
                item.unique_indexes,
            ),
        )
    )


def _discover_message_delivery_tables(
    snapshot: SourceSnapshot,
    snapshot_mappings: Sequence[ModelTableMapping] | None = None,
) -> tuple[tuple[str, str], ...]:
    if snapshot_mappings is None:
        snapshot_mappings = _discover_snapshot_table_mappings(snapshot)
    tables: set[tuple[str, str]] = {
        (mapping.schema, mapping.table)
        for mapping in snapshot_mappings
        if _is_message_or_dedup_mapping(
            mapping.entity_type,
            mapping.table,
            mapping.property_names,
            mapping.primary_key,
            mapping.unique_indexes,
        )
    }
    context_paths = sorted(path for path in snapshot.files if path.endswith("DbContext.cs"))
    declaration_pattern = re.compile(
        r"\b(?P<builder>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"
        r"[A-Za-z_][A-Za-z0-9_]*\s*\.\s*Entity\s*<(?P<entity>[A-Za-z_][A-Za-z0-9_.]*)>"
        r"\s*\(\s*\)\s*;"
    )
    direct_pattern = re.compile(
        r"\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*Entity\s*<(?P<entity>[A-Za-z_][A-Za-z0-9_.]*)>"
        r"\s*\(\s*\)\s*\.\s*ToTable\s*\("
    )
    builder_table_pattern = re.compile(
        r"\b(?P<builder>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*ToTable\s*\("
    )

    for relative_path in context_paths:
        _require_repo_relative_path(relative_path)
        content = _read_csharp(snapshot, relative_path)
        live_content = _live_csharp_code(content)
        schema_constant = _context_schema_constant(content, live_content, relative_path)
        declarations = list(declaration_pattern.finditer(live_content))

        for match in direct_pattern.finditer(live_content):
            entity_type = match.group("entity").rsplit(".", 1)[-1]
            opening = live_content.rfind("(", match.start(), match.end())
            mapping = _resolve_table_mapping(
                content, live_content, opening, schema_constant, relative_path
            )
            if mapping is not None and _is_message_or_dedup_mapping(
                entity_type, mapping[1], (), model_region=""
            ):
                tables.add(mapping)

        for table_match in builder_table_pattern.finditer(live_content):
            builder = table_match.group("builder")
            candidates = [
                declaration
                for declaration in declarations
                if declaration.start() < table_match.start()
                and declaration.group("builder") == builder
            ]
            if not candidates:
                continue
            declaration = candidates[-1]
            next_declaration_start = next(
                (
                    candidate.start()
                    for candidate in declarations
                    if candidate.start() > declaration.start()
                ),
                len(live_content),
            )
            if table_match.start() >= next_declaration_start:
                continue
            entity_type = declaration.group("entity").rsplit(".", 1)[-1]
            model_region = live_content[declaration.start() : next_declaration_start]
            opening = live_content.rfind("(", table_match.start(), table_match.end())
            mapping = _resolve_table_mapping(
                content, live_content, opening, schema_constant, relative_path
            )
            if mapping is not None and _is_message_or_dedup_mapping(
                entity_type, mapping[1], (), model_region=model_region
            ):
                tables.add(mapping)
    return tuple(sorted(tables))


def _discover_idempotency_key_tables(
    snapshot: SourceSnapshot,
    snapshot_mappings: Sequence[ModelTableMapping] | None = None,
) -> tuple[tuple[str, str], ...]:
    if snapshot_mappings is None:
        snapshot_mappings = _discover_snapshot_table_mappings(snapshot)
    return tuple(
        sorted(
            {
                (mapping.schema, mapping.table)
                for mapping in snapshot_mappings
                if "IdempotencyKeyHash" in mapping.property_names
            }
        )
    )


def _discover_replay_enforcement_tables(
    snapshot: SourceSnapshot,
    snapshot_mappings: Sequence[ModelTableMapping] | None = None,
) -> tuple[tuple[str, str], ...]:
    if snapshot_mappings is None:
        snapshot_mappings = _discover_snapshot_table_mappings(snapshot)
    return tuple(
        sorted(
            {
                (mapping.schema, mapping.table)
                for mapping in snapshot_mappings
                if "IdempotencyKeyHash" in mapping.property_names
                and (
                    "IdempotencyKeyHash" in mapping.primary_key
                    or any(
                        "IdempotencyKeyHash" in index
                        for index in mapping.unique_indexes
                    )
                )
            }
        )
    )


def _audit_live_source_fingerprint(snapshot: SourceSnapshot) -> tuple[int, str]:
    """Fingerprint the complete reviewed, non-generated Audit live source set.

    Metadata-only classification must fail closed for any live Audit source
    change. This bounded invariant is intentionally stricter than attempting to
    recognize every valid C# query or constant-expression spelling.
    """

    records: list[str] = []
    for relative_path in sorted(snapshot.files):
        folded = relative_path.casefold()
        if not relative_path.startswith(AUDIT_SOURCE_ROOT) or not folded.endswith(".cs"):
            continue
        parts = tuple(part.casefold() for part in PurePosixPath(relative_path).parts)
        if "migrations" in parts or folded.endswith(AUDIT_LIVE_SOURCE_EXCLUDED_SUFFIXES):
            continue
        live = _live_csharp_code_with_literals(_read_source(snapshot, relative_path))
        normalized = re.sub(r"\s+", " ", live).strip()
        records.append(f"{relative_path}\0{normalized}")
    payload = "\n".join(records).encode("utf-8")
    return len(records), hashlib.sha256(payload).hexdigest()


def _require_reviewed_audit_live_source(snapshot: SourceSnapshot) -> None:
    actual_count, actual_fingerprint = _audit_live_source_fingerprint(snapshot)
    if (
        actual_count != EXPECTED_AUDIT_LIVE_SOURCE_FILE_COUNT
        or actual_fingerprint != EXPECTED_AUDIT_LIVE_SOURCE_FINGERPRINT
    ):
        raise InventoryError(
            "Audit metadata-only live-source surface drifted; independent review is required."
        )


def _require_discovered_durable_surfaces(snapshot: SourceSnapshot) -> None:
    snapshot_mappings = _discover_snapshot_table_mappings(snapshot)
    discovered_idempotency = _discover_idempotency_key_tables(snapshot, snapshot_mappings)
    if discovered_idempotency != EXPECTED_IDEMPOTENCY_KEY_TABLES:
        raise InventoryError("Durable idempotency-key table surface drifted.")

    metadata_only = set(METADATA_ONLY_IDEMPOTENCY_KEY_TABLES)
    if not metadata_only.issubset(discovered_idempotency):
        raise InventoryError("Metadata-only idempotency-key classification drifted.")
    discovered_replay_tables = set(
        _discover_replay_enforcement_tables(snapshot, snapshot_mappings)
    )
    if set(discovered_idempotency) - discovered_replay_tables != metadata_only:
        raise InventoryError("Metadata-only idempotency-key classification drifted.")
    _require_reviewed_audit_live_source(snapshot)
    expected_replay_tables = set(discovered_idempotency) - metadata_only
    specified_replay_tables = {
        (spec.schema, spec.table)
        for spec in DURABLE_STORE_SPECS
        if spec.kind == "idempotency"
    }
    if specified_replay_tables != expected_replay_tables:
        raise InventoryError("Durable replay-store specification drifted.")

    specified_message_tables = tuple(
        sorted(
            (spec.schema, spec.table)
            for spec in DURABLE_STORE_SPECS
            if spec.kind in {"delivery", "inbox", "outbox"}
        )
    )
    if specified_message_tables != EXPECTED_MESSAGE_DELIVERY_TABLES:
        raise InventoryError("Durable message/delivery specification drifted.")

    discovered_message_tables = set(
        _discover_message_delivery_tables(snapshot, snapshot_mappings)
    )
    excluded_business_tables = set(NON_MESSAGE_DELIVERY_TABLES)
    if not excluded_business_tables.issubset(discovered_message_tables):
        raise InventoryError("Non-message delivery classification drifted.")
    for mapping in snapshot_mappings:
        if (mapping.schema, mapping.table) in excluded_business_tables and _is_message_or_dedup_mapping(
            "", "", mapping.property_names, mapping.primary_key, mapping.unique_indexes
        ):
            raise InventoryError("Non-message delivery table gained durable message semantics.")
    if discovered_message_tables - excluded_business_tables != set(EXPECTED_MESSAGE_DELIVERY_TABLES):
        raise InventoryError("Durable message/delivery source surface drifted.")


def _durable_store_inventory(snapshot: SourceSnapshot) -> list[dict[str, Any]]:
    discovered_contracts = tuple(
        sorted(
            path
            for path in snapshot.files
            if path.startswith("src/backend/Modules/") and path.endswith("OutboxV1.cs")
        )
    )
    if discovered_contracts != EXPECTED_OUTBOX_CONTRACT_PATHS:
        raise InventoryError("Durable outbox contract surface drifted.")

    _require_discovered_durable_surfaces(snapshot)

    scoped_writers = {
        (spec.identifier, evidence.path)
        for spec in DURABLE_STORE_SPECS
        for evidence in spec.evidence
        if evidence.live_code
    }
    if set(WRITER_METHOD_SCOPES) != scoped_writers:
        raise InventoryError("Durable writer method-scope registry drifted.")

    physical_keys = [(spec.schema, spec.table) for spec in DURABLE_STORE_SPECS]
    if len(physical_keys) != len(set(physical_keys)):
        raise InventoryError("Durable-store specifications duplicate a physical table.")

    records: list[dict[str, Any]] = []
    for spec in DURABLE_STORE_SPECS:
        source_files: list[dict[str, str]] = []
        for evidence in sorted(spec.evidence, key=lambda item: item.path):
            content = _read_csharp(snapshot, evidence.path)
            if evidence.live_code:
                method_name = WRITER_METHOD_SCOPES.get((spec.identifier, evidence.path))
                if method_name is None:
                    raise InventoryError(
                        f"Durable writer method scope is missing: {spec.identifier}:{evidence.path}."
                    )
                _require_reviewed_live_source(snapshot, evidence.path)
                _require_reviewed_method_fingerprint(
                    content,
                    evidence.path,
                    method_name,
                )
                _require_method_tokens(
                    content,
                    evidence.path,
                    method_name,
                    evidence.required_tokens,
                )
            else:
                _require_live_csharp_tokens(content, evidence.path, evidence.required_tokens)
            source_files.append(_source_file_record(snapshot, evidence.path))
        records.append(
            {
                "id": spec.identifier,
                "module": spec.module,
                "kind": spec.kind,
                "dbContext": spec.db_context,
                "entityType": spec.entity_type,
                "schema": spec.schema,
                "table": spec.table,
                "contracts": sorted(spec.contracts),
                "sourceFiles": source_files,
            }
        )
    records.sort(key=lambda record: record["id"])
    if len(records) != EXPECTED_DURABLE_STORE_COUNT:
        raise InventoryError("Durable-store specification count drifted.")
    return records


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise InventoryError("JSON schema contains a duplicate object key.")
        result[key] = value
    return result


def _load_schema(snapshot: SourceSnapshot) -> dict[str, Any]:
    relative_path = SCHEMA_PATH.as_posix()
    text = _read_source(snapshot, relative_path)
    try:
        schema = json.loads(text, object_pairs_hook=_reject_duplicate_keys)
    except (json.JSONDecodeError, UnicodeError) as exc:
        raise InventoryError("Recovery inventory schema is not valid JSON.") from exc
    if not isinstance(schema, dict):
        raise InventoryError("Recovery inventory schema root must be an object.")
    return schema


def _is_json_type(value: Any, expected: str) -> bool:
    if expected == "object":
        return isinstance(value, dict)
    if expected == "array":
        return isinstance(value, list)
    if expected == "string":
        return isinstance(value, str)
    if expected == "integer":
        return isinstance(value, int) and not isinstance(value, bool)
    if expected == "boolean":
        return isinstance(value, bool)
    raise InventoryError(f"Unsupported JSON Schema type: {expected}.")


def _json_kind(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, (int, float)):
        return "number"
    if isinstance(value, str):
        return "string"
    if isinstance(value, list):
        return "array"
    if isinstance(value, dict):
        return "object"
    return "unsupported"


def _json_equal(left: Any, right: Any) -> bool:
    """Compare values with JSON value semantics instead of Python bool/int equality."""

    if _json_kind(left) != _json_kind(right):
        return False
    if isinstance(left, list) and isinstance(right, list):
        return len(left) == len(right) and all(
            _json_equal(left_item, right_item)
            for left_item, right_item in zip(left, right, strict=True)
        )
    if isinstance(left, dict) and isinstance(right, dict):
        return left.keys() == right.keys() and all(
            _json_equal(left[key], right[key]) for key in left
        )
    return bool(left == right)


def _validate_against_schema(value: Any, schema: dict[str, Any], location: str = "$") -> None:
    unsupported = set(schema) - SUPPORTED_SCHEMA_KEYWORDS
    if unsupported:
        raise InventoryError(
            f"Recovery inventory schema uses unsupported keywords at {location}."
        )

    if "const" in schema and not _json_equal(value, schema["const"]):
        raise InventoryError(f"Recovery inventory violates const at {location}.")
    if "enum" in schema and not any(_json_equal(value, item) for item in schema["enum"]):
        raise InventoryError(f"Recovery inventory violates enum at {location}.")

    expected_type = schema.get("type")
    if expected_type is not None and not _is_json_type(value, expected_type):
        raise InventoryError(f"Recovery inventory has wrong type at {location}.")

    if isinstance(value, dict):
        required = schema.get("required", [])
        if not isinstance(required, list) or not all(isinstance(item, str) for item in required):
            raise InventoryError(f"Recovery inventory schema has invalid required at {location}.")
        missing = sorted(set(required) - set(value))
        if missing:
            raise InventoryError(f"Recovery inventory is missing fields at {location}.")
        properties = schema.get("properties", {})
        if not isinstance(properties, dict):
            raise InventoryError(f"Recovery inventory schema has invalid properties at {location}.")
        if schema.get("additionalProperties") is False:
            extras = sorted(set(value) - set(properties))
            if extras:
                raise InventoryError(f"Recovery inventory has extra fields at {location}.")
        for key, child in value.items():
            child_schema = properties.get(key)
            if child_schema is not None:
                if not isinstance(child_schema, dict):
                    raise InventoryError(
                        f"Recovery inventory schema has invalid property at {location}.{key}."
                    )
                _validate_against_schema(child, child_schema, f"{location}.{key}")

    if isinstance(value, list):
        minimum = schema.get("minItems")
        maximum = schema.get("maxItems")
        if minimum is not None and len(value) < minimum:
            raise InventoryError(f"Recovery inventory has too few items at {location}.")
        if maximum is not None and len(value) > maximum:
            raise InventoryError(f"Recovery inventory has too many items at {location}.")
        if schema.get("uniqueItems") is True:
            for index, item in enumerate(value):
                if any(_json_equal(item, previous) for previous in value[:index]):
                    raise InventoryError(f"Recovery inventory has duplicate items at {location}.")
        item_schema = schema.get("items")
        if item_schema is not None:
            if not isinstance(item_schema, dict):
                raise InventoryError(f"Recovery inventory schema has invalid items at {location}.")
            for index, item in enumerate(value):
                _validate_against_schema(item, item_schema, f"{location}[{index}]")

    if isinstance(value, str):
        minimum_length = schema.get("minLength")
        maximum_length = schema.get("maxLength")
        if minimum_length is not None and len(value) < minimum_length:
            raise InventoryError(f"Recovery inventory string is too short at {location}.")
        if maximum_length is not None and len(value) > maximum_length:
            raise InventoryError(f"Recovery inventory string is too long at {location}.")
        pattern = schema.get("pattern")
        if pattern is not None:
            try:
                matched = re.search(pattern, value)
            except re.error as exc:
                raise InventoryError(
                    f"Recovery inventory schema has invalid pattern at {location}."
                ) from exc
            if matched is None:
                raise InventoryError(f"Recovery inventory string violates pattern at {location}.")

    if isinstance(value, int) and not isinstance(value, bool):
        minimum = schema.get("minimum")
        if minimum is not None and value < minimum:
            raise InventoryError(f"Recovery inventory integer is too small at {location}.")


def _require_source_records(
    snapshot: SourceSnapshot,
    records: Sequence[dict[str, Any]],
    label: str,
) -> None:
    seen: set[str] = set()
    for record in records:
        relative_path = record["path"]
        _require_repo_relative_path(relative_path)
        folded_path = relative_path.casefold()
        if folded_path in seen:
            raise InventoryError(f"{label} contains a duplicate source path.")
        seen.add(folded_path)
        blob = snapshot.files.get(relative_path)
        if blob is None:
            raise InventoryError(f"{label} references a source outside the exact commit.")
        if record.get("sha256") != hashlib.sha256(blob).hexdigest():
            raise InventoryError(f"{label} source hash does not match the exact commit blob.")


def _validate_inventory_semantics(snapshot: SourceSnapshot, inventory: dict[str, Any]) -> None:
    if inventory["sourceCommit"] != snapshot.source_commit:
        raise InventoryError("Inventory source commit does not match the exact Git snapshot.")

    context_records = inventory["dbContexts"]
    migration_records = inventory["migrations"]
    document_inventory = inventory["documents"]
    durable_records = inventory["durableStores"]
    _require_source_records(snapshot, context_records, "DbContext inventory")
    _require_source_records(snapshot, migration_records, "Migration inventory")
    _require_source_records(snapshot, document_inventory["sourceFiles"], "Documents inventory")
    for adapter in document_inventory["objectStoreAdapters"]:
        _require_repo_relative_path(adapter["path"])
        if adapter["path"] not in snapshot.files:
            raise InventoryError("Documents adapter is outside the exact commit.")
    for record in durable_records:
        _require_source_records(snapshot, record["sourceFiles"], f"Durable store {record['id']}")

    classification_keys = [
        item["configurationKey"] for item in document_inventory["pathClassifications"]
    ]
    if len({key.casefold() for key in classification_keys}) != len(classification_keys):
        raise InventoryError("Documents path classifications duplicate a configuration key.")
    if document_inventory["pathClassifications"] != [
        {"configurationKey": key, "classification": classification}
        for key, classification in DOCUMENT_PATH_CLASSIFICATIONS
    ]:
        raise InventoryError("Documents recovery/transient path classification drifted.")

    physical_keys = [
        (record["schema"].casefold(), record["table"].casefold())
        for record in durable_records
    ]
    if len(physical_keys) != len(set(physical_keys)):
        raise InventoryError("Durable inventory duplicates a physical schema/table.")
    identifiers = [record["id"] for record in durable_records]
    if len(identifiers) != len(set(identifiers)):
        raise InventoryError("Durable inventory duplicates a store id.")

    expected_contexts = _db_context_inventory(snapshot)
    expected_migrations = _migration_inventory(snapshot)
    expected_documents = _documents_inventory(snapshot)
    expected_durable = _durable_store_inventory(snapshot)
    if context_records != expected_contexts:
        raise InventoryError("DbContext inventory does not match the exact commit surface.")
    if migration_records != expected_migrations:
        raise InventoryError("Migration inventory does not match the exact commit surface.")
    if document_inventory != expected_documents:
        raise InventoryError("Documents inventory semantics drifted.")

    expected_identities = {
        (
            spec.identifier,
            spec.module,
            spec.kind,
            spec.db_context,
            spec.entity_type,
            spec.schema,
            spec.table,
            tuple(sorted(spec.contracts)),
        )
        for spec in DURABLE_STORE_SPECS
    }
    actual_identities = {
        (
            record["id"],
            record["module"],
            record["kind"],
            record["dbContext"],
            record["entityType"],
            record["schema"],
            record["table"],
            tuple(record["contracts"]),
        )
        for record in durable_records
    }
    if actual_identities != expected_identities:
        raise InventoryError("Durable store identity semantics drifted.")
    if durable_records != expected_durable:
        raise InventoryError("Durable store evidence does not match the exact commit.")

    expected_counts = {
        "dbContexts": len(expected_contexts),
        "migrations": len(expected_migrations),
        "durableStores": len(expected_durable),
    }
    if inventory["counts"] != expected_counts:
        raise InventoryError("Inventory counts do not match the exact source surface.")


def _source_surface_paths(snapshot: SourceSnapshot) -> tuple[str, ...]:
    backend_prefix = f"{BACKEND_ROOT.as_posix()}/"
    backend_prefix_folded = backend_prefix.casefold()
    build_controls = {path.casefold() for path in REPOSITORY_BUILD_CONTROL_PATHS}
    return tuple(
        sorted(
            path
            for path in snapshot.files
            if path.casefold() in build_controls
            or path.casefold().startswith(backend_prefix_folded)
        )
    )


def _source_surface_fingerprint(snapshot: SourceSnapshot) -> tuple[int, str]:
    paths = _source_surface_paths(snapshot)
    digest = hashlib.sha256()
    for path in paths:
        encoded_path = path.encode("utf-8")
        content = snapshot.files[path]
        digest.update(len(encoded_path).to_bytes(8, "big"))
        digest.update(encoded_path)
        digest.update(len(content).to_bytes(8, "big"))
        digest.update(content)
    return len(paths), digest.hexdigest()


def _require_reviewed_source_surface(snapshot: SourceSnapshot) -> None:
    actual_count, actual_fingerprint = _source_surface_fingerprint(snapshot)
    if (
        actual_count != EXPECTED_SOURCE_SURFACE_FILE_COUNT
        or actual_fingerprint != EXPECTED_SOURCE_SURFACE_FINGERPRINT
    ):
        raise InventoryError(
            "Reviewed C# compilation source surface drifted: "
            f"expected count={EXPECTED_SOURCE_SURFACE_FILE_COUNT} "
            f"fingerprint={EXPECTED_SOURCE_SURFACE_FINGERPRINT}; "
            f"actual count={actual_count} fingerprint={actual_fingerprint}."
        )


def _validate_inventory_for_snapshot(snapshot: SourceSnapshot, inventory: dict[str, Any]) -> None:
    _require_reviewed_source_surface(snapshot)
    _validate_against_schema(inventory, _load_schema(snapshot))
    _validate_inventory_semantics(snapshot, inventory)


def validate_inventory(repository_root: Path, inventory: dict[str, Any]) -> None:
    """Validate an inventory against one verified, immutable commit snapshot."""

    source_commit = inventory.get("sourceCommit")
    if not isinstance(source_commit, str) or COMMIT_PATTERN.fullmatch(source_commit) is None:
        raise InventoryError("Source commit must be a lowercase 40-character Git SHA.")
    snapshot = _load_source_snapshot(repository_root, source_commit)
    _validate_inventory_for_snapshot(snapshot, inventory)


def _build_inventory_from_snapshot(snapshot: SourceSnapshot) -> dict[str, Any]:
    _require_reviewed_source_surface(snapshot)
    db_contexts = _db_context_inventory(snapshot)
    migrations = _migration_inventory(snapshot)
    documents = _documents_inventory(snapshot)
    durable_stores = _durable_store_inventory(snapshot)
    inventory: dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "inventoryMode": INVENTORY_MODE,
        "sourceCommit": snapshot.source_commit,
        "counts": {
            "dbContexts": len(db_contexts),
            "migrations": len(migrations),
            "durableStores": len(durable_stores),
        },
        "dbContexts": db_contexts,
        "migrations": migrations,
        "documents": documents,
        "durableStores": durable_stores,
        "assertions": {
            "sourceOnly": True,
            "containsSecrets": False,
            "containsConfigurationValues": False,
            "selectsProvider": False,
            "performsBackupOrRestore": False,
            "mutatesInfrastructure": False,
            "mutatesData": False,
            "deploys": False,
        },
    }
    _validate_inventory_for_snapshot(snapshot, inventory)
    return inventory


def build_inventory(repository_root: Path, source_commit: str) -> dict[str, Any]:
    """Build and schema-validate the source-only inventory for one exact commit."""

    snapshot = _load_source_snapshot(repository_root, source_commit)
    return _build_inventory_from_snapshot(snapshot)


def canonical_json(inventory: dict[str, Any]) -> str:
    """Serialize an inventory deterministically."""

    return json.dumps(inventory, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


def canonical_bytes(inventory: dict[str, Any]) -> bytes:
    """Serialize deterministic UTF-8 bytes with exactly one LF terminator."""

    return canonical_json(inventory).encode("utf-8")


def _git_environment() -> dict[str, str]:
    environment = os.environ.copy()
    environment["GIT_NO_REPLACE_OBJECTS"] = "1"
    return environment


def _run_git_text(
    repository_root: Path,
    arguments: Sequence[str],
) -> subprocess.CompletedProcess[str]:
    try:
        return subprocess.run(
            ["git", "-C", str(repository_root), *arguments],
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            env=_git_environment(),
        )
    except OSError as exc:
        raise InventoryError("Git is unavailable for exact source identification.") from exc


def _run_git_bytes(
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
        raise InventoryError("Git is unavailable for exact source identification.") from exc


def _require_git_toplevel(repository_root: Path) -> Path:
    try:
        root = repository_root.resolve(strict=True)
    except OSError as exc:
        raise InventoryError("Repository root does not exist.") from exc
    if not root.is_dir():
        raise InventoryError("Repository root is not a directory.")
    result = _run_git_text(root, ("rev-parse", "--show-toplevel"))
    if result.returncode != 0:
        raise InventoryError("Repository root is not inside a Git worktree.")
    try:
        top_level = Path(result.stdout.strip()).resolve(strict=True)
    except OSError as exc:
        raise InventoryError("Git top-level directory cannot be resolved.") from exc
    canonical_root = os.path.normcase(os.path.realpath(str(root)))
    canonical_top = os.path.normcase(os.path.realpath(str(top_level)))
    if canonical_root != canonical_top:
        raise InventoryError("Repository root must be the real Git top-level directory.")
    return root


def _git_source_commit(repository_root: Path) -> str:
    result = _run_git_text(repository_root, ("rev-parse", "--verify", "HEAD^{commit}"))
    commit = result.stdout.strip()
    if result.returncode != 0 or COMMIT_PATTERN.fullmatch(commit) is None:
        raise InventoryError("Exact source commit cannot be resolved.")
    return commit


def _require_inventory_inputs_clean(repository_root: Path) -> None:
    result = _run_git_text(
        repository_root,
        (
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--ignore-submodules=none",
        ),
    )
    if result.returncode != 0:
        raise InventoryError("Recovery inventory inputs cannot be verified as clean.")
    if result.stdout:
        raise InventoryError("Recovery inventory worktree contains tracked or untracked changes.")


def _is_inventory_input_path(relative_path: str) -> bool:
    folded = relative_path.casefold()
    backend = BACKEND_ROOT.as_posix().casefold()
    exact_inputs = {
        *(path.casefold() for path in REPOSITORY_BUILD_CONTROL_PATHS),
        SCHEMA_PATH.as_posix().casefold(),
        GENERATOR_PATH.as_posix().casefold(),
    }
    return folded == backend or folded.startswith(f"{backend}/") or folded in exact_inputs


def _require_no_worktree_source_shadows(
    repository_root: Path,
    committed_files: dict[str, bytes],
) -> None:
    committed = {path.casefold() for path in committed_files}
    backend_root = repository_root / BACKEND_ROOT
    if not backend_root.is_dir():
        raise InventoryError("Recovery inventory backend worktree root is missing.")
    for directory, directory_names, file_names in os.walk(backend_root, followlinks=False):
        directory_names[:] = [
            name for name in directory_names if name.casefold() not in {"bin", "obj"}
        ]
        for name in file_names:
            path = Path(directory, name)
            try:
                relative_path = path.relative_to(repository_root).as_posix()
            except ValueError as exc:
                raise InventoryError("Recovery inventory worktree source path escaped the repository.") from exc
            if relative_path.casefold() not in committed:
                raise InventoryError(
                    f"Recovery inventory worktree contains an uncommitted source shadow: "
                    f"{relative_path}."
                )

    build_controls = {path.casefold() for path in REPOSITORY_BUILD_CONTROL_PATHS}
    for relative_directory in (Path(), Path("src")):
        directory = repository_root / relative_directory
        if not directory.is_dir():
            continue
        for path in directory.iterdir():
            if not path.is_file():
                continue
            relative_path = path.relative_to(repository_root).as_posix()
            if relative_path.casefold() in build_controls and relative_path.casefold() not in committed:
                raise InventoryError(
                    f"Recovery inventory worktree contains an uncommitted build-control shadow: "
                    f"{relative_path}."
                )


def _require_worktree_generator_matches_commit(
    repository_root: Path,
    committed_files: dict[str, bytes],
) -> None:
    relative_path = GENERATOR_PATH.as_posix()
    expected = committed_files.get(relative_path)
    if expected is None:
        raise InventoryError("Exact commit is missing the recovery inventory generator.")
    worktree_path = repository_root / GENERATOR_PATH
    try:
        actual = worktree_path.read_bytes()
    except OSError as exc:
        raise InventoryError(
            "Executed recovery inventory generator bytes cannot be verified."
        ) from exc
    if actual != expected:
        raise InventoryError(
            "Executed recovery inventory generator bytes do not match the exact commit."
        )


def _require_generator_index_flags_clean(repository_root: Path) -> None:
    result = _run_git_text(
        repository_root,
        ("ls-files", "-v", "--", GENERATOR_PATH.as_posix()),
    )
    records = tuple(line for line in result.stdout.splitlines() if line)
    if result.returncode != 0 or len(records) != 1 or not records[0].startswith("H "):
        raise InventoryError(
            "Recovery inventory generator has hidden Git index flags or is not tracked."
        )


def _read_exact_generator_blob(repository_root: Path, source_commit: str) -> bytes:
    result = _run_git_bytes(
        repository_root,
        ("cat-file", "blob", f"{source_commit}:{GENERATOR_PATH.as_posix()}"),
    )
    if result.returncode != 0:
        raise InventoryError("Exact commit recovery inventory generator cannot be read.")
    return result.stdout


def _require_executed_generator_matches_exact_commit(
    repository_root: Path,
    source_commit: str,
) -> None:
    try:
        executed = Path(__file__).resolve(strict=True).read_bytes()
    except OSError as exc:
        raise InventoryError("Executed recovery inventory generator bytes cannot be read.") from exc
    expected = _read_exact_generator_blob(repository_root, source_commit)
    if executed != expected:
        raise InventoryError(
            "Executed recovery inventory generator bytes do not match the exact commit."
        )


def _read_commit_blobs(
    repository_root: Path,
    source_commit: str,
) -> dict[str, bytes]:
    tree = _run_git_bytes(
        repository_root,
        ("ls-tree", "-r", "-z", "--full-tree", source_commit),
    )
    if tree.returncode != 0:
        raise InventoryError("Exact commit tree cannot be enumerated.")

    entries: list[tuple[str, bytes]] = []
    folded_paths: set[str] = set()
    for raw_entry in tree.stdout.split(b"\x00"):
        if not raw_entry:
            continue
        try:
            metadata, raw_path = raw_entry.split(b"\t", 1)
            mode, object_type, object_id = metadata.decode("ascii").split(" ")
            relative_path = raw_path.decode("utf-8")
        except (ValueError, UnicodeError) as exc:
            raise InventoryError("Exact commit tree contains an unsupported path record.") from exc
        _require_repo_relative_path(relative_path, backend_only=False)
        if not _is_inventory_input_path(relative_path):
            continue
        if object_type != "blob" or mode not in {"100644", "100755"}:
            raise InventoryError(f"Inventory input is not a regular Git blob: {relative_path}.")
        folded_path = relative_path.casefold()
        if folded_path in folded_paths:
            raise InventoryError("Exact commit tree contains duplicate or case-colliding paths.")
        folded_paths.add(folded_path)
        entries.append((relative_path, object_id.encode("ascii")))

    if not entries:
        raise InventoryError("Exact commit contains no recovery inventory inputs.")
    request = b"".join(object_id + b"\n" for _, object_id in entries)
    batch = _run_git_bytes(repository_root, ("cat-file", "--batch"), input_bytes=request)
    if batch.returncode != 0:
        raise InventoryError("Exact commit blobs cannot be read.")

    files: dict[str, bytes] = {}
    cursor = 0
    for relative_path, expected_object_id in entries:
        header_end = batch.stdout.find(b"\n", cursor)
        if header_end < 0:
            raise InventoryError("Git blob batch response is truncated.")
        header = batch.stdout[cursor:header_end].split()
        if len(header) != 3 or header[0] != expected_object_id or header[1] != b"blob":
            raise InventoryError("Git blob batch response does not match the exact tree.")
        try:
            size = int(header[2])
        except ValueError as exc:
            raise InventoryError("Git blob batch response has an invalid size.") from exc
        content_start = header_end + 1
        content_end = content_start + size
        if content_end >= len(batch.stdout) or batch.stdout[content_end : content_end + 1] != b"\n":
            raise InventoryError("Git blob batch response is truncated.")
        files[relative_path] = batch.stdout[content_start:content_end]
        cursor = content_end + 1
    if cursor != len(batch.stdout):
        raise InventoryError("Git blob batch response contains unexpected trailing bytes.")
    return files


def _load_source_snapshot(repository_root: Path, source_commit: str) -> SourceSnapshot:
    if COMMIT_PATTERN.fullmatch(source_commit) is None:
        raise InventoryError("Source commit must be a lowercase 40-character Git SHA.")
    root = _require_git_toplevel(repository_root)
    head = _git_source_commit(root)
    resolved = _run_git_text(root, ("rev-parse", "--verify", f"{source_commit}^{{commit}}"))
    if resolved.returncode != 0 or resolved.stdout.strip() != source_commit or head != source_commit:
        raise InventoryError("Source commit must resolve exactly to the current Git HEAD.")
    _require_inventory_inputs_clean(root)
    files = _read_commit_blobs(root, source_commit)
    _require_generator_index_flags_clean(root)
    _require_worktree_generator_matches_commit(root, files)
    _require_no_worktree_source_shadows(root, files)
    _require_inventory_inputs_clean(root)
    if _git_source_commit(root) != source_commit:
        raise InventoryError("Git HEAD changed while exact commit blobs were read.")
    _require_no_worktree_source_shadows(root, files)
    for required_path in (SCHEMA_PATH.as_posix(), GENERATOR_PATH.as_posix()):
        if required_path not in files:
            raise InventoryError(f"Exact commit is missing required inventory input: {required_path}.")
    if not any(path.startswith(f"{BACKEND_ROOT.as_posix()}/") for path in files):
        raise InventoryError("Exact commit backend source root is missing.")
    return SourceSnapshot(root, source_commit, files)


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Generate the strict source-only recovery surface inventory as deterministic JSON."
        )
    )
    parser.add_argument(
        "--repository-root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="Repository root (default: parent of tools/).",
    )
    parser.add_argument(
        "--source-commit",
        help="Exact lowercase 40-character source commit (default: current HEAD).",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _build_parser().parse_args(argv)
    try:
        repository_root = arguments.repository_root.resolve()
        source_commit = arguments.source_commit or _git_source_commit(repository_root)
        if COMMIT_PATTERN.fullmatch(source_commit) is None:
            raise InventoryError("Source commit must be a lowercase 40-character Git SHA.")
        _require_executed_generator_matches_exact_commit(repository_root, source_commit)
        inventory = build_inventory(repository_root, source_commit)
    except InventoryError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2
    except Exception:
        print("ERROR: Recovery source inventory generation failed closed.", file=sys.stderr)
        return 2
    sys.stdout.buffer.write(canonical_bytes(inventory))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
