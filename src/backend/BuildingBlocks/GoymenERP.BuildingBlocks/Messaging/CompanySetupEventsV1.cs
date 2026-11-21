using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.BuildingBlocks.Messaging;

/// <summary>ID-3b owner contracts; opener comes from the Organization command actor, never client input.</summary>
public sealed record OrganizationCompanyOpenedV1(Guid CompanyId, Guid OpenedByUserId, DateTimeOffset OpenedAtUtc)
{
    public const string SourceName = "organization.company-setup";
    public const string EventType = "organization.company.opened.v1";
    public const int Version = 1;
}
public sealed record IdentityCompanyFirstAdminAssignedV1(Guid CompanyId, Guid UserId, Guid RoleAssignmentId,
    Guid ActorUserId, DateTimeOffset AssignedAtUtc)
{
    public const string SourceName = "identity.company-setup";
    public const string EventType = "identity.company.first_admin_assigned.v1";
    public const int Version = 1;

    /// <summary>One stable 0-to-1 fact per company, shared by opener and founder paths.</summary>
    public static Guid EventId(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company identity is required.", nameof(companyId));
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{EventType}:{companyId:D}"))[..16]);
    }
}
