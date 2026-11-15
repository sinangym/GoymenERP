namespace GoymenERP.Modules.Counterparty.Domain.Accounts;

/// <summary>Why a new legal person succeeds an old one (KLN-D38 (2)). The values are the cases the decision names.</summary>
public enum PartySuccessionKind
{
    /// <summary>A sole proprietorship becomes a company (şahıs işletmesinin şirkete dönüşmesi).</summary>
    Conversion = 1,

    /// <summary>Merger (birleşme).</summary>
    Merger = 2,

    /// <summary>Death of a natural person (ölüm).</summary>
    Death = 3,
}

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14) rule 2: when a NEW legal person takes over, a new cari is opened, the old cari is made passive
/// and linked to the new one as predecessor → successor. The link is COMPANY-SCOPED — it is recorded on the company's two
/// accounts — and append-only: it is never edited or deleted (a wrong link is a human correction outside this record). It
/// never moves a balance: the balance moves only by an approved transfer voucher (devir fişi) in Finance, which keeps its own
/// legal basis and evidence. One predecessor has at most one successor per company, and a link that would close a cycle in
/// the company's chain is refused.
/// </summary>
public sealed class PartySuccession
{
    public const int LegalBasisMaximumLength = 1000;
    public const int ActorMaximumLength = 128;

    private PartySuccession()
    {
    }

    internal PartySuccession(
        Guid id,
        Guid companyId,
        Guid predecessorPartyId,
        Guid successorPartyId,
        PartySuccessionKind kind,
        string legalBasis,
        DateOnly effectiveOn,
        string recordedByUserId,
        DateTimeOffset recordedAtUtc)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        PredecessorPartyId = RequireId(predecessorPartyId, nameof(predecessorPartyId));
        SuccessorPartyId = RequireId(successorPartyId, nameof(successorPartyId));
        if (PredecessorPartyId == SuccessorPartyId)
        {
            throw new ArgumentException("Bir cari kendisinin halefi olamaz.", nameof(successorPartyId));
        }

        Kind = Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Bilinmeyen halef türü.");
        LegalBasis = NormalizeLegalBasis(legalBasis);
        EffectiveOn = effectiveOn;
        RecordedByUserId = RequireText(recordedByUserId, nameof(recordedByUserId), ActorMaximumLength);
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>The old cari, made passive by the succession.</summary>
    public Guid PredecessorPartyId { get; private set; }

    /// <summary>The new cari of the new legal person.</summary>
    public Guid SuccessorPartyId { get; private set; }

    public PartySuccessionKind Kind { get; private set; }

    /// <summary>The legal basis the user gives (e.g. the registry announcement); required.</summary>
    public string LegalBasis { get; private set; } = string.Empty;

    /// <summary>The business day the new legal person took over.</summary>
    public DateOnly EffectiveOn { get; private set; }

    public string RecordedByUserId { get; private set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; private set; }

    /// <summary>The legal basis as stored (trimmed, required, bounded). Throws on blank or too long.</summary>
    public static string NormalizeLegalBasis(string? legalBasis) =>
        RequireText(legalBasis!, nameof(legalBasis), LegalBasisMaximumLength);

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
