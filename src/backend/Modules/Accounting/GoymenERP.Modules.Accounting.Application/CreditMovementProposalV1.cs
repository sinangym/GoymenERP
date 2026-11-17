using GoymenERP.BuildingBlocks.Messaging;

namespace GoymenERP.Modules.Accounting.Application;

// Consumer-owned projection of Finance v1. String amounts remain exact; an unknown functional amount stays null.
public sealed record CreditMovementSourceDocumentV1(string Reference, DateOnly DocumentDate, string Revision);
public sealed record CreditMovementSourceComponentV1(string Kind, string Amount, string? FunctionalAmount);
public sealed record CreditMovementSourceBankV1(Guid BankAccountId, string Amount, string SourceReference);
public sealed record CreditMovementSourceV1(Guid EventId, Guid CompanyId, Guid MovementId, Guid CreditAccountId,
    Guid CurrencyId, string Kind, CreditMovementSourceDocumentV1 SourceDocument, DateOnly TransactionDate,
    DateOnly AccountingDate, IReadOnlyList<CreditMovementSourceComponentV1> Components, CreditMovementSourceBankV1? BankEffect,
    Guid? ReversesMovementId, string ApprovedBy, string Reason);
public sealed record CreditMovementProposalViewV1(Guid ProposalId, Guid CompanyId, string Status,
    CreditMovementSourceV1 Source, string MappingStatus, long? MappingVersion, IReadOnlyList<string> WaitingReasons,
    DateTimeOffset ReceivedAtUtc);
public sealed record CreditMovementProposalListV1(Guid CompanyId, IReadOnlyList<CreditMovementProposalViewV1> Items);
public interface ICreditMovementProposalStoreV1
{
    Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken);
    Task<IReadOnlyList<CreditMovementProposalViewV1>> ListAsync(CancellationToken cancellationToken);
}
public interface ICreditMovementNormalJournalSourceProviderV1 : INormalJournalDraftServerSourceProviderV1 { }
public static class CreditMovementProposalInboxV1
{
    public const string SourceName = "finance.outbox.v1";
    public const string EventType = "finance.credit-movement.recorded.v1";
    public const string ConsumerName = "accounting.credit-movement-proposal";
    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } = [new(SourceName, EventType, 1)];
}
