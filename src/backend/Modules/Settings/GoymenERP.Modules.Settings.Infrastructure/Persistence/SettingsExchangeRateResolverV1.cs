using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsExchangeRateResolverV1"/>: the company's rate record read through the one calculator
/// (<see cref="RateDayExchangeRate"/>). Computed fresh on every call (SAT-D47: the risk check is never frozen); a document that
/// must keep its number snapshots the answer itself (SAT-D172).
/// </summary>
public sealed class SettingsExchangeRateResolverV1(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : ISettingsExchangeRateResolverV1
{
    public async Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly rateDay, Guid sourceCurrencyId, Guid targetCurrencyId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.AccessDenied);
        }

        return await ExchangeRateResolution.ResolveAsync(database, rateDay, sourceCurrencyId, targetCurrencyId, cancellationToken);
    }
}

/// <summary>
/// Reads the days the calculator needs and maps its answer to the contract with its provenance. The walk is bounded by the
/// records themselves (SAT-D55); the read starts with a window before the rate day and widens only while the walk ran past
/// the window and older records exist.
/// </summary>
internal static class ExchangeRateResolution
{
    private const int InitialWindowDays = 40;
    private const int WindowGrowth = 4;

    public static async Task<ExchangeRateResolutionV1> ResolveAsync(SettingsDbContext database, DateOnly rateDay,
        Guid sourceCurrencyId, Guid targetCurrencyId, CancellationToken cancellationToken)
    {
        if (sourceCurrencyId == Guid.Empty || targetCurrencyId == Guid.Empty || sourceCurrencyId == targetCurrencyId)
        {
            throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.RequestInvalid);
        }

        var request = new RateDayExchangeRateRequest(rateDay, ExchangeRateBulletinSource.Tcmb, sourceCurrencyId, targetCurrencyId);
        long window = InitialWindowDays;
        while (true)
        {
            var from = rateDay.DayNumber - window <= DateOnly.MinValue.DayNumber
                ? DateOnly.MinValue
                : DateOnly.FromDayNumber((int)(rateDay.DayNumber - window));
            ExchangeRateRows rows;
            RateDayExchangeRateResult result;
            try
            {
                rows = await ExchangeRateRows.ReadAsync(database, from, rateDay, track: false, cancellationToken);
                result = RateDayExchangeRate.Resolve(request, rows.BulletinDays(), rows.HalfDayMarks());
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
            {
                throw new SettingsPrerequisiteException(ExchangeRateProblemCodesV1.ProviderUnavailable);
            }

            var ranPastWindow = !result.IsResolved && result.StoppedAt is { } stop && stop < from;
            if (!ranPastWindow || from == DateOnly.MinValue
                || !await ExchangeRateRows.AnyBeforeAsync(database, from, cancellationToken))
            {
                return Map(result, rows);
            }

            window *= WindowGrowth;
        }
    }

    private static ExchangeRateResolutionV1 Map(RateDayExchangeRateResult result, ExchangeRateRows rows)
    {
        var currencyCode = CurrencyCodeOf(result.SourceCurrencyId, rows);
        if (!result.IsResolved)
        {
            return new ExchangeRateResolutionV1(result.RateDay, result.SourceCurrencyId, result.TargetCurrencyId, currencyCode,
                ExchangeRateResolutionStatesV1.Unavailable, ExchangeRateSourcesV1.Tcmb, result.HalfDayRuleApplied,
                result.PreviousFullBusinessDay, null, null, null, null, null, result.Code, result.StoppedAt);
        }

        var bulletinDate = result.BulletinDate!.Value;
        var manual = result.RateProvenance == ExchangeRateBulletinDayProvenance.Manual;
        var record = manual
            ? rows.ManualRates(bulletinDate)[currencyCode!].Record
            : rows.Current(bulletinDate, ExchangeRateRecordKindsV1.Bulletin, ExchangeRateProvenancesV1.Official)!;
        var rate = result.Rate!.Value.Rate;
        return new ExchangeRateResolutionV1(result.RateDay, result.SourceCurrencyId, result.TargetCurrencyId, currencyCode,
            ExchangeRateResolutionStatesV1.Resolved, ExchangeRateSourcesV1.Tcmb, result.HalfDayRuleApplied,
            result.PreviousFullBusinessDay, bulletinDate, rate.ToString(CultureInfo.InvariantCulture),
            manual ? ExchangeRateProvenancesV1.Manual : ExchangeRateProvenancesV1.Official, record.Id,
            record.RecordVersion.ToString(CultureInfo.InvariantCulture), null, null);
    }

    private static string? CurrencyCodeOf(Guid currencyId, ExchangeRateRows rows) =>
        rows.Rates.Select(rate => rate.CurrencyCode).Distinct(StringComparer.Ordinal)
            .FirstOrDefault(code => ExchangeRateCurrencyIdentitiesV1.ForIsoCode(code) == currencyId);
}
