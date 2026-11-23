using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<CreditMovementKindV1>))]
public enum CreditMovementKindV1 { Drawdown = 1, Repayment = 2, InterestAccrual = 3, OpeningBalance = 4 }
[JsonConverter(typeof(JsonStringEnumConverter<CreditMovementStatusV1>))]
public enum CreditMovementStatusV1 { Proposed = 1, Approved = 2, Rejected = 3 }
public sealed record CreditMovementComponentV1(string Kind, string Amount, string? FunctionalAmount);
public sealed record CreditBankEffectV1(Guid BankAccountId, string Amount, string SourceReference);

/// <summary>Documented actual facts, separate from the contract/repayment plan. Null TL is unknown, never converted or filled.</summary>
public sealed record CreditMovementProposeV1(Guid CreditAccountId, string ExpectedCreditVersion,
    CreditMovementKindV1 Kind, CreditSourceDocumentV1 SourceDocument, DateOnly TransactionDate,
    DateOnly AccountingDate, IReadOnlyList<CreditMovementComponentV1> Components, CreditBankEffectV1? BankEffect,
    Guid? ReplacesOpeningMovementId, string? ExpectedOpeningMovementVersion, string Reason);
public sealed record CreditMovementDecisionV1(Guid MovementId, string ExpectedVersion, bool Approve, string? Reason);
public sealed record CreditMovementDecisionBodyV1(string ExpectedVersion, bool Approve, string? Reason);
public sealed record CreditMovementViewV1(Guid Id, Guid CompanyId, string Version, CreditMovementStatusV1 Status,
    CreditMovementProposeV1 Source, Guid CurrencyId, bool IsOwnProposal, DateTimeOffset ProposedAtUtc,
    DateTimeOffset? DecidedAtUtc, string? RejectionReason, Guid? ReversedByMovementId,
    string? OpeningPeriodVersion, CreditPlanToleranceSnapshotV1 MoneyProfile);
public sealed record CreditMovementPageV1(Guid CompanyId, IReadOnlyList<CreditMovementViewV1> Items);
public sealed record CreditMovementResultV1(CommandOutcome Outcome, CreditMovementViewV1? Resource, bool IsReplay,
    string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);
public interface IFinanceCreditMovementServiceV1
{
    Task<CreditMovementResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        CreditMovementProposeV1 request, CancellationToken cancellationToken);
    Task<CreditMovementResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata,
        CreditMovementDecisionV1 request, CancellationToken cancellationToken);
    Task<ReadResult<CreditMovementPageV1>> ListAsync(FinanceContext context, Guid creditAccountId, CancellationToken cancellationToken);
}

/// <summary>One immutable actual movement. Accounting may create only a suggestion, never an automatic final journal.</summary>
public sealed record FinanceCreditMovementRecordedV1(Guid EventId, Guid CompanyId, Guid MovementId, Guid CreditAccountId,
    Guid CurrencyId, CreditMovementKindV1 Kind, CreditSourceDocumentV1 SourceDocument, DateOnly TransactionDate,
    DateOnly AccountingDate, IReadOnlyList<CreditMovementComponentV1> Components, CreditBankEffectV1? BankEffect,
    Guid? ReversesMovementId, string ApprovedBy, string Reason);
public static class FinanceCreditMovementEventsV1
{
    public const string SourceName = "finance.outbox.v1";
    public const string Recorded = "finance.credit-movement.recorded.v1";
    public const int SchemaVersion = 1;
}

/// <summary>Credit-specific mapping: an explicit correction date is not a bank receipt's occurredOn rule.</summary>
public interface IFinanceCreditAccountingPeriodPortV1
{
    Task<RecordPeriodAnswer> ReadAsync(FinanceContext context, DateOnly accountingDate, CancellationToken cancellationToken);
}
public sealed class UnavailableFinanceCreditAccountingPeriodPortV1 : IFinanceCreditAccountingPeriodPortV1
{
    public Task<RecordPeriodAnswer> ReadAsync(FinanceContext context, DateOnly accountingDate, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(RecordPeriodAnswer.Unavailable); }
}
