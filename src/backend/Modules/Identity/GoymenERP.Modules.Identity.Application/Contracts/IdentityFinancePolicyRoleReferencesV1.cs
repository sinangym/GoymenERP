namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>No 133 policy-authoring purpose only. No holders, assignments or authorization decisions leave Identity.</summary>
public interface IIdentityFinancePolicyRoleReferencesV1
{
    Task<IdentityFinancePolicyRoleReferencesV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public sealed record IdentityFinancePolicyRoleReferenceV1(Guid RoleId, string Code, string Name, string SourceVersion);
public sealed record IdentityFinancePolicyRoleReferencesV1(Guid CompanyId, IReadOnlyList<IdentityFinancePolicyRoleReferenceV1> Roles);
