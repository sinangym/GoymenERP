using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// BLG-03 against the real provider and a real database. The audit query is paused right after its
/// SQL read; the reader's company-wide audit grant is removed in real Identity rows meanwhile. The
/// held rows must not leave: 403, no item, no cursor. The same reader with the grant intact reads
/// the page normally, so the refusal is the fence and not the fixture.
/// </summary>
public sealed class IdentityAuditReadFreshAuthoritySqlServerRelationalAcceptanceTests
{
    private const string Permission = ErpPermissions.IdentityAuditRead;
    private const string SensitiveAfter = "{\"Secret\":\"SENTETIK-AUDIT-PAYLOAD\"}";

    [SqlServerFact]
    public async Task Audit_read_revalidates_authority_after_SQL_and_releases_nothing_once_revoked()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var company = TestCompanyIds.CompanyA;
        var pause = new AuditReadPause();
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services => services.AddDbContext<AuditDbContext>((_, options) => options.AddInterceptors(pause)));

        try
        {
            // The seeded group administrator holds the company-wide audit read through real Identity rows.
            using var client = factory.CreateClientFor(TestUsers.GroupAdmin, company);
            var correlation = $"blg03-{Guid.NewGuid():N}";
            await SeedAuditEventAsync(factory, company, correlation);
            await SeedAuditEventAsync(factory, company, correlation);
            var path = $"/api/v1/audit/events?pageSize=1&correlationId={correlation}";

            // Control: with the grant standing the page and its cursor are released.
            using (var allowed = await client.GetAsync(path))
            {
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
                var page = await allowed.Content.ReadFromJsonAsync<AuditEventPage>();
                Assert.NotNull(page);
                Assert.Single(page.Items);
                Assert.NotNull(page.NextCursor);
            }

            pause.Arm();
            var pending = client.GetAsync(path);
            Exception? revocationFailure = null;
            try
            {
                await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
                // Another request removes the grant while this read holds its rows.
                await RevokeAsync(factory, TestUsers.GroupAdmin);
            }
            catch (Exception exception)
            {
                revocationFailure = exception;
            }
            finally
            {
                pause.Continue.TrySetResult();
            }

            using var refused = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            if (revocationFailure is not null)
                throw revocationFailure;
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            var body = await refused.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SENTETIK-AUDIT-PAYLOAD", body, StringComparison.Ordinal);
            Assert.DoesNotContain(correlation, body, StringComparison.Ordinal);
            Assert.DoesNotContain("nextCursor", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("items", body, StringComparison.OrdinalIgnoreCase);

            // And the next request is refused on its own.
            using var after = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    /// <summary>Removes the audit read from every role the reader holds, in the real Identity rows.</summary>
    private static async Task RevokeAsync(SqlServerRelationalApiFactory factory, string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = await identity.ExternalIdentities
            .Where(value => value.Issuer == BootstrapIdentityOptions.Issuer && value.Subject == subject)
            .Select(value => value.UserId).SingleAsync();
        var roleIds = identity.RoleAssignments.Where(value => value.UserId == userId).Select(value => value.RoleId);
        var removed = await identity.RolePermissions
            .Where(value => value.PermissionCode == Permission && roleIds.Contains(value.RoleId))
            .ExecuteDeleteAsync();
        Assert.True(removed > 0);
    }

    private static async Task SeedAuditEventAsync(SqlServerRelationalApiFactory factory, Guid company, string correlation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        scope.ServiceProvider.GetRequiredService<IAuditEventCollector>().Enqueue(new AuditEventDraft(
            company,
            "identity.blg03-audit-fence.probe.v1",
            "IdentityBlg03AuditFenceProbe",
            Guid.NewGuid().ToString("N"),
            "Probe",
            null,
            null,
            AuditActorTypes.System,
            "blg03-audit-fence-test",
            Permission,
            ["SYSTEM_ADMINISTRATOR"],
            "{}",
            SensitiveAfter,
            "BLG-03 gerçek SQL kabul testi",
            null,
            AuditAiInvolvement.None,
            null,
            SourceChannels.System,
            null,
            null,
            correlation,
            null,
            $"idem-{Guid.NewGuid():N}"));
        await scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().SaveChangesAsync();
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        Assert.Matches("^goymen_erp_ci_[0-9a-f]{32}$", databaseName);
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"""
            IF DB_ID(@name) IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        drop.Parameters.AddWithValue("@name", databaseName);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Holds the audit query once, right after its rows were read from SQL.</summary>
    private sealed class AuditReadPause : DbCommandInterceptor
    {
        private int armed;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Volatile.Write(ref armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [audit].[AuditEvents]", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Reached.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
