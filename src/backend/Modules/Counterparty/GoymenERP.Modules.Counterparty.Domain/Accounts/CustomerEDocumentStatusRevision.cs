namespace GoymenERP.Modules.Counterparty.Domain.Accounts;

/// <summary>How a <see cref="CustomerEDocumentStatusRevision"/> came about.</summary>
public enum CustomerEDocumentStatusRevisionKind
{
    /// <summary>
    /// The known status a profile already had before its first dated change. It is written lazily, in the same save as that
    /// change, dated by the business day of the earlier check (the migration backfills nothing).
    /// </summary>
    Baseline = 1,

    /// <summary>A status recorded (or reset) by a user through the e-belge status command.</summary>
    Recorded = 2,
}

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14): the e-belge status is kept dated next to the VKN and legal name, so an invoice reads the
/// status that was on file on its document date. Like <see cref="CustomerEDocumentProfile"/> (SAT-D119) a revision is
/// GROUP-LEVEL and keyed by the profile (one VKN); the checking company is provenance only. Revisions are append-only.
/// <see cref="EffectiveFrom"/> is the business day of the check (or reset); a document dated before every revision of the
/// profile reads <see cref="EDocumentStatus.Unknown"/> — "not checked" (SAT-D102, fail-closed), never inferred.
/// </summary>
public sealed class CustomerEDocumentStatusRevision
{
    public const int ActorMaximumLength = 128;

    private CustomerEDocumentStatusRevision()
    {
    }

    internal CustomerEDocumentStatusRevision(
        Guid id,
        Guid profileId,
        long sequence,
        EDocumentStatus status,
        DateOnly effectiveFrom,
        CustomerEDocumentStatusRevisionKind kind,
        string? recordedByUserId,
        Guid? recordedInCompanyId,
        DateTimeOffset recordedAtUtc)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", nameof(id)) : id;
        ProfileId = profileId == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", nameof(profileId)) : profileId;
        Sequence = sequence >= 1 ? sequence : throw new ArgumentOutOfRangeException(nameof(sequence));
        Status = Enum.IsDefined(status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(status), status, "Bilinmeyen e-belge durumu.");
        Kind = Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Bilinmeyen e-belge değişikliği türü.");
        if (kind == CustomerEDocumentStatusRevisionKind.Recorded)
        {
            var actor = recordedByUserId?.Trim();
            if (string.IsNullOrEmpty(actor) || actor.Length > ActorMaximumLength)
            {
                throw new ArgumentException("Kaydeden kullanıcı kimliği geçersiz.", nameof(recordedByUserId));
            }

            if (recordedInCompanyId is null || recordedInCompanyId == Guid.Empty)
            {
                throw new ArgumentException("Kaydın yapıldığı şirket boş olamaz.", nameof(recordedInCompanyId));
            }

            recordedByUserId = actor;
        }

        EffectiveFrom = effectiveFrom;
        RecordedByUserId = recordedByUserId;
        RecordedInCompanyId = recordedInCompanyId;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ProfileId { get; private set; }

    /// <summary>The profile version this status produced.</summary>
    public long Sequence { get; private set; }

    public EDocumentStatus Status { get; private set; }

    /// <summary>The business day of the check (or reset) from which this status applies.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    public CustomerEDocumentStatusRevisionKind Kind { get; private set; }

    public string? RecordedByUserId { get; private set; }

    /// <summary>Provenance only: the company in whose context the status was recorded (not a tenant key).</summary>
    public Guid? RecordedInCompanyId { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>
    /// The status on file for a document dated <paramref name="documentDate"/>: the revision with the latest effective date on
    /// or before it (newest sequence on a tie); <see cref="EDocumentStatus.Unknown"/> when none applies.
    /// </summary>
    public static EDocumentStatus StatusAsOf(IEnumerable<CustomerEDocumentStatusRevision> revisions, DateOnly documentDate)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        CustomerEDocumentStatusRevision? best = null;
        foreach (var revision in revisions)
        {
            if (revision.EffectiveFrom > documentDate)
            {
                continue;
            }

            if (best is null
                || revision.EffectiveFrom > best.EffectiveFrom
                || revision.EffectiveFrom == best.EffectiveFrom && revision.Sequence > best.Sequence)
            {
                best = revision;
            }
        }

        return best?.Status ?? EDocumentStatus.Unknown;
    }
}
