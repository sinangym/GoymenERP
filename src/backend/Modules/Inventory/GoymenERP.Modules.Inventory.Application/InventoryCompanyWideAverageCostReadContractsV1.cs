namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// SAT-D272 / SAT-D347 / SAT-D350 / KLN-D83 / KLN-D84 (STK-S35, STK-SC33-a): the company-wide (all locations) on-hand-kg-weighted moving-average purchase
/// cost of one product, owned and computed by Inventory. Sales orders carry no location (SAT-D272 (1)), so Sales reads this
/// answer for its margin preview and approval snapshot and never combines location reads itself (SAT-D262 (3), SAT-D347).
/// Statuses and unreadable codes are those of the location read (<see cref="InventoryAverageCostReadStatusV1"/>,
/// <see cref="InventoryAverageCostUnreadableCodesV1"/>). Contract version 1 facts are pinned by
/// <c>InventoryCompanyWideAverageCostReadContractTests</c>.
/// </summary>
public static class InventoryCompanyWideAverageCostReadContractV1
{
    public const int Version = 1;

    /// <summary>Per stock kilogram, as the location read.</summary>
    public const string CostUnit = InventoryAverageCostReadContractV1.CostUnit;

    /// <summary>Purchase receipts only (SAT-D263), as the location read.</summary>
    public const string CostBasis = InventoryAverageCostReadContractV1.CostBasis;

    /// <summary>
    /// SAT-D347 / SAT-D350: every location of the company with stock of the product, weighted by its on-hand kilograms of
    /// the company's own goods it holds (KLN-D84: consignment and subcontractor stock do not weigh); a location without
    /// stock is left out; one location with stock that is unreadable or has no cost source makes the answer unreadable
    /// ("hesaplanamadı", KLN-D83; no partial average).
    /// </summary>
    public const string Aggregation = "all-locations-on-hand-kg-weighted";

    /// <summary>
    /// The ledger holds base-currency costs only (a foreign-currency receipt is unreadable until BS-1 / SAT-D295 is
    /// implemented), so the figure is in the company's base currency. Inventory does not own a currency code; the consumer
    /// resolves the company's base currency from its owner.
    /// </summary>
    public const string CostCurrencyBasis = "company-base-currency";

    /// <summary>SAT-D265 (2): the internal precision. A value never carries more decimals.</summary>
    public const int InternalScale = InventoryAverageCostReadContractV1.InternalScale;

    /// <summary>The stock quantity scale of Inventory's balances (kg, 3 decimals).</summary>
    public const int QuantityScale = 3;
}

/// <summary>
/// One location that holds stock of the product and therefore takes part in the answer: the on-hand kilograms of the
/// company's own goods it holds (KLN-D84) and the ledger row version it was read at (<c>null</c> when the ledger has no row for stock that is there).
/// A snapshot may record the list as the answer's source marker.
/// </summary>
public sealed record InventoryCompanyWideAverageCostSourceV1
{
    public InventoryCompanyWideAverageCostSourceV1(Guid locationId, decimal onHandKg, long? ledgerVersion)
    {
        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("The location is named.", nameof(locationId));
        }

        if (onHandKg <= 0m || Math.Round(onHandKg, InventoryCompanyWideAverageCostReadContractV1.QuantityScale,
                MidpointRounding.AwayFromZero) != onHandKg)
        {
            throw new ArgumentOutOfRangeException(nameof(onHandKg), "A source location holds positive stock with at most 3 decimals.");
        }

        if (ledgerVersion is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ledgerVersion));
        }

        LocationId = locationId;
        OnHandKg = onHandKg;
        LedgerVersion = ledgerVersion;
    }

    public Guid LocationId { get; }

    public decimal OnHandKg { get; }

    public long? LedgerVersion { get; }
}

/// <summary>
/// The company-wide answer for one product of the server-resolved company. A value carries the weighted average, an
/// unreadable carries a code and the location it was found at, a no-source carries neither: unknown is never a zero.
/// <see cref="Sources"/> lists, ordered by location id, every location with stock (empty for NoSource);
/// <see cref="OnHandKg"/> is their total.
/// </summary>
public sealed record InventoryCompanyWideAverageCostReadAnswerV1
{
    private InventoryCompanyWideAverageCostReadAnswerV1(
        Guid companyId, Guid productId, InventoryAverageCostReadStatusV1 status, decimal? averageCostPerKg,
        string? unreadableCode, Guid? unreadableLocationId, IReadOnlyList<InventoryCompanyWideAverageCostSourceV1> sources)
    {
        CompanyId = companyId;
        ProductId = productId;
        Status = status;
        AverageCostPerKg = averageCostPerKg;
        UnreadableCode = unreadableCode;
        UnreadableLocationId = unreadableLocationId;
        Sources = sources;
        OnHandKg = sources.Sum(source => source.OnHandKg);
    }

    public int ContractVersion => InventoryCompanyWideAverageCostReadContractV1.Version;

    public Guid CompanyId { get; }

    public Guid ProductId { get; }

