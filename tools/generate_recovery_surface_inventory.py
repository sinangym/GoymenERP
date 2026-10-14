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

EXPECTED_DB_CONTEXT_COUNT = 18
EXPECTED_DB_CONTEXT_PATH_FINGERPRINT = (
    "5fb71e920f1d038f7403a0e232717c2204bdeaabe204298c0dbadd603f22d56e"
)
EXPECTED_MIGRATION_COUNT = 91
EXPECTED_MIGRATION_PATH_FINGERPRINT = (
    "97073d908c46c8ba30de730a1a39c231512bfbf2f666fe1a9c484f6bada2bea3"
)
EXPECTED_DURABLE_STORE_COUNT = 20
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
MIGRATION_NAME_PATTERN = re.compile(r"^[0-9]{14}_[A-Za-z0-9]+\.cs$")

DOCUMENT_CONFIGURATION_KEYS = (
    "Documents:MaximumUploadBytes",
    "Documents:ObjectStore:RootPath",
    "Documents:Scanner:Defender:ExecutablePath",
    "Documents:Scanner:Defender:MaximumConcurrentScans",
    "Documents:Scanner:Defender:QuarantinePath",
    "Documents:Scanner:Defender:TimeoutSeconds",
    "Documents:Scanner:Mode",
)
DOCUMENT_SOURCE_PATHS = (
    "src/backend/GoymenERP.Api/DocumentsRuntime/DocumentsRuntimeExtensions.cs",
    "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/DocumentsModuleExtensions.cs",
    "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/FileSystemDocumentObjectStore.cs",
    "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs",
    "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Scanning/WindowsDefenderDocumentScanner.cs",
    "src/backend/Modules/Documents/README.md",
)
DOCUMENT_ADAPTERS = (
    (
        "FileSystemDocumentObjectStore",
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/FileSystemDocumentObjectStore.cs",
    ),
    (
        "ServerDirectoryDocumentObjectStore",
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs",
    ),
)
DOCUMENT_OBJECT_KEY_PATTERN = "^companies/[0-9a-f]{32}/sha256/[0-9a-f]{64}$"
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
    ("manufacturing", "Commands"),
    ("product", "PriceListAttempts"),
    ("purchasing", "PurchaseAcceptanceShares"),
    ("purchasing", "PurchaseAttempts"),
    ("sales", "DraftAttempts"),
)
METADATA_ONLY_IDEMPOTENCY_KEY_TABLES = (("audit", "AuditEvents"),)
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
            quote_count = 1
            while index + quote_count < length and content[index + quote_count] == '"':
                quote_count += 1
            if quote_count >= 3:
                delimiter = '"' * quote_count
                end = content.find(delimiter, index + quote_count)
                if end < 0:
                    raise InventoryError("C# source contains an unterminated raw string.")
                end += quote_count
                result.append(content[index:end])
                index = end
                continue

            verbatim = index > 0 and content[index - 1] == "@"
            start = index
            index += 1
            while index < length:
                if verbatim and content.startswith('""', index):
                    index += 2
                    continue
                if content[index] == '"':
                    index += 1
                    break
                if not verbatim and content[index] == "\\" and index + 1 < length:
                    index += 2
                else:
                    index += 1
            result.append(content[start:index])
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
        verbatim = delimiter == '"' and index > 0 and content[index - 1] == "@"
        quote_count = 1
        if delimiter == '"':
            while index + quote_count < len(content) and content[index + quote_count] == '"':
                quote_count += 1
        if delimiter == '"' and quote_count >= 3:
            raw_delimiter = '"' * quote_count
            end = content.find(raw_delimiter, index + quote_count)
            if end < 0:
                raise InventoryError("C# source contains an unterminated raw string.")
            index = end + quote_count
            result.append(_blank_csharp_region(content[start:index]))
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
        else:
            raise InventoryError("C# source contains an unterminated string or character literal.")
        result.append(_blank_csharp_region(content[start:index]))
    return "".join(result)


