using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>URT-D82/105 and the weighing-day answer: exact daily TCMB forex selling provenance; USD display only.</summary>
internal sealed class ManufacturingScrapUsdRateReader(ICompanyContext company, ICurrentUser user, ISettingsExchangeRateResolverV1 owner)
    : IManufacturingScrapUsdRateReadPortV1
{
    public async Task<ManufacturingScrapUsdRateSnapshotV1?> ReadAsync(Guid companyId, DateOnly weighingDay, CancellationToken cancellationToken)
    {
        var actor = user.UserId; if (!InScope()) return null;
        var usd = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD");
        try
        {
            var result = await owner.ResolveAsync(weighingDay, usd, SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
            if (!InScope() || result is null || result.RateDay != weighingDay || result.SourceCurrencyId != usd
                || result.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira || result.Source != ExchangeRateSourcesV1.Tcmb
                || result.State != ExchangeRateResolutionStatesV1.Resolved || result.BulletinDate is not { } bulletinDay
                || result.RecordId is not { } recordId || recordId == Guid.Empty || string.IsNullOrWhiteSpace(result.RecordVersion)
                || !decimal.TryParse(result.SellingRate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) || rate <= 0) return null;
            return new(companyId, weighingDay, bulletinDay, rate, recordId, result.RecordVersion, result.Source);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
        bool InScope() => company.IsResolved && company.CompanyId == companyId && companyId != Guid.Empty
            && user.IsAuthenticated && user.UserId == actor && !string.IsNullOrWhiteSpace(actor) && user.CanAccessCompany(companyId);
    }
}
