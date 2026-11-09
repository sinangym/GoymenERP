namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// F3a / API-20 (DEC-FIN-006): applies a RECORDED manual bank payment (money out) to one open PAYABLE item. Like a
/// receipt allocation it is not a money event and not a ledger row: the payment's outgoing money event stays the only
/// fact, the allocation only records how much of it settles which supplier item. Party, account, currency and amounts
/// are server-owned; the caller supplies identities, expected versions, the exact positive amount and a reason.
/// </summary>
public sealed record CreateFinancePaymentAllocationRequest(
    Guid AllocationId,
    Guid PaymentId,
    string ExpectedPaymentVersion,
    Guid OpenItemId,
    string ExpectedOpenItemVersion,
    string Amount,
    string Reason);

public sealed record FinancePaymentAllocationView(
    Guid Id,
    Guid CompanyId,
    Guid PaymentId,
    Guid OpenItemId,
    Guid CurrencyId,
    string Amount,
    FinanceAllocationStatus Status,
    string Version,
    string Reason,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? ReleasedBy,
    DateTimeOffset? ReleasedAtUtc,
    string? ReleaseReason);

public sealed record FinancePaymentAllocationCommandResult(
    CommandOutcome Outcome,
    FinancePaymentAllocationView? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

public sealed record FinancePaymentAllocationPage(
    Guid CompanyId,
    IReadOnlyList<FinancePaymentAllocationView> Items,
    bool WindowFull);

public sealed record FinancePaymentAllocationDetail(
    FinancePaymentAllocationView Allocation,
    string OpenItemVersion);

public interface IFinancePaymentAllocationReadService
{
    Task<ReadResult<FinancePaymentAllocationPage>> ListAuthorizedAsync(
        FinanceContext context,
        CancellationToken cancellationToken);

    Task<ReadResult<FinancePaymentAllocationDetail>> GetAuthorizedAsync(
        FinanceContext context,
        Guid allocationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Finance-owned payment allocation boundary. It is fenced by the same allocation permissions as the receipt
/// allocation (<see cref="FinancePermissionNames.AllocationWrite"/>); declaring it grants nothing.
/// </summary>
public interface IFinancePaymentAllocationService
{
    Task<FinancePaymentAllocationCommandResult> CreateAsync(
        FinanceContext context,
        CommandMetadata metadata,
        CreateFinancePaymentAllocationRequest request,
        CancellationToken cancellationToken);

    Task<FinancePaymentAllocationCommandResult> ReleaseAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ReleaseFinanceAllocationRequest request,
        CancellationToken cancellationToken);
}
