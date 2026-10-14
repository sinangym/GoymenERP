#!/usr/bin/env python3
"""Generate a deterministic, source-only recovery surface inventory."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any, NamedTuple, Sequence


SCHEMA_VERSION = "goymenerp.recovery-surface-inventory.v1"
INVENTORY_MODE = "SOURCE_ONLY"
SCHEMA_PATH = Path("contracts/recovery/recovery-surface-inventory-v1.schema.json")
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
EXPECTED_OUTBOX_INBOX_TABLES = (
    (
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDbContext.cs",
        "OutboxMessages",
    ),
    (
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDbContext.cs",
        "ObservationInbox",
    ),
    (
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDbContext.cs",
        "OutboxMessages",
    ),
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


class InventoryError(ValueError):
    """A fail-closed source inventory error that is safe to report."""


class SourceEvidence(NamedTuple):
    path: str
    required_tokens: tuple[str, ...]


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


def _evidence(path: str, *tokens: str) -> SourceEvidence:
    return SourceEvidence(path, tuple(tokens))


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
        _evidence(writer_path, *writer_tokens),
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


def _relative_path(repository_root: Path, path: Path) -> str:
    return path.relative_to(repository_root).as_posix()


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    except OSError as exc:
        raise InventoryError(f"Source file cannot be read: {path.name}.") from exc
    return digest.hexdigest()


def _read_source(repository_root: Path, relative_path: str) -> str:
    path = repository_root / Path(relative_path)
    if not path.is_file() or path.is_symlink():
        raise InventoryError(f"Required source file is missing or unsafe: {relative_path}.")
    try:
        return path.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        raise InventoryError(f"Required source file is not readable UTF-8: {relative_path}.") from exc


def _source_file_record(repository_root: Path, relative_path: str) -> dict[str, str]:
    path = repository_root / Path(relative_path)
    if not path.is_file() or path.is_symlink():
        raise InventoryError(f"Required source file is missing or unsafe: {relative_path}.")
    return {"path": relative_path, "sha256": _sha256_file(path)}


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


def _db_context_inventory(repository_root: Path) -> list[dict[str, str]]:
    backend = repository_root / BACKEND_ROOT
    paths = sorted(
        _relative_path(repository_root, path)
        for path in backend.rglob("*DbContext.cs")
        if path.is_file()
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
        content = _read_source(repository_root, relative_path)
        declaration = re.compile(
            rf"\bclass\s+{re.escape(context_name)}\s*\([^)]*\)\s*:\s*DbContext\s*\(",
            re.DOTALL,
        )
        if declaration.search(content) is None:
            raise InventoryError(f"DbContext declaration drifted: {relative_path}.")
        record = _source_file_record(repository_root, relative_path)
        result.append(
            {"name": context_name, "path": record["path"], "sha256": record["sha256"]}
        )
    return result


def _migration_inventory(repository_root: Path) -> list[dict[str, str]]:
    backend = repository_root / BACKEND_ROOT
    migration_paths = sorted(
        path
        for path in backend.rglob("*.cs")
        if path.is_file()
        and "Migrations" in path.parts
        and not path.name.endswith(".Designer.cs")
        and not path.name.endswith("ModelSnapshot.cs")
    )
    paths = [_relative_path(repository_root, path) for path in migration_paths]
    _require_path_set(
        "Migration",
        paths,
        EXPECTED_MIGRATION_COUNT,
        EXPECTED_MIGRATION_PATH_FINGERPRINT,
    )
    for path in migration_paths:
        if MIGRATION_NAME_PATTERN.fullmatch(path.name) is None:
            raise InventoryError(f"Migration filename is not canonical: {path.name}.")
    return [_source_file_record(repository_root, path) for path in paths]


def _require_tokens(content: str, relative_path: str, tokens: Sequence[str]) -> None:
    for token in tokens:
        if token not in content:
            raise InventoryError(f"Required source anchor drifted: {relative_path}.")


def _documents_inventory(repository_root: Path) -> dict[str, Any]:
    module_path = DOCUMENT_SOURCE_PATHS[1]
    module_content = _read_source(repository_root, module_path)
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

    storage_directory = (
        repository_root
        / "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage"
    )
    discovered_adapter_paths = sorted(
        _relative_path(repository_root, path)
        for path in storage_directory.glob("*DocumentObjectStore.cs")
        if path.is_file()
    )
    expected_adapter_paths = sorted(path for _, path in DOCUMENT_ADAPTERS)
    if discovered_adapter_paths != expected_adapter_paths:
        raise InventoryError("Documents object-store adapter surface drifted.")

    adapter_records: list[dict[str, str]] = []
    for adapter_name, relative_path in DOCUMENT_ADAPTERS:
        content = _read_source(repository_root, relative_path)
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

    runtime_content = _read_source(repository_root, DOCUMENT_SOURCE_PATHS[0])
    _require_tokens(
        runtime_content,
        DOCUMENT_SOURCE_PATHS[0],
        (
            "AddDocumentsRuntime",
            "allowDevelopmentStorageAndScanner",
            "Documents:ObjectStore:RootPath",
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
    readme = " ".join(_read_source(repository_root, readme_path).split())
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
            {
                "configurationKey": "Documents:ObjectStore:RootPath",
                "classification": "RECOVERY_SET_CONTENT",
            },
            {
                "configurationKey": "Documents:Scanner:Defender:QuarantinePath",
                "classification": "TRANSIENT_EXCLUDED",
            },
        ],
        "sourceFiles": [
            _source_file_record(repository_root, path) for path in DOCUMENT_SOURCE_PATHS
        ],
    }


def _discover_outbox_inbox_tables(repository_root: Path) -> tuple[tuple[str, str], ...]:
    tables: set[tuple[str, str]] = set()
    for path in (repository_root / BACKEND_ROOT).rglob("*DbContext.cs"):
        if not path.is_file():
            continue
        relative_path = _relative_path(repository_root, path)
        content = _read_source(repository_root, relative_path)
        for table in re.findall(r'ToTable\(\s*"([^"]+)"', content):
            lowered = table.lower()
            if "outbox" in lowered or "inbox" in lowered:
                tables.add((relative_path, table))
    return tuple(sorted(tables))


def _discover_idempotency_key_tables(
    repository_root: Path,
) -> tuple[tuple[str, str], ...]:
    entity_pattern = re.compile(
        r'modelBuilder\.Entity\("[^"]+", b =>\s*\{(.*?)\n\s*\}\);',
        re.DOTALL,
    )
    table_pattern = re.compile(r'b\.ToTable\("([^"]+)",\s*"([^"]+)"')
    tables: set[tuple[str, str]] = set()
    snapshot_paths = sorted(
        path
        for path in (repository_root / BACKEND_ROOT).rglob("*ModelSnapshot.cs")
        if path.is_file()
    )
    for path in snapshot_paths:
        relative_path = _relative_path(repository_root, path)
        content = _read_source(repository_root, relative_path)
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


def _require_discovered_durable_surfaces(repository_root: Path) -> None:
    discovered_idempotency = _discover_idempotency_key_tables(repository_root)
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


def _durable_store_inventory(repository_root: Path) -> list[dict[str, Any]]:
    modules = repository_root / "src/backend/Modules"
    discovered_contracts = tuple(
        sorted(
            _relative_path(repository_root, path)
            for path in modules.rglob("*OutboxV1.cs")
            if path.is_file()
        )
    )
    if discovered_contracts != EXPECTED_OUTBOX_CONTRACT_PATHS:
        raise InventoryError("Durable outbox contract surface drifted.")

    discovered_tables = _discover_outbox_inbox_tables(repository_root)
    if discovered_tables != EXPECTED_OUTBOX_INBOX_TABLES:
        raise InventoryError("Durable outbox/inbox table surface drifted.")
    _require_discovered_durable_surfaces(repository_root)

    physical_keys = [(spec.schema, spec.table) for spec in DURABLE_STORE_SPECS]
    if len(physical_keys) != len(set(physical_keys)):
        raise InventoryError("Durable-store specifications duplicate a physical table.")

    records: list[dict[str, Any]] = []
    for spec in DURABLE_STORE_SPECS:
        source_files: list[dict[str, str]] = []
        for evidence in sorted(spec.evidence, key=lambda item: item.path):
            content = _read_source(repository_root, evidence.path)
            _require_tokens(content, evidence.path, evidence.required_tokens)
            source_files.append(_source_file_record(repository_root, evidence.path))
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


def _load_schema(repository_root: Path) -> dict[str, Any]:
    relative_path = SCHEMA_PATH.as_posix()
    text = _read_source(repository_root, relative_path)
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


def _validate_against_schema(value: Any, schema: dict[str, Any], location: str = "$") -> None:
    unsupported = set(schema) - SUPPORTED_SCHEMA_KEYWORDS
    if unsupported:
        raise InventoryError(
            f"Recovery inventory schema uses unsupported keywords at {location}."
        )

    if "const" in schema and value != schema["const"]:
        raise InventoryError(f"Recovery inventory violates const at {location}.")
    if "enum" in schema and value not in schema["enum"]:
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
            canonical = [
                json.dumps(item, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
                for item in value
            ]
            if len(canonical) != len(set(canonical)):
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


def validate_inventory(repository_root: Path, inventory: dict[str, Any]) -> None:
    """Validate an inventory against the committed schema using only stdlib."""

    _validate_against_schema(inventory, _load_schema(repository_root))


def build_inventory(repository_root: Path, source_commit: str) -> dict[str, Any]:
    """Build and schema-validate the source-only inventory for one exact commit."""

    root = repository_root.resolve()
    if COMMIT_PATTERN.fullmatch(source_commit) is None:
        raise InventoryError("Source commit must be a lowercase 40-character Git SHA.")
    if not (root / BACKEND_ROOT).is_dir():
        raise InventoryError("Repository backend source root is missing.")

    db_contexts = _db_context_inventory(root)
    migrations = _migration_inventory(root)
    documents = _documents_inventory(root)
    durable_stores = _durable_store_inventory(root)
    inventory: dict[str, Any] = {
        "schemaVersion": SCHEMA_VERSION,
        "inventoryMode": INVENTORY_MODE,
        "sourceCommit": source_commit,
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
    validate_inventory(root, inventory)
    return inventory


def canonical_json(inventory: dict[str, Any]) -> str:
    """Serialize an inventory deterministically."""

    return json.dumps(inventory, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


def _run_git(repository_root: Path, arguments: Sequence[str]) -> subprocess.CompletedProcess[str]:
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


def _git_source_commit(repository_root: Path) -> str:
    result = _run_git(repository_root, ("rev-parse", "--verify", "HEAD^{commit}"))
    commit = result.stdout.strip().lower()
    if result.returncode != 0 or COMMIT_PATTERN.fullmatch(commit) is None:
        raise InventoryError("Exact source commit cannot be resolved.")
    return commit


def _require_tracked_backend_clean(repository_root: Path) -> None:
    result = _run_git(
        repository_root,
        ("diff", "--quiet", "HEAD", "--", BACKEND_ROOT.as_posix()),
    )
    if result.returncode == 1:
        raise InventoryError("Tracked backend inventory inputs differ from the exact source commit.")
    if result.returncode != 0:
        raise InventoryError("Tracked backend inventory inputs cannot be verified.")


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
        _require_tracked_backend_clean(repository_root)
        inventory = build_inventory(repository_root, source_commit)
    except InventoryError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2
    except Exception:
        print("ERROR: Recovery source inventory generation failed closed.", file=sys.stderr)
        return 2
    sys.stdout.write(canonical_json(inventory))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
