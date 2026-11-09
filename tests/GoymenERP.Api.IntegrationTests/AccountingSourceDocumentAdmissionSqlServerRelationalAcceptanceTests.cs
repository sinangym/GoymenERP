using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
/// B-04b source document admission port over the real host's Accounting store and a disposable SQL Server database.
/// Every account, plan and grant is SENTETIK. The active account plan and the posting account mapping are prepared
/// and activated through the existing routes; the port is called in process (it is not an HTTP surface) with a
/// SENTETIK host authority.
/// </summary>
public sealed class AccountingSourceDocumentAdmissionSqlServerRelationalAcceptanceTests
{
    private const string MappingsPath = "/api/v1/accounting/posting-account-mappings";
    private const string PackagesPath = "/api/v1/accounting/approved-source-catalog/packages";
    private const string Preparer = TestUsers.CompanyA;
    private const string Checker = TestUsers.CompanyAViewer;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Receivable120 = Guid.Parse("e0420000-0000-4000-8000-000000000120");
    private static readonly Guid Revenue600 = Guid.Parse("e0420000-0000-4000-8000-000000000600");
    private static readonly Guid Export601 = Guid.Parse("e0420000-0000-4000-8000-000000000601");
    private static readonly Guid VatOutput391 = Guid.Parse("e0420000-0000-4000-8000-000000000391");
    private static readonly Guid Invoice = Guid.Parse("e0420000-0000-4000-8000-000000000001");
    private static readonly Guid SourceDocument = Guid.Parse("e0420000-0000-4000-8000-000000000002");
    private static readonly DateOnly DocumentDate = new(2026, 11, 8);

    [SqlServerFact]
    public async Task An_admission_is_one_save_with_voucher_lines_and_audit_and_a_replay_creates_no_second_voucher()
    {
        await using var run = await Run.StartAsync(withMapping: true);

        var first = await run.AdmitAsync(CompanyA, Request(CompanyA));
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, first.Outcome);
        Assert.Equal(new Counts(1, 1, 4, 1), await run.CountsAsync(CompanyA));

        var replay = await run.AdmitAsync(CompanyA, Request(CompanyA));
        Assert.True(replay.IsReplay);
        Assert.Equal(first.VoucherDraftId, replay.VoucherDraftId);
        Assert.Equal(new Counts(1, 1, 4, 1), await run.CountsAsync(CompanyA));

