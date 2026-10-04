using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-03's missing direct HTTP plus real-SQL bridge. PR #536 already proves the complete disposable
/// browser journey, and FinanceRecordSqlServerRelationalAcceptanceTests already proves the service-level
/// terminal-result rules. This case does not claim either delivery again: it binds the public allocation
/// wire replay to the same persisted source/open-item links, source-allocation state and one existing money
/// event. Party eligibility and the open-period answer are explicit synthetic upstream prerequisites; the
/// receipt write, record, allocation write/read, Identity authority, Settings money profile and SQL stores
/// are production services. This is not real invoice/Mikro data or a production grant.
/// </summary>
public sealed class FinanceAllocationLifecycleSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Party = Guid.Parse("f3000000-0000-4000-8000-000000000001");
    private static readonly Guid PartyAccount = Guid.Parse("f3000000-0000-4000-8000-000000000002");
    private const string Actor = TestUsers.CompanyA;

    private static readonly string[] Grants =
    [
        FinancePermissionNames.DraftSave,
        FinancePermissionNames.DraftRead,
        FinancePermissionNames.AmountRead,
        FinancePermissionNames.Record,
        FinancePermissionNames.AllocationWrite,
        FinancePermissionNames.AllocationRead,
        "finance.bank-account.read",
    ];

    [SqlServerFact]
    public async Task Create_and_release_over_HTTP_close_and_reopen_the_same_open_item_without_a_second_money_event()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Actor, CompanyA, Grants);
        var bankAccountId = await run.ApprovedBankAccountAsync();
        using var client = run.Client(Actor, CompanyA);

        var saveCorrelation = Guid.NewGuid().ToString("N");
        JsonElement saved;
        using (var response = await client.SendAsync(Post(
                   FinanceEndpoints.DraftsPath,
                   "SENTETIK-allocation-source-save",
                   saveCorrelation,
                   Save(bankAccountId))))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            saved = await Json(response);
            Assert.Equal("Succeeded", saved.GetProperty("outcome").GetString());
            Assert.False(saved.GetProperty("isReplay").GetBoolean());
            Assert.Equal(saveCorrelation, saved.GetProperty("originalCorrelationId").GetString());
        }

        var savedSource = saved.GetProperty("resource").GetProperty("source");
        var sourceId = savedSource.GetProperty("id").GetGuid();
        var recordCorrelation = Guid.NewGuid().ToString("N");
        JsonElement recorded;
        using (var response = await client.SendAsync(Post(
                   $"{FinanceEndpoints.ManualReceiptsPath}/{sourceId:D}/record",
                   "SENTETIK-allocation-source-record",
                   recordCorrelation,
                   new RecordRequest(sourceId, savedSource.GetProperty("version").GetString()!))))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            recorded = await Json(response);
            Assert.Equal("Succeeded", recorded.GetProperty("outcome").GetString());
            Assert.False(recorded.GetProperty("isReplay").GetBoolean());
            Assert.Equal(recordCorrelation, recorded.GetProperty("originalCorrelationId").GetString());
        }

        var recordedSource = recorded.GetProperty("resource").GetProperty("source");
        Assert.Equal(sourceId, recordedSource.GetProperty("id").GetGuid());
        Assert.Equal(Lira, recordedSource.GetProperty("amount").GetProperty("currencyId").GetGuid());
        Assert.Equal("1000.00", recordedSource.GetProperty("amount").GetProperty("amount").GetString());

        var openItem = await run.OpenItemAsync();
        var allocationId = Guid.NewGuid();
        var createRequest = new CreateFinanceAllocationRequest(
            allocationId,
            sourceId,
            recordedSource.GetProperty("version").GetString()!,
            openItem.Id,
            openItem.Version,
            "125.50",
            "SENTETIK HTTP-SQL allocation bridge");
        var createCorrelation = Guid.NewGuid().ToString("N");

        JsonElement created;
        using (var response = await client.SendAsync(Post(
                   FinanceEndpoints.AllocationsPath,
                   "SENTETIK-allocation-create",
                   createCorrelation,
                   createRequest)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            created = await Json(response);
            Assert.Equal("Succeeded", created.GetProperty("outcome").GetString());
            Assert.False(created.GetProperty("isReplay").GetBoolean());
            Assert.Equal(createCorrelation, created.GetProperty("originalCorrelationId").GetString());
            Assert.Empty(created.GetProperty("issues").EnumerateArray());
        }

        var createdResource = created.GetProperty("resource");
        AssertAllocation(createdResource, allocationId, sourceId, openItem.Id, "Active", "1");
        var createdResourceJson = createdResource.GetRawText();
        var createdIssuesJson = created.GetProperty("issues").GetRawText();

        using (var response = await client.SendAsync(Post(
                   FinanceEndpoints.AllocationsPath,
                   "SENTETIK-allocation-create",
                   Guid.NewGuid().ToString("N"),
                   createRequest)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertReplayOf(response, createCorrelation);
            var replay = await Json(response);
            Assert.Equal("Succeeded", replay.GetProperty("outcome").GetString());
            Assert.True(replay.GetProperty("isReplay").GetBoolean());
            Assert.Equal(createCorrelation, replay.GetProperty("originalCorrelationId").GetString());
            Assert.Equal(createdResourceJson, replay.GetProperty("resource").GetRawText());
            Assert.Equal(createdIssuesJson, replay.GetProperty("issues").GetRawText());
        }

        JsonElement activeDetail;
        using (var response = await client.GetAsync($"{FinanceEndpoints.AllocationsPath}/{allocationId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            activeDetail = await Json(response);
        }
        AssertAllocation(activeDetail.GetProperty("allocation"), allocationId, sourceId, openItem.Id, "Active", "1");
        Assert.Equal("2", activeDetail.GetProperty("openItemVersion").GetString());
        Assert.Equal(("0.00", FinanceOpenItemStatuses.Closed), await run.OpenItemStateAsync(openItem.Id));
        Assert.Equal(("1000.00", "874.50", 1L), await run.SourceStateAsync(sourceId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.MoneyEvents WHERE CompanyId = @company AND SourceId = @source",
            ("@company", CompanyA), ("@source", sourceId)));

        var releaseRequest = new ReleaseFinanceAllocationRequest(
            allocationId,
            createdResource.GetProperty("version").GetString()!,
            activeDetail.GetProperty("openItemVersion").GetString()!,
            "SENTETIK HTTP-SQL release bridge");
        var releaseCorrelation = Guid.NewGuid().ToString("N");
        JsonElement released;
        using (var response = await client.SendAsync(Post(
                   $"{FinanceEndpoints.AllocationsPath}/{allocationId:D}/release",
                   "SENTETIK-allocation-release",
                   releaseCorrelation,
                   releaseRequest)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            released = await Json(response);
            Assert.Equal("Succeeded", released.GetProperty("outcome").GetString());
            Assert.False(released.GetProperty("isReplay").GetBoolean());
            Assert.Equal(releaseCorrelation, released.GetProperty("originalCorrelationId").GetString());
            Assert.Empty(released.GetProperty("issues").EnumerateArray());
        }

        var releasedResource = released.GetProperty("resource");
        AssertAllocation(releasedResource, allocationId, sourceId, openItem.Id, "Released", "2");
        var releasedResourceJson = releasedResource.GetRawText();
        var releasedIssuesJson = released.GetProperty("issues").GetRawText();

        using (var response = await client.SendAsync(Post(
                   $"{FinanceEndpoints.AllocationsPath}/{allocationId:D}/release",
                   "SENTETIK-allocation-release",
                   Guid.NewGuid().ToString("N"),
                   releaseRequest)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertReplayOf(response, releaseCorrelation);
            var replay = await Json(response);
            Assert.Equal("Succeeded", replay.GetProperty("outcome").GetString());
            Assert.True(replay.GetProperty("isReplay").GetBoolean());
            Assert.Equal(releaseCorrelation, replay.GetProperty("originalCorrelationId").GetString());
            Assert.Equal(releasedResourceJson, replay.GetProperty("resource").GetRawText());
            Assert.Equal(releasedIssuesJson, replay.GetProperty("issues").GetRawText());
        }

        using (var response = await client.GetAsync($"{FinanceEndpoints.AllocationsPath}/{allocationId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var reopened = await Json(response);
            AssertAllocation(reopened.GetProperty("allocation"), allocationId, sourceId, openItem.Id, "Released", "2");
            Assert.Equal("3", reopened.GetProperty("openItemVersion").GetString());
        }

        Assert.Equal(("125.50", FinanceOpenItemStatuses.Open), await run.OpenItemStateAsync(openItem.Id));
        Assert.Equal(("1000.00", "1000.00", 2L), await run.SourceStateAsync(sourceId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Allocations WHERE CompanyId = @company AND Id = @id "
            + "AND SourceId = @source AND OpenItemId = @openItem",
            ("@company", CompanyA), ("@id", allocationId), ("@source", sourceId), ("@openItem", openItem.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.MoneyEvents WHERE CompanyId = @company AND SourceId = @source",
            ("@company", CompanyA), ("@source", sourceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CompanyId = @company AND CommandName = @command "
            + "AND FinanceAllocationId = @id",
            ("@company", CompanyA), ("@command", FinanceAllocationService.CreateCommandName), ("@id", allocationId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CompanyId = @company AND CommandName = @command "
            + "AND FinanceAllocationId = @id",
            ("@company", CompanyA), ("@command", FinanceAllocationService.ReleaseCommandName), ("@id", allocationId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @company "
            + "AND EntityType = N'Finance.Allocation' AND EntityId = @id",
            ("@company", CompanyA), ("@id", allocationId)));
    }

    private static DraftSaveRequest Save(Guid bankAccountId) => new(
        null,
        null,
        new ReceiptPreparation(
            Party,
            PartyAccount,
            bankAccountId,
            new MoneyText("1000.00", Lira),
            new ProfileReference(
                ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.ProfileId.ToString("D"),
                ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.Version.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            new DateOnly(2026, 10, 1),
            null,
            null,
            null));

    private static HttpRequestMessage Post<T>(string path, string key, string correlation, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, FinanceJson.CreateOptions()),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", correlation);
        return request;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static void AssertReplayOf(HttpResponseMessage response, string originalCorrelation)
    {
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(originalCorrelation,
            Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    private static void AssertAllocation(
        JsonElement resource,
        Guid allocationId,
        Guid sourceId,
        Guid openItemId,
        string status,
        string version)
    {
        Assert.Equal(allocationId, resource.GetProperty("id").GetGuid());
        Assert.Equal(CompanyA, resource.GetProperty("companyId").GetGuid());
        Assert.Equal(sourceId, resource.GetProperty("sourceId").GetGuid());
        Assert.Equal(openItemId, resource.GetProperty("openItemId").GetGuid());
        Assert.Equal(Lira, resource.GetProperty("currencyId").GetGuid());
        Assert.Equal("125.50", resource.GetProperty("amount").GetString());
        Assert.Equal(status, resource.GetProperty("status").GetString());
        Assert.Equal(version, resource.GetProperty("version").GetString());
    }

    private sealed class SyntheticPartyPort : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(
            FinanceContext context,
            Guid partyId,
            Guid companyAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                context.CompanyId == CompanyA && partyId == Party && companyAccountId == PartyAccount
                    ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                        new PartyAccountEligibility(Party, PartyAccount, CompanyA, true, true))
                    : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    private sealed class SyntheticOpenPeriodRule : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(
            FinanceContext context,
            DateOnly occurredOn,
            DateOnly? valueOn,
            DateTimeOffset decidedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    private sealed record OpenItem(Guid Id, string Version);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        private SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
            {
                services.RemoveAll<IFinancePartyAccountPort>();
                services.AddScoped<IFinancePartyAccountPort, SyntheticPartyPort>();
                services.RemoveAll<IFinanceRecordPeriodRule>();
                services.AddScoped<IFinanceRecordPeriodRule, SyntheticOpenPeriodRule>();
            });
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        public HttpClient Client(string developmentUser, Guid company)
        {
            var client = Factory.CreateClientFor(developmentUser, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task<Guid> ApprovedBankAccountAsync()
        {
            var id = Guid.NewGuid();
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "INSERT INTO finance.BankAccounts (Id, CompanyId, CurrencyId, DisplayName, MoneyMovementEligible, Iban, "
                + "Status, Version, ProposedBy, ProposedAtUtc, ApprovedBy, ApprovedAtUtc, UpdatedAtUtc) VALUES "
                + "(@id, @company, @currency, N'SENTETIK allocation bank', 1, N'TR330006100519786457841326', "
                + "2, 2, N'SENTETIK-proposer', SYSDATETIMEOFFSET(), N'SENTETIK-approver', "
                + "SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
                connection);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@company", CompanyA);
            command.Parameters.AddWithValue("@currency", Lira);
            await command.ExecuteNonQueryAsync();
            return id;
        }

        public async Task<OpenItem> OpenItemAsync()
        {
            var batchId = Guid.NewGuid();
            var openItemId = Guid.NewGuid();
            const string checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            await using var db = Context(CompanyA);
            db.OpenItemSourceBatches.Add(new FinanceOpenItemSourceBatchRow
            {
                Id = batchId,
                CompanyId = CompanyA,
                SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
                SourceVersion = "1",
                SourceChecksum = checksum,
                IdempotencyKeyHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                PayloadHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                ActorId = "synthetic-approved-invoice-adapter",
                ResultJson = "{}",
                CorrelationId = $"synthetic-open-item-{batchId:N}",
                AdmittedAtUtc = DateTimeOffset.UtcNow,
            });
            db.OpenItems.Add(new FinanceOpenItemRow
            {
                Id = openItemId,
                CompanyId = CompanyA,
                SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
                SourceId = Guid.NewGuid(),
                SourceLineId = Guid.NewGuid(),
                SourceVersion = "1",
                SourceBatchId = batchId,
                SourceBatchVersion = "1",
                SourceBatchChecksum = checksum,
                PartyId = Party,
                CompanyAccountId = PartyAccount,
                CurrencyId = Lira,
                OriginalAmountText = "125.50",
                OutstandingAmountText = "125.50",
                Status = FinanceOpenItemStatuses.Open,
                Version = 1,
                CreatedBy = "synthetic-approved-invoice-adapter",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "synthetic-approved-invoice-adapter",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return new OpenItem(openItemId, "1");
        }

        public async Task<(string Outstanding, int Status)> OpenItemStateAsync(Guid openItemId) =>
            (await ScalarAsync<string>(
                    "SELECT OutstandingAmountText FROM finance.OpenItems WHERE CompanyId = @company AND Id = @id",
                    ("@company", CompanyA), ("@id", openItemId)),
                await ScalarAsync<int>(
                    "SELECT Status FROM finance.OpenItems WHERE CompanyId = @company AND Id = @id",
                    ("@company", CompanyA), ("@id", openItemId)));

        public async Task<(string Original, string Unallocated, long Version)> SourceStateAsync(Guid sourceId) =>
            (await ScalarAsync<string>(
                    "SELECT OriginalAmountText FROM finance.SourceAllocationStates "
                    + "WHERE CompanyId = @company AND SourceId = @source",
                    ("@company", CompanyA), ("@source", sourceId)),
                await ScalarAsync<string>(
                    "SELECT UnallocatedAmountText FROM finance.SourceAllocationStates "
                    + "WHERE CompanyId = @company AND SourceId = @source",
                    ("@company", CompanyA), ("@source", sourceId)),
                await ScalarAsync<long>(
                    "SELECT Version FROM finance.SourceAllocationStates "
                    + "WHERE CompanyId = @company AND SourceId = @source",
                    ("@company", CompanyA), ("@source", sourceId)));

        public async Task GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await db.ExternalIdentities.SingleOrDefaultAsync(
                row => row.Issuer == BootstrapIdentityOptions.Issuer && row.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                db.Users.Add(new UserAccount(actor, "Synthetic allocation user", null));
                db.ExternalIdentities.Add(new ExternalIdentityLink(
                    Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(
                roleId,
                $"FIN-ALLOCATION-HTTP-SQL-{roleId:N}",
                "Synthetic allocation HTTP-SQL role",
                true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(
                Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
            assignment.AddScope(AccessScopeType.Company, company);
            db.Roles.Add(role);
            db.RoleAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        private FinanceDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(
                new DbContextOptionsBuilder<FinanceDbContext>()
                    .UseSqlServer(Factory.DatabaseConnectionString,
                        sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                    .Options,
                companyContext);
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
}
