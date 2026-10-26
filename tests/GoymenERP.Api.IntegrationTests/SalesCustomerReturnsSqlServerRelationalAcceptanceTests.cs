using System.Data;
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
using GoymenERP.Modules.Logistics.Domain.Returns;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LogisticsQuantity = GoymenERP.Modules.Logistics.Domain.Quantities.Quantity;
using LogisticsQuantityUnit = GoymenERP.Modules.Logistics.Domain.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D8 / API-10 (delegation No 21, 38) against a REAL SQL Server and the REAL host composition: a customer return
/// approved in Logistics shows on the Sales order's <c>customer-returns</c> read the next time the order is opened (read
/// afresh through the Logistics-owned <see cref="ISalesOrderCustomerReturnReadPortV1"/>, never from Logistics tables by
/// Sales); a pending or rejected request is not counted; the read itself writes nothing in either module; another company's
/// returns never reach the order and another company cannot read it (404); without the draft read it is 403.
/// <para>
/// The Sales draft is stored and approved through the real Sales store from ISOLATED SYNTHETIC owner snapshots (the
/// SalesDeliveryProgress precedent); the instruction is created, issued, loaded, shipped and DELIVERED through the real
/// Logistics store; the return is requested and decided through the real Logistics customer-return store under its
/// Serializable contract. The Inventory participant of the approval (the physical return movement) is LOG-RET's own
/// acceptance, not this read's: its identities are synthetic. The unit is the approved GDC kilogram (Ö1/Ö2). SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesCustomerReturnsSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7c11");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7c12");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7c13");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7c14");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");
    private static readonly LogisticsMutationAudit Delivered = new("logistics.loading-instruction.delivery-recorded.v1", "RecordDelivery", "SENTETIK teslim edildi");

    [SqlServerFact]
    public async Task An_approved_customer_return_shows_on_the_sales_order_after_reopen_and_pending_or_rejected_requests_do_not_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        var source = await run.DeliveredInstructionAsync(CompanyA, draft, "k", delivered: 4m);

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using var client = run.Client(reader, CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/customer-returns";

        // Delivered, nothing returned yet: the owner answered and the line shows a definite zero.
        Assert.Equal(("6", "0"), Line(await run.ReadAsync(client, path), orderLine.Id));

        // A pending request is not a return yet: nothing is counted.
        var first = await run.RequestReturnAsync(CompanyA, source, 1.5m, "k-r1");
        Assert.Equal(("6", "0"), Line(await run.ReadAsync(client, path), orderLine.Id));

        // Approved in Logistics: the order, opened again, shows the exact approved quantity in its order unit.
        await run.DecideReturnAsync(CompanyA, first, approve: true, "k-r1-approve");
        var opened = await run.ReadAsync(client, path);
        Assert.Equal(("6", "1.5"), Line(opened, orderLine.Id));
        Assert.Equal("4", opened.GetProperty("draftVersion").GetString());

        // A rejected request adds nothing; a second approved return adds exactly its quantity.
        var rejected = await run.RequestReturnAsync(CompanyA, source, 0.5m, "k-r2");
        await run.DecideReturnAsync(CompanyA, rejected, approve: false, "k-r2-reject");
        Assert.Equal(("6", "1.5"), Line(await run.ReadAsync(client, path), orderLine.Id));
        var second = await run.RequestReturnAsync(CompanyA, source, 0.75m, "k-r3");
        await run.DecideReturnAsync(CompanyA, second, approve: true, "k-r3-approve");
        Assert.Equal(("6", "2.25"), Line(await run.ReadAsync(client, path), orderLine.Id));

        // The read writes nothing in either module (no attempt, audit, return or version effect).
        var before = await run.EffectsAsync(draft.Id);
        await run.ReadAsync(client, path);
        Assert.Equal(before, await run.EffectsAsync(draft.Id));
        Assert.Equal((3, 2), (before.Returns, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [logistics].[CustomerReturns] WHERE [SourceOrderId] = @id AND [Status] = @approved",
            ("@id", draft.Id), ("@approved", (int)CustomerReturnStatus.Approved))));
        // Sales holds no copy: the approved draft is untouched (version 4, Approved, Rev.0).
        Assert.Equal((4L, 3, 0), (await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task Another_companys_customer_returns_never_reach_the_order_and_only_a_fresh_draft_read_of_its_own_company_reads_it_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        // Company B delivers and approves a return naming company A's order line (a foreign binding): B's rows only.
        var bSource = await run.DeliveredInstructionAsync(CompanyB, draft, "b", delivered: 3m);
        var bReturn = await run.RequestReturnAsync(CompanyB, bSource, 2m, "b-r1");
        await run.DecideReturnAsync(CompanyB, bReturn, approve: true, "b-r1-approve");

        // The owner port under each company: A sees nothing of B's records; B's own port sees only its own.
        await using (var a = run.LogisticsContext(CompanyA))
        {
            var answer = await new LogisticsSalesOrderCustomerReturnReadPort(a.Context, a.Company).ReadApprovedReturnsAsync(draft.Id);
            Assert.Equal((CompanyA, draft.Id), (answer.CompanyId, answer.SalesOrderId));
            Assert.Empty(answer.Lines);
        }

        await using (var b = run.LogisticsContext(CompanyB))
        {
            var answer = await new LogisticsSalesOrderCustomerReturnReadPort(b.Context, b.Company).ReadApprovedReturnsAsync(draft.Id);
            Assert.Equal((CompanyB, orderLine.Id, KgUnit, 2m), (answer.CompanyId, Assert.Single(answer.Lines).SalesOrderLineId,
                answer.Lines[0].UnitId, answer.Lines[0].ApprovedReturnedQuantity));
        }

        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/customer-returns";
        // Company A's read-only user holds no Sales permission (none is seeded): 403 before anything is read.
        using (var ungranted = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync(path)).StatusCode);
        }

        // Company B with its own draft read: A's draft does not exist for it (404, never 403), its own returns notwithstanding.
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        {
            using var missing = await clientOfB.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.DoesNotContain("not_found", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var clientOfA = run.Client(readerOfA, CompanyA))
        {
            Assert.Equal(("6", "0"), Line(await run.ReadAsync(clientOfA, path), orderLine.Id));
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static (string Ordered, string ApprovedReturned) Line(JsonElement returns, Guid lineId)
    {
        var line = Assert.Single(returns.GetProperty("lines").EnumerateArray());
        Assert.Equal(lineId.ToString("D"), line.GetProperty("lineId").GetString());
        Assert.Equal(KgUnit.ToString("D"), line.GetProperty("orderUnitId").GetString());
        return (line.GetProperty("ordered").GetString()!, line.GetProperty("approvedReturned").GetString()!);
    }

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static LogisticsQuantity PositiveKg(decimal value) => LogisticsQuantity.Positive(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity, no slices) from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>The delivered source of a return: instruction, its line, the shipment and delivery, and the delivered kg.</summary>
    private sealed record ReturnSource(Guid InstructionId, Guid LineId, Guid ShipmentId, Guid DeliveryId, Guid OrderId,
        Guid OrderLineId, decimal Delivered);

    /// <summary>(sales attempts, audit events, return attempts, returns, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int ReturnAttempts, int Returns, long DraftVersion);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class LogisticsScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public ILogisticsInstructionStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class LogisticsRead(LogisticsDbContext context, CompanyContext company) : IAsyncDisposable
    {
        public LogisticsDbContext Context => context;

        public CompanyContext Company => company;

        public ValueTask DisposeAsync() => context.DisposeAsync();
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
        public string Correlation { get; } = $"sales-api-10-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A stored draft of the company, submitted and approved (version 4), through the real Sales store.</summary>
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

        /// <summary>
        /// An instruction of <paramref name="company"/> for the draft's one line (6 kg, the approved kg unit), created, issued,
        /// loaded in full, shipped and delivered <paramref name="delivered"/> kg through the real Logistics store.
        /// </summary>
        public async Task<ReturnSource> DeliveredInstructionAsync(Guid company, SalesOrderDraft draft, string keyPrefix, decimal delivered)
        {
            var orderLine = Assert.Single(draft.Lines);
            var shipmentId = Guid.NewGuid();
            var deliveryId = Guid.NewGuid();
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(6m), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loaded = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(6m),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            var shipped = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loaded.Version, shipmentId, lineId,
                    Kg(delivered), now), Shipped))!.Instruction.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-deliver"), Hash($"{keyPrefix}-l-deliver"),
                LogisticsCommands.RecordDeliveryV1, id, (instruction, now) => instruction.MarkDelivered(shipped.Version, deliveryId, lineId,
                    shipmentId, Kg(delivered), now), Delivered);
            return new(id, lineId, shipmentId, deliveryId, draft.Id, orderLine.Id, delivered);
        }

        /// <summary>A pending return of <paramref name="quantity"/> kg against the delivery, through the real customer-return store.</summary>
        public async Task<Guid> RequestReturnAsync(Guid company, ReturnSource source, decimal quantity, string key)
        {
            var requested = CustomerReturn.Request(Guid.NewGuid(), company, source.InstructionId, source.LineId, source.DeliveryId,
                source.ShipmentId, source.OrderId, source.OrderLineId, KgUnit, PositiveKg(quantity), PositiveKg(source.Delivered),
                "SENTETIK iade", Actor, T0.AddHours(1), T0.AddHours(1));
            await ReturnStoreAsync(company, store => store.StoreRequestAsync(LogisticsEnvelope(key), Hash(key), requested));
            return requested.Id;
        }

        /// <summary>
        /// The decision of a pending return through the real customer-return store; the Inventory participant's identities are
        /// SYNTHETIC (the physical return movement is the customer-return slice's own acceptance).
        /// </summary>
        public Task DecideReturnAsync(Guid company, Guid customerReturnId, bool approve, string key) =>
            ReturnStoreAsync(company, async store =>
            {
                var pending = (await store.FindAsync(customerReturnId))!;
                const string decider = "SENTETIK-return-approver";
                if (approve)
                {
                    pending.Approve(pending.Version, decider, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0.AddHours(2));
                }
                else
                {
                    pending.Reject(pending.Version, decider, T0.AddHours(2), "SENTETIK red");
                }

                return await store.StoreDecisionAsync(LogisticsEnvelope(key, decider), Hash(key),
                    approve ? LogisticsCustomerReturnCommandsV1.Approve : LogisticsCustomerReturnCommandsV1.Reject, pending);
            });

        private async Task ReturnStoreAsync<T>(Guid company, Func<LogisticsCustomerReturnStore, Task<T>> operation)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            await using var dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            var store = new LogisticsCustomerReturnStore(dbContext, companyContext, collector, TimeProvider.System);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await operation(store);
            await transaction.CommitAsync();
        }

        public async Task<JsonElement> ReadAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[CustomerReturnAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[CustomerReturns] WHERE [SourceOrderId] = @id", ("@id", draftId)),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

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

        public LogisticsRead LogisticsContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new LogisticsRead(new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .Options, companyContext), companyContext);
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company (company-wide scope).</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales customer-returns user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"API-10-{roleId:N}", "Synthetic Sales customer-returns role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
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
