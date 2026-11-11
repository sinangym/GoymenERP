using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

public enum PurchaseOrderStatus
{
    Draft = 1,
    Approved = 2,

    /// <summary>
    /// A1-3 (plan API-10, No.161 §2): an approved order whose not-yet-accepted remainder was cancelled in full — on every line,
    /// even after a partial acceptance. Terminal: no acceptance, breakdown or second cancellation follows; the accepted shares stay.
    /// </summary>
    Cancelled = 3,
}

/// <summary>
/// A purchase commitment towards one supplier Party for one company (#275 §7.2). An approved
/// order is FUTURE supply: it is not physical stock and it books nothing in Inventory. What this
/// record tracks is shares only — ordered, broken down into technical lines, accepted, remaining.
/// The physical receipt, its lot/label and the stock movement belong to Inventory (T09), and a
/// second allocation engine is deliberately not built here. A2a-1 (TEK-01, ALS-D5): the order carries one currency (its Settings
/// money profile, <see cref="Money"/>) and each general line an optional manual price with a discount; tax, exchange rate, payment
/// plan and totals are later A2a slices.
/// Approval here is a state transition only; which approvals a purchase needs is policy (§8)
/// and is not chosen by this type.
/// <para>
/// This aggregate is the single authority for every share bound; each refusal carries a
/// <see cref="PurchasingRuleCodes"/> code. Line units are Settings identities; the Application
/// layer's verifier checks them against the Settings profile before a line is added.
/// </para>
/// </summary>
public sealed class PurchaseOrder
{
    private readonly List<PurchaseOrderLine> lines = [];
    private readonly List<PurchaseAcceptanceShare> acceptances = [];

    private PurchaseOrder(Guid id, Guid companyId, Guid supplierPartyId, Guid? sourceRequestId, DateTimeOffset now,
        PurchaseOrderMoney? money = null, string? preparedBy = null)
    {
        Money = money;
        PreparedBy = preparedBy is null ? null : RequireActor(preparedBy, nameof(preparedBy));
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        SupplierPartyId = Identity.Require(supplierPartyId, nameof(supplierPartyId));
        if (sourceRequestId == Guid.Empty)
        {
            throw new ArgumentException("Kimlik boş olamaz.", nameof(sourceRequestId));
        }

        SourceRequestId = sourceRequestId;
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public Guid SupplierPartyId { get; }
    public Guid? SourceRequestId { get; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;

    /// <summary>
    /// A2a-1 (TEK-01): the order's currency as the Settings money profile verified at opening, or null for an order opened
    /// without one (every line then "fiyat bekliyor", ALS-D18). Set once with the order; never changed here (a currency change is
    /// a header revision, API-12/A2b).
    /// </summary>
    public PurchaseOrderMoney? Money { get; }
    public long Version { get; private set; } = 1;

    /// <summary>
    /// A2a-5a (ALS-D3, Fable F-015): the actor who prepared (opened) the order — the maker. The approver must be a different actor
    /// (<see cref="Approve"/>); an order whose preparer is unknown is never approved (fail-closed,
    /// <see cref="PurchasingRuleCodes.OrderMakerUnknown"/>). The store rebuilds it from the stored creator.
    /// </summary>
    public string? PreparedBy { get; private set; }

    /// <summary>
    /// A2a-5a: who approved the order and when; null while Draft, and null for an order approved before A2a-5a (its approver was
    /// not recorded; it is not invented).
    /// </summary>
    public PurchaseOrderApproval? Approval { get; private set; }

    /// <summary>A1-3 (No.161 §2): who cancelled the unaccepted remainder, when and why; null until <see cref="Cancel"/>.</summary>
    public PurchaseOrderCancellation? Cancellation { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Read-only views; the lists behind them never leave this record.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => lines.AsReadOnly();

    public IReadOnlyList<PurchaseAcceptanceShare> Acceptances => acceptances.AsReadOnly();

    public static PurchaseOrder Create(Guid id, Guid companyId, Guid supplierPartyId, DateTimeOffset now, string? preparedBy = null) =>
        new(id, companyId, supplierPartyId, null, now, preparedBy: preparedBy);

    /// <summary>A2a-1: an order opened in <paramref name="money"/>'s currency (null: no currency; no line may then be priced).</summary>
    public static PurchaseOrder Create(Guid id, Guid companyId, Guid supplierPartyId, PurchaseOrderMoney? money, DateTimeOffset now,
        string? preparedBy = null) =>
        new(id, companyId, supplierPartyId, null, now, money, preparedBy);

    /// <summary>
    /// PUR-4a: an order opened towards a supplier for ONE request (A1-1: an APPROVED one, read by the service), whose lines bind to that request's
    /// lines at the quantity this supplier takes (multi-sourcing, Sinan #66 5738113623 §1: a request line may
    /// split across several orders; the sum of the shares never exceeds the line's need — the store enforces the
    /// sum across orders at approval, this record enforces the binding shape).
    /// </summary>
    public static PurchaseOrder CreateForRequest(Guid id, Guid companyId, Guid supplierPartyId, Guid sourceRequestId, DateTimeOffset now,
        string? preparedBy = null) =>
        new(id, companyId, supplierPartyId, Identity.Require(sourceRequestId, nameof(sourceRequestId)), now, preparedBy: preparedBy);

    /// <summary>A2a-1: <see cref="CreateForRequest(Guid, Guid, Guid, Guid, DateTimeOffset)"/> in <paramref name="money"/>'s currency.</summary>
    public static PurchaseOrder CreateForRequest(Guid id, Guid companyId, Guid supplierPartyId, Guid sourceRequestId, PurchaseOrderMoney? money,
        DateTimeOffset now, string? preparedBy = null) =>
        new(id, companyId, supplierPartyId, Identity.Require(sourceRequestId, nameof(sourceRequestId)), now, money, preparedBy);

    /// <summary>
    /// Sources an order from an APPROVED request of the SAME company (A1-1, ALS-D2/ALS-D16: an order is opened only
    /// for an approved need), copying its lines as general lines with their units. The request stays what it is; the
    /// order is the commitment. Sourcing the same request again is not refused here (the Σ-shares rule is the store's,
    /// at approval).
    /// </summary>
    public static PurchaseOrder FromRequest(Guid id, PurchaseRequest request, Guid supplierPartyId,
        Func<Guid> lineIds, DateTimeOffset now, string? preparedBy = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lineIds);
        if (request.Status != PurchaseRequestStatus.Approved)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.RequestNotApproved,
                "Yalnız onaylı talepten sipariş açılabilir.");
        }

        var order = new PurchaseOrder(id, request.CompanyId, supplierPartyId, request.Id, now, preparedBy: preparedBy);
        foreach (var requestLine in request.Lines)
        {
            order.lines.Add(new PurchaseOrderLine(Identity.Require(lineIds(), nameof(lineIds)), requestLine.ProductId,
                requestLine.UnitId, requestLine.Quantity, PurchaseOrderLineKind.General, null, requestLine.Id));
        }

        return order;
    }

