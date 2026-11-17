using System.Data.Common;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Isolated synthetic Identity grants only. SQL acceptance sources; production assignment is not exercised.</summary>
public sealed class IdentityProductCogsAuthoritySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Cogs_requires_its_own_sql_assignment_and_fresh_revocation_and_the_Down_guard_preserve_the_original_decision() =>
        WithDatabaseAsync(async connection =>
        {
            var company = new CompanyContext();
            company.Set(TestCompanyIds.CompanyA);
            await using var seed = Database(connection, company);
            var fixture = await SeedAsync(seed, company.CompanyId);
            var actor = new Actor(fixture.UserId, company.CompanyId);
            await using var read = Database(connection, company);
            var authority = new SqlProductMasterAuthorizationService(read, actor, company,
                new ProductPilotIssuerBinding(fixture.IssuerUserId, fixture.ExternalIdentityId));
            foreach (var operation in new[] { GroupSharedMasterOperation.Read, GroupSharedMasterOperation.DraftCreate,
                         GroupSharedMasterOperation.DraftBasicEdit, GroupSharedMasterOperation.Activate,
                         GroupSharedMasterOperation.Deactivate, GroupSharedMasterOperation.Reactivate })
                Assert.True((await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, operation)).IsGranted);
            Assert.False((await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.SetCogsClassification)).IsGranted);

            var guard = Assert.IsType<SqlOperation>(new AddProductCogsClassificationAuthority().DownOperations[0]).Sql;
            Assert.Contains("THROW 51979", guard, StringComparison.Ordinal);
            await ExecuteAsync(connection, guard); // All six legacy operations alone remain rollback-safe.
            var cogs = Assignment(fixture, ProductMasterOperation.SetCogsClassification);
            seed.Set<ProductMasterAssignment>().Add(cogs);
            await seed.SaveChangesAsync();
            Assert.Equal(8, cogs.RowVersion.Length);
            var allowed = await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.SetCogsClassification);
            Assert.True(allowed.IsGranted);
            Assert.Equal((fixture.UserId, company.CompanyId, GroupSharedMasterOperation.SetCogsClassification),
                (allowed.LocalActorId, allowed.OriginCompanyId, allowed.Operation));
            Assert.Equal(allowed.AuthorityRevision, (await authority.GetSnapshotAsync(
                GroupSharedMasterKind.Product, GroupSharedMasterOperation.SetCogsClassification)).AuthorityRevision);

            var originalDecision = cogs.DecisionId;
            cogs.Revoke(Guid.NewGuid(), "SENTETIK isolated COGS revocation", DateTimeOffset.UtcNow);
            await seed.SaveChangesAsync();
            Assert.False((await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.SetCogsClassification)).IsGranted);
            Assert.True((await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.Read)).IsGranted);
            var refusedDown = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection, guard));
            Assert.Equal(51979, refusedDown.Number);
            await using var observed = Database(connection, company);
            var durable = await observed.Set<ProductMasterAssignment>().SingleAsync(x => x.Id == cogs.Id);
            Assert.False(durable.IsActive);
            Assert.Equal(originalDecision, durable.DecisionId);
            Assert.Equal(ProductMasterOperation.SetCogsClassification, durable.Operation);
            var invalid = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(connection,
                "UPDATE [identity].[ProductMasterAssignments] SET [Operation] = 8 WHERE [Id] = @id;", new SqlParameter("@id", cogs.Id)));
            Assert.Equal(547, invalid.Number);
            Assert.Contains("CK_ProductMasterAssignments_Operation", invalid.Message, StringComparison.Ordinal);
        });

    [SqlServerFact]
    public Task Cogs_sql_authority_does_not_release_a_previous_actor_or_company_after_its_real_query_returns() =>
        WithDatabaseAsync(async connection =>
        {
            var company = new MutableCogsCompanyContext();
            company.Set(TestCompanyIds.CompanyA);
            await using var seed = Database(connection, company);
            var fixture = await SeedAsync(seed, company.CompanyId);
            seed.Set<ProductMasterAssignment>().Add(Assignment(fixture, ProductMasterOperation.SetCogsClassification));
            await seed.SaveChangesAsync();
            foreach (var change in new[] { "actor", "origin" })
            {
                company.Set(TestCompanyIds.CompanyA);
                var actor = new Actor(fixture.UserId, company.CompanyId);
                var observer = new AfterAssignmentQuery();
                await using var read = Database(connection, company, observer);
                var authority = new SqlProductMasterAuthorizationService(read, actor, company,
                    new ProductPilotIssuerBinding(fixture.IssuerUserId, fixture.ExternalIdentityId));
                Assert.True((await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.SetCogsClassification)).IsGranted);
                observer.Next = () =>
                {
                    if (change == "actor") actor.ActorId = Guid.NewGuid();
                    else company.Set(TestCompanyIds.CompanyB);
                };
                var denied = await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, GroupSharedMasterOperation.SetCogsClassification);
                Assert.Equal(1, observer.Fired);
                Assert.False(denied.IsGranted);
                Assert.Equal(Guid.Empty, denied.LocalActorId);
                Assert.Equal(Guid.Empty, denied.OriginCompanyId);
                Assert.Equal(string.Empty, denied.AuthorityRevision);
            }
        });

    private static IdentityDbContext Database(string connection, ICompanyContext company, DbCommandInterceptor? observer = null)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(connection);
        if (observer is not null) options.AddInterceptors(observer);
        return new(options.Options, company);
    }
    private static async Task<Fixture> SeedAsync(IdentityDbContext database, Guid companyId)
    {
        var actor = new UserAccount(Guid.NewGuid(), "SENTETIK COGS actor", null);
        var issuer = new UserAccount(Guid.NewGuid(), "SENTETIK isolated decision issuer", null);
        var external = new ExternalIdentityLink(Guid.NewGuid(), issuer.Id, "sentetik-cogs-issuer", Guid.NewGuid().ToString("N"));
        var role = await database.Roles.SingleAsync(x => x.Code == ProductMasterAssignment.RequiredRoleCode);
        var member = new RoleAssignment(Guid.NewGuid(), actor.Id, role.Id, companyId, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
        member.AddScope(AccessScopeType.Company, companyId);
        var fixture = new Fixture(actor.Id, role.Id, issuer.Id, external.Id);
        database.AddRange(actor, issuer, external, member);
        foreach (var operation in new[] { ProductMasterOperation.Read, ProductMasterOperation.DraftCreate,
                     ProductMasterOperation.DraftBasicEdit, ProductMasterOperation.Activate,
                     ProductMasterOperation.Deactivate, ProductMasterOperation.Reactivate })
            database.Set<ProductMasterAssignment>().Add(Assignment(fixture, operation));
        await database.SaveChangesAsync();
        return fixture;
    }
    private static ProductMasterAssignment Assignment(Fixture fixture, ProductMasterOperation operation) =>
        new(Guid.NewGuid(), fixture.UserId, fixture.RoleId, operation, fixture.IssuerUserId, fixture.ExternalIdentityId,
            Guid.NewGuid(), "SENTETIK isolated SQL decision; not a production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null);
    private sealed record Fixture(Guid UserId, Guid RoleId, Guid IssuerUserId, Guid ExternalIdentityId);
    // Deliberate authority-fault input; production CompanyContext remains immutable within a request.
    private sealed class MutableCogsCompanyContext : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId) => CompanyId = companyId != Guid.Empty ? companyId : throw new ArgumentException(nameof(companyId));
    }
    private sealed class Actor(Guid id, Guid companyId) : ICurrentUser
    {
        public Guid ActorId { get; set; } = id;
        public bool IsAuthenticated => true;
        public string UserId => ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
    private sealed class AfterAssignmentQuery : DbCommandInterceptor
    {
        public Action? Next { get; set; }
        public int Fired { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Next is { } callback && command.CommandText.Contains("ProductMasterAssignments", StringComparison.Ordinal))
            {
                Next = null;
                Fired++;
                callback();
            }
            return ValueTask.FromResult(result);
        }
    }
    private static async Task ExecuteAsync(string connectionString, string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task WithDatabaseAsync(Func<string, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try { _ = factory.Services; await test(factory.DatabaseConnectionString); }
        finally
        {
            await factory.DisposeAsync();
            if (!Regex.IsMatch(factory.DatabaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside the isolated generated SQL fixture.");
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            drop.Parameters.AddWithValue("@name", factory.DatabaseName);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
