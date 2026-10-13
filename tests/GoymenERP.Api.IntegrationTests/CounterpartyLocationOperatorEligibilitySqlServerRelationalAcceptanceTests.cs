using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyLocationOperatorEligibilitySqlServerRelationalAcceptanceTests
{
    private static readonly Guid EligibleParty = Guid.Parse("73000000-0000-4000-8000-000000000001");
    private static readonly Guid TollingOnlyParty = Guid.Parse("73000000-0000-4000-8000-000000000002");
    private static readonly Guid InactiveParty = Guid.Parse("73000000-0000-4000-8000-000000000003");
    private static readonly Guid InactiveAccountParty = Guid.Parse("73000000-0000-4000-8000-000000000004");
    private static readonly Guid ForeignParty = Guid.Parse("73000000-0000-4000-8000-000000000005");

    [SqlServerFact]
    public async Task Exact_location_operator_source_is_company_scoped_supplier_only_fresh_and_read_only()
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

            var context = new MutableCompanyContext(TestCompanyIds.CompanyA);
            var user = new MutableCurrentUser(
                "organization-location-writer",
                new HashSet<Guid> { TestCompanyIds.CompanyA });
            var observer = new CapturingObserver();
            await using var dbContext = CreateDbContext(factory.DatabaseConnectionString, context);
            var source = Source(dbContext, context, user, observer);

            var eligible = await source.ReadForLocationOperatorAsync(Request(EligibleParty));
            Assert.Equal(CounterpartyLocationOperatorEligibilityOutcomeV1.Eligible, eligible.Outcome);
            Assert.NotNull(eligible.Operator);
            Assert.Equal(
                (EligibleParty, "CAR-992001", "Uygun Tedarikçi", (string?)"Uygun Ticaret", "2", "3"),
                (eligible.Operator.PartyId,
                    eligible.Operator.Code,
                    eligible.Operator.LegalName,
                    eligible.Operator.TradeName,
                    eligible.Operator.PartyVersion,
                    eligible.Operator.AccountVersion));
            Assert.NotEqual(Guid.Empty, eligible.Operator.CompanyAccountId);

            var tollingOnly = await source.ReadForLocationOperatorAsync(Request(TollingOnlyParty));
            var inactiveParty = await source.ReadForLocationOperatorAsync(Request(InactiveParty));
            var inactiveAccount = await source.ReadForLocationOperatorAsync(Request(InactiveAccountParty));
            Assert.All(new[] { tollingOnly, inactiveParty, inactiveAccount }, answer =>
            {
                Assert.Equal(CounterpartyLocationOperatorEligibilityOutcomeV1.Ineligible, answer.Outcome);
                Assert.Null(answer.Operator);
            });

            var foreign = await source.ReadForLocationOperatorAsync(Request(ForeignParty));
            var missing = await source.ReadForLocationOperatorAsync(Request(Guid.NewGuid()));
            Assert.All(new[] { foreign, missing }, answer =>
            {
                Assert.Equal(CounterpartyLocationOperatorEligibilityOutcomeV1.NotFound, answer.Outcome);
                Assert.Null(answer.Operator);
            });

            var queriesBeforeDenied = observer.Commands.Count;
            var wrongCompany = await source.ReadForLocationOperatorAsync(
                Request(EligibleParty) with { CompanyId = TestCompanyIds.CompanyB });
            var wrongActor = await source.ReadForLocationOperatorAsync(
                Request(EligibleParty) with { ActorId = "other-writer" });
            Assert.All(new[] { wrongCompany, wrongActor }, answer =>
            {
                Assert.Equal(CounterpartyLocationOperatorEligibilityOutcomeV1.Denied, answer.Outcome);
                Assert.Null(answer.Operator);
            });
            Assert.Equal(queriesBeforeDenied, observer.Commands.Count);

            var driftingContext = new MutableCompanyContext(TestCompanyIds.CompanyA);
            var contextObserver = new CapturingObserver
            {
                OnObserve = () => driftingContext.Set(TestCompanyIds.CompanyB),
            };
            await using var contextDb = CreateDbContext(factory.DatabaseConnectionString, driftingContext);
            var contextDrift = await Source(contextDb, driftingContext, user, contextObserver)
                .ReadForLocationOperatorAsync(Request(EligibleParty));
            Assert.Equal(CounterpartyLocationOperatorEligibilityOutcomeV1.Denied, contextDrift.Outcome);
            Assert.Null(contextDrift.Operator);
            Assert.Single(contextObserver.Commands);

            var driftingUser = new MutableCurrentUser(
                "organization-location-writer",
                new HashSet<Guid> { TestCompanyIds.CompanyA });
            var actorContext = new MutableCompanyContext(TestCompanyIds.CompanyA);
            var actorObserver = new CapturingObserver
            {
                OnObserve = () => driftingUser.SetActor("replacement-writer"),
            };
            await using var actorDb = CreateDbContext(factory.DatabaseConnectionString, actorContext);
            var actorDrift = await Source(actorDb, actorContext, driftingUser, actorObserver)
                .ReadForLocationOperatorAsync(Request(EligibleParty));
            Assert.Equal(CounterpartyLocationOperatorEligibilityOutcomeV1.Denied, actorDrift.Outcome);
            Assert.Null(actorDrift.Operator);
            Assert.Single(actorObserver.Commands);

            var failureContext = new MutableCompanyContext(TestCompanyIds.CompanyA);
            await using var failureDb = CreateDbContext(factory.DatabaseConnectionString, failureContext);
            var unavailable = await Source(failureDb, failureContext, user, new ThrowingObserver())
                .ReadForLocationOperatorAsync(Request(EligibleParty));
            Assert.Equal(CounterpartyLocationOperatorEligibilityOutcomeV1.Unavailable, unavailable.Outcome);
            Assert.Null(unavailable.Operator);

            Assert.Equal(6, observer.Commands.Count);
            Assert.All(observer.Commands, command =>
            {
                Assert.StartsWith("SELECT", command.TrimStart(), StringComparison.Ordinal);
                Assert.Contains("a.[CompanyId] = @CompanyId", command, StringComparison.Ordinal);
                Assert.Contains("a.[PartyId] = @PartyId", command, StringComparison.Ordinal);
                Assert.DoesNotContain("INSERT", command, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("UPDATE", command, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("DELETE", command, StringComparison.OrdinalIgnoreCase);
            });
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static CounterpartyLocationOperatorEligibilityRequestV1 Request(Guid partyId) =>
        new(TestCompanyIds.CompanyA, "organization-location-writer", partyId);

    private static CounterpartyLocationOperatorEligibilitySourceV1 Source(
        CounterpartyDbContext dbContext,
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        ICounterpartyLocationOperatorEligibilitySqlObserver observer) =>
        new(dbContext, companyContext, currentUser, observer);

    private static CounterpartyDbContext CreateDbContext(
        string connectionString,
        ICompanyContext companyContext) =>
        new(
            new DbContextOptionsBuilder<CounterpartyDbContext>()
                .UseSqlServer(connectionString)
                .Options,
            companyContext);

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
                (@Eligible, N'CAR-992001', N'LegalEntity', N'Uygun Tedarikçi', N'Uygun Ticaret', 1, 2, @Now, @Now),
                (@TollingOnly, N'CAR-992002', N'LegalEntity', N'Yalnız Fason', NULL, 1, 1, @Now, @Now),
                (@InactiveParty, N'CAR-992003', N'LegalEntity', N'Pasif Tedarikçi', NULL, 0, 1, @Now, @Now),
                (@InactiveAccount, N'CAR-992004', N'LegalEntity', N'Pasif Hesap', NULL, 1, 1, @Now, @Now),
                (@Foreign, N'CAR-992005', N'LegalEntity', N'Yabancı Şirket', NULL, 1, 1, @Now, @Now);

            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (NEWID(), @CompanyA, @Eligible, @Supplier, NULL, 1, 3, @Now, @Now),
                (NEWID(), @CompanyA, @TollingOnly, @Tolling, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @InactiveParty, @Supplier, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @InactiveAccount, @Supplier, NULL, 0, 1, @Now, @Now),
                (NEWID(), @CompanyB, @Foreign, @Supplier, NULL, 1, 1, @Now, @Now);
            """;
        command.Parameters.AddWithValue("@Eligible", EligibleParty);
        command.Parameters.AddWithValue("@TollingOnly", TollingOnlyParty);
        command.Parameters.AddWithValue("@InactiveParty", InactiveParty);
        command.Parameters.AddWithValue("@InactiveAccount", InactiveAccountParty);
        command.Parameters.AddWithValue("@Foreign", ForeignParty);
        command.Parameters.AddWithValue("@CompanyA", TestCompanyIds.CompanyA);
        command.Parameters.AddWithValue("@CompanyB", TestCompanyIds.CompanyB);
        command.Parameters.AddWithValue("@Supplier", (int)PartyRole.Supplier);
        command.Parameters.AddWithValue("@Tolling", (int)PartyRole.Tolling);
        command.Parameters.AddWithValue(
            "@Now",
            new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
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

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class MutableCurrentUser(string actorId, IReadOnlySet<Guid> companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; private set; } = actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => companies;
        public bool CanAccessCompany(Guid companyId) => companies.Contains(companyId);
        public void SetActor(string actor) => UserId = actor;
    }

    private sealed class CapturingObserver : ICounterpartyLocationOperatorEligibilitySqlObserver
    {
        public List<string> Commands { get; } = [];
        public Action? OnObserve { get; init; }

        public void Observe(string commandText)
        {
            Commands.Add(commandText);
            OnObserve?.Invoke();
        }
    }

    private sealed class ThrowingObserver : ICounterpartyLocationOperatorEligibilitySqlObserver
    {
        public void Observe(string commandText) => throw new InvalidOperationException("simulated outage");
    }
}