    public PurchaseOrderLine AddLine(long expectedVersion, Guid lineId, Guid productId, Guid unitId, Quantity ordered, DateTimeOffset now) =>
        AddLine(expectedVersion, lineId, productId, unitId, ordered, sourceRequestLineId: null, now);

    /// <summary>
    /// PUR-4a: a general line, optionally bound to one line of the order's source request (the share this
    /// supplier takes of that need). A binding needs the order to have a source request; the same request line
    /// is bound at most once per order (<see cref="PurchasingRuleCodes.DuplicateSourceLine"/>).
    /// </summary>
    public PurchaseOrderLine AddLine(long expectedVersion, Guid lineId, Guid productId, Guid unitId, Quantity ordered, Guid? sourceRequestLineId,
        DateTimeOffset now) =>
        AddLine(expectedVersion, lineId, productId, unitId, ordered, sourceRequestLineId, price: null, now);

    /// <summary>
    /// A2a-1 (TEK-01, ALS-D5, ALS-D18): a general line with an optional manual price (see <see cref="RequirePrice"/>); without one
    /// the line is "fiyat bekliyor" and the order is still approvable and receivable (ALS-D18).
    /// </summary>
    public PurchaseOrderLine AddLine(long expectedVersion, Guid lineId, Guid productId, Guid unitId, Quantity ordered, Guid? sourceRequestLineId,
        PurchaseOrderLinePrice? price, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Draft, PurchasingRuleCodes.OrderNotDraft, "Yalnız taslak siparişe satır eklenebilir.");
        Identity.Require(lineId, nameof(lineId));
        Identity.Require(productId, nameof(productId));
        Identity.Require(unitId, nameof(unitId));
        if (ordered.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(ordered), "Sipariş miktarı sıfırdan büyük olmalıdır.");
        }

        if (sourceRequestLineId is { } sourceLine)
        {
            Identity.Require(sourceLine, nameof(sourceRequestLineId));
            if (SourceRequestId is null)
            {
                throw new PurchasingRuleException(PurchasingRuleCodes.SourceLineWithoutRequest,
                    "Talep satırına bağlı satır yalnız bir talebe açılmış siparişe eklenir.");
            }

            if (lines.Any(line => line.SourceRequestLineId == sourceLine))
            {
                throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateSourceLine,
                    "Aynı talep satırı bir siparişe ikinci kez bağlanamaz; pay tek satırda verilir.");
            }
        }

        RequireNewLineId(lineId);
        if (price is not null)
        {
            RequirePrice(price, unitId, ordered);
        }

        var line = new PurchaseOrderLine(lineId, productId, unitId, ordered, PurchaseOrderLineKind.General, null, sourceRequestLineId, price);
        lines.Add(line);
        Touch(now);
        return line;
    }

    /// <summary>
    /// Approves a Draft order with lines. A2a-5a (ALS-D3, Fable F-015): the approver is never the preparer — compared ordinally,
    /// as the database's binary collation does (<see cref="PurchasingRuleCodes.OrderSameActor"/>); an order whose preparer is
    /// unknown is refused (<see cref="PurchasingRuleCodes.OrderMakerUnknown"/>, fail-closed). The approval threshold (API-96)
    /// is A2a-5's, not this rule's.
    /// </summary>
    public void Approve(long expectedVersion, string approvedBy, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(approvedBy, nameof(approvedBy));
        RequireStatus(PurchaseOrderStatus.Draft, PurchasingRuleCodes.OrderNotDraft, "Yalnız taslak sipariş onaylanabilir.");
        if (lines.Count == 0)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderWithoutLines, "Satırsız sipariş onaylanamaz.");
        }

        if (PreparedBy is null)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderMakerUnknown,
                "Siparişi hazırlayan bilinmiyor; görev ayrılığı doğrulanamadığından onay verilmez.");
        }

        if (string.Equals(PreparedBy, actor, StringComparison.Ordinal))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderSameActor,
                "Siparişi hazırlayan kişi aynı siparişi onaylayamaz (görev ayrılığı).");
        }

        Approval = new PurchaseOrderApproval(actor, now.ToUniversalTime());
        Status = PurchaseOrderStatus.Approved;
        Touch(now);
    }

    /// <summary>
    /// Store create only: the stored order's preparer is the actor of the create envelope. A draft built without one takes it; one built
    /// with another actor is refused, so the row, the audit and the returned order never disagree on who prepared it.
    /// </summary>
    internal void StampPreparer(string actor)
    {
        var preparer = RequireActor(actor, nameof(actor));
        if (PreparedBy is null)
        {
            PreparedBy = preparer;
        }
        else if (!string.Equals(PreparedBy, preparer, StringComparison.Ordinal))
        {
            throw new ArgumentException("Siparişin hazırlayanı açılış isteğini yapan kullanıcı olmalıdır.", nameof(actor));
        }
    }

    /// <summary>
    /// Store rebuild only: an order stored as approved before A2a-5a carries no approver. Its approval is replayed without one
    /// (the fact is not invented and the rule is not applied retroactively); every other shape goes through <see cref="Approve"/>.
    /// </summary>
    internal void RestoreLegacyApproval(long expectedVersion, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireStatus(PurchaseOrderStatus.Draft, PurchasingRuleCodes.OrderNotDraft, "Yalnız taslak sipariş onaylanabilir.");
        if (lines.Count == 0)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderWithoutLines, "Satırsız sipariş onaylanamaz.");
        }

        Status = PurchaseOrderStatus.Approved;
        Touch(now);
    }

    /// <summary>
    /// Carves a technical line out of a general line (§7.2, §13: 1.000 general → 925 general +
    /// 75 technical). Allowed while Draft and after approval — that is the §7.2 case, a general
    /// purchase whose technical breakdown becomes known later; the total commitment never changes
    /// and the technical line keeps the general line's unit, only its product may differ. The
    /// general remainder plus its technical slices never exceed the first commitment, an already
    /// accepted general share cannot be sliced again, and a technical line cannot be broken down
    /// further. Whether a breakdown after approval needs a re-approval is §8 policy, not chosen here.
    /// </summary>
    public PurchaseOrderLine BreakDown(long expectedVersion, Guid generalLineId, Guid technicalLineId,
        Guid technicalProductId, Quantity slice, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        // Breakdown is open in Draft and Approved (pinned by tests). A1-3 added the third status, Cancelled, and with it this
        // guard, its own code and its test (ORG #288 P3-1): a cancelled order is never reported as "not approved".
        RequireNotCancelled();
        var general = FindLine(generalLineId);
        if (general.Kind != PurchaseOrderLineKind.General)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.TechnicalLineNotBreakable,
                "Teknik satır yeniden kırılamaz; kırılım yalnız genel satırdan yapılır.");
        }

        Identity.Require(technicalLineId, nameof(technicalLineId));
        Identity.Require(technicalProductId, nameof(technicalProductId));
        RequireNewLineId(technicalLineId);
        if (slice.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(slice), "Kırılım miktarı sıfırdan büyük olmalıdır.");
        }

        if (slice.IsGreaterThan(general.RemainingQuantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.SliceExceedsRemaining,
                "Kırılım genel satırın kabul edilmemiş kalanını aşamaz; aynı pay ikinci kez kırılamaz.");
        }

        var technical = new PurchaseOrderLine(technicalLineId, technicalProductId, general.UnitId, slice,
            PurchaseOrderLineKind.Technical, general.Id, general.SourceRequestLineId);
        general.AddTechnicalSlice(slice);
        lines.Add(technical);
        Touch(now);
        return technical;
    }

    /// <summary>
    /// Records an accepted SHARE against a line (§13: 100 ton ordered, 40 accepted → 40 accepted +
    /// 60 remaining; 140 never). Share only: no lot, label, location or stock movement is created
    /// here. A duplicate acceptance id is the same receipt twice and is refused.
    /// </summary>
    public PurchaseAcceptanceShare RecordAcceptance(long expectedVersion, Guid acceptanceId, Guid lineId,
        Quantity accepted, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled();
        RequireStatus(PurchaseOrderStatus.Approved, PurchasingRuleCodes.OrderNotApproved, "Kabul payı yalnız onaylı siparişe kaydedilir.");
        Identity.Require(acceptanceId, nameof(acceptanceId));
        if (acceptances.Any(share => share.AcceptanceId == acceptanceId))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateAcceptanceId, "Aynı kabul kaydı ikinci kez işlenemez.");
        }

        if (accepted.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(accepted), "Kabul miktarı sıfırdan büyük olmalıdır.");
        }

        var line = FindLine(lineId);
        if (accepted.IsGreaterThan(line.RemainingQuantity))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.AcceptanceExceedsRemaining,
                "Kabul payı satırın kalan taahhüdünü aşamaz.");
        }

        line.AddAcceptedShare(accepted);
        var share = new PurchaseAcceptanceShare(acceptanceId, line.Id, accepted, now.ToUniversalTime());
        acceptances.Add(share);
        Touch(now);
        return share;
    }

    /// <summary>
    /// A1-3 (plan API-10; No.161 §2, Sinan 2026-10-27): cancels the WHOLE not-yet-accepted remainder of an APPROVED order — every
    /// line's remaining (committed − accepted − cancelled) becomes its cancelled share, even after a partial acceptance; there is
    /// no line- or quantity-level partial cancel. The accepted shares, the lines and the source request binding stay as they are;
    /// nothing physical or financial happens here (the remainder only leaves the future supply and the order's remaining,
    /// master INV-006: Onaylı − İptal − KabulNet − KesinRed/İade). The reason is required, never defaulted or cut.
    /// <para>
    /// Refusals: a Draft or an already cancelled order is <see cref="PurchasingRuleCodes.OrderNotCancellable"/>; an order with
    /// nothing left to cancel (every line fully accepted) is <see cref="PurchasingRuleCodes.OrderNothingToCancel"/> (fail-closed:
    /// a cancellation that cancels nothing is not recorded). Who may cancel is the service's permission, not this record's.
    /// </para>
    /// </summary>
    public void Cancel(long expectedVersion, string cancelledBy, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(cancelledBy, nameof(cancelledBy));
        if (Status != PurchaseOrderStatus.Approved)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderNotCancellable,
                "Yalnız onaylı siparişin kabul edilmemiş kalanı iptal edilir.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderCancellationReasonRequired, "Sipariş kalanının iptali gerekçe ister.");
        }

        if (reason.Length > MaximumReasonLength)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderCancellationReasonTooLong,
                $"İptal gerekçesi en fazla {MaximumReasonLength} karakterdir; fazlası kesilmez, reddedilir.");
        }

        if (lines.All(line => line.RemainingQuantity.IsZero))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderNothingToCancel,
                "Siparişin kabul edilmemiş kalanı yok; iptal edilecek miktar bulunmuyor.");
        }

        foreach (var line in lines)
        {
            line.CancelRemainder();
        }

        Cancellation = new PurchaseOrderCancellation(actor, now.ToUniversalTime(), reason);
        Status = PurchaseOrderStatus.Cancelled;
        Touch(now);
    }

    /// <summary>A1-3: the bound of a cancellation reason (the request decisions' bound); a longer text is refused, never cut.</summary>
    public const int MaximumReasonLength = 4000;

    /// <summary>A1-3: the actor column's bound.</summary>
    public const int MaximumActorLength = 128;

    /// <summary>
    /// A2a-1: the price rules, all refused with a stable code and never repaired — the order has a currency
    /// (<see cref="PurchasingRuleCodes.PriceCurrencyRequired"/>); the unit price is zero or more — an explicit 0 is a price whose
    /// line amount is 0, kept apart from no price ("fiyat bekliyor", ALS-D18; Sinan #66 6704170096), and on a zero price a discount
    /// can only be a percent or a zero amount (any positive amount exceeds the price: fail-closed, never a negative net); the price unit is the line's entry unit, because no conversion is published yet (ALS-D51/D58/D60,
    /// <see cref="PurchasingRuleCodes.PriceUnitConversionUnavailable"/>); the unit price and a discount amount carry at most the
    /// snapshot's <see cref="PurchaseOrderMoney.PriceScale"/> (A2a-1c, ALS-D72: the profile's unit-price scale, 4 for TRY; without
    /// one the amount scale, Ö2; never rounded) and fit the columns; a discount is a percent (0..100) OR an amount
    /// (0..unit price); and both the net unit price and the line amount are computable exactly at write time (#944 review A P2:
    /// a read never meets a price it cannot compute). No minimum price, margin or discount ceiling is decided for
    /// purchasing (ALS-D5 "elle"), so none is invented.
    /// </summary>
    private void RequirePrice(PurchaseOrderLinePrice price, Guid unitId, Quantity ordered)
    {
        if (Money is not { } money)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.PriceCurrencyRequired, "Fiyatlı satır siparişin para birimini ister.");
        }

        Identity.Require(price.PriceUnitId, nameof(price));
        if (price.UnitPrice < 0m)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.PriceNegative,
                "Birim fiyat negatif olamaz; fiyatı bilinmeyen satır fiyatsız bırakılır, açık 0 fiyat ayrı bir değerdir.");
        }

        if (price.PriceUnitId != unitId)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.PriceUnitConversionUnavailable,
                "Fiyat birimi satırın giriş birimi olmalıdır; birim çevrimi henüz yayımlanmadı, tutar hesaplanamıyor.");
        }

        RequireMoneyText(price.UnitPrice, money.PriceScale);
        if (price.DiscountPercent is not null && price.DiscountAmount is not null)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DiscountKindConflict, "İskonto ya yüzde ya tutar olarak verilir; ikisi birden verilmez.");
        }

        if (price.DiscountPercent is { } percent
            && (percent < 0m || percent > 100m || BuildingBlocks.Monetary.Money.SignificantDecimals(percent) > PurchaseOrderLinePrice.PercentScale))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DiscountPercentOutOfRange,
                $"İskonto yüzdesi 0 ile 100 arasında ve en fazla {PurchaseOrderLinePrice.PercentScale} ondalık olmalıdır.");
        }

        if (price.DiscountAmount is { } amount)
        {
            if (amount < 0m || amount > price.UnitPrice)
            {
                throw new PurchasingRuleException(PurchasingRuleCodes.DiscountExceedsPrice, "İskonto tutarı negatif olamaz ve birim fiyatı aşamaz.");
            }

            RequireMoneyText(amount, money.PriceScale);
        }

        RequireDeferral(price, money);
        try
        {
            _ = price.NetUnitPrice;
            _ = price.LineAmount(ordered.Value, money);
        }
        catch (OverflowException)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.PriceOutOfRange, "Fiyat, miktar ve iskonto hesabı decimal sınırını aşıyor.");
        }
    }

    /// <summary>
    /// A2a-2 (ALS-D38, ALS-D75 "Satış'la aynı"): a maturity surcharge is a percent (0..100, at most six decimals — the Sales bound) of the
    /// discounted unit price OR an amount per unit (zero or more, at most the price scale), never both, and never on a 0 unit price
    /// (Sinan, EK13). A deferred price typed directly as the unit price carries none (ALS-D76).
    /// </summary>
    private static void RequireDeferral(PurchaseOrderLinePrice price, PurchaseOrderMoney money)
    {
        if (!price.HasDeferral)
        {
            return;
        }

        if (price.DeferralPercent is not null && price.DeferralAmount is not null)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DeferralKindConflict, "Vade farkı ya yüzde ya tutar olarak verilir; ikisi birden verilmez.");
        }

        if (price.UnitPrice == 0m)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DeferralOnZeroPrice, "0 fiyatlı satıra vade farkı girilmez.");
        }

        if (price.DeferralPercent is { } percent
            && (percent < 0m || percent > 100m || BuildingBlocks.Monetary.Money.SignificantDecimals(percent) > PurchaseOrderLinePrice.PercentScale))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DeferralPercentOutOfRange,
                $"Vade farkı yüzdesi 0 ile 100 arasında ve en fazla {PurchaseOrderLinePrice.PercentScale} ondalık olmalıdır.");
        }

        if (price.DeferralAmount is { } amount)
        {
            if (amount < 0m)
            {
                throw new PurchasingRuleException(PurchasingRuleCodes.DeferralAmountNegative, "Vade farkı tutarı negatif olamaz.");
            }

            RequireMoneyText(amount, money.PriceScale);
        }
    }

    private static void RequireMoneyText(decimal value, int scale)
    {
        if (BuildingBlocks.Monetary.Money.SignificantDecimals(value) > scale)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.PriceScaleExceeded,
                $"Tutar en fazla {scale} ondalık taşıyabilir; fazlası yuvarlanmaz, reddedilir.");
        }

        if (decimal.Truncate(decimal.Abs(value)).ToString(System.Globalization.CultureInfo.InvariantCulture).Length > PurchaseOrderLinePrice.MaximumIntegerDigits)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.PriceOutOfRange, "Tutar saklanan alanın tam sayı basamağını aşıyor.");
        }
    }

    /// <summary>What is still expected from the supplier in one unit: future supply, never stock.</summary>
    public Quantity RemainingCommitment(QuantityUnit unit) =>
        lines.Where(line => line.Quantity.Unit == unit)
            .Aggregate(Quantity.Zero(unit), (sum, line) => sum.Add(line.RemainingQuantity));

    private PurchaseOrderLine FindLine(Guid lineId) =>
        lines.FirstOrDefault(line => line.Id == lineId)
        ?? throw new PurchasingRuleException(PurchasingRuleCodes.LineNotFound, "Sipariş satırı bulunamadı.");

    private void RequireNewLineId(Guid lineId)
    {
        if (lines.Any(line => line.Id == lineId))
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.DuplicateLineId, "Aynı sipariş satırı kimliği ikinci kez eklenemez.");
        }
    }

    private static string RequireActor(string? actor, string parameterName) =>
        string.IsNullOrWhiteSpace(actor) || actor.Length > MaximumActorLength
        || !string.Equals(actor, actor.Trim(), StringComparison.Ordinal)
            ? throw new ArgumentException("Aktör kimliği boş, kenar boşluklu veya çok uzun olamaz.", parameterName)
            : actor;

    private void RequireNotCancelled()
    {
        if (Status == PurchaseOrderStatus.Cancelled)
        {
            throw new PurchasingRuleException(PurchasingRuleCodes.OrderCancelled,
                "Kalanı iptal edilmiş siparişe kabul payı veya kırılım eklenmez.");
        }
    }

    private void RequireStatus(PurchaseOrderStatus required, string code, string message)
    {
        if (Status != required)
        {
            throw new PurchasingRuleException(code, message);
        }
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new PurchasingVersionConflictException(expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }
}

/// <summary>A2a-5a (ALS-D3): the recorded approval of an order — who approved it (never its preparer) and when.</summary>
public sealed record PurchaseOrderApproval(string ApprovedBy, DateTimeOffset ApprovedAtUtc);

/// <summary>A1-3 (No.161 §2): the recorded cancellation of an order's unaccepted remainder — who, when and why.</summary>
public sealed record PurchaseOrderCancellation(string CancelledBy, DateTimeOffset CancelledAtUtc, string Reason);

/// <summary>One recorded acceptance share. Not a receipt document and not a stock movement.</summary>
public sealed record PurchaseAcceptanceShare(Guid AcceptanceId, Guid LineId, Quantity Quantity, DateTimeOffset RecordedAtUtc);
