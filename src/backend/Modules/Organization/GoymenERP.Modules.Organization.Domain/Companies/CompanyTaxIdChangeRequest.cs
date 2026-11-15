using GoymenERP.Modules.Organization.Domain.Common;

namespace GoymenERP.Modules.Organization.Domain.Companies;

public enum CompanyTaxIdChangeStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// KIM-S23 (KIM-D28, DEC-ORG-005): a proposed change of the company's own VKN. The preparer opens it, it stays pending
/// and the company's VKN does not move until ANOTHER person approves it (hazırlayan ≠ onaylayan). The request keeps the
/// VKN and the company version it was prepared against, so an approval never applies over a change it did not see.
/// Requests are never deleted: approved and rejected ones stay as the decision trail next to the audit rows.
/// <see cref="CurrentVkn"/> is null only for a placeholder company that has no VKN yet; its first VKN goes through the
/// same approval.
/// </summary>
public sealed class CompanyTaxIdChangeRequest : ICompanyOwnedRecord
{
    public const int ReasonMaximumLength = 1000;
    public const int NoteMaximumLength = 1000;
    public const int ActorMaximumLength = 128;

    public CompanyTaxIdChangeRequest(
        Guid id,
        Guid companyId,
        string? currentVkn,
        string proposedVkn,
        long companyVersionAtRequest,
        string reason,
        string requestedBy,
        DateTimeOffset requestedAtUtc)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        if (currentVkn is not null && !Company.IsCanonicalVkn(currentVkn))
        {
            throw new ArgumentException("Mevcut VKN kanonik biçimde değil.", nameof(currentVkn));
        }

        if (!Company.IsCanonicalVkn(proposedVkn))
        {
            throw new ArgumentException("Önerilen VKN tam olarak 10 ASCII rakamdan oluşmalıdır.", nameof(proposedVkn));
        }

        if (string.Equals(currentVkn, proposedVkn, StringComparison.Ordinal))
        {
            throw new ArgumentException("Önerilen VKN mevcut VKN ile aynı.", nameof(proposedVkn));
        }

        if (companyVersionAtRequest < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(companyVersionAtRequest));
        }

        CurrentVkn = currentVkn;
        ProposedVkn = proposedVkn;
        CompanyVersionAtRequest = companyVersionAtRequest;
        Reason = RequireText(reason, nameof(reason), ReasonMaximumLength);
        RequestedBy = RequireText(requestedBy, nameof(requestedBy), ActorMaximumLength);
        RequestedAtUtc = requestedAtUtc;
        Status = CompanyTaxIdChangeStatus.Pending;
    }

    private CompanyTaxIdChangeRequest()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string? CurrentVkn { get; private set; }

    public string ProposedVkn { get; private set; } = string.Empty;

    public long CompanyVersionAtRequest { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public CompanyTaxIdChangeStatus Status { get; private set; }

    public string RequestedBy { get; private set; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public string? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public string? DecisionNote { get; private set; }

    public long Version { get; private set; } = 1;

    public byte[] RowVersion { get; private set; } = [];

    public bool IsPending => Status == CompanyTaxIdChangeStatus.Pending;

    public bool IsRequestedBy(string actorId) => string.Equals(RequestedBy, actorId, StringComparison.Ordinal);

    /// <summary>
    /// True when the company still carries exactly the VKN and version this request was prepared against. A request
    /// that is not current is never applied; it can only be rejected.
    /// </summary>
    public bool IsCurrentFor(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.Id == CompanyId
            && company.Version == CompanyVersionAtRequest
            && string.Equals(company.Vkn, CurrentVkn, StringComparison.Ordinal);
    }

    /// <summary>Approves the request and applies the proposed VKN to <paramref name="company"/> in the same unit of work.</summary>
    public void Approve(Company company, string decidedBy, string? note, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(company);
        var actor = RequireDecidable(decidedBy);
        if (!IsCurrentFor(company))
        {
            throw new InvalidOperationException(
                "Şirket kaydı talep hazırlandıktan sonra değişti; talep uygulanamaz.");
        }

        var normalizedNote = NormalizeNote(note);
        company.ApplyApprovedVkn(ProposedVkn, utcNow);
        Close(CompanyTaxIdChangeStatus.Approved, actor, normalizedNote, utcNow);
    }

    public void Reject(string decidedBy, string? note, DateTimeOffset utcNow)
    {
        var actor = RequireDecidable(decidedBy);
        Close(CompanyTaxIdChangeStatus.Rejected, actor, NormalizeNote(note), utcNow);
    }

    private string RequireDecidable(string decidedBy)
    {
        var actor = RequireText(decidedBy, nameof(decidedBy), ActorMaximumLength);
        if (!IsPending)
        {
            throw new InvalidOperationException("Karara bağlanmış VKN değişiklik talebi yeniden karara bağlanamaz.");
        }

        if (IsRequestedBy(actor))
        {
            throw new InvalidOperationException("Talebi hazırlayan kişi kendi talebini onaylayamaz veya reddedemez.");
        }

        return actor;
    }

    private void Close(CompanyTaxIdChangeStatus outcome, string actor, string? note, DateTimeOffset utcNow)
    {
        Status = outcome;
        DecidedBy = actor;
        DecidedAtUtc = utcNow;
        DecisionNote = note;
        Version++;
    }

    private static string? NormalizeNote(string? note)
    {
        var normalized = note?.Trim();
        if (normalized is { Length: > NoteMaximumLength })
        {
            throw new ArgumentOutOfRangeException(nameof(note));
        }

        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", parameterName) : value;

    private static string RequireText(string value, string parameterName, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        return normalized.Length > maximumLength
            ? throw new ArgumentOutOfRangeException(parameterName)
            : normalized;
    }
}
