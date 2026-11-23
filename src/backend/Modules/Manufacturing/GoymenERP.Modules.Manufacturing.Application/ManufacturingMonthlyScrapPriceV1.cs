using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Modules.Manufacturing.Application;

// URT-S13 (SAT-D316 (3)-(4), URT-D47, URT-D65): the company enters one scrap price per month by hand, in TL per kg (no
// per-material price). Each scrap weighing is valued at the price of the month it was weighed in; a month without a price
// leaves the cost pending ("maliyet yok", not finalised) and is never a silent zero. The price value itself is never
// chosen here. Who enters it and under which approval is not decided (URT-D57 (3) is an author reading), so the two
// permissions below are registered and granted to no role. URT-D72: a wrong price is corrected by a new version with a
// reason under the same write permission; the old version stays, the latest version is effective and every version is
// audited. A cost that is not finalised reads the effective price when it is computed; a finalised cost is not
// recomputed here (no finalised cost snapshot reads this store yet). URT-D73: only the current and past months (the
// Europe/Istanbul business day of the entry) take a price.

/// <summary>
/// URT-S13, URT-D72: the company's monthly scrap prices — record a month's first price or correct it with a new version,
/// each in one save (the price row, its audit event and the command record, or nothing), and list a year's months. A
/// port of its own beside the plan and order stores; the one store implements it. Every read and write is the
/// server-resolved company's only.
/// </summary>
public interface IManufacturingMonthlyScrapPriceStore
{
    /// <summary>
    /// Records the month's price. A month that already has a price is refused with
    /// <see cref="ManufacturingRuleCodes.ScrapPriceAlreadyRecorded"/>, also when another writer recorded it first.
    /// </summary>
    Task<MonthlyScrapPrice> RecordScrapPriceAsync(MonthlyScrapPrice price, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// URT-D72: corrects the month's effective (latest) price with the next version. A month without a price is refused
    /// with <see cref="ManufacturingStoreCodes.ScrapPriceNotFound"/>; a stale <paramref name="expectedVersion"/>, also one
    /// another corrector made stale first, is a <see cref="ManufacturingVersionConflictException"/>; the domain's rules
    /// (<see cref="MonthlyScrapPrice.Correct"/>) refuse the rest. Nothing is written on a refusal.
    /// </summary>
    Task<MonthlyScrapPrice> CorrectScrapPriceAsync(Guid correctionId, ScrapPricePeriod period, int expectedVersion,
        ScrapPriceTlPerKg price, string reason, ManufacturingCommandMetadata metadata, DateTimeOffset correctedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The year's recorded months, each as its effective (latest) version, the latest month first. A stored row that does
    /// not rebuild is refused, never skipped.
    /// </summary>
    Task<IReadOnlyList<MonthlyScrapPrice>> ListScrapPricesAsync(int year, CancellationToken cancellationToken = default);
}

/// <summary>
/// URT-S13: the scrap price of one month as the production cost reads it for a scrap weighing (URT-D47 (1)). It answers
/// the cost calculator's <see cref="ScrapPriceInput"/>: <see cref="ScrapPriceReadStatus.Value"/> with the month's
/// effective price (its latest version, URT-D72 (2)),
/// <see cref="ScrapPriceReadStatus.Missing"/> when the month has none (the cost stays pending, URT-D47 (2)) and
/// <see cref="ScrapPriceReadStatus.Unreadable"/> when it cannot be read or the company is not the resolved one. It carries
/// no permission: the caller resolves and fences the company. It never picks a month or a value for the caller.
/// </summary>
public interface IManufacturingMonthlyScrapPriceReaderV1
{
    Task<ScrapPriceInput> ReadScrapPriceAsync(Guid companyId, ScrapPricePeriod period,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>POST /scrap-prices</c>: the calendar month (<see cref="Year"/>, <see cref="Month"/> 1-12) and its scrap price in TL
/// per kg as lossless invariant text (as kilograms travel, Ö2): plain notation, greater than zero, at most four decimals.
/// </summary>
public sealed record RecordMonthlyScrapPriceRequestV1(int Year, int Month, string? TlPerKg);

/// <summary>
/// <c>POST /scrap-prices/{year}/{month}/corrections</c> (URT-D72): the month's effective version the caller read
/// (<see cref="ExpectedVersion"/>), the corrected price in TL per kg as lossless invariant text (as on the first entry) and
/// the correction's reason (required, at most <see cref="MonthlyScrapPrice.ReasonMaximumLength"/> characters trimmed).
/// </summary>
public sealed record CorrectMonthlyScrapPriceRequestV1(int ExpectedVersion, string? TlPerKg, string? Reason);

/// <summary>
/// One recorded month's effective scrap price; <see cref="TlPerKg"/> is invariant text, never a JSON number. URT-D72:
/// <see cref="Version"/> is 1 for the first entry and one more for each correction; <see cref="Reason"/> is the latest
/// correction's reason, null on version 1.
/// </summary>
public sealed record MonthlyScrapPriceViewV1(
    Guid ScrapPriceId, int Year, int Month, string TlPerKg, int Version, string? Reason, string RecordedBy,
    DateTimeOffset RecordedAtUtc);

/// <summary>
/// The year's recorded months, the latest month first. A month that is not listed has no price: its scrap weighings leave
/// the cost pending (URT-D47 (2), URT-D65).
/// </summary>
public sealed record MonthlyScrapPriceListViewV1(int Year, IReadOnlyList<MonthlyScrapPriceViewV1> Items);

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// <c>POST /scrap-prices</c> (URT-S13) under <c>manufacturing.scrap-prices.write</c>: records the company's first scrap
    /// price of one month (version 1). A month that already has a price is 409
    /// <c>manufacturing_scrap_price_already_recorded</c>; a first entry never replaces it, the month is corrected through
    /// <see cref="CorrectMonthlyScrapPriceAsync"/> (URT-D72). A fifth decimal is 422
    /// <c>manufacturing_scrap_price_precision_exceeded</c>; a zero, negative, malformed or oversized price is a 400 on
    /// <c>tlPerKg</c> (URT-D74 (1)). URT-D73: a month after the Europe/Istanbul business day is 422
    /// <c>manufacturing_scrap_price_future_month</c>; a host that cannot resolve the zone answers 503. ADR-011 as the other
    /// commands: the same key replays the first answer.
    /// </summary>
    public Task<ManufacturingApiResult<MonthlyScrapPriceViewV1>> RecordMonthlyScrapPriceAsync(
        RecordMonthlyScrapPriceRequestV1? request, string idempotencyKey, string permission,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("request"));
        }

        if (request.Year < DateOnly.MinValue.Year || request.Year > DateOnly.MaxValue.Year)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("year"));
        }

