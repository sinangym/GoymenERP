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

EXPECTED_DB_CONTEXT_COUNT = 19
EXPECTED_DB_CONTEXT_DECLARATION_FINGERPRINT = (
    "3d62237456b4c9d291ced56872b2748d1ce53eed7f6d6164f8cc63e406eca09c"
)
# Tren-1 W8 source closure (3e85cbbb1): exact producer measurements only; K3 review remains pending.
EXPECTED_MIGRATION_COUNT = 340
EXPECTED_MIGRATION_DECLARATION_FINGERPRINT = (
    "3a0f712509f11c2648f54b5cba036561613286c10e36de27ea7a23c682d68edc"
)
# tren-1 wave 3 integration: +7 durable stores from platform/alis_cari/uretim/finans/kimlik and
# +1 from stok_lojistik (product.profile-weight-attempts); every new classification is
# K3 review pending (owning team) until an independent K3 review signs it.
# tren-1 wave 5 integration: +7 durable stores (finans: credit-account and opening-balance
# commands; stok_lojistik: logistics.outbox, weighing-difference and package-standard attempts;
# alis_cari: supplier-return attempts; satis: subcontract-service-invoice attempts); every new
# classification is K3 review pending (owning team) until an independent K3 review signs it.
# W8 FIN-S11: the actual Finance outbox is inventoried below; classification remains K3 pending.
EXPECTED_DURABLE_STORE_COUNT = 66
# Recovery pin Faz 1 (docs/delivery/RECOVERY_PIN_CAKISMA_ONERISI_v1.md, Sinan Q1 = yes, Issue #66):
# the former global byte pin over every src/backend byte is replaced by a NARROW risky-surface
# pin.  The narrow set is a token/file-class SUPERSET (never an inheritance guess) of the inputs
# that can make a lexical inventory miss a linked file, generator, global alias, symbol-shadow
# file, hidden SqlClient/DbContext use or a partial of a reviewed DbContext/store type:
#   * every REPOSITORY_BUILD_CONTROL_PATHS file,
#   * every non-Markdown, non-.cs file under src/backend (*.csproj, *.props, *.targets, *.rsp,
#     *.csproj.user, appsettings*.json, *.resx...: MSBuild auto-imports and embedded inputs),
#   * every src/backend .cs whose comment-free text (literals and every preprocessor region kept,
#     so wider than live code) contains a RISKY_SOURCE_TOKENS token, a DbContext/object-store
#     adapter name or the name of a global alias whose target is risky,
#   * every `using X =` alias file (global and file-local) and every `global using` file,
#   * every reviewed path (REVIEWED_LIVE_SOURCE_SHA256, reviewed methods, durable-store and
#     Documents evidence) and every discovered DbContext/Migration/object-store declaration file,
#   * every file declaring a type named like a reviewed partial DbContext/store/adapter type,
#   * every file declaring a class/record used as the first base of a risky declaration, and every
#     file with several namespace declarations,
#   * a shadow-declaration index (path, namespace, name) of every type declared in a namespace that
#     equals or encloses a risky file's namespace (the global namespace encloses all) whose simple
#     name occurs in such a risky file.
# Measurement note (one-time, NOT re-run by CI): over the last 21 origin/main commits touching
# src/backend/ up to 1e694aa3e (8310d5cba .. 1e694aa3e, 2026-11-02..07), 18 of 21 would still have
# moved this narrow pin (16 add a migration or edit a DbContext, the others edit Program.cs or an
# EF-using service); 3 (one K2 Manufacturing internal-method change and two README-only K1
# commits) would not.  Reproduce: `git log --format=%h 8310d5cba^..1e694aa3e -- src/backend`, then
# for each commit and its parent run
# `python3 -I .claude/skills/recovery-inventory-repin/scripts/print_recovery_actuals.py` in a
# clean worktree and compare the risky-surface fingerprint.
# Known limits (documented, not closed): a type name assembled by a full string split is
# invisible to the lexical token scan (reflection by concatenated string); delegate types are in
# the shadow index.
# The full src/backend byte digest is still computed and emitted as `sourceSurface` in the
# inventory (and copied into the release manifest).  The manifest sourceSurface is post-hoc
# evidence, not a merge block: it is never compared with a pinned constant.
EXPECTED_RISKY_SOURCE_SURFACE_FILE_COUNT = 1461
EXPECTED_RISKY_SOURCE_SURFACE_FINGERPRINT = (
    '9af854faccd37fccca382a68dd8d6316d4d00824fbd438ec91b3784243ac9854'
)
RISKY_SOURCE_TOKENS = (
    "DbContext",
    "ModelBuilder",
    "MigrationBuilder",
    "Migration",
    "IDocumentObjectStoreV1",
    "SqlConnection",
    "SqlCommand",
    "SqlClient",
    "Microsoft.Data.SqlClient",
    "Microsoft.EntityFrameworkCore",
    "ToTable",
    "ExecuteSql",
    "FromSql",
    "SqlQuery",
    "DbConnection",
    "DbCommand",
    "System.Data",
    "InternalsVisibleTo",
    "ModuleInitializer",
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
    "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/CounterpartyPartyDeactivatedOutboxV1.cs",
    "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/CounterpartyPartyMergedOutboxV1.cs",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceFinancialMovementOutboxV1.cs",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryCustomerReturnOutboxV1.cs",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryDefiniteShipmentOutboxV1.cs",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventorySupplierReturnExitOutboxV1.cs",
    "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Application/Outbox/LogisticsDefiniteShipmentOutboxV1.cs",
)
EXPECTED_IDEMPOTENCY_KEY_TABLES = (
    ('accounting', 'Commands'),
    ('accounting', 'SourceDocumentAdmissions'),
    ('audit', 'AuditEvents'),
    ('documents', 'DocumentAttempts'),
    ('documents', 'DocumentBrandingAttempts'),
    ('finance', 'CashAccountCommands'),
    ('finance', 'CashTransferCommands'),
    ('finance', 'Commands'),
    ('finance', 'CreditAccountCommands'),
    ('finance', 'CustomerReturnCredits'),
    ('finance', 'ExpensePaymentCommands'),
    ('finance', 'InvoiceCancellations'),
    ('finance', 'OfficialInvoiceImportCommands'),
    ('finance', 'OfficialSalesInvoiceCommands'),
    ('finance', 'OpenItemSourceBatches'),
    ('finance', 'OpeningBalanceCommands'),
    ('finance', 'PartyCollateralCommands'),
    ('finance', 'PartyCreditLimitCommands'),
    ('idempotency', 'CommandExecutions'),
    ('idempotency', 'GroupSharedCommandTerminals'),
    ('inventory', 'Commands'),
    ('logistics', 'CustomerReturnAttempts'),
    ('logistics', 'LoadingAttempts'),
    ('logistics', 'TransferDepartureAttempts'),
    ('logistics', 'TransferOutgoingDocumentAttempts'),
    ('logistics', 'WeighingDifferenceAttempts'),
    ('manufacturing', 'Commands'),
    ('organization', 'CompanyOpeningExecutions'),
    ('product', 'PaintMembershipAttempts'),
    ('product', 'PriceListAttempts'),
    ('product', 'ProductDimensionAttempts'),
    ('product', 'ProductItemTypeAttempts'),
    ('product', 'ProductPackageStandardAttempts'),
    ('product', 'ProductProfileWeightAttempts'),
    ('purchasing', 'IncomingInvoiceAttempts'),
    ('purchasing', 'InvoiceImportReservations'),
    ('purchasing', 'PurchaseAcceptanceShares'),
    ('purchasing', 'PurchaseAttempts'),
    ('purchasing', 'PurchaseGoodsReceiptBatchAttempts'),
    ('purchasing', 'PurchaseSupplierReturnAttempts'),
    ('sales', 'ContractAttempts'),
    ('sales', 'CustomerReturnAttempts'),
    ('sales', 'DraftAttempts'),
    ('sales', 'InvoiceAttempts'),
    ('sales', 'QuoteAttempts'),
    ('sales', 'RequestAttempts'),
    ('sales', 'ShipmentRiskExceptionAttempts'),
    ('sales', 'SubcontractInvoiceCommandAttempts'),
    ('sales', 'SubcontractServiceInvoiceAttempts'),
)
METADATA_ONLY_IDEMPOTENCY_KEY_TABLES = (("audit", "AuditEvents"),)
AUDIT_SOURCE_ROOT = "src/backend/Modules/Audit/"
AUDIT_LIVE_SOURCE_EXCLUDED_SUFFIXES = (".designer.cs", ".g.cs", ".generated.cs")
EXPECTED_AUDIT_LIVE_SOURCE_FILE_COUNT = 42
EXPECTED_AUDIT_LIVE_SOURCE_FINGERPRINT = (
    '5c2a38e67872cae375351d148756ad7b233fef3fbe805c20f9db18d1e3416a4e'
)
EXPECTED_MESSAGE_DELIVERY_TABLES = (
    ('accounting', 'Deliveries'),
    ('counterparty', 'OutboxMessages'),
    ('finance', 'OfficialInvoiceImportWorkDeliveries'),
    ('finance', 'OutboxMessages'),
    ('identity', 'CompanySetupInboxReceipts'),
    ('identity', 'OutboxMessages'),
    ('inventory', 'OutboxMessages'),
    ('logistics', 'OutboxMessages'),
    ('organization', 'CompanySetupInboxReceipts'),
    ('organization', 'CompanySetupOutboxMessages'),
    ('purchasing', 'OutboxMessages'),
    ('sales', 'OfficialInvoiceCancellationDeliveries'),
    ('sales', 'OutboxMessages'),
    ('work', 'NotificationInbox'),
    ('work', 'ObservationInbox'),
    ('work', 'OutboxDeliveries'),
    ('work', 'OutboxMessages'),
    ('work', 'OutboxSourceCursors'),
)
NON_MESSAGE_DELIVERY_TABLES = (
    # A3 API-30 (ALS-D33): append-only purchase order line delivery slices (business records; "Delivery" is in the name only).
    ("purchasing", "PurchaseOrderLineDeliverySlices"),
    ("sales", "DraftDeliveryScheduleDates"),
    ("sales", "DraftDeliverySchedules"),
    ("sales", "DraftDeliverySlices"),
    # SAT-D30 / SAT-D213: append-only over-delivery approvals (business records; "Delivery" is in the name only).
    ("sales", "DraftOverDeliveryApprovals"),
    # F-029 S4b: append-only Sales delivery tolerance policy versions in Settings (business records; "Delivery" is in the name only).
    ("settings", "SalesDeliveryToleranceVersions"),
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
ACCOUNTING_SOURCE_DOCUMENT_ADMISSION_STORE = (
    "src/backend/Modules/Accounting/"
    "GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingSourceDocumentAdmissionStoreV1.cs"
)
DOCUMENTS_CONTEXT = (
    "src/backend/Modules/Documents/"
    "GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentsDbContext.cs"
)
DOCUMENTS_STORE = (
    "src/backend/Modules/Documents/"
    "GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentsStore.cs"
)
COUNTERPARTY_CONTEXT = (
    "src/backend/Modules/Counterparty/"
    "GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyDbContext.cs"
)
FINANCE_CONTEXT = (
    "src/backend/Modules/Finance/"
    "GoymenERP.Modules.Finance.Infrastructure/Persistence/FinanceDbContext.cs"
)
IDEMPOTENCY_ROOT = (
    "src/backend/Modules/Idempotency/"
    "GoymenERP.Modules.Idempotency.Infrastructure/Persistence"
)
IDENTITY_CONTEXT = (
    "src/backend/Modules/Identity/"
    "GoymenERP.Modules.Identity.Infrastructure/Persistence/IdentityDbContext.cs"
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
    # W8 actual notification inbox and immutable official-cancellation delivery histories; K3 pending.
    DurableStoreSpec(
        'sales.official-invoice-cancellation-deliveries', 'Sales', 'delivery', 'SalesDbContext', 'OfficialInvoiceCancellationDeliveryRow', 'sales', 'OfficialInvoiceCancellationDeliveries', (),
        (
            _evidence('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs', 'Schema = "sales"'),
            _evidence('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.OfficialCancellations.cs', 'Entity<OfficialInvoiceCancellationDeliveryRow>()', 'ToTable("OfficialInvoiceCancellationDeliveries", Schema', 'HasKey'),
            _evidence('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117195343_AddSalesOfficialInvoiceCancellation.cs', 'name: "OfficialInvoiceCancellationDeliveries"'),
            _evidence('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.OfficialCancellations.cs', 'dbContext.OfficialInvoiceCancellationDeliveries.Add(new()', 'Sequence = view.DeliveryVersion', 'await SaveOfficialAudit', live_code=True),
        ),
    ),
    DurableStoreSpec(
        'work.notification-inbox', 'Work', 'inbox', 'WorkDbContext', 'WorkNotificationInboxRow', 'work', 'NotificationInbox', (),
        (
            _evidence('src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDbContext.cs', 'Schema = "work"'),
            _evidence('src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDbContext.Notifications.cs', 'Entity<WorkNotificationInboxRow>()', 'ToTable("NotificationInbox", Schema', 'HasKey'),
            _evidence('src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261116234150_AddPersonalNotifications.cs', 'name: "NotificationInbox"'),
            _evidence('src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkNotificationStoreV1.cs', 'database.NotificationInbox.Add(new WorkNotificationInboxRow()', 'PayloadSha256 = fingerprint', 'await database.SaveChangesAsync(cancellationToken)', live_code=True),
        ),
    ),
    # W8 discovered replay stores: actual unique keys, owner DDL and operative writers; K3 pending.
    DurableStoreSpec(
        'documents.branding-attempts', 'Documents', 'idempotency', 'DocumentsDbContext', 'DocumentBrandingAttemptRow', 'documents', 'DocumentBrandingAttempts', (),
        (
            _evidence('src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentsDbContext.cs', 'Schema = "documents"'),
            _evidence('src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentsDbContext.Branding.cs', 'Entity<DocumentBrandingAttemptRow>()', 'ToTable("DocumentBrandingAttempts", Schema', 'IdempotencyKeyHash', 'HasKey'),
            _evidence('src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261117140908_AddDocumentBrandingPublications.cs', 'name: "DocumentBrandingAttempts"'),
            _evidence('src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentBrandingService.cs', 'db.BrandingAttempts.Add(Attempt(', live_code=True),
        ),
    ),
    DurableStoreSpec(
        'logistics.transfer-departure-attempts', 'Logistics', 'idempotency', 'LogisticsDbContext', 'TransferDepartureAttemptRow', 'logistics', 'TransferDepartureAttempts', (),
        (
            _evidence('src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsDbContext.cs', 'Schema = "logistics"'),
            _evidence('src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsDbContext.TransferDepartures.cs', 'Entity<TransferDepartureAttemptRow>()', 'ToTable("TransferDepartureAttempts", Schema', 'IdempotencyKeyHash', 'HasKey'),
            _evidence('src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117111253_AddTransferDepartures.cs', 'name: "TransferDepartureAttempts"'),
            _evidence('src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferDepartureOwner.cs', 'database.TransferDepartureAttempts.Add(new TransferDepartureAttemptRow', live_code=True),
        ),
    ),
    DurableStoreSpec(
        'organization.company-opening-executions', 'Organization', 'idempotency', 'OrganizationDbContext', 'CompanyOpeningExecution', 'organization', 'CompanyOpeningExecutions', (),
        (
            _evidence('src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationDbContext.CompanyOpening.cs', 'Entity<CompanyOpeningExecution>()', 'ToTable("CompanyOpeningExecutions", "organization"', 'IdempotencyKeyHash', 'HasKey'),
            _evidence('src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116205304_AddAuthorizedCompanyOpening.cs', 'name: "CompanyOpeningExecutions"'),
            _evidence('src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanyOpeningV1.cs', 'database.CompanyOpeningExecutions.Add(new CompanyOpeningExecution(', live_code=True),
        ),
    ),
    DurableStoreSpec(
        'product.paint-membership-attempts', 'Product', 'idempotency', 'ProductPriceListDbContext', 'ProductPaintMembershipAttemptRow', 'product', 'PaintMembershipAttempts', (),
        (
            _evidence('src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDbContext.cs', 'Schema = "product"'),
            _evidence('src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDbContext.PaintMemberships.cs', 'Entity<ProductPaintMembershipAttemptRow>()', 'ToTable("PaintMembershipAttempts", Schema', 'IdempotencyKeyHash', 'HasKey'),
            _evidence('src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261117013540_AddProductPaintMemberships.cs', 'name: "PaintMembershipAttempts"'),
            _evidence('src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPaintMembershipService.cs', 'database.PaintMembershipAttempts.Add(new ProductPaintMembershipAttemptRow', live_code=True),
        ),
    ),
    DurableStoreSpec(
        'purchasing.incoming-invoice-attempts', 'Purchasing', 'idempotency', 'PurchasingDbContext', 'PurchaseIncomingInvoiceAttemptRow', 'purchasing', 'IncomingInvoiceAttempts', (),
        (
            _evidence('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingDbContext.cs', 'Schema = "purchasing"'),
            _evidence('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingDbContext.IncomingInvoices.cs', 'Entity<PurchaseIncomingInvoiceAttemptRow>()', 'ToTable("IncomingInvoiceAttempts", Schema', 'IdempotencyKeyHash', 'HasKey'),
            _evidence('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117084001_AddPurchaseIncomingInvoices.cs', 'name: "IncomingInvoiceAttempts"'),
            _evidence('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.IncomingInvoices.cs', 'dbContext.IncomingInvoiceAttempts.Add(new()', live_code=True),
        ),
    ),
    DurableStoreSpec(
        'purchasing.invoice-import-reservations', 'Purchasing', 'idempotency', 'PurchasingDbContext', 'PurchaseInvoiceImportReservationRow', 'purchasing', 'InvoiceImportReservations', (),
        (
            _evidence('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingDbContext.cs', 'Schema = "purchasing"'),
            _evidence('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingDbContext.InvoiceImports.cs', 'Entity<PurchaseInvoiceImportReservationRow>()', 'ToTable("InvoiceImportReservations", Schema', 'IdempotencyKeyHash', 'HasKey'),
            _evidence('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117112436_AddPurchaseInvoiceImportReservations.cs', 'name: "InvoiceImportReservations"'),
            _evidence('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.InvoiceImports.cs', 'dbContext.InvoiceImportReservations.Add(created)', live_code=True),
        ),
    ),
    DurableStoreSpec(
        'sales.subcontract-invoice-command-attempts', 'Sales', 'idempotency', 'SalesDbContext', 'SubcontractInvoiceCommandAttemptRow', 'sales', 'SubcontractInvoiceCommandAttempts', (),
        (
            _evidence('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs', 'Schema = "sales"'),
            _evidence('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.SubcontractLifecycle.cs', 'Entity<SubcontractInvoiceCommandAttemptRow>()', 'ToTable("SubcontractInvoiceCommandAttempts", Schema', 'IdempotencyKeyHash', 'HasKey'),
            _evidence('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117113343_AddSubcontractInvoiceLifecycle.cs', 'name: "SubcontractInvoiceCommandAttempts"'),
            _evidence('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractLifecycle.cs', 'dbContext.SubcontractInvoiceCommandAttempts.Add(acceptedAttempt)', live_code=True),
        ),
    ),
    # W8 FIN-S11/FIN-S14: an existing physical Finance owner outbox, not a new provider or activation.
    # Both actual append sites share the owner save and durable command replay. K3 review remains pending.
    DurableStoreSpec(
        "finance.outbox", "Finance", "outbox", "FinanceDbContext", "FinanceOutboxRow",
        "finance", "OutboxMessages", ("FinanceFinancialMovementEventsV1", "FinanceCreditMovementEventsV1"),
        (
            _evidence(FINANCE_CONTEXT, 'Schema = "finance"'),
            _evidence("src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/FinanceDbContext.Credits.cs",
                      "model.Entity<FinanceOutboxRow>()", 'outbox.ToTable("OutboxMessages", Schema',
                      "outbox.HasKey(x => new { x.CompanyId, x.Id })",
                      "outbox.HasQueryFilter(x => companyContext.IsResolved && x.CompanyId == companyContext.CompanyId)",
                      "outbox.Property(x => x.Position).IsRowVersion()",
                      "outbox.HasIndex(x => new { x.CompanyId, x.EventType, x.AggregateId, x.AggregateVersion }).IsUnique()"),
            _evidence("src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117005509_AddCreditActualMovements.cs",
                      'name: "OutboxMessages"', 'schema: "finance"'),
            _evidence("src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceFinancialMovementOutboxV1.cs",
                      "database.OutboxMessages.Add(new FinanceOutboxRow", "CompanyId = companyId",
                      "SchemaVersion = FinanceFinancialMovementEventsV1.SchemaVersion", live_code=True),
            _evidence("src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditMovementServiceV1.cs",
                      "db.OutboxMessages.Add(new FinanceOutboxRow()", "CompanyId = context.CompanyId",
                      "EventType = FinanceCreditMovementEventsV1.Recorded", live_code=True),
            _evidence("src/backend/Modules/Finance/GoymenERP.Modules.Finance.Contracts/FinanceFinancialMovementEventsV1.cs",
                      'SourceName = "finance.outbox.v1"', "SchemaVersion = 1",
                      "public sealed record FinanceExpensePaymentRecordedV1", "public sealed record FinanceCashTransferRecordedV1"),
            _evidence("src/backend/Modules/Finance/GoymenERP.Modules.Finance.Contracts/CreditMovementV1.cs",
                      "public static class FinanceCreditMovementEventsV1", "public sealed record FinanceCreditMovementRecordedV1"),
        ),
    ),
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
        "accounting.source-document-admissions", "Accounting", "idempotency", "AccountingDbContext",
        "AccountingSourceDocumentAdmissionRow", "accounting", "SourceDocumentAdmissions", ACCOUNTING_CONTEXT,
        "src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261108222146_AddSourceDocumentAdmissions.cs",
        ACCOUNTING_SOURCE_DOCUMENT_ADMISSION_STORE,
        ("dbContext.SourceDocumentAdmissions.Add(row)",),
        mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "counterparty.outbox", "Counterparty", "outbox", "CounterpartyDbContext", "CounterpartyOutboxRow",
        "counterparty", "OutboxMessages", COUNTERPARTY_CONTEXT,
        "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20261115132003_AddCounterpartyOutbox.cs",
        "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.Deactivation.cs",
        ("dbContext.OutboxMessages.Add(new CounterpartyOutboxRow", "EventType = CounterpartyPartyDeactivatedOutboxV1.EventType"),
        mapping_tokens=("AggregateId", "Sequence", "IsUnique()"),
        contracts=("CounterpartyPartyDeactivatedOutboxV1", "CounterpartyPartyMergedOutboxV1"),
        extra_evidence=(
            _evidence(
                "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/CounterpartyPartyDeactivatedOutboxV1.cs",
                "public static class CounterpartyPartyDeactivatedOutboxV1",
                'EventType = "counterparty.party.deactivated.v1"',
            ),
            _evidence(
                "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/CounterpartyPartyMergedOutboxV1.cs",
                "public static class CounterpartyPartyMergedOutboxV1",
                'EventType = "counterparty.party.merged.v1"',
            ),
            _evidence(
                "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.Merge.cs",
                "dbContext.OutboxMessages.Add(new CounterpartyOutboxRow",
                "EventType = CounterpartyPartyMergedOutboxV1.EventType",
                live_code=True,
            ),
        ),
        uses_schema_constant=False,
    ),
    _store(
        "documents.document-attempts", "Documents", "idempotency", "DocumentsDbContext",
        "DocumentAttemptRow", "documents", "DocumentAttempts", DOCUMENTS_CONTEXT,
        "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20260930120000_InitialDocuments.cs",
        DOCUMENTS_STORE, ("dbContext.Attempts.Add(new DocumentAttemptRow",),
        mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.cash-account-commands", "Finance", "idempotency", "FinanceDbContext",
        "CashAccountCommandRow", "finance", "CashAccountCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261112070144_AddCashAccounts.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCashAccountService.cs",
        ("dbContext.CashAccountCommands.Add(new CashAccountCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.cash-transfer-commands", "Finance", "idempotency", "FinanceDbContext",
        "CashTransferCommandRow", "finance", "CashTransferCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115145712_AddCashTransfers.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/CashTransferService.cs",
        ("dbContext.CashTransferCommands.Add(new CashTransferCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.customer-return-credits", "Finance", "idempotency", "FinanceDbContext",
        "FinanceCustomerReturnCreditRow", "finance", "CustomerReturnCredits", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114231451_AddCustomerReturnCredits.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceAcceptanceService.cs",
        ("dbContext.CustomerReturnCredits.Add(new FinanceCustomerReturnCreditRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.command-replay", "Finance", "idempotency", "FinanceDbContext",
        "FinanceCommandRow", "finance", "Commands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260918200000_InitialFinanceDrafts.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ReceiptDraftService.cs",
        ("dbContext.Commands.Add(new FinanceCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.expense-payment-commands", "Finance", "idempotency", "FinanceDbContext",
        "ExpensePaymentCommandRow", "finance", "ExpensePaymentCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115000505_AddExpensePayments.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ExpensePaymentService.cs",
        ("dbContext.ExpensePaymentCommands.Add(new ExpensePaymentCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.invoice-cancellations", "Finance", "idempotency", "FinanceDbContext",
        "FinanceInvoiceCancellationRow", "finance", "InvoiceCancellations", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261107131505_AddInvoiceCancellations.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceOpenItemCancellationService.cs",
        ("dbContext.InvoiceCancellations.Add(new FinanceInvoiceCancellationRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.official-invoice-import-commands", "Finance", "idempotency", "FinanceDbContext",
        "FinanceOfficialInvoiceImportCommandRow", "finance", "OfficialInvoiceImportCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260930185113_AddOfficialInvoiceImportApprovalV1.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs",
        ("dbContext.OfficialInvoiceImportCommands.Add(row)",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.official-sales-invoice-commands", "Finance", "idempotency", "FinanceDbContext",
        "FinanceOfficialSalesInvoiceCommandRow", "finance", "OfficialSalesInvoiceCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261111233047_AddOfficialSalesInvoices.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialSalesInvoiceServiceV1.cs",
        ("dbContext.OfficialSalesInvoiceCommands.Add(new FinanceOfficialSalesInvoiceCommandRow",),
        mapping_tokens=("IdempotencyKeyHash",),
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
        "finance.party-credit-limit-commands", "Finance", "idempotency", "FinanceDbContext",
        "PartyCreditLimitCommandRow", "finance", "PartyCreditLimitCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114200445_AddPartyCreditLimits.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePartyCreditLimitService.cs",
        ("dbContext.PartyCreditLimitCommands.Add(new PartyCreditLimitCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.party-collateral-commands", "Finance", "idempotency", "FinanceDbContext",
        "PartyCollateralCommandRow", "finance", "PartyCollateralCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115184536_AddPartyCollaterals.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePartyCollateralService.cs",
        ("dbContext.PartyCollateralCommands.Add(new PartyCollateralCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    # KIM-S5 source inventory only; the obsolete automatic-opener path remains PARTIAL.
    _store(
        "identity.company-setup-inbox", "Identity", "inbox", "IdentityDbContext",
        "CompanySetupInboxReceipt", "identity", "CompanySetupInboxReceipts",
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/IdentityDbContext.CompanySetup.cs",
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261116185533_AddCompanySetupInboxAndOutboxPosition.cs",
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityCompanySetupService.cs",
        ("database.CompanySetupInboxReceipts.Add(new CompanySetupInboxReceipt(", "BeginTransactionAsync(IsolationLevel.Serializable",
         "database.CompanySetupInboxReceipts.AsNoTracking()", "receipt.PayloadHash != hash",
         "database.SaveChangesAsync(cancellationToken)", "transaction.CommitAsync(cancellationToken)"),
        mapping_tokens=("HasKey(x => new { x.CompanyId, x.EventId })", "HasQueryFilter(x => x.CompanyId == CompanySetupCompanyId)"),
        contracts=("OrganizationCompanyOpenedV1",),
        extra_evidence=(
            _evidence("src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Messaging/CompanySetupEventsV1.cs",
                      "public sealed record OrganizationCompanyOpenedV1", 'EventType = "organization.company.opened.v1"'),
        ),
        uses_schema_constant=False,
    ),
    DurableStoreSpec(
        "identity.outbox", "Identity", "outbox", "IdentityDbContext", "IdentityOutboxMessage",
        "identity", "OutboxMessages", (),
        (
            _evidence(IDENTITY_CONTEXT, "Entity<IdentityOutboxMessage>()", "entity.ToTable(", '"OutboxMessages",', '"identity",', "SourceOperation"),
            _evidence("src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261115144305_AddIdentityBootstrapState.cs", 'name: "OutboxMessages"'),
            _evidence(
                "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Bootstrap/FounderBootstrapService.cs",
                "dbContext.OutboxMessages.Add(new IdentityOutboxMessage(",
                "IdentityFounderBootstrappedV1.EventType",
                live_code=True,
            ),
            _evidence(
                "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityAccessAssignmentService.Plumbing.cs",
                "database.OutboxMessages.Add(new IdentityOutboxMessage(",
                "IdentityPermissionChangedV1.EventType",
                live_code=True,
            ),
            _evidence(
                "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityCompanySetupService.cs",
                "database.OutboxMessages.Add(new IdentityOutboxMessage(IdentityCompanyFirstAdminAssignedV1.EventId(metadata.CompanyId)",
                "var payload = new IdentityCompanyFirstAdminAssignedV1(",
                "IdentityCompanyFirstAdminAssignedV1.EventType", "IdentityPermissionChangedV1.EventType",
                live_code=True,
            ),
            _evidence("src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Messaging/CompanySetupEventsV1.cs",
                      "public sealed record IdentityCompanyFirstAdminAssignedV1",
                      'EventType = "identity.company.first_admin_assigned.v1"'),
        ),
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
        contracts=(
            "InventoryCustomerReturnOutboxV1",
            "InventoryDefiniteShipmentOutboxV1",
            "InventorySupplierReturnExitOutboxV1",
        ),
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
            # tren-1 wave 3 (stok_lojistik): supplier-return exit outbox; K3 review pending (stok).
            _evidence(
                "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventorySupplierReturnExitOutboxV1.cs",
                "public static class InventorySupplierReturnExitOutboxV1",
                'EventType = "inventory.supplier-return-exit.recorded.v1"',
            ),
            _evidence(
                "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventorySupplierReturnExitParticipant.cs",
                "context.OutboxMessages.Add(new InventoryOutboxRow",
                "EventType = InventorySupplierReturnExitOutboxV1.EventType",
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
        "product.item-type-attempts", "Product", "idempotency", "ProductPriceListDbContext",
        "ProductItemTypeAttemptRow", "product", "ProductItemTypeAttempts",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDbContext.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261031195253_AddProductItemTypes.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductItemTypeService.cs",
        ("database.ItemTypeAttempts.Add(new ProductItemTypeAttemptRow",),
        mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "product.dimension-attempts", "Product", "idempotency", "ProductPriceListDbContext",
        "ProductDimensionAttemptRow", "product", "ProductDimensionAttempts", "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDbContext.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261114194618_AddProductCardDimensions.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductDimensionService.cs",
        ("database.DimensionAttempts.Add(new ProductDimensionAttemptRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    # tren-1 wave 3 (stok_lojistik, STK profile weight import): K3 review pending (stok).
    _store(
        "product.profile-weight-attempts", "Product", "idempotency", "ProductPriceListDbContext",
        "ProductProfileWeightAttemptRow", "product", "ProductProfileWeightAttempts", "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDbContext.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261115184532_AddProductProfileWeightImports.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductProfileWeightTableService.cs",
        ("database.ProfileWeightAttempts.Add(new ProductProfileWeightAttemptRow",), mapping_tokens=("IdempotencyKeyHash",),
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
        "purchasing.goods-receipt-batch-attempts", "Purchasing", "idempotency", "PurchasingDbContext",
        "PurchaseGoodsReceiptBatchAttemptRow", "purchasing", "PurchaseGoodsReceiptBatchAttempts", PURCHASING_CONTEXT,
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261115184944_AddPurchaseGoodsReceiptBatches.cs",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseGoodsReceiptBatchStore.cs",
        ("dbContext.GoodsReceiptBatchAttempts.Add(Attempt(",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "purchasing.outbox", "Purchasing", "outbox", "PurchasingDbContext", "PurchasingOutboxRow",
        "purchasing", "OutboxMessages", PURCHASING_CONTEXT,
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261115132609_AddPurchaseOrderLineClosure.cs",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs",
        ("dbContext.OutboxMessages.Add(LineClosedMessage(",),
        mapping_tokens=("AggregateId", "Sequence", "IsUnique()", "SourceOperation"),
    ),
    _store(
        "sales.contract-attempts", "Sales", "idempotency", "SalesDbContext", "ContractAttemptRow",
        "sales", "ContractAttempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261115160935_AddSalesContracts.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Contracts.cs",
        ("dbContext.ContractAttempts.Add(NewContractAttempt(",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "sales.customer-return-attempts", "Sales", "idempotency", "SalesDbContext",
        "CustomerReturnAttemptRow",
        "sales", "CustomerReturnAttempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110215452_AddCustomerReturns.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.CustomerReturns.cs",
        ("dbContext.CustomerReturnAttempts.Add(NewCustomerReturnAttempt(",), mapping_tokens=("IdempotencyKeyHash",),
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
        "sales.invoice-attempts", "Sales", "idempotency", "SalesDbContext", "InvoiceAttemptRow",
        "sales", "InvoiceAttempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261107155919_AddSalesInvoices.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Invoices.cs",
        ("dbContext.InvoiceAttempts.Add(new InvoiceAttemptRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "sales.quote-attempts", "Sales", "idempotency", "SalesDbContext", "QuoteAttemptRow",
        "sales", "QuoteAttempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027071300_AddSalesQuotes.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Quotes.cs",
        ("dbContext.QuoteAttempts.Add(new QuoteAttemptRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "sales.request-attempts", "Sales", "idempotency", "SalesDbContext", "RequestAttemptRow",
        "sales", "RequestAttempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027063346_AddSalesRequests.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Requests.cs",
        ("dbContext.RequestAttempts.Add(new RequestAttemptRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "sales.shipment-risk-exception-attempts", "Sales", "idempotency", "SalesDbContext",
        "ShipmentRiskExceptionAttemptRow",
        "sales", "ShipmentRiskExceptionAttempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110155556_AddShipmentRiskExceptions.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.RiskExceptions.cs",
        ("dbContext.ShipmentRiskExceptionAttempts.Add(NewRiskExceptionAttempt(",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "sales.outbox", "Sales", "outbox", "SalesDbContext", "SalesOutboxRow", "sales",
        "OutboxMessages",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108143000_AddSalesInvoiceFinalization.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.InvoiceFinalization.cs",
        ("dbContext.OutboxMessages.Add(new SalesOutboxRow",), mapping_tokens=("SourceOperation",),
    ),
    _store(
        "work.observation-inbox", "Work", "inbox", "WorkDbContext", "WorkObservationInboxRow",
        "work", "ObservationInbox", WORK_CONTEXT,
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261005190000_InitialWorkDesiredStateStorage.cs",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
        ("dbContext.ObservationInbox.Add(inbox)",), mapping_tokens=("SourceKeyDigest", "ObservationOrdinal"),
    ),
    _store(
        "work.outbox-cursor", "Work", "delivery", "WorkDbContext", "WorkOutboxSourceCursorRow",
        "work", "OutboxSourceCursors", WORK_CONTEXT,
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261108221330_AddWorkOutboxDispatch.cs",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs",
        ("db.OutboxSourceCursors.Add(new WorkOutboxSourceCursorRow",), mapping_tokens=("SourceName", "Position"),
    ),
    _store(
        "work.outbox-delivery", "Work", "inbox", "WorkDbContext", "WorkOutboxDeliveryRow",
        "work", "OutboxDeliveries", WORK_CONTEXT,
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261108221330_AddWorkOutboxDispatch.cs",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs",
        ("db.OutboxDeliveries.Add(row)",), mapping_tokens=("ConsumerName", "PayloadSha256"),
    ),
    _store(
        "work.outbox", "Work", "outbox", "WorkDbContext", "WorkOutboxRow", "work",
        "OutboxMessages", WORK_CONTEXT,
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261005190000_InitialWorkDesiredStateStorage.cs",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
        ("dbContext.OutboxMessages.Add(outbox)",), mapping_tokens=("SourceKeyDigest", "ObservationOrdinal"),
    ),
    # tren-1 wave 5 integration (finans FIN-S8/FIN-S9): K3 review pending (finans).
    _store(
        "finance.credit-account-commands", "Finance", "idempotency", "FinanceDbContext",
        "CreditAccountCommandRow", "finance", "CreditAccountCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116111738_AddCreditAccounts.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditAccountService.cs",
        ("dbContext.CreditAccountCommands.Add(new CreditAccountCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "finance.opening-balance-commands", "Finance", "idempotency", "FinanceDbContext",
        "OpeningBalanceCommandRow", "finance", "OpeningBalanceCommands", FINANCE_CONTEXT,
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116080924_AddOpeningBalances.cs",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpeningBalanceService.cs",
        ("dbContext.OpeningBalanceCommands.Add(new OpeningBalanceCommandRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    # tren-1 wave 5 integration (stok_lojistik STK-S27): definite-shipment outbox; K3 review pending (stok).
    _store(
        "logistics.outbox", "Logistics", "outbox", "LogisticsDbContext", "LogisticsOutboxMessageRow",
        "logistics", "OutboxMessages", LOGISTICS_CONTEXT,
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261116010428_AddShipmentRiskGateOutbox.cs",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs",
        ("dbContext.OutboxMessages.Add(new LogisticsOutboxMessageRow", "EventType = LogisticsDefiniteShipmentOutboxV1.RecordedEventType"),
        mapping_tokens=("SubjectId", "IsUnique()"),
        contracts=("LogisticsDefiniteShipmentOutboxV1",),
        extra_evidence=(
            _evidence(
                "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Application/Outbox/LogisticsDefiniteShipmentOutboxV1.cs",
                "public static class LogisticsDefiniteShipmentOutboxV1",
                'RecordedEventType = "logistics.definite-shipment.recorded.v1"',
            ),
        ),
    ),
    # tren-1 wave 5 integration (stok_lojistik): K3 review pending (stok).
    _store(
        "logistics.weighing-difference-attempts", "Logistics", "idempotency", "LogisticsDbContext",
        "WeighingDifferenceAttemptRow", "logistics", "WeighingDifferenceAttempts", LOGISTICS_CONTEXT,
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261116003008_AddWeighingDifferences.cs",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsWeighingDifferenceStore.cs",
        ("dbContext.WeighingDifferenceAttempts.Add(Attempt(",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "product.package-standard-attempts", "Product", "idempotency", "ProductPriceListDbContext",
        "ProductPackageStandardAttemptRow", "product", "ProductPackageStandardAttempts", "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListDbContext.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261115234445_AddProductPackageStandards.cs",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPackageStandardService.cs",
        ("database.PackageStandardAttempts.Add(new ProductPackageStandardAttemptRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
    _store(
        "organization.company-setup-inbox", "Organization", "inbox", "OrganizationDbContext",
        "CompanySetupInboxReceipt", "organization", "CompanySetupInboxReceipts",
        "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationDbContext.CompanySetup.cs",
        "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116185536_AddCompanySetupLifecycleAndMessaging.cs",
        "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanySetupInboxConsumer.cs",
        ("database.CompanySetupInboxReceipts.Add(new CompanySetupInboxReceipt(", "BeginTransactionAsync(IsolationLevel.Serializable",
         "database.CompanySetupInboxReceipts.AsNoTracking()", "receipt.PayloadHash != hash", "company.CompleteSetup(now)",
         "database.SaveChangesAsync(cancellationToken)", "transaction.CommitAsync(cancellationToken)"),
        mapping_tokens=("HasKey(x => new { x.CompanyId, x.EventId })", "HasQueryFilter(x => x.CompanyId == companyContext.CompanyId)"),
        contracts=("IdentityCompanyFirstAdminAssignedV1",),
        extra_evidence=(
            _evidence("src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Messaging/CompanySetupEventsV1.cs",
                      "public sealed record IdentityCompanyFirstAdminAssignedV1",
                      'EventType = "identity.company.first_admin_assigned.v1"'),
        ),
        uses_schema_constant=False,
    ),
    # W8 actual authorized company opening now enqueues this existing outbox in the same transaction.
    # Exact OpenAsync writer evidence replaces the W7 gap; K3 and activation remain separate.
    DurableStoreSpec(
        "organization.company-setup-outbox", "Organization", "outbox", "OrganizationDbContext",
        "OrganizationCompanySetupOutboxMessage", "organization", "CompanySetupOutboxMessages",
        ("OrganizationCompanyOpenedV1",),
        (
            _evidence("src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationDbContext.CompanySetup.cs",
                      "Entity<OrganizationCompanySetupOutboxMessage>()", 'ToTable("CompanySetupOutboxMessages", "organization"',
                      "HasKey(x => x.Id)", "Property(x => x.Position).IsRowVersion()",
                      "HasQueryFilter(x => x.CompanyId == companyContext.CompanyId)"),
            _evidence("src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116185536_AddCompanySetupLifecycleAndMessaging.cs",
                      'name: "CompanySetupOutboxMessages"'),
            _evidence("src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanySetupOutboxSource.cs",
                      "ReadAfterAsync", "database.CompanySetupOutboxMessages.AsNoTracking()",
                      "x.CompanyId == companyId && x.Position > after && x.Position < horizon",
                      "OrganizationCompanyOpenedV1.EventType"),
            _evidence("src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Messaging/CompanySetupEventsV1.cs",
                      "public sealed record OrganizationCompanyOpenedV1", 'EventType = "organization.company.opened.v1"'),
            _evidence('src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanyOpeningV1.cs', "database.CompanySetupOutboxMessages.Add(new OrganizationCompanySetupOutboxMessage(", "OrganizationCompanyOpenedV1", live_code=True),
        ),
    ),
    # tren-1 wave 5 integration (alis_cari supplier returns): K3 review pending (alis_cari).
    _store(
        "purchasing.supplier-return-attempts", "Purchasing", "idempotency", "PurchasingDbContext",
        "PurchaseSupplierReturnAttemptRow", "purchasing", "PurchaseSupplierReturnAttempts", PURCHASING_CONTEXT,
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116113129_AddPurchaseSupplierReturns.cs",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseSupplierReturnStore.cs",
        ("dbContext.SupplierReturnAttempts.Add(creationAttempt)", "creationAttempt.ResultJson = JsonSerializer.Serialize(stored, Json)"), mapping_tokens=("IdempotencyKeyHash",),
    ),
    # tren-1 wave 5 integration (satis subcontract service invoices): K3 review pending (satis).
    _store(
        "sales.subcontract-service-invoice-attempts", "Sales", "idempotency", "SalesDbContext",
        "SubcontractServiceInvoiceAttemptRow", "sales", "SubcontractServiceInvoiceAttempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261116002458_AddSubcontractServiceInvoices.cs",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractServiceInvoices.cs",
        ("dbContext.SubcontractServiceInvoiceAttempts.Add(new SubcontractServiceInvoiceAttemptRow",), mapping_tokens=("IdempotencyKeyHash",),
    ),
)


# A lexical write token is evidence only inside the operative method named here.  The
# binding is intentionally explicit: moving a write into an unused helper or a deferred
# lambda must invalidate the inventory instead of silently preserving its source hash.
WRITER_METHOD_SCOPES = {
    ('sales.official-invoice-cancellation-deliveries', 'src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.OfficialCancellations.cs'): 'RecordDeliveryCoreAsync',
    ('work.notification-inbox', 'src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkNotificationStoreV1.cs'): 'DeliverAsync',
    ('documents.branding-attempts', 'src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentBrandingService.cs'): 'ExecuteAsync',
    ('logistics.transfer-departure-attempts', 'src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferDepartureOwner.cs'): 'OwnedAsync',
    ('organization.company-opening-executions', 'src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanyOpeningV1.cs'): 'OpenAsync',
    ('product.paint-membership-attempts', 'src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPaintMembershipService.cs'): 'ExecuteAsync',
    ('purchasing.incoming-invoice-attempts', 'src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.IncomingInvoices.cs'): 'AddInvoiceAttempt',
    ('purchasing.invoice-import-reservations', 'src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.InvoiceImports.cs'): 'ReserveAsync',
    ('sales.subcontract-invoice-command-attempts', 'src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractLifecycle.cs'): 'UpdateLifecycleCoreAsync',
    ('organization.company-setup-outbox', 'src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanyOpeningV1.cs'): "OpenAsync",
    ("finance.outbox", "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceFinancialMovementOutboxV1.cs"): "Add",
    ("finance.outbox", "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditMovementServiceV1.cs"): "AddEvent",
    ("identity.company-setup-inbox", "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityCompanySetupService.cs"): "HandleAsync",
    ("identity.outbox", "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityCompanySetupService.cs"): "StageAssignmentAsync",
    ("organization.company-setup-inbox", "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanySetupInboxConsumer.cs"): "HandleAsync",
    ("accounting.command-replay", ACCOUNTING_STORE): "AddCommand",
    ("accounting.delivery-inbox", ACCOUNTING_STORE): "AddDelivery",
    ("accounting.source-document-admissions", ACCOUNTING_SOURCE_DOCUMENT_ADMISSION_STORE): "WriteCoreAsync",
    ("counterparty.outbox", "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.Deactivation.cs"): "EnqueuePartyDeactivatedEvent",
    ("counterparty.outbox", "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.Merge.cs"): "EnqueuePartyMergedEvent",
    ("documents.document-attempts", DOCUMENTS_STORE): "StoreUploadCoreAsync",
    ("finance.cash-transfer-commands", "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/CashTransferService.cs"): "AddCommand",
    ("finance.party-collateral-commands", "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePartyCollateralService.cs"): "AddCommand",
    ("identity.outbox", "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Bootstrap/FounderBootstrapService.cs"): "WriteAsync",
    ("identity.outbox", "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityAccessAssignmentService.Plumbing.cs"): "AddPermissionChanges",
    ("purchasing.goods-receipt-batch-attempts", "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseGoodsReceiptBatchStore.cs"): "SaveNewAsync",
    ("purchasing.outbox", "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs"): "MutateOrderCoreAsync",
    ("sales.contract-attempts", "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Contracts.cs"): "StoreContractAsync",
    (
        "finance.cash-account-commands",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCashAccountService.cs",
    ): "AddCommand",
    (
        "finance.customer-return-credits",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceAcceptanceService.cs",
    ): "AcceptAsync",
    (
        "finance.command-replay",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ReceiptDraftService.cs",
    ): "DraftSaveAsync",
    (
        "finance.expense-payment-commands",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ExpensePaymentService.cs",
    ): "AddCommand",
    (
        "finance.invoice-cancellations",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceOpenItemCancellationService.cs",
    ): "CancelAsync",
    (
        "finance.official-invoice-import-commands",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs",
    ): "AddCommand",
    (
        "finance.official-sales-invoice-commands",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialSalesInvoiceServiceV1.cs",
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
        "finance.party-credit-limit-commands",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePartyCreditLimitService.cs",
    ): "AddCommand",
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
        "inventory.outbox",
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventorySupplierReturnExitParticipant.cs",
    ): "StageAsync",
    (
        "logistics.customer-return-attempts",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsCustomerReturnStore.cs",
    ): "RecordRefusalAsync",
    (
        "logistics.loading-attempts",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs",
    ): "StoreInstructionCoreAsync",
    (
        "logistics.transfer-outgoing-document-attempts",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferOutgoingDocumentStore.cs",
    ): "StoreDraftCoreAsync",
    (
        "manufacturing.command-replay",
        "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.cs",
    ): "AddCommand",
    (
        "product.price-list-attempts",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListStore.cs",
    ): "RecordRefusalAsync",
    (
        "product.item-type-attempts",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductItemTypeService.cs",
    ): "AddAttempt",
    (
        "product.dimension-attempts",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductDimensionService.cs",
    ): "AddAttempt",
    (
        "product.profile-weight-attempts",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductProfileWeightTableService.cs",
    ): "AddAttempt",
    (
        "purchasing.purchase-acceptance-shares",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseAcceptanceParticipant.cs",
    ): "StageAsync",
    (
        "purchasing.purchase-attempts",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs",
    ): "StoreRequestAsync",
    (
        "sales.customer-return-attempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.CustomerReturns.cs",
    ): "RecordCustomerReturnRefusalAsync",
    (
        "sales.draft-attempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs",
    ): "StoreDraftAsync",
    (
        "sales.invoice-attempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Invoices.cs",
    ): "StoreInvoiceAsync",
    (
        "sales.outbox",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.InvoiceFinalization.cs",
    ): "FinalizeInvoiceAsync",
    (
        "sales.quote-attempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Quotes.cs",
    ): "RecordQuoteRefusalAsync",
    (
        "sales.request-attempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Requests.cs",
    ): "RecordRequestRefusalAsync",
    (
        "sales.shipment-risk-exception-attempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.RiskExceptions.cs",
    ): "RecordRiskExceptionRefusalAsync",
    (
        "work.observation-inbox",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
    ): "ReconcileAsync",
    (
        "work.outbox",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
    ): "ReconcileAsync",
    (
        "work.outbox-cursor",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs",
    ): "DispatchSourceAsync",
    (
        "work.outbox-delivery",
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs",
    ): "DeliverAsync",
    # tren-1 wave 5 integration: K3 review pending (owning team).
    (
        "finance.credit-account-commands",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditAccountService.cs",
    ): "AddCommand",
    (
        "finance.opening-balance-commands",
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpeningBalanceService.cs",
    ): "AddCommand",
    (
        "logistics.outbox",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs",
    ): "AddShipmentRiskGateRows",
    (
        "logistics.weighing-difference-attempts",
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsWeighingDifferenceStore.cs",
    ): "StoreAsync",
    (
        "product.package-standard-attempts",
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPackageStandardService.cs",
    ): "AddAttempt",
    (
        "purchasing.supplier-return-attempts",
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseSupplierReturnStore.cs",
    ): "SaveNewAsync",
    (
        "sales.subcontract-service-invoice-attempts",
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractServiceInvoices.cs",
    ): "StoreSubcontractServiceInvoiceCoreAsync",
}


# These declaration-and-body fingerprints bind recovery evidence to the reviewed live
# methods.  Any semantic or execution-shape change must be deliberately re-inventoried.
REVIEWED_OPERATIVE_METHOD_FINGERPRINTS = {
    ('src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/AccountingSourceDocumentAdmissionStoreV1.cs', 'WriteCoreAsync'): 'b4781a7fd74dd11073a13aa3ab346fffd1fbdb78c7fadee56704b295c48737ee',
    ('src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentsStore.cs', 'StoreUploadCoreAsync'): '470d2d084382d6a1319d76b6cbe08398511d3b25ae05e4ddcce17c302fdb876a',
    ('src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs', 'StoreInstructionCoreAsync'): '66e35b6879cad00bf22e5404c6e1074a659a8899974e9b276ea084af5a71a74a',
    ('src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferOutgoingDocumentStore.cs', 'StoreDraftCoreAsync'): 'edbc84f0399134fd2ee78a19076e4b5b224b2f26aa9c4608cbefa3273e507690',
    ('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractServiceInvoices.cs', 'StoreSubcontractServiceInvoiceCoreAsync'): 'b7b636bb56046042192d2b34272a4e447a91df79a94023cac639cd40bb77308c',
    ('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.OfficialCancellations.cs', 'RecordDeliveryCoreAsync'): '024cd86456d4718b6469d63f95bc264be03a889249e5ec008265d4f7f65b457c',
    ('src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkNotificationStoreV1.cs', 'DeliverAsync'): 'b0f10bdc40d55343e0525796bedddab7d76648652897338ea54cfc4e1297d7fc',
    ('src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentBrandingService.cs', 'ExecuteAsync'): 'ee292cd2d23aa6a77986894d07c85db975806cbeb3f2b88ed10c4da473a166ff',
    ('src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferDepartureOwner.cs', 'OwnedAsync'): 'b92e422368b12054d1fd377dd7c8be7c7f0457cb355eaa845bf783f0b6dcc660',
    ('src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanyOpeningV1.cs', 'OpenAsync'): '6e2034b554c897d14aef45967119dbe74ff48caa3fdd1fbb41efce8bbaad5922',
    ('src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPaintMembershipService.cs', 'ExecuteAsync'): '5c471ccfa284e2a70d549b26d41cd3a67bd3d8b755ac8ab95cb55c3563f05720',
    ('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.IncomingInvoices.cs', 'AddInvoiceAttempt'): 'f5e07813e86c291e195fa6b0aadd2e4b3777360c8108778227040ac0fe32c92e',
    ('src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.InvoiceImports.cs', 'ReserveAsync'): '0370132f3b7aaf81c8cd5204b4f93f3a52265325c53619993010916807c11bba',
    ('src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractLifecycle.cs', 'UpdateLifecycleCoreAsync'): '9bedc0f9e118c8e62453d6703b868c0c328d2b685cfe591d2f5e0d465bccd21e',
    # W8 actual Finance outbox append methods; technical inventory, K3 review pending.
    ('src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceFinancialMovementOutboxV1.cs', 'Add'): 'e35043ca26982c951966869d91767e06323933e59494ca0e0fc3a9525a44fb7e',
    ('src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditMovementServiceV1.cs', 'AddEvent'): 'd0e632763959df43cb31e6dba815b5cd6a66e758579584805e9b5344aaa48e47',
    ("src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityCompanySetupService.cs", "HandleAsync"): '9b8c9fe980c21eb53405edfc5e0660390f470a5c5c15fa4941b6e89a3d776a25',
    ("src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityCompanySetupService.cs", "StageAssignmentAsync"): '660cf4f8c4cc265af88fc128c243621cbb8f66c6f8a93e462731b3ebc87bc378',
    ("src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanySetupInboxConsumer.cs", "HandleAsync"): 'c13e7be7d84f5fa57fd11c157df9ab9f8c7e57c203d7748cd252b7e5efaf1102',
    (
        "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.Deactivation.cs",
        "EnqueuePartyDeactivatedEvent",
    ): "3cba5b859bda1de2fbebcfde1454d5c2c06887237342967aa5fcd5533da36965",
    (
        "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.Merge.cs",
        "EnqueuePartyMergedEvent",
    ): "52a982505b510644ca43af551a1f0ca8b47c5bb93ee9052a8fc7e0e60c649227",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/CashTransferService.cs",
        "AddCommand",
    ): "95b837a7bdd7a512c0af8c8c2ceea08945e2c1ead5190892f19c53ed3ea82d02",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePartyCollateralService.cs",
        "AddCommand",
    ): "1eef154818a36b16d6e57aab69b7c6e99d3ea39edb9d5afb521ace0d6977a546",
    (
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Bootstrap/FounderBootstrapService.cs",
        "WriteAsync",
    ): "6f7567888c1ed9dc3a63fa75b0fac2af53e1013882431b2274beb7766065d98a",
    (
        "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityAccessAssignmentService.Plumbing.cs",
        "AddPermissionChanges",
    ): "1fa48113b3b588a42165e1dd2db4c0f09c9b215d3245b47016e604d9bd4063ad",
    (
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseGoodsReceiptBatchStore.cs",
        "SaveNewAsync",
    ): "72b4d1a6711c166b5a61382e818fde73510e858dbedf870c04256ffdfdb31583",
    (
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs",
        "MutateOrderCoreAsync",
    ): "888755fd034f6eb1275a6a3832ed5c640f86d50c764beac7ea25f73188e26e81",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Contracts.cs",
        "StoreContractAsync",
    ): "a724dd8fc5e1ae23917cbbd5ed69a0be510994366de724bd5827de47548d6545",
    ("src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs", "DeliverAsync"): "c87dadf8e2278a266cd8966a30e3842e686d207cc6511f09a6b098b9cd7af0f6",
    ("src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs", "DispatchSourceAsync"): "6d25cc932f10b7d65152d654c7c6ae6263d74f7f5fa8b0f24d36e08cb3de93a6",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.InvoiceFinalization.cs",
        "FinalizeInvoiceAsync",
    ): "2eaf24a1922b322c203825a80973b4140f737c66cf9e7e41588537329ba0f522",
    (ACCOUNTING_STORE, "AddCommand"): "db718449f8e13e277eb0aa197b20d7210130ab1c0d2091dd6011e43e781e47a1",
    (ACCOUNTING_SOURCE_DOCUMENT_ADMISSION_STORE, "WriteAsync"): "ed65d9615d044a6ddb5190c0ea5c2cae15b4ad68085582b4bae0d1593789f97b",
    (ACCOUNTING_STORE, "AddDelivery"): "b24889ce6551605bb62723f3bce1a2baa59c3680894167c71b3fd51ce0be1cb0",
    (DOCUMENT_MODULE_PATH, "AddDocumentsModule"): "954a4d8f7f4526be680e0c22202ced238b864ca992fe8b6a12a3e7a2933dea3d",
    (DOCUMENT_MODULE_PATH, "RegisterProductionStorageAndScanner"): "bfea0db0f27c66dcdab7d411b5e5e6d2b986a25d157d0693fc70cbd3d5d0b19b",
    (DOCUMENTS_STORE, "StoreUploadAsync"): "674169860f5d2f572bdbc0c9764afd8cf0075e19232196cef7832d87d9b7a6f4",
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
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCashAccountService.cs",
        "AddCommand",
    ): "67ee020b63114a1b8930acba8f1276fcc42834755f9651b9d3172d335a5cf381",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceOpenItemCancellationService.cs",
        "CancelAsync",
    ): "86f3f5051d8bb3ab98878f7405ddf5bc3e80156294d0a7a181ee36d710d4834e",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpenItemSourceAdmissionService.cs",
        "AdmitAsync",
    ): "0f4c4d7baf159176293b11d3d8cb2f814171e528a114bea1ed11b3f53610b916",
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
    ): "e3db8a5240e2dc6e605fd3b265c963ff095e530d0714740228d0ff7b883f4509",
    (
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs",
        "StageAsync",
    ): "daa240ca2ecdcede3fb3abd11f66d13a67e80f53b459369e0bf568207f0144a4",
    # tren-1 wave 3 (stok_lojistik): K3 review pending (stok).
    (
        "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventorySupplierReturnExitParticipant.cs",
        "StageAsync",
    ): "036e503a1ff5ef671f6ee978854810b40ceec42554560edf043094faae1f3ac7",
    (
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductProfileWeightTableService.cs",
        "AddAttempt",
    ): "eba67eb455ec7a040f5b0174365d3ec7d14e1b031ac6f2cf5271dc574758e0ee",
    (
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsCustomerReturnStore.cs",
        "RecordRefusalAsync",
    ): "502fcbf1b14fca92c1e4d34bad0e96121a27a120c963499ceb2b9b1b83093d4e",
    (
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs",
        "StoreInstructionAsync",
    ): "823e4f7c545501f64d65249e532499ffb392fb4568f6c100f48837d43f3b355a",
    (
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferOutgoingDocumentStore.cs",
        "StoreDraftAsync",
    ): "78b6a45945dca23571ff50badfc1ff09807c44fa729d1f5e0be4e81370d237af",
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
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductItemTypeService.cs",
        "AddAttempt",
    ): "bbc4dc954237d291a25d1ffdc074972335b65240d7e3fcfea3141ece1dcd729c",
    (
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseAcceptanceParticipant.cs",
        "StageAsync",
    ): "bdfdfef847802b54dd4f9222cdc20195c7b7d9c56e5a73c6303a8a3850deef3b",
    (
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs",
        "StoreRequestAsync",
    ): "94e4f9fc371feb652059dbf25ffb7214933c3f43123516b2f515a01f50a2c20b",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs",
        "StoreDraftAsync",
    ): "6706dc3ddcb2dd03fbfd7ac47fa64b3a8db2964451e7741bf8ef722a4c769a68",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Invoices.cs",
        "StoreInvoiceAsync",
    ): "06d71d88e034eb14859e26587d15f6a2a0df4a207402ab4ab309208929899471",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Quotes.cs",
        "RecordQuoteRefusalAsync",
    ): "c2b6bef89eb2e4f24001ffdff375b3b67f1c292dc1252cf07edfda8e218a0e0a",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Requests.cs",
        "RecordRequestRefusalAsync",
    ): "81c17b68bf692f4cbecb7bf7220776ff1f623add0d868829cca4484fbc0a0bb0",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.RiskExceptions.cs",
        "RecordRiskExceptionRefusalAsync",
    ): "ade77a294e3c768b7f939b445fca42e6ade3f12060ebc87f8830c2f436743328",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.CustomerReturns.cs",
        "RecordCustomerReturnRefusalAsync",
    ): "028e5839e5cdf22c05ae85b6501e603e9cac1986c173951dfdb88dfe379c1f98",
    (
        "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs",
        "ReconcileAsync",
    ): "0c760ebfe36fce642faa069b72637e699f16e1d14a04f0d154f43357be542657",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialSalesInvoiceServiceV1.cs",
        "AddCommand",
    ): "7692294ce6b3bfdb42f4f03395b151e6fdae9589e38ef31da538dadc4321bf5b",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceAcceptanceService.cs",
        "AcceptAsync",
    ): "82dd611d0cd2880b111cab71f9d473cf84db639d516d11eaf1dcd030efe2f8fa",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ExpensePaymentService.cs",
        "AddCommand",
    ): "c2b445bce9cc19a74021c23820719ef4cb0cdd36f380c10d2d69d2c31b80affc",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePartyCreditLimitService.cs",
        "AddCommand",
    ): "5c59cb3eca4c72f9d0a92555ebdf17c3af381d245a677ec46137af31a5c0d0e7",
    (
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductDimensionService.cs",
        "AddAttempt",
    ): "b5443ee7278edd96917b41298631ab00415d88db1c37e2f24de3fa64ebbc4b92",
    # tren-1 wave 5 integration: values from print_recovery_actuals.py; K3 review pending.
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditAccountService.cs",
        "AddCommand",
    ): "0a4ad7a39397f8d16908e374224ae3d2900521dc53fb13cf04daa41d04882188",
    (
        "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpeningBalanceService.cs",
        "AddCommand",
    ): "7afc3f0cf05db50ea04c13c15c1e0179a288a4fa5139ee75c3bae6aa658a135e",
    (
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs",
        "AddShipmentRiskGateRows",
    ): "55c808f72cdfce8dbc2bd09a6e83830687de2b595336bdac739f09b8abcf6357",
    (
        "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsWeighingDifferenceStore.cs",
        "StoreAsync",
    ): "92446538d12b5866cff2c7712096350fea79414c6b734ebd0dc48f60faaed901",
    (
        "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPackageStandardService.cs",
        "AddAttempt",
    ): "8dbbe5222b9d9e47889393524d836dc95f6fcd1cc438d0dfc955022cc7aaef9e",
    (
        "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseSupplierReturnStore.cs",
        "SaveNewAsync",
    ): "608d385e3867eeaa1807b4869a81636f485dd677fbe944aca69bf98835501eca",
    (
        "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractServiceInvoices.cs",
        "StoreSubcontractServiceInvoiceAsync",
    ): "211f329cb719c791639bc711465dd015614db5a9f291650bccf6c82919354aeb",
}


# The full blobs retain reachability evidence around the pinned methods (for example,
# private writer call sites) and bind every emitted source record to reviewed bytes.
REVIEWED_LIVE_SOURCE_SHA256 = {
    'src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.OfficialCancellations.cs': '8dfbf6c7a82632a1a41fd33e1e03618379075c9c9013a64ee1c1292137b60262',
    'src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkNotificationStoreV1.cs': '4f5a41d4ba313431a301ec32d4c9c8ce7f6630f1f6ef09259ad8919eb26cdb6d',
    'src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/DocumentBrandingService.cs': '6cf6ad2ad562945f1c3d69824e5df40e7276d121cbb7db90e6f26c606dc608c8',
    'src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferDepartureOwner.cs': '5f4b17b228d7cb9211c38fd4e1d99dfcd07afaae983f5c773ec6a6a55f698e4c',
    'src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanyOpeningV1.cs': 'ce21d76a89b36a20e2c27ca0df61a6b39b4c0ecd0aa511800e6cd6ad9797f742',
    'src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPaintMembershipService.cs': 'f92ce179bcb00200f361a6ff41ea5e1eb27840db9ff54f22966c8192d9b35355',
    'src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.IncomingInvoices.cs': 'a99b3fcace66aca1921f198bde6ee99fbcb85059c51b18c33cc114bc4d599317',
    'src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.InvoiceImports.cs': '087ef79936d91f86345379055410440d9d398fac469ee49452c11da0e5e24249',
    'src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractLifecycle.cs': 'e8963efeab8cd457656250f758c9d6c0cc12ceb07351a97471796d34253bcbd5',
    # W8 actual Finance outbox owner sources; technical inventory, K3 review pending.
    'src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceFinancialMovementOutboxV1.cs': 'c0483f7503aea2b2c09f161b95fe6521941e7b62f37f1617b5aff9ee51727f6e',
    'src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditMovementServiceV1.cs': '8bf81687fedcdf2bcfd2a1cba9d2121e15f0283f56408f1ec762ce267059d30e',
    "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityCompanySetupService.cs": '38a9d9b59d4d5653adf32a41dcc78ec215c6025ab2c6e0ccfca428e9caf63761',
    "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationCompanySetupInboxConsumer.cs": 'de639412a53981aa16665239583f1e4486c811e6e9f2423239272e75633b47f4',
    "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.Deactivation.cs": "6f44d4f0d467bf3f80f95e84f4d63eb662cbec25a18282fbbf050d211314d827",
    "src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/CounterpartyCommandService.Merge.cs": "188e991b0262c6fd7b6c111f4fb6cfca8143ef951da7e319b68f275e98884428",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/CashTransferService.cs": "e17c8b935548619c0094588563bd471a9c33b338f1a57a1caf60e48a9ee00ae8",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePartyCollateralService.cs": 'ff97f2ec411a72cc3ae7ca57038d0e94447db856da2423dbd09cee8de08629bb',
    "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Bootstrap/FounderBootstrapService.cs": "9dd6232849741c9f396415761baab66dd62c1441649fc25bd63f3c4c5f19d0aa",
    "src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityAccessAssignmentService.Plumbing.cs": "bbb6d1a13710800065c8f8274e1b32b9461b93533d25dc49fad45fd666d95916",
    "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseGoodsReceiptBatchStore.cs": "d8f149c014a8c0323888478cecc57a91e2b9a9caceef61670355995da6020c08",
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Contracts.cs": "f4014c66f561b649148f36f594c4076dfa02f26a4831c41d0401ea0573e79e2d",
    "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs": "298f6a269c2eab1827bc5bf6afdea08e2dda08d7f02a06234c4ad1e44b18aec5",
    ACCOUNTING_SOURCE_DOCUMENT_ADMISSION_STORE: "da331a72251cd610f4dcd1f755c88a174b426a12158f164bbe65e9ced98c3dc5",
    ACCOUNTING_STORE: '9753a303d40da5210fe67cb2f542edf6c293ebe318ddfaeed8e579d4b8df3368',
    DOCUMENT_MODULE_PATH: "daf96c1173fd67e0374d6c2b33a15df758aaae54fd9ee527e13e2849b0d5c8a6",
    DOCUMENTS_STORE: "5650ad7c0588f9b054964b578ab26fdc7d6d10a8a6ba646e9ecf4c5ff4714217",
    DOCUMENT_SCANNER_PATH: "39686c28f47ddb2f65085e7587241141426641b069ab8079ac1a93542d144189",
    "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/FileSystemDocumentObjectStore.cs": "8c7a2eed1888d36d0b61c60b1296d904adaf0338c34f361236befc8e1c1d6274",
    "src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Storage/ServerDirectoryDocumentObjectStore.cs": "0f79216993cdbbc20d7c92d830ad1c08f3ff2ab5b0ab9084aaae03f765784c1a",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialInvoiceImportApprovalServiceV1.cs": "573d1b13dafb963b6521a099600130a759711d016f22c62529c3da0b2227a1af",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCashAccountService.cs": '88673cddf3d04e897a9232759fcc3bb95de70f32c103bcb7b376570f15c13244',
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceOpenItemCancellationService.cs": 'c399f24befdd07edfcca0947fd4bde624016765567e9dc8003d9be0dbe8693d0',
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOfficialSalesInvoiceServiceV1.cs": '7ad45f56c7c70c7f951530fb407c4af9e3a42d0031df86ed94a6cf4f3df12f65',
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpenItemSourceAdmissionService.cs": 'de1afdfe2c221e5bb9fe9eaa49f9ae1763aaf8b5ec127f3de6f8b9f6b79f8c62',
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ReceiptDraftService.cs": '60f18042fcef1d300340fdfdc6c4bd34c689cbb5a11f8eec14a046ae9e62f7ff',
    "src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Executions/SqlGroupSharedCommandTerminalStore.cs": "5ecf927a5505ff79b5d2f3d79e436dc674d1b98aa05db28c8ae6e6669ac9b301",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/InventoryOpeningCommandService.cs": "fd384d4cdc34ec642db71049b1dbd0180f2d317f38acfd3a6758d943d571bebb",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryCustomerReturnParticipant.cs": "1652ab5f2d4ed5317948e93bca102230ff2c6bd46af392eea745c2535d49e43b",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventoryDefiniteShipmentParticipant.cs": '5c6f5106974dcf98dd6da309256d867bad49db9184cc9256358645106a84c93a',
    "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsCustomerReturnStore.cs": "06808d6fe56293d16cf04d41fdff20505dcc0ed1862471ceae600e48a30c91df",
    "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsStore.cs": "20c61595c4c05a84097373db7cf44ba8078514f430df459fb98ad75ea0bca290",
    "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsTransferOutgoingDocumentStore.cs": '9cad954953c033049b2b879ba47a16a6c9cde69fd0449673e1f6b4228ea2b918',
    "src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/ManufacturingStore.cs": "e49f4fb8bc094d5fca5ff76a60a578408e38c9ae4583062b5f3ff2964d640d0c",
    "src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/OrganizationService.cs": "c8225e56247f665a1aed783aca150d4f8d8a06ff33fa22eebf0b7812cc6c8389",
    "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPriceListStore.cs": "0ec797ef39bd37ee97ffb8a945de0c7fd515a38d2ee6891914e6dac6e3e2362d",
    "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductItemTypeService.cs": "3f7ba4e6a381dfc4d6c94655c866e027153f6a7e669a4ffc20bde33d7829d6bd",
    "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseAcceptanceParticipant.cs": '72ae3cae1dae9eff49d017cec110e2e1b62a28740d649c9f88ae624903bfde08',
    "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchasingStore.cs": 'b2e983d8ca8a4d129594a9854da7fc8575c8046f5d8a9cdf00ab538f331fa8f8',
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.cs": '40e8cbe7e8b23c89ce388c314af2f106058d6ecadd156eb2d613b646a0716315',
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Invoices.cs": "71c50971d757c3410b329f4accf4b3a8ff0edf852c63047fe69d6e847a35efe9",
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.InvoiceFinalization.cs": "279acce00504aed41446fc373a7c1f050a6b74d7913dc3f44852069ba5728041",
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Quotes.cs": "b250ecebc925f96e25389d8d9adb17f43825974325a2d4130459785ec9ad4112",
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.Requests.cs": "de60ec41c77f850799aa125e2a61c8e24b5c7de26068851e474790df86a38d91",
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.RiskExceptions.cs": "1a30f5ef3b3d0c13833f98920ca74fa6900472b13c85d74cd06a263e346868ba",
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.CustomerReturns.cs": "a238cb434ea935fbdd0a10e83366f1442560b4a30e33783f254ec43f8bd1044d",
    "src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkDesiredStateStoreV2.cs": "c8b78e13e5a6b5d2075730346a718dbf48672ca47356de212271240a7f76b472",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceInvoiceAcceptanceService.cs": "8949362fef136f4fb3d386603649c42f5acb5c8d20d19efdcdcc8e5b28d3d45f",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/ExpensePaymentService.cs": "26828b64480b92a844fce7590413ed7100fc5727b164b7f8e06cc735e6a77de8",
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinancePartyCreditLimitService.cs": "e31360adde051e12f46f433cb831f044338a085dbd64368f18098ddcc3f4cb04",
    "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductDimensionService.cs": "917f76d56262c646943d23540c306f9eecbf035fe77fbeae18c7ef309cb2e115",
    # tren-1 wave 3 (stok_lojistik): new live writers; K3 review pending (stok).
    "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductProfileWeightTableService.cs": "23fbd437a6ce008fba062e652a547692f2fe70f07ddad6525225736f2568f625",
    "src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/InventorySupplierReturnExitParticipant.cs": "a1c12b96cb7e9091bc04ae375040ed8b4fc04457001bcf180102e18e0d0263e6",
    # tren-1 wave 5 integration: values from print_recovery_actuals.py; K3 review pending.
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceCreditAccountService.cs": '8c3cd1d3609ee51f45ce933e387e199e5e27a10da084b8982a3ef3864b072d21',
    "src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/FinanceOpeningBalanceService.cs": "b264220af4b1147e09e305d325aa3ae1fc3d62fca63d580b4c585664a4c0801c",
    "src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/LogisticsWeighingDifferenceStore.cs": "6a8f49845400b0d6380441734e2b68434e8004cdf639615752ece8be239d433b",
    "src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/ProductPackageStandardService.cs": "b44f4ff760e074b6a6f7ebf1a5c1f1186e16cc269e7eba7bc0a299b035cdd573",
    "src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/PurchaseSupplierReturnStore.cs": '55e8a86217760de4e1ffc1d6cc7a49ffc3f44347b374bd890d31ccaa9a9639ad',
    "src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesStore.SubcontractServiceInvoices.cs": "ca6451e77c725c5f23e3110045adef1041bd96f846e152c9228954ff170f1b02",
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


_PURE_TEXT_CACHE_LIMIT = 16384
_STRIPPED_CSHARP_CACHE: dict[str, str] = {}
_LIVE_CSHARP_CACHE: dict[str, str] = {}


def _cached_text_transform(cache: dict[str, str], content: str, transform: Any) -> str:
    """Memoize a pure str -> str transform; failures raise and are never cached."""

    cached = cache.get(content)
    if cached is None:
        cached = transform(content)
        if len(cache) >= _PURE_TEXT_CACHE_LIMIT:
            cache.clear()
        cache[content] = cached
    return cached


def _strip_csharp_comments(content: str) -> str:
    """Remove C# comments while preserving strings and line structure."""

    return _cached_text_transform(_STRIPPED_CSHARP_CACHE, content, _strip_csharp_comments_uncached)


def _strip_csharp_comments_uncached(content: str) -> str:
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

    return _cached_text_transform(_LIVE_CSHARP_CACHE, content, _live_csharp_code_uncached)


def _live_csharp_code_uncached(content: str) -> str:
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
    # Evidence only: recomputed from the exact commit so the record cannot be forged, but the
    # full-tree digest is never compared with a pinned constant (post-hoc evidence, not a merge block).
    if inventory["sourceSurface"] != _source_surface_record(snapshot):
        raise InventoryError("Inventory sourceSurface evidence does not match the exact commit.")


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


def _surface_fingerprint(snapshot: SourceSnapshot, paths: Sequence[str]) -> tuple[int, str]:
    digest = hashlib.sha256()
    for path in paths:
        encoded_path = path.encode("utf-8")
        content = snapshot.files[path]
        digest.update(len(encoded_path).to_bytes(8, "big"))
        digest.update(encoded_path)
        digest.update(len(content).to_bytes(8, "big"))
        digest.update(content)
    return len(paths), digest.hexdigest()


def _source_surface_fingerprint(snapshot: SourceSnapshot) -> tuple[int, str]:
    """Full src/backend + build-control byte digest: post-hoc evidence, never pinned."""

    return _surface_fingerprint(snapshot, _source_surface_paths(snapshot))


_RISKY_ALIAS_PATTERN = re.compile(
    r"\busing\s+(?:static\s+)?(?:unsafe\s+)?@?[^\W\d]\w*\s*(?:<[^;=]*>)?\s*="
)
_RISKY_GLOBAL_USING_PATTERN = re.compile(r"\bglobal\s+using\b")
_RISKY_GLOBAL_ALIAS_NAME_PATTERN = re.compile(
    r"\bglobal\s+using\s+(?:static\s+)?(?:unsafe\s+)?@?(?P<alias>[^\W\d]\w*)\s*="
    r"\s*(?P<target>[^;]*);"
)
_RISKY_TYPE_DECLARATION_PATTERN = re.compile(
    r"\b(?P<kind>class|struct|interface|record|enum)\s+(?:(?:class|struct)\s+)?"
    r"@?(?P<name>[^\W\d]\w*)"
)
# Delegate types also declare a simple name (`public delegate void Task();`) that shadows an
# imported type; the return type may be generic, qualified, an array, nullable or a tuple.
_RISKY_DELEGATE_DECLARATION_PATTERN = re.compile(
    r"\bdelegate\s+(?:ref\s+(?:readonly\s+)?)?(?:[^;(){}=]|\([^()]*\))*?"
    r"(?<![\w@])@?(?P<name>[^\W\d]\w*)\s*(?:<(?:[^;(){}<>]|<(?:[^;(){}<>]|<[^;(){}<>]*>)*>)*>)?\s*\("
)
_RISKY_PARTIAL_DECLARATION_PATTERN = re.compile(
    r"\bpartial\s+(?:class|struct|interface|record(?:\s+(?:class|struct))?)\s+"
    r"@?(?P<name>[^\W\d]\w*)"
)
_RISKY_NAMESPACE_PATTERN = re.compile(r"\bnamespace\s+(?P<name>@?[^\W\d][\w.@]*)")
_CSHARP_KEYWORDS = frozenset(
    "abstract as base bool break byte case catch char checked class const continue decimal "
    "default delegate do double else enum event explicit extern false finally fixed float for "
    "foreach goto if implicit in int interface internal is lock long namespace new null object "
    "operator out override params private protected public readonly ref return sbyte sealed "
    "short sizeof stackalloc static string struct switch this throw true try typeof uint ulong "
    "unchecked unsafe ushort using virtual void volatile while".split()
)
_RISKY_IDENTIFIER_PATTERN = re.compile(r"[^\W\d]\w*")
_RISKY_CLASS_BASE_PATTERN = re.compile(
    r"\b(?:class|record)\s+@?[^\W\d]\w*[^{;:]*:\s*(?P<base>[\w.@:]+)"
)


def _reviewed_source_paths() -> set[str]:
    paths = set(REVIEWED_LIVE_SOURCE_SHA256)
    paths.update(path for path, _ in REVIEWED_OPERATIVE_METHOD_FINGERPRINTS)
    paths.update(path for _, path in WRITER_METHOD_SCOPES)
    paths.update(DOCUMENT_SOURCE_PATHS)
    paths.add(DOCUMENT_OBJECT_STORE_INTERFACE_PATH)
    paths.update(EXPECTED_OUTBOX_CONTRACT_PATHS)
    for spec in DURABLE_STORE_SPECS:
        paths.update(evidence.path for evidence in spec.evidence)
    return paths


def _risky_text(snapshot: SourceSnapshot, relative_path: str) -> str:
    try:
        text = snapshot.files[relative_path].decode("utf-8")
    except UnicodeError as exc:
        raise InventoryError(f"Backend source blob is not readable UTF-8: {relative_path}.") from exc
    return text[1:] if text.startswith("\ufeff") else text


def _namespace_and_ancestors(name: str) -> set[str]:
    parts = [part.removeprefix("@") for part in name.split(".")]
    return {".".join(parts[: index + 1]) for index in range(len(parts))}


_RISKY_SURFACE_CACHE: dict[
    tuple[tuple[str, bytes], ...],
    tuple[tuple[str, ...], tuple[tuple[str, str, str], ...]],
] = {}
_RISKY_SURFACE_CACHE_LIMIT = 8


def _risky_source_surface(
    snapshot: SourceSnapshot,
) -> tuple[tuple[str, ...], tuple[tuple[str, str, str], ...]]:
    """Memoize the narrow risky surface by exact snapshot content (a pure function of it)."""

    key = tuple(sorted(snapshot.files.items()))
    cached = _RISKY_SURFACE_CACHE.get(key)
    if cached is None:
        cached = _compute_risky_source_surface(snapshot)
        if len(_RISKY_SURFACE_CACHE) >= _RISKY_SURFACE_CACHE_LIMIT:
            _RISKY_SURFACE_CACHE.clear()
        _RISKY_SURFACE_CACHE[key] = cached
    return cached


def _compute_risky_source_surface(
    snapshot: SourceSnapshot,
) -> tuple[tuple[str, ...], tuple[tuple[str, str, str], ...]]:
    """Return the narrow risky surface: byte-pinned paths and the shadow-declaration index.

    The byte-pinned paths are a token/file-class superset (see the Faz 1 note).  Tokens and
    aliases match comment-free text with literals and every preprocessor region kept (wider than
    live code; comments never compile).  Declarations, namespaces and base lists use comment-free,
    literal-masked text so prose and strings cannot invent type names.
    """

    backend_prefix = f"{BACKEND_ROOT.as_posix()}/".casefold()
    build_controls = {path.casefold() for path in REPOSITORY_BUILD_CONTROL_PATHS}
    selected: set[str] = set()
    csharp: dict[str, str] = {}
    reviewed_documents = _reviewed_source_paths()
    for path in snapshot.files:
        folded = path.casefold()
        if folded in build_controls:
            selected.add(path)
        elif folded.startswith(backend_prefix):
            if folded.endswith(".cs"):
                csharp[path] = _strip_csharp_comments(_risky_text(snapshot, path))
            elif not folded.endswith(".md") or path in reviewed_documents:
                # Markdown is documentation (it can only compile through a pinned project file);
                # every other backend input (*.csproj, *.props, *.targets, *.rsp, *.csproj.user,
                # appsettings*.json, *.resx, fonts...) is byte-pinned.
                selected.add(path)
    structural = {path: _mask_csharp_strings(text) for path, text in csharp.items()}
    declarations = {
        path: (
            *(
                (match.group("kind"), match.group("name"))
                for match in _RISKY_TYPE_DECLARATION_PATTERN.finditer(text)
                if match.group("name") not in _CSHARP_KEYWORDS
            ),
            *(
                ("delegate", match.group("name"))
                for match in _RISKY_DELEGATE_DECLARATION_PATTERN.finditer(text)
                if match.group("name") not in _CSHARP_KEYWORDS
            ),
        )
        for path, text in structural.items()
    }

    global_aliases = {
        (match.group("alias"), match.group("target"))
        for text in csharp.values()
        for match in _RISKY_GLOBAL_ALIAS_NAME_PATTERN.finditer(text)
    }
    word_tokens: set[str] = set()
    reviewed_paths = {path for path in _reviewed_source_paths() if path in csharp}
    discovered = (
        *_discover_csharp_subclasses(snapshot, "DbContext"),
        *_discover_csharp_subclasses(snapshot, "Migration"),
    )
    adapters = _discover_document_object_store_adapters(snapshot)
    reviewed_paths.update(declaration.path for declaration in discovered)
    reviewed_paths.update(path for _, _, path in adapters)
    reviewed_types = {declaration.name for declaration in discovered}
    reviewed_types.update(name for _, name, _ in adapters)
    word_tokens.update(
        declaration.name for declaration in discovered if "Migration" not in declaration.name
    )
    word_tokens.update(name for _, name, _ in adapters)
    # A partial needs the reviewed declaration to be partial too; non-partial same-name types in
    # other namespaces are covered by the shadow index below.
    for path in reviewed_paths:
        reviewed_types.update(
            match.group("name")
            for match in _RISKY_PARTIAL_DECLARATION_PATTERN.finditer(structural[path])
        )
    # A global alias whose target is risky (a token, a reviewed type or another risky alias)
    # makes its alias name a risky token in every file; other global aliases only pin their
    # own declaring file.
    while True:
        risky_aliases = {
            alias
            for alias, target in global_aliases
            if alias not in word_tokens
            and (
                any(token in target for token in RISKY_SOURCE_TOKENS)
                or not set(_RISKY_IDENTIFIER_PATTERN.findall(target)).isdisjoint(
                    reviewed_types | word_tokens
                )
            )
        }
        if not risky_aliases:
            break
        word_tokens.update(risky_aliases)
    word_pattern = (
        re.compile(
            r"(?<![A-Za-z0-9_])(?:"
            + "|".join(re.escape(token) for token in sorted(word_tokens))
            + r")(?![A-Za-z0-9_])"
        )
        if word_tokens
        else None
    )

    risky: set[str] = set(reviewed_paths)
    for path, text in csharp.items():
        namespace_count = len(_RISKY_NAMESPACE_PATTERN.findall(structural[path]))
        if (
            namespace_count > 1
            or any(token in text for token in RISKY_SOURCE_TOKENS)
            or (word_pattern is not None and word_pattern.search(text) is not None)
            or _RISKY_ALIAS_PATTERN.search(text) is not None
            or _RISKY_GLOBAL_USING_PATTERN.search(text) is not None
            or any(name in reviewed_types for _, name in declarations[path])
        ):
            risky.add(path)

    # Class base types of risky declarations are byte-pinned when declared here as a class or
    # record: a partial of a base class can inject a nested type into the derived scope.
    base_types: set[str] = set()
    for path in risky:
        for match in _RISKY_CLASS_BASE_PATTERN.finditer(structural[path]):
            first_base = _RISKY_IDENTIFIER_PATTERN.findall(match.group("base"))
            if first_base:
                base_types.add(first_base[-1])
    for path, declared in declarations.items():
        if path not in risky and any(
            kind.startswith(("class", "record")) and name in base_types
            for kind, name in declared
        ):
            risky.add(path)

    # Symbol shadowing: a type declared in a namespace that equals or encloses a risky file's
    # namespace wins over that file's using-imports.  Every such declaration whose simple name
    # occurs in an enclosed risky file is pinned by (path, namespace, name): introducing,
    # moving or renaming a shadowing type moves the pin, while the declaring file's body stays
    # outside the byte pin (a new name used by a risky file changes that risky file anyway).
    risky_identifiers: dict[str, set[str]] = {}
    for path in risky:
        identifiers = set(_RISKY_IDENTIFIER_PATTERN.findall(csharp[path]))
        risky_identifiers.setdefault("", set()).update(identifiers)
        for match in _RISKY_NAMESPACE_PATTERN.finditer(structural[path]):
            for namespace in _namespace_and_ancestors(match.group("name")):
                risky_identifiers.setdefault(namespace, set()).update(identifiers)
    shadow_index: set[tuple[str, str, str]] = set()
    for path, text in structural.items():
        if path in risky:
            continue
        # A file without a namespace declares into the global namespace, which encloses all.
        declared_namespace = _RISKY_NAMESPACE_PATTERN.search(text)
        namespace = (
            ""
            if declared_namespace is None
            else ".".join(
                part.removeprefix("@") for part in declared_namespace.group("name").split(".")
            )
        )
        used = risky_identifiers.get(namespace, set())
        shadow_index.update(
            (path, namespace, name) for _, name in declarations[path] if name in used
        )
    selected.update(risky)
    return tuple(sorted(selected)), tuple(sorted(shadow_index))


def _risky_source_surface_paths(snapshot: SourceSnapshot) -> tuple[str, ...]:
    return _risky_source_surface(snapshot)[0]


def _risky_source_surface_fingerprint(snapshot: SourceSnapshot) -> tuple[int, str]:
    paths, shadow_index = _risky_source_surface(snapshot)
    count, digest = _surface_fingerprint(snapshot, paths)
    combined = hashlib.sha256(digest.encode("ascii"))
    for path, namespace, name in shadow_index:
        combined.update(f"\n{path}\t{namespace}\t{name}".encode("utf-8"))
    return count, combined.hexdigest()


def _require_reviewed_source_surface(snapshot: SourceSnapshot) -> None:
    actual_count, actual_fingerprint = _risky_source_surface_fingerprint(snapshot)
    if (
        actual_count != EXPECTED_RISKY_SOURCE_SURFACE_FILE_COUNT
        or actual_fingerprint != EXPECTED_RISKY_SOURCE_SURFACE_FINGERPRINT
    ):
        raise InventoryError(
            "Reviewed risky C# compilation source surface drifted: "
            f"expected count={EXPECTED_RISKY_SOURCE_SURFACE_FILE_COUNT} "
            f"fingerprint={EXPECTED_RISKY_SOURCE_SURFACE_FINGERPRINT}; "
            f"actual count={actual_count} fingerprint={actual_fingerprint}."
        )


def _source_surface_record(snapshot: SourceSnapshot) -> dict[str, Any]:
    """Non-blocking evidence: the manifest sourceSurface is post-hoc evidence, not a merge block."""

    file_count, sha256 = _source_surface_fingerprint(snapshot)
    risky_count, risky_sha256 = _risky_source_surface_fingerprint(snapshot)
    return {
        "fileCount": file_count,
        "sha256": sha256,
        "riskyFileCount": risky_count,
        "riskySha256": risky_sha256,
        "blocking": False,
    }


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
        "sourceSurface": _source_surface_record(snapshot),
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
