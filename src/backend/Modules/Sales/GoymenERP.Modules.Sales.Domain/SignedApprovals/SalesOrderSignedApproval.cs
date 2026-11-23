using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Domain.SignedApprovals;

/// <summary>WP13 (YOL-D11): the one axis of a signed approval record. Born <see cref="Active"/>; <see cref="Passive"/> is terminal.</summary>
public enum SalesOrderSignedApprovalState
{
    Active = 1,
    Passive = 2,
}

/// <summary>Why an active signed approval became passive: withdrawn by a person (with a reason) or superseded by a newer one.</summary>
public enum SalesOrderSignedApprovalDeactivationKind
{
    Withdrawn = 1,
    Superseded = 2,
}

/// <summary>The exact vault document version the record points at (Documents owner's answer, pinned at recording time).</summary>
public sealed record SalesOrderSignedApprovalDocument(Guid DocumentId, int VersionNo, string Sha256);

/// <summary>Who made the record passive, when, how and (for a withdrawal) why; a supersession names its successor.</summary>
public sealed record SalesOrderSignedApprovalDeactivation(
    SalesOrderSignedApprovalDeactivationKind Kind, string DeactivatedBy, DateTimeOffset DeactivatedAtUtc, string? Reason,
    Guid? SupersededById);

/// <summary>
/// WP13 (YOL-D11, DALGA_PLANI_v2 1.14): the customer's wet-signed approval of a sales order (received by e-mail or WhatsApp),
/// uploaded to the order in the Document Vault and recorded here as "imzalı onay alındı" for the order's approved revision at that
/// moment. The record only points at the vault document (id, version, SHA-256): the file itself stays in the vault, which never
/// overwrites or deletes it. Nothing is deleted here either: a newer record supersedes the active one, a wrong one is withdrawn with a
/// reason; both keep the row (passive). The record does not change the order, its acceptance or any amount.
/// </summary>
public sealed class SalesOrderSignedApproval
{
    public const int MaximumReasonLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;
    public const int Sha256Length = 64;

    private SalesOrderSignedApproval(Guid id, Guid companyId, Guid orderId, int orderRevision, long orderVersion,
        SalesOrderSignedApprovalDocument document, string recordedBy, DateTimeOffset recordedAtUtc)
    {
        Id = id;
        CompanyId = companyId;
        OrderId = orderId;
        OrderRevision = orderRevision;
        OrderVersion = orderVersion;
        Document = document;
        RecordedBy = recordedBy;
        RecordedAtUtc = recordedAtUtc;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public Guid OrderId { get; }

    /// <summary>The order's commercial revision the signature covers (the approved revision when it was recorded).</summary>
    public int OrderRevision { get; }

    /// <summary>The order version the person saw when recording (the expected version they sent).</summary>
    public long OrderVersion { get; }

    public SalesOrderSignedApprovalDocument Document { get; }

    public SalesOrderSignedApprovalState State { get; private set; } = SalesOrderSignedApprovalState.Active;

    /// <summary>1 when recorded; 2 once passive. Nothing else moves it.</summary>
    public long Version { get; private set; } = 1;

    public string RecordedBy { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    public SalesOrderSignedApprovalDeactivation? Deactivation { get; private set; }

    public static SalesOrderSignedApproval Record(Guid id, Guid companyId, Guid orderId, int orderRevision, long orderVersion,
        SalesOrderSignedApprovalDocument document, string actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (id == Guid.Empty || companyId == Guid.Empty || orderId == Guid.Empty)
        {
            throw new ArgumentException("İmzalı onay kaydı, şirket ve sipariş kimlikleri boş olamaz.");
        }

        if (orderRevision < 0 || orderVersion < 1)
        {
            throw new ArgumentException("İmzalı onay geçerli bir sipariş revizyonu ve sürümü ister.");
        }

        RequireDocument(document);
        return new SalesOrderSignedApproval(id, companyId, orderId, orderRevision, orderVersion, document, Actor(actor),
            now.ToUniversalTime());
    }

    /// <summary>A newer signed approval of the same order replaces this active one (no reason: the successor is the reason).</summary>
    public void Supersede(Guid successorId, string actor, DateTimeOffset now)
    {
        if (successorId == Guid.Empty || successorId == Id)
        {
            throw new ArgumentException("Yerine geçen imzalı onay başka bir kayıt olmalıdır.", nameof(successorId));
        }

        RequireActive();
        Deactivate(new SalesOrderSignedApprovalDeactivation(SalesOrderSignedApprovalDeactivationKind.Superseded, Actor(actor),
            now.ToUniversalTime(), null, successorId));
    }

    /// <summary>A person withdraws a wrongly recorded signed approval (expected version, a reason); the row stays as passive.</summary>
    public void Withdraw(long expectedVersion, string actor, string? reason, DateTimeOffset now)
    {
        if (expectedVersion != Version)
        {
            throw new SalesVersionConflictException(expectedVersion, Version);
        }

        RequireActive();
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new SalesRuleException(SalesRuleCodes.SignedApprovalReasonRequired, "Pasifleştirme bir gerekçe ister.");
        }

        if (text.Length > MaximumReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.SignedApprovalReasonTooLong, "Pasifleştirme gerekçesi sınırı aşıyor.");
        }

        Deactivate(new SalesOrderSignedApprovalDeactivation(SalesOrderSignedApprovalDeactivationKind.Withdrawn, Actor(actor),
            now.ToUniversalTime(), text, null));
    }

    public static SalesOrderSignedApproval Restore(Guid id, Guid companyId, Guid orderId, int orderRevision, long orderVersion,
        SalesOrderSignedApprovalDocument document, SalesOrderSignedApprovalState state, long version, string recordedBy,
        DateTimeOffset recordedAtUtc, SalesOrderSignedApprovalDeactivation? deactivation)
    {
        var item = Record(id, companyId, orderId, orderRevision, orderVersion, document, recordedBy, recordedAtUtc);
        var passive = state == SalesOrderSignedApprovalState.Passive;
        if (state is not (SalesOrderSignedApprovalState.Active or SalesOrderSignedApprovalState.Passive)
            || passive != (deactivation is not null) || version != (passive ? 2 : 1))
        {
            throw new InvalidOperationException("Saklanan imzalı onay kaydı tutarsız.");
        }

        if (deactivation is not null)
        {
            item.State = state;
            item.Version = version;
            item.Deactivation = deactivation;
        }

        return item;
    }

    private void Deactivate(SalesOrderSignedApprovalDeactivation deactivation)
    {
        State = SalesOrderSignedApprovalState.Passive;
        Deactivation = deactivation;
        Version++;
    }

    private void RequireActive()
    {
        if (State != SalesOrderSignedApprovalState.Active)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, "Yalnız etkin imzalı onay pasifleşir.");
        }
    }

    private static void RequireDocument(SalesOrderSignedApprovalDocument document)
    {
        if (document.DocumentId == Guid.Empty || document.VersionNo < 1 || document.Sha256 is not { Length: Sha256Length }
            || !document.Sha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new SalesRuleException(SalesRuleCodes.SignedApprovalDocumentInvalid, "İmzalı onay belgesi tanımlanamadı.");
        }
    }

    private static string Actor(string actor)
    {
        var text = actor?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaximumActorLength)
        {
            throw new ArgumentException("İşlemi yapan kullanıcı tanımlanamadı.", nameof(actor));
        }

        return text;
    }
}
