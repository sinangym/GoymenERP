using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>FNS-D31/D70: current, company-owned Settings types. No historical rate, seed or fallback is inferred.</summary>
public sealed class SettingsFinanceCollateralTypeCatalogReaderV1(ICompanyContext company, ICurrentUser user,
    ISettingsCollateralTypeCatalogV1 owner, TimeProvider time) : IFinanceCollateralTypeCatalogReader
{
    public async Task<FinanceCollateralTypeCatalogAnswer> ListAsync(Guid companyId, DateOnly asOf,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = user.UserId;
        bool Current() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(companyId)
            && FinanceBusinessDay.TryToday(time, out var today) && today == asOf;
        if (!Current()) return FinanceCollateralTypeCatalogAnswer.NotAvailable;
        SettingsCollateralTypesViewV1 answer;
        try { answer = await owner.ListAsync(companyId, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return FinanceCollateralTypeCatalogAnswer.NotAvailable; }
        if (!Current() || answer is null || answer.CompanyId != companyId || answer.Entries is null
            || answer.Entries.Any(x => x is null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Code)
                || string.IsNullOrWhiteSpace(x.Name) || !Enum.IsDefined(x.Kind)
                || !FinancePartyCollateralFigures.TryRate(x.RatePercent, out _) || !Version(x.Version))
            || answer.Entries.Select(x => x.Id).Distinct().Count() != answer.Entries.Count
            || answer.Entries.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != answer.Entries.Count)
            return FinanceCollateralTypeCatalogAnswer.NotAvailable;
        return new(FinanceCollateralTypeCatalogState.Available, answer.Entries.Select(x =>
            new FinanceCollateralTypeCatalogItem(x.Id, x.Code, x.Name, x.Active, x.RatePercent, x.Version, false)).ToArray());
    }

    private static bool Version(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
        out var version) && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;
}
