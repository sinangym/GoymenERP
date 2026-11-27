using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

public sealed record CashExchangeBookValueDecisionDetailV1(CashTransferView Transfer, CashExchangeBookValueViewV1 Proposal,
    [property: JsonIgnore] CashExchangeBookValueDecisionProofV1? DecisionProof = null);
public sealed record CashExchangeBookValueDecisionProofV1(Guid CompanyId, Guid ProposalId, Guid TransferId, string PreparedBy,
    DateTimeOffset PreparedAtUtc, string Status, string Version, string TransferSourceHash, string ProposalFactsDigest, string PreviewDigest);
public sealed record CashExchangeBookValueDecisionReceiptV1(Guid CompanyId, Guid ProposalId, Guid TransferId, string CommandName,
    string IdempotencyKeyHash, string PayloadHash, string ActorId, string ExpectedVersion, string ResultVersion,
    string OriginalCorrelationId, DateTimeOffset RecordedAtUtc, string ProposalFactsDigest, string OriginalReceiptDigest)
{
    // Already read and hash-bound by the native receipt restorer; never a public response or extra owner read.
    [JsonIgnore] public CashTransferView? TransferSource { get; init; }
}
public interface ICashExchangeBookValueDecisionReadV1
{
    Task<ReadResult<CashExchangeBookValueDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken);
}
