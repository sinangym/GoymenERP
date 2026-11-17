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
        if (profile.SourceDocument is not { } source || !Text(source.Reference, 100) || !Text(source.Revision, 100)
            || source.DocumentDate == default) Invalid("profile.sourceDocument");
        if (!Enum.IsDefined(profile.PlanNature)) Invalid("profile.planNature");
        if (new object?[] { profile.Cash, profile.NonCash, profile.InstrumentAndFactoring, profile.Leasing }.Count(x => x is not null) != 1)
            Invalid("profile");
        switch (request.Group)
        {
            case CreditAccountGroup.Cash:
                if (profile.Cash is not { } cash || !Enum.IsDefined(cash.Kind)
                    || cash.MaturityDate is { } maturity && maturity < request.StartDate) Invalid("profile.cash");
                break;
            case CreditAccountGroup.NonCash:
                if (profile.NonCash is not { } nonCash || !Enum.IsDefined(nonCash.Kind)
                    || !Text(nonCash.BeneficiaryReference, 200)
                    || nonCash.IsIndefinite != (nonCash.ExpiryDate is null)
                    || nonCash.ExpiryDate is { } expiry && expiry < request.StartDate
                    || profile.PlanNature != CreditPlanNatureV1.SourceMovements) Invalid("profile.nonCash");
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
                break;
            case CreditAccountGroup.Leasing:
                if (profile.Leasing is not { } leasing || !Text(leasing.AssetReference, 200)
                    || leasing.LeaseStartDate == default || leasing.LeaseEndDate < leasing.LeaseStartDate) Invalid("profile.leasing");
                break;
            default: Invalid("group"); break;
        }
        if (profile.PlanNature == CreditPlanNatureV1.SourceMovements && request.Installments.Count != 0)
            Invalid("installments");
        if (request.StartDate == default) Invalid("startDate");
        return issues;
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value)
        && value == value.Trim() && value.Length <= maximum;
}
