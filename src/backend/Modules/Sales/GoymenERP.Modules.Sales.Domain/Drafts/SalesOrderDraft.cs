using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

public enum SalesDraftKind
{
    Order = 1,
}

public enum SalesDraftState
{
    Draft = 1,
}

/// <summary>The header facts of a draft as the client sent them; identities only, validated by their owners at command time.</summary>
public sealed record SalesDraftHeader(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    DateOnly BusinessDate,
    DateOnly? RequestedDeliveryDate = null,
    string? Note = null,
    OwnerReference? DeliveryAddress = null,
    OwnerReference? Contact = null,
    OwnerReference? Responsible = null,
    OwnerReference? PaymentTerms = null,
    OwnerReference? SourceStrategy = null,
    OwnerReference? Package = null);

/// <summary>Everything a line needs at <see cref="SalesOrderDraft.AddLine"/>; the owner snapshots come from the ports, the rest from the client.</summary>
public sealed record SalesDraftLineInput(
    Guid LineId,
    ProductSnapshot Product,
    UnitProfileSnapshot OrderUnit,
    string QuantityText,
    decimal Quantity,
    DateOnly? RequestedDeliveryDate = null,
    string? Note = null,
    CommercialDraftFields? Commercial = null,
    IReadOnlyList<DeliverySliceInput>? RequestedDeliverySlices = null);

public sealed record DeliverySliceInput(Guid SliceId, Guid OrderUnitId, decimal Quantity, DateOnly RequestedDeliveryDate);

/// <summary>
/// A sales order draft (T03, SALES-P1): one company, one customer (Party + CompanyPartyAccount),
/// one currency, one business date, 1–500 lines. It is born <see cref="SalesDraftState.Draft"/>
/// at commercial revision 0 and stays there in this slice: approval, revision and cancellation
/// are T33; prices, discounts, taxes and rates are T28 and are retained only as text.
/// <list type="bullet">
/// <item><b>Version ≠ revision:</b> <see cref="Version"/> is the optimistic-concurrency token every
/// mutation names (the Purchasing / Logistics / Manufacturing pattern); <see cref="Revision"/> is
/// the commercial revision (<c>Rev.0</c>) and does not move here.</item>
/// <item><b>Owner snapshots:</b> a line carries what the Product and Settings owners answered for
/// this company on this business date; the draft refuses a snapshot of another company or another
/// date, and an order unit the Product snapshot does not bind as the order unit.</item>
/// <item><b>Ö2:</b> the quantity's dimension must be the unit profile's; decimals beyond
/// min(profile scale, 3) are refused, never rounded; the client's text is kept verbatim.</item>
/// <item><b>Delivery plan:</b> slices are same-unit, positive, unique, at most 500, and never add
/// up to more than the line; a shortfall is "unplanned", never "delivered".</item>
/// <item><b>Money:</b> the draft carries the Settings money profile snapshot for its currency;
/// there is no draft without one (#257: currency mandatory even unpriced). Today the approved
/// provider has none, so no real draft is stored — the store fails closed before this type.</item>
/// </list>
/// Nothing here authorizes; company and actor are the server's, and the document number is a
/// technical, company-scoped identifier, not an official document (Ö8).
/// </summary>
public sealed class SalesOrderDraft
{
    public const int MaximumLines = 500;
    public const int MaximumSlicesPerLine = 500;
    public const int MaximumNoteLength = 4000;
    public const int MaximumDocumentNoLength = 64;

    private readonly List<SalesOrderDraftLine> lines = [];

    private SalesOrderDraft(Guid id, Guid companyId, string documentNo, SalesDraftHeader header,
        MoneyProfileSnapshot money, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        DocumentNo = RequireDocumentNo(documentNo);
        Header = header;
        Money = money;
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }

    /// <summary>The server-resolved company; never taken from the request and never changed.</summary>
    public Guid CompanyId { get; }

    /// <summary>Technical, company-scoped display identifier assigned by the store on first commit. Not an official document number (Ö8); no gap-free guarantee.</summary>
    public string DocumentNo { get; }

    public SalesDraftKind Kind => SalesDraftKind.Order;

    public SalesDraftState State => SalesDraftState.Draft;

    /// <summary>Commercial revision; <c>Rev.0</c> on the wire. Independent of <see cref="Version"/> and fixed in this slice (T33).</summary>
    public int Revision => 0;

    /// <summary>Optimistic concurrency; starts at 1 and advances by one per recorded mutation.</summary>
    public long Version { get; private set; } = 1;

    public SalesDraftHeader Header { get; }