        var conflict = await run.AdmitAsync(CompanyA, Request(CompanyA) with { PartyId = Guid.NewGuid() });
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.IdempotencyConflict, conflict.Reason);
        Assert.Equal(new Counts(1, 1, 4, 1), await run.CountsAsync(CompanyA));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT AttemptCount FROM accounting.SourceDocumentAdmissions WHERE CompanyId = @c", ("@c", CompanyA)));

        // The voucher balances in the database and belongs to its own company only.
        Assert.Equal(0m, await run.ScalarAsync<decimal>(
            "SELECT SUM(CASE WHEN Side = 1 THEN Amount ELSE -Amount END) FROM accounting.SourceDocumentVoucherDraftLines WHERE CompanyId = @c",
            ("@c", CompanyA)));
        Assert.Equal(new Counts(0, 0, 0, 0), await run.CountsAsync(CompanyB));
    }

    [SqlServerFact]
    public async Task A_missing_mapping_waits_without_a_voucher_and_the_same_key_is_admitted_after_it_is_configured()
    {
        await using var run = await Run.StartAsync(withMapping: false);

        var waiting = await run.AdmitAsync(CompanyA, Request(CompanyA));
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.MappingPending, waiting.Reason);
        Assert.True(waiting.Retryable);
        Assert.Equal(new Counts(1, 0, 0, 1), await run.CountsAsync(CompanyA));

        await run.ConfigureMappingAsync();
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, admitted.Outcome);
        Assert.Equal(new Counts(1, 1, 4, 2), await run.CountsAsync(CompanyA));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT AttemptCount FROM accounting.SourceDocumentAdmissions WHERE CompanyId = @c", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Two_racing_admissions_of_one_key_leave_one_voucher_and_the_database_holds_the_checks()
    {
        await using var run = await Run.StartAsync(withMapping: true);

        var answers = await Task.WhenAll(
            run.AdmitAsync(CompanyA, Request(CompanyA)), run.AdmitAsync(CompanyA, Request(CompanyA)));
        Assert.Contains(answers, answer => answer.Outcome == AccountingSourceDocumentAdmissionOutcomeV1.Admitted && !answer.IsReplay);
        Assert.All(answers, answer => Assert.NotEqual(AccountingSourceDocumentAdmissionOutcomeV1.Refused, answer.Outcome));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId = @c", ("@c", CompanyA)));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.SourceDocumentVoucherDraftLines WHERE CompanyId = @c", ("@c", CompanyA)));

        // The checks and unique indexes refuse what the service would never write.
        var status = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE accounting.SourceDocumentAdmissions SET Status = 9 WHERE CompanyId = '" + CompanyA + "'"));
        Assert.Equal(547, status.Number);
        var amount = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE accounting.SourceDocumentVoucherDraftLines SET Amount = 0 WHERE CompanyId = '" + CompanyA + "'"));
        Assert.Equal(547, amount.Number);
        var side = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE accounting.SourceDocumentVoucherDraftLines SET Side = 3 WHERE CompanyId = '" + CompanyA + "'"));
        Assert.Equal(547, side.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.SourceDocumentAdmissions WHERE CompanyId = @c AND Status = 2", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task The_source_document_admission_migrations_Down_passes_while_empty_and_refuses_while_an_admission_exists()
    {
        await using var run = await Run.StartAsync(withMapping: true);
        var guard = Assert.IsType<SqlOperation>(new AddSourceDocumentAdmissions().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        Assert.Equal(AccountingSourceDocumentAdmissionOutcomeV1.Admitted, (await run.AdmitAsync(CompanyA, Request(CompanyA))).Outcome);

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51240, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.SourceDocumentAdmissions WHERE CompanyId = @c", ("@c", CompanyA)));
    }

    private static AccountingSourceDocumentAdmissionRequestV1 Request(Guid company) =>
        new(company, "SENTETIK-actor", "SENTETIK-corr", null, AccountingSourceDocumentKindsV1.SalesInvoiceKey(Invoice, 1),
            AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocument, Invoice, 1, Guid.Parse("e0420000-0000-4000-8000-000000000003"),
            Guid.Parse("e0420000-0000-4000-8000-000000000004"), Guid.Parse("e0420000-0000-4000-8000-000000000005"), null,
            DocumentDate, DocumentDate.AddDays(30), new("1200", "240", "0", "1440"), new("1200", "240", "0", "1440"),
            [
                new(1, null, null, false, "20", "1000", "200", "0"),
                new(2, null, null, false, "20", "200", "40", "0"),
            ]);

    private static HttpRequestMessage Command(HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return request;
    }

    private static string Key(string purpose) => $"SENTETIK-b04b-{purpose}-{Guid.NewGuid():N}";

    /// <summary>Admission rows, drafts, draft lines and audit events of one company.</summary>
    private sealed record Counts(int Admissions, int Drafts, int Lines, int Audits);

    private sealed class HostAuthority : IAccountingSourceDocumentAdmissionAuthorityV1
    {
        public Task<AccountingSourceDocumentAuthorityAnswerV1> CheckAsync(
            Guid companyId, string actorId, string documentKind, Guid sourceDocumentId, CancellationToken cancellationToken) =>
            Task.FromResult(new AccountingSourceDocumentAuthorityAnswerV1(
                AccountingSourceDocumentAuthorityDecisionV1.Allowed, "synthetic.invoices.finalize", ["SENTETIK-B04B"]));
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

        public static async Task<Run> StartAsync(bool withMapping)
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
                if (withMapping)
                {
                    await run.ConfigureMappingAsync();
                }

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
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK Accounting admission actor", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(
                    Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"ACC-ADMISSION-{roleId:N}", "SENTETIK Accounting admission test role", true);
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

        public async Task<AccountingSourceDocumentAdmissionAnswerV1> AdmitAsync(
            Guid company, AccountingSourceDocumentAdmissionRequestV1 request)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var companyContext = provider.GetRequiredService<ICompanyContext>();
            companyContext.Set(company);
            var service = new AccountingSourceDocumentAdmissionServiceV1(
                provider.GetRequiredService<IAccountingSourceDocumentAdmissionStoreV1>(),
                provider.GetRequiredService<IAccountingPostingAccountMappingResolverV1>(),
                new HostAuthority(), companyContext, provider.GetRequiredService<GoymenERP.BuildingBlocks.Observability.ICorrelationContext>());
            return await service.AdmitAsync(request);
        }

        public async Task<Counts> CountsAsync(Guid company) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceDocumentAdmissions WHERE CompanyId = @c", ("@c", company)),
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId = @c", ("@c", company)),
            await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceDocumentVoucherDraftLines WHERE CompanyId = @c", ("@c", company)),
            await ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId = @c AND EntityType = N'Accounting.SourceDocumentAdmission'",
                ("@c", company)));

        public async Task ConfigureMappingAsync()
        {
            await GrantAsync(Preparer, AccountingPostingAccountMappingPermissionNamesV1.Prepare);
            await GrantAsync(Checker, AccountingPostingAccountMappingPermissionNamesV1.Activate);
            using var preparerClient = Client(Preparer);
            using var checkerClient = Client(Checker);
            var prepare = Command(HttpMethod.Post, MappingsPath, Key("prepare"));
            prepare.Content = JsonContent.Create(new
            {
                expectedLatestMappingVersion = 0,
                effectiveFrom = "2026-11-01",
                rows = new object[]
                {
                    new { purpose = "sales-invoice", mappingKey = "receivable", accountId = Receivable120 },
                    new { purpose = "sales-invoice", mappingKey = "revenue", accountId = Revenue600 },
                    new { purpose = "sales-invoice", mappingKey = "vat-output", accountId = VatOutput391 },
                },
            });
            using (var prepared = await preparerClient.SendAsync(prepare))
            {
                Assert.Equal(HttpStatusCode.Created, prepared.StatusCode);
            }

            var activate = Command(HttpMethod.Post, $"{MappingsPath}/1/activation", Key("activate"));
            activate.Content = JsonContent.Create(new { expectedVersion = 1, expectedActiveMappingVersion = 0 });
            using var activated = await checkerClient.SendAsync(activate);
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
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
                + $"{Export601:D},601.01,SENTETIK yurt disi satislar\n"
                + $"{VatOutput391:D},391.01,SENTETIK hesaplanan kdv\n");
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
                { new StringContent("mikro-synthetic-b04b"), "accountPlanSourceVersion" },
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
