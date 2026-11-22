using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal sealed partial class IdentityAccessAssignmentService
{
    private async Task RequireUserManagementAcrossMembershipAsync(Guid actor, Guid target, CancellationToken ct)
    {
        if (await new IdentityUserManagementEvaluator(database).EvaluateAsync(actor, target, timeProvider.GetUtcNow(), ct)
            == IdentityUserManagementDecision.Forbidden)
            throw new UnauthorizedAccessException();
        // No membership is a recorded business refusal after the context-company permission fence, never a grant.
    }

    private async Task RequireCommandStillAsync(Command command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId != command.Metadata.ActorId || !companyContext.IsResolved)
            throw new UnauthorizedAccessException();
        await RequireStillAsync(command.Permission, command.CompanyId, ct);
        if (command.ManagedUserId is { } target)
            await RequireUserManagementAcrossMembershipAsync(command.ActorUserId, target, ct);
    }

    /// <summary>
    /// The managed-user list (K3-B finding 1, KIM-D16/KIM-D57). Accounts are group-wide, so the context-company fence alone
    /// would list every local user of the group. A row is returned only when either
    /// <list type="bullet">
    /// <item>the user is a member of the context company now and every membership company of the user is one where the
    /// actor holds <c>identity.users.manage</c> company-wide (KIM-D16, the same evaluator as the
    /// password-reset/unlock/deactivate fence), or</item>
    /// <item>the user has no valid assignment in any company and the actor created the account (KIM-D57). The platform-level
    /// System Administrator branch of KIM-D57 has no technical mapping yet and admits nobody (fail-closed).</item>
    /// </list>
    /// </summary>
    public async Task<IReadOnlyList<IdentityManagedUserV1>> ReadUsersAsync(CancellationToken cancellationToken = default)
    {
        var company = RequireContextCompany();
        if (!Guid.TryParse(currentUser.UserId, out var actor) || actor == Guid.Empty) throw new UnauthorizedAccessException();
        await RequireAsync(ErpPermissions.IdentityUsersManage, company, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var managed = await new IdentityUserManagementEvaluator(database).GetCompanyWideManagedCompanyIdsAsync(actor, now, cancellationToken);
        var valid = database.RoleAssignments.AsNoTracking().Where(a => a.Status == RoleAssignmentStatus.Active
            && a.ValidFromUtc <= now && (a.ValidUntilUtc == null || a.ValidUntilUtc > now));
        var rows = await (from user in database.Users.AsNoTracking()
                          join credential in database.LocalCredentials.AsNoTracking() on user.Id equals credential.UserId
                          where (valid.Any(a => a.UserId == user.Id && a.CompanyId == company)
                                  && !valid.Any(a => a.UserId == user.Id && !managed.Contains(a.CompanyId)))
                              || (user.CreatedByUserId == actor && !valid.Any(a => a.UserId == user.Id))
                          orderby user.DisplayName, user.Id
                          select new { user.Id, user.DisplayName, user.IsActive, credential.UserName, credential.Version,
                              credential.MustChangePassword, credential.LockoutEndUtc }).ToListAsync(cancellationToken);
        await RequireStillAsync(ErpPermissions.IdentityUsersManage, company, cancellationToken);
        return rows.Select(x => new IdentityManagedUserV1(x.Id, x.DisplayName, x.UserName, x.IsActive,
            x.Version.ToString(CultureInfo.InvariantCulture), x.MustChangePassword, x.LockoutEndUtc > now)).ToArray();
    }

    public async Task<IdentityAccessCommandResultV1> CreateUserAsync(CreateLocalUserV1 request,
        IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        var errors = ReasonFieldErrors(request.Reason).ToList();
        if (request.UserId == Guid.Empty || request.ExpectedVersion != "0"
            || string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > UserAccount.DisplayNameMaxLength
            || LocalUserName.TryNormalize(request.UserName) is null || string.IsNullOrEmpty(request.Password))
            errors.Add(new("user", FieldCodes.Invalid));
        // Password and all derived password material stay out of the fingerprint, stored response, audit and logs.
        var prepared = await BeginAsync(new Intent(IdentityUserManagementV1.Create, "Create", "Identity.User",
            TargetText(request.UserId), ErpPermissions.IdentityUsersManage,
            new { request.UserId, request.ExpectedVersion, request.DisplayName, request.UserName, request.Reason }, null, false, errors), metadata, cancellationToken);
        if (prepared.Command is not { } command) return prepared.Early!;
        if (passwordHasher is null || passwordContext is null || localOptions is null)
            return Unstored(Answers.Unavailable(), metadata);
        var violations = localOptions.Value.ToPasswordPolicy().Evaluate(request.Password,
            [request.UserName, .. await passwordContext.GetCompanyTermsAsync(cancellationToken)]);
        if (violations.Count > 0) return Unstored(Answers.Problem(400, IdentityUserManagementV1.PolicyViolation,
            "Parola uygun değil.", "Parola geçerli parola politikasını karşılamıyor."), metadata);
        var hash = passwordHasher.Hash(request.Password);
        return await RunLockedAsync(command, async now =>
        {
            if (await database.Users.AnyAsync(x => x.Id == request.UserId, cancellationToken)) return Step.Refuse(Answers.VersionConflict());
            var normalized = LocalUserName.TryNormalize(request.UserName)!;
            if (await database.LocalCredentials.AnyAsync(x => x.NormalizedUserName == normalized, cancellationToken))
                return Step.Refuse(Answers.Problem(409, IdentityUserManagementV1.UserNameTaken, "Kullanıcı adı kullanılıyor.", "Başka bir kullanıcı adı girin."));
            var user = new UserAccount(request.UserId, request.DisplayName, email: null, createdByUserId: command.ActorUserId);
            var credential = new LocalCredential(user.Id, request.UserName, hash, passwordHasher.FormatVersion, true, now, Guid.NewGuid());
            database.Users.Add(user);
            database.LocalCredentials.Add(credential);
            database.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), user.Id, LocalAccountIdentity.Issuer, LocalAccountIdentity.SubjectOf(user.Id)));
            return UserChanged(command, user, credential, now, "created", request.Reason, before: null);
        }, cancellationToken);
    }

    public async Task<IdentityAccessCommandResultV1> ManageUserAsync(Guid userId, string action, ManageLocalUserV1 request,
        IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        if (action is not (IdentityUserManagementV1.ResetPassword or IdentityUserManagementV1.Unlock or IdentityUserManagementV1.Deactivate))
            throw new ArgumentException("Kullanıcı işlemi geçersiz.", nameof(action));
        var errors = ReasonFieldErrors(request.Reason).ToList();
        if (userId == Guid.Empty || (action == IdentityUserManagementV1.ResetPassword && string.IsNullOrEmpty(request.Password)))
            errors.Add(new("user", FieldCodes.Invalid));
        var prepared = await BeginAsync(new Intent($"identity.user.{action}.v1", action, "Identity.User", TargetText(userId),
            ErpPermissions.IdentityUsersManage, new { UserId = userId, request.ExpectedVersion, request.Reason }, request.ExpectedVersion, true, errors),
            metadata, cancellationToken, managedUserId: userId);
        if (prepared.Command is not { } command) return prepared.Early!;
        return await RunLockedAsync(command, async now =>
        {
            var membership = await new IdentityUserManagementEvaluator(database).GetMembershipCompanyIdsAsync(userId, now, cancellationToken);
            if (membership.Count == 0) return Step.Refuse(Answers.Problem(409, IdentityUserManagementV1.AssignmentRequired,
                "Şirket ataması gerekli.", "Önce kullanıcıyı bir şirkete atayın."));
            var user = await database.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
            var credential = await database.LocalCredentials.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
            if (user is null || credential is null) return Step.Refuse(Answers.NotFound());
            if (credential.Version != command.ExpectedVersion) return Step.Refuse(Answers.VersionConflict());
            if (!user.IsActive) return Step.Refuse(Answers.UserNotAssignable());
            var before = UserImage(user, credential);
            if (action == IdentityUserManagementV1.Deactivate)
            {
                if (string.IsNullOrWhiteSpace(bootstrapOptions.Value.AdminRoleCode)) return Step.Refuse(Answers.Unavailable());
                var admin = await database.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.Code == bootstrapOptions.Value.AdminRoleCode && x.IsActive, cancellationToken);
                if (admin is null) return Step.Refuse(Answers.Unavailable());
                foreach (var company in membership)
                {
                    var admins = database.RoleAssignments.AsNoTracking().Where(a => a.CompanyId == company && a.RoleId == admin.Id
                        && a.Status == RoleAssignmentStatus.Active && a.ValidFromUtc <= now && a.ValidUntilUtc == null
                        && a.Scopes.Any(s => s.ScopeType == AccessScopeType.Company && s.ScopeId == company)
                        && database.Users.Any(u => u.Id == a.UserId && u.IsActive));
                    if (await admins.AnyAsync(a => a.UserId == userId, cancellationToken)
                        && !await admins.AnyAsync(a => a.UserId != userId, cancellationToken)) return Step.Refuse(Answers.LastAdminRequired());
                }
                user.Deactivate(now);
                credential.RenewSecurityStamp(Guid.NewGuid());
            }
            else if (action == IdentityUserManagementV1.Unlock) credential.Unlock();
            else
            {
                if (passwordHasher is null || passwordContext is null || localOptions is null) return Step.Refuse(Answers.Unavailable());
                if (localOptions.Value.ToPasswordPolicy().Evaluate(request.Password!,
                        [credential.UserName, .. await passwordContext.GetCompanyTermsAsync(cancellationToken)]).Count > 0)
                    return Step.Refuse(Answers.Problem(400, IdentityUserManagementV1.PolicyViolation, "Parola uygun değil.", "Parola geçerli parola politikasını karşılamıyor."));
                credential.ResetPassword(passwordHasher.Hash(request.Password!), passwordHasher.FormatVersion, now, Guid.NewGuid());
            }
            return UserChanged(command, user, credential, now, action, request.Reason, before);
        }, cancellationToken);
    }

    private static object UserImage(UserAccount user, LocalCredential credential) => new
    { user.Id, user.DisplayName, user.IsActive, credential.Version, credential.MustChangePassword, credential.TwoFactorEnabled };

    private static Step UserChanged(Command command, UserAccount user, LocalCredential credential, DateTimeOffset now,
        string action, string reason, object? before) => Step.Changed(new Staged(
            action == "created" ? Answers.Created(user.Id, credential.Version, "Active", null) : Answers.Ok(user.Id, credential.Version, user.IsActive ? "Active" : "Inactive", null),
            [Audit(command, $"identity.user.{action}.v1", "Identity.User", user.Id, action,
                command.ExpectedVersion, credential.Version, before, UserImage(user, credential), reason.Trim(), null)],
            null, Answers.VersionConflict()));
}
