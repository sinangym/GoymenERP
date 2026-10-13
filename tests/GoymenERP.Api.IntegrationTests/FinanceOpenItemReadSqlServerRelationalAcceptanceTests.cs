using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FI04 over disposable real SQL. Every open item enters through the existing approved official-file adapter contract
/// and <see cref="FinanceOpenItemSourceAdmissionService"/>; the test never inserts an open-item row directly. Synthetic
/// owner and admission-authority ports are fixture inputs. HTTP authority uses normal Identity users, roles, company
/// scopes and the production Finance permission port. Reads must remain company/actor fenced and write-free.
/// </summary>
public sealed class FinanceOpenItemReadSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b20");
    private const string ReaderA = "SENTETIK-fi04-reader-a";
    private const string ReaderB = "SENTETIK-fi04-reader-b";

    [SqlServerFact]
    public async Task Real_SQL_pages_equal_timestamps_reopens_outside_first_page_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var admittedA = await run.AdmitApprovedFileAsync(CompanyA, 52);
        var admittedB = await run.AdmitApprovedFileAsync(CompanyB, 1);
        var before = await run.PersistentEffectsAsync();
        var expectedOrder = await run.OrderedOpenItemIdsAsync(CompanyA);

        FinanceOpenItemPageV1 firstPage;
        await using (var reader = run.Reader(CompanyA, ReaderA,
            [PermissionDecision.Allowed, PermissionDecision.Allowed],
            [PermissionDecision.Denied, PermissionDecision.Denied]))
        {
            var result = await reader.Service.ListAuthorizedAsync(
                reader.Context, new SourceListRequest(null), CancellationToken.None);
            Assert.Equal(ReadOutcome.Found, result.Outcome);
            firstPage = Assert.IsType<FinanceOpenItemPageV1>(result.Resource);
            Assert.Equal(FinanceOpenItemReadService.PageSize, firstPage.Items.Count);
            Assert.NotNull(firstPage.NextCursor);
            Assert.Equal(expectedOrder.Take(FinanceOpenItemReadService.PageSize),
                firstPage.Items.Select(item => item.Id));
            Assert.All(firstPage.Items, item =>
            {
                Assert.Equal(CompanyA, item.CompanyId);
                Assert.Equal(FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment, item.SourceKind);
                Assert.Null(item.Status);
                AssertRedacted(item.InitialAmount);
                AssertRedacted(item.OutstandingAmount);
            });
            Assert.Single(firstPage.Items.Select(item => item.CreatedAtUtc).Distinct());
        }

        // A close/reopen at the same keyset produces the identical first page. Every row in this admitted batch has
        // the same CreatedAtUtc, so this pins the SQL tie-breaker to Id DESC rather than relying on insertion order.
        await using (var reopened = run.Reader(CompanyA, ReaderA, AllowedPair(), AllowedPair()))
        {
            var repeated = Assert.IsType<FinanceOpenItemPageV1>((await reopened.Service.ListAuthorizedAsync(
                reopened.Context, new SourceListRequest(null), CancellationToken.None)).Resource);
            Assert.Equal(firstPage.Items.Select(item => item.Id), repeated.Items.Select(item => item.Id));
            Assert.Equal(firstPage.NextCursor, repeated.NextCursor);
            Assert.All(repeated.Items, item => Assert.Equal(FinanceOpenItemStatusV1.Open, item.Status));
        }

        FinanceOpenItemPageV1 secondPage;
        await using (var next = run.Reader(CompanyA, ReaderA, AllowedPair(), AllowedPair()))
        {
            secondPage = Assert.IsType<FinanceOpenItemPageV1>((await next.Service.ListAuthorizedAsync(
                next.Context, new SourceListRequest(firstPage.NextCursor), CancellationToken.None)).Resource);
            Assert.Equal(expectedOrder.Skip(FinanceOpenItemReadService.PageSize),
                secondPage.Items.Select(item => item.Id));
            Assert.Null(secondPage.NextCursor);
        }

        var allIds = firstPage.Items.Concat(secondPage.Items).Select(item => item.Id).ToArray();
        Assert.Equal(52, allIds.Length);
        Assert.Equal(52, allIds.Distinct().Count());
        Assert.Equal(admittedA.OpenItemIds.Order(), allIds.Order());
        Assert.Single(firstPage.Items.Concat(secondPage.Items).Select(item => item.CreatedAtUtc).Distinct());

        // Detail is not constrained to the first list page. The separate amount authority reveals the two stored
        // values and no computed monetary derivative.
        var outsideFirstPage = secondPage.Items[0];
        await using (var detailReader = run.Reader(CompanyA, ReaderA, AllowedPair(), AllowedPair()))
        {
            var result = await detailReader.Service.DetailAuthorizedAsync(
                detailReader.Context, outsideFirstPage.Id, CancellationToken.None);
            var detail = Assert.IsType<FinanceOpenItemViewV1>(result.Resource);
            Assert.Equal(ReadOutcome.Found, result.Outcome);
            Assert.Equal(outsideFirstPage.Id, detail.Id);
            Assert.Equal(FinanceOpenItemStatusV1.Open, detail.Status);
            Assert.Equal(AmountDisclosure.Visible, detail.InitialAmount.Disclosure);
            Assert.NotNull(detail.InitialAmount.Money);
            Assert.Equal(AmountDisclosure.Visible, detail.OutstandingAmount.Disclosure);
            Assert.Equal(detail.InitialAmount.Money, detail.OutstandingAmount.Money);
        }

        await using (var anotherActor = run.Reader(CompanyA, ReaderB,
            [PermissionDecision.Allowed], [PermissionDecision.Allowed]))
        {
            var result = await anotherActor.Service.ListAuthorizedAsync(
                anotherActor.Context, new SourceListRequest(firstPage.NextCursor), CancellationToken.None);
            Assert.Equal(ReadOutcome.NotFound, result.Outcome);
            Assert.Null(result.Resource);
        }

        await using (var anotherCompany = run.Reader(CompanyB, ReaderA,
            [PermissionDecision.Allowed], [PermissionDecision.Allowed]))
        {
            var badCursor = await anotherCompany.Service.ListAuthorizedAsync(
                anotherCompany.Context, new SourceListRequest(firstPage.NextCursor), CancellationToken.None);
            Assert.Equal(ReadOutcome.NotFound, badCursor.Outcome);
            Assert.Null(badCursor.Resource);
        }

        await using (var companyB = run.Reader(CompanyB, ReaderB, AllowedPair(), AllowedPair()))
        {
            var own = Assert.IsType<FinanceOpenItemPageV1>((await companyB.Service.ListAuthorizedAsync(
                companyB.Context, new SourceListRequest(null), CancellationToken.None)).Resource);
            Assert.Equal(admittedB.OpenItemIds[0], Assert.Single(own.Items).Id);
        }

        await using (var foreign = run.Reader(CompanyB, ReaderB, AllowedPair(), AllowedPair()))
        {
            var result = await foreign.Service.DetailAuthorizedAsync(
                foreign.Context, outsideFirstPage.Id, CancellationToken.None);
            Assert.Equal(ReadOutcome.NotFound, result.Outcome);
            Assert.Null(result.Resource);
        }

        await using (var unknown = run.Reader(CompanyA, ReaderA, AllowedPair(), AllowedPair()))
        {
            var result = await unknown.Service.DetailAuthorizedAsync(
                unknown.Context, Guid.NewGuid(), CancellationToken.None);
            Assert.Equal(ReadOutcome.NotFound, result.Outcome);
            Assert.Null(result.Resource);
        }

        // Money authority was allowed before the SQL await and revoked after it: stored money must not escape.
        await using (var amountRevoked = run.Reader(CompanyA, ReaderA, AllowedPair(),
            [PermissionDecision.Allowed, PermissionDecision.Denied]))
        {
            var result = await amountRevoked.Service.DetailAuthorizedAsync(
                amountRevoked.Context, outsideFirstPage.Id, CancellationToken.None);
            var detail = Assert.IsType<FinanceOpenItemViewV1>(result.Resource);
            Assert.Equal(ReadOutcome.Found, result.Outcome);
            Assert.Null(detail.Status);
            AssertRedacted(detail.InitialAmount);
            AssertRedacted(detail.OutstandingAmount);
        }

        // Metadata authority was revoked after the SQL await: no row or amount is returned at all.
        await using (var readRevoked = run.Reader(CompanyA, ReaderA,
            [PermissionDecision.Allowed, PermissionDecision.Denied], AllowedPair()))
        {
            var result = await readRevoked.Service.ListAuthorizedAsync(
                readRevoked.Context, new SourceListRequest(null), CancellationToken.None);
            Assert.Equal(ReadOutcome.Denied, result.Outcome);
            Assert.Null(result.Resource);
        }

        Assert.Equal(before, await run.PersistentEffectsAsync());
    }

    [SqlServerFact]
    public async Task Over_HTTP_real_grants_keep_metadata_and_amount_authorities_separate_and_fresh()
    {
        await using var run = await Run.StartAsync();
        var admittedA = await run.AdmitApprovedFileAsync(CompanyA, 51);
        await run.AdmitApprovedFileAsync(CompanyB, 1);
        await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            FinancePermissionNames.OpenItemRead, FinancePermissionNames.OpenItemAmountRead);
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, FinancePermissionNames.OpenItemRead);
        await run.GrantAsync(TestUsers.CompanyB, CompanyB,
            FinancePermissionNames.OpenItemRead, FinancePermissionNames.OpenItemAmountRead);
        using var full = run.Factory.CreateClientFor(TestUsers.CompanyA, CompanyA);
        using var metadataOnly = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA);
        using var companyB = run.Factory.CreateClientFor(TestUsers.CompanyB, CompanyB);
        var beforeReads = await run.PersistentEffectsAsync();

        string cursor;
        using (var response = await full.GetAsync(FinanceOpenItemReadEndpoints.Path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertPrivateNoStore(response);
            var json = await response.Content.ReadAsStringAsync();
            AssertMetadataOnlyShape(json, AmountDisclosure.Visible, admittedA.Amounts);
            var page = JsonSerializer.Deserialize<FinanceOpenItemPageV1>(json, FinanceJson.CreateOptions())!;
            Assert.All(page.Items, item =>
            {
                Assert.NotNull(item.InitialAmount.Money);
                Assert.NotNull(item.OutstandingAmount.Money);
                Assert.Contains(item.InitialAmount.Money.Amount, admittedA.Amounts);
                Assert.Equal(item.InitialAmount.Money, item.OutstandingAmount.Money);
            });
            cursor = Assert.IsType<string>(page.NextCursor);
        }

        Guid detailId;
        using (var response = await metadataOnly.GetAsync(FinanceOpenItemReadEndpoints.Path))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertPrivateNoStore(response);
            var json = await response.Content.ReadAsStringAsync();
            AssertMetadataOnlyShape(json, AmountDisclosure.Redacted, admittedA.Amounts);
            detailId = JsonSerializer.Deserialize<FinanceOpenItemPageV1>(json, FinanceJson.CreateOptions())!.Items[0].Id;
        }

        using (var response = await metadataOnly.GetAsync($"{FinanceOpenItemReadEndpoints.Path}/{detailId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertPrivateNoStore(response);
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            AssertMetadataOnlyItem(document.RootElement, AmountDisclosure.Redacted);
            Assert.DoesNotContain(Scalars(document.RootElement), token => admittedA.Amounts.Contains(token));
        }

        // The opaque page token is both actor- and company-bound. Both callers hold metadata read authority, so
        // these 400s are cursor refusals rather than an authorization shortcut.
        using (var wrongActor = await metadataOnly.GetAsync(
            $"{FinanceOpenItemReadEndpoints.Path}?cursor={cursor}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrongActor.StatusCode);
        }
        using (var wrongCompany = await companyB.GetAsync(
            $"{FinanceOpenItemReadEndpoints.Path}?cursor={cursor}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrongCompany.StatusCode);
        }
        using (var foreign = await companyB.GetAsync(
            $"{FinanceOpenItemReadEndpoints.Path}/{detailId:D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        using (var unknown = await full.GetAsync(
            $"{FinanceOpenItemReadEndpoints.Path}/{Guid.NewGuid():D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        Assert.Equal(beforeReads, await run.PersistentEffectsAsync());

        await run.RevokeAsync(TestUsers.CompanyA, CompanyA);
        var afterRevocation = await run.PersistentEffectsAsync();
        using (var refused = await full.GetAsync(FinanceOpenItemReadEndpoints.Path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }
        Assert.Equal(afterRevocation, await run.PersistentEffectsAsync());
    }

    private static PermissionDecision[] AllowedPair() =>
        [PermissionDecision.Allowed, PermissionDecision.Allowed];

    private static void AssertRedacted(AmountView amount)
    {
        Assert.Equal(AmountDisclosure.Redacted, amount.Disclosure);
        Assert.Null(amount.Money);
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response) =>
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });

    private static void AssertMetadataOnlyShape(
        string json,
        AmountDisclosure disclosure,
        IReadOnlyList<string> storedAmounts)
    {
        using var document = JsonDocument.Parse(json);
        AssertExactProperties(document.RootElement, "companyId", "items", "nextCursor");
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(FinanceOpenItemReadService.PageSize, items.Length);
        Assert.All(items, item => AssertMetadataOnlyItem(item, disclosure));
        if (disclosure != AmountDisclosure.Visible)
        {
            Assert.DoesNotContain(Scalars(document.RootElement), token => storedAmounts.Contains(token));
        }
    }

    private static void AssertMetadataOnlyItem(JsonElement item, AmountDisclosure disclosure)
    {
        AssertExactProperties(item,
            "id", "companyId", "sourceKind", "sourceId", "sourceLineId", "sourceVersion",
            "partyId", "companyAccountId", "currencyId", "version", "dueOn", "status", "createdAtUtc",
            "initialAmount", "outstandingAmount");
        AssertAmount(item.GetProperty("initialAmount"), disclosure);
        AssertAmount(item.GetProperty("outstandingAmount"), disclosure);
        var status = item.GetProperty("status");
        if (disclosure == AmountDisclosure.Visible)
        {
            Assert.Equal(FinanceOpenItemStatusV1.Open.ToString(), status.GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, status.ValueKind);
        }
    }

    private static void AssertAmount(JsonElement amount, AmountDisclosure disclosure)
    {
        AssertExactProperties(amount, "disclosure", "money");
        Assert.Equal(disclosure.ToString(), amount.GetProperty("disclosure").GetString());
        var money = amount.GetProperty("money");
        if (disclosure == AmountDisclosure.Visible)
        {
            AssertExactProperties(money, "amount", "currencyId");
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, money.ValueKind);
        }
    }

    private static void AssertExactProperties(JsonElement value, params string[] expected) =>
        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.Ordinal),
            value.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));

    private static IEnumerable<string> Scalars(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Scalars(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Scalars),
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Number => [element.GetRawText()],
        _ => [],
    };

    private sealed record Owner(Guid CompanyId, Guid PartyId, Guid CompanyAccountId);

    private sealed record Admitted(
        IReadOnlyList<Guid> OpenItemIds,
        IReadOnlyList<string> Amounts);

    private sealed class ReadScope(
        FinanceDbContext db,
        FinanceOpenItemReadService service,
        FinanceContext context) : IAsyncDisposable
    {
        public FinanceOpenItemReadService Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class AdmissionScope(
        FinanceDbContext db,
        FinanceOpenItemSourceAdmissionService service,
        FinanceContext context) : IAsyncDisposable
    {
        public FinanceOpenItemSourceAdmissionService Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA, collector: null);
            await context.Database.MigrateAsync();
            return run;
        }

        public async Task<Admitted> AdmitApprovedFileAsync(Guid companyId, int count)
        {
            var owner = new Owner(companyId, Guid.NewGuid(), Guid.NewGuid());
            var batchId = Guid.NewGuid();
            var batchVersion = $"approved-file-{companyId:N}";
            var batchChecksum = new string(companyId == CompanyA ? 'a' : 'b', 64);
            var amounts = Enumerable.Range(0, count)
                .Select(index => $"{1000 + index}.00")
                .ToArray();
            var rows = Enumerable.Range(0, count)
                .Select(index => new ApprovedFinanceOpenItemSourceRow(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    $"invoice-v1-{index}",
                    FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                    owner.PartyId,
                    owner.CompanyAccountId,
                    Currency,
                    amounts[index],
                    new DateOnly(2026, 11, 1).AddDays(index)))
                .ToArray();
            var reference = new FinanceOpenItemSourceReference(batchId, batchVersion, batchChecksum);
            var batch = new ApprovedFinanceOpenItemSourceBatch(
                batchId, companyId, batchVersion, batchChecksum, rows);

            await using var scope = Admission(companyId, owner, batch);
            var result = await scope.Service.AdmitAsync(
                scope.Context,
                new CommandMetadata($"fi04-approved-file-{companyId:N}"),
                reference,
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            Assert.False(result.IsReplay);
            Assert.Equal(rows.Select(row => row.OpenItemId), result.OpenItemIds);
            return new Admitted(result.OpenItemIds, amounts);
        }

        public ReadScope Reader(
            Guid companyId,
            string actor,
            PermissionDecision[] read,
            PermissionDecision[] amount)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            var db = Context(company, collector: null);
            var service = new FinanceOpenItemReadService(
                db, company, new FixedCurrentUser(actor, companyId), new ReadPermissions(read, amount));
            return new ReadScope(db, service,
                new FinanceContext(companyId, actor, $"fi04-read-{Guid.NewGuid():N}", null));
        }

        public async Task GrantAsync(string developmentUser, Guid companyId, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await identity.ExternalIdentities.SingleOrDefaultAsync(
                candidate => candidate.Issuer == BootstrapIdentityOptions.Issuer
                    && candidate.Subject == developmentUser);
            Guid actorId;
            if (link is null)
            {
                actorId = Guid.NewGuid();
                identity.Users.Add(new UserAccount(actorId, "Synthetic FI04 SQL reader", null));
                identity.ExternalIdentities.Add(new ExternalIdentityLink(
                    Guid.NewGuid(), actorId, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actorId = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"FIN-FI04-{roleId:N}", "Synthetic FI04 SQL role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(
                Guid.NewGuid(), actorId, roleId, companyId,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, companyId);
            identity.Roles.Add(role);
            identity.RoleAssignments.Add(assignment);
            await identity.SaveChangesAsync();
        }

        public async Task RevokeAsync(string developmentUser, Guid companyId)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var actorId = await identity.ExternalIdentities
                .Where(candidate => candidate.Issuer == BootstrapIdentityOptions.Issuer
                    && candidate.Subject == developmentUser)
                .Select(candidate => candidate.UserId)
                .SingleAsync();
            var assignments = await identity.RoleAssignments.Include(assignment => assignment.Scopes)
                .Where(assignment => assignment.UserId == actorId && assignment.CompanyId == companyId)
                .ToListAsync();
            identity.RoleAssignments.RemoveRange(assignments);
            await identity.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<Guid>> OrderedOpenItemIdsAsync(Guid companyId)
        {
            var ids = new List<Guid>();
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT Id FROM finance.OpenItems WHERE CompanyId = @company ORDER BY CreatedAtUtc DESC, Id DESC",
                connection);
            command.Parameters.AddWithValue("@company", companyId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetGuid(0));
            }
            return ids;
        }

        public Task<string> PersistentEffectsAsync() => ScalarAsync<string>(
            """
            SELECT CONCAT(
                (SELECT COUNT_BIG(*) FROM finance.OpenItems), ':',
                (SELECT COUNT_BIG(*) FROM finance.OpenItemSourceBatches), ':',
                (SELECT COUNT_BIG(*) FROM finance.Commands), ':',
                (SELECT COUNT_BIG(*) FROM finance.MoneyEvents), ':',
                (SELECT COUNT_BIG(*) FROM finance.OutgoingMoneyEvents), ':',
                (SELECT COUNT_BIG(*) FROM finance.Allocations), ':',
                (SELECT COUNT_BIG(*) FROM finance.SourceAllocationStates), ':',
                (SELECT COUNT_BIG(*) FROM audit.AuditEvents), ':',
                (SELECT COUNT_BIG(*) FROM inventory.OutboxMessages))
            """);

        private AdmissionScope Admission(
            Guid companyId,
            Owner owner,
            ApprovedFinanceOpenItemSourceBatch batch)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            var collector = new AuditEventCollector();
            var db = Context(company, collector);
            var actor = "SENTETIK-approved-file-admitter";
            var service = new FinanceOpenItemSourceAdmissionService(
                db,
                company,
                new FixedCurrentUser(actor, companyId),
                new AllowedAdmissionAuthority(),
                new FixedApprovedSource(batch),
                new ActiveParty(owner),
                collector,
                new FinanceAuditEnvelope(
                    "synthetic.finance.open-item-source.admit", ["SYNTHETIC-FINANCE"], "Web"),
                TimeProvider.System);
            return new AdmissionScope(db, service,
                new FinanceContext(companyId, actor, $"fi04-admit-{Guid.NewGuid():N}", null));
        }

        private FinanceDbContext Context(Guid companyId, AuditEventCollector? collector)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            return Context(company, collector);
        }

        private FinanceDbContext Context(CompanyContext company, AuditEventCollector? collector)
        {
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            if (collector is not null)
            {
                options.AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
            }
            return new FinanceDbContext(options.Options, company);
        }

        private async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class ReadPermissions(
        PermissionDecision[] read,
        PermissionDecision[] amount) : IFinancePermissionPort
    {
        private int readIndex;
        private int amountIndex;

        public Task<PermissionDecision> CheckAsync(
            FinanceContext context,
            FinanceAction action,
            Guid? sourceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(action switch
            {
                FinanceAction.ReadOpenItems => Next(read, ref readIndex),
                FinanceAction.ReadOpenItemAmount => Next(amount, ref amountIndex),
                _ => throw new InvalidOperationException($"Unexpected FI04 read action: {action}"),
            });
        }

        private static PermissionDecision Next(PermissionDecision[] decisions, ref int index)
        {
            Assert.NotEmpty(decisions);
            var result = decisions[Math.Min(index, decisions.Length - 1)];
            index++;
            return result;
        }
    }

    private sealed class AllowedAdmissionAuthority : IFinanceOpenItemSourceAuthority
    {
        public Task<PermissionDecision> CheckAsync(
            FinanceContext context,
            Guid batchId,
            CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class FixedApprovedSource(ApprovedFinanceOpenItemSourceBatch batch)
        : IApprovedFinanceOpenItemSourcePort
    {
        public Task<ReadResult<ApprovedFinanceOpenItemSourceBatch>> GetApprovedAsync(
            FinanceContext context,
            FinanceOpenItemSourceReference source,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<ApprovedFinanceOpenItemSourceBatch>(ReadOutcome.Found, batch));
    }

    private sealed class ActiveParty(Owner owner) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(
            FinanceContext context,
            Guid partyId,
            Guid companyAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(context.CompanyId == owner.CompanyId
                && partyId == owner.PartyId
                && companyAccountId == owner.CompanyAccountId
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(
                        owner.PartyId, owner.CompanyAccountId, owner.CompanyId, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    private sealed class FixedCurrentUser(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
}
