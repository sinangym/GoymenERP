using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-04a posting account mapping catalog over the real host and a disposable SQL Server database. Every account,
/// plan and grant is SENTETIK. The active approved account plan is prepared and activated through the existing
/// catalog routes; the mapping is prepared by one SENTETIK actor and activated by another.
/// </summary>
public sealed class AccountingPostingAccountMappingSqlServerRelationalAcceptanceTests
{
    private const string MappingsPath = "/api/v1/accounting/posting-account-mappings";
    private const string PackagesPath = "/api/v1/accounting/approved-source-catalog/packages";
    private const string Preparer = TestUsers.CompanyA;
    private const string Checker = TestUsers.CompanyAViewer;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Receivable120 = Guid.Parse("e0400000-0000-4000-8000-000000000120");
    private static readonly Guid Revenue600 = Guid.Parse("e0400000-0000-4000-8000-000000000600");
    private static readonly Guid Export601 = Guid.Parse("e0400000-0000-4000-8000-000000000601");

    [SqlServerFact]
    public async Task A_mapping_version_is_one_save_with_attempt_and_audit_and_another_user_activates_it_atomically()
    {
        await using var run = await Run.StartAsync();
        // The preparer also holds the activate name: maker-checker refuses them on the command, not by the grant.
        var preparer = await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Prepare,
            AccountingPostingAccountMappingPermissionNamesV1.Activate, AccountingPostingAccountMappingPermissionNamesV1.Read);
        var checker = await run.GrantAsync(Checker, AccountingPostingAccountMappingPermissionNamesV1.Activate,
            AccountingPostingAccountMappingPermissionNamesV1.Read);
        using var preparerClient = run.Client(Preparer);
        using var checkerClient = run.Client(Checker);

        using (var prepared = await PrepareAsync(preparerClient, 0, Key("prepare-1")))
        {
            Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
            var body = await Json(prepared);
            Assert.Equal(("Prepared", 1L), (body.GetProperty("status").GetString(), body.GetProperty("mappingVersion").GetInt64()));
            Assert.Equal(preparer, body.GetProperty("preparedByUserId").GetGuid());
        }

        Assert.Equal(new Counts(1, 3, 1, 1), await run.CountsAsync());

