namespace GoymenERP.Modules.Purchasing.Domain.Invoices;

/// <summary>Versioned persistence snapshot. Restore replays the same domain transitions; invalid history cannot become a ready invoice.</summary>
public sealed record PurchaseIncomingInvoiceSnapshot(int SchemaVersion, Guid Id, Guid CompanyId, Guid SupplierPartyId,
    Guid CompanyAccountId, Guid CurrencyId, Guid FunctionalCurrencyId, int MoneyScale, int FunctionalScale,
    decimal? OfficialRate, PurchaseInvoiceSource Source, PurchaseInvoiceAmounts Amounts,
    PurchaseInvoiceAmounts? FunctionalAmounts, PurchaseInvoiceLine[] Lines, PurchaseInvoiceInstallment[] Installments,
    string RecordedBy, DateTimeOffset RecordedAtUtc, long Version, PurchaseIncomingInvoiceStatus Status,
    PurchaseInvoiceReceiptMatch[] Matches, PurchaseInvoiceVariancePolicy? Policy, string? MatchedBy,
    PurchaseInvoiceVarianceDecision? VarianceDecision, Guid? AdmissionId, long? FinalizedVersion,
    string? FinalizedBy, DateTimeOffset? FinalizedAtUtc, DateTimeOffset? FinanceAcceptedAtUtc,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    PurchaseInvoiceAdditionalCostMatch[]? AdditionalCosts = null)
{
    public static PurchaseIncomingInvoiceSnapshot Capture(PurchaseIncomingInvoice invoice) => new(1, invoice.Id,
        invoice.CompanyId, invoice.SupplierPartyId, invoice.CompanyAccountId, invoice.CurrencyId, invoice.FunctionalCurrencyId,
        invoice.MoneyScale, invoice.FunctionalScale, invoice.OfficialRate, invoice.Source, invoice.Amounts, invoice.FunctionalAmounts,
        invoice.Lines.ToArray(), invoice.Installments.ToArray(), invoice.RecordedBy, invoice.RecordedAtUtc, invoice.Version, invoice.Status,
        invoice.Matches.ToArray(), invoice.Policy, invoice.MatchedBy, invoice.VarianceDecision, invoice.AdmissionId,
        invoice.FinalizedVersion, invoice.FinalizedBy, invoice.FinalizedAtUtc, invoice.FinanceAcceptedAtUtc,
        invoice.AdditionalCosts.Count == 0 ? null : invoice.AdditionalCosts.ToArray());

    public PurchaseIncomingInvoice Restore()
    {
        if (SchemaVersion != 1 || !Enum.IsDefined(Status) || Matches is null) throw Invalid();
        var invoice = PurchaseIncomingInvoice.Record(Id, CompanyId, SupplierPartyId, CompanyAccountId, CurrencyId,
            FunctionalCurrencyId, MoneyScale, FunctionalScale, OfficialRate, Source, Amounts, FunctionalAmounts,
            Lines, Installments, RecordedBy, RecordedAtUtc);
        if (Status >= PurchaseIncomingInvoiceStatus.Matched)
        {
            if (MatchedBy is null) throw Invalid();
            invoice.Match(invoice.Version, Matches, Policy, MatchedBy, AdditionalCosts);
            if (VarianceDecision is { } decision)
                invoice.ApproveVariance(invoice.Version, decision.ApprovedBy, decision.Reason, decision.ApprovedAtUtc);
        }
        else if (Matches.Length != 0 || AdditionalCosts is { Length: > 0 } || Policy is not null || MatchedBy is not null || VarianceDecision is not null) throw Invalid();
        if (Status >= PurchaseIncomingInvoiceStatus.FinancePending)
        {
            if (AdmissionId is not { } admission || FinalizedBy is null || FinalizedAtUtc is not { } finalizedAt) throw Invalid();
            invoice.Finalize(invoice.Version, admission, FinalizedBy, finalizedAt);
        }
        else if (AdmissionId is not null || FinalizedVersion is not null || FinalizedBy is not null || FinalizedAtUtc is not null) throw Invalid();
        if (Status == PurchaseIncomingInvoiceStatus.FinanceAccepted)
        {
            if (FinanceAcceptedAtUtc is not { } acceptedAt) throw Invalid();
            invoice.AcknowledgeFinance(invoice.Version, AdmissionId!.Value, acceptedAt);
        }
        else if (FinanceAcceptedAtUtc is not null) throw Invalid();
        if (invoice.Version != Version || invoice.FinalizedVersion != FinalizedVersion || invoice.Status != Status) throw Invalid();
        return invoice;
    }

    private static InvalidOperationException Invalid() => new("Alış faturası geçmişi ve sürümü uyuşmuyor.");
}
