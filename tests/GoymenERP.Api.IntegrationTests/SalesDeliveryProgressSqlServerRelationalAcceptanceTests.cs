using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application;
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
/// AT-024 ("Kısmi teslimat sipariş kalanını doğru tutar") against a REAL SQL Server and the REAL host composition: a
/// delivery recorded through the real Logistics delivery route shows on the Sales order's delivery progress the next time
/// the order is opened (read afresh through the Logistics-owned <see cref="ISalesOrderDeliveryReadPortV1"/>, never from
/// Logistics tables by Sales); the same delivery key replayed is answered from the stored attempt and adds nothing; a
/// second delivery adds exactly its quantity; the read itself writes nothing in either module; another company's
/// deliveries never reach the order and another company cannot read it (404); without the draft read it is 403.
/// <para>
/// The Sales draft is stored and approved through the real Sales store from ISOLATED SYNTHETIC owner snapshots (the
/// SalesDeliverySchedule precedent); the instruction is created, issued, loaded and SHIPPED through the real Logistics
/// store (a definite shipment's Inventory half is LOG-5's own acceptance, not this read's); the DELIVERY is the real
/// route with a fresh synthetic grant. The unit is the approved GDC kilogram (Ö1/Ö2): the delivery route verifies it
/// against the real Settings catalogue. No tolerance is judged (OPEN-004). SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesDeliveryProgressSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    // The Logistics realization acceptance's business day: the approved kg profile is in effect on it.
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7c01");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7c02");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7c03");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7c04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");
    private static readonly LogisticsMutationAudit Delivered = new("logistics.loading-instruction.delivery-recorded.v1", "RecordDelivery", "SENTETIK teslim edildi");

    [SqlServerFact]
    public async Task A_delivery_recorded_in_logistics_shows_on_the_sales_order_after_reopen_and_its_replay_is_not_counted_twice_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        var (instructionId, lineId, shipmentId, version) = await run.ShippedInstructionAsync(CompanyA, draft, "k", shipped: 4m);

        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, LogisticsPermissionManifestV1.DeliveriesRecord);
        using var client = run.Client(subject, CompanyA);
        var progressPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/delivery-progress";

        // Shipped but nothing delivered yet: the whole order line remains.
        Assert.Equal(("4", "0", "6", "0"), Line(await run.ProgressAsync(client, progressPath), orderLine.Id));

        // The read writes nothing in either module (no attempt, audit, share or version effect).
        var before = await run.EffectsAsync(draft.Id, instructionId);
        await run.ProgressAsync(client, progressPath);
        Assert.Equal(before, await run.EffectsAsync(draft.Id, instructionId));

        // A partial delivery through the REAL Logistics route; the order, opened again, shows it and keeps the exact remainder.
        var deliveryId = Guid.NewGuid();
        using (var delivered = await client.SendAsync(Delivery(instructionId, lineId, "SENTETIK-delivery-1", version, deliveryId, shipmentId, "2.5")))
        {
            Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
            Assert.False(delivered.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(("4", "2.5", "3.5", "0"), Line(await run.ProgressAsync(client, progressPath), orderLine.Id));

        // The same delivery again under its key: the stored answer, replayed — no second share, nothing counted twice.
        using (var replayed = await client.SendAsync(Delivery(instructionId, lineId, "SENTETIK-delivery-1", version, deliveryId, shipmentId, "2.5")))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(("4", "2.5", "3.5", "0"), Line(await run.ProgressAsync(client, progressPath), orderLine.Id));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [logistics].[LoadingShares] WHERE [InstructionId] = @id AND [SourceShipmentId] = @s",
            ("@id", instructionId), ("@s", shipmentId)));

        // The same delivery id under ANOTHER key is the domain's duplicate refusal: still nothing counted twice.
        using (var duplicate = await client.SendAsync(Delivery(instructionId, lineId, "SENTETIK-delivery-dup", version + 1, deliveryId, shipmentId, "1")))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        }

        // A second delivery adds exactly its quantity.
        using (var second = await client.SendAsync(Delivery(instructionId, lineId, "SENTETIK-delivery-2", version + 1, Guid.NewGuid(), shipmentId, "1.5")))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }

        var reopened = await run.ProgressAsync(client, progressPath);
        Assert.Equal(("4", "4", "2", "0"), Line(reopened, orderLine.Id));
        Assert.Equal("4", reopened.GetProperty("draftVersion").GetString());
        Assert.Equal(4m, await run.ScalarAsync<decimal>(
            "SELECT [Delivered] FROM [logistics].[LoadingInstructionLines] WHERE [LineId] = @id", ("@id", lineId)));
        // Sales holds no copy of any of it: the approved draft is untouched (version 4, Approved, Rev.0).
        Assert.Equal((4L, 3, 0), (await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task Another_companys_deliveries_never_reach_the_order_and_only_a_fresh_draft_read_of_its_own_company_reads_it_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        // Company B records a shipment and a delivery naming company A's order line (a foreign binding): B's rows only.
        var (bInstruction, bLine, bShipment, bVersion) = await run.ShippedInstructionAsync(CompanyB, draft, "b", shipped: 3m);
        await using (var b = run.LogisticsStore(CompanyB))
        {
            await b.Store.MutateInstructionAsync(run.LogisticsEnvelope("b-deliver"), Hash("b-deliver"), LogisticsCommands.RecordDeliveryV1, bInstruction,
                (instruction, now) => instruction.MarkDelivered(bVersion, Guid.NewGuid(), bLine, bShipment, Kg(3m), now), Delivered);
        }

        // The owner port under each company: A sees nothing of B's records; B's own port sees only its own.
        await using (var a = run.LogisticsContext(CompanyA))
        {
            Assert.Empty((await new LogisticsSalesOrderDeliveryReadPort(a.Context, a.Company).ReadOrderDeliveriesAsync(draft.Id)).Lines);
        }

        await using (var b = run.LogisticsContext(CompanyB))
        {
            var answer = await new LogisticsSalesOrderDeliveryReadPort(b.Context, b.Company).ReadOrderDeliveriesAsync(draft.Id);
            Assert.Equal((CompanyB, orderLine.Id, 3m, 3m), (answer.CompanyId, Assert.Single(answer.Lines).OrderLineId,
                answer.Lines[0].ShippedQuantity, answer.Lines[0].DeliveredQuantity));
        }

        // The real Sales service over the real port of company A: the order line keeps its whole quantity.
        await using (var a = run.SalesStore(CompanyA))
        await using (var logistics = run.LogisticsContext(CompanyA))
        {
            var service = run.SalesService(a.Store, CompanyA, new LogisticsSalesOrderDeliveryReadPort(logistics.Context, logistics.Company));
            var progress = await service.GetDraftDeliveryProgressAsync(draft.Id);
            Assert.Equal(new SalesDraftLineDeliveryProgressV1(orderLine.Id, 1, KgUnit, "6", "0", "0", "6", "0"), Assert.Single(progress.Lines));
        }

        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/delivery-progress";
        // Company A's read-only user holds no Sales permission (none is seeded): 403 before anything is read.
        using (var ungranted = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ungranted.GetAsync(path)).StatusCode);
        }

        // Company B with its own draft read: A's draft does not exist for it (404, never 403).
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync(path)).StatusCode);
        }

        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var clientOfA = run.Client(readerOfA, CompanyA))
        {
            Assert.Equal(("0", "0", "6", "0"), Line(await run.ProgressAsync(clientOfA, path), orderLine.Id));
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static (string Shipped, string Delivered, string Remaining, string Excess) Line(JsonElement progress, Guid lineId)
    {
        var line = Assert.Single(progress.GetProperty("lines").EnumerateArray());
        Assert.Equal(lineId.ToString("D"), line.GetProperty("lineId").GetString());
        return (line.GetProperty("shipped").GetString()!, line.GetProperty("delivered").GetString()!,
            line.GetProperty("remaining").GetString()!, line.GetProperty("excess").GetString()!);
    }

    private static HttpRequestMessage Delivery(Guid instructionId, Guid lineId, string key, long expectedVersion, Guid deliveryId,
        Guid shipmentId, string quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{LogisticsEndpoints.InstructionsPath}/{instructionId:D}/lines/{lineId:D}/deliveries")
        {
            Content = JsonContent.Create(new RecordDeliveryRequest(expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                deliveryId, shipmentId, quantity, T0.AddMinutes(30))),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

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

    private sealed class ReadAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == SalesPermissionManifestV1.DraftsRead
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK-SALES-READER"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class NoActiveRoles : IActiveRoleCodeReader
    {
        public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    /// <summary>The owner ports a delivery-progress read never asks; reaching one is a failure of the case.</summary>
    private sealed class NoOwners : ISalesPartyAccountPortV1, ISettingsPrerequisiteProvider
    {
        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A delivery-progress read asks no such owner.");
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A delivery-progress read asks no such owner.");
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A delivery-progress read asks no such owner.");
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A delivery-progress read asks no such owner.");
    }

    /// <summary>(sales attempts, audit events, logistics attempts, shares of the instruction, draft version, instruction version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int LogisticsAttempts, int Shares, long DraftVersion, long InstructionVersion);

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
        public string Correlation { get; } = $"sales-at-024-{Guid.NewGuid():N}";

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
        /// loaded in full and shipped <paramref name="shipped"/> kg through the real Logistics store: its id, line, shipment, version.
        /// </summary>
        public async Task<(Guid InstructionId, Guid LineId, Guid ShipmentId, long Version)> ShippedInstructionAsync(Guid company,
            SalesOrderDraft draft, string keyPrefix, decimal shipped)
        {
            var orderLine = Assert.Single(draft.Lines);
            var shipmentId = Guid.NewGuid();
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
            var shippedInstruction = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loaded.Version, shipmentId, lineId, Kg(shipped), now),
                Shipped))!.Instruction.Instruction;
            return (id, lineId, shipmentId, shippedInstruction.Version);
        }

        public async Task<JsonElement> ProgressAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId, Guid instructionId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingShares] WHERE [InstructionId] = @id", ("@id", instructionId)),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)),
            await ScalarAsync<long>("SELECT [Version] FROM [logistics].[LoadingInstructions] WHERE [Id] = @id", ("@id", instructionId)));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key) =>
            new(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        /// <summary>The REAL draft service over a real store and the given delivery owner, the draft read granted and nothing else.</summary>
        public SalesDraftService SalesService(ISalesOrderDraftStore store, Guid company, ISalesOrderDeliveryReadPortV1 deliveries)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var owners = new NoOwners();
            return new SalesDraftService(companyContext, new FixedUser("SENTETIK-reader", company), new ReadAuthority(), new NoActiveRoles(),
                new FixedCorrelation(Correlation), owners, owners, null, store, new SalesDraftServiceBindings(), TimeProvider.System,
                deliveries: deliveries);
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales delivery-progress user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"AT-024-{roleId:N}", "Synthetic Sales delivery-progress role", true);
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
