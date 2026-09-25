using System.Globalization;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

public static class CompanyCardExpensePolicy
{
    private const int ShortTextMaximumLength = 128;
    private static readonly Regex AmountSyntax = new(
        "^(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<ValidationIssue> Validate(
        CompanyCardExpenseV1 expense,
        Guid expectedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(expense);
        var issues = new List<ValidationIssue>();

        if (expectedCompanyId == Guid.Empty || expense.CompanyId != expectedCompanyId)
        {
            issues.Add(new("companyId", "company_context_invalid"));
        }
        if (expense.Id == Guid.Empty || expense.CardId == Guid.Empty || expense.DocumentId == Guid.Empty
            || expense.Money.CurrencyId == Guid.Empty)
        {
            issues.Add(new("identity", "identity_required"));
        }
        if (expense.EventKind != CompanyCardFinancialEventKind.Expense)
        {
            issues.Add(new("eventKind", "finance_company_card_slip_is_expense"));
        }
        if (!PositiveAmount(expense.Money.Amount))
        {
            issues.Add(new("money.amount", "finance_company_card_amount_invalid"));
        }
        if (!PositiveVersion(expense.Version) || !PositiveVersion(expense.CardVersion)
            || !CanonicalProfileId(expense.Profile.ProfileId) || !PositiveVersion(expense.Profile.Version))
        {
            issues.Add(new("version", "finance_company_card_version_required"));
        }
        if (expense.SpentOn == default)
        {
            issues.Add(new("spentOn", "finance_company_card_spent_on_required"));
        }
        if (!Bounded(expense.SlipReference) || !Bounded(expense.SpentBy)
            || !Bounded(expense.ExpenseType) || !Bounded(expense.ContextType)
            || !Bounded(expense.ContextId))
        {
            issues.Add(new("evidence", "finance_company_card_expense_evidence_required"));
        }

        return issues;
    }

    private static bool PositiveAmount(string value) => Required(value)
        && AmountSyntax.IsMatch(value)
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
        && amount > 0m;

    private static bool PositiveVersion(string value) => Required(value)
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0;

    private static bool CanonicalProfileId(string value) => Required(value)
        && Guid.TryParseExact(value, "D", out var profileId)
        && profileId != Guid.Empty;

    private static bool Bounded(string value) => Required(value) && value.Length <= ShortTextMaximumLength;

    private static bool Required(string value) => !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);
}
