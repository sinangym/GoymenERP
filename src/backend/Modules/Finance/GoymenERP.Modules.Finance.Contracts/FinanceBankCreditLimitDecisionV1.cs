namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>One approval-purpose record; no general credit workspace, bank card or principal assessment.</summary>
public sealed record FinanceBankCreditLimitDecisionDetailV1(CreditLimitViewV1 Limit, string ProposedBy, string SourceFactsDigest);

/// <summary>Private proof of the actual immutable native decision result, never a current workspace substitute.</summary>
public sealed record FinanceBankCreditLimitDecisionReceiptV1(Guid CompanyId, Guid LimitId, string CommandName,
    string IdempotencyKeyHash, string PayloadHash, string ActorId, string ExpectedVersion, string ResultVersion,
    string OriginalCorrelationId, DateTimeOffset DecidedAtUtc, string SourceFactsDigest, string OriginalReceiptDigest);

public interface IFinanceBankCreditLimitDecisionReadV1
{
    Task<ReadResult<FinanceBankCreditLimitDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid limitId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
}
