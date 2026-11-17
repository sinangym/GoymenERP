namespace GoymenERP.Modules.Identity.Application.Contracts;

/// <summary>KIM-D9/D16/D17/D19. No method assigns a role, grants a permission, or reactivates an account.</summary>
public interface IIdentityUserManagementServiceV1
{
    Task<IReadOnlyList<IdentityManagedUserV1>> ReadUsersAsync(CancellationToken cancellationToken = default);
    Task<IdentityAccessCommandResultV1> CreateUserAsync(CreateLocalUserV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<IdentityAccessCommandResultV1> ManageUserAsync(Guid userId, string action, ManageLocalUserV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

public sealed record IdentityManagedUserV1(Guid UserId, string DisplayName, string UserName, bool IsActive, string Version, bool MustChangePassword, bool IsLocked);
public sealed record CreateLocalUserV1(Guid UserId, string ExpectedVersion, string DisplayName, string UserName, string Password, string Reason)
{
    public override string ToString() => nameof(CreateLocalUserV1);
}
public sealed record ManageLocalUserV1(string ExpectedVersion, string? Password, string Reason)
{
    public override string ToString() => nameof(ManageLocalUserV1);
}

public static class IdentityUserManagementV1
{
    public const string Create = "identity.user.create.v1";
    public const string ResetPassword = "password-reset";
    public const string Unlock = "unlock";
    public const string Deactivate = "deactivate";
    public const string AssignmentRequired = "identity.user.company_assignment_required";
    public const string UserNameTaken = "identity.user.user_name_taken";
    public const string PolicyViolation = "identity.user.password_policy_violation";
}
