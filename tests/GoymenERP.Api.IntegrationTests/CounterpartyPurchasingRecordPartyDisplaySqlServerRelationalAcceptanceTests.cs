using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyPurchasingRecordPartyDisplaySqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActivePartyA = Guid.Parse("72000000-0000-0000-0000-000000000001");
    private static readonly Guid ActivePartyB = Guid.Parse("72000000-0000-0000-0000-000000000002");
    private static readonly Guid InactiveParty = Guid.Parse("72000000-0000-0000-0000-000000000003");
    private static readonly Guid InactiveAccountParty = Guid.Parse("72000000-0000-0000-0000-000000000004");
    private static readonly Guid ForeignParty = Guid.Parse("72000000-0000-0000-0000-000000000005");

    [SqlServerFact]
    public async Task Authorized_purchasing_record_projection_is_one_query_company_scoped_fresh_and_non_disclosing()
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
                "purchasing-reader",
                new HashSet<Guid> { TestCompanyIds.CompanyA });
            var observer = new CapturingSqlObserver();
            var organization = new TestOrganizationService(TestCompanyIds.CompanyA);
            await using var dbContext = CreateDbContext(factory.DatabaseConnectionString, context);
            var source = Source(dbContext, context, user, organization, observer);
            var request = new CounterpartyPurchasingRecordPartyDisplayRequestV1(
                TestCompanyIds.CompanyA,
                "purchasing-reader",
                [ForeignParty, ActivePartyB, Guid.NewGuid(), InactiveParty, ActivePartyA, InactiveAccountParty]);

            var answer = await source.ReadForAuthorizedPurchasingRecordsAsync(request);

            Assert.Equal(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Found, answer.Outcome);
            Assert.Collection(
                answer.Items,
                item => Assert.Equal(
                    (ActivePartyA, "CAR-991001", "Tedarikçi Cari A", (string?)"A Ticaret"),
                    (item.PartyId, item.Code, item.LegalName, item.TradeName)),
                item => Assert.Equal(
                    (ActivePartyB, "CAR-991002", "Tedarikçi Cari B", (string?)null),
                    (item.PartyId, item.Code, item.LegalName, item.TradeName)));
            Assert.Equal(2, organization.Reads);
            var command = Assert.Single(observer.Commands);
            Assert.Contains("OPENJSON(@PartyIds)", command, StringComparison.Ordinal);
            Assert.Contains("a.[CompanyId] = @CompanyId", command, StringComparison.Ordinal);
            Assert.Contains("a.[IsActive] = 1", command, StringComparison.Ordinal);
            Assert.Contains("p.[IsActive] = 1", command, StringComparison.Ordinal);

            var beforeDenied = observer.Commands.Count;
            var wrongCompany = await source.ReadForAuthorizedPurchasingRecordsAsync(request with
            {
                CompanyId = TestCompanyIds.CompanyB,
            });
            var wrongActor = await source.ReadForAuthorizedPurchasingRecordsAsync(request with
            {
                ActorId = "other-reader",
            });
            Assert.All(new[] { wrongCompany, wrongActor }, denied =>
            {
                Assert.Equal(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied, denied.Outcome);
                Assert.Empty(denied.Items);
            });
            Assert.Equal(beforeDenied, observer.Commands.Count);

            var staleContext = new TestCompanyContext(TestCompanyIds.CompanyA);
            var staleOrganization = new TestOrganizationService(TestCompanyIds.CompanyA)
            {
                OnRead = read =>
                {
                    if (read == 2)
                    {
                        staleContext.Set(TestCompanyIds.CompanyB);
                    }
                },
            };
            var staleObserver = new CapturingSqlObserver();
            await using var staleDbContext = CreateDbContext(factory.DatabaseConnectionString, staleContext);
            var stale = await Source(staleDbContext, staleContext, user, staleOrganization, staleObserver)
                .ReadForAuthorizedPurchasingRecordsAsync(request);
            Assert.Equal(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied, stale.Outcome);
            Assert.Empty(stale.Items);
            Assert.Single(staleObserver.Commands);

            var actorContext = new TestCompanyContext(TestCompanyIds.CompanyA);
            var mutableUser = new TestCurrentUser(
                "purchasing-reader",
                new HashSet<Guid> { TestCompanyIds.CompanyA });
            var actorOrganization = new TestOrganizationService(TestCompanyIds.CompanyA)
            {
                OnRead = read =>
                {
                    if (read == 2)
                    {
                        mutableUser.SetActor("replacement-reader");
                    }
                },
            };
            var actorObserver = new CapturingSqlObserver();
            await using var actorDbContext = CreateDbContext(factory.DatabaseConnectionString, actorContext);
            var actorDrift = await Source(actorDbContext, actorContext, mutableUser, actorOrganization, actorObserver)
                .ReadForAuthorizedPurchasingRecordsAsync(request);
            Assert.Equal(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied, actorDrift.Outcome);
            Assert.Empty(actorDrift.Items);
            Assert.Single(actorObserver.Commands);

            var accessContext = new TestCompanyContext(TestCompanyIds.CompanyA);
            var accessOrganization = new TestOrganizationService(TestCompanyIds.CompanyA);
            accessOrganization.OnRead = read =>
            {
                if (read == 2)
                {
                    accessOrganization.Authorized = false;
                }
            };
            var accessObserver = new CapturingSqlObserver();
            await using var accessDbContext = CreateDbContext(factory.DatabaseConnectionString, accessContext);
            var accessLost = await Source(accessDbContext, accessContext, user, accessOrganization, accessObserver)
                .ReadForAuthorizedPurchasingRecordsAsync(request);
            Assert.Equal(CounterpartyPurchasingRecordPartyDisplayOutcomeV1.Denied, accessLost.Outcome);
            Assert.Empty(accessLost.Items);
            Assert.Single(accessObserver.Commands);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static CounterpartyPurchasingRecordPartyDisplaySourceV1 Source(
        CounterpartyDbContext dbContext,
        ICompanyContext context,
        ICurrentUser user,
        IOrganizationService organization,
        ICounterpartyPurchasingRecordPartyDisplaySqlObserver observer) =>
        new(
            dbContext,
            context,
            user,
            organization,
            observer);

    private static CounterpartyDbContext CreateDbContext(string connectionString, ICompanyContext context) =>
        new(new DbContextOptionsBuilder<CounterpartyDbContext>().UseSqlServer(connectionString).Options, context);

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
                (@ActiveA, N'CAR-991001', N'LegalEntity', N'Tedarikçi Cari A', N'A Ticaret', 1, 1, @Now, @Now),
                (@ActiveB, N'CAR-991002', N'LegalEntity', N'Tedarikçi Cari B', NULL, 1, 1, @Now, @Now),
                (@InactiveParty, N'CAR-991003', N'LegalEntity', N'Pasif Cari', NULL, 0, 1, @Now, @Now),
                (@InactiveAccountParty, N'CAR-991004', N'LegalEntity', N'Pasif Hesap Cari', NULL, 1, 1, @Now, @Now),
                (@ForeignParty, N'CAR-991005', N'LegalEntity', N'Yabancı Şirket Cari', NULL, 1, 1, @Now, @Now);

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
        var builder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" };
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
        private string actor = actorId;
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => companies;
        public bool CanAccessCompany(Guid companyId) => companies.Contains(companyId);
        public void SetActor(string value) => actor = value;
    }

    private sealed class TestOrganizationService(Guid activeCompanyId) : IOrganizationService
    {
        public int Reads { get; private set; }
        public Action<int>? OnRead { get; set; }
        public bool Authorized { get; set; } = true;

        public Task<IReadOnlyList<CompanySummary>> GetAuthorizedCompaniesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            OnRead?.Invoke(Reads);
            return Task.FromResult<IReadOnlyList<CompanySummary>>(
                Authorized ? [new CompanySummary(activeCompanyId, "TEST", "Test Company", false)] : []);
        }

        public Task<IReadOnlyList<SiteSummary>> GetSitesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PageResult<LocationSummary>> GetLocationsAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<LocationSummary?> GetLocationAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorkCenterSummary>> GetWorkCentersAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<WorkCenterDetail?> GetWorkCenterAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CommandExecutionResult> SetLocationActiveStateAsync(
            Guid id,
            SetLocationActiveState command,
            CommandMetadata metadata,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturingSqlObserver : ICounterpartyPurchasingRecordPartyDisplaySqlObserver
    {
        public List<string> Commands { get; } = [];
        public void Observe(string commandText) => Commands.Add(commandText);
    }
}
