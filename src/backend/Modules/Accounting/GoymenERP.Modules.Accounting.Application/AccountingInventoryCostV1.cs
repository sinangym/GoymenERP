using GoymenERP.BuildingBlocks.Messaging;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Different economic sources retain different identities and posting purposes. Neither is a second stock movement.</summary>
public enum AccountingInventoryCostKindV1 { SalesInvoicedShipment = 1, PurchaseInvoiceDifference = 2 }

/// <summary>Exact owner amount. Receiving or adapting a source never rounds it to make it postable.</summary>
public sealed record AccountingInventoryCostRatioV1(string Numerator, string Denominator);

/// <summary>
/// Sales uses one component per invoice line; its evidence names the whole claimed dispatch line and every actual
/// Inventory movement line selected by the Logistics owner proof. Commercial invoice kg is not a physical-cost ratio.
/// Purchasing uses one component per Product/location residual; opposite signed components are never netted away.
/// SourceProofJson contains immutable owner facts, not a request from the preparer's browser.
/// </summary>
public sealed record AccountingInventoryCostComponentV1(string ComponentKey, Guid ProductId,
    Guid? InvoiceLineId, int? InvoiceLineNumber, Guid? LocationId, AccountingInventoryCostRatioV1 SignedTlAmount,
    string SourceProofJson, string SourceProofSha256);

public sealed record AccountingInventoryCostSourceV1(Guid CompanyId, Guid EventId, AccountingInventoryCostKindV1 Kind,
    Guid InvoiceId, long InvoiceVersion, Guid AdmissionId, DateOnly InvoiceDate, string? OfficialInvoiceNumber,
    Guid? FinalizedBy, string SourcePayloadSha256, IReadOnlyList<AccountingInventoryCostComponentV1> Components);

/// <summary>
/// Projection is supplied by actual owner ports and a committed event. A missing immutable shipment stamp or
/// a mismatched dispatch/invoice binding is unavailable; it is not replaced by a current average or invoice price.
/// </summary>
public interface IAccountingInventoryCostSourceReaderV1
{
    Task<AccountingInventoryCostSourceV1> ReadAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default);
}

public sealed record AccountingInventoryCostClassificationV1(InvoiceLineCogsClassificationV1 Classification,
    Guid AccountId, string Origin, string? ProductVersion, string? ClassificationVersion,
    long? MappingVersion, long InvoiceLineRevision);

public sealed record AccountingInventoryCostPreparationV1(Guid PeriodId, DateOnly AccountingDate,
    DateOnly TransactionDate, Guid CurrencyId, string SignedAmount, Guid MoneyProfileId, long MoneyProfileVersion, int MoneyScale,
    AccountingInventoryCostClassificationV1 Classification, InvoiceLineCogsAccountV1 CounterAccount,
    Guid AccountPlanPackageId, long AccountPlanPackageVersion, long? AccountPlanCatalogVersion,
    string AccountPlanSourceVersion, string AccountPlanSha256, Guid PreparedBy, DateTimeOffset PreparedAtUtc, string Reason);

public sealed record AccountingInventoryCostProposalV1(Guid ProposalId, Guid CompanyId, Guid EventId,
    AccountingInventoryCostKindV1 Kind, Guid InvoiceId, long InvoiceVersion, Guid AdmissionId, DateOnly InvoiceDate,
    string? OfficialInvoiceNumber, AccountingInventoryCostComponentV1 Component, long Version, string Status,
    DateTimeOffset SourceOccurredAtUtc, AccountingInventoryCostPreparationV1? Preparation = null,
    Guid? ApprovedBy = null, DateTimeOffset? ApprovedAtUtc = null, Guid? JournalId = null,
    Guid? CancellationId = null, AccountingJournalReversalProposalViewV1? ReversalProposal = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CurrentPostedJournalId = null)
{
    public string ExactTlAmount => AccountingInventoryCostAmountsV1.TryExactDecimal(Component.SignedTlAmount, 28, out var amount)
        ? amount.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture)
        : Component.SignedTlAmount.Numerator + " / " + Component.SignedTlAmount.Denominator;
}

public sealed record AccountingInventoryCostListV1(Guid CompanyId, IReadOnlyList<AccountingInventoryCostProposalV1> Items);
public sealed record PrepareAccountingInventoryCostV1(long ExpectedVersion, Guid PeriodId, DateOnly AccountingDate,
    DateOnly TransactionDate, Guid CounterAccountId, string Reason);
public sealed record ProposeAccountingInventoryCostReversalV1(long ExpectedVersion, Guid ProposalId,
    Guid PeriodId, DateOnly AccountingDate, DateOnly TransactionDate, string Reason);

public interface IAccountingInventoryCostStoreV1
{
    Task RecordAttemptAsync(AccountingCommandRecord record, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default);
    Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind, CancellationToken cancellationToken = default);
    Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken cancellationToken = default);
    Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid proposalId, AccountingInventoryCostKindV1 kind,
        PrepareAccountingInventoryCostV1 request, Guid actorId, Func<CancellationToken, Task> freshFence,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid proposalId, AccountingInventoryCostKindV1 kind,
        long expectedVersion, Guid actorId, Func<CancellationToken, Task> freshFence,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid proposalId, ProposeAccountingInventoryCostReversalV1 request,
        Guid actorId, Func<CancellationToken, Task> freshFence, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public sealed class AccountingInventoryCostInboxConsumerV1(IAccountingInventoryCostStoreV1 store) : IWorkInboxConsumerV1
{
    public Task HandleAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken) => store.AcceptAsync(envelope, cancellationToken);
}

public static class AccountingInventoryCostInboxV1
{
    public const string ConsumerName = "accounting.inventory-cost-proposals";
    public const string SalesSourceName = "sales";
    public const string SalesEventType = "sales.invoice.finalized.v1";
    public const string InventorySourceName = "inventory.outbox.v1";
    public const string PurchaseDifferenceEventType = "inventory.purchase-invoice-cost-difference.proposed.v1";
    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } =
        [new(SalesSourceName, SalesEventType, 1), new(InventorySourceName, PurchaseDifferenceEventType, 1)];
}

public static class AccountingInventoryCostTraceV1
{
    public const string PurchaseDifferencePurpose = "purchase-invoice-cost-difference";

    // SourceId for a first Sales component is InvoiceId, matching the existing SAT-D422 posted-evidence owner.
    public static string SalesPurpose(Guid invoiceLineId, long finalizedVersion) =>
        InvoiceLineCogsPostingTraceV1.For(invoiceLineId, finalizedVersion);

    public static string SalesComponent(Guid invoiceLineId) => invoiceLineId != Guid.Empty
        ? "invoice-line:" + invoiceLineId.ToString("D") : throw new ArgumentException("A real invoice line is required.");

    public static string PurchaseComponent(Guid productId, Guid locationId) => productId != Guid.Empty && locationId != Guid.Empty
        ? "product-location:" + productId.ToString("D") + ":" + locationId.ToString("D")
        : throw new ArgumentException("A real product and original receipt location are required.");
}
