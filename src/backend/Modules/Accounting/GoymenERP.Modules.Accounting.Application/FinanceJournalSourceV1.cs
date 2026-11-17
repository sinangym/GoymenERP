using GoymenERP.BuildingBlocks.Messaging;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>An immutable received Finance fact. Received is not a prepared or posted journal.</summary>
public sealed record FinanceJournalSourceViewV1(Guid EventId, Guid CompanyId, Guid SourceId, string SourceKind,
    string SourceVersion, Guid CurrencyId, string Amount, string OwnerProfileId, string OwnerProfileVersion,
    DateOnly OccurredOn, DateTimeOffset RecordedAtUtc, DateTimeOffset ReceivedAtUtc, string Status,
    string MappingStatus, string? PreparationVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FinanceCashExchangeValueV1? CashExchange = null);
public sealed record FinanceCashExchangeValueV1(Guid TransferId, Guid? BookValueId, Guid SourceCurrencyId, string SourceAmount,
    Guid TargetCurrencyId, string TargetAmount, string? SourceBookValueTl, string PostingState);
public interface IFinanceCashExchangeJournalSourceProviderV1
{
    Task<NormalJournalDraftServerSourceV1?> ResolveAsync(Guid companyId, Guid sourceId, CancellationToken cancellationToken);
}
public sealed record FinanceJournalSourceListV1(Guid CompanyId, IReadOnlyList<FinanceJournalSourceViewV1> Items);
public interface IFinanceJournalSourceStoreV1
{
    Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinanceJournalSourceViewV1>> ListAsync(CancellationToken cancellationToken);
}
public static class FinanceJournalSourceInboxV1
{
    public const string ConsumerName = "accounting.finance-journal-source";
    public const string SourceName = "finance.outbox.v1";
    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } =
    [
        new(SourceName, NormalJournalSourceKindsV1.RecordedExpensePayment, 1),
        new(SourceName, NormalJournalSourceKindsV1.RecordedCashTransfer, 1),
        new(SourceName, NormalJournalSourceKindsV1.RecordedCashExchange, 1),
        new(SourceName, NormalJournalSourceKindsV1.ApprovedCashExchangeBookValue, 1)
    ];
}
public sealed class FinanceJournalSourceInboxConsumerV1(IFinanceJournalSourceStoreV1 store) : IWorkInboxConsumerV1
{
    public Task HandleAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken) =>
        store.AcceptAsync(envelope, cancellationToken);
}
