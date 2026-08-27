namespace GoymenERP.Modules.Identity.Domain.Assignments;

public sealed class RoleAssignment
{
    private readonly List<RoleAssignmentScope> _scopes = [];

    private RoleAssignment()
    {
    }

    public RoleAssignment(
        Guid id,
        Guid userId,
        Guid roleId,
        Guid companyId,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validUntilUtc,
        Guid? approvedByUserId)
    {
        if (id == Guid.Empty || userId == Guid.Empty || roleId == Guid.Empty
            || companyId == Guid.Empty)
        {
            throw new ArgumentException("Rol ataması için geçerli kimlikler zorunludur.");
        }

        if (validUntilUtc.HasValue && validUntilUtc.Value <= validFromUtc)
        {
            throw new ArgumentException("Yetki bitişi başlangıçtan sonra olmalıdır.");
        }

        Id = id;
        UserId = userId;
        RoleId = roleId;
        CompanyId = companyId;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
        ApprovedByUserId = approvedByUserId;
        Status = RoleAssignmentStatus.Active;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateTimeOffset ValidFromUtc { get; private set; }

    public DateTimeOffset? ValidUntilUtc { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    public RoleAssignmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public long Version { get; private set; }

    public IReadOnlyCollection<RoleAssignmentScope> Scopes => _scopes.AsReadOnly();

    public void AddScope(AccessScopeType scopeType, Guid scopeId)
    {
        if (scopeType == AccessScopeType.Company && scopeId != CompanyId)
        {
            throw new InvalidOperationException(
                "Şirket kapsamının kimliği rol atamasının şirketiyle eşleşmelidir.");
        }

        if (_scopes.Any(x => x.ScopeType == scopeType && x.ScopeId == scopeId))
        {
            return;
        }

        _scopes.Add(new RoleAssignmentScope(Id, CompanyId, scopeType, scopeId));
    }

    public bool IsEffectiveAt(DateTimeOffset instant)
    {
        return Status == RoleAssignmentStatus.Active
            && ValidFromUtc <= instant
            && (!ValidUntilUtc.HasValue || ValidUntilUtc.Value > instant)
            && _scopes.Count > 0;
    }
}
