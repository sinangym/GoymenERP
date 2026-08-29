using System.Data;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Seeding;

internal sealed class SystemRolePermissionReconciler(IdentityDbContext dbContext)
    : ISystemRolePermissionReconciler
{
    public async Task<SystemRolePermissionReconciliationResult> ReconcileAsync(
        SystemRolePermissionReconciliationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!dbContext.Database.IsSqlServer())
        {
            throw new InvalidOperationException(
                "Sistem rolü yetki uzlaştırması yalnız ilişkisel SQL Server sağlayıcısında çalışır.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var permissionsAdded = 0;
            foreach (var target in plan.Targets.OrderBy(static target => target.RoleId))
            {
                var role = await LoadExactRoleForUpdateAsync(target, cancellationToken);
                foreach (var permission in target.PermissionsToEnsure)
                {
                    if (role.Permissions.Any(existing => string.Equals(
                            existing.PermissionCode,
                            permission,
                            StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    role.AddPermission(permission);
                    permissionsAdded++;
                }
            }

            if (permissionsAdded > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new SystemRolePermissionReconciliationResult(
                plan.Targets.Count,
                permissionsAdded);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<RoleDefinition> LoadExactRoleForUpdateAsync(
        SystemRolePermissionTarget target,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Roles
            .FromSqlInterpolated($$"""
                SELECT [Id], [Code], [Name], [IsSystem], [IsActive],
                       [CreatedAtUtc], [UpdatedAtUtc], [Version]
                FROM [identity].[Roles] WITH (UPDLOCK, HOLDLOCK)
                WHERE [Id] = {{target.RoleId}} OR [Code] = {{target.RoleCode}}
                """)
            .Include(role => role.Permissions)
            .ToListAsync(cancellationToken);

        var roleById = candidates.SingleOrDefault(role => role.Id == target.RoleId);
        var roleByCode = candidates.SingleOrDefault(role => string.Equals(
            role.Code,
            target.RoleCode,
            StringComparison.Ordinal));

        if (roleById is null || roleByCode is null || roleById.Id != roleByCode.Id)
        {
            throw new SystemRolePermissionReconciliationException(
                $"Sistem rolü kimlik/kod eşleşmesi geçersiz: {target.RoleId:D} / {target.RoleCode}.");
        }

        return roleById;
    }
}
