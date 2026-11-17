namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>A period boundary representation of existing facts, not a cash movement or an admitted open item.</summary>
public sealed record FinancePeriodCarryLineV1(string AccountKind, Guid AccountId, Guid? PartyId,
    Guid CurrencyId, string Debit, string Credit);
public sealed record FinancePeriodCarrySourceV1(Guid ClosureId, Guid CompanyId, Guid SourcePeriodId,
    long ClosedPeriodVersion, DateOnly SourceStartsOn, DateOnly SourceEndsOn, Guid TargetPeriodId,
    DateOnly TargetStartsOn, DateOnly TargetEndsOn, Guid ActorId, DateTimeOffset RecordedAtUtc);
public sealed record FinancePeriodCarryVoucherV1(Guid Id, Guid CompanyId, FinancePeriodCarrySourceV1 Source,
    string SourceHash, IReadOnlyList<FinancePeriodCarryLineV1> Lines, Guid? ReversalId);
public sealed record FinancePeriodCarryReversalV1(Guid Id, Guid CompanyId, Guid ClosureId, Guid VoucherId,
    Guid ActorId, DateTimeOffset RecordedAtUtc, string Reason);
