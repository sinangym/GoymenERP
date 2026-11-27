using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal static class FinanceCreditAccountProfilePolicy
{
    // Strict JSON enum serialization must not turn malformed input into an exception before validation.
    internal static bool DefinedEnums(CreditAccountProposeRequest request) =>
        (request.Profile is not { } profile || (Enum.IsDefined(profile.PlanNature)
            && (profile.Cash is null || Enum.IsDefined(profile.Cash.Kind))
            && (profile.NonCash is null || Enum.IsDefined(profile.NonCash.Kind))
            && (profile.InstrumentAndFactoring is null || Enum.IsDefined(profile.InstrumentAndFactoring.Kind))))
        && request.Installments.All(line => line?.Charges is null
            || line.Charges.All(charge => charge is null || Enum.IsDefined(charge.Treatment)));

    internal static IReadOnlyList<ValidationIssue> Validate(CreditAccountProposeRequest request)
    {
        var issues = new List<ValidationIssue>();
        var profile = request.Profile;
        void Invalid(string field) => issues.Add(new(field, "credit_source_profile_invalid"));
        if (profile is null) { Invalid("profile"); return issues; }
        if (profile.LenderPartyId == Guid.Empty || profile.LenderCompanyPartyAccountId == Guid.Empty) Invalid("profile.lender");
        if (!Document(profile.SourceDocument)) Invalid("profile.sourceDocument");
        if (!Enum.IsDefined(profile.PlanNature)) Invalid("profile.planNature");
        if (new object?[] { profile.Cash, profile.NonCash, profile.InstrumentAndFactoring, profile.Leasing }.Count(x => x is not null) != 1)
            Invalid("profile");
        switch (request.Group)
        {
            case CreditAccountGroup.Cash:
                if (profile.Cash is not { } cash || !Enum.IsDefined(cash.Kind)
                    || cash.MaturityDate is { } maturity && maturity < request.StartDate) Invalid("profile.cash");
                else
                {
                    // FNS-D124 / approved field list v1.2: the source kind determines whether
                    // an imported plan or actual source movements are required. No synthetic plan.
                    var movements = cash.Kind is CreditCashKindV1.Revolving or CreditCashKindV1.Overdraft
                        or CreditCashKindV1.CreditCard;
                    if (profile.PlanNature != (movements ? CreditPlanNatureV1.SourceMovements : CreditPlanNatureV1.Installments))
                        Invalid("profile.planNature");
                    if ((cash.Kind is CreditCashKindV1.Spot or CreditCashKindV1.Installment) && cash.MaturityDate is null)
                        Invalid("profile.cash.maturityDate");
                    if (cash.IndexCurrencyId == Guid.Empty
                        || cash.Kind == CreditCashKindV1.ForeignCurrencyIndexed && cash.IndexCurrencyId is null)
                        Invalid("profile.cash.indexCurrencyId");
                    if (cash.ExportCommitmentReference is { } export && !Text(export, 200))
                        Invalid("profile.cash.exportCommitmentReference");
                }
                break;
            case CreditAccountGroup.NonCash:
                if (profile.NonCash is not { } nonCash || !Enum.IsDefined(nonCash.Kind)
                    || !Text(nonCash.BeneficiaryReference, 200)
                    || nonCash.IsIndefinite != (nonCash.ExpiryDate is null)
                    || nonCash.ExpiryDate is { } expiry && expiry < request.StartDate
                    || profile.PlanNature != CreditPlanNatureV1.SourceMovements) Invalid("profile.nonCash");
                if (profile.NonCash is { } nonCashFields)
                {
                    if (!Amount(nonCashFields.RiskAmount)) Invalid("profile.nonCash.riskAmount");
                    if (nonCashFields.Commission is { } commission && (!Amount(commission.Amount)
                        || commission.CommissionDate == default || !Document(commission.SourceDocument)
                        || commission.Period is { } period && !Text(period, 200)))
                        Invalid("profile.nonCash.commission");
                    if (nonCashFields.RelatedCommercialDocumentReference is { } commercial && !Text(commercial, 200))
                        Invalid("profile.nonCash.relatedCommercialDocumentReference");
                    if (nonCashFields.ReturnEvidence is { } returned
                        && (returned.ReturnedOn == default || !Document(returned.SourceDocument)))
                        Invalid("profile.nonCash.returnEvidence");
                }
                break;
            case CreditAccountGroup.InstrumentAndFactoring:
                if (profile.InstrumentAndFactoring is not { } instrument || !Enum.IsDefined(instrument.Kind)
                    || instrument.InstrumentIds is null || instrument.ReceivableDocumentReferences is null
                    || instrument.InstrumentIds.Count > 600 || instrument.ReceivableDocumentReferences.Count > 600
                    || instrument.InstrumentIds.Any(x => x == Guid.Empty)
                    || instrument.InstrumentIds.Distinct().Count() != instrument.InstrumentIds.Count
                    || instrument.ReceivableDocumentReferences.Any(x => !Text(x, 100))
                    || (instrument.Kind == CreditInstrumentKindV1.Factoring
                        ? instrument.WithRecourse is null || instrument.ReceivableDocumentReferences.Count == 0
                        : instrument.InstrumentIds.Count == 0)) Invalid("profile.instrumentAndFactoring");
                if (profile.InstrumentAndFactoring is { } instrumentFields)
                {
                    if (instrumentFields.MaturityDate is not { } instrumentMaturity || instrumentMaturity < request.StartDate)
                        Invalid("profile.instrumentAndFactoring.maturityDate");
                    if (instrumentFields.Deductions is not { } deductions || !Amount(deductions.Interest)
                        || !Amount(deductions.Commission) || !Amount(deductions.Bsmv))
                        Invalid("profile.instrumentAndFactoring.deductions");
                }
                break;
            case CreditAccountGroup.Leasing:
                if (profile.Leasing is not { } leasing || !Text(leasing.AssetReference, 200)
                    || leasing.LeaseStartDate == default || leasing.LeaseEndDate < leasing.LeaseStartDate) Invalid("profile.leasing");
                if (profile.Leasing is { } leasingFields)
                {
                    if (leasingFields.ContractDate is null || leasingFields.ContractDate == default(DateOnly))
                        Invalid("profile.leasing.contractDate");
                    if (leasingFields.PurchaseOptionAmount is { } purchase && !Amount(purchase))
                        Invalid("profile.leasing.purchaseOptionAmount");
                    if (profile.PlanNature != CreditPlanNatureV1.Installments) Invalid("profile.planNature");
                }
                break;
            default: Invalid("group"); break;
        }
        if (profile.PlanNature == CreditPlanNatureV1.SourceMovements && request.Installments.Count != 0)
            Invalid("installments");
        // FNS-D127: source VAT replaces BSMV/KKDF for leasing; never infer VAT from a tax or a description.
        foreach (var line in request.Installments.Where(line => line is not null))
        {
            if (request.Group != CreditAccountGroup.Leasing && line.Vat is not null) Invalid("installments.vat");
            if (request.Group == CreditAccountGroup.Leasing
                && (!FinanceExactDecimalText.TryParseNonNegative(line.Bsmv, out var bsmv) || bsmv.Text.Any(c => c is >= '1' and <= '9')
                    || !FinanceExactDecimalText.TryParseNonNegative(line.Kkdf, out var kkdf) || kkdf.Text.Any(c => c is >= '1' and <= '9')))
                Invalid("installments.leasingTax");
        }
        if (request.StartDate == default) Invalid("startDate");
        return issues;
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value == value.Trim() && value.Length <= maximum;

    private static bool Document(CreditSourceDocumentV1? value) => value is not null && Text(value.Reference, 100)
        && Text(value.Revision, 100) && value.DocumentDate != default;

    private static bool Amount(string? value) => value is { Length: <= 128 }
        && FinanceExactDecimalText.TryParseNonNegative(value, out _);
}
