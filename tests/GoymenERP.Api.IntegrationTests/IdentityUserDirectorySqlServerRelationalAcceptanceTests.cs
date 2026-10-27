using System.Security.Claims;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MOD-08 user directory through the real host composition on a real SQL Server: the membership predicate, the
/// display-name search and the company fence are SQL translations here, not in-memory LINQ. Data is set up through
/// Identity's own context (the owner store) in two fresh synthetic companies, so nothing seeded can explain an answer;
/// the Sales central adapter is resolved from the same host and reaches the same port. Every identity is SENTETIK.
/// </summary>
public sealed class IdentityUserDirectorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_directory_names_and_lists_only_the_people_of_the_context_company_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var companyX = Guid.NewGuid();
            var companyY = Guid.NewGuid();
            var seeded = await SeedAsync(factory, companyX, companyY);
            var baseline = await CountsAsync(factory);

            await AsCallerAsync(factory, seeded.Caller, companyX, async services =>
            {
                var port = services.GetRequiredService<IUserDirectoryReadPortV1>();

                var resolved = await port.ResolveAsync(new UserDirectoryResolveRequestV1(companyX, Actor(seeded.Caller),
                    [seeded.Alice, seeded.Retired, seeded.Passive, seeded.Outsider, Guid.NewGuid()]));
                Assert.Equal(UserDirectoryOutcomeV1.Found, resolved.Outcome);
                Assert.Equal(
                    new Dictionary<Guid, (string, bool)>
                    {
                        [seeded.Alice] = ("SENTETIK Alice", true),
                        [seeded.Retired] = ("SENTETIK Emekli", false),
                        [seeded.Passive] = ("SENTETIK Pasif", false),
                    },
                    resolved.Items.ToDictionary(item => item.UserId, item => (item.DisplayName, item.IsActiveInCompany)));

                // The display-name fragment is matched by the database (case-insensitive under its collation).
                var search = await port.SearchActiveAsync(
                    new UserDirectorySearchRequestV1(companyX, Actor(seeded.Caller), "sentetik", 10));
                Assert.Equal(UserDirectoryOutcomeV1.Found, search.Outcome);
                Assert.False(search.HasMore);
                Assert.Equal(["SENTETIK Alice", "SENTETIK Arayan", "SENTETIK Bora"],
                    search.Items.Select(item => item.DisplayName));

                var narrowed = await port.SearchActiveAsync(
                    new UserDirectorySearchRequestV1(companyX, Actor(seeded.Caller), "bOrA", 10));
                Assert.Equal([seeded.Bora], narrowed.Items.Select(item => item.UserId));

                // A LIKE wildcard in the fragment is a literal character, not "everyone".
                var wildcard = await port.SearchActiveAsync(
                    new UserDirectorySearchRequestV1(companyX, Actor(seeded.Caller), "%", 10));
                Assert.Equal((UserDirectoryOutcomeV1.Found, 0), (wildcard.Outcome, wildcard.Items.Count));

                var page = await port.SearchActiveAsync(
                    new UserDirectorySearchRequestV1(companyX, Actor(seeded.Caller), null, 1));
                Assert.True(page.HasMore);
                Assert.Equal(["SENTETIK Alice"], page.Items.Select(item => item.DisplayName));

                // Naming another company than the resolved context is denied before anything is read.
                var foreign = await port.ResolveAsync(
                    new UserDirectoryResolveRequestV1(companyY, Actor(seeded.Caller), [seeded.Outsider]));
                Assert.Equal((UserDirectoryOutcomeV1.Denied, 0), (foreign.Outcome, foreign.Items.Count));

                // The Sales central adapter reaches the same port and maps only the display fields.
                var sales = await services.GetRequiredService<ISalesUserDisplaySourceV1>().GetAuthorizedBatchAsync(
                    new SalesUserDisplayBatchRequestV1(companyX, Actor(seeded.Caller), [seeded.Alice, seeded.Outsider]));
                Assert.Equal(SalesUserDisplayBatchOutcomeV1.Success, sales.Outcome);
                Assert.Equal([new SalesUserDisplayV1(seeded.Alice, "SENTETIK Alice", true)], sales.Items);
            });

            // Company Y: its own member and nobody of X.
            await AsCallerAsync(factory, seeded.Outsider, companyY, async services =>
            {
                var search = await services.GetRequiredService<IUserDirectoryReadPortV1>().SearchActiveAsync(
                    new UserDirectorySearchRequestV1(companyY, Actor(seeded.Outsider), null, 10));
                Assert.Equal([seeded.Outsider], search.Items.Select(item => item.UserId));
            });

            // A former member whose token still carries X is refused by the fresh membership read.
            await AsCallerAsync(factory, seeded.Retired, companyX, async services =>
            {
                var answer = await services.GetRequiredService<IUserDirectoryReadPortV1>().ResolveAsync(
                    new UserDirectoryResolveRequestV1(companyX, Actor(seeded.Retired), [seeded.Alice]));
                Assert.Equal((UserDirectoryOutcomeV1.Denied, 0), (answer.Outcome, answer.Items.Count));
                var sales = await services.GetRequiredService<ISalesUserDisplaySourceV1>().GetAuthorizedBatchAsync(
                    new SalesUserDisplayBatchRequestV1(companyX, Actor(seeded.Retired), [seeded.Alice]));
                Assert.Equal((SalesUserDisplayBatchOutcomeV1.Denied, 0), (sales.Outcome, sales.Items.Count));
            });

            // A read: no Identity row changed.
            Assert.Equal(baseline, await CountsAsync(factory));
        });
    }

    /// <summary>
    /// SAT-D27 (1): the sales-owner picker lists only the people active in the company who hold the order create name there
    /// now. The holder predicate (active assignment inside its validity, active role carrying the name, company scope for a
    /// company-wide-only name) is a SQL translation here; the Sales adapter and the Sales handler are resolved from the same
    /// host, and the handler itself is authorized by a real grant of the caller (the request write name, SAT-D27 (2)).
    /// </summary>
    [SqlServerFact]
    public async Task The_sales_owner_picker_lists_only_the_order_create_holders_of_the_context_company_on_real_SQL()
    {
        await WithDatabaseAsync(async factory =>
        {
            var companyX = Guid.NewGuid();
            var companyY = Guid.NewGuid();
            var seeded = await SeedHoldersAsync(factory, companyX, companyY);
            var baseline = await CountsAsync(factory);

            await AsCallerAsync(factory, seeded.Caller, companyX, async services =>
            {
                var port = services.GetRequiredService<IUserDirectoryReadPortV1>();
                var holders = await port.SearchActiveAsync(new UserDirectorySearchRequestV1(
                    companyX, Actor(seeded.Caller), null, 10, SalesPermissionManifestV1.DraftsCreate));
                Assert.Equal(UserDirectoryOutcomeV1.Found, holders.Outcome);
                Assert.Equal([("SENTETIK Satisci", seeded.Holder), ("SENTETIK Satisci Iki", seeded.SecondHolder)],
                    holders.Items.Select(item => (item.DisplayName, item.UserId)));

                // The fragment narrows within the holders (the Y holder and the site holder also match "Satisci").
                var narrowed = await port.SearchActiveAsync(new UserDirectorySearchRequestV1(
                    companyX, Actor(seeded.Caller), "iki", 10, SalesPermissionManifestV1.DraftsCreate));
                Assert.Equal([seeded.SecondHolder], narrowed.Items.Select(item => item.UserId));

                // An unregistered name is refused, never "everyone".
                var unknown = await port.SearchActiveAsync(new UserDirectorySearchRequestV1(
                    companyX, Actor(seeded.Caller), null, 10, "sales.owners.everyone"));
                Assert.Equal((UserDirectoryOutcomeV1.Denied, 0), (unknown.Outcome, unknown.Items.Count));

                // The Sales adapter passes the eligibility name and maps id + name only.
                var adapter = await services.GetRequiredService<ISalesOwnerCandidateSourceV1>().SearchAsync(
                    new SalesOwnerCandidateRequestV1(companyX, Actor(seeded.Caller), null, 1, SalesPermissionManifestV1.DraftsCreate));
                Assert.Equal(SalesOwnerCandidateOutcomeV1.Success, adapter.Outcome);
                Assert.True(adapter.HasMore);
                Assert.Equal([new SalesOwnerOptionV1(seeded.Holder, "SENTETIK Satisci")], adapter.Items);

                // The Sales handler: the caller holds only the request write name (SAT-D27 (2)) and gets the same list.
                var picker = await services.GetRequiredService<ISalesReferenceReadService>().GetSalesOwnersAsync(new SalesOwnerQueryV1(null, 10));
                Assert.Equal(200, picker.StatusCode);
                Assert.Equal(companyX, picker.Value!.CompanyId);
                Assert.Equal([seeded.Holder, seeded.SecondHolder], picker.Value.Owners.Select(owner => owner.UserId));
            });

            // Company Y lists its own holder only.
            await AsCallerAsync(factory, seeded.HolderInY, companyY, async services =>
            {
                var holders = await services.GetRequiredService<IUserDirectoryReadPortV1>().SearchActiveAsync(
                    new UserDirectorySearchRequestV1(companyY, Actor(seeded.HolderInY), null, 10, SalesPermissionManifestV1.DraftsCreate));
                Assert.Equal([seeded.HolderInY], holders.Items.Select(item => item.UserId));
            });

            // A member of X without any Sales name: the handler refuses before Identity lists anyone.
            await AsCallerAsync(factory, seeded.PlainMember, companyX, async services =>
            {
                var picker = await services.GetRequiredService<ISalesReferenceReadService>().GetSalesOwnersAsync(new SalesOwnerQueryV1(null, 10));
                Assert.Equal((403, null), (picker.StatusCode, picker.Value));
            });

            Assert.Equal(baseline, await CountsAsync(factory));
        });
    }

    private static async Task<Holders> SeedHoldersAsync(SqlServerRelationalApiFactory factory, Guid companyX, Guid companyY)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;
        var caller = AddUser(db, "SENTETIK Arayan");
        Grant(db, caller, companyX, SalesPermissionManifestV1.RequestsWrite, AccessScopeType.Company, companyX, now.AddDays(-1), null);
        var holder = AddUser(db, "SENTETIK Satisci");
        Grant(db, holder, companyX, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Company, companyX, now.AddDays(-1), null);
        var secondHolder = AddUser(db, "SENTETIK Satisci Iki");
        Grant(db, secondHolder, companyX, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Company, companyX, now.AddDays(-1), null);
        // A member of X whose order create name is site-scoped only: not a holder of a company-wide-only name.
        var siteHolder = AddUser(db, "SENTETIK Satisci Saha");
        Assign(db, siteHolder, companyX, now.AddDays(-1), null);
        Grant(db, siteHolder, companyX, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Site, Guid.NewGuid(), now.AddDays(-1), null);
        // A member of X whose order create grant has expired.
        var expired = AddUser(db, "SENTETIK Satisci Eski");
        Assign(db, expired, companyX, now.AddDays(-1), null);
        Grant(db, expired, companyX, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Company, companyX, now.AddDays(-30), now.AddDays(-1));
        // A member of X who holds the name in Y only.
        var holderInY = AddUser(db, "SENTETIK Satisci Y");
        Assign(db, holderInY, companyX, now.AddDays(-1), null);
        Grant(db, holderInY, companyY, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Company, companyY, now.AddDays(-1), null);
        // A plain member of X and a passive holder.
        var plain = AddUser(db, "SENTETIK Uye");
        Assign(db, plain, companyX, now.AddDays(-1), null);
        var passive = AddUser(db, "SENTETIK Satisci Pasif");
        Grant(db, passive, companyX, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Company, companyX, now.AddDays(-1), null);
        await db.SaveChangesAsync();
        db.Entry(await db.Users.SingleAsync(user => user.Id == passive))
            .Property(nameof(UserAccount.IsActive)).CurrentValue = false;
        await db.SaveChangesAsync();
        return new Holders(caller, holder, secondHolder, holderInY, plain);
    }

    private static void Grant(IdentityDbContext db, Guid user, Guid company, string permission, AccessScopeType scopeType, Guid scopeId,
        DateTimeOffset from, DateTimeOffset? until)
    {
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"OWNER-PICKER-{roleId:N}", "SENTETIK sorumlu secici rolu", isSystem: false);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), user, roleId, company, from, until, approvedByUserId: null);
        assignment.AddScope(scopeType, scopeId);
        db.Roles.Add(role);
        db.RoleAssignments.Add(assignment);
    }

    private sealed record Holders(Guid Caller, Guid Holder, Guid SecondHolder, Guid HolderInY, Guid PlainMember);

    private static async Task<Seeded> SeedAsync(SqlServerRelationalApiFactory factory, Guid companyX, Guid companyY)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var caller = AddUser(db, "SENTETIK Arayan");
        var alice = AddUser(db, "SENTETIK Alice");
        var bora = AddUser(db, "SENTETIK Bora");
        var retired = AddUser(db, "SENTETIK Emekli");
        var passive = AddUser(db, "SENTETIK Pasif");
        var outsider = AddUser(db, "SENTETIK Disaridan");
        var now = DateTimeOffset.UtcNow;
        Assign(db, caller, companyX, now.AddDays(-1), null);
        Assign(db, alice, companyX, now.AddDays(-1), null);
        Assign(db, bora, companyX, now.AddDays(-1), null);
        Assign(db, retired, companyX, now.AddDays(-30), now.AddDays(-1));
        Assign(db, passive, companyX, now.AddDays(-1), null);
        Assign(db, outsider, companyY, now.AddDays(-1), null);
        await db.SaveChangesAsync();
        db.Entry(await db.Users.SingleAsync(user => user.Id == passive))
            .Property(nameof(UserAccount.IsActive)).CurrentValue = false;
        await db.SaveChangesAsync();
        return new Seeded(caller, alice, bora, retired, passive, outsider);
    }

    private static Guid AddUser(IdentityDbContext db, string displayName)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new UserAccount(id, displayName, $"{id:N}@example.invalid"));
        return id;
    }

    private static void Assign(IdentityDbContext db, Guid user, Guid company, DateTimeOffset from, DateTimeOffset? until)
    {
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"DIRECTORY-{roleId:N}", "SENTETIK dizin rolu", isSystem: false);
        role.AddPermission(ErpPermissions.OrganizationSitesRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), user, roleId, company, from, until, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        db.Roles.Add(role);
        db.RoleAssignments.Add(assignment);
    }

    private static async Task AsCallerAsync(
        SqlServerRelationalApiFactory factory,
        Guid caller,
        Guid company,
        Func<IServiceProvider, Task> test)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        try
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(GoymenClaimTypes.UserId, Actor(caller)),
                    new Claim(GoymenClaimTypes.CompanyAccess, company.ToString("D")),
                ], "IsolatedSqlFixture")),
            };
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
            await test(scope.ServiceProvider);
        }
        finally
        {
            accessor.HttpContext = previous;
        }
    }

    private static async Task<(int Users, int Roles, int Assignments, long Versions)> CountsAsync(
        SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (await db.Users.CountAsync(), await db.Roles.CountAsync(), await db.RoleAssignments.CountAsync(),
            await db.RoleAssignments.SumAsync(assignment => assignment.Version));
    }

    private static string Actor(Guid userId) => userId.ToString("D");

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Identity_UserDirectory_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection,
            applyTestCompanyBTopologyTemplates: false, databaseName: databaseName);
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        // Validate the immutable cleanup target before running assertions, not in their finally path.
        Assert.Equal(databaseName, cleanupDb.Database.GetDbConnection().Database);
        try
        {
            await test(factory);
        }
        finally
        {
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }

    private sealed record Seeded(Guid Caller, Guid Alice, Guid Bora, Guid Retired, Guid Passive, Guid Outsider);
}
