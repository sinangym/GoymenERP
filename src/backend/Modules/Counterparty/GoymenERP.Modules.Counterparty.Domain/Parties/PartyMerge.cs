namespace GoymenERP.Modules.Counterparty.Domain.Parties;

/// <summary>
/// F-027 / EK-18 #76-#77: the approved merge of a duplicate party into a surviving party, recorded once at group level.
/// It names the company the approval was given in, both parties with their versions after the merge, the reason and the
/// approver. Append-only: a merge is never edited, deleted or undone (no unmerge); a wrong merge is corrected forward
/// with a new card and reversed links (DEC-CAR-003). A duplicate is merged at most once (unique in the database).
/// </summary>
public sealed class PartyMerge
{
    public const int ReasonMaximumLength = 1000;
    public const int ActorMaximumLength = 128;
    public const int CorrelationIdMaximumLength = 128;

    private PartyMerge()
    {
    }

    public PartyMerge(
        Guid id,
        Guid approvalCompanyId,
        Guid survivorPartyId,
        long survivorPartyVersion,
        Guid mergedPartyId,
        long mergedPartyVersion,
        string reason,
        string approvedBy,
        DateTimeOffset approvedAtUtc,
        string correlationId)
    {
        Id = RequireId(id, nameof(id));
        ApprovalCompanyId = RequireId(approvalCompanyId, nameof(approvalCompanyId));
        SurvivorPartyId = RequireId(survivorPartyId, nameof(survivorPartyId));
        MergedPartyId = RequireId(mergedPartyId, nameof(mergedPartyId));
        if (survivorPartyId == mergedPartyId)
        {
            throw new ArgumentException("Cari kendisiyle birleştirilemez.", nameof(mergedPartyId));
        }

        SurvivorPartyVersion = RequireVersion(survivorPartyVersion, nameof(survivorPartyVersion));
        MergedPartyVersion = RequireVersion(mergedPartyVersion, nameof(mergedPartyVersion));
        Reason = RequireText(reason, nameof(reason), ReasonMaximumLength);
        ApprovedBy = RequireText(approvedBy, nameof(approvedBy), ActorMaximumLength);
        ApprovedAtUtc = approvedAtUtc;
        CorrelationId = RequireText(correlationId, nameof(correlationId), CorrelationIdMaximumLength);
    }

    public Guid Id { get; private set; }

    /// <summary>The active company the approval was given in (the request's server-resolved company).</summary>
    public Guid ApprovalCompanyId { get; private set; }

    public Guid SurvivorPartyId { get; private set; }

    /// <summary>The survivor's version the approval was fenced on (the survivor itself does not change).</summary>
    public long SurvivorPartyVersion { get; private set; }

    public Guid MergedPartyId { get; private set; }

    /// <summary>The duplicate's version after the merge.</summary>
    public long MergedPartyVersion { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public string ApprovedBy { get; private set; } = string.Empty;

    public DateTimeOffset ApprovedAtUtc { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    private static Guid RequireId(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;

    private static long RequireVersion(long value, string parameterName) =>
        value < 1 ? throw new ArgumentOutOfRangeException(parameterName, value, "Sürüm 1'den küçük olamaz.") : value;

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Değer {maximumLength} karakterden uzun olamaz.");
    }
}
