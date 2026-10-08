using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyPartyDisplayBatchReadSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActivePartyA = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid ActivePartyB = Guid.Parse("71000000-0000-0000-0000-000000000002");
    private static readonly Guid InactiveParty = Guid.Parse("71000000-0000-0000-0000-000000000003");
    private static readonly Guid InactiveAccountParty = Guid.Parse("71000000-0000-0000-0000-000000000004");
    private static readonly Guid ForeignParty = Guid.Parse("71000000-0000-0000-0000-000000000005");

    [SqlServerFact]
    public async Task BatchRead_IsSingleQueryCompanyScopedFreshAndNonDisclosing()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            using var health = await healthClient.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await SeedAsync(factory.DatabaseConnectionString);

            var context = new TestCompanyContext(TestCompanyIds.CompanyA);
            var user = new TestCurrentUser(
                "sales-reader",
                new HashSet<Guid> { TestCompanyIds.CompanyA });
            var observer = new CapturingSqlObserver();
            await using var dbContext = CreateDbContext(
                factory.DatabaseConnectionString,
                context);
            var authorization = new TestAuthorizationService();
            var organization = new TestOrganizationService(TestCompanyIds.CompanyA);
            var service = new CounterpartyPartyDisplayBatchReadServiceV1(
                dbContext,
                context,
                user,
                authorization,
                organization,
                observer);

            var request = new CounterpartyPartyDisplayBatchReadRequestV1(
                TestCompanyIds.CompanyA,
                "sales-reader",
                [ForeignParty, ActivePartyB, Guid.NewGuid(), InactiveParty, ActivePartyA, InactiveAccountParty]);
            var answer = await service.ReadAuthorizedAsync(request);

            Assert.Equal(CounterpartyPartyDisplayBatchReadOutcomeV1.Found, answer.Outcome);
            Assert.Collection(
                answer.Items,
                item => Assert.Equal(
                    (ActivePartyA, "CAR-990001", "Aktif Cari A", (string?)"A Ticaret"),
                    (item.PartyId, item.Code, item.LegalName, item.TradeName)),
                item => Assert.Equal(
                    (ActivePartyB, "CAR-990002", "Aktif Cari B", (string?)null),
                    (item.PartyId, item.Code, item.LegalName, item.TradeName)));
            Assert.Equal(2, authorization.PermissionReads);
            var command = Assert.Single(observer.Commands);
            Assert.Contains("OPENJSON(@PartyIds)", command, StringComparison.Ordinal);
            Assert.Contains("a.[CompanyId] = @CompanyId", command, StringComparison.Ordinal);
            Assert.Contains("a.[IsActive] = 1", command, StringComparison.Ordinal);
            Assert.Contains("p.[IsActive] = 1", command, StringComparison.Ordinal);

            var beforeInvalid = observer.Commands.Count;
            var duplicate = await service.ReadAuthorizedAsync(request with
            {
                PartyIds = [ActivePartyA, ActivePartyA],
            });
            var tooMany = await service.ReadAuthorizedAsync(request with
            {
                PartyIds = Enumerable.Range(1, 101).Select(_ => Guid.NewGuid()).ToArray(),
            });
            var foreignContext = await service.ReadAuthorizedAsync(request with
            {
                CompanyId = TestCompanyIds.CompanyB,
            });
            Assert.All(
                new[] { duplicate, tooMany, foreignContext },
                closed =>
                {
                    Assert.Equal(CounterpartyPartyDisplayBatchReadOutcomeV1.Denied, closed.Outcome);
                    Assert.Empty(closed.Items);
                });
            Assert.Equal(beforeInvalid, observer.Commands.Count);

            var deniedAuthorization = new TestAuthorizationService { Granted = false };
            var denied = await new CounterpartyPartyDisplayBatchReadServiceV1(
                    dbContext,
                    context,
                    user,
                    deniedAuthorization,
                    organization,
                    observer)
                .ReadAuthorizedAsync(request);
            Assert.Equal(CounterpartyPartyDisplayBatchReadOutcomeV1.Denied, denied.Outcome);
            Assert.Empty(denied.Items);
            Assert.Equal(beforeInvalid, observer.Commands.Count);

            var staleContext = new TestCompanyContext(TestCompanyIds.CompanyA);
            var staleAuthorization = new TestAuthorizationService
            {
                OnPermissionRead = read =>
                {
                    if (read == 2)
                    {
                        staleContext.Set(TestCompanyIds.CompanyB);
                    }
                },
            };
            await using var staleDbContext = CreateDbContext(
                factory.DatabaseConnectionString,
                staleContext);
            var staleObserver = new CapturingSqlObserver();
            var stale = await new CounterpartyPartyDisplayBatchReadServiceV1(
                    staleDbContext,
                    staleContext,
                    user,
                    staleAuthorization,
                    organization,
                    staleObserver)
                .ReadAuthorizedAsync(request);
            Assert.Equal(CounterpartyPartyDisplayBatchReadOutcomeV1.Denied, stale.Outcome);
            Assert.Empty(stale.Items);
            Assert.Single(staleObserver.Commands);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static CounterpartyDbContext CreateDbContext(
        string connectionString,
        ICompanyContext context)
    {
        var options = new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new CounterpartyDbContext(options, context);
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@ActiveA, N'CAR-990001', N'LegalEntity', N'Aktif Cari A', N'A Ticaret', 1, 1, @Now, @Now),
                (@ActiveB, N'CAR-990002', N'LegalEntity', N'Aktif Cari B', NULL, 1, 1, @Now, @Now),
                (@InactiveParty, N'CAR-990003', N'LegalEntity', N'Pasif Cari', NULL, 0, 1, @Now, @Now),
                (@InactiveAccountParty, N'CAR-990004', N'LegalEntity', N'Pasif Hesap Cari', NULL, 1, 1, @Now, @Now),
                (@ForeignParty, N'CAR-990005', N'LegalEntity', N'Yabancı Şirket Cari', NULL, 1, 1, @Now, @Now);

            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (NEWID(), @CompanyA, @ActiveA, 0, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @ActiveB, 0, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @InactiveParty, 0, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @InactiveAccountParty, 0, NULL, 0, 1, @Now, @Now),
                (NEWID(), @CompanyB, @ForeignParty, 0, NULL, 1, 1, @Now, @Now);
            """;
        command.Parameters.AddWithValue("@ActiveA", ActivePartyA);
        command.Parameters.AddWithValue("@ActiveB", ActivePartyB);
        command.Parameters.AddWithValue("@InactiveParty", InactiveParty);
        command.Parameters.AddWithValue("@InactiveAccountParty", InactiveAccountParty);
        command.Parameters.AddWithValue("@ForeignParty", ForeignParty);
        command.Parameters.AddWithValue("@CompanyA", TestCompanyIds.CompanyA);
        command.Parameters.AddWithValue("@CompanyB", TestCompanyIds.CompanyB);
        command.Parameters.AddWithValue("@Now", new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(10, await command.ExecuteNonQueryAsync());
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved { get; private set; } = true;
        public Guid CompanyId { get; private set; } = companyId;

        public void Set(Guid value)
        {
            CompanyId = value;
            IsResolved = value != Guid.Empty;
        }
    }

    private sealed class TestCurrentUser(string actorId, IReadOnlySet<Guid> companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => companies;
        public bool CanAccessCompany(Guid companyId) => companies.Contains(companyId);
    }

    private sealed class TestAuthorizationService : IErpAuthorizationService
    {
        public bool Granted { get; init; } = true;
        public Action<int>? OnPermissionRead { get; init; }
        public int PermissionReads { get; private set; }

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(ErpPermissions.CounterpartyPartiesRead, permission);
            Assert.Equal(TestCompanyIds.CompanyA, companyId);
            PermissionReads++;
            OnPermissionRead?.Invoke(PermissionReads);
            return Task.FromResult(Granted
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(),
                    new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId,
            IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestOrganizationService(Guid activeCompanyId) : IOrganizationService
    {
        public Task<IReadOnlyList<CompanySummary>> GetAuthorizedCompaniesAsync(
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CompanySummary>>(
            [new CompanySummary(activeCompanyId, "TEST", "Test Company", false)]);

        public Task<IReadOnlyList<SiteSummary>> GetSitesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PageResult<LocationSummary>> GetLocationsAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LocationSummary?> GetLocationAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkCenterSummary>> GetWorkCentersAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WorkCenterDetail?> GetWorkCenterAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CommandExecutionResult> SetLocationActiveStateAsync(
            Guid id,
            SetLocationActiveState command,
            CommandMetadata metadata,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturingSqlObserver : ICounterpartyPartyDisplayBatchSqlObserver
    {
        public List<string> Commands { get; } = [];
        public void Observe(string commandText) => Commands.Add(commandText);
    }
}
