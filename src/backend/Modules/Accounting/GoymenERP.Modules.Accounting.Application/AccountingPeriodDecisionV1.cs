namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Approval-purpose reads only. Neither method grants the general period-list permission.</summary>
public interface IAccountingPeriodDecisionReadV1
{
    Task<AccountingApiResult<AccountingPeriodOpeningDecisionDetailV1>> ReadOpeningDecisionAsync(Guid periodId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<AccountingApiResult<AccountingPeriodReopeningDecisionDetailV1>> ReadReopeningDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record AccountingPeriodOpeningDecisionDetailV1(PeriodViewV1 Period, string SourceFactsDigest, string PreviewDigest);
public sealed record AccountingPeriodReopeningDecisionDetailV1(AccountingPeriodReopeningViewV1 Reopening,
    PeriodViewV1 Period, AccountingPeriodClosureViewV1 Closure, string SourceFactsDigest, string PreviewDigest)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool? LaterPeriodClosed { get; init; }
}
public sealed record AccountingPeriodApprovalReceiptV1(Guid CompanyId, Guid PeriodId, Guid? ReopeningId,
    string CommandName, string IdempotencyKeyHash, string PayloadHash, Guid ActorId, long ExpectedVersion,
    long? ExpectedPeriodVersion, long ResultVersion, string OriginalCorrelationId, DateTimeOffset ApprovedAtUtc,
    string SourceFactsDigest, string OriginalResultDigest)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool? LaterPeriodClosed { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public DateOnly? PeriodStartsOn { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public DateOnly? PeriodEndsOn { get; init; }
}
public sealed record AccountingPeriodReopeningDecisionSourceV1(AccountingPeriodRecord Period,
    AccountingPeriodReopeningViewV1 Reopening, AccountingPeriodClosureViewV1 Closure)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool? LaterPeriodClosed { get; init; }
}
public interface IAccountingPeriodDecisionStoreV1
{
    Task<AccountingPeriodRecord?> ReadOpeningDecisionAsync(Guid periodId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
    Task<AccountingPeriodReopeningDecisionSourceV1?> ReadReopeningDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
}
