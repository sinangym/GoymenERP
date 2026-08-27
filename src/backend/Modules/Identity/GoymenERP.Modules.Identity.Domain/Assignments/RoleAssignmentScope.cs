namespace GoymenERP.Modules.Identity.Domain.Assignments;

public sealed class RoleAssignmentScope
{
    private RoleAssignmentScope()
    {
    }

    internal RoleAssignmentScope(
        Guid roleAssignmentId,
        Guid companyId,
        AccessScopeType scopeType,
        Guid scopeId)
    {
        if (roleAssignmentId == Guid.Empty || companyId == Guid.Empty || scopeId == Guid.Empty)
        {
            throw new ArgumentException("Yetki kapsamı için geçerli kimlikler zorunludur.");
        }

        RoleAssignmentId = roleAssignmentId;
        CompanyId = companyId;
        ScopeType = scopeType;
        ScopeId = scopeId;
    }

    public Guid RoleAssignmentId { get; private set; }

    public Guid CompanyId { get; private set; }

    public AccessScopeType ScopeType { get; private set; }

    public Guid ScopeId { get; private set; }
}
