using System.Data.Common;
using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// URT-S13 (URT-D47, URT-D65), URT-D72: the company's monthly scrap prices. A month's first price is version 1; a
/// correction adds the next version with its reason. Each version is written with its audit event and its command record
/// in the same save and is never changed or deleted here (the row is append-only under the context's write guard). The
/// unique (company, year, month, version) index keeps one row per version also when two writers race: the loser of two
/// first entries gets <see cref="ManufacturingRuleCodes.ScrapPriceAlreadyRecorded"/>, the loser of two corrections of the
/// same version a version conflict, and nothing of it is stored. The latest version is the month's effective price.
/// </summary>
public sealed partial class ManufacturingStore : IManufacturingMonthlyScrapPriceStore, IManufacturingMonthlyScrapPriceReaderV1
{
    private const string ScrapPriceEntityType = "Manufacturing.MonthlyScrapPrice";

    public async Task<MonthlyScrapPrice> RecordScrapPriceAsync(MonthlyScrapPrice price, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(price);
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        if (price.CompanyId != companyId)
        {
            throw Refused(ManufacturingStoreCodes.CompanyContextMismatch,
                "Hurda fiyatı aktif şirket bağlamının şirketine ait değil.");
        }

        var year = price.Period.Year;
        var month = price.Period.Month;
        var recorded = await dbContext.MonthlyScrapPrices.AsNoTracking()
            .AnyAsync(row => row.CompanyId == companyId && row.Year == year && row.Month == month, cancellationToken);
        if (recorded)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.ScrapPriceAlreadyRecorded,
                "Bu ayın hurda fiyatı zaten girildi; girilmiş ay değiştirilmez.");
        }

        if (price.Version != MonthlyScrapPrice.FirstVersion)
        {
            throw new ArgumentException("Ayın ilk hurda fiyatı girişi 1. sürümdür.", nameof(price));
        }

        dbContext.MonthlyScrapPrices.Add(ScrapPriceRow(price, metadata));
        Audit(metadata, companyId, ScrapPriceEntityType, price.Id, "manufacturing.scrap-price.recorded.v1",
            "RecordScrapPrice", versionBefore: null, versionAfter: price.Version, before: "{}",
            after: ScrapPriceAuditJson(price), "Aylık hurda fiyatı girişi");
        // ADR-011 §6: the command record commits with the price row and its audit event, or nothing does.
        Complete(metadata, companyId, price);
        await SaveAsync(cancellationToken);
        return price;
    }

    public async Task<MonthlyScrapPrice> CorrectScrapPriceAsync(Guid correctionId, ScrapPricePeriod period,
        int expectedVersion, ScrapPriceTlPerKg price, string reason, ManufacturingCommandMetadata metadata,
        DateTimeOffset correctedAtUtc, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var year = period.Year;
        var month = period.Month;

        // URT-D72: the month's effective price is its latest version; a month that has none is not found.
        var latest = await dbContext.MonthlyScrapPrices.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Year == year && row.Month == month)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw Refused(ManufacturingStoreCodes.ScrapPriceNotFound, "Bu şirketin bu ay için hurda fiyatı yok.");
        var current = RestoreScrapPrice(latest);

        // The domain refuses a stale version, a blank reason, the same price and a future month; nothing is written then.
        var corrected = current.Correct(correctionId, expectedVersion, price, reason, metadata.ActorId, correctedAtUtc);
        dbContext.MonthlyScrapPrices.Add(ScrapPriceRow(corrected, metadata));
        Audit(metadata, companyId, ScrapPriceEntityType, corrected.Id, "manufacturing.scrap-price.corrected.v1",
            "CorrectScrapPrice", versionBefore: current.Version, versionAfter: corrected.Version,
            before: ScrapPriceAuditJson(current), after: ScrapPriceAuditJson(corrected), corrected.Reason!);
        // ADR-011 §6: the command record commits with the new version and its audit event, or nothing does.
        Complete(metadata, companyId, corrected);
        try
        {
            await SaveAsync(cancellationToken);
        }
        catch (ManufacturingRuleException exception)
            when (exception.Code == ManufacturingRuleCodes.ScrapPriceAlreadyRecorded)
        {
            // Another correction of the same version landed first (the unique version index): this one is stale.
            throw new ManufacturingVersionConflictException(expectedVersion, corrected.Version);
        }

        return corrected;
    }

    public async Task<IReadOnlyList<MonthlyScrapPrice>> ListScrapPricesAsync(int year,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        var rows = await dbContext.MonthlyScrapPrices.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Year == year)
            .ToListAsync(cancellationToken);

        // URT-D72: a month answers its effective (latest) version; the older versions stay in the store and the audit.
        return
        [
            .. rows.GroupBy(row => row.Month)
                .Select(versions => versions.MaxBy(row => row.Version)!)
                .OrderByDescending(row => row.Month)
                .Select(RestoreScrapPrice),
        ];
    }

    public async Task<ScrapPriceInput> ReadScrapPriceAsync(Guid companyId, ScrapPricePeriod period,
        CancellationToken cancellationToken = default)
    {
        // The company is the caller's, fenced by it; a company that is not the resolved one is never read.
        if (companyId == Guid.Empty || period == default || !companyContext.IsResolved
            || companyContext.CompanyId != companyId)
        {
            return new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null);
        }

        try
        {
            var year = period.Year;
            var month = period.Month;
            // URT-D72 (2): the month's effective price is its latest version; an unfinalised cost reads the corrected one.
            var rows = await dbContext.MonthlyScrapPrices.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Year == year && row.Month == month)
                .OrderByDescending(row => row.Version)
                .Take(2)
                .ToListAsync(cancellationToken);
            return rows switch
            {
                // URT-D47 (2), URT-D65: a month without a price leaves the cost pending, never a zero.
                [] => ScrapPriceInput.Missing,
                // Two rows of one version break the unique index's promise: nothing is chosen between them.
                [var latest, var previous] when latest.Version == previous.Version
                    => new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null),
                [var latest, ..] => RestoreScrapPrice(latest).ToInput(),
            };
        }
        catch (Exception exception) when (exception is DbException or ManufacturingStoreException
            || exception is ManufacturingRuleException { Code: ManufacturingRuleCodes.StoredRecordInvalid })
        {
            return new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null);
        }
    }

    private static MonthlyScrapPrice RestoreScrapPrice(MonthlyScrapPriceRow row) =>
        MonthlyScrapPrice.Restore(row.Id, row.CompanyId, row.Year, row.Month, row.TlPerKg, row.Version, row.Reason,
            row.RecordedBy, row.RecordedAtUtc);

    private static MonthlyScrapPriceRow ScrapPriceRow(MonthlyScrapPrice price, ManufacturingCommandMetadata metadata) =>
        new()
        {
            CompanyId = price.CompanyId,
            Id = price.Id,
            Year = price.Period.Year,
            Month = price.Period.Month,
            TlPerKg = price.Price.Value,
            Version = price.Version,
            Reason = price.Reason,
            RecordedBy = price.RecordedBy,
            RecordedAtUtc = price.RecordedAtUtc,
            CorrelationId = metadata.CorrelationId.Trim(),
        };

    private static string ScrapPriceAuditJson(MonthlyScrapPrice price) => JsonSerializer.Serialize(new
    {
        period = price.Period.ToString(),
        year = price.Period.Year,
        month = price.Period.Month,
        tlPerKg = price.Price.Value,
        version = price.Version,
        reason = price.Reason,
        price.RecordedBy,
        price.RecordedAtUtc,
    }, Json);
}