        using (var self = await ActivateAsync(preparerClient, 1, 1, 0, Key("self")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        }

        using (var activated = await ActivateAsync(checkerClient, 1, 1, 0, Key("activate-1")))
        {
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
            var body = await Json(activated);
            Assert.Equal(("Active", 2L), (body.GetProperty("status").GetString(), body.GetProperty("version").GetInt64()));
            Assert.Equal(checker, body.GetProperty("activatedByUserId").GetGuid());
        }

        // The self-activation refusal is a recorded attempt (it replays), with no audit and no state change.
        Assert.Equal(new Counts(1, 3, 3, 2), await run.CountsAsync());

        using (var prepared = await PrepareAsync(preparerClient, 1, Key("prepare-2")))
        {
            Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        }

        using (var activated = await ActivateAsync(checkerClient, 2, 1, 1, Key("activate-2")))
        {
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        }

        // Activation supersedes the previous version in the same save: one attempt, two audit events.
        Assert.Equal(new Counts(2, 6, 5, 5), await run.CountsAsync());
        Assert.Equal((3, 3L), await run.VersionAsync(1));
        Assert.Equal((2, 2L), await run.VersionAsync(2));
        using var reopened = await checkerClient.GetAsync($"{MappingsPath}/active");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal(2, (await Json(reopened)).GetProperty("mappingVersion").GetInt64());

        await using var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var resolver = scope.ServiceProvider.GetRequiredService<IAccountingPostingAccountMappingResolverV1>();
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Mapped(Export601, 2), await resolver.ResolveAsync(
            CompanyA, AccountingPostingAccountMappingPurposesV1.SalesInvoice, new DateOnly(2026, 11, 2),
            AccountingPostingAccountMappingKeysV1.Revenue, "Steel", null, true));
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Mapped(Revenue600, 2), await resolver.ResolveAsync(
            CompanyA, AccountingPostingAccountMappingPurposesV1.SalesInvoice, new DateOnly(2026, 11, 2),
            AccountingPostingAccountMappingKeysV1.Revenue, "Steel", null, false));
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Unmapped(2), await resolver.ResolveAsync(
            CompanyA, AccountingPostingAccountMappingPurposesV1.SalesInvoice, new DateOnly(2026, 11, 2),
            AccountingPostingAccountMappingKeysV1.VatOutput));
    }

    [SqlServerFact]
    public async Task Two_racing_activations_leave_one_active_version_and_the_database_holds_the_checks()
    {
        await using var run = await Run.StartAsync();
        var preparer = await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Prepare);
        await run.GrantAsync(Checker, AccountingPostingAccountMappingPermissionNamesV1.Activate);
        using var preparerClient = run.Client(Preparer);
        using (var first = await PrepareAsync(preparerClient, 0, Key("race-1")))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using (var second = await PrepareAsync(preparerClient, 1, Key("race-2")))
        {
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        }

        using var left = run.Client(Checker);
        using var right = run.Client(Checker);
        var answers = await Task.WhenAll(
            ActivateAsync(left, 1, 1, 0, Key("race-activate-1")),
            ActivateAsync(right, 2, 1, 0, Key("race-activate-2")));
        try
        {
            Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
            Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var answer in answers) answer.Dispose();
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.PostingAccountMappingVersions WHERE CompanyId = @c AND Status = 2",
            ("@c", CompanyA)));
        var prepared = await run.ScalarAsync<long>(
            "SELECT MappingVersion FROM accounting.PostingAccountMappingVersions WHERE CompanyId = @c AND Status = 1",
            ("@c", CompanyA));

        // The database refuses a second active version and a self-activation, whatever writes them.
        var secondActive = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE accounting.PostingAccountMappingVersions SET Status = 2, ActivatedByUserId = NEWID(), "
            + "ActivatedAtUtc = SYSDATETIMEOFFSET() WHERE CompanyId = @c AND MappingVersion = @v; SELECT 0",
            ("@c", CompanyA), ("@v", prepared)));
        Assert.Contains(secondActive.Number, new[] { 2601, 2627 });
        var selfActivation = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE accounting.PostingAccountMappingVersions SET Status = 3, ActivatedByUserId = @p, "
            + "ActivatedAtUtc = SYSDATETIMEOFFSET() WHERE CompanyId = @c AND MappingVersion = @v; SELECT 0",
            ("@c", CompanyA), ("@v", prepared), ("@p", preparer)));
        Assert.Equal(547, selfActivation.Number);
        var wrongKey = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO accounting.PostingAccountMappingRows (CompanyId, MappingVersion, LineNumber, Purpose, MappingKey, "
            + "AccountId, AccountCode) VALUES (@c, @v, 99, N'sales-invoice', N'payable', NEWID(), N'320'); SELECT 0",
            ("@c", CompanyA), ("@v", prepared)));
        Assert.Equal(547, wrongKey.Number);
        var duplicateWildcard = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO accounting.PostingAccountMappingRows (CompanyId, MappingVersion, LineNumber, Purpose, MappingKey, "
            + "AccountId, AccountCode) VALUES (@c, @v, 99, N'sales-invoice', N'receivable', NEWID(), N'120'); SELECT 0",
            ("@c", CompanyA), ("@v", prepared)));
        Assert.Contains(duplicateWildcard.Number, new[] { 2601, 2627 });
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.PostingAccountMappingVersions WHERE CompanyId = @c AND Status = 2",
            ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Another_company_cannot_read_or_activate_a_mapping_version_and_the_resolver_stays_in_its_company()
    {
        await using var run = await Run.StartAsync();
        await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Prepare);
        await run.GrantAsync(Checker, AccountingPostingAccountMappingPermissionNamesV1.Activate,
            AccountingPostingAccountMappingPermissionNamesV1.Read);
        await run.GrantAsync(TestUsers.CompanyB, CompanyB, AccountingPostingAccountMappingPermissionNamesV1.Read,
            AccountingPostingAccountMappingPermissionNamesV1.Activate);
        using var preparerClient = run.Client(Preparer);
        using var checkerClient = run.Client(Checker);
        using var ofB = run.Client(TestUsers.CompanyB, CompanyB);

        using (var first = await PrepareAsync(preparerClient, 0, Key("iso-1")))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using (var activated = await ActivateAsync(checkerClient, 1, 1, 0, Key("iso-activate-1")))
        {
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        }

        using (var second = await PrepareAsync(preparerClient, 1, Key("iso-2")))
        {
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        }

        // (a) B cannot read A's versions (prepared, active) or A's active version: 404 with nothing of A in the body.
        foreach (var path in new[] { $"{MappingsPath}/1", $"{MappingsPath}/2", $"{MappingsPath}/active" })
        {
            using var read = await ofB.GetAsync(path);
            var body = await read.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            Assert.DoesNotContain(CompanyA.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Revenue600.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("600.01", body, StringComparison.Ordinal);
        }

        // (b) B cannot activate A's prepared version (IDOR): 404, and A's state and counts do not change.
        using (var cross = await ActivateAsync(ofB, 2, 1, 1, Key("iso-activate-cross")))
        {
            Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        }

        using (var crossActive = await ActivateAsync(ofB, 1, 3, 0, Key("iso-activate-cross-active")))
        {
            Assert.Equal(HttpStatusCode.NotFound, crossActive.StatusCode);
        }

        Assert.Equal((2, 2L), await run.VersionAsync(1));
        Assert.Equal((1, 1L), await run.VersionAsync(2));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.PostingAccountMappingVersions WHERE CompanyId = @c", ("@c", CompanyB)));
        Assert.Equal(new Counts(2, 6, 3, 3), await run.CountsAsync());

        // (c) the resolver sees only its own company: A maps, B has no active mapping and cannot ask for A.
        await using var scopeB = run.Factory.Services.CreateAsyncScope();
        scopeB.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyB);
        var resolverB = scopeB.ServiceProvider.GetRequiredService<IAccountingPostingAccountMappingResolverV1>();
        var date = new DateOnly(2026, 11, 2);
        var sales = AccountingPostingAccountMappingPurposesV1.SalesInvoice;
        Assert.Equal(AccountingPostingAccountMappingUnavailableReasonsV1.NoActiveMapping,
            (await resolverB.ResolveAsync(CompanyB, sales, date, AccountingPostingAccountMappingKeysV1.Revenue))
            .UnavailableReason);
        Assert.Equal(AccountingPostingAccountMappingUnavailableReasonsV1.CompanyContextInvalid,
            (await resolverB.ResolveAsync(CompanyA, sales, date, AccountingPostingAccountMappingKeysV1.Revenue))
            .UnavailableReason);

        await using var scopeA = run.Factory.Services.CreateAsyncScope();
        scopeA.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var resolverA = scopeA.ServiceProvider.GetRequiredService<IAccountingPostingAccountMappingResolverV1>();
        Assert.Equal(AccountingPostingAccountMappingResolutionV1.Mapped(Revenue600, 1), await resolverA.ResolveAsync(
            CompanyA, sales, date, AccountingPostingAccountMappingKeysV1.Revenue, "Steel", null, false));
    }

    [SqlServerFact]
    public async Task The_posting_mapping_migrations_Down_passes_while_empty_and_refuses_while_a_version_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddPostingAccountMappings().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        await run.GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Prepare);
        using var client = run.Client(Preparer);
        using (var prepared = await PrepareAsync(client, 0, Key("down")))
        {
            Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51212, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.PostingAccountMappingVersions WHERE CompanyId = @c", ("@c", CompanyA)));
    }

    private static Task<HttpResponseMessage> PrepareAsync(HttpClient client, long expectedLatest, string key)
    {
        var request = Command(HttpMethod.Post, MappingsPath, key);
        request.Content = JsonContent.Create(new
        {
            expectedLatestMappingVersion = expectedLatest,
            effectiveFrom = "2026-11-01",
            rows = new object[]
            {
                new { purpose = "sales-invoice", mappingKey = "receivable", accountId = Receivable120 },
                new { purpose = "sales-invoice", mappingKey = "revenue", accountId = Revenue600 },
                new { purpose = "sales-invoice", mappingKey = "revenue", isExport = (bool?)true, accountId = Export601 },
            },
        });
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ActivateAsync(
        HttpClient client, long mappingVersion, long expectedVersion, long expectedActive, string key)
    {
        var request = Command(HttpMethod.Post, $"{MappingsPath}/{mappingVersion}/activation", key);
        request.Content = JsonContent.Create(new { expectedVersion, expectedActiveMappingVersion = expectedActive });
        return client.SendAsync(request);
    }

    private static HttpRequestMessage Command(HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return request;
    }

    private static string Key(string purpose) => $"SENTETIK-b04a-{purpose}-{Guid.NewGuid():N}";

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed record Counts(int Versions, int Rows, int Commands, int Audits);

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
            var run = new Run(baseConnectionString, new SqlServerRelationalApiFactory(baseConnectionString));
            try
            {
                using (var client = run.Factory.CreateClient())
                using (var health = await client.GetAsync("/health/live"))
                {
                    health.EnsureSuccessStatusCode();
                }

                var company = new CompanyContext();
                company.Set(CompanyA);
                await using (var context = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
                                 .UseSqlServer(run.Factory.DatabaseConnectionString,
                                     sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                                 .Options, company))
                {
                    await context.Database.MigrateAsync();
                }

                await run.ActivatePlanAsync();
                return run;
            }
            catch
            {
                await run.DisposeAsync();
                throw;
            }
        }

        public HttpClient Client(string developmentUser, Guid? company = null)
        {
            var client = Factory.CreateClientFor(developmentUser, company ?? CompanyA);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public Task<Guid> GrantAsync(string developmentUser, params string[] permissions) =>
            GrantAsync(developmentUser, CompanyA, permissions);

        public async Task<Guid> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                identity => identity.Issuer == BootstrapIdentityOptions.Issuer && identity.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK Accounting mapping actor", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(
                    Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"ACC-MAPPING-{roleId:N}", "SENTETIK Accounting mapping test role", true);
            foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(
                Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return actor;
        }

        public async Task<Counts> CountsAsync() => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.PostingAccountMappingVersions WHERE CompanyId = @c",
                ("@c", CompanyA)),
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.PostingAccountMappingRows WHERE CompanyId = @c",
                ("@c", CompanyA)),
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM accounting.Commands WHERE CompanyId = @c AND CommandName LIKE N'accounting.posting-account-mapping.%'",
                ("@c", CompanyA)),
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @c AND EntityType = N'Accounting.PostingAccountMapping'",
                ("@c", CompanyA)));

        public async Task<(int Status, long Version)> VersionAsync(long mappingVersion)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT Status, Version FROM accounting.PostingAccountMappingVersions WHERE CompanyId = @c AND MappingVersion = @v",
                connection);
            command.Parameters.AddWithValue("@c", CompanyA);
            command.Parameters.AddWithValue("@v", mappingVersion);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetInt32(0), reader.GetInt64(1));
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
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

        private async Task ActivatePlanAsync()
        {
            const string planUser = TestUsers.GroupAdmin;
            await GrantAsync(planUser, ApprovedAccountingSourceCatalogPermissionNamesV1.Prepare,
                ApprovedAccountingSourceCatalogPermissionNamesV1.Activate);
            var accounts = Encoding.UTF8.GetBytes(
                ApprovedAccountingSourcePackageTemplateV1.AccountsHeader + "\n"
                + $"{Receivable120:D},120.01,SENTETIK alicilar\n"
                + $"{Revenue600:D},600.01,SENTETIK yurt ici satislar\n"
                + $"{Export601:D},601.01,SENTETIK yurt disi satislar\n");
            var mappings = Encoding.UTF8.GetBytes(
                ApprovedAccountingSourcePackageTemplateV1.MappingsHeader + "\n"
                + $"30000000-0000-4000-8000-000000000001,{NormalJournalSourceKindsV1.RecordedManualReceipt},v1,profile,"
                + $"profile-v1,40000000-0000-4000-8000-000000000001,receipt,rule-v1,debit,Debit,{Receivable120:D}\n"
                + $"30000000-0000-4000-8000-000000000001,{NormalJournalSourceKindsV1.RecordedManualReceipt},v1,profile,"
                + $"profile-v1,40000000-0000-4000-8000-000000000001,receipt,rule-v1,credit,Credit,{Revenue600:D}\n");
            var packageId = Guid.NewGuid();
            using var client = Client(planUser);
            var form = new MultipartFormDataContent
            {
                { new StringContent(packageId.ToString("D")), "packageId" },
                { new StringContent("mikro-synthetic-b04a"), "accountPlanSourceVersion" },
                { new StringContent(Convert.ToHexString(SHA256.HashData(accounts))), "expectedAccountsSourceSha256" },
                { new StringContent(Convert.ToHexString(SHA256.HashData(mappings))), "expectedPostingMappingsSourceSha256" },
                { Csv(accounts), "accountsCsv", "accounts.csv" },
                { Csv(mappings), "postingMappingsCsv", "posting-mappings.csv" },
            };
            using var prepare = Command(HttpMethod.Post, PackagesPath, Key("plan-prepare"));
            prepare.Content = form;
            using (var prepared = await client.SendAsync(prepare))
            {
                Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
            }

            using var activate = Command(HttpMethod.Post, $"{PackagesPath}/{packageId:D}/activation", Key("plan-activate"));
            activate.Content = JsonContent.Create(new { expectedPackageVersion = 1, expectedActiveCatalogVersion = 0 });
            using var activated = await client.SendAsync(activate);
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        }

        private static ByteArrayContent Csv(byte[] bytes)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("text/csv") { CharSet = "utf-8" };
            return content;
        }
    }
}
