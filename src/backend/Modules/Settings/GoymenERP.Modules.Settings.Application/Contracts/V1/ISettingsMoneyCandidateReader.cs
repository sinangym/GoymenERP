namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Read-only money currency candidates for the server-resolved company (Business #66
/// 5702138790). A separate port on purpose: <see cref="ISettingsPrerequisiteProvider"/> is
/// implemented by other modules' test doubles and does not change.
/// <para>
/// Being listed is NOT validity. A candidate carries no scale, profile, version, operation
/// or effective range; a consumer revalidates the chosen <c>CurrencyId</c> with its own
/// known-id resolution (<see cref="ISettingsPrerequisiteProvider.GetMoneyProfileAsync"/>
/// for its operation and business date) before any write.
/// </para>
/// <para>
/// The company is never a request input. Outcomes (Business #66 5713672616, error 1):
/// a reachable source with no candidates for the company answers an empty list, meaning
/// "no candidates"; a missing, unreachable, forbidden or malformed source throws and is
/// never an empty list, a hard-coded currency or a default. A list is malformed when
/// <see cref="MoneyCurrencyCandidatesV1.EnsureWellFormed"/> rejects it.
/// </para>
/// <para>
/// Candidate order is undefined. The first row is not a default, and no candidate — not
/// even a single one — is selected automatically.
/// </para>
/// </summary>
public interface ISettingsMoneyCandidateReader
{
    Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One candidate, and only what a picker may show and send back: the owner's stable
/// currency identity (the sole reference for selection and revalidation), the owner's
/// real code, and its display label. The code is a label, never an identity.
/// </summary>
public sealed record MoneyCurrencyCandidateV1(Guid CurrencyId, string Code, string DisplayLabel);

public static class MoneyCurrencyCandidatesV1
{
    /// <summary>
    /// The one shape check for a candidate list, for a provider before it answers and a
    /// consumer before it shows one. A missing list or row, an empty <c>CurrencyId</c>, an
    /// empty or whitespace <c>Code</c> or <c>DisplayLabel</c>, or a <c>CurrencyId</c> listed
    /// twice rejects the whole list as <c>settings_provider_unavailable</c>: a malformed
    /// source is unavailable, not partly usable. An empty list is well formed. The list is
    /// returned as given — never filtered, reordered or defaulted.
    /// </summary>
    public static IReadOnlyList<MoneyCurrencyCandidateV1> EnsureWellFormed(
        IReadOnlyList<MoneyCurrencyCandidateV1>? candidates)
    {
        if (candidates is null)
        {
            throw Malformed();
        }

        var seen = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.CurrencyId == Guid.Empty
                || string.IsNullOrWhiteSpace(candidate.Code)
                || string.IsNullOrWhiteSpace(candidate.DisplayLabel)
                || !seen.Add(candidate.CurrencyId))
            {
                throw Malformed();
            }
        }

        return candidates;
    }

    private static SettingsPrerequisiteException Malformed() => new("settings_provider_unavailable");
}
