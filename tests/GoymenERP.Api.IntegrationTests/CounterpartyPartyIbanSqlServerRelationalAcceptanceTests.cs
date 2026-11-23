using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FNS-D121 (K3): the cari IBAN list against real SQL Server — one save of row + command record + audit, the filtered unique
/// index (one active copy per party), the state check, FNS-D122 foreign IBAN storage with the TR rule kept in the check, company isolation of the owner source and the Down guard (THROW 52252).
/// The permission measurement is SENTETIK (registered names are granted to nobody).
/// </summary>
public sealed class CounterpartyPartyIbanSqlServerRelationalAcceptanceTests
{
    private const string Iban = "TR130006200000000000000002";

    [SqlServerFact]
    public async Task Add_and_deactivate_are_audited_one_active_copy_per_party_and_the_down_guard_refuses_after_rows()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using (var client = factory.CreateClient())
            using (var health = await client.GetAsync("/health/live")) health.EnsureSuccessStatusCode();
            var guard = Assert.IsType<SqlOperation>(new AddPartyIbans().DownOperations[0]).Sql;
            await ExecuteAsync(factory.DatabaseConnectionString, guard);
            var party = Guid.NewGuid();
            await using (var seed = factory.Services.CreateAsyncScope())
            {
                seed.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var db = seed.ServiceProvider.GetRequiredService<CounterpartyDbContext>();
                db.Parties.Add(new Party(party, PartyCode.Parse("CAR-900121"), PartyKind.LegalEntity, "SENTETIK IBAN Cari"));
                db.CompanyPartyAccounts.Add(new CompanyPartyAccount(Guid.NewGuid(), TestCompanyIds.CompanyA, party, PartyRole.Supplier));
                await db.SaveChangesAsync();
            }

            Guid first;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var service = Service(scope.ServiceProvider, TestCompanyIds.CompanyA);
                var added = await service.AddAsync(party, new(Guid.NewGuid(), Iban, "SENTETIK Banka", null), "SENTETIK-a",
                    "SENTETIK-iban", null, "Web", null, null, default);
                Assert.Equal(CounterpartyPartyIbanOutcomeV1.Succeeded, added.Outcome);
                first = added.Resource!.Id;
                Assert.True((await service.AddAsync(party, new(first, Iban, "SENTETIK Banka", null), "SENTETIK-a",
                    "SENTETIK-iban", null, "Web", null, null, default)).Replayed);
            }

            // The filtered unique index refuses a second active copy whatever the writer.
            var copy = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory.DatabaseConnectionString,
                "INSERT INTO counterparty.PartyIbans (CompanyId, Id, PartyId, Version, Iban, BankName, IsActive, AddedBy, AddedAtUtc) "
                + "SELECT CompanyId, NEWID(), PartyId, 1, Iban, BankName, 1, AddedBy, AddedAtUtc FROM counterparty.PartyIbans"));
            Assert.Contains(copy.Number, new[] { 2601, 2627 });

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var service = Service(scope.ServiceProvider, TestCompanyIds.CompanyA);
                Assert.Equal(CounterpartyPartyIbanOutcomeV1.Succeeded, (await service.DeactivateAsync(party, first,
                    new("1", "SENTETIK hesap kapandi"), "SENTETIK-d", "SENTETIK-iban", null, "Web", null, null, default)).Outcome);
            }

            await using (var foreign = factory.Services.CreateAsyncScope())
            {
                foreign.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyB);
                var source = foreign.ServiceProvider.GetRequiredService<ICounterpartyPartyIbanSourceV1>();
                Assert.Empty((await source.FindPartiesAsync([Iban], default))!);
            }

            // FNS-D122: a foreign IBAN (34-character column) is stored; the check keeps the TR rule (TR + 24 digits) for any writer.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var service = Service(scope.ServiceProvider, TestCompanyIds.CompanyA);
                var foreignIban = await service.AddAsync(party, new(Guid.NewGuid(), "GB82 WEST 1234 5698 7654 32", "SENTETIK Bank", null),
                    "SENTETIK-g", "SENTETIK-iban", null, "Web", null, null, default);
                Assert.Equal((CounterpartyPartyIbanOutcomeV1.Succeeded, "GB82WEST12345698765432"),
                    (foreignIban.Outcome, foreignIban.Resource!.Iban));
            }

            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory.DatabaseConnectionString,
                "INSERT INTO counterparty.PartyIbans (CompanyId, Id, PartyId, Version, Iban, BankName, IsActive, AddedBy, AddedAtUtc) "
                + "SELECT TOP (1) CompanyId, NEWID(), PartyId, 1, 'TR1300062000000000000000A2', BankName, 1, AddedBy, AddedAtUtc "
                + "FROM counterparty.PartyIbans"))).Number);
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(factory.DatabaseConnectionString,
                "UPDATE counterparty.PartyIbans SET DeactivationReason = NULL"))).Number);
            Assert.Equal(3, await ScalarAsync(factory.DatabaseConnectionString,
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'counterparty.party-iban.%'"));
            Assert.Equal(3, await ScalarAsync(factory.DatabaseConnectionString, "SELECT COUNT(*) FROM counterparty.PartyIbanCommands"));
            Assert.Equal(52252, (await Assert.ThrowsAsync<SqlException>(() =>
                ExecuteAsync(factory.DatabaseConnectionString, guard))).Number);
        }
        finally
        {
            var name = factory.DatabaseName;
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await ExecuteAsync(baseConnection,
                $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END;");
        }
    }

    private static ICounterpartyPartyIbanService Service(IServiceProvider provider, Guid company)
    {
        provider.GetRequiredService<ICompanyContext>().Set(company);
        return new CounterpartyPartyIbanService(provider.GetRequiredService<CounterpartyDbContext>(), new User(company),
            provider.GetRequiredService<ICompanyContext>(), new Authority(), provider.GetRequiredService<IAuditEventCollector>(),
            TimeProvider.System);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string> { "SENTETIK" }, new HashSet<Guid>(),
                new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-iban-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }
}
