namespace GoymenERP.Modules.Counterparty.Domain.Parties;

/// <summary>The life of a <see cref="PartyTaxIdentityChangeRequest"/>. A decided request never moves again.</summary>
public enum PartyTaxIdentityChangeStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// AT-009 maker-checker (user decision 2026-09-30; CARI-DEC-004): replacing a party's existing VKN/TCKN is proposed here
/// and applied only when a DIFFERENT person approves it. The request is company-scoped — raised and decided inside the
/// company whose Cari card it was raised on — and records the number that was current when it was made, so an
/// approval can tell that the party moved in the meantime (stale). The maker never decides their own request, not even
/// to reject it; the database enforces the same rule.
/// </summary>
public sealed class PartyTaxIdentityChangeRequest
{
    public const int ReasonMaximumLength = 1000;
    public const int NoteMaximumLength = 1000;
    public const int ActorMaximumLength = 128;

    private PartyTaxIdentityChangeRequest()
    {
    }

    public PartyTaxIdentityChangeRequest(
        Guid id,
        Guid companyId,
        Guid partyId,
        string? currentTaxIdentityNumber,
        TaxIdentityNumber proposed,
        long partyVersionAtRequest,
        string reason,
        string requestedBy,
        DateTimeOffset requestedAtUtc)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        PartyId = RequireId(partyId, nameof(partyId));
        if (string.IsNullOrEmpty(proposed.Value))
        {
            throw new ArgumentException("Önerilen numara doğrulanmadan talep açılamaz.", nameof(proposed));
        }

        if (currentTaxIdentityNumber is null)
        {
            throw new ArgumentException(
                "Numarası olmayan cari için değişiklik talebi açılmaz; ilk giriş doğrudan yapılır.",
                nameof(currentTaxIdentityNumber));
        }

        if (string.Equals(currentTaxIdentityNumber, proposed.Value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Önerilen numara mevcut numarayla aynı.", nameof(proposed));
        }

        if (partyVersionAtRequest < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(partyVersionAtRequest));
        }

        CurrentTaxIdentityNumber = currentTaxIdentityNumber;
        ProposedTaxIdentityNumber = proposed.Value;
        PartyVersionAtRequest = partyVersionAtRequest;
        Reason = RequireText(reason, nameof(reason), ReasonMaximumLength);
        RequestedBy = RequireText(requestedBy, nameof(requestedBy), ActorMaximumLength);
        RequestedAtUtc = requestedAtUtc;
        Status = PartyTaxIdentityChangeStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid PartyId { get; private set; }

    public string CurrentTaxIdentityNumber { get; private set; } = string.Empty;

    public string ProposedTaxIdentityNumber { get; private set; } = string.Empty;

    public long PartyVersionAtRequest { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public PartyTaxIdentityChangeStatus Status { get; private set; }

    public string RequestedBy { get; private set; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public string? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public string? DecisionNote { get; private set; }

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public bool IsRequestedBy(string actorId) => string.Equals(RequestedBy, actorId, StringComparison.Ordinal);

    public void Approve(string decidedBy, string? note, DateTimeOffset utcNow) =>
        Decide(PartyTaxIdentityChangeStatus.Approved, decidedBy, note, utcNow);

    public void Reject(string decidedBy, string? note, DateTimeOffset utcNow) =>
        Decide(PartyTaxIdentityChangeStatus.Rejected, decidedBy, note, utcNow);

    private void Decide(PartyTaxIdentityChangeStatus outcome, string decidedBy, string? note, DateTimeOffset utcNow)
    {
        var actor = RequireText(decidedBy, nameof(decidedBy), ActorMaximumLength);
        if (Status != PartyTaxIdentityChangeStatus.Pending)
        {
            throw new InvalidOperationException("Karara bağlanmış değişiklik talebi yeniden karara bağlanamaz.");
        }

        if (IsRequestedBy(actor))
        {
            throw new InvalidOperationException("Talebi açan kişi kendi talebini onaylayamaz veya reddedemez.");
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
