namespace GoymenERP.Modules.Identity.Domain.Approvals;

public sealed class AccessGrantRequest
{
    /// <summary>Longest free-text reason a requester may give.</summary>
    public const int ReasonMaxLength = 500;

    /// <summary>Longest reject reason code (canonical lower-case code, not free text).</summary>
    public const int DecisionReasonCodeMaxLength = 64;

    private AccessGrantRequest()
    {
    }

    public AccessGrantRequest(
        Guid id,
        Guid requestedByUserId,
        Guid targetUserId,
        Guid roleId,
        Guid companyId,
        bool requiresSecondApproval)
        : this(
            id,
            requestedByUserId,
            targetUserId,
            roleId,
            companyId,
            requiresSecondApproval,
            DateTimeOffset.UtcNow,
            validFromUtc: null,
            validUntilUtc: null,
            reason: null)
    {
    }

    public AccessGrantRequest(
        Guid id,
        Guid requestedByUserId,
        Guid targetUserId,
        Guid roleId,
        Guid companyId,
        bool requiresSecondApproval,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset? validFromUtc,
        DateTimeOffset? validUntilUtc,
        string? reason)
    {
        if (id == Guid.Empty || requestedByUserId == Guid.Empty
            || targetUserId == Guid.Empty || roleId == Guid.Empty
            || companyId == Guid.Empty)
        {
            throw new ArgumentException("Yetki talebi için geçerli kimlikler zorunludur.");
        }

        if (!IsValidWindow(validFromUtc, validUntilUtc))
        {
            throw new ArgumentException("Yetki bitişi için başlangıç zorunludur ve bitiş başlangıçtan sonra olmalıdır.");
        }

        Id = id;
        RequestedByUserId = requestedByUserId;
        TargetUserId = targetUserId;
        RoleId = roleId;
        CompanyId = companyId;
        RequiresSecondApproval = requiresSecondApproval;
        Status = AccessGrantRequestStatus.Pending;
        RequestedAtUtc = requestedAtUtc;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        Reason = NormalizeReason(reason);
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid RequestedByUserId { get; private set; }

    public Guid TargetUserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid CompanyId { get; private set; }

    public bool RequiresSecondApproval { get; private set; }

    public AccessGrantRequestStatus Status { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    /// <summary>Requested start of the assignment; null means "from the approval".</summary>
    public DateTimeOffset? ValidFromUtc { get; private set; }

    /// <summary>Requested end of the assignment; null means open-ended. Needs <see cref="ValidFromUtc"/>.</summary>
    public DateTimeOffset? ValidUntilUtc { get; private set; }

    /// <summary>Requester's free-text reason, trimmed, at most <see cref="ReasonMaxLength"/> characters.</summary>
    public string? Reason { get; private set; }

    public Guid? DecidedByUserId { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>Set exactly when the request is approved.</summary>
    public AccessGrantApprovalMode? ApprovalMode { get; private set; }

    /// <summary>The checker's reject reason code; set only on a rejected request.</summary>
    public string? DecisionReasonCode { get; private set; }

    /// <summary>The role assignment the approval created; null on every other state.</summary>
    public Guid? ResultingRoleAssignmentId { get; private set; }

    public long Version { get; private set; }

    /// <summary>True when the requester or the target is the given user (the KIM-D1 / ADR-004 section 6 rule).</summary>
    public bool IsRequesterOrTarget(Guid userId) => userId == RequestedByUserId || userId == TargetUserId;

    public void Approve(Guid approverUserId, DateTimeOffset decidedAtUtc, Guid? resultingRoleAssignmentId = null)
    {
        EnsurePending();
        EnsureValidActor(approverUserId);

        if (RequiresSecondApproval && IsRequesterOrTarget(approverUserId))
        {
            throw new InvalidOperationException(
                "Kritik yetki talebini talep eden veya hedef kullanıcı onaylayamaz.");
        }

        var assignmentId = EnsureAssignment(resultingRoleAssignmentId);
        Decide(AccessGrantRequestStatus.Approved, approverUserId, decidedAtUtc);
        ApprovalMode = AccessGrantApprovalMode.SecondApproval;
        ResultingRoleAssignmentId = assignmentId;
    }

    /// <summary>
    /// KIM-D5 path (a): the active founder's own direct Critical assignment. The caller proves that the founder is
    /// the bootstrap founder and active; this aggregate only refuses a decider who is not the requester.
    /// </summary>
    public void ApproveAsFounder(Guid founderUserId, DateTimeOffset decidedAtUtc, Guid resultingRoleAssignmentId)
    {
        EnsurePending();
        EnsureValidActor(founderUserId);

        if (founderUserId != RequestedByUserId)
        {
            throw new InvalidOperationException("Kurucu doğrudan ataması yalnız kurucunun kendi talebinde uygulanabilir.");
        }

        var assignmentId = EnsureAssignment(resultingRoleAssignmentId);
        Decide(AccessGrantRequestStatus.Approved, founderUserId, decidedAtUtc);
        ApprovalMode = AccessGrantApprovalMode.FounderDirect;
        ResultingRoleAssignmentId = assignmentId;
    }

    public void Reject(Guid approverUserId, string reasonCode, DateTimeOffset decidedAtUtc)
    {
        EnsurePending();
        EnsureValidActor(approverUserId);

        if (!IsCanonicalReasonCode(reasonCode))
        {
            throw new ArgumentException("Ret gerekçe kodu geçerli değil.", nameof(reasonCode));
        }

        if (RequiresSecondApproval && IsRequesterOrTarget(approverUserId))
        {
            throw new InvalidOperationException(
                "Kritik yetki talebini talep eden veya hedef kullanıcı karara bağlayamaz.");
        }

        Decide(AccessGrantRequestStatus.Rejected, approverUserId, decidedAtUtc);
        DecisionReasonCode = reasonCode;
    }

    public void Withdraw(Guid requesterUserId, DateTimeOffset withdrawnAtUtc)
    {
        EnsurePending();
        EnsureValidActor(requesterUserId);

        if (requesterUserId != RequestedByUserId)
        {
            throw new InvalidOperationException("Yetki talebini yalnız talep eden geri çekebilir.");
        }

        Decide(AccessGrantRequestStatus.Withdrawn, requesterUserId, withdrawnAtUtc);
    }

    /// <summary>A start without an end, both empty, or an end strictly after a given start.</summary>
    public static bool IsValidWindow(DateTimeOffset? validFromUtc, DateTimeOffset? validUntilUtc) =>
        !validUntilUtc.HasValue || (validFromUtc.HasValue && validUntilUtc.Value > validFromUtc.Value);

    /// <summary>Lower-case ASCII code: a letter first, then letters, digits, '_', '.', '-'; at most 64 characters.</summary>
    public static bool IsCanonicalReasonCode(string? reasonCode)
    {
        if (string.IsNullOrEmpty(reasonCode) || reasonCode.Length > DecisionReasonCodeMaxLength
            || reasonCode[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var character in reasonCode)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Trimmed reason or null; refuses an over-long text or a control character.</summary>
    public static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var trimmed = reason.Trim();
        if (trimmed.Length > ReasonMaxLength || trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("Yetki talebi gerekçesi geçerli değil.", nameof(reason));
        }

        return trimmed;
    }

    private void Decide(AccessGrantRequestStatus status, Guid deciderUserId, DateTimeOffset decidedAtUtc)
    {
        Status = status;
        DecidedByUserId = deciderUserId;
        DecidedAtUtc = decidedAtUtc;
        Version++;
    }

    private static Guid? EnsureAssignment(Guid? resultingRoleAssignmentId)
    {
        if (resultingRoleAssignmentId == Guid.Empty)
        {
            throw new ArgumentException("Onayın oluşturduğu rol ataması kimliği boş olamaz.");
        }

        return resultingRoleAssignmentId;
    }

    private void EnsurePending()
    {
        if (Status != AccessGrantRequestStatus.Pending)
        {
            throw new InvalidOperationException("Karara bağlanmış yetki talebi değiştirilemez.");
        }
    }

    private static void EnsureValidActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Karar veren kullanıcı kimliği boş olamaz.", nameof(actorUserId));
        }
    }
}