    public MoneyProfileSnapshot Money { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<SalesOrderDraftLine> Lines => lines.AsReadOnly();

    public static SalesOrderDraft Create(Guid id, Guid companyId, string documentNo, SalesDraftHeader header,
        MoneyProfileSnapshot money, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(money);
        Identity.Require(header.PartyId, nameof(header.PartyId));
        Identity.Require(header.CompanyAccountId, nameof(header.CompanyAccountId));
        Identity.Require(header.CurrencyId, nameof(header.CurrencyId));
        RequireNote(header.Note);
        foreach (var reference in new[] { header.DeliveryAddress, header.Contact, header.Responsible, header.PaymentTerms, header.SourceStrategy, header.Package })
        {
            if (reference is not null && (reference.Id == Guid.Empty || string.IsNullOrWhiteSpace(reference.ExpectedVersion)))
            {
                throw new ArgumentException("Sahip referansı kimlik ve beklenen sürüm ister.", nameof(header));
            }
        }

        if (money.CompanyId != companyId)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotCompanyMismatch, "Para profili başka şirketin.");
        }

        if (money.CurrencyId != header.CurrencyId || money.ProfileId == Guid.Empty || string.IsNullOrWhiteSpace(money.ProfileVersion))
        {
            throw new ArgumentException("Para profili taslağın para birimini adlandırmalı ve tam olmalıdır.", nameof(money));
        }

