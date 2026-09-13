using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Contracts;

// Consumer adapter only. Operation is supplied by the centrally approved mapping.
// The provider is scoped by the authenticated host. Exceptions preserve owner codes.
public sealed class SettingsReceiptPrerequisites(ISettingsPrerequisiteProvider provider)
{
    public async Task<PeriodEligibilityV1> RequirePeriodAsync(FinanceContext context, string operation,
        DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default)
    {
        var period = await provider.GetPeriodEligibilityAsync(operation, occurredOn, valueOn, cancellationToken);
        if (period.CompanyId != context.CompanyId || period.Operation != operation ||
            period.OccurredOn != occurredOn || period.ValueOn != valueOn)
            throw new SettingsPrerequisiteException("finance_period_scope_mismatch");
        if (!period.IsEligible)
            throw new SettingsPrerequisiteException(period.DenialCode ?? "finance_period_ineligible");
        return period;
    }

    public async Task<IReadOnlyList<ValidationIssue>> ValidatePreparationAsync(FinanceContext context,
        string operation, ReceiptPreparation preparation, BankAccountView? bank,
        PartyAccountEligibility? party, CancellationToken cancellationToken = default)
    {
        var profile = await provider.GetMoneyProfileAsync(preparation.Money.CurrencyId, operation,
            preparation.OccurredOn, cancellationToken);
        return PreparationValidator.Validate(preparation, context.CompanyId, bank, party, profile, operation);
    }
}
