namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>Exact approval-purpose actual facts, not a general credit workspace grant.</summary>
public sealed record FinanceCreditMovementDecisionDetailV1(CreditMovementViewV1 Movement, string ProposedBy,
    string? BankDisplayName, string SourceFactsDigest);
public sealed record FinanceCreditMovementDecisionReceiptV1(Guid CompanyId, Guid MovementId, string CommandName,
    string IdempotencyKeyHash, string PayloadHash, string ActorId, string ExpectedVersion, string ResultVersion,
    string OriginalCorrelationId, DateTimeOffset DecidedAtUtc, string SourceFactsDigest, string OriginalReceiptDigest);
public interface IFinanceCreditMovementDecisionReadV1
{
    Task<ReadResult<FinanceCreditMovementDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid movementId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
}
