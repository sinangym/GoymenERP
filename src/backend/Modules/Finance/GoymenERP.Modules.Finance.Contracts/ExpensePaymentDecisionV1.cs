namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>Approval-purpose human facts only; no bank card, balance or general treasury read authority.</summary>
public sealed record ExpensePaymentDecisionDetailV1(ExpensePaymentView Payment, string TreasuryAccountLabel,
    IReadOnlyList<string> Revisers, string DraftFactsDigest);

/// <summary>The actual immutable Approve command result; never reconstructed from a subsequently recorded payment.</summary>
public sealed record ExpensePaymentDecisionReceiptV1(Guid CompanyId, Guid PaymentId, string CommandName,
    string IdempotencyKeyHash, string PayloadHash, string ActorId, string ExpectedVersion, string ResultVersion,
    string OriginalCorrelationId, DateTimeOffset RecordedAtUtc, string DraftFactsDigest, string OriginalReceiptDigest);

public interface IExpensePaymentDecisionReadV1
{
    Task<ReadResult<ExpensePaymentDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid paymentId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
}
