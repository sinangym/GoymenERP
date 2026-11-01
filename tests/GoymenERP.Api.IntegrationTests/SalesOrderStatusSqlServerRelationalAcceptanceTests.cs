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
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using LogisticsQuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-SAL-001 / API-08 against a REAL SQL Server and the REAL host composition: the order's seven status axes read through
/// <c>GET /api/v1/sales/drafts/{id}/status</c>. The realization axis is judged from shipments recorded through the REAL
/// Logistics store (read by Sales only through the Logistics-owned <see cref="ISalesOrderDeliveryReadPortV1"/>) with the
/// company's delivery tolerance from the REAL Settings Sales policy record (<see cref="ApprovedSalesPolicyCatalog"/>, SAT-D5
/// 1 / 2 / 3 % for GDC from 2026-10-25): half shipped is partial and not banded (SAT-D16); 99.5 of 100 kg is within 1 % and
/// "Completed" (SAT-D5); an order whose business date precedes every approved tolerance is "PolicyUnavailable" — never a
/// default. Source, invoicing and financial closure are "NoSource". The read writes nothing in any module; another company's
/// shipments never reach the order and another company cannot read it (404); without the draft read it is 403.
/// <para>
/// The Sales drafts are stored and approved through the real Sales store from ISOLATED SYNTHETIC owner snapshots (the
/// SalesDeliveryProgress precedent); the instructions are created, issued, loaded and SHIPPED through the real Logistics store.
/// SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesOrderStatusSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    // A business day on which GDC's (company A) and GSP's (company B) approved SAT-D5 tolerance is in force (SAT-D14).
    private static readonly DateOnly Day = new(2026, 10, 26);
    // A business day before GDC's first approved tolerance (effective 2026-10-25): no policy in force.
    private static readonly DateOnly BeforePolicy = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("4a7e0c11-5d2b-4c8e-9f10-2b3c4d5e6f01");
    private static readonly Guid MoneyProfile = Guid.Parse("4a7e0c11-5d2b-4c8e-9f10-2b3c4d5e6f02");
    private static readonly Guid KgProfile = Guid.Parse("4a7e0c11-5d2b-4c8e-9f10-2b3c4d5e6f03");
    private static readonly Guid Product = Guid.Parse("4a7e0c11-5d2b-4c8e-9f10-2b3c4d5e6f04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");

    [SqlServerFact]
    public async Task Realization_is_judged_from_real_logistics_shipments_with_the_approved_settings_tolerance_and_without_one_it_is_policy_unavailable_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "100", Day);
        var orderLine = Assert.Single(draft.Lines);
        var (instructionId, lineId, _, version) = await run.ShippedInstructionAsync(CompanyA, draft, "k", instructed: 100m, shipped: 50m);

        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using var client = run.Client(subject, CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status";

        // Half shipped: partial, and the open remainder is not a shortfall (SAT-D16) — not banded.
        var status = await run.StatusAsync(client, path);
        Assert.Equal(["draftVersion", "revision", "lifecycle", "internalApproval", "customerAcceptance", "source", "realization",
            "invoicing", "financialClosure", "readyToClose"], status.EnumerateObject().Select(member => member.Name));
        // SAT-D32: approved → "Aktif"; not ready to close while the realization is partial.
        Assert.Equal(JsonValueKind.False, status.GetProperty("readyToClose").ValueKind);
        Assert.Equal(("4", "Rev.0", "Active", "Approved", "NotRequired"), (status.GetProperty("draftVersion").GetString(),
            status.GetProperty("revision").GetString(), State(status, "lifecycle"), State(status, "internalApproval"),
            State(status, "customerAcceptance")));
        Assert.Equal(("NoSource", "NoSource", "NoSource"), (State(status, "source"), State(status, "invoicing"), State(status, "financialClosure")));
        Assert.Equal("Partial", State(status, "realization"));
        Assert.Equal(JsonValueKind.False, status.GetProperty("realization").GetProperty("warning").ValueKind);
        // The tolerance is the REAL approved GDC record, asked for the order's business date.
        var gdc = ApprovedSalesPolicyCatalog.GdcDeliveryToleranceV1;
        var tolerance = status.GetProperty("realization").GetProperty("tolerance");
        Assert.Equal((gdc.ProfileId.ToString("D"), gdc.Version, "1", "2", "3", "2026-10-25", JsonValueKind.Null, "2026-10-26"),
            (tolerance.GetProperty("profileId").GetString(), tolerance.GetProperty("profileVersion").GetInt64(),
                tolerance.GetProperty("warnPercent").GetString(), tolerance.GetProperty("approvalPercent").GetString(),
                tolerance.GetProperty("blockPercent").GetString(), tolerance.GetProperty("effectiveFrom").GetString(),
                tolerance.GetProperty("effectiveThrough").ValueKind, tolerance.GetProperty("evaluatedOn").GetString()));
        Assert.Equal((orderLine.Id.ToString("D"), "100", "50", "0", "Partial", "Under", "50", null, null), Line(status));

        // The read writes nothing in either module (no attempt, audit, share or version effect).
        var before = await run.EffectsAsync(draft.Id, instructionId);
        await run.StatusAsync(client, path);
        Assert.Equal(before, await run.EffectsAsync(draft.Id, instructionId));

        // A second real shipment brings the line to 99.5 of 100 kg: 0.5 % short is within SAT-D5's 1 % — "Completed".
        await run.ShipMoreAsync(CompanyA, instructionId, lineId, version, "k-l-ship-2", 49.5m);
        var completed = await run.StatusAsync(client, path);
        Assert.Equal(("Completed", "Rev.0"), (State(completed, "realization"),
            completed.GetProperty("realization").GetProperty("basisRevision").GetString()));
        Assert.Equal((orderLine.Id.ToString("D"), "100", "99.5", "0", "Completed", "Under", "0.5", "Within", null), Line(completed));
        // SAT-D32 "tüm eksenler": approved, acceptance not required and realization complete, but source, invoicing and
        // financial closure have no owner on main (NoSource is never complete) — NOT ready to close; and nothing closes the
        // order: the stored lifecycle stays Draft (1).
        Assert.Equal(("NoSource", "NoSource", "NoSource"), (State(completed, "source"), State(completed, "invoicing"),
            State(completed, "financialClosure")));
        Assert.Equal(JsonValueKind.False, completed.GetProperty("readyToClose").ValueKind);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)));
        // Sales holds no copy of any of it: the approved draft is untouched (version 4, Approved, Rev.0).
        Assert.Equal((4L, 3, 0), (await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));

        // An order whose business date precedes every approved GDC tolerance, with a real shipment: fail-closed, no default.
        var early = await run.ApprovedDraftAsync(CompanyA, "e", "10", BeforePolicy);
        await run.ShippedInstructionAsync(CompanyA, early, "e", instructed: 10m, shipped: 4m);
        var unavailable = await run.StatusAsync(client, $"{SalesEndpoints.DraftsPath}/{early.Id:D}/status");
        Assert.Equal("PolicyUnavailable", State(unavailable, "realization"));
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("realization").GetProperty("tolerance").ValueKind);
        Assert.Equal((Assert.Single(early.Lines).Id.ToString("D"), "10", "4", "0", "PolicyUnavailable", null, null, null, null), Line(unavailable));
        // The other axes are still answered.
        Assert.Equal(("Active", "Approved"), (State(unavailable, "lifecycle"), State(unavailable, "internalApproval")));
        Assert.Equal(JsonValueKind.False, unavailable.GetProperty("readyToClose").ValueKind);
    }

    [SqlServerFact]
    public async Task Another_companys_shipments_never_reach_the_order_status_and_only_a_draft_read_of_its_own_company_reads_it_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6", Day);
        var orderLine = Assert.Single(draft.Lines);
        // Company B records a shipment naming company A's order line (a foreign binding): B's rows only.
        await run.ShippedInstructionAsync(CompanyB, draft, "b", instructed: 6m, shipped: 6m);

        // The owner port under company A sees nothing of B's records.
        await using (var a = run.LogisticsContext(CompanyA))
        {
            Assert.Empty((await new LogisticsSalesOrderDeliveryReadPort(a.Context, a.Company).ReadOrderDeliveriesAsync(draft.Id)).Lines);
        }

        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status";
        // Company A's read-only user holds no Sales permission (none is seeded): 403 before anything is read.
        using (var ungranted = run.Client(TestUsers.CompanyAViewer, CompanyA))
        using (var forbidden = await ungranted.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.True(forbidden.Headers.CacheControl is { Private: true, NoStore: true });
        }

        // Company B with its own draft read: A's draft does not exist for it (404, never 403).
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        using (var missing = await clientOfB.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.True(missing.Headers.CacheControl is { Private: true, NoStore: true });
        }

        // Company A's draft read: B's shipment is invisible — nothing started, so no tolerance was needed.
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using var clientOfA = run.Client(readerOfA, CompanyA);
        using var response = await clientOfA.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NotStarted", State(status, "realization"));
        Assert.Equal(JsonValueKind.Null, status.GetProperty("realization").GetProperty("tolerance").ValueKind);
        Assert.Equal((orderLine.Id.ToString("D"), "6", "0", "0", "NotStarted", "Under", "6", null, null), Line(status));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string? State(JsonElement status, string axis) => status.GetProperty(axis).GetProperty("state").GetString();

    private static (string? LineId, string? Ordered, string? Shipped, string? Delivered, string? State, string? Direction, string? Deviation,
        string? Band, string? ClosedRemainder) Line(JsonElement status)
    {
        var line = Assert.Single(status.GetProperty("realization").GetProperty("lines").EnumerateArray());
        // SAT-D29's warning marks only a completed warn-band over-delivery; Logistics ships no more than ordered, so never here.
        Assert.Equal((JsonValueKind.False, JsonValueKind.False), (line.GetProperty("warning").ValueKind,
            line.GetProperty("approvedOverDelivery").ValueKind));
        // SAT-D33: the real Logistics return owner answered — nothing approved is returned; shown apart, never netted.
        Assert.Equal("0", line.GetProperty("approvedReturned").GetString());
        return (line.GetProperty("lineId").GetString(), line.GetProperty("ordered").GetString(), line.GetProperty("shipped").GetString(),
            line.GetProperty("delivered").GetString(), line.GetProperty("state").GetString(), line.GetProperty("direction").GetString(),
            line.GetProperty("deviation").GetString(), line.GetProperty("band").GetString(), line.GetProperty("closedRemainder").GetString());
    }

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity, no slices) on the business day from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity, DateOnly day)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, day, day.AddDays(10), DueTerms: TestDueTerms.PointDue30);
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), day.AddDays(3)), T0);
        return draft;
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
        public string Correlation { get; } = $"sales-api-08-{Guid.NewGuid():N}";

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

        /// <summary>A stored draft of the company on the business day, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity, DateOnly day)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity, day))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        /// <summary>
        /// An instruction of <paramref name="company"/> for the draft's one line (<paramref name="instructed"/> kg, the approved kg
        /// unit), created, issued, loaded in full and shipped <paramref name="shipped"/> kg through the real Logistics store: its id,
        /// line, shipment, version.
        /// </summary>
        public async Task<(Guid InstructionId, Guid LineId, Guid ShipmentId, long Version)> ShippedInstructionAsync(Guid company,
            SalesOrderDraft draft, string keyPrefix, decimal instructed, decimal shipped)
        {
            var orderLine = Assert.Single(draft.Lines);
            var shipmentId = Guid.NewGuid();
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), draft.Header.BusinessDate, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(instructed), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loaded = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(instructed),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            var shippedInstruction = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loaded.Version, shipmentId, lineId, Kg(shipped), now),
                Shipped))!.Instruction.Instruction;
            return (id, lineId, shipmentId, shippedInstruction.Version);
        }

        /// <summary>One more definite shipment of <paramref name="shipped"/> kg from the loaded share, through the real Logistics store: the new version.</summary>
        public async Task<long> ShipMoreAsync(Guid company, Guid instructionId, Guid lineId, long version, string key, decimal shipped)
        {
            await using var scope = LogisticsStore(company);
            return (await scope.Store.MutateInstructionAsync(LogisticsEnvelope(key), Hash(key), LogisticsCommands.RecordShipmentV1, instructionId,
                (instruction, now) => instruction.MarkShipped(version, Guid.NewGuid(), lineId, Kg(shipped), now), Shipped))!.Instruction.Instruction.Version;
        }

        public async Task<JsonElement> StatusAsync(HttpClient client, string path)
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales status-axes user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"API-08-{roleId:N}", "Synthetic Sales status-axes role", true);
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
