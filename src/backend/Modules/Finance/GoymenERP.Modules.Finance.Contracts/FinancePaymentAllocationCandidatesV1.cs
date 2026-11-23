namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>Existing recorded payment and payable sources, including only explicit balance-transfer positions.</summary>
public sealed record FinancePaymentAllocationCandidateItemV1(Guid OpenItemId, string Version, string SourceKind,
    Guid SourceId, DateOnly? DueOn, Guid OriginalPartyId, Guid OriginalAccountId,
    IReadOnlyList<FinanceAllocationPositionV1> Positions);
public sealed record FinancePaymentAllocationCandidatesViewV1(Guid CompanyId, Guid PaymentId, string PaymentVersion,
    Guid CurrencyId, Guid OriginalPartyId, Guid OriginalAccountId, IReadOnlyList<FinanceAllocationPositionV1> Positions,
    IReadOnlyList<FinancePaymentAllocationCandidateItemV1> OpenItems, int PageIndex, bool HasMore,
    IReadOnlyList<FinancePaymentAllocationView> Allocations, bool HasMoreAllocations);
public interface IFinancePaymentAllocationCandidatesV1
{
    Task<ReadResult<FinancePaymentAllocationCandidatesViewV1>> ReadAsync(FinanceContext context, Guid paymentId,
        int pageIndex, CancellationToken cancellationToken);
}