    public InventoryAverageCostReadStatusV1 Status { get; }

    /// <summary>Only with <see cref="InventoryAverageCostReadStatusV1.Value"/>: non-negative, at most 4 decimals.</summary>
    public decimal? AverageCostPerKg { get; }

    /// <summary>Always <see cref="InventoryCompanyWideAverageCostReadContractV1.CostCurrencyBasis"/>.</summary>
    public string CostCurrencyBasis => InventoryCompanyWideAverageCostReadContractV1.CostCurrencyBasis;

    /// <summary>Only with <see cref="InventoryAverageCostReadStatusV1.Unreadable"/>: one of <see cref="InventoryAverageCostUnreadableCodesV1.All"/>.</summary>
    public string? UnreadableCode { get; }

    /// <summary>Only with <see cref="InventoryAverageCostReadStatusV1.Unreadable"/>: the location that cannot be read.</summary>
    public Guid? UnreadableLocationId { get; }

    public decimal OnHandKg { get; }

    public IReadOnlyList<InventoryCompanyWideAverageCostSourceV1> Sources { get; }

    public static InventoryCompanyWideAverageCostReadAnswerV1 ForValue(
        Guid companyId, Guid productId, decimal averageCostPerKg, IEnumerable<InventoryCompanyWideAverageCostSourceV1> sources)
    {
        RequireIdentity(companyId, productId);
        if (averageCostPerKg < 0m || Math.Round(averageCostPerKg, InventoryCompanyWideAverageCostReadContractV1.InternalScale,
                MidpointRounding.AwayFromZero) != averageCostPerKg)
        {
            throw new ArgumentOutOfRangeException(nameof(averageCostPerKg), "The average is non-negative with at most 4 decimals.");
        }

        var ordered = RequireSources(sources);
        if (ordered.Any(source => source.LedgerVersion is null))
        {
            throw new ArgumentException("A value is read from a ledger row at every source location.", nameof(sources));
        }

        return new(companyId, productId, InventoryAverageCostReadStatusV1.Value, averageCostPerKg, null, null, ordered);
    }

    public static InventoryCompanyWideAverageCostReadAnswerV1 ForUnreadable(
        Guid companyId, Guid productId, string unreadableCode, Guid unreadableLocationId,
        IEnumerable<InventoryCompanyWideAverageCostSourceV1> sources)
    {
        RequireIdentity(companyId, productId);
        if (!InventoryAverageCostUnreadableCodesV1.All.Contains(unreadableCode))
        {
            throw new ArgumentOutOfRangeException(nameof(unreadableCode), "Unknown unreadable code.");
        }

        var ordered = RequireSources(sources);
        if (!ordered.Any(source => source.LocationId == unreadableLocationId))
        {
            throw new ArgumentException("The unreadable location is one of the source locations.", nameof(unreadableLocationId));
        }

        return new(companyId, productId, InventoryAverageCostReadStatusV1.Unreadable, null, unreadableCode,
            unreadableLocationId, ordered);
    }

    public static InventoryCompanyWideAverageCostReadAnswerV1 ForNoSource(Guid companyId, Guid productId)
    {
        RequireIdentity(companyId, productId);
        return new(companyId, productId, InventoryAverageCostReadStatusV1.NoSource, null, null, null, []);
    }

    private static void RequireIdentity(Guid companyId, Guid productId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company is named.", nameof(companyId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("The product is named.", nameof(productId));
        }
    }

    private static InventoryCompanyWideAverageCostSourceV1[] RequireSources(
        IEnumerable<InventoryCompanyWideAverageCostSourceV1> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var ordered = sources.ToArray();
        if (ordered.Length == 0 || ordered.Any(source => source is null))
        {
            throw new ArgumentException("An answer with a figure or a refusal names its source locations.", nameof(sources));
        }

        ordered = ordered.OrderBy(source => source.LocationId).ToArray();
        if (ordered.Select(source => source.LocationId).Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("A location is a source once.", nameof(sources));
        }

        return ordered;
    }
}

/// <summary>
/// Read port V1, owned and implemented by Inventory (registered in its module): the company-wide moving-average purchase
/// cost per stock kilogram of one product of the server-resolved company (SAT-D272, SAT-D347, SAT-D350, KLN-D83, KLN-D84).
/// Another unit is read through <see cref="IInventoryAverageCostUnitReadPortV1"/> (KLN-D85). It carries no
/// authority (the consumer fences before and after), reads only the resolved company's own company-filtered ledger and
/// balances, creates nothing (no attempt, audit, movement or version effect) and never throws for a missing figure: that
/// is <see cref="InventoryAverageCostReadStatusV1.NoSource"/>. Another company's rows are invisible. An unresolved company
/// is refused (never an unscoped read).
/// </summary>
public interface IInventoryCompanyWideAverageCostReadPortV1
{
    Task<InventoryCompanyWideAverageCostReadAnswerV1> ReadCompanyWideAsync(
        Guid productId, CancellationToken cancellationToken = default);
}
