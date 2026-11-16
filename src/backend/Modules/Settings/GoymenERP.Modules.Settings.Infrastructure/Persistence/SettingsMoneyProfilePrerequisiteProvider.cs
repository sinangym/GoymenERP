using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>F029-S3: existing unit/period ports are preserved; a stored money timeline has no compiled fallback.</summary>
public sealed class SettingsMoneyProfilePrerequisiteProvider(SettingsDbContext database, ICompanyContext companyContext,
    ICurrentUser currentUser) : ISettingsPrerequisiteProvider
{
    private readonly SettingsUnitProfilePrerequisiteProvider inner = new(database, companyContext, currentUser);

    public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        inner.GetUnitProfileAsync(unitId, effectiveOn, cancellationToken);

    public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
        CancellationToken cancellationToken = default) => inner.GetPeriodEligibilityAsync(operation, occurredOn, valueOn, cancellationToken);

    public async Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || !companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
            throw new SettingsPrerequisiteException("settings_access_denied");
        MoneyProfileVersionRow[] rows;
        try { rows = await MoneyProfileReads.LoadAsync(database, currencyId, operation, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        { throw new SettingsPrerequisiteException("settings_provider_unavailable"); }
        if (rows.Length == 0) return await inner.GetMoneyProfileAsync(currencyId, operation, effectiveOn, cancellationToken);
        MoneyProfileReads.Validate(rows, companyContext.CompanyId, currencyId, operation);
        var entry = SalesPolicyTimelineV1.For(MoneyProfileReads.Timeline(rows), effectiveOn)
            ?? throw new SettingsPrerequisiteException("settings_profile_invalid");
        var row = entry.Value;
        return new MoneyProfileV1(row.CompanyId, row.CurrencyId, row.CurrencyCode, operation,
            new ProfileVersionV1(row.ProfileId, row.Version), row.Scale, row.IsActive, entry.EffectiveFrom,
            entry.EffectiveThrough, row.UnitPriceScale);
    }
}
