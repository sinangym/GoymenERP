using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-22…24: the stored quote header — customer, currency, business date and the money profile its prices were checked
/// under; its lifecycle (state, current revision, loss, conversion) is the only change (the write guard admits exactly those).
/// </summary>
internal sealed class QuoteRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;

    /// <summary>1 Open, 2 Converted, 3 Lost (<see cref="SalesQuoteState"/>); the facts follow the state (check constraint).</summary>
    public int State { get; set; }
    public int Revision { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public DateOnly BusinessDate { get; set; }

    public string MoneyOperation { get; set; } = string.Empty;
    public Guid MoneyProfileId { get; set; }
    public string MoneyProfileVersion { get; set; } = string.Empty;
    public DateOnly MoneyEffectiveOn { get; set; }
    public bool MoneyIsActive { get; set; }
    public DateOnly MoneyEffectiveFrom { get; set; }
    public DateOnly? MoneyEffectiveThrough { get; set; }
    public int MoneyScale { get; set; }

    public string? LossReason { get; set; }
    public string? LostBy { get; set; }
    public DateTimeOffset? LostAtUtc { get; set; }
    public Guid? ConvertedDraftId { get; set; }
    public string? ConvertedBy { get; set; }
    public DateTimeOffset? ConvertedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>One recorded revision of a quote (Rev.0 at creation): its terms, who and when. Append-only, never rewritten.</summary>
internal sealed class QuoteRevisionRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid QuoteId { get; set; }
    public int Revision { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public DateOnly? RequestedDeliveryDate { get; set; }
    public string? Note { get; set; }
    public string RevisedBy { get; set; } = string.Empty;
    public DateTimeOffset RevisedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>One line of one quote revision with the owners' snapshots and the quoted commercial texts. Append-only.</summary>
internal sealed class QuoteLineRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid QuoteId { get; set; }
    public int Revision { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }

    public Guid ProductId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateOnly ProductEffectiveOn { get; set; }

    public Guid StockUnitCompanyId { get; set; }
    public Guid StockUnitId { get; set; }
    public Guid StockUnitProfileId { get; set; }
    public string StockUnitProfileVersion { get; set; } = string.Empty;
    public Guid SalesUnitCompanyId { get; set; }
    public Guid SalesUnitId { get; set; }
    public Guid SalesUnitProfileId { get; set; }
    public string SalesUnitProfileVersion { get; set; } = string.Empty;
    public Guid OrderUnitBindingCompanyId { get; set; }
    public Guid OrderUnitBindingUnitId { get; set; }
    public Guid OrderUnitBindingProfileId { get; set; }
    public string OrderUnitBindingProfileVersion { get; set; } = string.Empty;

    public Guid OrderUnitCompanyId { get; set; }
    public Guid OrderUnitId { get; set; }
    public string OrderUnitCode { get; set; } = string.Empty;
    public string OrderUnitDimensionCode { get; set; } = string.Empty;
    public Guid OrderUnitProfileId { get; set; }
    public string OrderUnitProfileVersion { get; set; } = string.Empty;
    public bool OrderUnitIsKilogram { get; set; }
    public int OrderUnitProfileScale { get; set; }
    public int OrderUnitScale { get; set; }
    public bool OrderUnitIsActive { get; set; }
    public DateOnly OrderUnitEffectiveFrom { get; set; }
    public DateOnly? OrderUnitEffectiveThrough { get; set; }

    public decimal Quantity { get; set; }
    public string QuantityText { get; set; } = string.Empty;
    public string? UnitPrice { get; set; }
    public Guid? PriceUnitId { get; set; }
    public string? DiscountPercent { get; set; }
    public DateOnly? RequestedDeliveryDate { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// ADR-SAL-001 §2 (API-22 / API-24): the quote commands' attempt record (create / revise / lose) — the draft attempt's shape
/// in the quote's own table: (company, command name, uppercase SHA-256 of the trimmed key) is the identity, the raw key is
/// never stored, the request hash is its content and the actor is stored to be compared. Append-only: the EF write guard and
/// the SQL trigger refuse every UPDATE and DELETE. The conversion's attempt is the ORDER's (<see cref="DraftAttemptRow"/>).
/// </summary>
internal sealed class QuoteAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = a stored quote (<see cref="QuoteId"/> set); 2 = a recorded terminal refusal (<see cref="RefusalJson"/> set) — ADR-011 §7.</summary>
    public int Outcome { get; set; }
    public Guid? QuoteId { get; set; }
    public string? RefusalJson { get; set; }

    /// <summary>ADR-SAL-001 §2: kept for the shape; no quote command stores a response snapshot (CK_QuoteAttempts_Result).</summary>
    public string? ResultJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// No 163: one request a quote covers. <see cref="IsActive"/> is true while the quote is open or converted (a request is
/// covered by at most ONE such quote: filtered unique index) and false once the quote is lost — the only change admitted.
/// </summary>
internal sealed class QuoteRequestRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid QuoteId { get; set; }
    public Guid RequestId { get; set; }
    public bool IsActive { get; set; }
}

internal static class SalesQuoteRows
{
    internal static void Add(SalesDbContext dbContext, SalesQuote quote, string correlationId)
    {
        var money = quote.Money;
        dbContext.Quotes.Add(new QuoteRow
        {
            Id = quote.Id,
            CompanyId = quote.CompanyId,
            DocumentNo = quote.DocumentNo,
            State = (int)quote.State,
            Revision = quote.Revision,
            Version = quote.Version,
            PartyId = quote.Header.PartyId,
            CompanyAccountId = quote.Header.CompanyAccountId,
            CurrencyId = quote.Header.CurrencyId,
            BusinessDate = quote.Header.BusinessDate,
            MoneyOperation = money.Operation,
            MoneyProfileId = money.ProfileId,
            MoneyProfileVersion = money.ProfileVersion,
            MoneyEffectiveOn = money.EffectiveOn,
            MoneyIsActive = money.IsActive,
            MoneyEffectiveFrom = money.EffectiveFrom,
            MoneyEffectiveThrough = money.EffectiveThrough,
            MoneyScale = money.Scale,
            CreatedAtUtc = quote.CreatedAtUtc,
            UpdatedAtUtc = quote.UpdatedAtUtc,
            CreatedBy = quote.CreatedBy,
            CorrelationId = correlationId.Trim(),
        });
        foreach (var requestId in quote.RequestIds)
        {
            dbContext.QuoteRequests.Add(new QuoteRequestRow { CompanyId = quote.CompanyId, QuoteId = quote.Id, RequestId = requestId, IsActive = true });
        }

        AddRevision(dbContext, quote, quote.CreatedBy, correlationId);
    }

    /// <summary>Appends the quote's CURRENT revision (its terms and lines); earlier revisions are never rewritten.</summary>
    internal static void AddRevision(SalesDbContext dbContext, SalesQuote quote, string revisedBy, string correlationId)
    {
        dbContext.QuoteRevisions.Add(new QuoteRevisionRow
        {
            CompanyId = quote.CompanyId,
            QuoteId = quote.Id,
            Revision = quote.Revision,
            ValidUntil = quote.Terms.ValidUntil,
            RequestedDeliveryDate = quote.Terms.RequestedDeliveryDate,
            Note = quote.Terms.Note,
            RevisedBy = revisedBy.Trim(),
            RevisedAtUtc = quote.UpdatedAtUtc,
            CorrelationId = correlationId.Trim(),
        });
        foreach (var line in quote.Lines)
        {
            var product = line.Product;
            var unit = line.OrderUnit;
            dbContext.QuoteLines.Add(new QuoteLineRow
            {
                CompanyId = quote.CompanyId,
                QuoteId = quote.Id,
                Revision = quote.Revision,
                LineNo = line.LineNo,
                LineId = line.Id,
                ProductId = product.ProductId,
                ProductVersion = product.ProductVersion,
                ProductCode = product.Code,
                ProductName = product.Name,
                ProductEffectiveOn = product.EffectiveOn,
                StockUnitCompanyId = product.StockUnit.CompanyId,
                StockUnitId = product.StockUnit.UnitId,
                StockUnitProfileId = product.StockUnit.ProfileId,
                StockUnitProfileVersion = product.StockUnit.ProfileVersion,
                SalesUnitCompanyId = product.SalesUnit.CompanyId,
                SalesUnitId = product.SalesUnit.UnitId,
                SalesUnitProfileId = product.SalesUnit.ProfileId,
                SalesUnitProfileVersion = product.SalesUnit.ProfileVersion,
                OrderUnitBindingCompanyId = product.OrderUnit.CompanyId,
                OrderUnitBindingUnitId = product.OrderUnit.UnitId,
                OrderUnitBindingProfileId = product.OrderUnit.ProfileId,
                OrderUnitBindingProfileVersion = product.OrderUnit.ProfileVersion,
                OrderUnitCompanyId = unit.CompanyId,
                OrderUnitId = unit.UnitId,
                OrderUnitCode = unit.UnitCode,
                OrderUnitDimensionCode = unit.DimensionCode,
                OrderUnitProfileId = unit.ProfileId,
                OrderUnitProfileVersion = unit.ProfileVersion,
                OrderUnitIsKilogram = unit.IsKilogram,
                OrderUnitProfileScale = unit.ProfileScale,
                OrderUnitScale = unit.Scale,
                OrderUnitIsActive = unit.IsActive,
                OrderUnitEffectiveFrom = unit.EffectiveFrom,
                OrderUnitEffectiveThrough = unit.EffectiveThrough,
                Quantity = line.Quantity.Value,
                QuantityText = line.QuantityText,
                UnitPrice = line.UnitPrice,
                PriceUnitId = line.PriceUnitId,
                DiscountPercent = line.DiscountPercent,
                RequestedDeliveryDate = line.RequestedDeliveryDate,
                Note = line.Note,
            });
        }
    }

    /// <summary>Rebuilds a stored quote through the domain on its CURRENT revision; a corrupt row is refused, not repaired.</summary>
    internal static SalesQuote Restore(QuoteRow row, QuoteRevisionRow revision, IEnumerable<QuoteLineRow> lineRows, IEnumerable<QuoteRequestRow> requests)
    {
        if (revision.Revision != row.Revision || revision.QuoteId != row.Id)
        {
            throw new InvalidOperationException("Kayıtlı teklif revizyonu başlıkla uyuşmuyor.");
        }

        var lines = lineRows.Where(line => line.Revision == row.Revision).OrderBy(line => line.LineNo).ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].LineNo != index + 1)
            {
                throw new InvalidOperationException("Kayıtlı teklif satırları kesintisiz numaralı değil.");
            }
        }

        var money = new MoneyProfileSnapshot(row.CompanyId, row.CurrencyId, row.MoneyOperation, row.MoneyProfileId, row.MoneyProfileVersion,
            row.MoneyEffectiveOn, row.MoneyIsActive, row.MoneyEffectiveFrom, row.MoneyEffectiveThrough, row.MoneyScale);
        var quote = SalesQuote.Create(row.Id, row.CompanyId, row.DocumentNo,
            new SalesQuoteHeader(row.PartyId, row.CompanyAccountId, row.CurrencyId, row.BusinessDate), money,
            [.. requests.Select(request => request.RequestId).Order()],
            new SalesQuoteTerms(revision.ValidUntil, revision.RequestedDeliveryDate, revision.Note),
            [.. lines.Select(line => new SalesQuoteLineInput(line.LineId,
                new ProductSnapshot(line.ProductId, line.ProductVersion, line.ProductCode, line.ProductName, line.ProductEffectiveOn,
                    new UnitBindingSnapshot(line.StockUnitCompanyId, line.StockUnitId, line.StockUnitProfileId, line.StockUnitProfileVersion),
                    new UnitBindingSnapshot(line.SalesUnitCompanyId, line.SalesUnitId, line.SalesUnitProfileId, line.SalesUnitProfileVersion),
                    new UnitBindingSnapshot(line.OrderUnitBindingCompanyId, line.OrderUnitBindingUnitId, line.OrderUnitBindingProfileId,
                        line.OrderUnitBindingProfileVersion)),
                new UnitProfileSnapshot(line.OrderUnitCompanyId, line.OrderUnitId, line.OrderUnitCode, line.OrderUnitDimensionCode,
                    line.OrderUnitProfileId, line.OrderUnitProfileVersion, line.OrderUnitIsKilogram, line.OrderUnitProfileScale,
                    line.OrderUnitScale, line.OrderUnitIsActive, line.OrderUnitEffectiveFrom, line.OrderUnitEffectiveThrough),
                line.QuantityText, line.Quantity, line.UnitPrice, line.PriceUnitId, line.DiscountPercent, line.RequestedDeliveryDate, line.Note))],
            row.CreatedBy, row.CreatedAtUtc);
        if (!Enum.IsDefined(typeof(SalesQuoteState), row.State))
        {
            throw new InvalidOperationException("Kayıtlı teklif durumu tanınmıyor.");
        }

        var loss = row.LossReason is { } reason && row.LostBy is { } lostBy && row.LostAtUtc is { } lostAt
            ? new SalesQuoteLoss(reason, lostBy, lostAt)
            : null;
        var conversion = row.ConvertedDraftId is { } draftId && row.ConvertedBy is { } convertedBy && row.ConvertedAtUtc is { } convertedAt
            ? new SalesQuoteConversion(draftId, convertedBy, convertedAt)
            : null;
        quote.Restore((SalesQuoteState)row.State, row.Revision, loss, conversion, row.Version, row.UpdatedAtUtc);
        return quote;
    }
}
