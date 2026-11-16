using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// FNS-D77/D78 (STK-SC36-b): the dry read port for the approved rate stored on the goods receipt where a label's material first
/// entered stock. A non-order conversion (FNS-D83 priority: order rate, then this first-receipt rate, then "hesaplanamadı")
/// asks it per label; a multi-input production order (FNS-D78) asks it once per input label, so every input keeps its own
/// first-receipt rate. Reading writes nothing and never guesses: whatever cannot be answered from a recorded rate is
/// <see cref="InventoryFirstReceiptExchangeRateStatusV1.Unavailable"/> with a stable reason, never a fallback rate.
/// <para>
/// The first entry is the label's earliest <c>Receipt</c> movement line (by occurrence, then recording, then movement id). A
/// label that never had a receipt of its own follows its split lineage to the single parent label; a label made from several
/// inputs (merge, manufacturing) is ambiguous and answers
/// <see cref="InventoryFirstReceiptExchangeRateReasonsV1.LineageAmbiguous"/> (which input's rate counts is not decided).
/// </para>
/// </summary>
public interface IInventoryFirstReceiptExchangeRateReadPortV1
{
    Task<InventoryFirstReceiptExchangeRateV1> GetAsync(Guid companyId, Guid labelId, CancellationToken cancellationToken = default);
}

/// <summary>The three answers of <see cref="IInventoryFirstReceiptExchangeRateReadPortV1"/>.</summary>
public enum InventoryFirstReceiptExchangeRateStatusV1
{
    /// <summary>The first goods receipt carries an approved, recorded rate and it is answered.</summary>
    Rated = 1,

    /// <summary>The label is found but no first-receipt rate can be answered; the caller shows "hesaplanamadı".</summary>
    Unavailable = 2,

    /// <summary>No such label in the asked company.</summary>
    NotFound = 3,
}

/// <summary>Stable reason codes for <see cref="InventoryFirstReceiptExchangeRateStatusV1.Unavailable"/>.</summary>
public static class InventoryFirstReceiptExchangeRateReasonsV1
{
    /// <summary>The material first entered stock by a receipt that is not a purchasing goods acceptance (opening, production...).</summary>
    public const string FirstEntryNotGoodsReceipt = "first_entry_not_goods_receipt";

    /// <summary>
    /// The first entry is a goods acceptance, but no approved rate is recorded on it. Today no goods receipt records a rate, so
    /// every goods-acceptance entry answers this until the recorded rate exists (an open question, not chosen here).
    /// </summary>
    public const string RateNotRecorded = "first_receipt_rate_not_recorded";

    /// <summary>The label descends from several input labels (merge or manufacturing), so a single first receipt is not defined.</summary>
    public const string LineageAmbiguous = "first_receipt_lineage_ambiguous";

    /// <summary>Neither the label nor any ancestor within the lineage bound has a receipt movement.</summary>
    public const string NoStockEntry = "first_receipt_no_stock_entry";

    /// <summary>The stored facts cannot be trusted (lineage deeper than the bound, inconsistent rows); fail-closed.</summary>
    public const string Unreadable = "first_receipt_unreadable";

    public static IReadOnlyList<string> All { get; } =
        [FirstEntryNotGoodsReceipt, RateNotRecorded, LineageAmbiguous, NoStockEntry, Unreadable];
}

/// <summary>
/// One answer of <see cref="IInventoryFirstReceiptExchangeRateReadPortV1"/>. The company and the asked label are always echoed.
/// Once the first entry is found, its label (the asked label or an ancestor), movement and entry time are named; for a goods
/// acceptance also the acceptance identity and the receipt's cost currency. <see cref="Rate"/> is set only for
/// <see cref="InventoryFirstReceiptExchangeRateStatusV1.Rated"/>; <see cref="UnavailableReason"/> only for
/// <see cref="InventoryFirstReceiptExchangeRateStatusV1.Unavailable"/>. Built only through its factories.
/// </summary>
public sealed record InventoryFirstReceiptExchangeRateV1
{
    private InventoryFirstReceiptExchangeRateV1(
        InventoryFirstReceiptExchangeRateStatusV1 status,
        Guid companyId,
        Guid labelId,
        InventoryFirstReceiptEntryV1? entry,
        ExchangeRate? rate,
        string? unavailableReason)
    {
        Status = status;
        CompanyId = companyId;
        LabelId = labelId;
        Entry = entry;
        Rate = rate;
        UnavailableReason = unavailableReason;
    }

    public InventoryFirstReceiptExchangeRateStatusV1 Status { get; }

    public Guid CompanyId { get; }

