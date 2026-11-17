using System.Globalization;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsFinancePolicyAdministration
{
    public async Task<SettingsCollateralRateReadV1> PreviewCollateralRateAsync(PreviewSettingsCollateralRateV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = Capture(cancellationToken); await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], false, cancellationToken);
        var result = await GetCollateralRateAsync(scope.CompanyId, request.CollateralTypeId, request.BankInstitutionId, request.BankSourceVersion,
            request.ValidUntil, request.BusinessDate, cancellationToken);
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], true, cancellationToken);
        return result;
    }
    public async Task<SettingsCollateralRateReadV1> GetCollateralRateAsync(Guid companyId, Guid collateralTypeId,
        Guid? bankInstitutionId, string? bankSourceVersion, DateOnly? validUntil, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        var scope = Capture(cancellationToken); if (scope.CompanyId != companyId) throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.AccessDenied);
        if (collateralTypeId == Guid.Empty || businessDate == DateOnly.MinValue || validUntil is { } end && end < businessDate
            || (bankInstitutionId is null) != (bankSourceVersion is null)) return CollateralUnavailable();
        var types = await ReadAsync(() => collateralTypes.ListAsync(companyId, cancellationToken)); Same(scope, cancellationToken);
        if (types.CompanyId != companyId || types.Entries is null) return CollateralUnavailable();
        var matchedTypes = types.Entries.Where(x => x.Id == collateralTypeId).ToArray();
        if (matchedTypes.Length != 1 || !CollateralTypeRulesV1.IsKind(matchedTypes[0].Kind)
            || !CollateralTypeRulesV1.TryRate(matchedTypes[0].RatePercent, out var typeRate) || string.IsNullOrWhiteSpace(matchedTypes[0].Version))
            return CollateralUnavailable();
        // An explicit stored proof is required for bank-specific matching. No account/name/IBAN inference, including legacy rows.
        if (bankInstitutionId is { } bankId)
        {
            var proof = await ReadBankInstitutionProofAsync(companyId, bankId, bankSourceVersion!, cancellationToken); Same(scope, cancellationToken);
            if (proof is null || !proof.Active) return CollateralUnavailable();
        }
        var rows = await ReadAsync(() => database.CollateralRatePolicies.AsNoTracking().Where(x => x.State != "Draft" && x.ValidFrom <= businessDate
            && (x.ValidThrough == null || businessDate <= x.ValidThrough) && (x.RetiredFrom == null || businessDate < x.RetiredFrom)
            && (x.Scope == "Company" || x.CollateralTypeId == collateralTypeId && x.BankInstitutionId == bankInstitutionId)).ToArrayAsync(cancellationToken));
        Same(scope, cancellationToken);
        if (rows.Any(x => !ValidHeader(x) || !Enum.TryParse<SettingsCollateralRateScopeV1>(x.Scope, out var kind)
            || !CollateralRateScopeValid(kind, x.CollateralTypeId, x.CollateralTypeVersion, x.BankInstitutionId, x.BankSourceVersion, x.MaximumRemainingDays)
            || !CollateralTypeRulesV1.TryRate(x.RatePercent, out var canonical) || canonical != x.RatePercent)) return CollateralUnavailable();
        // Only a real end date has remaining days. Open-ended records never acquire an invented day count.
        var days = validUntil is { } until ? until.DayNumber - businessDate.DayNumber : (int?)null;
        if (bankInstitutionId is not null)
        {
            var special = rows.Where(x => x.Scope == "TypeBankRemainingDays" && days.HasValue && x.MaximumRemainingDays is { } maximum && days.Value <= maximum).ToArray();
            if (special.Length > 1) return CollateralUnavailable();
            if (special.Length == 1) return CollateralRateAnswer(special[0], matchedTypes[0].Version);
            var bank = rows.Where(x => x.Scope == "TypeBank").ToArray();
            if (bank.Length > 1) return CollateralUnavailable();
            if (bank.Length == 1) return CollateralRateAnswer(bank[0], matchedTypes[0].Version);
        }
        // FNS-D31/D70 already owns every real type's rate. It is K6's type tier, not a second policy table or a missing-source default.
        // Thus an explicit company fallback remains lower priority than a valid existing type rate. A malformed type never becomes success.
        return new(SettingsFinancePolicyReadOutcomeV1.Available, typeRate, $"settings.collateral-type:{collateralTypeId:D}:{matchedTypes[0].Version}", "Type", null, matchedTypes[0].Version);
    }

    private static SettingsCollateralRateReadV1 CollateralUnavailable() => new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, null, null, SettingsFinancePolicyCodesV1.Unavailable);
    private static SettingsCollateralRateReadV1 CollateralRateAnswer(CollateralRatePolicyRow row, string typeVersion) => new(SettingsFinancePolicyReadOutcomeV1.Available,
        row.RatePercent, $"settings.collateral-rate:{row.PolicyId:D}:{row.Version.ToString(CultureInfo.InvariantCulture)}", row.Scope, null, typeVersion);
}
