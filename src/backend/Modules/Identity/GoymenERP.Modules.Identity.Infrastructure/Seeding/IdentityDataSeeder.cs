using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Identity.Infrastructure.Seeding;

internal sealed class IdentityDataSeeder(
    IdentityDbContext dbContext,
    IOptions<IdentitySeedOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        Validate(seed);

        var knownUserIds = await dbContext.Users
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken);
        var knownExternalIdentityIds = await dbContext.ExternalIdentities
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken);
        var knownRoleIds = await dbContext.Roles
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken);
        var existingAssignments = await dbContext.RoleAssignments
            .AsNoTracking()
            .Include(x => x.Scopes)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var assignmentSeed in seed.Assignments)
        {
            if (existingAssignments.TryGetValue(
                    assignmentSeed.Id,
                    out var existingAssignment))
            {
                EnsureExistingAssignmentMatchesSeed(existingAssignment, assignmentSeed);
            }
        }

        foreach (var userSeed in seed.Users)
        {
            if (knownUserIds.Add(userSeed.Id))
            {
                dbContext.Users.Add(new UserAccount(
                    userSeed.Id,
                    userSeed.DisplayName,
                    userSeed.Email));
            }

            foreach (var identitySeed in userSeed.ExternalIdentities)
            {
                if (knownExternalIdentityIds.Add(identitySeed.Id))
                {
                    dbContext.ExternalIdentities.Add(new ExternalIdentityLink(
                        identitySeed.Id,
                        userSeed.Id,
                        identitySeed.Issuer,
                        identitySeed.Subject));
                }
            }
        }

        foreach (var roleSeed in seed.Roles)
        {
            if (!knownRoleIds.Add(roleSeed.Id))
            {
                continue;
            }

            var role = new RoleDefinition(
                roleSeed.Id,
                roleSeed.Code,
                roleSeed.Name,
                roleSeed.IsSystem);
            foreach (var permission in roleSeed.Permissions)
            {
                role.AddPermission(permission);
            }

            dbContext.Roles.Add(role);
        }

        foreach (var assignmentSeed in seed.Assignments)
        {
            if (existingAssignments.ContainsKey(assignmentSeed.Id))
            {
                continue;
            }

            var assignment = new RoleAssignment(
                assignmentSeed.Id,
                assignmentSeed.UserId,
                assignmentSeed.RoleId,
                assignmentSeed.CompanyId,
                assignmentSeed.ValidFromUtc,
                assignmentSeed.ValidUntilUtc,
                assignmentSeed.ApprovedByUserId);
            foreach (var scopeSeed in assignmentSeed.Scopes)
            {
                assignment.AddScope(
                    Enum.Parse<AccessScopeType>(scopeSeed.Type, ignoreCase: true),
                    scopeSeed.Id);
            }

            dbContext.RoleAssignments.Add(assignment);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureExistingAssignmentMatchesSeed(
        RoleAssignment existing,
        IdentityAssignmentSeed seed)
    {
        var seedScopes = seed.Scopes
            .Select(scope => (
                Type: Enum.Parse<AccessScopeType>(scope.Type, ignoreCase: true),
                scope.Id))
            .ToHashSet();
        var existingScopes = existing.Scopes
            .Select(scope => (Type: scope.ScopeType, Id: scope.ScopeId))
            .ToHashSet();

        var matches = existing.UserId == seed.UserId
            && existing.RoleId == seed.RoleId
            && existing.CompanyId == seed.CompanyId
            && existing.ValidFromUtc == seed.ValidFromUtc
            && existing.ValidUntilUtc == seed.ValidUntilUtc
            && existing.ApprovedByUserId == seed.ApprovedByUserId
            && existing.Status == RoleAssignmentStatus.Active
            && existing.Scopes.All(scope =>
                scope.RoleAssignmentId == existing.Id
                && scope.CompanyId == existing.CompanyId)
            && existingScopes.SetEquals(seedScopes);

        if (!matches)
        {
            throw new InvalidOperationException(
                $"Seed rol ataması kalıcı kayıtla eşleşmiyor: {seed.Id:D}.");
        }
    }

    private static void Validate(IdentitySeedOptions seed)
    {
        if (seed.Users.Count == 0 || seed.Roles.Count == 0 || seed.Assignments.Count == 0)
        {
            throw new InvalidOperationException("Kimlik seed yapılandırması boş olamaz.");
        }

        var userIds = seed.Users.Select(x => x.Id).ToHashSet();
        var roleIds = seed.Roles.Select(x => x.Id).ToHashSet();
        if (userIds.Contains(Guid.Empty)
            || roleIds.Contains(Guid.Empty)
            || seed.Assignments.Any(x => x.Id == Guid.Empty))
        {
            throw new InvalidOperationException("Kimlik seed değerlerinde boş kimlik bulunamaz.");
        }

        if (seed.Assignments
            .GroupBy(x => x.Id)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                "Seed rol ataması kimlikleri benzersiz olmalıdır.");
        }

        var unknownPermissions = seed.Roles
            .SelectMany(x => x.Permissions)
            .Where(x => !ErpPermissions.IsKnown(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknownPermissions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Tanımsız seed yetkisi: {string.Join(", ", unknownPermissions)}");
        }

        if (seed.Assignments.Any(x =>
                !userIds.Contains(x.UserId)
                || !roleIds.Contains(x.RoleId)
                || x.CompanyId == Guid.Empty
                || x.Scopes.Count == 0))
        {
            throw new InvalidOperationException(
                "Rol atamaları tanımlı kullanıcı/rol/şirket ve en az bir kapsam içermelidir.");
        }

        if (seed.Assignments
            .SelectMany(x => x.Scopes.Select(scope => (Assignment: x, Scope: scope)))
            .Any(x => x.Scope.Id == Guid.Empty
                || (Enum.TryParse<AccessScopeType>(x.Scope.Type, true, out var type)
                    && type == AccessScopeType.Company
                    && x.Scope.Id != x.Assignment.CompanyId)
                || !Enum.TryParse<AccessScopeType>(x.Scope.Type, true, out _)))
        {
            throw new InvalidOperationException("Rol ataması kapsamı geçersiz.");
        }
    }
}
