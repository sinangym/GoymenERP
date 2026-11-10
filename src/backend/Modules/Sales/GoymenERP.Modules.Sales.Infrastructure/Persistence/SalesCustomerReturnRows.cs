using System.Text.Json;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-40 (SAT-D288…SAT-D294): one customer return invoice as Sales records it (<c>sales.CustomerReturns</c>). The header copied
/// from the original invoice, the customer's document and the lines never change; only the approval axis moves (Draft →
/// PendingApproval → Approved → Finalized, one step at a time; the write guard and the checks). The totals are the sums of the
/// lines (SAT-D292), stored for the list; the lines are a JSON array. <see cref="FinalizationSequence"/> is the return's place
/// among the FINALISED returns of its original invoice: UX_CustomerReturns_FinalizationSequence makes two finalisations computed
/// against the same priors one concurrent change (SAT-D292 (6)).
/// </summary>
internal sealed class CustomerReturnRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OriginalInvoiceId { get; set; }
    public string OriginalDocumentNo { get; set; } = string.Empty;
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public int AmountScale { get; set; }

    /// <summary>SAT-D172 / SAT-D289 (1): the original invoice's locked rate; null exactly for a TL return (with the two below).</summary>
    public decimal? ExchangeRate { get; set; }
    public Guid? FunctionalCurrencyId { get; set; }
    public int? FunctionalScale { get; set; }

    /// <summary>SAT-D288 (3): the optional shipment link; an opaque Logistics reference Sales never reads or writes.</summary>
    public Guid? ShipmentId { get; set; }

    /// <summary>SAT-D288 (1): the customer's own document number and date (the ERP never issues the return invoice).</summary>
    public string CustomerDocumentNo { get; set; } = string.Empty;
    public DateOnly CustomerDocumentDate { get; set; }
    public int State { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }

    /// <summary>SAT-D290: the approver — never the preparer or the submitter (CK_CustomerReturns_MakerChecker).</summary>
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? FinalizedBy { get; set; }
    public DateTimeOffset? FinalizedAtUtc { get; set; }
    public int? FinalizationSequence { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal WithholdingAmount { get; set; }
    public decimal LineTotalAmount { get; set; }
    public decimal? FunctionalNetAmount { get; set; }
    public decimal? FunctionalVatAmount { get; set; }
    public decimal? FunctionalWithholdingAmount { get; set; }
    public decimal? FunctionalLineTotalAmount { get; set; }
    public bool NeedsReview { get; set; }
    public int LineCount { get; set; }
    public string LinesJson { get; set; } = "[]";
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>API-40 (ADR-011 §2–§8): the customer return commands' own attempt records (append-only).</summary>
internal sealed class CustomerReturnAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = a stored return (<see cref="CustomerReturnId"/> set); 2 = a recorded terminal refusal (<see cref="RefusalJson"/> set).</summary>
    public int Outcome { get; set; }
    public Guid? CustomerReturnId { get; set; }

    /// <summary>The return a refused state command named (null for a refused create); kept as data, never a foreign key.</summary>
    public Guid? TargetId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>The one mapping between a domain customer return and its row (write) and back (read; the domain checks consistency).</summary>
internal static class SalesCustomerReturnRows
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private sealed record StoredAmounts(decimal Net, decimal Vat, decimal Withholding, decimal LineTotal);

    private sealed record StoredLine(int LineNo, Guid OriginalInvoiceLineId, int OriginalLineNo, Guid ShipmentId, Guid ProductId,
        Guid UnitId, decimal Quantity, decimal NetUnitPrice, decimal VatPercent, decimal? WithholdingShare, bool IsLastReturn,
        StoredAmounts Amounts, StoredAmounts? Functional, bool NeedsReview);

    public static CustomerReturnRow ToRow(SalesCustomerReturn item, string correlationId)
    {
        var header = item.Header;
        if (header.AmountScale > SalesInvoiceRows.MaximumStoredScale || header.FunctionalScale > SalesInvoiceRows.MaximumStoredScale)
        {
            throw new SalesDraftStoreException(SalesDraftStoreCodes.ValueDoesNotFit, "İade tutar ölçeği saklanan sütuna sığmıyor.");
        }

        var totals = item.Totals;
        var functional = item.FunctionalTotals;
        var row = new CustomerReturnRow
        {
            Id = item.Id,
            CompanyId = item.CompanyId,
            OriginalInvoiceId = header.OriginalInvoiceId,
            OriginalDocumentNo = header.OriginalDocumentNo,
            PartyId = header.PartyId,
            CompanyAccountId = header.CompanyAccountId,
            CurrencyId = header.CurrencyId,
            AmountScale = header.AmountScale,
            ExchangeRate = header.Rate,
            FunctionalCurrencyId = header.FunctionalCurrencyId,
            FunctionalScale = header.FunctionalScale,
            ShipmentId = item.ShipmentId,
            CustomerDocumentNo = item.CustomerDocumentNo,
            CustomerDocumentDate = item.CustomerDocumentDate,
            CreatedBy = item.CreatedBy,
            CreatedAtUtc = item.CreatedAtUtc,
            NetAmount = totals.Net.Amount,
            VatAmount = totals.Vat.Amount,
            WithholdingAmount = totals.Withholding.Amount,
            LineTotalAmount = totals.LineTotal.Amount,
            FunctionalNetAmount = functional?.Net.Amount,
            FunctionalVatAmount = functional?.Vat.Amount,
            FunctionalWithholdingAmount = functional?.Withholding.Amount,
            FunctionalLineTotalAmount = functional?.LineTotal.Amount,
            NeedsReview = item.NeedsReview,
            LineCount = item.Lines.Count,
            LinesJson = JsonSerializer.Serialize(item.Lines.Select(ToStored).ToList(), Json),
            CorrelationId = correlationId.Trim(),
        };
        Apply(row, item);
        return row;
    }

    /// <summary>Rebuilds the domain return through its own rules; a corrupt row is refused, never repaired.</summary>
    public static SalesCustomerReturn Restore(CustomerReturnRow row)
    {
        if (!Enum.IsDefined((SalesCustomerReturnState)row.State))
        {
            throw new InvalidOperationException("Kayıtlı iade faturası durumu tanınmıyor.");
        }

        var header = new SalesCustomerReturnHeader(row.OriginalInvoiceId, row.OriginalDocumentNo, row.PartyId, row.CompanyAccountId,
            row.CurrencyId, row.AmountScale, row.ExchangeRate, row.FunctionalCurrencyId, row.FunctionalScale);
        var stored = JsonSerializer.Deserialize<List<StoredLine>>(row.LinesJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı iade satırları okunamadı.");
        var lines = stored.Select(line => FromStored(line, header)).ToList();
        var facts = new SalesCustomerReturnFacts(row.SubmittedBy, row.SubmittedAtUtc, row.ApprovedBy, row.ApprovedAtUtc, row.FinalizedBy,
            row.FinalizedAtUtc);
        var item = SalesCustomerReturn.Restore(row.Id, row.CompanyId, header, row.ShipmentId, row.CustomerDocumentNo,
            row.CustomerDocumentDate, (SalesCustomerReturnState)row.State, facts, row.CreatedBy, row.CreatedAtUtc, row.Version,
            row.UpdatedAtUtc, lines);

        // The stored totals are the list's copy of the lines' sums (SAT-D292); a row whose copy disagrees is corrupt.
        var totals = item.Totals;
        var functional = item.FunctionalTotals;
        if (row.LineCount != lines.Count || row.NeedsReview != item.NeedsReview
            || row.NetAmount != totals.Net.Amount || row.VatAmount != totals.Vat.Amount
            || row.WithholdingAmount != totals.Withholding.Amount || row.LineTotalAmount != totals.LineTotal.Amount
            || row.FunctionalNetAmount != functional?.Net.Amount || row.FunctionalVatAmount != functional?.Vat.Amount
            || row.FunctionalWithholdingAmount != functional?.Withholding.Amount
            || row.FunctionalLineTotalAmount != functional?.LineTotal.Amount
            || (item.State == SalesCustomerReturnState.Finalized) != (row.FinalizationSequence is not null))
        {
            throw new InvalidOperationException("Kayıtlı iade faturası toplamları satırlarını tutmuyor.");
        }

        return item;
    }

    /// <summary>Writes the approval axis onto the tracked row: the state, the facts, the version and the time only.</summary>
    public static void Apply(CustomerReturnRow row, SalesCustomerReturn item)
    {
        row.State = (int)item.State;
        row.SubmittedBy = item.Facts.SubmittedBy;
        row.SubmittedAtUtc = item.Facts.SubmittedAtUtc;
        row.ApprovedBy = item.Facts.ApprovedBy;
        row.ApprovedAtUtc = item.Facts.ApprovedAtUtc;
        row.FinalizedBy = item.Facts.FinalizedBy;
        row.FinalizedAtUtc = item.Facts.FinalizedAtUtc;
        row.Version = item.Version;
        row.UpdatedAtUtc = item.UpdatedAtUtc;
    }

    private static StoredLine ToStored(SalesCustomerReturnLine line) => new(line.LineNo, line.OriginalInvoiceLineId, line.OriginalLineNo,
        line.ShipmentId, line.ProductId, line.UnitId, line.Quantity, line.NetUnitPrice, line.VatPercent, line.WithholdingShare,
        line.IsLastReturn, ToStored(line.Amounts), line.Functional is { } functional ? ToStored(functional) : null, line.NeedsReview);

    private static StoredAmounts ToStored(SalesCustomerReturnAmounts amounts) =>
        new(amounts.Net.Amount, amounts.Vat.Amount, amounts.Withholding.Amount, amounts.LineTotal.Amount);

    private static SalesCustomerReturnLine FromStored(StoredLine line, SalesCustomerReturnHeader header)
    {
        if (line is null || line.Amounts is null)
        {
            throw new InvalidOperationException("Kayıtlı iade satırı okunamadı.");
        }

        SalesCustomerReturnAmounts? functional = null;
        if (line.Functional is { } tl)
        {
            if (header.FunctionalCurrencyId is not { } functionalCurrency || header.FunctionalScale is not { } functionalScale)
            {
                throw new InvalidOperationException("Kayıtlı iade satırı TL tutarı taşıyor ama iade TL değil.");
            }

            functional = FromStored(tl, functionalCurrency, functionalScale);
        }

        return new SalesCustomerReturnLine(line.LineNo, line.OriginalInvoiceLineId, line.OriginalLineNo, line.ShipmentId, line.ProductId,
            line.UnitId, line.Quantity, line.NetUnitPrice, line.VatPercent, line.WithholdingShare, line.IsLastReturn,
            FromStored(line.Amounts, header.CurrencyId, header.AmountScale), functional, line.NeedsReview);
    }

    private static SalesCustomerReturnAmounts FromStored(StoredAmounts amounts, Guid currencyId, int scale) => new(
        Money.Of(amounts.Net, currencyId, scale), Money.Of(amounts.Vat, currencyId, scale),
        Money.Of(amounts.Withholding, currencyId, scale), Money.Of(amounts.LineTotal, currencyId, scale));
}
