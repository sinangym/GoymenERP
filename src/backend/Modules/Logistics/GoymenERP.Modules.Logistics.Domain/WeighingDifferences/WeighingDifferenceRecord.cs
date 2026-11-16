namespace GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

public enum WeighingDifferenceRecordStatus
{
    /// <summary>No approval needed: no excess, or the excess is below the rate (SAT-D319 (4)–(5), SAT-D342).</summary>
    Recorded = 1,

    /// <summary>The excess is at or above the rate, or the rate was unreadable: the sale waits (SAT-D319 (6), SAT-D341).</summary>
    PendingApproval = 2,

    /// <summary>An authorised approver other than the people who entered the weighing or shipment approved it (SAT-D320 (1), SAT-D321).</summary>
    Approved = 3,
}

/// <summary>
/// Logistics-owned record of one dispatch note's weighing difference: the snapshot of the note, scale, physical cover, the
/// settings it was evaluated with and the decision. One per dispatch note; its evaluation never changes after it is recorded,
/// only its approval does (status/version). It never moves stock: the "kantar farkı" stock entry at average cost (SAT-D319 (7))
/// belongs to Inventory and is a separate slice.
/// </summary>
public sealed class WeighingDifferenceRecord
{
    public const int MaximumActorLength = 128;
    private readonly WeighingDifferenceProductResult[] products;
    private readonly string[] enteredBy;

    private WeighingDifferenceRecord(Guid companyId, Guid recordId, Guid dispatchNoteId, Guid instructionId,
        WeighingDifferenceEvaluation evaluation, IEnumerable<string> enteredBy, bool enteredByComplete,
        WeighingDifferenceRecordStatus status, int version, string recordedBy, DateTimeOffset recordedAtUtc,
        string? approvedBy, DateTimeOffset? approvedAtUtc)
    {
        CompanyId = RequireId(companyId, nameof(companyId));
        RecordId = RequireId(recordId, nameof(recordId));
        DispatchNoteId = RequireId(dispatchNoteId, nameof(dispatchNoteId));
        InstructionId = RequireId(instructionId, nameof(instructionId));
        Evaluation = evaluation ?? throw new ArgumentNullException(nameof(evaluation));
        products = [.. evaluation.Products];
        if (products.Length == 0) throw new ArgumentException("Kantar farkı kaydı ürünsüz olamaz.", nameof(evaluation));
        RecordedBy = RequireActor(recordedBy, nameof(recordedBy));
        this.enteredBy = [.. (enteredBy ?? throw new ArgumentNullException(nameof(enteredBy)))
            .Select(actor => RequireActor(actor, nameof(enteredBy)))
            .Append(RecordedBy)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        EnteredByComplete = enteredByComplete;
        Status = Enum.IsDefined(status) ? status : throw new ArgumentOutOfRangeException(nameof(status));
        Version = version >= 1 ? version : throw new ArgumentOutOfRangeException(nameof(version));
        RecordedAtUtc = recordedAtUtc;
        ApprovedBy = approvedBy is null ? null : RequireActor(approvedBy, nameof(approvedBy));
        ApprovedAtUtc = approvedAtUtc;
        RequireShape();
    }

    public Guid CompanyId { get; }
    public Guid RecordId { get; }
    public Guid DispatchNoteId { get; }
    public Guid InstructionId { get; }
    public WeighingDifferenceEvaluation Evaluation { get; }
    public IReadOnlyList<WeighingDifferenceProductResult> Products => Array.AsReadOnly(products);

    /// <summary>
    /// SAT-D321 (2): everyone who entered the weighing or a shipment on the note, plus whoever recorded this difference. None of
    /// them may approve it (SAT-D320 (1), SAT-D321 (1)).
    /// </summary>
    public IReadOnlyList<string> EnteredBy => Array.AsReadOnly(enteredBy);

    /// <summary>False when a shipment or weighing on the note was stored without its actor: then nobody can be shown not to have entered it.</summary>
    public bool EnteredByComplete { get; }

    public WeighingDifferenceRecordStatus Status { get; private set; }
    public int Version { get; private set; }
    public string RecordedBy { get; }
    public DateTimeOffset RecordedAtUtc { get; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public static WeighingDifferenceRecord Create(Guid companyId, Guid recordId, Guid dispatchNoteId, Guid instructionId,
        WeighingDifferenceEvaluation evaluation, IEnumerable<string> enteredBy, bool enteredByComplete, string actorId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var status = evaluation.Decision == WeighingDifferenceDecision.ApprovalRequired
            ? WeighingDifferenceRecordStatus.PendingApproval
            : WeighingDifferenceRecordStatus.Recorded;
        return new(companyId, recordId, dispatchNoteId, instructionId, evaluation, enteredBy, enteredByComplete, status, 1,
            actorId, now, null, null);
    }

    public static WeighingDifferenceRecord Restore(Guid companyId, Guid recordId, Guid dispatchNoteId, Guid instructionId,
        WeighingDifferenceEvaluation evaluation, IEnumerable<string> enteredBy, bool enteredByComplete,
        WeighingDifferenceRecordStatus status, int version, string recordedBy, DateTimeOffset recordedAtUtc,
        string? approvedBy, DateTimeOffset? approvedAtUtc) =>
        new(companyId, recordId, dispatchNoteId, instructionId, evaluation, enteredBy, enteredByComplete, status, version,
            recordedBy, recordedAtUtc, approvedBy, approvedAtUtc);

    /// <summary>
    /// SAT-D320 (1) / SAT-D321 (1)–(2): the approver is never one of the people who entered the weighing, a shipment or this
    /// record. Who may approve at all (the Satış Müdürü) is the approve permission's grant, a human gate.
    /// </summary>
    public void Approve(int expectedVersion, string actorId, DateTimeOffset now)
    {
        if (expectedVersion != Version)
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.VersionConflict,
                "Kantar farkı kaydı başka bir işlemle değişti.");
        if (Status != WeighingDifferenceRecordStatus.PendingApproval)
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.NotPendingApproval,
                "Kantar farkı kaydı onay beklemiyor.");
        var actor = RequireActor(actorId, nameof(actorId));
        if (!EnteredByComplete)
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.EnteredByUnknown,
                "Kantar veya sevk kaydını gireni bilinmeyen fark onaylanamaz.");
        if (enteredBy.Contains(actor, StringComparer.Ordinal))
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.SelfApproval,
                "Kantar veya sevk kaydını giren kişi kendi farkını onaylayamaz.");
        Status = WeighingDifferenceRecordStatus.Approved;
        ApprovedBy = actor;
        ApprovedAtUtc = now;
        Version++;
    }

    private void RequireShape()
    {
        var approved = Status == WeighingDifferenceRecordStatus.Approved;
        if (approved != (ApprovedBy is not null) || approved != ApprovedAtUtc.HasValue)
            throw new ArgumentException("Kantar farkı onay alanları durumla uyumsuz.");
        var waits = Evaluation.Decision == WeighingDifferenceDecision.ApprovalRequired;
        if (waits == (Status == WeighingDifferenceRecordStatus.Recorded))
            throw new ArgumentException("Kantar farkı durumu kararla uyumsuz.");
        if (Status == WeighingDifferenceRecordStatus.Recorded && Version != 1)
            throw new ArgumentException("Onaysız kantar farkı kaydı değişmez.");
    }

    private static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException("Kimlik boş olamaz.", name) : value;

    private static string RequireActor(string? value, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumActorLength)
            throw new ArgumentException("Kullanıcı kimliği geçersiz.", name);
        return trimmed;
    }
}
