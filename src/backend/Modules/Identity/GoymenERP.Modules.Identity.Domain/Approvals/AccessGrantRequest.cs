namespace GoymenERP.Modules.Identity.Domain.Approvals;

public sealed class AccessGrantRequest
{
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
    {
        if (id == Guid.Empty || requestedByUserId == Guid.Empty
            || targetUserId == Guid.Empty || roleId == Guid.Empty
            || companyId == Guid.Empty)
        {
            throw new ArgumentException("Yetki talebi için geçerli kimlikler zorunludur.");
        }

        Id = id;
        RequestedByUserId = requestedByUserId;
        TargetUserId = targetUserId;
        RoleId = roleId;
        CompanyId = companyId;
        RequiresSecondApproval = requiresSecondApproval;
        Status = AccessGrantRequestStatus.Pending;
        RequestedAtUtc = DateTimeOffset.UtcNow;
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

    public Guid? DecidedByUserId { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public long Version { get; private set; }

    public void Approve(Guid approverUserId, DateTimeOffset decidedAtUtc)
    {
        EnsurePending();
        EnsureValidActor(approverUserId);

        if (RequiresSecondApproval
            && (approverUserId == RequestedByUserId || approverUserId == TargetUserId))
        {
            throw new InvalidOperationException(
                "Kritik yetki talebini talep eden veya hedef kullanıcı onaylayamaz.");
        }

        Status = AccessGrantRequestStatus.Approved;
        DecidedByUserId = approverUserId;
        DecidedAtUtc = decidedAtUtc;
        Version++;
    }

    public void Reject(Guid approverUserId, DateTimeOffset decidedAtUtc)
    {
        EnsurePending();
        EnsureValidActor(approverUserId);
        Status = AccessGrantRequestStatus.Rejected;
        DecidedByUserId = approverUserId;
        DecidedAtUtc = decidedAtUtc;
        Version++;
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
