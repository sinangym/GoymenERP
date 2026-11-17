using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

public sealed class SqlCompanySetupGroupAuthorityV1(IdentityDbContext database, ICurrentUser currentUser,
    TimeProvider timeProvider) : ICompanySetupGroupAuthorityV1
{
    public async Task<CompanySetupGroupAuthoritySnapshotV1> ReadAsync(CompanySetupGroupOperationV1 operation,
        CancellationToken cancellationToken = default)
    {
        if (operation is not (CompanySetupGroupOperationV1.OpenCompany or CompanySetupGroupOperationV1.AssignFirstAdmin)
            || !currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out var actor) || actor == Guid.Empty)
            return CompanySetupGroupAuthoritySnapshotV1.Denied(operation);
        var permission = operation == CompanySetupGroupOperationV1.OpenCompany
            ? ErpPermissions.OrganizationCompaniesOpen : ErpPermissions.IdentityCompanyFirstAdminAssign;
        if (!ErpPermissions.IsKnown(permission)) throw new InvalidOperationException("Company setup permission is unregistered.");
        var now = timeProvider.GetUtcNow();
        var rows = await (from assignment in database.CompanySetupGroupAssignments.AsNoTracking()
            join user in database.Users.AsNoTracking() on assignment.UserId equals user.Id
            where assignment.UserId == actor && user.IsActive && assignment.IsActive && assignment.Operation == (int)operation
                && assignment.ValidFromUtc <= now && (!assignment.ValidUntilUtc.HasValue || assignment.ValidUntilUtc > now)
            select new { assignment.Id, assignment.Operation, assignment.DecisionId, assignment.RowVersion,
                assignment.ValidFromUtc, assignment.ValidUntilUtc, UserVersion = user.Version }).ToListAsync(cancellationToken);
        if (rows.Count != 1 || rows[0].RowVersion.Length != 8 || !currentUser.IsAuthenticated || !Guid.TryParse(currentUser.UserId, out var afterActor) || afterActor != actor)
            return CompanySetupGroupAuthoritySnapshotV1.Denied(operation);
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Actor = actor, Rows = rows })));
        return new(true, operation, actor, revision);
    }
}
