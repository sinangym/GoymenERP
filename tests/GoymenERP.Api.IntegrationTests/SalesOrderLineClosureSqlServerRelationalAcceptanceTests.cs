using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using LogisticsQuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D17 / SAT-D168 / SAT-D215 against a REAL SQL Server and the REAL host composition: a recorded line closure is stored in
/// one save through the real Sales store, rebuilt by the store's restore (counted by the version floor), refused a second time
/// for the same line, held by the identity check and the draft / line foreign keys, and the migration refuses to roll back over a
/// recorded closure (THROW 51227, forward fix). SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderLineClosureSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    // A business day on which GDC's (company A) approved SAT-D5 tolerance (1 / 2 / 3 %) is in force (SAT-D14).
    private static readonly DateOnly Day = new(2026, 10, 26);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a01");
    private static readonly Guid MoneyProfile = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a02");
    private static readonly Guid KgProfile = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a03");
    private static readonly Guid Product = Guid.Parse("5b8f1d22-6e3c-4d9f-8a21-3c4d5e6f7a04");
    private const string Shipper = "SENTETIK-shipper";

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit LineClosed =
        new("sales.order-draft.line-closed.v1", "CloseLine", "SENTETIK satır kapatıldı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");

    [SqlServerFact]
    public async Task The_line_closure_migrations_Down_passes_with_no_closure_and_refuses_while_a_closure_exists()
    {
        await using var run = await Run.StartAsync(owner: null);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100");
        var line = Assert.Single(draft.Lines);
        var guard = Assert.IsType<SqlOperation>(new AddDraftLineClosures().DownOperations[0]).Sql;
        // No closure: the guard passes (an empty table rolls back freely).
        await run.ScalarAsync<int>(guard + " SELECT 1");

        // One closure through the real Sales store: the domain's input as the command builds it (90 of 100 kg shipped, 10 % short).
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.CloseLineAsync(run.SalesEnvelope("k-close", "SENTETIK-closer"), Hash("k-close"),
                SalesDraftCommands.CloseLineV1, draft.Id,
                (d, input, now) => d.CloseLine(4, "SENTETIK-closer", input, now), LineClosed,
                (_, _) => Task.FromResult(new LineClosureInput(line.Id, 0, "100", 90m, DeliveryToleranceBand.Approval, [Shipper])));
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"));
        Assert.Equal(51227, error.Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var closuresIndex = Array.FindIndex(applied, id => id.EndsWith("_AddDraftLineClosures", StringComparison.Ordinal));
        Assert.True(closuresIndex > 0);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[closuresIndex - 1]));
        Assert.Equal(51227, rollback.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal((5L, "100", "90", "10"), (
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Ordered] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [ShippedAtClosure] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [CancelledRemainder] FROM [sales].[DraftLineClosures] WHERE [DraftId] = @id", ("@id", draft.Id))));

        // The rebuilt order carries its closure (the store's restore through the domain, counted by the version floor).
        await using (var reader = run.SalesStore(CompanyA))
        {
            var reread = await reader.Store.FindDraftAsync(draft.Id);
            var closure = Assert.Single(reread!.LineClosures);
            Assert.Equal((5L, "SENTETIK-closer", true), (reread.Version, closure.ClosedBy, reread.IsLineClosed(line.Id)));
        }

        // The database refuses a second closure of the same line, one under another company, one at a version below the floor and
        // one at the version the first closure already took.
        const string insert = "INSERT INTO [sales].[DraftLineClosures] ([CompanyId],[DraftId],[LineId],[LineNo],[BasisRevision],[Ordered],"
            + "[ShippedAtClosure],[CancelledRemainder],[Band],[ClosedBy],[ClosedAtUtc],[DraftVersion],[CorrelationId]) "
            + "VALUES (@company, @id, @line, 1, 0, N'100', N'90', N'10', 2, N'x', SYSDATETIMEOFFSET(), @version, N'x'); SELECT 1";
        Assert.Equal(2627, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyA), ("@id", draft.Id), ("@line", line.Id), ("@version", 6L)))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyB), ("@id", draft.Id), ("@line", line.Id), ("@version", 6L)))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(insert,
            ("@company", CompanyA), ("@id", draft.Id), ("@line", Guid.NewGuid()), ("@version", 4L)))).Number);
    }

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity) from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>(sales attempts, audit events, over-delivery approvals, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int Approvals, long DraftVersion);

    /// <summary>
    /// A SYNTHETIC delivery owner that NAMES the shipper (the real Logistics owner does not: SAT-D209). It answers only under the
    /// server-resolved company, creates nothing, and an order it holds nothing for is an empty list (the port's rules).
    /// </summary>
    private sealed class SyntheticDeliveries
    {
        private readonly ConcurrentDictionary<(Guid Company, Guid Order), SalesOrderLineDeliveryV1[]> lines = new();

        public void Set(Guid company, Guid order, params SalesOrderLineDeliveryV1[] entries) => lines[(company, order)] = entries;

        public SalesOrderDeliveryReadAnswerV1 Answer(Guid company, Guid order) =>
            new(company, order, lines.TryGetValue((company, order), out var entries) ? entries : []);
    }

    private sealed class SyntheticDeliveryPort(SyntheticDeliveries owner, ICompanyContext company) : ISalesOrderDeliveryReadPortV1
    {
        public Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(owner.Answer(company.CompanyId, orderId));
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class LogisticsScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public LogisticsStore Store => store;
        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
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
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-sat-d30-{Guid.NewGuid():N}";

        /// <summary>The real host; with <paramref name="owner"/>, its delivery port is the synthetic owner that names the shipper.</summary>
        public static async Task<Run> StartAsync(SyntheticDeliveries? owner)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = owner is null
                ? new SqlServerRelationalApiFactory(baseConnectionString)
                : new SqlServerRelationalApiFactory(baseConnectionString, services =>
                {
                    services.RemoveAll<ISalesOrderDeliveryReadPortV1>();
                    services.AddScoped<ISalesOrderDeliveryReadPortV1>(provider =>
                        new SyntheticDeliveryPort(owner, provider.GetRequiredService<ICompanyContext>()));
                });
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored draft of the company on the business day, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        /// <summary>An instruction for the draft's one line, issued, loaded and shipped <paramref name="shipped"/> kg through the real Logistics store.</summary>
        public async Task ShippedInstructionAsync(Guid company, SalesOrderDraft draft, string keyPrefix, decimal shipped)
        {
            var orderLine = Assert.Single(draft.Lines);
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), draft.Header.BusinessDate, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(shipped), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loaded = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(shipped),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loaded.Version, Guid.NewGuid(), lineId, Kg(shipped), now),
                Shipped);
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draftId)),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key) =>
            new(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public LogisticsScope LogisticsStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new LogisticsScope(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company (company-wide scope).</summary>
        public async Task<(string Subject, Guid UserId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales over-delivery user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SAT-D30-{roleId:N}", "Synthetic Sales over-delivery role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return (developmentUser, actor);
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
    }
}