def _mask_literal_false_preprocessor(content: str) -> str:
    frames: list[list[bool]] = []
    result: list[str] = []
    directive_pattern = re.compile(r"^\s*#\s*(if|elif|else|endif)\b(.*)$", re.IGNORECASE)
    for line in content.splitlines(keepends=True):
        directive = directive_pattern.match(line)
        if directive is not None:
            command = directive.group(1).casefold()
            expression = directive.group(2).strip()
            literal_false = expression.strip("()").strip().casefold() == "false"
            if command == "if":
                frames.append([literal_false, literal_false])
            elif command == "elif" and frames:
                if frames[-1][0]:
                    frames[-1][1] = literal_false
            elif command == "else" and frames:
                if frames[-1][0]:
                    frames[-1][1] = False
            elif command == "endif" and frames:
                frames.pop()
            result.append(_blank_csharp_region(line))
            continue
        if any(frame[1] for frame in frames):
            result.append(_blank_csharp_region(line))
        else:
            result.append(line)
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
    without_disabled_directives = _mask_literal_false_preprocessor(without_literals)
    return _mask_literal_false_if_blocks(without_disabled_directives)


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


def _path_set_fingerprint(paths: Sequence[str]) -> str:
    canonical = "".join(f"{path}\n" for path in sorted(paths)).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def _require_path_set(
    label: str,
    paths: Sequence[str],
    expected_count: int,
    expected_fingerprint: str,
) -> None:
    actual_count = len(paths)
    actual_fingerprint = _path_set_fingerprint(paths)
    if actual_count != expected_count or actual_fingerprint != expected_fingerprint:
        raise InventoryError(
            f"{label} surface drifted: expected count={expected_count} "
            f"fingerprint={expected_fingerprint}; actual count={actual_count} "
            f"fingerprint={actual_fingerprint}."
        )


def _db_context_inventory(snapshot: SourceSnapshot) -> list[dict[str, str]]:
    paths = sorted(
        path
        for path in snapshot.files
        if path.startswith(f"{BACKEND_ROOT.as_posix()}/") and path.endswith("DbContext.cs")
    )
    _require_path_set(
        "DbContext",
        paths,
        EXPECTED_DB_CONTEXT_COUNT,
        EXPECTED_DB_CONTEXT_PATH_FINGERPRINT,
    )

    result: list[dict[str, str]] = []
    for relative_path in paths:
        context_name = Path(relative_path).stem
        _require_repo_relative_path(relative_path)
        content = _read_csharp(snapshot, relative_path)
        declaration = re.compile(
            rf"\bclass\s+{re.escape(context_name)}\s*\([^)]*\)\s*:\s*DbContext\s*\(",
            re.DOTALL,
        )
        if declaration.search(content) is None:
            raise InventoryError(f"DbContext declaration drifted: {relative_path}.")
        record = _source_file_record(snapshot, relative_path)
        result.append(
            {"name": context_name, "path": record["path"], "sha256": record["sha256"]}
        )
    return result


def _migration_inventory(snapshot: SourceSnapshot) -> list[dict[str, str]]:
    paths = sorted(
        path
        for path in snapshot.files
        if path.startswith(f"{BACKEND_ROOT.as_posix()}/")
        and path.endswith(".cs")
        and "Migrations" in PurePosixPath(path).parts
        and not path.endswith(".Designer.cs")
        and not path.endswith("ModelSnapshot.cs")
    )
    _require_path_set(
        "Migration",
        paths,
        EXPECTED_MIGRATION_COUNT,
        EXPECTED_MIGRATION_PATH_FINGERPRINT,
    )
    for path in paths:
        _require_repo_relative_path(path)
        name = PurePosixPath(path).name
        if MIGRATION_NAME_PATTERN.fullmatch(name) is None:
            raise InventoryError(f"Migration filename is not canonical: {name}.")
    return [_source_file_record(snapshot, path) for path in paths]


def _require_tokens(content: str, relative_path: str, tokens: Sequence[str]) -> None:
    for token in tokens:
        if token not in content:
            raise InventoryError(f"Required source anchor drifted: {relative_path}.")