    public Guid LabelId { get; }

    /// <summary>The first stock entry, when one was found.</summary>
    public InventoryFirstReceiptEntryV1? Entry { get; }

    public ExchangeRate? Rate { get; }

    public string? UnavailableReason { get; }

    /// <summary>No such label in <paramref name="companyId"/>.</summary>
    public static InventoryFirstReceiptExchangeRateV1 NotFound(Guid companyId, Guid labelId)
    {
        RequireIdentity(companyId, labelId);
        return new(InventoryFirstReceiptExchangeRateStatusV1.NotFound, companyId, labelId, null, null, null);
    }

    /// <summary>No first-receipt rate can be answered for <paramref name="reason"/>; the entry is named when it was found.</summary>
    public static InventoryFirstReceiptExchangeRateV1 Unavailable(Guid companyId, Guid labelId, InventoryFirstReceiptEntryV1? entry,
        string reason)
    {
        RequireIdentity(companyId, labelId);
        if (string.IsNullOrEmpty(reason) || !InventoryFirstReceiptExchangeRateReasonsV1.All.Contains(reason, StringComparer.Ordinal))
        {
            throw new ArgumentException("İlk mal kabul kuru okunamama nedeni tanımsız.", nameof(reason));
        }

        if (entry is null && reason is InventoryFirstReceiptExchangeRateReasonsV1.FirstEntryNotGoodsReceipt
            or InventoryFirstReceiptExchangeRateReasonsV1.RateNotRecorded)
        {
            throw new ArgumentException("Bu okunamama nedeni ilk stok girişini adlandırmalıdır.", nameof(entry));
        }

        if (reason == InventoryFirstReceiptExchangeRateReasonsV1.RateNotRecorded && entry is { GoodsAcceptanceId: null })
        {
            throw new ArgumentException("Kur kaydı yok nedeni yalnız mal kabul girişinde verilir.", nameof(entry));
        }

        return new(InventoryFirstReceiptExchangeRateStatusV1.Unavailable, companyId, labelId, entry, null, reason);
    }

    /// <summary>The first goods receipt's recorded, approved <paramref name="rate"/>.</summary>
    public static InventoryFirstReceiptExchangeRateV1 Rated(Guid companyId, Guid labelId, InventoryFirstReceiptEntryV1 entry,
        ExchangeRate rate)
    {
        RequireIdentity(companyId, labelId);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.GoodsAcceptanceId is null)
        {
            throw new ArgumentException("İlk mal kabul kuru yalnız mal kabul girişinden verilir.", nameof(entry));
        }

        if (rate == default)
        {
            throw new ArgumentException("İlk mal kabul kuru boş olamaz.", nameof(rate));
        }

        return new(InventoryFirstReceiptExchangeRateStatusV1.Rated, companyId, labelId, entry, rate, null);
    }

    private static void RequireIdentity(Guid companyId, Guid labelId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket kimliği boş olamaz.", nameof(companyId));
        }

        if (labelId == Guid.Empty)
        {
            throw new ArgumentException("Etiket kimliği boş olamaz.", nameof(labelId));
        }
    }
}

/// <summary>
/// The label's first stock entry: the label that received it (the asked label or a split ancestor), the receipt movement, the
/// time it occurred, the goods acceptance identity (null when the entry is not a goods acceptance) and the receipt's cost
/// currency (null when the receipt carried no cost).
/// </summary>
public sealed record InventoryFirstReceiptEntryV1
{
    public InventoryFirstReceiptEntryV1(Guid entryLabelId, Guid movementId, DateTimeOffset enteredAtUtc, Guid? goodsAcceptanceId,
        Guid? costCurrencyId)
    {
        if (entryLabelId == Guid.Empty || movementId == Guid.Empty || goodsAcceptanceId == Guid.Empty || costCurrencyId == Guid.Empty)
        {
            throw new ArgumentException("İlk stok girişi kimlikleri boş olamaz.", nameof(entryLabelId));
        }

        if (enteredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("İlk stok girişi zamanı UTC olmalıdır.", nameof(enteredAtUtc));
        }

        EntryLabelId = entryLabelId;
        MovementId = movementId;
        EnteredAtUtc = enteredAtUtc;
        GoodsAcceptanceId = goodsAcceptanceId;
        CostCurrencyId = costCurrencyId;
    }

    public Guid EntryLabelId { get; }

    public Guid MovementId { get; }

    public DateTimeOffset EnteredAtUtc { get; }

    public Guid? GoodsAcceptanceId { get; }

    public Guid? CostCurrencyId { get; }
}
