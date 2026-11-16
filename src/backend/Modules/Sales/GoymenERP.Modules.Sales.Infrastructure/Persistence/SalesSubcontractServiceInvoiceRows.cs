using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// F2: the subcontract service invoice draft header (<c>sales.SubcontractServiceInvoices</c>). The F2 key (order + loading
/// instruction + weighing) is unique per company (UX_SubcontractServiceInvoices_Weighing); amounts live on the lines.
/// </summary>
internal sealed class SubcontractServiceInvoiceRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public int State { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateOnly BusinessDate { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public Guid MoneyProfileId { get; set; }
    public string MoneyProfileVersion { get; set; } = string.Empty;
    public int AmountScale { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid LoadingInstructionId { get; set; }
    public Guid WeighingId { get; set; }
    public int WeighingRevision { get; set; }
    public long InstructionVersion { get; set; }

    /// <summary>LOJ-D9: the Logistics owner's delivery weighing net kg, verbatim invariant text (the billed quantity).</summary>
    public string NetKgText { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>F2: one service line (<c>sales.SubcontractServiceInvoiceLines</c>, LOJ-D4) with the calculator's frozen amounts.</summary>
internal sealed class SubcontractServiceInvoiceLineRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public int PriceBasisRevision { get; set; }
    public Guid ProductId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; }
    public string QuantityText { get; set; } = string.Empty;
    public string? UnitPrice { get; set; }
    public Guid? PriceUnitId { get; set; }
    public string? DiscountPercent { get; set; }
    public string? PricingJson { get; set; }
    public string TaxJson { get; set; } = string.Empty;

    /// <summary>SAT-D76: the exact, never rounded net unit price as invariant text.</summary>
    public string NetUnitPrice { get; set; } = string.Empty;
    public decimal VatPercent { get; set; }
    public decimal? WithholdingShare { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal WithholdingAmount { get; set; }
}

/// <summary>The subcontract service invoice command's own attempt table (ADR-011 §2): append-only, one row per key.</summary>
internal sealed class SubcontractServiceInvoiceAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>The one mapping between a subcontract service invoice and its rows (write) and back (read; the domain checks consistency).</summary>
internal static class SalesSubcontractServiceInvoiceRows
{
    /// <summary>The quantity column holds the Logistics weighing's own kg scale (decimal(18, 3)); more is refused, never cut.</summary>
    internal const int QuantityStoredScale = 3;

    public static void Add(SalesDbContext dbContext, SalesSubcontractServiceInvoice invoice, string correlationId)
    {
        var header = invoice.Header;
        if (header.AmountScale > SalesInvoiceRows.MaximumStoredScale
            || invoice.Lines.Any(line => Money.SignificantDecimals(line.VatPercent) > 4
                || line.WithholdingShare is { } share && Money.SignificantDecimals(share) > 10
                || Money.SignificantDecimals(line.Input.Quantity) > QuantityStoredScale))
        {
            throw new SalesDraftStoreException(SalesDraftStoreCodes.ValueDoesNotFit,
                "Fason hizmet faturası tutar ölçeği saklanan sütuna sığmıyor.");
        }

        dbContext.SubcontractServiceInvoices.Add(new SubcontractServiceInvoiceRow
        {
            Id = invoice.Id,
            CompanyId = invoice.CompanyId,
            DocumentNo = invoice.DocumentNo,
            State = (int)invoice.State,
            Version = invoice.Version,
            BusinessDate = header.BusinessDate,
            PartyId = header.PartyId,
            CompanyAccountId = header.CompanyAccountId,
            CurrencyId = header.CurrencyId,
            MoneyProfileId = header.MoneyProfileId,
            MoneyProfileVersion = header.MoneyProfileVersion,
            AmountScale = header.AmountScale,
            SalesOrderId = invoice.Source.SalesOrderId,
            LoadingInstructionId = invoice.Source.LoadingInstructionId,
            WeighingId = invoice.Source.WeighingId,
            WeighingRevision = invoice.Source.WeighingRevision,
            InstructionVersion = invoice.Source.InstructionVersion,
            NetKgText = invoice.Source.NetKgText,
            CreatedBy = invoice.CreatedBy,
            CreatedAtUtc = invoice.CreatedAtUtc,
            CorrelationId = correlationId.Trim(),
        });
        foreach (var line in invoice.Lines)
        {
            var input = line.Input;
            dbContext.SubcontractServiceInvoiceLines.Add(new SubcontractServiceInvoiceLineRow
            {
                CompanyId = invoice.CompanyId,
                InvoiceId = invoice.Id,
                LineNo = line.LineNo,
                LineId = line.Id,
                SalesOrderLineId = input.SalesOrderLineId,
                PriceBasisRevision = input.PriceBasisRevision,
                ProductId = input.ProductId,
                UnitId = input.UnitId,
                Quantity = input.Quantity,
                QuantityText = input.QuantityText,
                UnitPrice = input.Commercial.UnitPrice,
                PriceUnitId = input.Commercial.PriceUnitId,
                DiscountPercent = input.Commercial.DiscountPercent,
                PricingJson = SalesLinePricingJson.Serialize(input.Commercial),
                TaxJson = SalesTaxJson.SerializeLine(input.Tax)!,
                NetUnitPrice = SalesLinePriceCalculator.Format(line.NetUnitPrice),
                VatPercent = line.VatPercent,
                WithholdingShare = line.WithholdingShare,
                NetAmount = line.Amounts.Net.Amount,
                VatAmount = line.Amounts.Vat.Amount,
                WithholdingAmount = line.Amounts.Withholding.Amount,
            });
        }
    }

    public static SalesSubcontractServiceInvoice Restore(SubcontractServiceInvoiceRow row, IReadOnlyList<SubcontractServiceInvoiceLineRow> lineRows)
    {
        var header = new SalesSubcontractServiceInvoiceHeader(row.PartyId, row.CompanyAccountId, row.CurrencyId, row.MoneyProfileId,
            row.MoneyProfileVersion, row.AmountScale, row.BusinessDate);
        var source = new SalesSubcontractServiceInvoiceSource(row.SalesOrderId, row.LoadingInstructionId, row.WeighingId,
            row.WeighingRevision, row.InstructionVersion, row.NetKgText);
        var lines = lineRows.OrderBy(line => line.LineNo).Select(line =>
        {
            var input = new SalesSubcontractServiceLineInput(line.LineId, line.SalesOrderLineId, line.PriceBasisRevision, line.ProductId,
                line.UnitId, line.Quantity, line.QuantityText,
                SalesLinePricingJson.Restore(line.UnitPrice, line.PriceUnitId, line.DiscountPercent, line.PricingJson),
                SalesTaxJson.RestoreLine(line.TaxJson)
                    ?? throw new InvalidOperationException("Fason hizmet faturası satırının vergi kaydı okunamadı."));
            var amounts = SalesInvoiceCalculator.Set(Money.Of(line.NetAmount, row.CurrencyId, row.AmountScale),
                Money.Of(line.VatAmount, row.CurrencyId, row.AmountScale), Money.Of(line.WithholdingAmount, row.CurrencyId, row.AmountScale));
            return SalesSubcontractServiceInvoice.StoredLine(line.LineNo, input, SalesLinePriceCalculator.Parse(line.NetUnitPrice),
                line.VatPercent, line.WithholdingShare, amounts);
        }).ToList();
        return SalesSubcontractServiceInvoice.Restore(row.Id, row.CompanyId, row.DocumentNo, header, source,
            (SalesSubcontractServiceInvoiceState)row.State, row.Version, row.CreatedBy, row.CreatedAtUtc, lines);
    }
}
