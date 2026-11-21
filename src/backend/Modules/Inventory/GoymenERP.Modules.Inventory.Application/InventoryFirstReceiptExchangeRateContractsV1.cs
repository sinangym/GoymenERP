using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Inventory.Domain.Costing;
using System.Text.Json;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// FNS-D77/D78/D103/D104: immutable first-entry FX, captured automatically with the genuine goods acceptance.
/// Splits retain their parent's evidence; merges use exact kg-weighted rates of the same directed currency pair.
/// Manufacturing creates new material and does not borrow its inputs' single rate. Missing owner facts remain unavailable.
/// Reading writes nothing; neither current rates nor supplier-invoice stock-cost FX replace the first-entry snapshot.
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

    public const string CurrencyPairMismatch = "first_receipt_currency_pair_mismatch";
    public const string RateNotRequired = "first_receipt_rate_not_required";

    public static IReadOnlyList<string> All { get; } =
        [FirstEntryNotGoodsReceipt, RateNotRecorded, LineageAmbiguous, NoStockEntry, Unreadable, CurrencyPairMismatch, RateNotRequired];
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
        string? unavailableReason, InventoryExactCostRatioV1? rateRatio = null, Guid? sourceCurrencyId = null,
        Guid? targetCurrencyId = null, string? sourceVersion = null, IReadOnlyList<InventoryFirstReceiptRateSourceV1>? sources = null)
    {
        Status = status;
        CompanyId = companyId;
        LabelId = labelId;
        Entry = entry;
        Rate = rate;
        UnavailableReason = unavailableReason;
        RateRatio = rateRatio;
        SourceCurrencyId = sourceCurrencyId;
        TargetCurrencyId = targetCurrencyId;
        SourceVersion = sourceVersion;
        Sources = sources ?? [];
    }

    public InventoryFirstReceiptExchangeRateStatusV1 Status { get; }

    public Guid CompanyId { get; }

    public Guid LabelId { get; }

    /// <summary>The first stock entry, when one was found.</summary>
    public InventoryFirstReceiptEntryV1? Entry { get; }

    public ExchangeRate? Rate { get; }

    public string? UnavailableReason { get; }

    /// <summary>Authoritative exact rate. Rate is null when the exact result has no finite decimal representation.</summary>
    public InventoryExactCostRatioV1? RateRatio { get; }
    public Guid? SourceCurrencyId { get; }
    public Guid? TargetCurrencyId { get; }
    public string? SourceVersion { get; }
    /// <summary>Actual first receipts and their exact material shares, never a fictitious single receipt for a merge.</summary>
    public IReadOnlyList<InventoryFirstReceiptRateSourceV1> Sources { get; }

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

        return new(InventoryFirstReceiptExchangeRateStatusV1.Rated, companyId, labelId, entry, rate, null,
            InventoryExactCostRatioV1.From(ExactCostRatio.Of(rate.Rate)), rate.SourceCurrencyId, rate.TargetCurrencyId);
    }

    /// <summary>Owner-proved exact result, with a normalized, bounded list of actual receipt sources.</summary>
    public static InventoryFirstReceiptExchangeRateV1 RatedExact(Guid companyId, Guid labelId, Guid sourceCurrencyId,
        Guid targetCurrencyId, InventoryExactCostRatioV1 rateRatio, string sourceVersion,
        IReadOnlyList<InventoryFirstReceiptRateSourceV1> sources)
    {
        RequireIdentity(companyId, labelId);
        ArgumentNullException.ThrowIfNull(sources);
        if (sourceCurrencyId == Guid.Empty || targetCurrencyId == Guid.Empty || sourceCurrencyId == targetCurrencyId
            || string.IsNullOrWhiteSpace(sourceVersion) || sources.Count is 0 or > WeightedFirstReceiptExchangeRateCalculator.MaximumInputs)
            throw new ArgumentException("İlk giriş kuru kaynakları geçersiz.");
        var rate = rateRatio.Value();
        var totalShare = ExactCostRatio.Of(0m);
        var weighted = ExactCostRatio.Of(0m);
        var movements = new HashSet<Guid>();
        foreach (var source in sources)
        {
            if (source is null || source.Entry is null || source.Snapshot is null
                || source.Entry.GoodsAcceptanceId is null || !movements.Add(source.Entry.MovementId)
                || !InventoryFirstEntryRateAdmissionV1.IsValid(source.Snapshot, companyId, source.Entry.GoodsAcceptanceId.Value)
                || source.Snapshot.PurchaseOrderId == Guid.Empty || source.Snapshot.PurchaseOrderVersion is not > 0
                || source.Snapshot.State != InventoryFirstEntryRateStateV1.Rated
                || source.Snapshot.SourceCurrencyId != sourceCurrencyId || source.Snapshot.TargetCurrencyId != targetCurrencyId
                || source.Snapshot.Reason is not null || string.IsNullOrWhiteSpace(source.Snapshot.SourceVersion)
                || source.Snapshot.SourceKind is not (InventoryFirstEntryRateSourcesV1.ApprovedPurchaseOrder or InventoryFirstEntryRateSourcesV1.EntryDayTcmb))
                throw new ArgumentException("İlk giriş kuru kaynak kimliği uyuşmuyor.");
            using var evidence = JsonDocument.Parse(source.Snapshot.EvidenceJson);
            if (evidence.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("İlk giriş kuru kanıtı geçersiz.");
            var share = source.MaterialShare.Value();
            var actualRate = InventoryExactCostRatioV1.Resolve(source.Snapshot.Rate, source.Snapshot.RateRatio);
            if (share.Sign <= 0 || actualRate.Sign <= 0) throw new ArgumentException("İlk giriş kuru payı geçersiz.");
            totalShare = totalShare.Add(share);
            weighted = weighted.Add(share.Multiply(actualRate));
        }
        if (rate.Sign <= 0 || totalShare.CompareTo(ExactCostRatio.Of(1m)) != 0 || rate.CompareTo(weighted) != 0)
            throw new ArgumentException("İlk giriş kuru pay toplamı uyuşmuyor.");
        ExchangeRate? scalar = rate.TryToDecimal(out var exact) ? ExchangeRate.Of(sourceCurrencyId, targetCurrencyId, exact) : null;
        return new(InventoryFirstReceiptExchangeRateStatusV1.Rated, companyId, labelId,
            sources.Count == 1 ? sources[0].Entry : null, scalar, null, rateRatio, sourceCurrencyId, targetCurrencyId,
            sourceVersion, sources.ToArray());
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

/// <summary>One immutable receipt's material share in the requested label; shares sum exactly to one.</summary>
public sealed record InventoryFirstReceiptRateSourceV1(InventoryFirstReceiptEntryV1 Entry,
    InventoryExactCostRatioV1 MaterialShare, InventoryFirstEntryRateV1 Snapshot);
