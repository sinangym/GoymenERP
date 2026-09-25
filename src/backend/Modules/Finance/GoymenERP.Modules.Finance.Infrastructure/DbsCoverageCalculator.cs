using System.Globalization;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// DEC-FIN-008 projection only. DBS coverage changes secured/unsecured risk and available
/// DBS capacity; it never records bank receipt or cash collection.
/// </summary>
public static class DbsCoverageCalculator
{
    private static readonly Regex AmountSyntax = new(
        "^(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static DbsCoverageCalculationV1 Calculate(DbsCoverageProjectionInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var issues = new List<ValidationIssue>();
        if (input.CompanyId == Guid.Empty)
        {
            issues.Add(new("companyId", "company_context_invalid"));
        }
        if (input.Scale is < 0 or > 28 || !CanonicalProfile(input.Profile))
        {
            issues.Add(new("profile", "finance_dbs_profile_invalid"));
        }

        var currencyId = input.TotalOpen.CurrencyId;
        if (currencyId == Guid.Empty || input.SecuredBefore.CurrencyId != currencyId
            || input.AvailableDbsBefore.CurrencyId != currencyId || input.Assignment.CurrencyId != currencyId)
        {
            issues.Add(new("currencyId", "finance_dbs_currency_mismatch"));
        }

        var totalValid = TryAmount(input.TotalOpen.Amount, input.Scale, out var totalOpen);
        var securedValid = TryAmount(input.SecuredBefore.Amount, input.Scale, out var securedBefore);
        var availableValid = TryAmount(input.AvailableDbsBefore.Amount, input.Scale, out var availableBefore);
        var assignmentValid = TryAmount(input.Assignment.Amount, input.Scale, out var assignment);
        if (!totalValid || !securedValid || !availableValid || !assignmentValid)
        {
            issues.Add(new("amount", "finance_dbs_amount_invalid"));
        }
        if (issues.Count != 0)
        {
            return new(null, issues);
        }

        if (securedBefore > totalOpen)
        {
            issues.Add(new("securedBefore", "finance_dbs_secured_exceeds_open"));
        }
        if (assignment <= 0m)
        {
            issues.Add(new("assignment", "finance_dbs_assignment_positive_required"));
        }
        if (assignment > totalOpen - securedBefore)
        {
            issues.Add(new("assignment", "finance_dbs_assignment_exceeds_unsecured"));
        }
        if (assignment > availableBefore)
        {
            issues.Add(new("assignment", "finance_dbs_assignment_exceeds_available"));
        }
        if (issues.Count != 0)
        {
            return new(null, issues);
        }

        var securedAfter = securedBefore + assignment;
        return new(new DbsCoverageProjectionV1(
            input.CompanyId,
            Money(totalOpen, currencyId, input.Scale),
            Money(securedAfter, currencyId, input.Scale),
            Money(totalOpen - securedAfter, currencyId, input.Scale),
            Money(availableBefore - assignment, currencyId, input.Scale),
            Money(0m, currencyId, input.Scale),
            input.Profile,
            CreatesMoneyEvent: false), []);
    }

    private static bool TryAmount(string raw, int scale, out decimal amount)
    {
        amount = 0m;
        if (scale is < 0 or > 28 || string.IsNullOrWhiteSpace(raw) || !AmountSyntax.IsMatch(raw))
        {
            return false;
        }
        var separator = raw.IndexOf('.', StringComparison.Ordinal);
        if (separator >= 0 && raw.Length - separator - 1 > scale)
        {
            return false;
        }
        return decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }

    private static bool CanonicalProfile(ProfileReference profile) => profile is not null
        && Guid.TryParseExact(profile.ProfileId, "D", out var profileId)
        && profileId != Guid.Empty
        && long.TryParse(profile.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0;

    private static MoneyText Money(decimal amount, Guid currencyId, int scale) =>
        new(amount.ToString($"F{scale}", CultureInfo.InvariantCulture), currencyId);
}