        if (money.EffectiveOn != header.BusinessDate)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotDateMismatch, "Para profili taslağın iş tarihi için alınmamış.");
        }

        return new SalesOrderDraft(id, companyId, documentNo, header, money, now);
    }

    /// <summary>
    /// Adds a line from the client's input and the owners' snapshots. The aggregate is the single
    /// authority: draft state, line count and identity, snapshot company/date/unit consistency,
    /// quantity dimension and scale, note length, commercial pairing, and the delivery plan.
    /// </summary>
    public SalesOrderDraftLine AddLine(long expectedVersion, SalesDraftLineInput input, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Product);
        ArgumentNullException.ThrowIfNull(input.OrderUnit);
        RequireDraft();
        Identity.Require(input.LineId, nameof(input.LineId));
        if (lines.Count >= MaximumLines)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManyLines, $"Bir taslak en fazla {MaximumLines} satır taşır.");
        }

        if (lines.Any(line => line.Id == input.LineId))
        {
            throw new SalesRuleException(SalesRuleCodes.DuplicateLineId, "Aynı satır kimliği ikinci kez eklenemez.");
        }

        RequireNote(input.Note);
        RequireSnapshots(input.Product, input.OrderUnit);
        var quantity = RequireQuantity(input.Quantity, input.OrderUnit);
        RequireQuantityText(input.QuantityText, input.Quantity);
        var commercial = input.Commercial ?? CommercialDraftFields.None;
        if ((commercial.UnitPrice is null) != (commercial.PriceUnitId is null))
        {
            throw new SalesRuleException(SalesRuleCodes.PriceUnitPairing,
                "Birim fiyat kendi fiyat birimini ister; fiyat birimi fiyatsız verilemez.");
        }

        var slices = RequireSlices(input.RequestedDeliverySlices, input.OrderUnit, quantity);
        var line = new SalesOrderDraftLine(input.LineId, lines.Count + 1, input.Product, input.OrderUnit, quantity,
            input.QuantityText, input.RequestedDeliveryDate, input.Note, commercial, slices);
        lines.Add(line);
        Touch(now);
        return line;
    }

    /// <summary>A draft is stored only with at least one line (#257: 1–500 lines).</summary>
    public void RequireStorable()
    {
        if (lines.Count == 0)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftWithoutLines, "Satırsız taslak kaydedilmez.");
        }
    }

    private void RequireSnapshots(ProductSnapshot product, UnitProfileSnapshot orderUnit)
    {
        if (product.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(product.ProductVersion)
            || string.IsNullOrWhiteSpace(product.Code) || string.IsNullOrWhiteSpace(product.Name)
            || product.StockUnit is null || product.SalesUnit is null || product.OrderUnit is null)
        {
            throw new ArgumentException("Ürün snapshot'ı eksik.", nameof(product));
        }

        if (orderUnit.UnitId == Guid.Empty || orderUnit.ProfileId == Guid.Empty || string.IsNullOrWhiteSpace(orderUnit.UnitCode)
            || string.IsNullOrWhiteSpace(orderUnit.ProfileVersion) || orderUnit.ProfileScale < 0 || orderUnit.Scale < 0)
        {
            throw new ArgumentException("Birim profili snapshot'ı eksik.", nameof(orderUnit));
        }

        // The snapshot's stated applied scale must be what the domain derives; a snapshot that
        // says "3" over a profile of 1 (or "5" over Ö2's 3) is malformed, not a wider permission.
        if (orderUnit.Scale != AppliedScale(orderUnit))
        {
            throw new ArgumentException("Birim profili snapshot'ının uygulanan ölçeği min(profil, Ö2) ile uyuşmuyor.", nameof(orderUnit));
        }

        foreach (var binding in new[] { product.StockUnit, product.SalesUnit, product.OrderUnit })
        {
            if (binding.CompanyId != CompanyId)
            {
                throw new SalesRuleException(SalesRuleCodes.SnapshotCompanyMismatch, "Ürün birim bağı başka şirketin.");
            }
        }

        if (orderUnit.CompanyId != CompanyId)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotCompanyMismatch, "Birim profili başka şirketin.");
        }

        if (product.EffectiveOn != Header.BusinessDate)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotDateMismatch, "Ürün snapshot'ı taslağın iş tarihi için alınmamış.");
        }

        if (product.OrderUnit.UnitId != orderUnit.UnitId || product.OrderUnit.ProfileId != orderUnit.ProfileId
            || !string.Equals(product.OrderUnit.ProfileVersion, orderUnit.ProfileVersion, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.ProductUnitMismatch,
                "Seçilen sipariş birimi, ürün sahibinin sipariş birimi bağıyla aynı değil.");
        }
    }

    /// <summary>The scale the domain applies: min(owner's profile scale, Ö2's own cap for the dimension). Derived here, never taken from the snapshot's word (ÜRÜN #306 P3-1).</summary>
    public static int AppliedScale(UnitProfileSnapshot orderUnit)
    {
        ArgumentNullException.ThrowIfNull(orderUnit);
        var unit = orderUnit.IsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
        return Math.Min(orderUnit.ProfileScale, Quantity.AllowedScale(unit));
    }

    private static Quantity RequireQuantity(decimal value, UnitProfileSnapshot orderUnit)
    {
        var unit = orderUnit.IsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Satır miktarı sıfırdan büyük olmalıdır.");
        }

        var applied = AppliedScale(orderUnit);
        if (Quantity.SignificantDecimals(value) > applied)
        {
            throw new SalesRuleException(SalesRuleCodes.QuantityScaleExceeded,
                $"Miktar en fazla {applied} ondalık taşıyabilir; fazlası yuvarlanmaz, reddedilir.");
        }

        return Quantity.Positive(value, unit);
    }

    private static IReadOnlyList<DeliverySlice> RequireSlices(IReadOnlyList<DeliverySliceInput>? inputs,
        UnitProfileSnapshot orderUnit, Quantity quantity)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return [];
        }

        if (inputs.Count > MaximumSlicesPerLine)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManySlices, $"Bir satır en fazla {MaximumSlicesPerLine} termin dilimi taşır.");
        }

        var ids = new HashSet<Guid>();
        var slices = new List<DeliverySlice>(inputs.Count);
        var planned = Quantity.Zero(quantity.Unit);
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            Identity.Require(input.SliceId, nameof(input.SliceId));
            if (!ids.Add(input.SliceId))
            {
                throw new SalesRuleException(SalesRuleCodes.DuplicateSliceId, "Aynı dilim kimliği ikinci kez verilemez.");
            }

            if (input.OrderUnitId != orderUnit.UnitId)
            {
                throw new SalesRuleException(SalesRuleCodes.SliceUnitMismatch, "Termin dilimi satırın biriminde olmalıdır; dönüşüm yok.");
            }

            var sliceQuantity = RequireQuantity(input.Quantity, orderUnit);
            planned = planned.Add(sliceQuantity);
            if (planned.IsGreaterThan(quantity))
            {
                throw new SalesRuleException(SalesRuleCodes.SlicesExceedQuantity, "Termin dilimleri satır miktarını aşamaz.");
            }

            slices.Add(new DeliverySlice(input.SliceId, sliceQuantity, input.RequestedDeliveryDate));
        }

        return slices;
    }

    /// <summary>
    /// The client's quantity text is kept verbatim, but it must BE the quantity: the same decimal
    /// value in plain invariant notation (#257's wire shape; ÜRÜN #306 P3-3). "12.500" and 12.5m
    /// agree; "12.5" and 12.51m, or "1,2", do not.
    /// </summary>
    private static void RequireQuantityText(string? text, decimal value)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !decimal.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            || parsed != value)
        {
            throw new ArgumentException("Miktar metni miktarın kendisiyle uyuşmuyor.", nameof(text));
        }
    }

    private static void RequireNote(string? note)
    {
        if (note is not null && note.Length > MaximumNoteLength)
        {
            throw new SalesRuleException(SalesRuleCodes.NoteTooLong, $"Not en fazla {MaximumNoteLength} karakterdir.");
        }
    }

    private static string RequireDocumentNo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Belge numarası boş olamaz.", nameof(value));
        }

        var documentNo = value.Trim();
        return documentNo.Length <= MaximumDocumentNoLength
            ? documentNo
            : throw new ArgumentException($"Belge numarası en fazla {MaximumDocumentNoLength} karakterdir.", nameof(value));
    }

    private void RequireDraft()
    {
        if (State != SalesDraftState.Draft)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftNotDraft, "Yalnız taslak değiştirilebilir.");
        }
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new SalesVersionConflictException(expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }
}
