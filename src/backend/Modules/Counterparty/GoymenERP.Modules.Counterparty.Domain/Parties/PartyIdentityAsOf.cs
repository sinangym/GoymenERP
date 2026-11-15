namespace GoymenERP.Modules.Counterparty.Domain.Parties;

/// <summary>
/// KLN-D38 (4): the identity a document dated <c>documentDate</c> reads. For one attribute the applicable revision is the one
/// with the latest effective date on or before the document date (an undated revision counts as "since always"); among
/// revisions of the same effective date the newest (highest <see cref="PartyIdentityRevision.Sequence"/>) wins. A date before
/// every dated revision with no undated one has no value (null) — nothing is guessed.
/// </summary>
public static class PartyIdentityAsOf
{
    public static PartyIdentityRevision? Resolve(
        IEnumerable<PartyIdentityRevision> revisions,
        PartyIdentityAttribute attribute,
        DateOnly documentDate)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        PartyIdentityRevision? best = null;
        foreach (var revision in revisions)
        {
            if (revision.Attribute != attribute
                || revision.EffectiveFrom is { } effective && effective > documentDate)
            {
                continue;
            }

            if (best is null || IsLater(revision, best))
            {
                best = revision;
            }
        }

        return best;
    }

    private static bool IsLater(PartyIdentityRevision candidate, PartyIdentityRevision current)
    {
        var candidateFrom = candidate.EffectiveFrom ?? DateOnly.MinValue;
        var currentFrom = current.EffectiveFrom ?? DateOnly.MinValue;
        return candidateFrom != currentFrom ? candidateFrom > currentFrom : candidate.Sequence > current.Sequence;
    }
}
