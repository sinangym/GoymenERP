using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real Identity revocation/passivation after the first successful authority read, before the VKN save.
/// The decorator only arranges the race; every permission answer is the production provider's SQL answer.</summary>
public sealed class OrganizationCompanyTaxIdAuthoritySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task A_passivated_requester_cannot_save_a_tax_id_request() => CheckAsync("request", passivate: true);

    [SqlServerFact]
    public Task A_revoked_approver_cannot_approve_a_tax_id_request() => CheckAsync("approve", passivate: false);

    [SqlServerFact]
    public Task A_revoked_approver_cannot_reject_a_tax_id_request() => CheckAsync("reject", passivate: false);

    [SqlServerFact]
    public Task A_passivated_approver_cannot_apply_a_staged_tax_id_correction() => CheckAsync("correction", passivate: true);

    private static async Task CheckAsync(string operation, bool passivate)
    {
        var actor = Guid.NewGuid();
        var companyId = TestCompanyIds.CompanyA;
        var race = new AuthorityRace();
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        await using var factory = new SqlServerRelationalApiFactory(connection, services =>
        {
            services.RemoveAll<ICurrentUser>();
            services.AddSingleton<ICurrentUser>(new TaxIdUser(actor, companyId));
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddScoped<IErpAuthorityRevalidationService>(provider => new RacingAuthority(
                (IErpAuthorityRevalidationService)provider.GetRequiredService<IErpAuthorizationService>(), race));
        });
        await using var cleanup = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanup.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        Assert.Equal(factory.DatabaseName, cleanupDb.Database.GetDbConnection().Database);
        try
        {
            var permission = operation == "request" ? OrganizationCompanyTaxIdPermissionsV1.Request : OrganizationCompanyTaxIdPermissionsV1.Approve;
            var roleId = await GrantAsync(factory, actor, companyId, permission);
            var recordId = Guid.NewGuid();
            const string proposedVkn = "9000000033";
            string? originalVkn;
            long originalVersion;
            await using (var seed = factory.Services.CreateAsyncScope())
            {
                seed.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
                var db = seed.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                var company = await db.Companies.SingleAsync(row => row.Id == companyId);
                originalVkn = company.Vkn;
                originalVersion = company.Version;
                Assert.NotEqual(proposedVkn, originalVkn);
                if (operation == "correction")
                    db.CompanyTaxIdCorrections.Add(new(recordId, companyId, "SENTETIK-tax-id-authority",
                        company.Vkn!, proposedVkn, company.Version, "SENTETIK correction", DateTimeOffset.UtcNow));
                else if (operation != "request")
                    db.CompanyTaxIdChangeRequests.Add(new(recordId, companyId, company.Vkn, proposedVkn, company.Version,
                        "SENTETIK request", "SENTETIK-other-maker", DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }

            race.AfterFirstGrant = () => LoseAuthorityAsync(factory, actor, passivate);
            var correlation = "SENTETIK-tax-id-fence-" + Guid.NewGuid().ToString("N");
            var metadata = new CommandMetadata(companyId, actor.ToString("D"), correlation, null, SourceChannels.Web,
                "127.0.0.1", "SENTETIK-agent", correlation);
            Task<CommandExecutionResult> Invoke(IOrganizationCompanyTaxIdService service) => operation switch
            {
                "request" => service.RequestCompanyTaxIdChangeAsync(new(originalVersion, proposedVkn, "SENTETIK request"), metadata, default),
                "correction" => service.ApplyCompanyTaxIdCorrectionAsync(recordId, new(originalVersion), metadata, default),
                _ => service.DecideCompanyTaxIdChangeAsync(recordId, new(originalVersion, operation, null), metadata, default),
            };

            await using (var attempt = factory.Services.CreateAsyncScope())
            {
                attempt.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
                var result = await Invoke(attempt.ServiceProvider.GetRequiredService<IOrganizationCompanyTaxIdService>());
                Assert.Equal(CommandResult.NotFound, result.Result);
                Assert.False(result.IsReplay);
                Assert.Equal(2, race.Reads);
                Assert.True(race.FirstWasGranted);
                Assert.False(race.LastWasGranted);
                var db = attempt.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                Assert.Empty(db.ChangeTracker.Entries());
                // Flush again: a denied command must not leave mutation, success audit or receipt waiting in this scope.
                await db.SaveChangesAsync();
            }

            await using (var inspect = factory.Services.CreateAsyncScope())
            {
                inspect.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
                var db = inspect.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                var company = await db.Companies.AsNoTracking().SingleAsync(row => row.Id == companyId);
                Assert.Equal((originalVkn, originalVersion), (company.Vkn, company.Version));
                Assert.Equal(0, await db.CommandExecutions.CountAsync(row => row.OriginalCorrelationId == correlation));
                Assert.Equal(0, await db.Set<AuditEvent>().CountAsync(row => row.CorrelationId == correlation));
                if (operation == "request") Assert.Empty(await db.CompanyTaxIdChangeRequests.ToListAsync());
                else if (operation == "correction") Assert.True((await db.CompanyTaxIdCorrections.SingleAsync(row => row.Id == recordId)).IsPending);
                else Assert.True((await db.CompanyTaxIdChangeRequests.SingleAsync(row => row.Id == recordId)).IsPending);
            }

            await RestoreAuthorityAsync(factory, actor, companyId, roleId, passivate);
            race.AfterFirstGrant = null;
            await using (var retry = factory.Services.CreateAsyncScope())
            {
                retry.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
                var service = retry.ServiceProvider.GetRequiredService<IOrganizationCompanyTaxIdService>();
                var result = await Invoke(service);
                Assert.Equal(operation == "request" ? CommandResult.Created : CommandResult.Succeeded, result.Result);
                Assert.False(result.IsReplay);
                Assert.True((await Invoke(service)).IsReplay);
                var db = retry.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                Assert.Equal(1, await db.CommandExecutions.CountAsync(row => row.OriginalCorrelationId == correlation));
                Assert.Equal(1, await db.Set<AuditEvent>().CountAsync(row => row.CorrelationId == correlation));
            }
        }
        finally { await cleanupDb.Database.EnsureDeletedAsync(); }
    }

    private static async Task<Guid> GrantAsync(SqlServerRelationalApiFactory factory, Guid actor, Guid company, string permission)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"TAX-ID-FENCE-{roleId:N}", "SENTETIK tax id authority", true);
        role.AddPermission(permission);
        db.Users.Add(new UserAccount(actor, "SENTETIK tax id actor", null));
        db.Roles.Add(role);
        db.RoleAssignments.Add(Assignment(actor, company, roleId));
        await db.SaveChangesAsync();
        return roleId;
    }

    private static RoleAssignment Assignment(Guid actor, Guid company, Guid roleId)
    {
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
        assignment.AddScope(AccessScopeType.Company, company);
        return assignment;
    }

    private static async Task LoseAuthorityAsync(SqlServerRelationalApiFactory factory, Guid actor, bool passivate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (passivate) (await db.Users.SingleAsync(row => row.Id == actor)).Deactivate(DateTimeOffset.UtcNow);
        else db.RoleAssignments.Remove(await db.RoleAssignments.Include(row => row.Scopes).SingleAsync(row => row.UserId == actor));
        await db.SaveChangesAsync();
    }

    private static async Task RestoreAuthorityAsync(SqlServerRelationalApiFactory factory, Guid actor, Guid company, Guid roleId, bool passivate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        if (passivate) (await db.Users.SingleAsync(row => row.Id == actor)).Reactivate(DateTimeOffset.UtcNow);
        else db.RoleAssignments.Add(Assignment(actor, company, roleId));
        await db.SaveChangesAsync();
    }

    private sealed class AuthorityRace
    {
        public Func<Task>? AfterFirstGrant { get; set; }
        public int Reads { get; set; }
        public bool FirstWasGranted { get; set; }
        public bool LastWasGranted { get; set; }
    }

    private sealed class RacingAuthority(IErpAuthorityRevalidationService inner, AuthorityRace race) : IErpAuthorityRevalidationService
    {
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            var scope = await inner.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
            if (race.AfterFirstGrant is not { } afterFirstGrant) return scope;
            race.Reads++;
            race.LastWasGranted = scope.IsGranted;
            if (race.Reads == 1)
            {
                race.FirstWasGranted = scope.IsGranted;
                Assert.True(scope.IsGranted);
                await afterFirstGrant();
            }
            return scope;
        }
    }

    private sealed class TaxIdUser(Guid actor, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([companyId]);
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
}
