using System.Data.Common;
using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// URT-S13 (URT-D47, URT-D65): the company's monthly scrap prices. A month's price is recorded once, with its audit
/// event and its command record in the same save, and is never changed or deleted here (the row is append-only under the
/// context's write guard). The unique (company, year, month) index keeps one price per month also when two writers race;
/// the loser gets <see cref="ManufacturingRuleCodes.ScrapPriceAlreadyRecorded"/> and nothing of it is stored.
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

        dbContext.MonthlyScrapPrices.Add(new MonthlyScrapPriceRow
        {
            CompanyId = companyId,
            Id = price.Id,
            Year = year,
            Month = month,
            TlPerKg = price.Price.Value,
            RecordedBy = price.RecordedBy,
            RecordedAtUtc = price.RecordedAtUtc,
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        Audit(metadata, companyId, ScrapPriceEntityType, price.Id, "manufacturing.scrap-price.recorded.v1",
            "RecordScrapPrice", versionBefore: null, versionAfter: null, before: "{}",
            after: JsonSerializer.Serialize(new
            {
                period = price.Period.ToString(),
                year,
                month,
                tlPerKg = price.Price.Value,
                price.RecordedBy,
                price.RecordedAtUtc,
            }, Json),
            "Aylık hurda fiyatı girişi");
        // ADR-011 §6: the command record commits with the price row and its audit event, or nothing does.
        Complete(metadata, companyId, price);
        await SaveAsync(cancellationToken);
        return price;
    }

    public async Task<IReadOnlyList<MonthlyScrapPrice>> ListScrapPricesAsync(int year,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        var rows = await dbContext.MonthlyScrapPrices.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Year == year)
            .OrderByDescending(row => row.Month)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(RestoreScrapPrice)];
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
            var rows = await dbContext.MonthlyScrapPrices.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.Year == year && row.Month == month)
                .Take(2)
                .ToListAsync(cancellationToken);
            return rows.Count switch
            {
                // URT-D47 (2), URT-D65: a month without a price leaves the cost pending, never a zero.
                0 => ScrapPriceInput.Missing,
                1 => RestoreScrapPrice(rows[0]).ToInput(),
                // Two prices for one month break the unique index's promise: nothing is chosen between them.
                _ => new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null),
            };
        }
        catch (Exception exception) when (exception is DbException or ManufacturingStoreException
            || exception is ManufacturingRuleException { Code: ManufacturingRuleCodes.StoredRecordInvalid })
        {
            return new ScrapPriceInput(ScrapPriceReadStatus.Unreadable, null);
        }
    }

    private static MonthlyScrapPrice RestoreScrapPrice(MonthlyScrapPriceRow row) =>
        MonthlyScrapPrice.Restore(row.Id, row.CompanyId, row.Year, row.Month, row.TlPerKg, row.RecordedBy,
            row.RecordedAtUtc);
}
