using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

// Real owner tables/query sources; synthetic caller authority only. Runtime SQL evidence remains NOT_RUN.
public sealed class CounterpartyProductPackageCustomerSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Customer = Guid.Parse("72000000-0000-4000-8000-000000000001");
    private static readonly Guid Supplier = Guid.Parse("72000000-0000-4000-8000-000000000002");
    private static readonly Guid InactiveParty = Guid.Parse("72000000-0000-4000-8000-000000000003");
    private static readonly Guid InactiveAccount = Guid.Parse("72000000-0000-4000-8000-000000000004");
    private static readonly Guid Foreign = Guid.Parse("72000000-0000-4000-8000-000000000005");
    private static readonly Guid Account = Guid.Parse("73000000-0000-4000-8000-000000000001");
    private const string Actor = "SENTETIK-package-customer";

    [SqlServerFact]
    public async Task Requested_customers_require_actual_active_party_company_account_and_customer_role_in_one_statement()
    {
        await using var run = await Fixture.StartAsync(); var context = new Context(); var probe = new ReadProbe();
        await using var database = run.Database(context, probe); var gate = new Gate();
        var source = new CounterpartyProductPackageStandardCustomerSourceV1(database, context, new User(), gate);
        var answer = await source.ReadAuthorizedAsync(new(TestCompanyIds.CompanyA, Actor,
            [Foreign, InactiveAccount, Supplier, Customer, InactiveParty, Guid.NewGuid()]));
        Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Found, answer.Outcome);
        var item = Assert.Single(answer.Items);
        Assert.Equal((TestCompanyIds.CompanyA, Customer, Account), (item.CompanyId, item.PartyId, item.CompanyAccountId));
        Assert.Equal(("7", "11"), (item.PartyVersion, item.AccountVersion));
        Assert.True(item.PartyIsActive); Assert.True(item.AccountIsActive); Assert.True(item.HasCustomerRole);
        Assert.Equal(("CAR-990011", "SENTETIK müşteri", "SENTETIK ticari müşteri"), (item.Code, item.LegalName, item.TradeName));
        Assert.Single(probe.Commands); Assert.Equal(2, gate.Calls);
        Assert.False(database.ChangeTracker.HasChanges()); Assert.Empty(database.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task Fresh_permission_or_identity_loss_after_actual_query_never_releases_the_already_read_customer()
    {
        await using var run = await Fixture.StartAsync();
        var context = new Context(); var user = new User(); var probe = new ReadProbe();
        await using var database = run.Database(context, probe);
        var revoked = new Gate { BeforeReturn = (permission, calls) => calls == 1 && permission == ErpPermissions.ProductPackageStandardsManage };
        var denied = await new CounterpartyProductPackageStandardCustomerSourceV1(database, context, user, revoked)
            .ReadAuthorizedAsync(new(TestCompanyIds.CompanyA, Actor, [Customer]));
        Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied, denied.Outcome); Assert.Empty(denied.Items); Assert.Single(probe.Commands);

        probe.Commands.Clear(); var drift = new Gate { BeforeReturn = (_, calls) => { if (calls == 2) user.UserId = "SENTETIK-other"; return true; } };
        var changed = await new CounterpartyProductPackageStandardCustomerSourceV1(database, context, user, drift)
            .ReadAuthorizedAsync(new(TestCompanyIds.CompanyA, Actor, [Customer]));
        Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied, changed.Outcome); Assert.Empty(changed.Items); Assert.Single(probe.Commands);
    }

    [SqlServerFact]
    public async Task Owner_read_ignores_tracked_pending_roles_without_flushing_or_clearing_and_observes_real_committed_revisions()
    {
        await using var run = await Fixture.StartAsync(); var context = new Context(); var probe = new ReadProbe();
        await using var database = run.Database(context, probe);
        var tracked = await database.CompanyPartyAccounts.SingleAsync(value => value.Id == Account);
        tracked.SetRoles(PartyRole.Supplier); Assert.True(database.ChangeTracker.HasChanges()); probe.Commands.Clear();
        var source = new CounterpartyProductPackageStandardCustomerSourceV1(database, context, new User(), new Gate());
        var request = new CounterpartyProductPackageStandardCustomerRequestV1(TestCompanyIds.CompanyA, Actor, [Customer]);
        var original = await source.ReadAuthorizedAsync(request);
        Assert.Equal("11", Assert.Single(original.Items).AccountVersion); Assert.Single(probe.Commands);
        Assert.Equal(EntityState.Modified, database.Entry(tracked).State); Assert.Equal(PartyRole.Supplier, tracked.Roles);

        await run.ChangeRoleAsync(PartyRole.Supplier, 12); probe.Commands.Clear();
        var removed = await source.ReadAuthorizedAsync(request);
        Assert.Equal(CounterpartyProductPackageStandardCustomerOutcomeV1.Found, removed.Outcome); Assert.Empty(removed.Items); Assert.Single(probe.Commands);
        await run.ChangeRoleAsync(PartyRole.Customer | PartyRole.Supplier, 13); probe.Commands.Clear();
        var restored = await source.ReadAuthorizedAsync(request);
        Assert.Equal("13", Assert.Single(restored.Items).AccountVersion); Assert.Single(probe.Commands);
        Assert.Equal(EntityState.Modified, database.Entry(tracked).State);
    }

    private sealed class Fixture(SqlServerRelationalApiFactory factory, string baseConnection) : IAsyncDisposable
    {
        public static async Task<Fixture> StartAsync()
        {
            var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var fixture = new Fixture(new SqlServerRelationalApiFactory(connection), connection);
            try
            {
                using var client = fixture.factory.CreateClient(); using var health = await client.GetAsync("/health/live"); health.EnsureSuccessStatusCode();
                await fixture.SeedAsync(); return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        private readonly SqlServerRelationalApiFactory factory = factory;
        public CounterpartyDbContext Database(ICompanyContext context, ReadProbe probe) => new(
            new DbContextOptionsBuilder<CounterpartyDbContext>().UseSqlServer(factory.DatabaseConnectionString).AddInterceptors(probe).Options, context);
        private async Task SeedAsync()
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO counterparty.Parties
                    (Id,Code,Kind,LegalName,TradeName,IsActive,Version,CreatedAtUtc,UpdatedAtUtc)
                VALUES (@Customer,N'CAR-990011',N'LegalEntity',N'SENTETIK müşteri',N'SENTETIK ticari müşteri',1,7,@Now,@Now),
                    (@Supplier,N'CAR-990012',N'LegalEntity',N'SENTETIK tedarikçi',NULL,1,1,@Now,@Now),
                    (@InactiveParty,N'CAR-990013',N'LegalEntity',N'SENTETIK pasif cari',NULL,0,1,@Now,@Now),
                    (@InactiveAccount,N'CAR-990014',N'LegalEntity',N'SENTETIK pasif hesap',NULL,1,1,@Now,@Now),
                    (@Foreign,N'CAR-990015',N'LegalEntity',N'SENTETIK diğer şirket',NULL,1,1,@Now,@Now);
                INSERT INTO counterparty.CompanyPartyAccounts
                    (Id,CompanyId,PartyId,Roles,IsActive,Version,CreatedAtUtc,UpdatedAtUtc)
                VALUES (@Account,@CompanyA,@Customer,3,1,11,@Now,@Now),
                    (NEWID(),@CompanyA,@Supplier,2,1,1,@Now,@Now),
                    (NEWID(),@CompanyA,@InactiveParty,1,1,1,@Now,@Now),
                    (NEWID(),@CompanyA,@InactiveAccount,1,0,1,@Now,@Now),
                    (NEWID(),@CompanyB,@Foreign,1,1,1,@Now,@Now);
                """;
            command.Parameters.AddWithValue("@Customer", Customer); command.Parameters.AddWithValue("@Supplier", Supplier);
            command.Parameters.AddWithValue("@InactiveParty", InactiveParty); command.Parameters.AddWithValue("@InactiveAccount", InactiveAccount);
            command.Parameters.AddWithValue("@Foreign", Foreign); command.Parameters.AddWithValue("@Account", Account);
            command.Parameters.AddWithValue("@CompanyA", TestCompanyIds.CompanyA); command.Parameters.AddWithValue("@CompanyB", TestCompanyIds.CompanyB);
            command.Parameters.AddWithValue("@Now", new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
            Assert.Equal(10, await command.ExecuteNonQueryAsync());
        }
        public async Task ChangeRoleAsync(PartyRole role, long version)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString); await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE counterparty.CompanyPartyAccounts SET Roles=@Role,Version=@Version WHERE Id=@Id AND CompanyId=@Company";
            command.Parameters.AddWithValue("@Role", (int)role); command.Parameters.AddWithValue("@Version", version);
            command.Parameters.AddWithValue("@Id", Account); command.Parameters.AddWithValue("@Company", TestCompanyIds.CompanyA);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        public async ValueTask DisposeAsync()
        {
            var name = factory.DatabaseName; await factory.DisposeAsync();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "\\Agoymen_erp_ci_[a-f0-9]{32}\\z")) throw new InvalidOperationException("Synthetic database identity required");
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await connection.OpenAsync(); await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(@Name) IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
            command.Parameters.AddWithValue("@Name", name); await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class ReadProbe : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
    private sealed class Context : ICompanyContext
    { public bool IsResolved => true; public Guid CompanyId { get; private set; } = TestCompanyIds.CompanyA; public void Set(Guid companyId) => CompanyId = companyId; }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId { get; set; } = Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class Gate : IErpAuthorityRevalidationService
    {
        public int Calls { get; private set; } public Func<string, int, bool>? BeforeReturn { get; init; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++; var granted = BeforeReturn?.Invoke(permission, Calls) ?? permission == ErpPermissions.ProductPackageStandardsManage;
            return Task.FromResult(granted && companyId == TestCompanyIds.CompanyA
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
        }
    }
}
