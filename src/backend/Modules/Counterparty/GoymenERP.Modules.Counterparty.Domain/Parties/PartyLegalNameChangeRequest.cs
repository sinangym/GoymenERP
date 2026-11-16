namespace GoymenERP.Modules.Counterparty.Domain.Parties;

/// <summary>The life of a <see cref="PartyLegalNameChangeRequest"/>. A decided request never moves again.</summary>
public enum PartyLegalNameChangeStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// ALS-D111 + ALS-D113 (replacing the one-step ALS-D110 path): a change of a Cari card's official legal name (resmi/hukuki
/// unvan) is proposed here and applied only when a DIFFERENT person approves it; one approval is enough. Until then the
/// party keeps its legal name. The request is company-scoped — raised and decided inside the company whose Cari card it
/// was raised on — and records the legal name and the party version that were current when it was made, so an approval
/// can tell that the party moved in the meantime (stale). The proposer never decides their own request, not even to
/// reject it; the database enforces the same rule.
/// </summary>
public sealed class PartyLegalNameChangeRequest
{
    public const int ReasonMaximumLength = 1000;
    public const int NoteMaximumLength = 1000;
    public const int ActorMaximumLength = 128;

    private PartyLegalNameChangeRequest()
    {
    }

    public PartyLegalNameChangeRequest(
        Guid id,
        Guid companyId,
        Guid partyId,
        string currentLegalName,
        string proposedLegalName,
        long partyVersionAtRequest,
        string reason,
        string requestedBy,
        DateTimeOffset requestedAtUtc)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        PartyId = RequireId(partyId, nameof(partyId));
        var current = Party.NormalizeLegalName(currentLegalName);
        var proposed = Party.NormalizeLegalName(proposedLegalName);
        if (string.Equals(current, proposed, StringComparison.Ordinal))
        {
            throw new ArgumentException("Önerilen resmi unvan mevcut unvanla aynı.", nameof(proposedLegalName));
        }

        if (partyVersionAtRequest < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(partyVersionAtRequest));
        }

        CurrentLegalName = current;
        ProposedLegalName = proposed;
        PartyVersionAtRequest = partyVersionAtRequest;
        Reason = RequireText(reason, nameof(reason), ReasonMaximumLength);
        RequestedBy = RequireText(requestedBy, nameof(requestedBy), ActorMaximumLength);
        RequestedAtUtc = requestedAtUtc;
        Status = PartyLegalNameChangeStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid PartyId { get; private set; }

    public string CurrentLegalName { get; private set; } = string.Empty;

    public string ProposedLegalName { get; private set; } = string.Empty;

    public long PartyVersionAtRequest { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public PartyLegalNameChangeStatus Status { get; private set; }

    public string RequestedBy { get; private set; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public string? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public string? DecisionNote { get; private set; }

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public bool IsRequestedBy(string actorId) => string.Equals(RequestedBy, actorId, StringComparison.Ordinal);

    public void Approve(string decidedBy, string? note, DateTimeOffset utcNow) =>
        Decide(PartyLegalNameChangeStatus.Approved, decidedBy, note, utcNow);

    public void Reject(string decidedBy, string? note, DateTimeOffset utcNow) =>
        Decide(PartyLegalNameChangeStatus.Rejected, decidedBy, note, utcNow);

    private void Decide(PartyLegalNameChangeStatus outcome, string decidedBy, string? note, DateTimeOffset utcNow)
    {
        var actor = RequireText(decidedBy, nameof(decidedBy), ActorMaximumLength);
        if (Status != PartyLegalNameChangeStatus.Pending)
        {
            throw new InvalidOperationException("Karara bağlanmış unvan değişiklik talebi yeniden karara bağlanamaz.");
        }

        if (IsRequestedBy(actor))
        {
            throw new InvalidOperationException("Talebi açan kişi kendi unvan talebini onaylayamaz veya reddedemez.");
        }

        var normalizedNote = note?.Trim();
        if (normalizedNote is { Length: > NoteMaximumLength })
        {
            throw new ArgumentOutOfRangeException(nameof(note));
        }

        Status = outcome;
        DecidedBy = actor;
        DecidedAtUtc = utcNow;
        DecisionNote = string.IsNullOrEmpty(normalizedNote) ? null : normalizedNote;
        Version++;
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"Değer {maximumLength} karakterden uzun olamaz.");
    }
}
