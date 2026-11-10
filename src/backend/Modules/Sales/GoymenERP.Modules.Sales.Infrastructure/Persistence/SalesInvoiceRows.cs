using System.Text.Json;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>API-32: the invoice header (<c>sales.Invoices</c>). Amounts live on the lines; the instalments are a JSON array.</summary>
internal sealed class InvoiceRow : ISalesTenantRow
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

    /// <summary>SAT-D172: the rate written on the invoice; null exactly for a TL invoice (with the four columns below).</summary>
    public decimal? ExchangeRate { get; set; }
    public DateOnly? RateDay { get; set; }
    public DateOnly? RateBulletinDate { get; set; }
    public Guid? FunctionalCurrencyId { get; set; }
    public int? FunctionalScale { get; set; }
    public string InstalmentsJson { get; set; } = "[]";
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>API-33: who submitted the draft and when (null while a draft).</summary>
    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }

    /// <summary>API-33 (SAT-D88/D204): the approver or rejecter — never the preparer or the submitter (CK_Invoices_MakerChecker).</summary>
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }

    /// <summary>API-33: who cancelled the invoice before its approval, when and why.</summary>
    public string? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
}

/// <summary>
/// API-32: one invoice line = one whole shipment share (<c>sales.InvoiceLines</c>). <see cref="ReleasedAtUtc"/> is null while the
/// line holds its shipment (UX_InvoiceLines_ActiveShipment, AT-055 / SAT-D94); a later cancellation releases it, never deletes.
/// </summary>
internal sealed class InvoiceLineRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid DispatchNoteId { get; set; }
    public string DispatchNoteNumber { get; set; } = string.Empty;
    public int NoteLineNo { get; set; }
    public DateOnly ShipmentDate { get; set; }
    public Guid SalesOrderId { get; set; }
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
    /// <summary>SAT-D76: the exact, never rounded net unit price as invariant text (no SQL scale may cut it).</summary>
    public string NetUnitPrice { get; set; } = string.Empty;
    public decimal VatPercent { get; set; }
    public decimal? WithholdingShare { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal WithholdingAmount { get; set; }
    public decimal? FunctionalNetAmount { get; set; }
    public decimal? FunctionalVatAmount { get; set; }
    public decimal? FunctionalWithholdingAmount { get; set; }

    /// <summary>EK42: the amount types that took the group difference (comma separated names), null when none.</summary>
    public string? FunctionalAdjusted { get; set; }
    public bool FunctionalNeedsReview { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
}

/// <summary>The invoice commands' own attempt table (ADR-011 §2, ADR-SAL-001 §2): the request attempt's shape, append-only.</summary>
internal sealed class InvoiceAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = a stored invoice (<see cref="InvoiceId"/> set); 2 = a recorded terminal refusal (<see cref="RefusalJson"/> set).</summary>
    public int Outcome { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>API-35: the append-only official match of an approved invoice (SAT-D89); one row per invoice, never updated or deleted.</summary>
internal sealed class InvoiceMatchRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public string OfficialInvoiceNo { get; set; } = string.Empty;
    public string Ettn { get; set; } = string.Empty;
    public decimal InvoiceAmount { get; set; }
    public decimal OfficialAmount { get; set; }
    public decimal Difference { get; set; }
    public decimal Tolerance { get; set; }
    public string MatchedBy { get; set; } = string.Empty;
    public DateTimeOffset MatchedAtUtc { get; set; }
    public string? DifferenceApprovedBy { get; set; }
    public string? DifferenceReason { get; set; }

    // SAT-D241 (EK48): the separate VAT and withholding comparison — all six set on a match recorded since, all six null before.
    public decimal? InvoiceVatAmount { get; set; }
    public decimal? OfficialVatAmount { get; set; }
    public decimal? VatDifference { get; set; }
    public decimal? InvoiceWithholdingAmount { get; set; }
    public decimal? OfficialWithholdingAmount { get; set; }
    public decimal? WithholdingDifference { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// API-36: the finalisation and the Finance hand-off ledger of a finalised invoice (<c>sales.InvoiceFinalizations</c>, one row per
/// invoice). The facts are immutable; only the ledger columns move (Waiting → Admitted, the attempt counters), by the guard trigger's rules.
/// </summary>
internal sealed class InvoiceFinalizationRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid AdmissionId { get; set; }
    public long FinalizedVersion { get; set; }
    public string FinalizedBy { get; set; } = string.Empty;
    public DateTimeOffset FinalizedAtUtc { get; set; }

    /// <summary>1 = Waiting ("Finans kabulü bekliyor"), 2 = Admitted.</summary>
    public int FinanceStatus { get; set; }
    public DateTimeOffset? AdmittedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public string? LastOutcome { get; set; }
    public string PlanJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>API-36: the transactional outbox of the Sales invoice events (append-only; consumers dedup by <see cref="Id"/>).</summary>
internal sealed class SalesOutboxRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string SourceOperation { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public long AggregateVersion { get; set; }
    public int Sequence { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>The one mapping between a domain invoice and its rows (write) and back (read; the domain checks consistency).</summary>
internal static class SalesInvoiceRows
{
    /// <summary>The decimal(28, 8) amount columns hold at most 8 decimals; a currency profile with more is refused, never cut.</summary>
    internal const int MaximumStoredScale = 8;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static void Add(SalesDbContext dbContext, SalesInvoice invoice, string correlationId)
    {
        var header = invoice.Header;
        if (header.AmountScale > MaximumStoredScale || header.Rate is { FunctionalScale: > MaximumStoredScale }
            || header.Rate is { } rate && Money.SignificantDecimals(rate.Rate) > 10
            || invoice.Lines.Any(line => Money.SignificantDecimals(line.VatPercent) > 4
                || line.WithholdingShare is { } share && Money.SignificantDecimals(share) > 10))
        {
            throw new SalesDraftStoreException(SalesDraftStoreCodes.ValueDoesNotFit, "Fatura tutar ölçeği saklanan sütuna sığmıyor.");
        }

        dbContext.Invoices.Add(new InvoiceRow
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
            ExchangeRate = header.Rate?.Rate,
            RateDay = header.Rate?.RateDay,
            RateBulletinDate = header.Rate?.BulletinDate,
            FunctionalCurrencyId = header.Rate?.FunctionalCurrencyId,
            FunctionalScale = header.Rate?.FunctionalScale,
            InstalmentsJson = JsonSerializer.Serialize(invoice.Instalments.Select(item =>
                new StoredInstalment(item.SequenceNo, item.DueDate, item.SharePercent, item.Amount.Amount)), Json),
            CreatedBy = invoice.CreatedBy,
            CreatedAtUtc = invoice.CreatedAtUtc,
            CorrelationId = correlationId.Trim(),
            SubmittedBy = invoice.Decision.SubmittedBy,
            SubmittedAtUtc = invoice.Decision.SubmittedAtUtc,
            DecidedBy = invoice.Decision.DecidedBy,
            DecidedAtUtc = invoice.Decision.DecidedAtUtc,
            DecisionReason = invoice.Decision.DecisionReason,
            CancelledBy = invoice.Decision.CancelledBy,
            CancelledAtUtc = invoice.Decision.CancelledAtUtc,
            CancellationReason = invoice.Decision.CancellationReason,
        });
        foreach (var line in invoice.Lines)
        {
            var share = line.Share;
            dbContext.InvoiceLines.Add(new InvoiceLineRow
            {
                CompanyId = invoice.CompanyId,
                InvoiceId = invoice.Id,
                LineNo = line.LineNo,
                LineId = line.Id,
                ShipmentId = share.ShipmentId,
                DispatchNoteId = share.DispatchNoteId,
                DispatchNoteNumber = share.DispatchNoteNumber,
                NoteLineNo = share.NoteLineNo,
                ShipmentDate = share.ShipmentDate,
                SalesOrderId = share.SalesOrderId,
                SalesOrderLineId = share.SalesOrderLineId,
                PriceBasisRevision = share.PriceBasisRevision,
                ProductId = share.ProductId,
                UnitId = share.UnitId,
                Quantity = share.Quantity,
                QuantityText = share.QuantityText,
                UnitPrice = share.Commercial.UnitPrice,
                PriceUnitId = share.Commercial.PriceUnitId,
                DiscountPercent = share.Commercial.DiscountPercent,
                PricingJson = SalesLinePricingJson.Serialize(share.Commercial),
                TaxJson = SalesTaxJson.SerializeLine(share.Tax)!,
                NetUnitPrice = SalesLinePriceCalculator.Format(line.NetUnitPrice),
                VatPercent = line.VatPercent,
                WithholdingShare = line.WithholdingShare,
                NetAmount = line.Amounts.Net.Amount,
                VatAmount = line.Amounts.Vat.Amount,
                WithholdingAmount = line.Amounts.Withholding.Amount,
                FunctionalNetAmount = line.Functional?.Amounts.Net.Amount,
                FunctionalVatAmount = line.Functional?.Amounts.Vat.Amount,
                FunctionalWithholdingAmount = line.Functional?.Amounts.Withholding.Amount,
                FunctionalAdjusted = line.Functional is { Adjusted.Count: > 0 } functional
                    ? string.Join(',', functional.Adjusted.Select(type => type.ToString()))
                    : null,
                FunctionalNeedsReview = line.Functional?.NeedsReview ?? false,
            });
        }
    }

    /// <summary>
    /// Writes a decision's facts onto the tracked header row (the version is the concurrency token of this write) and, when the
    /// invoice no longer holds its shares (rejected or cancelled), stamps every tracked line's <c>ReleasedAtUtc</c> in the same
    /// save (AT-055, SAT-D94; the lines are never deleted). The header is tracked first, so its state is written before the lines.
    /// </summary>
    public static void ApplyDecision(InvoiceRow row, SalesInvoice invoice)
    {
        row.State = (int)invoice.State;
        row.Version = invoice.Version;
        row.SubmittedBy = invoice.Decision.SubmittedBy;
        row.SubmittedAtUtc = invoice.Decision.SubmittedAtUtc;
        row.DecidedBy = invoice.Decision.DecidedBy;
        row.DecidedAtUtc = invoice.Decision.DecidedAtUtc;
        row.DecisionReason = invoice.Decision.DecisionReason;
        row.CancelledBy = invoice.Decision.CancelledBy;
        row.CancelledAtUtc = invoice.Decision.CancelledAtUtc;
        row.CancellationReason = invoice.Decision.CancellationReason;
        // The shares of a rejected or cancelled invoice are released by TR_Invoices_Guard in the same UPDATE statement (EF would write
        // the lines before the header, and the line trigger reads the header's new state), so the lines are not written here.
    }

    public static InvoiceMatchRow ToMatchRow(Guid companyId, Guid invoiceId, SalesInvoiceMatchFacts facts, string correlationId)
    {
        if (Money.SignificantDecimals(facts.InvoiceFunctionalAmount) > MaximumStoredScale
            || Money.SignificantDecimals(facts.OfficialFunctionalAmount) > MaximumStoredScale
            || Money.SignificantDecimals(facts.Difference) > MaximumStoredScale
            || Money.SignificantDecimals(facts.Tolerance) > MaximumStoredScale
            || (facts.Taxes is { } taxes && new[]
            {
                taxes.InvoiceVatAmount, taxes.OfficialVatAmount, taxes.VatDifference, taxes.InvoiceWithholdingAmount,
                taxes.OfficialWithholdingAmount, taxes.WithholdingDifference,
            }.Any(amount => Money.SignificantDecimals(amount) > MaximumStoredScale)))
        {
            throw new SalesDraftStoreException(SalesDraftStoreCodes.ValueDoesNotFit, "Eşleşme tutar ölçeği saklanan sütuna sığmıyor.");
        }

        return new InvoiceMatchRow
        {
            CompanyId = companyId,
            InvoiceId = invoiceId,
            OfficialInvoiceNo = facts.OfficialNo,
            Ettn = facts.Ettn,
            InvoiceAmount = facts.InvoiceFunctionalAmount,
            OfficialAmount = facts.OfficialFunctionalAmount,
            Difference = facts.Difference,
            Tolerance = facts.Tolerance,
            MatchedBy = facts.MatchedBy,
            MatchedAtUtc = facts.MatchedAtUtc,
            DifferenceApprovedBy = facts.DifferenceApprovedBy,
            DifferenceReason = facts.DifferenceReason,
            InvoiceVatAmount = facts.Taxes?.InvoiceVatAmount,
            OfficialVatAmount = facts.Taxes?.OfficialVatAmount,
            VatDifference = facts.Taxes?.VatDifference,
            InvoiceWithholdingAmount = facts.Taxes?.InvoiceWithholdingAmount,
            OfficialWithholdingAmount = facts.Taxes?.OfficialWithholdingAmount,
            WithholdingDifference = facts.Taxes?.WithholdingDifference,
            CorrelationId = correlationId.Trim(),
        };
    }

    public static SalesInvoiceMatchFacts ToMatchFacts(InvoiceMatchRow row) => new SalesInvoiceMatchFacts(row.OfficialInvoiceNo, row.Ettn, row.InvoiceAmount,
        row.OfficialAmount, row.Difference, row.Tolerance, row.MatchedBy, row.MatchedAtUtc, row.DifferenceApprovedBy, row.DifferenceReason,
        ToTaxMatchFacts(row));

    private static SalesInvoiceTaxMatchFacts? ToTaxMatchFacts(InvoiceMatchRow row) =>
        row is { InvoiceVatAmount: { } invoiceVat, OfficialVatAmount: { } officialVat, VatDifference: { } vatDifference,
            InvoiceWithholdingAmount: { } invoiceWithholding, OfficialWithholdingAmount: { } officialWithholding,
            WithholdingDifference: { } withholdingDifference }
            ? new SalesInvoiceTaxMatchFacts(invoiceVat, officialVat, vatDifference, invoiceWithholding, officialWithholding,
                withholdingDifference)
            : row is { InvoiceVatAmount: null, OfficialVatAmount: null, VatDifference: null, InvoiceWithholdingAmount: null,
                OfficialWithholdingAmount: null, WithholdingDifference: null }
                ? null
                : throw new InvalidOperationException("Kayıtlı resmî fatura eşleşmesi okunamadı.");

    public static InvoiceFinalizationRow ToFinalizationRow(Guid companyId, Guid invoiceId, SalesInvoiceFinalization facts, string correlationId)
    {
        if (facts.Plan.Any(item => Money.SignificantDecimals(item.Amount) > MaximumStoredScale
            || item.FunctionalAmount is { } functional && Money.SignificantDecimals(functional) > MaximumStoredScale))
        {
            throw new SalesDraftStoreException(SalesDraftStoreCodes.ValueDoesNotFit, "Taksit tutar ölçeği saklanan sütuna sığmıyor.");
        }

        return new InvoiceFinalizationRow
        {
            CompanyId = companyId,
            InvoiceId = invoiceId,
            AdmissionId = facts.AdmissionId,
            FinalizedVersion = facts.FinalizedVersion,
            FinalizedBy = facts.FinalizedBy,
            FinalizedAtUtc = facts.FinalizedAtUtc,
            FinanceStatus = (int)facts.FinanceStatus,
            AdmittedAtUtc = facts.AdmittedAtUtc,
            AttemptCount = facts.AttemptCount,
            LastAttemptAtUtc = facts.LastAttemptAtUtc,
            LastOutcome = facts.LastOutcome,
            PlanJson = JsonSerializer.Serialize(facts.Plan.Select(item => new StoredPlanItem(item.OpenItemId, item.InstallmentId, item.SequenceNo,
                item.DueDate, item.Amount, item.FunctionalAmount)), Json),
            CorrelationId = correlationId.Trim(),
        };
    }

    public static void ApplyLedger(InvoiceFinalizationRow row, SalesInvoiceFinalization facts)
    {
        row.FinanceStatus = (int)facts.FinanceStatus;
        row.AdmittedAtUtc = facts.AdmittedAtUtc;
        row.AttemptCount = facts.AttemptCount;
        row.LastAttemptAtUtc = facts.LastAttemptAtUtc;
        row.LastOutcome = facts.LastOutcome;
    }

    public static SalesInvoiceFinalization ToFinalizationFacts(InvoiceFinalizationRow row) => new SalesInvoiceFinalization(row.AdmissionId, row.FinalizedVersion,
        row.FinalizedBy, row.FinalizedAtUtc, (SalesInvoiceFinanceStatus)row.FinanceStatus, row.AdmittedAtUtc, row.AttemptCount,
        row.LastAttemptAtUtc, row.LastOutcome,
        [.. (JsonSerializer.Deserialize<List<StoredPlanItem>>(row.PlanJson, Json)
            ?? throw new InvalidOperationException("Fatura taksit planı okunamadı."))
            .Select(item => new SalesInvoiceFinalInstalment(item.OpenItemId, item.InstallmentId, item.SequenceNo, item.DueDate, item.Amount,
                item.FunctionalAmount))]);

    public static SalesInvoice Restore(InvoiceRow row, IReadOnlyList<InvoiceLineRow> lineRows)
    {
        var state = (SalesInvoiceState)row.State;
        if (Enum.IsDefined(state) && lineRows.Any(line => line.ReleasedAtUtc is null) != (state is SalesInvoiceState.Draft
                or SalesInvoiceState.PendingApproval or SalesInvoiceState.Approved or SalesInvoiceState.Finalized))
        {
            throw new InvalidOperationException("Kayıtlı fatura durumu ile sevk paylarının serbest bırakılması birbirini tutmuyor.");
        }

        var rate = row.ExchangeRate is { } value
            ? new SalesInvoiceRateSnapshot(value, row.RateDay!.Value, row.RateBulletinDate!.Value, row.FunctionalCurrencyId!.Value,
                row.FunctionalScale!.Value)
            : null;
        var header = new SalesInvoiceHeader(row.PartyId, row.CompanyAccountId, row.CurrencyId, row.MoneyProfileId, row.MoneyProfileVersion,
            row.AmountScale, row.BusinessDate, rate);
        var lines = lineRows.OrderBy(line => line.LineNo).Select(line =>
        {
            var share = new SalesInvoiceShareInput(line.LineId, line.ShipmentId, line.DispatchNoteId, line.DispatchNoteNumber,
                line.NoteLineNo, line.ShipmentDate, line.SalesOrderId, line.SalesOrderLineId, line.PriceBasisRevision, line.ProductId,
                line.UnitId, line.Quantity, line.QuantityText,
                SalesLinePricingJson.Restore(line.UnitPrice, line.PriceUnitId, line.DiscountPercent, line.PricingJson),
                SalesTaxJson.RestoreLine(line.TaxJson) ?? throw new InvalidOperationException("Fatura satırının vergi kaydı okunamadı."));
            var amounts = SalesInvoiceCalculator.Set(Money.Of(line.NetAmount, row.CurrencyId, row.AmountScale),
                Money.Of(line.VatAmount, row.CurrencyId, row.AmountScale), Money.Of(line.WithholdingAmount, row.CurrencyId, row.AmountScale));
            SalesInvoiceFunctionalLine? functional = null;
            if (rate is not null)
            {
                if (line.FunctionalNetAmount is not { } net || line.FunctionalVatAmount is not { } vat
                    || line.FunctionalWithholdingAmount is not { } withholding)
                {
                    throw new InvalidOperationException("Dövizli fatura satırının TL tutarları eksik.");
                }

                functional = new SalesInvoiceFunctionalLine(
                    SalesInvoiceCalculator.Set(Money.Of(net, rate.FunctionalCurrencyId, rate.FunctionalScale),
                        Money.Of(vat, rate.FunctionalCurrencyId, rate.FunctionalScale),
                        Money.Of(withholding, rate.FunctionalCurrencyId, rate.FunctionalScale)),
                    line.FunctionalAdjusted is null
                        ? []
                        : [.. line.FunctionalAdjusted.Split(',').Select(name => Enum.Parse<SalesInvoiceAmountType>(name, ignoreCase: false))],
                    line.FunctionalNeedsReview);
            }

            return SalesInvoice.StoredLine(line.LineNo, share, SalesLinePriceCalculator.Parse(line.NetUnitPrice), line.VatPercent, line.WithholdingShare, amounts, functional);
        }).ToList();
        var instalments = (JsonSerializer.Deserialize<List<StoredInstalment>>(row.InstalmentsJson, Json)
                ?? throw new InvalidOperationException("Fatura taksitleri okunamadı."))
            .Select(item => new SalesInvoiceInstalment(item.SequenceNo, item.DueDate, item.SharePercent,
                Money.Of(item.Amount, row.CurrencyId, row.AmountScale)))
            .ToList();
        return SalesInvoice.Restore(row.Id, row.CompanyId, row.DocumentNo, header, (SalesInvoiceState)row.State, row.Version, row.CreatedBy,
            row.CreatedAtUtc, lines, instalments,
            new SalesInvoiceDecisionFacts(row.SubmittedBy, row.SubmittedAtUtc, row.DecidedBy, row.DecidedAtUtc, row.DecisionReason,
                row.CancelledBy, row.CancelledAtUtc, row.CancellationReason));
    }

    private sealed record StoredPlanItem(Guid OpenItemId, Guid InstallmentId, int SequenceNo, DateOnly DueDate, decimal Amount,
        decimal? FunctionalAmount);

    private sealed record StoredInstalment(int SequenceNo, DateOnly DueDate, string SharePercent, decimal Amount);
}