def _documents_inventory(snapshot: SourceSnapshot) -> dict[str, Any]:
    module_path = DOCUMENT_SOURCE_PATHS[1]
    module_content = _read_csharp(snapshot, module_path)
    discovered_keys = tuple(
        sorted(
            re.findall(
                r'public const string [A-Za-z][A-Za-z0-9]*Key\s*=\s*"(Documents:[^"]+)";',
                module_content,
            )
        )
    )
    if discovered_keys != DOCUMENT_CONFIGURATION_KEYS:
        raise InventoryError("Documents configuration-key surface drifted.")

    storage_directory = PurePosixPath(
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage"
    )
    discovered_adapter_paths = sorted(
        path
        for path in snapshot.files
        if PurePosixPath(path).parent == storage_directory
        and PurePosixPath(path).name.endswith("DocumentObjectStore.cs")
    )
    expected_adapter_paths = sorted(path for _, path in DOCUMENT_ADAPTERS)
    if discovered_adapter_paths != expected_adapter_paths:
        raise InventoryError("Documents object-store adapter surface drifted.")

    adapter_records: list[dict[str, str]] = []
    for adapter_name, relative_path in DOCUMENT_ADAPTERS:
        content = _read_csharp(snapshot, relative_path)
        declaration = re.compile(
            rf"\bclass\s+{re.escape(adapter_name)}\b[^:]*:\s*IDocumentObjectStoreV1\b",
            re.DOTALL,
        )
        if declaration.search(content) is None:
            raise InventoryError(f"Documents adapter declaration drifted: {relative_path}.")
        _require_tokens(
            content,
            relative_path,
            (
                DOCUMENT_OBJECT_KEY_PATTERN,
                "DocumentContentHash.Compute",
            ),
        )
        adapter_records.append({"name": adapter_name, "path": relative_path})

    runtime_content = _read_csharp(snapshot, DOCUMENT_SOURCE_PATHS[0])
    _require_tokens(
        runtime_content,
        DOCUMENT_SOURCE_PATHS[0],
        (
            "AddDocumentsRuntime",
            "allowDevelopmentStorageAndScanner",
            "services.AddDocumentsModule(",
        ),
    )
    _require_tokens(
        module_content,
        module_path,
        (
            "ServerDirectoryDocumentObjectStore.Open",
            "new FileSystemDocumentObjectStore",
            "DefenderQuarantinePathKey",
        ),
    )

    readme_path = DOCUMENT_SOURCE_PATHS[-1]
    scanner_path = DOCUMENT_SOURCE_PATHS[4]
    scanner = _read_csharp(snapshot, scanner_path)
    live_scanner = _live_csharp_code(scanner)
    _require_tokens(
        live_scanner,
        scanner_path,
        (
            "private static void TryDelete(string path)",
        ),
    )
    scan_body = _live_csharp_code(_csharp_braced_body(
        scanner,
        r"\bpublic\s+async\s+Task<DocumentScanVerdictV1>\s+ScanAsync\s*\(",
        scanner_path,
    ))
    if "var copy = Path.Combine(options.QuarantineDirectory" not in scan_body:
        raise InventoryError(f"Documents quarantine-copy lifecycle drifted: {scanner_path}.")
    cleanup = re.compile(
        r"finally\s*\{\s*TryDelete\(copy\);\s*gate\.Release\(\);\s*\}",
        re.DOTALL,
    )
    if cleanup.search(scan_body) is None:
        raise InventoryError(f"Documents transient cleanup lifecycle drifted: {scanner_path}.")
    try_delete_body = _live_csharp_code(_csharp_braced_body(
        scanner,
        r"\bprivate\s+static\s+void\s+TryDelete\s*\(\s*string\s+path\s*\)",
        scanner_path,
    ))
    delete_sequence = re.compile(
        r"\bif\s*\(\s*File\.Exists\(path\)\s*\)\s*\{\s*File\.Delete\(path\);\s*\}",
        re.DOTALL,
    )
    if delete_sequence.search(try_delete_body) is None:
        raise InventoryError(f"Documents transient deletion implementation drifted: {scanner_path}.")

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
            _source_file_record(snapshot, path) for path in DOCUMENT_SOURCE_PATHS
        ],
    }


