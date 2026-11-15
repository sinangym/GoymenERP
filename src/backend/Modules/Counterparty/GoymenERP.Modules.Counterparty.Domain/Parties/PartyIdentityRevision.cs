namespace GoymenERP.Modules.Counterparty.Domain.Parties;

/// <summary>Which dated identity attribute a <see cref="PartyIdentityRevision"/> records (KLN-D38).</summary>
public enum PartyIdentityAttribute
{
    /// <summary>The party's VKN/TCKN.</summary>
    TaxIdentityNumber = 1,

    /// <summary>The party's official legal name (resmi/hukuki unvan).</summary>
    LegalName = 2,
}

/// <summary>How a <see cref="PartyIdentityRevision"/> came about.</summary>
public enum PartyIdentityRevisionKind
{
    /// <summary>
    /// The value the party already had before its first dated change. It is written lazily, in the same save as that first
    /// change (the migration backfills nothing). It has no effective date: it stands for every document date until a later
    /// dated revision.
    /// </summary>
    Baseline = 1,

    /// <summary>The first VKN/TCKN entry of a party that had none (AT-009 first entry). Like a baseline it has no date.</summary>
    InitialEntry = 2,

    /// <summary>
    /// KLN-D38 (1): a wrong entry fixed by an approved, reasoned correction (CARI-DEC-004 maker-checker). The party stays the
    /// same cari. A correction carries the approved change request and its reason.
    /// </summary>
    Correction = 3,

    /// <summary>
    /// KLN-D38 (3): a real change of the same legal person (e.g. a type change that keeps the VKN), effective from the
    /// business day it was recorded.
    /// </summary>
    DatedChange = 4,
}

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14): the party's VKN/TCKN and legal name are kept dated so an invoice reads the identity that was
/// valid on its document date (rule 4). Like <see cref="Party"/> (DEC-CAR-001) a revision is GROUP-LEVEL — it has no
/// <c>CompanyId</c>; the company in which it was recorded is kept only as provenance. Revisions are append-only: a later
/// correction is a new revision, never an edit. <see cref="Sequence"/> is the party version the change produced, so two
/// revisions of one party never share it and the newest of a day is the one with the highest sequence.
/// </summary>
public sealed class PartyIdentityRevision
{
    public const int ValueMaximumLength = Party.LegalNameMaximumLength;
    public const int ReasonMaximumLength = PartyTaxIdentityChangeRequest.ReasonMaximumLength;
    public const int ActorMaximumLength = PartyTaxIdentityChangeRequest.ActorMaximumLength;

    private PartyIdentityRevision()
    {
    }

    internal PartyIdentityRevision(
        Guid id,
        Guid partyId,
        PartyIdentityAttribute attribute,
        long sequence,
        string value,
        DateOnly? effectiveFrom,
        PartyIdentityRevisionKind kind,
        string? reason,
        Guid? sourceChangeRequestId,
        string? recordedByUserId,
        Guid? recordedInCompanyId,
        DateTimeOffset recordedAtUtc)
    {
        Id = RequireId(id, nameof(id));
        PartyId = RequireId(partyId, nameof(partyId));
        Attribute = Enum.IsDefined(attribute)
            ? attribute
            : throw new ArgumentOutOfRangeException(nameof(attribute), attribute, "Bilinmeyen kimlik alanı.");
        Kind = Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Bilinmeyen kimlik değişikliği türü.");
        Sequence = sequence >= 1 ? sequence : throw new ArgumentOutOfRangeException(nameof(sequence));
        Value = RequireText(value, nameof(value), ValueMaximumLength);
        var undated = kind is PartyIdentityRevisionKind.Baseline or PartyIdentityRevisionKind.InitialEntry;
        if (undated != effectiveFrom is null)
        {
            throw new ArgumentException(
                "Başlangıç ve ilk giriş kayıtları tarihsizdir; düzeltme ve tarihli değişiklik bir yürürlük tarihi taşır.",
                nameof(effectiveFrom));
        }

        if (kind == PartyIdentityRevisionKind.Correction
            && (attribute != PartyIdentityAttribute.TaxIdentityNumber || sourceChangeRequestId is null))
        {
            throw new ArgumentException(
                "Düzeltme yalnız onaylı bir vergi/kimlik numarası değişiklik talebinden doğar.",
                nameof(sourceChangeRequestId));
        }

        if (kind != PartyIdentityRevisionKind.Correction && sourceChangeRequestId is not null)
        {
            throw new ArgumentException("Yalnız düzeltme bir değişiklik talebine bağlanır.", nameof(sourceChangeRequestId));
        }

        EffectiveFrom = effectiveFrom;
        Reason = kind == PartyIdentityRevisionKind.Correction
            ? RequireText(reason!, nameof(reason), ReasonMaximumLength)
            : reason is null ? null : RequireText(reason, nameof(reason), ReasonMaximumLength);
        SourceChangeRequestId = sourceChangeRequestId is { } source && source == Guid.Empty
            ? throw new ArgumentException("Kimlik boş olamaz.", nameof(sourceChangeRequestId))
            : sourceChangeRequestId;
        if (kind != PartyIdentityRevisionKind.Baseline && (recordedByUserId is null || recordedInCompanyId is null))
        {
            throw new ArgumentException("Kimlik değişikliğini kaydeden kullanıcı ve şirket boş olamaz.", nameof(recordedByUserId));
        }

        RecordedByUserId = recordedByUserId is null ? null : RequireText(recordedByUserId, nameof(recordedByUserId), ActorMaximumLength);
        RecordedInCompanyId = recordedInCompanyId is { } company && company == Guid.Empty
            ? throw new ArgumentException("Kimlik boş olamaz.", nameof(recordedInCompanyId))
            : recordedInCompanyId;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid PartyId { get; private set; }

    public PartyIdentityAttribute Attribute { get; private set; }

    /// <summary>The party version produced by this change (baseline: the version when dated history began).</summary>
    public long Sequence { get; private set; }

    /// <summary>The VKN/TCKN digits or the legal name, as stored on the party.</summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>
    /// The first business day the value applies to; null = since always (baseline and first entry). A correction is dated by the
    /// business day it was approved, so documents dated earlier keep reading the value that was on file then.
    /// </summary>
    public DateOnly? EffectiveFrom { get; private set; }

    public PartyIdentityRevisionKind Kind { get; private set; }

    /// <summary>The approved reason of a correction (KLN-D38 (1)); optional otherwise.</summary>
    public string? Reason { get; private set; }

    /// <summary>The approved change request a correction came from.</summary>
    public Guid? SourceChangeRequestId { get; private set; }

    public string? RecordedByUserId { get; private set; }

    /// <summary>Provenance only: the company in whose context the change was recorded (not a tenant key).</summary>
    public Guid? RecordedInCompanyId { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>A baseline revision for the value a party already holds when dated history begins.</summary>
    internal static PartyIdentityRevision Baseline(
        Guid id, Guid partyId, PartyIdentityAttribute attribute, long sequence, string value, DateTimeOffset recordedAtUtc) =>
        new(id, partyId, attribute, sequence, value, null, PartyIdentityRevisionKind.Baseline, null, null, null, null, recordedAtUtc);

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