        if (request.Month is < ScrapPricePeriod.FirstMonth or > ScrapPricePeriod.LastMonth)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("month"));
        }

        if (ParseKg(request.TlPerKg) is not { } value || value <= 0m || value >= ScrapPriceTlPerKg.ExclusiveUpperBound)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("tlPerKg"));
        }

        var period = new ScrapPricePeriod(request.Year, request.Month);
        return RunAsync<MonthlyScrapPriceViewV1>(permission, idempotencyKey, ManufacturingCommandNames.RecordScrapPrice,
            new
            {
                year = period.Year,
                month = period.Month,
                // The price as its canonical text, so the hash never depends on trailing zeros or a serializer's settings.
                tlPerKg = Text(value),
            }, ManufacturingApiStatus.Created,
            async (metadata, actor, companyId) =>
            {
                if (scrapPrices is null || BusinessToday() is null)
                {
                    return new ManufacturingApiResult<MonthlyScrapPriceViewV1>(ManufacturingApiStatus.Unavailable, null,
                        false, null);
                }

                // A fifth decimal and a future month (URT-D73) are the domain's rules (422), raised here inside the attempt
                // like the kilogram rule.
                var price = MonthlyScrapPrice.Record(Guid.NewGuid(), companyId, period, new ScrapPriceTlPerKg(value),
                    metadata.ActorId, timeProvider.GetUtcNow());
                var recorded = await scrapPrices.RecordScrapPriceAsync(price, metadata, cancellationToken);
                return new ManufacturingApiResult<MonthlyScrapPriceViewV1>(ManufacturingApiStatus.Created,
                    MonthlyScrapPriceView(recorded), false, null);
            }, stored => MonthlyScrapPriceView((MonthlyScrapPrice)stored), cancellationToken);
    }

    /// <summary>
    /// <c>POST /scrap-prices/{year}/{month}/corrections</c> (URT-D72) under <c>manufacturing.scrap-prices.write</c>, the
    /// same permission as the first entry (URT-D57): corrects the month's effective price with the next version and a
    /// reason; the old version stays and both are audited. A month without a price is 404 (no code). A stale
    /// <c>expectedVersion</c> is 409 <c>manufacturing_version_conflict</c> with the current version. The same price again is
    /// 422 <c>manufacturing_scrap_price_unchanged</c>, a fifth decimal 422 <c>manufacturing_scrap_price_precision_exceeded</c>
    /// and (URT-D73) a month after the Europe/Istanbul business day 422 <c>manufacturing_scrap_price_future_month</c>. A
    /// zero, negative, malformed or oversized price is a 400 on <c>tlPerKg</c> (URT-D74 (1)), a blank or too long reason a
    /// 400 on <c>reason</c>. A host that cannot resolve the zone answers 503. ADR-011: the same key replays the first answer.
    /// </summary>
    public Task<ManufacturingApiResult<MonthlyScrapPriceViewV1>> CorrectMonthlyScrapPriceAsync(int year, int month,
        CorrectMonthlyScrapPriceRequestV1? request, string idempotencyKey, string permission,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("request"));
        }

        if (year < DateOnly.MinValue.Year || year > DateOnly.MaxValue.Year)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("year"));
        }

        if (month is < ScrapPricePeriod.FirstMonth or > ScrapPricePeriod.LastMonth)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("month"));
        }

        if (request.ExpectedVersion < MonthlyScrapPrice.FirstVersion)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("expectedVersion"));
        }

        if (ParseKg(request.TlPerKg) is not { } value || value <= 0m || value >= ScrapPriceTlPerKg.ExclusiveUpperBound)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("tlPerKg"));
        }

        if (request.Reason?.Trim() is not { Length: > 0 and <= MonthlyScrapPrice.ReasonMaximumLength } reason)
        {
            return Task.FromResult(Invalid<MonthlyScrapPriceViewV1>("reason"));
        }

        var period = new ScrapPricePeriod(year, month);
        return RunAsync<MonthlyScrapPriceViewV1>(permission, idempotencyKey, ManufacturingCommandNames.CorrectScrapPrice,
            new
            {
                year = period.Year,
                month = period.Month,
                expectedVersion = request.ExpectedVersion,
                tlPerKg = Text(value),
                reason,
            }, ManufacturingApiStatus.Ok,
            async (metadata, _, _) =>
            {
                if (scrapPrices is null || BusinessToday() is null)
                {
                    return new ManufacturingApiResult<MonthlyScrapPriceViewV1>(ManufacturingApiStatus.Unavailable, null,
                        false, null);
                }

                // The store reads the month's effective version and the domain decides; a fifth decimal is raised here.
                var corrected = await scrapPrices.CorrectScrapPriceAsync(Guid.NewGuid(), period, request.ExpectedVersion,
                    new ScrapPriceTlPerKg(value), reason, metadata, timeProvider.GetUtcNow(), cancellationToken);
                return new ManufacturingApiResult<MonthlyScrapPriceViewV1>(ManufacturingApiStatus.Ok,
                    MonthlyScrapPriceView(corrected), false, null);
            }, stored => MonthlyScrapPriceView((MonthlyScrapPrice)stored), cancellationToken);
    }

    /// <summary>
    /// <c>GET /scrap-prices?year=</c> (URT-S13) under <c>manufacturing.scrap-prices.read</c>: the company's recorded months
    /// of one year, each as its effective (latest) version, the latest month first. The year is required (a bounded answer of at most twelve months). Without the
    /// store the answer is a retryable 503, never an empty list; the read permission is measured again after the read.
    /// </summary>
    public async Task<ManufacturingApiResult<MonthlyScrapPriceListViewV1>> ListMonthlyScrapPricesAsync(int? year,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<MonthlyScrapPriceListViewV1>();
        }

        if (year is not { } listedYear || listedYear < DateOnly.MinValue.Year || listedYear > DateOnly.MaxValue.Year)
        {
            return Invalid<MonthlyScrapPriceListViewV1>("year");
        }

        if (scrapPrices is null)
        {
            return new ManufacturingApiResult<MonthlyScrapPriceListViewV1>(ManufacturingApiStatus.Unavailable, null,
                false, null);
        }

        var companyId = companyContext.CompanyId;
        ManufacturingApiResult<MonthlyScrapPriceListViewV1> answer;
        try
        {
            var prices = await scrapPrices.ListScrapPricesAsync(listedYear, cancellationToken);

            // A list holding another company's or another year's row breaks the port's contract: nothing is answered.
            answer = prices.All(price => price.CompanyId == companyId && price.Period.Year == listedYear)
                ? new ManufacturingApiResult<MonthlyScrapPriceListViewV1>(ManufacturingApiStatus.Ok,
                    new MonthlyScrapPriceListViewV1(listedYear, [.. prices.Select(MonthlyScrapPriceView)]), false, null)
                : new ManufacturingApiResult<MonthlyScrapPriceListViewV1>(ManufacturingApiStatus.Unavailable, null,
                    false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            answer = new ManufacturingApiResult<MonthlyScrapPriceListViewV1>(ManufacturingApiStatus.Unavailable, null,
                false, null);
        }

        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingScrapPricesRead, companyId, cancellationToken);
        return scope.IsGranted && HasCapturedContext(companyId, actorId)
            ? answer
            : Denied<MonthlyScrapPriceListViewV1>();
    }

    private static MonthlyScrapPriceViewV1 MonthlyScrapPriceView(MonthlyScrapPrice price) =>
        new(price.Id, price.Period.Year, price.Period.Month, Text(price.Price.Value), price.Version, price.Reason,
            price.RecordedBy, price.RecordedAtUtc);
}