def _discover_message_delivery_tables(snapshot: SourceSnapshot) -> tuple[tuple[str, str], ...]:
    tables: set[tuple[str, str]] = set()
    context_paths = sorted(path for path in snapshot.files if path.endswith("DbContext.cs"))
    table_pattern = re.compile(r'ToTable\(\s*"(?P<table>[^"]+)"')
    for relative_path in context_paths:
        _require_repo_relative_path(relative_path)
        content = _read_csharp(snapshot, relative_path)
        schema_match = re.search(r'\bconst\s+string\s+Schema\s*=\s*"([^"]+)"\s*;', content)
        for match in table_pattern.finditer(content):
            table = match.group("table")
            lowered = table.lower()
            if not any(marker in lowered for marker in ("outbox", "inbox", "deliver")):
                continue
            tail = content[match.end() :]
            explicit_schema = re.match(r'\s*,\s*"([^"]+)"', tail)
            schema_constant = re.match(r"\s*,\s*Schema\b", tail)
            default_schema = re.match(r"\s*(?:\)|,\s*[A-Za-z][A-Za-z0-9]*\s*=>)", tail)
            if explicit_schema is not None:
                schema = explicit_schema.group(1)
            elif schema_constant is not None or default_schema is not None:
                if schema_match is None:
                    raise InventoryError(f"Message/delivery schema cannot be resolved: {relative_path}.")
                schema = schema_match.group(1)
            else:
                raise InventoryError(f"Message/delivery mapping shape drifted: {relative_path}.")
            tables.add((schema, table))
    return tuple(sorted(tables))


def _discover_idempotency_key_tables(snapshot: SourceSnapshot) -> tuple[tuple[str, str], ...]:
    entity_pattern = re.compile(
        r'modelBuilder\.Entity\("[^"]+", b =>\s*\{(.*?)\n\s*\}\);',
        re.DOTALL,
    )
    table_pattern = re.compile(r'b\.ToTable\("([^"]+)",\s*"([^"]+)"')
    tables: set[tuple[str, str]] = set()
    snapshot_paths = sorted(path for path in snapshot.files if path.endswith("ModelSnapshot.cs"))
    for relative_path in snapshot_paths:
        _require_repo_relative_path(relative_path)
        content = _read_csharp(snapshot, relative_path)
        for body in entity_pattern.findall(content):
            if "IdempotencyKeyHash" not in body:
                continue
            match = table_pattern.search(body)
            if match is None:
                raise InventoryError(
                    f"Idempotency-key snapshot mapping drifted: {relative_path}."
                )
            table, schema = match.groups()
            tables.add((schema, table))
    return tuple(sorted(tables))


def _require_discovered_durable_surfaces(snapshot: SourceSnapshot) -> None:
    discovered_idempotency = _discover_idempotency_key_tables(snapshot)
    if discovered_idempotency != EXPECTED_IDEMPOTENCY_KEY_TABLES:
        raise InventoryError("Durable idempotency-key table surface drifted.")

    metadata_only = set(METADATA_ONLY_IDEMPOTENCY_KEY_TABLES)
    if not metadata_only.issubset(discovered_idempotency):
        raise InventoryError("Metadata-only idempotency-key classification drifted.")
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

    discovered_message_tables = set(_discover_message_delivery_tables(snapshot))
    excluded_business_tables = set(NON_MESSAGE_DELIVERY_TABLES)
    if not excluded_business_tables.issubset(discovered_message_tables):
        raise InventoryError("Non-message delivery classification drifted.")
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

    physical_keys = [(spec.schema, spec.table) for spec in DURABLE_STORE_SPECS]
    if len(physical_keys) != len(set(physical_keys)):
        raise InventoryError("Durable-store specifications duplicate a physical table.")

    records: list[dict[str, Any]] = []
    for spec in DURABLE_STORE_SPECS:
        source_files: list[dict[str, str]] = []
        for evidence in sorted(spec.evidence, key=lambda item: item.path):
            content = _read_csharp(snapshot, evidence.path)
            anchor_content = _live_csharp_code(content) if evidence.live_code else content
            _require_tokens(anchor_content, evidence.path, evidence.required_tokens)
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


def _validate_inventory_for_snapshot(snapshot: SourceSnapshot, inventory: dict[str, Any]) -> None:
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
            "--",
            *INVENTORY_INPUT_PATHS,
        ),
    )
    if result.returncode != 0:
        raise InventoryError("Recovery inventory inputs cannot be verified as clean.")
    if result.stdout:
        raise InventoryError("Recovery inventory inputs contain tracked or untracked worktree changes.")


def _read_commit_blobs(
    repository_root: Path,
    source_commit: str,
) -> dict[str, bytes]:
    tree = _run_git_bytes(
        repository_root,
        ("ls-tree", "-r", "-z", "--full-tree", source_commit, "--", *INVENTORY_INPUT_PATHS),
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
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _build_parser().parse_args(argv)
    try:
        repository_root = arguments.repository_root.resolve()
        source_commit = _git_source_commit(repository_root)
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
