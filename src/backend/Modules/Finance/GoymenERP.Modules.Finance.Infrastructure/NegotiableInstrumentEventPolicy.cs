using System.Globalization;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

public static class NegotiableInstrumentEventPolicy
{
    private const int TextMaximumLength = 256;
    private static readonly Regex AmountSyntax = new(
        "^(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<ValidationIssue> Validate(
        NegotiableInstrumentEventV1 candidate,
        Guid expectedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var issues = new List<ValidationIssue>();

        if (expectedCompanyId == Guid.Empty || candidate.CompanyId != expectedCompanyId)
        {
            issues.Add(new("companyId", "company_context_invalid"));
        }
        if (candidate.EventId == Guid.Empty || candidate.InstrumentId == Guid.Empty
            || candidate.PartyId == Guid.Empty || candidate.CompanyAccountId == Guid.Empty
            || candidate.DocumentId == Guid.Empty || candidate.FaceValue.CurrencyId == Guid.Empty)
        {
            issues.Add(new("identity", "identity_required"));
        }
        if (!Enum.IsDefined(candidate.Kind) || !Enum.IsDefined(candidate.Direction)
            || !Enum.IsDefined(candidate.State))
        {
            issues.Add(new("state", "finance_instrument_state_invalid"));
        }
        if (!PositiveVersion(candidate.ExpectedVersion) || !CanonicalProfileId(candidate.Profile.ProfileId)
            || !PositiveVersion(candidate.Profile.Version))
        {
            issues.Add(new("version", "finance_instrument_version_invalid"));
        }
        if (!PositiveAmount(candidate.FaceValue.Amount))
        {
            issues.Add(new("faceValue.amount", "finance_instrument_amount_invalid"));
        }
        if (candidate.MaturityDate == default)
        {
            issues.Add(new("maturityDate", "finance_instrument_maturity_required"));
        }
        if (candidate.OccurredAtUtc == default || candidate.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            issues.Add(new("occurredAtUtc", "finance_instrument_occurrence_invalid"));
        }
        if (!Bounded(candidate.ActorId) || !Bounded(candidate.Reason))
        {
            issues.Add(new("evidence", "finance_instrument_evidence_required"));
        }

        var reverses = candidate.ReversesEventId;
        if (candidate.State == NegotiableInstrumentState.Reversed)
        {
            if (reverses is null || reverses == Guid.Empty || reverses == candidate.EventId)
            {
                issues.Add(new("reversesEventId", "finance_instrument_reversal_source_required"));
            }
        }
        else if (reverses is not null)
        {
            issues.Add(new("reversesEventId", "finance_instrument_reversal_source_not_allowed"));
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

    private static bool Bounded(string value) => Required(value) && value.Length <= TextMaximumLength;

    private static bool Required(string value) => !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);
}
