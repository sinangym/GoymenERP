namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// SAT-D262 (SINAN_KARARLARI_2026-11-10_SATIS_EK52): Inventory owns the moving-average purchase cost and hands it out only
/// through this versioned read port. Sales reads it at order creation and stores its own snapshot; it never reads
/// Inventory tables and never computes the cost. Contract version 1 facts are pinned by
/// <c>InventoryAverageCostReadContractTests</c>.
/// </summary>
public static class InventoryAverageCostReadContractV1
{
    public const int Version = 1;

    /// <summary>The cost is per stock kilogram. Any other unit is a NEEDS-SINAN conversion: no value is given for it.</summary>
    public const string CostUnit = "kg";

    /// <summary>The cost basis: purchase receipts only (SAT-D263); no second cost type is added.</summary>
    public const string CostBasis = "purchase-moving-average";

    /// <summary>SAT-D265 (2): the internal precision. A value never carries more decimals.</summary>
    public const int InternalScale = 4;
}

public enum InventoryAverageCostReadStatusV1
{
    /// <summary>A readable average (<see cref="InventoryAverageCostReadAnswerV1.AverageCostPerKg"/>).</summary>
    Value = 1,

    /// <summary>The position exists but cannot be read (an open decision, see the code). Never zero.</summary>
    Unreadable = 2,

    /// <summary>Nothing is recorded for the position, including "the purchase price input is not connected yet".</summary>
    NoSource = 3,
}

/// <summary>Stable wire codes for <see cref="InventoryAverageCostReadStatusV1.Unreadable"/>.</summary>
public static class InventoryAverageCostUnreadableCodesV1
{
    public const string NegativeStock = "negative_stock";
    public const string UnitConversion = "unit_conversion_undecided";
    public const string ForeignCurrency = "foreign_currency_undecided";
    public const string LandedOrAdditionalCost = "additional_cost_undecided";
    public const string Return = "return_effect_undecided";
    public const string ProductionSource = "mixed_source_undecided";
    public const string TransferSourceUnreadable = "transfer_source_unreadable";
    public const string LedgerStockMismatch = "uncosted_stock_on_hand";
    public const string ZeroCostReceipt = "zero_cost_receipt";

    public static IReadOnlyList<string> All { get; } =
    [
        NegativeStock, UnitConversion, ForeignCurrency, LandedOrAdditionalCost, Return, ProductionSource,
        TransferSourceUnreadable, LedgerStockMismatch, ZeroCostReceipt,
    ];
}

/// <summary>
/// The answer for one (company, location, product) position of the server-resolved company. A value carries the average,
/// an unreadable carries a code, a no-source carries neither: unknown is never a zero. <see cref="LedgerVersion"/> names
/// the ledger row version the answer was read at (a snapshot may record it); it is null for NoSource.
/// </summary>
public sealed record InventoryAverageCostReadAnswerV1
{
    private InventoryAverageCostReadAnswerV1(
        Guid companyId, Guid locationId, Guid productId, InventoryAverageCostReadStatusV1 status,
        decimal? averageCostPerKg, string? unreadableCode, long? ledgerVersion)
    {
        CompanyId = companyId;
        LocationId = locationId;
        ProductId = productId;
        Status = status;
        AverageCostPerKg = averageCostPerKg;
        UnreadableCode = unreadableCode;
        LedgerVersion = ledgerVersion;
    }

    public int ContractVersion => InventoryAverageCostReadContractV1.Version;

    public Guid CompanyId { get; }

    public Guid LocationId { get; }

    public Guid ProductId { get; }

    public InventoryAverageCostReadStatusV1 Status { get; }

    /// <summary>Only with <see cref="InventoryAverageCostReadStatusV1.Value"/>: non-negative, at most 4 decimals.</summary>
    public decimal? AverageCostPerKg { get; }

    /// <summary>Only with <see cref="InventoryAverageCostReadStatusV1.Unreadable"/>: one of <see cref="InventoryAverageCostUnreadableCodesV1.All"/>.</summary>
    public string? UnreadableCode { get; }

    public long? LedgerVersion { get; }

    public static InventoryAverageCostReadAnswerV1 ForValue(
        Guid companyId, Guid locationId, Guid productId, decimal averageCostPerKg, long ledgerVersion)
    {
        RequireIdentity(companyId, locationId, productId);
        if (averageCostPerKg < 0m || Math.Round(averageCostPerKg, InventoryAverageCostReadContractV1.InternalScale,
                MidpointRounding.AwayFromZero) != averageCostPerKg)
        {
            throw new ArgumentOutOfRangeException(nameof(averageCostPerKg), "The average is non-negative with at most 4 decimals.");
        }

        RequireVersion(ledgerVersion);
        return new(companyId, locationId, productId, InventoryAverageCostReadStatusV1.Value, averageCostPerKg, null, ledgerVersion);
    }

    public static InventoryAverageCostReadAnswerV1 ForUnreadable(
        Guid companyId, Guid locationId, Guid productId, string unreadableCode, long ledgerVersion)
    {
        RequireIdentity(companyId, locationId, productId);
        if (!InventoryAverageCostUnreadableCodesV1.All.Contains(unreadableCode))
        {
            throw new ArgumentOutOfRangeException(nameof(unreadableCode), "Unknown unreadable code.");
        }

        RequireVersion(ledgerVersion);
        return new(companyId, locationId, productId, InventoryAverageCostReadStatusV1.Unreadable, null, unreadableCode, ledgerVersion);
    }

    public static InventoryAverageCostReadAnswerV1 ForNoSource(Guid companyId, Guid locationId, Guid productId)
    {
        RequireIdentity(companyId, locationId, productId);
        return new(companyId, locationId, productId, InventoryAverageCostReadStatusV1.NoSource, null, null, null);
    }

    private static void RequireIdentity(Guid companyId, Guid locationId, Guid productId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("The company is named.", nameof(companyId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException("The location is named.", nameof(locationId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("The product is named.", nameof(productId));
        }
    }

    private static void RequireVersion(long ledgerVersion)
    {
        if (ledgerVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ledgerVersion));
        }
    }
}

/// <summary>
/// Read port V1, owned and implemented by Inventory (registered in its module): the moving-average purchase cost per stock
/// kilogram of one product at one location of the server-resolved company. It carries no authority (the consumer fences
/// before and after), reads only the resolved company's own company-filtered ledger, creates nothing (no attempt, audit,
/// movement or version effect) and never throws for a missing figure: that is <see cref="InventoryAverageCostReadStatusV1.NoSource"/>.
/// Another company's rows are invisible. An unresolved company is refused (never an unscoped read).
/// </summary>
public interface IInventoryAverageCostReadPortV1
{
    Task<InventoryAverageCostReadAnswerV1> ReadAsync(Guid locationId, Guid productId, CancellationToken cancellationToken = default);
}
