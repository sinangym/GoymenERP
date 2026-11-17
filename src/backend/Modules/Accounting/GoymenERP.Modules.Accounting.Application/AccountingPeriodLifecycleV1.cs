namespace GoymenERP.Modules.Accounting.Application;

/// <summary>The Accounting owner names the exact closed/target periods; the host invokes the
/// real Finance owner in the SAME Serializable transaction. No second cash/party movement.</summary>
public sealed record AccountingPeriodCarryRequestV1(Guid ClosureId, Guid CompanyId, Guid SourcePeriodId,
    long ClosedPeriodVersion, DateOnly SourceStartsOn, DateOnly SourceEndsOn, Guid TargetPeriodId,
    DateOnly TargetStartsOn, DateOnly TargetEndsOn, Guid ActorId, DateTimeOffset RecordedAtUtc);

public sealed record AccountingPeriodCarryReceiptV1(Guid ClosureId, Guid CompanyId, Guid SourcePeriodId,
    Guid TargetPeriodId, Guid VoucherId, string SourceHash, int LineCount);

public sealed record AccountingPeriodCarryReversalV1(Guid ReopeningId, Guid CompanyId, Guid ClosureId,
    Guid VoucherId, Guid ActorId, DateTimeOffset RecordedAtUtc, string Reason);

public interface IAccountingPeriodCarryForwardPortV1
{
    Task<AccountingPeriodCarryReceiptV1> RecordAsync(AccountingPeriodCarryRequestV1 request,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken);
    Task ReverseAsync(AccountingPeriodCarryReversalV1 request, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken);
}

public sealed record ProposeAccountingPeriodReopeningV1(long ExpectedVersion, Guid ProposalId, string Reason);
public sealed record ApproveAccountingPeriodReopeningV1(long ExpectedVersion, long ExpectedPeriodVersion);
public sealed record AccountingPeriodReopeningViewV1(Guid Id, Guid CompanyId, Guid PeriodId, Guid ClosureId,
    long ClosedPeriodVersion, Guid ProposedBy, DateTimeOffset ProposedAtUtc, string Reason,
    Guid? ApprovedBy, DateTimeOffset? ApprovedAtUtc, long Version);

public sealed record AccountingPeriodClosureViewV1(Guid Id, Guid CompanyId, Guid PeriodId,
    long ClosedPeriodVersion, Guid TargetPeriodId, Guid CarryVoucherId, string SourceHash, int LineCount,
    Guid ClosedBy, DateTimeOffset ClosedAtUtc, Guid? ReopeningId, Guid? ReopenedBy, DateTimeOffset? ReopenedAtUtc);

public sealed record AccountingPeriodLifecycleViewV1(Guid CompanyId, Guid PeriodId, long PeriodVersion,
    IReadOnlyList<AccountingPeriodClosureViewV1> Closures, IReadOnlyList<AccountingPeriodReopeningViewV1> Reopenings);

public interface IAccountingPeriodReopeningStoreV1
{
    Task<AccountingPeriodLifecycleViewV1?> ReadLifecycleAsync(Guid periodId, CancellationToken cancellationToken);
    Task<AccountingPeriodReopeningViewV1> ProposeReopeningAsync(Guid periodId,
        ProposeAccountingPeriodReopeningV1 request, Guid actor, AccountingCommandMetadata metadata, CancellationToken cancellationToken);
    Task<AccountingPeriodReopeningViewV1> ApproveReopeningAsync(Guid periodId, Guid proposalId,
        ApproveAccountingPeriodReopeningV1 request, Guid actor, AccountingCommandMetadata metadata, CancellationToken cancellationToken);
}
