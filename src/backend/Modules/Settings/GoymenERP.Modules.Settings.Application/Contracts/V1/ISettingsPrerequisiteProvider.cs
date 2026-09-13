namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Server-side, company-scoped prerequisites. A result is not a write permission.
/// Consumers revalidate prerequisites within their command consistency boundary
/// and retain the returned identity/version/value snapshot with the transaction.
/// Missing sources throw; callers must never substitute a default profile or open period.
/// </summary>
public interface ISettingsPrerequisiteProvider
{
    Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default);

    Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation,
        DateOnly effectiveOn, CancellationToken cancellationToken = default);

    Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation,
        DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default);
}

public sealed record ProfileVersionV1(Guid ProfileId, long Version);

public sealed record UnitProfileV1(Guid CompanyId, Guid UnitId, string UnitCode,
    string DimensionCode, bool IsKilogram, ProfileVersionV1 Profile, int Scale,
    bool IsActive, DateOnly EffectiveFrom, DateOnly? EffectiveThrough);

public sealed record MoneyProfileV1(Guid CompanyId, Guid CurrencyId, string CurrencyCode,
    string Operation, ProfileVersionV1 Profile, int Scale, bool IsActive,
    DateOnly EffectiveFrom, DateOnly? EffectiveThrough);

/// <summary>
/// Eligibility applies only to this company, operation and exact date pair.
/// An eligible period does not authorize the actor, approve a source, or record money.
/// </summary>
public sealed record PeriodEligibilityV1(Guid CompanyId, Guid PeriodId,
    ProfileVersionV1 Policy, string Operation, DateOnly OccurredOn, DateOnly? ValueOn,
    bool IsEligible, string? DenialCode);

public sealed class SettingsPrerequisiteException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
