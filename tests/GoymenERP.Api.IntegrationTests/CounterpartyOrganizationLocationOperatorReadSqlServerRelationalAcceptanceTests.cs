using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyOrganizationLocationOperatorReadSqlServerRelationalAcceptanceTests
{
    private static readonly Guid EligibleOne = Guid.Parse("74000000-0000-4000-8000-000000000001");
    private static readonly Guid EligibleTwo = Guid.Parse("74000000-0000-4000-8000-000000000002");
    private static readonly Guid EligibleThree = Guid.Parse("74000000-0000-4000-8000-000000000003");
    private static readonly Guid InactiveParty = Guid.Parse("74000000-0000-4000-8000-000000000004");
    private static readonly Guid InactiveAccount = Guid.Parse("74000000-0000-4000-8000-000000000005");
    private static readonly Guid RemovedSupplier = Guid.Parse("74000000-0000-4000-8000-000000000006");
    private static readonly Guid ForeignSupplier = Guid.Parse("74000000-0000-4000-8000-000000000007");
    private static readonly Guid LocationOne = Guid.Parse("75000000-0000-4000-8000-000000000001");
    private static readonly Guid LocationTwo = Guid.Parse("75000000-0000-4000-8000-000000000002");
    private static readonly Guid LocationThree = Guid.Parse("75000000-0000-4000-8000-000000000003");
    private const string Actor = "organization-location-writer";
    private const string Session = "organization.location.operator-edit.v1";

    [SqlServerFact]
    public async Task Authorized_operator_disclosure_and_supplier_candidates_are_bounded_fresh_and_read_only()
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
            var before = await ReadCountsAsync(factory.DatabaseConnectionString);

            var context = new MutableCompanyContext(TestCompanyIds.CompanyA);
            var user = new MutableCurrentUser(Actor, new HashSet<Guid> { TestCompanyIds.CompanyA });
            var authority = new MutableAuthority(
                readLocations: new HashSet<Guid> { LocationOne, LocationTwo, LocationThree },
                writeLocations: new HashSet<Guid> { LocationOne });
            var observer = new CapturingObserver();
            await using var dbContext = CreateDbContext(factory.DatabaseConnectionString, context);
            using var cursorCodec = CreateCursorCodec();
            var source = Source(dbContext, context, user, authority, cursorCodec, observer);

            var emptyBindings = await source.ReadAuthorizedDisplaysAsync(new(
                TestCompanyIds.CompanyA,
                Actor,
                []));
            var duplicateBindings = await source.ReadAuthorizedDisplaysAsync(new(
                TestCompanyIds.CompanyA,
                Actor,
                [new(LocationOne, EligibleOne), new(LocationOne, EligibleTwo)]));
            var nullBinding = await source.ReadAuthorizedDisplaysAsync(new(
                TestCompanyIds.CompanyA,
                Actor,
                [null!]));
            Assert.All(new[] { emptyBindings, duplicateBindings, nullBinding }, answer =>
            {
                Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable, answer.Outcome);
                Assert.Empty(answer.Items);
            });

            var wrongContextMalformed = await source.ReadAuthorizedDisplaysAsync(new(
                Guid.Empty,
                Actor,
                [null!]));
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, wrongContextMalformed.Outcome);
            Assert.Empty(wrongContextMalformed.Items);
            Assert.Empty(observer.Commands);

            var display = await source.ReadAuthorizedDisplaysAsync(new(
                TestCompanyIds.CompanyA,
                Actor,
                [
                    new(LocationOne, EligibleOne),
                    new(LocationTwo, InactiveParty),
                    new(LocationThree, RemovedSupplier),
                ]));
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success, display.Outcome);
            Assert.Collection(display.Items.OrderBy(item => item.LocationId),
                item => Assert.Equal(
                    (LocationOne, EligibleOne, "CAR-000100", "Birinci Tedarikçi", (string?)"Birinci Ticaret", true),
                    (item.LocationId, item.PartyId, item.Code, item.LegalName, item.TradeName, item.IsCurrentlyEligible)),
                item => Assert.Equal(
                    (LocationTwo, InactiveParty, "CAR-000300", "Tarihsel Pasif Taraf", (string?)null, false),
                    (item.LocationId, item.PartyId, item.Code, item.LegalName, item.TradeName, item.IsCurrentlyEligible)),
                item => Assert.Equal(
                    (LocationThree, RemovedSupplier, "CAR-000500", "Rolü Kaldırılmış", (string?)null, false),
                    (item.LocationId, item.PartyId, item.Code, item.LegalName, item.TradeName, item.IsCurrentlyEligible)));

            var missing = await source.ReadAuthorizedDisplaysAsync(new(
                TestCompanyIds.CompanyA,
                Actor,
                [new(LocationOne, Guid.Parse("74000000-0000-4000-8000-000000000099"))]));
            var foreign = await source.ReadAuthorizedDisplaysAsync(new(
                TestCompanyIds.CompanyA,
                Actor,
                [new(LocationOne, ForeignSupplier)]));
            Assert.All(new[] { missing, foreign }, answer =>
            {
                Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable, answer.Outcome);
                Assert.Empty(answer.Items);
            });

            var first = await source.SearchAuthorizedCandidatesAsync(Candidate(LocationOne, pageSize: 1));
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success, first.Outcome);
            Assert.True(first.HasMore);
            Assert.NotNull(first.NextCursor);
            Assert.Equal(EligibleOne, Assert.Single(first.Items).PartyId);

            var second = await source.SearchAuthorizedCandidatesAsync(
                Candidate(LocationOne, pageSize: 1) with { Cursor = first.NextCursor });
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success, second.Outcome);
            Assert.Equal(EligibleTwo, Assert.Single(second.Items).PartyId);

            var all = await source.SearchAuthorizedCandidatesAsync(Candidate(LocationOne, pageSize: 25));
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success, all.Outcome);
            Assert.Equal([EligibleOne, EligibleTwo, EligibleThree], all.Items.Select(item => item.PartyId));
            Assert.DoesNotContain(all.Items, item =>
                item.PartyId == InactiveParty
                || item.PartyId == InactiveAccount
                || item.PartyId == RemovedSupplier
                || item.PartyId == ForeignSupplier);

            foreach (var scopeOrigin in Enum.GetValues<TestAuthorityScopeOrigin>())
            {
                var scopedAuthority = new MutableAuthority(
                    readLocations: new HashSet<Guid> { LocationOne },
                    writeLocations: new HashSet<Guid> { LocationOne },
                    scopeOrigin);
                var scopedObserver = new CapturingObserver();
                await using var scopedDb = CreateDbContext(factory.DatabaseConnectionString, context);
                using var scopedCodec = CreateCursorCodec();
                var scopedCandidate = await Source(
                        scopedDb,
                        context,
                        user,
                        scopedAuthority,
                        scopedCodec,
                        scopedObserver)
                    .SearchAuthorizedCandidatesAsync(Candidate(LocationOne, pageSize: 25));
                Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success, scopedCandidate.Outcome);
                Assert.NotEmpty(scopedCandidate.Items);
                Assert.Equal(2, scopedAuthority.Requests.Count);
                Assert.All(scopedAuthority.Requests, request =>
                    Assert.Equal(OrganizationLocationAuthorityAccessV1.Write, request.Access));
                Assert.Equal(scopeOrigin, scopedAuthority.ScopeOrigin);
            }

            var queriesBeforeCursorDrift = observer.Commands.Count;
            var cursorDrift = await source.SearchAuthorizedCandidatesAsync(
                Candidate(LocationTwo, pageSize: 1) with { Cursor = first.NextCursor });
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, cursorDrift.Outcome);
            Assert.Equal(queriesBeforeCursorDrift, observer.Commands.Count);

            var queryCollision = await source.SearchAuthorizedCandidatesAsync(
                Candidate(LocationOne, pageSize: 1) with { Query = "-", Cursor = first.NextCursor });
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.InvalidCursor, queryCollision.Outcome);
            Assert.Equal(queriesBeforeCursorDrift, observer.Commands.Count);

            var displayScope = new MutableAuthority(
                readLocations: new HashSet<Guid> { LocationOne },
                writeLocations: new HashSet<Guid> { LocationOne });
            var displayScopeObserver = new CapturingObserver();
            await using var displayScopeDb = CreateDbContext(factory.DatabaseConnectionString, context);
            using var displayScopeCodec = CreateCursorCodec();
            var deniedDisplay = await Source(
                    displayScopeDb,
                    context,
                    user,
                    displayScope,
                    displayScopeCodec,
                    displayScopeObserver)
                .ReadAuthorizedDisplaysAsync(new(
                    TestCompanyIds.CompanyA,
                    Actor,
                    [new(LocationTwo, EligibleTwo)]));
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, deniedDisplay.Outcome);
            Assert.Empty(deniedDisplay.Items);
            Assert.Empty(displayScopeObserver.Commands);

            var deniedCandidateAuthority = new MutableAuthority(
                readLocations: new HashSet<Guid> { LocationOne },
                writeLocations: new HashSet<Guid>());
            var deniedCandidateObserver = new CapturingObserver();
            await using var deniedCandidateDb = CreateDbContext(factory.DatabaseConnectionString, context);
            using var deniedCandidateCodec = CreateCursorCodec();
            var deniedCandidate = await Source(
                    deniedCandidateDb,
                    context,
                    user,
                    deniedCandidateAuthority,
                    deniedCandidateCodec,
                    deniedCandidateObserver)
                .SearchAuthorizedCandidatesAsync(Candidate(LocationOne, pageSize: 25));
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, deniedCandidate.Outcome);
            Assert.Empty(deniedCandidate.Items);
            Assert.Empty(deniedCandidateObserver.Commands);

            var driftingAuthority = new MutableAuthority(
                readLocations: new HashSet<Guid> { LocationOne },
                writeLocations: new HashSet<Guid> { LocationOne });
            var driftObserver = new CapturingObserver
            {
                OnObserve = () => driftingAuthority.Revoke(OrganizationLocationAuthorityAccessV1.Write),
            };
            await using var driftDb = CreateDbContext(factory.DatabaseConnectionString, context);
            using var driftCodec = CreateCursorCodec();
            var authorityDrift = await Source(driftDb, context, user, driftingAuthority, driftCodec, driftObserver)
                .SearchAuthorizedCandidatesAsync(Candidate(LocationOne, pageSize: 25));
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, authorityDrift.Outcome);
            Assert.Empty(authorityDrift.Items);
            Assert.Single(driftObserver.Commands);

            await using var outageDb = CreateDbContext(factory.DatabaseConnectionString, context);
            using var outageCodec = CreateCursorCodec();
            var outage = await Source(outageDb, context, user, authority, outageCodec, new ThrowingObserver())
                .SearchAuthorizedCandidatesAsync(Candidate(LocationOne, pageSize: 25));
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable, outage.Outcome);
            Assert.Empty(outage.Items);

            Assert.Equal(6, observer.Commands.Count);
            Assert.Equal(3, observer.Commands.Count(command =>
                command.Operation == CounterpartyOrganizationLocationOperatorReadSqlOperation.Display));
            Assert.Equal(3, observer.Commands.Count(command =>
                command.Operation == CounterpartyOrganizationLocationOperatorReadSqlOperation.Candidates));
            Assert.All(observer.Commands, command =>
            {
                Assert.StartsWith("SELECT", command.CommandText.TrimStart(), StringComparison.Ordinal);
                Assert.Contains("@CompanyId", command.CommandText, StringComparison.Ordinal);
                Assert.DoesNotContain("INSERT", command.CommandText, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("UPDATE", command.CommandText, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("DELETE", command.CommandText, StringComparison.OrdinalIgnoreCase);
            });

            var after = await ReadCountsAsync(factory.DatabaseConnectionString);
            Assert.Equal(before, after);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static CounterpartyOrganizationLocationOperatorCandidateRequestV1 Candidate(
        Guid locationId,
        int pageSize) =>
        new(TestCompanyIds.CompanyA, Actor, Session, locationId, null, pageSize, null);

    private static CounterpartyOrganizationLocationOperatorReadSourceV1 Source(
        CounterpartyDbContext dbContext,
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IOrganizationLocationAuthorityRevalidationV1 authority,
        ICounterpartyCursorCodec cursorCodec,
        ICounterpartyOrganizationLocationOperatorReadSqlObserver observer) =>
        new(dbContext, companyContext, currentUser, authority, cursorCodec, TimeProvider.System, observer);

    private static CounterpartyCursorCodec CreateCursorCodec() =>
        new(TimeProvider.System, new CounterpartyCursorRuntimeOptions(
            false,
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray())));

    private static CounterpartyDbContext CreateDbContext(string connectionString, ICompanyContext companyContext) =>
        new(new DbContextOptionsBuilder<CounterpartyDbContext>().UseSqlServer(connectionString).Options, companyContext);

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
                (@EligibleOne, N'CAR-000100', N'LegalEntity', N'Birinci Tedarikçi', N'Birinci Ticaret', 1, 1, @Now, @Now),
                (@EligibleTwo, N'CAR-000101', N'LegalEntity', N'İkinci Tedarikçi', NULL, 1, 1, @Now, @Now),
                (@EligibleThree, N'CAR-000200', N'LegalEntity', N'Üçüncü Tedarikçi', NULL, 1, 1, @Now, @Now),
                (@InactiveParty, N'CAR-000300', N'LegalEntity', N'Tarihsel Pasif Taraf', NULL, 0, 1, @Now, @Now),
                (@InactiveAccount, N'CAR-000400', N'LegalEntity', N'Pasif Şirket Hesabı', NULL, 1, 1, @Now, @Now),
                (@RemovedSupplier, N'CAR-000500', N'LegalEntity', N'Rolü Kaldırılmış', NULL, 1, 1, @Now, @Now),
                (@ForeignSupplier, N'CAR-000600', N'LegalEntity', N'Yabancı Şirket Tedarikçisi', NULL, 1, 1, @Now, @Now);

            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (NEWID(), @CompanyA, @EligibleOne, @Supplier, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @EligibleTwo, @Supplier, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @EligibleThree, @Supplier, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @InactiveParty, @Supplier, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyA, @InactiveAccount, @Supplier, NULL, 0, 1, @Now, @Now),
                (NEWID(), @CompanyA, @RemovedSupplier, @Tolling, NULL, 1, 1, @Now, @Now),
                (NEWID(), @CompanyB, @ForeignSupplier, @Supplier, NULL, 1, 1, @Now, @Now);
            """;
        command.Parameters.AddWithValue("@EligibleOne", EligibleOne);
        command.Parameters.AddWithValue("@EligibleTwo", EligibleTwo);
        command.Parameters.AddWithValue("@EligibleThree", EligibleThree);
        command.Parameters.AddWithValue("@InactiveParty", InactiveParty);
        command.Parameters.AddWithValue("@InactiveAccount", InactiveAccount);
        command.Parameters.AddWithValue("@RemovedSupplier", RemovedSupplier);
        command.Parameters.AddWithValue("@ForeignSupplier", ForeignSupplier);
        command.Parameters.AddWithValue("@CompanyA", TestCompanyIds.CompanyA);
        command.Parameters.AddWithValue("@CompanyB", TestCompanyIds.CompanyB);
        command.Parameters.AddWithValue("@Supplier", (int)PartyRole.Supplier);
        command.Parameters.AddWithValue("@Tolling", (int)PartyRole.Tolling);
        command.Parameters.AddWithValue("@Now", new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(14, await command.ExecuteNonQueryAsync());
    }

    private static async Task<(int Parties, int Accounts)> ReadCountsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT COUNT(*) FROM [counterparty].[Parties]), "
            + "(SELECT COUNT(*) FROM [counterparty].[CompanyPartyAccounts]);";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
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

    private enum TestAuthorityScopeOrigin
    {
        Company = 1,
        DirectLocation = 2,
        Site = 3,
        WorkCenter = 4,
    }

    private sealed class MutableAuthority(
        IReadOnlySet<Guid> readLocations,
        IReadOnlySet<Guid> writeLocations,
        TestAuthorityScopeOrigin scopeOrigin = TestAuthorityScopeOrigin.DirectLocation)
        : IOrganizationLocationAuthorityRevalidationV1
    {
        private readonly HashSet<OrganizationLocationAuthorityAccessV1> revoked = [];

        public TestAuthorityScopeOrigin ScopeOrigin { get; } = scopeOrigin;
        public List<OrganizationLocationAuthorityRequestV1> Requests { get; } = [];

        public void Revoke(OrganizationLocationAuthorityAccessV1 access) => revoked.Add(access);

        public Task<OrganizationLocationAuthorityOutcomeV1> RevalidateAsync(
            OrganizationLocationAuthorityRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.CompanyId != TestCompanyIds.CompanyA
                || request.ActorId != Actor
                || revoked.Contains(request.Access))
            {
                return Task.FromResult(OrganizationLocationAuthorityOutcomeV1.Denied);
            }

            IReadOnlySet<Guid>? locations = request.Access switch
            {
                OrganizationLocationAuthorityAccessV1.Read => readLocations,
                OrganizationLocationAuthorityAccessV1.Write => writeLocations,
                _ => null,
            };
            return Task.FromResult(locations is not null
                && request.LocationIds.Count > 0
                && request.LocationIds.All(locations.Contains)
                    ? OrganizationLocationAuthorityOutcomeV1.Authorized
                    : OrganizationLocationAuthorityOutcomeV1.Denied);
        }
    }

    private sealed class CapturingObserver : ICounterpartyOrganizationLocationOperatorReadSqlObserver
    {
        public List<(CounterpartyOrganizationLocationOperatorReadSqlOperation Operation, string CommandText)> Commands { get; } = [];
        public Action? OnObserve { get; init; }

        public void Observe(CounterpartyOrganizationLocationOperatorReadSqlOperation operation, string commandText)
        {
            Commands.Add((operation, commandText));
            OnObserve?.Invoke();
        }
    }

    private sealed class ThrowingObserver : ICounterpartyOrganizationLocationOperatorReadSqlObserver
    {
        public void Observe(CounterpartyOrganizationLocationOperatorReadSqlOperation operation, string commandText) =>
            throw new InvalidOperationException("simulated Counterparty source outage");
    }
}
