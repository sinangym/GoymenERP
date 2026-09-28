using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-SAL-003 (KILITLI: "termin güncellemesi planlama/sevkiyat rolündedir ve yeniden onay gerektirmez, ancak auditlidir")
/// against a REAL SQL Server: the delivery-schedule update of a stored, APPROVED draft is ONE save that APPENDS the schedule
/// and its dates, moves only the header's version and timestamp, records the attempt and the audit event (dates before and
/// after) — or nothing at all; the draft reopens with the new dates and still Approved at Rev.0; the Sales line port (the
/// Logistics L-02 / allocation source) answers the new line date; the dates as created survive as schedule 0; the same key
/// replays without a second save; another company sees nothing; the migration refuses to roll back while a schedule exists.
/// <para>
/// The service runs under its own REAL name, <c>sales.orders.delivery-schedule.write</c>, registered centrally
/// (ErpPermissions.SalesOrdersDeliveryScheduleWrite); an unregistered name stays a 503 setup gap, pinned in
/// SalesDraftServiceTests. The drafts are built from ISOLATED SYNTHETIC owner snapshots. SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesDeliveryScheduleSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 26);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Currency = Guid.Parse("8b2e3d4c-5b6a-4798-8b1c-2d3e4f5a6b01");
    private static readonly Guid MoneyProfile = Guid.Parse("8b2e3d4c-5b6a-4798-8b1c-2d3e4f5a6b02");
    private static readonly Guid KgUnit = Guid.Parse("8b2e3d4c-5b6a-4798-8b1c-2d3e4f5a6b03");
    private static readonly Guid KgProfile = Guid.Parse("8b2e3d4c-5b6a-4798-8b1c-2d3e4f5a6b04");
    private static readonly Guid Product = Guid.Parse("8b2e3d4c-5b6a-4798-8b1c-2d3e4f5a6b05");

    /// <summary>The Sales owner's own schedule name, now registered in the central catalogue.</summary>
    private const string SchedulePermission = ErpPermissions.SalesOrdersDeliveryScheduleWrite;

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");

    [SqlServerFact]
    public async Task An_approved_orders_dates_move_in_one_appending_save_reopen_without_re_approval_and_reach_the_line_port_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var line = draft.Lines[0];
        var (sliceA, sliceB) = (line.RequestedDeliverySlices[0], line.RequestedDeliverySlices[1]);
        var request = new SalesDraftDeliveryScheduleRequest("4", Day.AddDays(30),
            [new SalesDraftLineScheduleV1(line.Id, Day.AddDays(31),
                [new SalesDraftSliceScheduleV1(sliceB.SliceId, Day.AddDays(12)), new SalesDraftSliceScheduleV1(sliceA.SliceId, Day.AddDays(11))])]);

        // (a) Without the schedule name: asked, answered no, nothing changes (the approve and draft names do not confer it).
        await using (var a = run.Store(CompanyA))
        {
            var approver = new RecordingAuthority(SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove);
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(a, "SENTETIK-approver", approver).RescheduleDeliveryAsync(draft.Id, request, run.Metadata("k-denied")));
            Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
            Assert.Equal([(SchedulePermission, CompanyA)], approver.Measured);
        }

        // (b) The ONE save, measured and refused: the whole change set reached it together, and SQL Server keeps nothing of it.
        var faulting = new SaveRecorder { Fault = true };
        await using (var a = run.Store(CompanyA, faulting))
        {
            await Assert.ThrowsAsync<SaveFault>(() => run.Service(a, "SENTETIK-planner", new RecordingAuthority(SchedulePermission))
                .RescheduleDeliveryAsync(draft.Id, request, run.Metadata("k-schedule")));
        }

        AssertScheduleChangeSet(Assert.Single(faulting.Saves), dateRows: 3);
        Assert.Equal((4L, 0), (await run.VersionAsync(draft.Id), await run.CountAsync("[sales].[DraftDeliverySchedules]", draft.Id)));
        Assert.Equal(0, await run.CountAsync("[sales].[DraftDeliveryScheduleDates]", draft.Id));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [IdempotencyKeyHash] = @h",
            ("@h", SalesStore.IdempotencyKeyHash("k-schedule"))));

        // (c) The planner's ONE save under the same key (nothing of the refused save was recorded): approved, Rev.0, version +1.
        var recorder = new SaveRecorder();
        await using (var a = run.Store(CompanyA, recorder))
        {
            var service = run.Service(a, "SENTETIK-planner", new RecordingAuthority(SchedulePermission, SalesPermissionManifestV1.DraftsRead));
            var moved = await service.RescheduleDeliveryAsync(draft.Id, request, run.Metadata("k-schedule"));
            Assert.False(moved.IsReplay);
            AssertScheduleChangeSet(Assert.Single(recorder.Saves), dateRows: 3);
            Assert.Equal(("Approved", "Rev.0", "5", Day.AddDays(30)), (moved.Detail.Draft.ApprovalState, moved.Detail.Draft.Revision, moved.Detail.Draft.Version,
                moved.Detail.RequestedDeliveryDate!.Value));

            // The same key again: the stored result as the replay, and NO second save.
            var replay = await service.RescheduleDeliveryAsync(draft.Id, request, run.Metadata("k-schedule", "SENTETIK-second"));
            Assert.Equal((true, run.Correlation), (replay.IsReplay, replay.OriginalCorrelationId));
            Assert.Single(recorder.Saves);

            // Reopen through a fresh read: the current dates are the schedule's, the history keeps schedule 0 as created.
            var history = await service.GetDraftDeliverySchedulesAsync(draft.Id);
            Assert.Equal([0, 1], history.Items.Select(item => item.ScheduleNo));
            Assert.Equal(((string?)null, (DateOnly?)Day.AddDays(10)), (history.Items[0].ChangedBy, history.Items[0].RequestedDeliveryDate));
            Assert.Equal(("SENTETIK-planner", (DateOnly?)Day.AddDays(30)), (history.Items[1].ChangedBy, history.Items[1].RequestedDeliveryDate));
            Assert.Equal([Day.AddDays(11), Day.AddDays(12)], history.Items[1].Lines[0].Slices.Select(slice => slice.RequestedDeliveryDate));
        }

        await using (var a = run.Store(CompanyA))
        {
            var reopened = (await a.Store.FindDraftAsync(draft.Id))!;
            Assert.Equal((InternalApprovalState.Approved, 0, 5L, Day.AddDays(31)),
                (reopened.ApprovalState, reopened.Revision, reopened.Version, reopened.Lines[0].RequestedDeliveryDate!.Value));
            Assert.Equal([(sliceA.SliceId, "0.5", Day.AddDays(11)), (sliceB.SliceId, "0.75", Day.AddDays(12))],
                reopened.Lines[0].RequestedDeliverySlices.Select(slice => (slice.SliceId, slice.QuantityText, slice.RequestedDeliveryDate)));

            // Downstream: the Sales line port (what Logistics L-02 and the allocation host read) answers the NEW line date.
            var page = await a.Port.ListApprovedLinesAsync(100, null);
            var approvedLine = Assert.Single(page.Items, item => item.LineId == line.Id);
            Assert.Equal((Day.AddDays(31), 0), (approvedLine.RequestedDeliveryDate!.Value, approvedLine.Revision));
        }

        // The rows as SQL Server holds them: the draft's own rows keep the dates as created (schedule 0, never rewritten),
        // the appended schedule holds the new ones, the approval axis is untouched, one attempt and one audit event.
        Assert.Equal((3, 0, Day.AddDays(10)), (await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            DateOnly.FromDateTime(await run.ScalarAsync<DateTime>("SELECT [RequestedDeliveryDate] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)))));
        Assert.Equal(Day.AddDays(1), DateOnly.FromDateTime(await run.ScalarAsync<DateTime>(
            "SELECT [RequestedDeliveryDate] FROM [sales].[DraftDeliverySlices] WHERE [DraftId] = @id AND [SliceNo] = 1", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftDeliverySchedules] WHERE [CompanyId] = @c AND [DraftId] = @id AND [ScheduleNo] = 1 "
            + "AND [ChangedBy] = N'SENTETIK-planner' AND [DraftVersion] = 5 AND [RequestedDeliveryDate] = @d AND [CorrelationId] = @corr",
            ("@c", CompanyA), ("@id", draft.Id), ("@d", Day.AddDays(30).ToDateTime(TimeOnly.MinValue)), ("@corr", run.Correlation)));
        Assert.Equal(3, await run.CountAsync("[sales].[DraftDeliveryScheduleDates]", draft.Id));
        Assert.Equal(Day.AddDays(12), DateOnly.FromDateTime(await run.ScalarAsync<DateTime>(
            "SELECT [RequestedDeliveryDate] FROM [sales].[DraftDeliveryScheduleDates] WHERE [DraftId] = @id AND [ScheduleNo] = 1 AND [SliceNo] = 2 AND [SliceId] = @s",
            ("@id", draft.Id), ("@s", sliceB.SliceId))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @cmd AND [DraftId] = @id AND [Outcome] = 1",
            ("@cmd", SalesDraftCommands.RescheduleDeliveryV1), ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Sales.OrderDraft' AND [EventType] = N'sales.order-draft.delivery-rescheduled.v1' "
            + "AND [EntityId] = @id AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5 AND [PermissionCode] = @perm",
            ("@id", draft.Id.ToString("D")), ("@perm", SchedulePermission)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = N'SENTETIK-second'"));

        // (d) A stale version is the key's terminal 409, recorded bound to the draft; nothing else moves.
        await using (var a = run.Store(CompanyA))
        {
            var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => run.Service(a, "SENTETIK-planner", new RecordingAuthority(SchedulePermission))
                .RescheduleDeliveryAsync(draft.Id, request with { RequestedDeliveryDate = Day.AddDays(45) }, run.Metadata("k-stale")));
            Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (stale.Status, stale.Code));
        }

        Assert.Equal((5L, 1), (await run.VersionAsync(draft.Id), await run.CountAsync("[sales].[DraftDeliverySchedules]", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [IdempotencyKeyHash] = @h AND [Outcome] = 2 AND [DraftId] = @id",
            ("@h", SalesStore.IdempotencyKeyHash("k-stale")), ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task Another_companys_draft_and_schedule_are_invisible_and_cannot_be_rescheduled_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var line = draft.Lines[0];
        var request = new SalesDraftDeliveryScheduleRequest("4", Day.AddDays(30),
            [new SalesDraftLineScheduleV1(line.Id, Day.AddDays(31),
                [.. line.RequestedDeliverySlices.Select(slice => new SalesDraftSliceScheduleV1(slice.SliceId, slice.RequestedDeliveryDate.AddDays(7)))])]);
        await using (var a = run.Store(CompanyA))
        {
            await run.Service(a, "SENTETIK-planner", new RecordingAuthority(SchedulePermission)).RescheduleDeliveryAsync(draft.Id, request, run.Metadata("k-a"));
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindDraftAsync(draft.Id));
            var service = run.Service(b, "SENTETIK-planner", new RecordingAuthority(SchedulePermission, SalesPermissionManifestV1.DraftsRead), company: CompanyB);
            var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                service.RescheduleDeliveryAsync(draft.Id, request with { ExpectedVersion = "5" }, run.Metadata("k-b")));
            Assert.Equal(404, missing.Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.GetDraftDeliverySchedulesAsync(draft.Id))).Status);
        }

        Assert.Equal((5L, 1), (await run.VersionAsync(draft.Id), await run.CountAsync("[sales].[DraftDeliverySchedules]", draft.Id)));
    }

    [SqlServerFact]
    public async Task The_schedule_migration_refuses_to_roll_back_while_any_schedule_exists()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var line = draft.Lines[0];
        await using (var a = run.Store(CompanyA))
        {
            await run.Service(a, "SENTETIK-planner", new RecordingAuthority(SchedulePermission)).RescheduleDeliveryAsync(draft.Id,
                new SalesDraftDeliveryScheduleRequest("4", Day.AddDays(30), [new SalesDraftLineScheduleV1(line.Id, line.RequestedDeliveryDate,
                    [.. line.RequestedDeliverySlices.Select(slice => new SalesDraftSliceScheduleV1(slice.SliceId, slice.RequestedDeliveryDate))])]),
                run.Metadata("k-a"));
        }

        await using var context = run.Context(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        // DEC-SAL-005's cancellations sit on top; with none stored they roll back freely, then the schedule guard refuses.
        Assert.EndsWith("_AddDraftCancellations", applied[^1], StringComparison.Ordinal);
        Assert.EndsWith("_AddDeliverySchedules", applied[^2], StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[^3]));
        Assert.Equal(51054, error.Number);
        Assert.Equal(applied[..^1], await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.CountAsync("[sales].[DraftDeliverySchedules]", draft.Id));
    }

    /// <summary>
    /// The change set of a schedule update's ONE save, asserted TOGETHER: the schedule and its date rows (Added), the draft
    /// header (Modified — EXACTLY UpdatedAtUtc and Version: no approval fact, no revision), the attempt and the audit event
    /// (Added). No existing line or slice row is in it: the dates as created are never rewritten.
    /// </summary>
    private static void AssertScheduleChangeSet(IReadOnlyList<SaveRecorder.Entry> save, int dateRows)
    {
        Assert.Equal(
            [
                "AuditEvent:Added", "DraftAttemptRow:Added",
                .. Enumerable.Repeat("DraftDeliveryScheduleDateRow:Added", dateRows),
                "DraftDeliveryScheduleRow:Added", "DraftRow:Modified",
            ],
            save.Select(entry => $"{entry.Entity}:{entry.State}").Order(StringComparer.Ordinal));
        Assert.Equal([nameof(DraftRow.UpdatedAtUtc), nameof(DraftRow.Version)], Assert.Single(save, entry => entry.Entity == nameof(DraftRow)).Modified);
    }

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (1.25 kg; slices 0.5 + 0.75; header date +10, line date +3) from SYNTHETIC owner snapshots: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "1.25", 1.25m, Day.AddDays(3),
            RequestedDeliverySlices: [new(Guid.NewGuid(), KgUnit, "0.5", 0.5m, Day.AddDays(1)), new(Guid.NewGuid(), KgUnit, "0.75", 0.75m, Day.AddDays(2))]), T0);
        return draft;
    }

    private sealed class RecordingAuthority(params string[] granted) : IErpAuthorityRevalidationService
    {
        public List<(string Permission, Guid Company)> Measured { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Measured.Add((permission, companyId));
            return Task.FromResult(granted.Contains(permission, StringComparer.Ordinal)
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK-PLANNER"], StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
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

    /// <summary>The owner ports a schedule update never asks; reaching one is a failure of the case, not a stub answer.</summary>
    private sealed class NoOwners : ISalesPartyAccountPortV1, ISettingsPrerequisiteProvider
    {
        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A schedule update asks no owner.");
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A schedule update asks no owner.");
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A schedule update asks no owner.");
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A schedule update asks no owner.");
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
        public string Correlation { get; } = $"sales-dec-sal-003-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        /// <summary>A stored draft of the company, submitted and approved (version 4), through the real store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix)
        {
            await using var scope = Store(company);
            var draft = (await scope.Store.StoreDraftAsync(Envelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo))).Draft;
            await scope.Store.UpdateDraftStateAsync(Envelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(Envelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        public SalesDraftWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], correlation ?? Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        /// <summary>The REAL draft service over this scope's real store, the schedule name bound to its registered real name.</summary>
        public SalesDraftService Service(StoreScope scope, string actor, IErpAuthorityRevalidationService authority, Guid? company = null)
        {
            var companyId = company ?? CompanyA;
            var companyContext = new CompanyContext();
            companyContext.Set(companyId);
            var owners = new NoOwners();
            return new SalesDraftService(companyContext, new FixedUser(actor, companyId), authority, new NoActiveRoles(),
                new FixedCorrelation(Correlation), owners, owners, null, scope.Store,
                new SalesDraftServiceBindings() with { DeliverySchedulePermission = SchedulePermission }, TimeProvider.System);
        }

        public SalesCommandMetadata Metadata(string key, string? correlation = null) => new(key, correlation ?? Correlation, null, T0);

        public SalesDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public StoreScope Store(Guid company, IInterceptor? extra = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var builder = new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (extra is not null)
            {
                builder.AddInterceptors(extra);
            }

            var dbContext = new SalesDbContext(builder.Options, companyContext);
            var store = new SalesStore(dbContext, companyContext, collector, TimeProvider.System);
            return new StoreScope(dbContext, store, new SalesOrderLineReferencePort(store, companyContext));
        }

        public Task<long> VersionAsync(Guid draftId) =>
            ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId));

        public Task<int> CountAsync(string table, Guid draftId) =>
            ScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE [DraftId] = @id", ("@id", draftId));

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

    private sealed class StoreScope(SalesDbContext dbContext, SalesStore store, SalesOrderLineReferencePort port) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ISalesOrderLineReferencePortV1 Port => port;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class SaveRecorder : SaveChangesInterceptor
    {
        public sealed record Entry(string Entity, EntityState State, string[] Modified);

        public List<IReadOnlyList<Entry>> Saves { get; } = [];

        public bool Fault { get; init; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves.Add(eventData.Context!.ChangeTracker.Entries()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(entry => new Entry(entry.Metadata.ClrType.Name, entry.State,
                    entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name).Order(StringComparer.Ordinal).ToArray()))
                .ToArray());
            return Fault ? throw new SaveFault() : ValueTask.FromResult(result);
        }
    }

    private sealed class SaveFault() : Exception("SENTETIK: the one save was refused before any statement reached SQL Server");
}
