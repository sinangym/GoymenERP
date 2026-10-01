using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SALES-P2 against a REAL SQL Server: Sales's own migration on top of the host's migrated and
/// seeded database, and Audit's real save interceptor.
/// <para>
/// The drafts stored here are built from ISOLATED SYNTHETIC owner snapshots (Product eligibility,
/// Settings unit and money profiles, Counterparty account): the store validates nothing about the
/// owners and stores only what the domain accepted, so these tests prove the draft store — the R3
/// identities, the tenant boundary, the race guard, the audit sidecar and the domain round-trip —
/// and nothing about any owner's rules. They are not a sales delivery and claim no user outcome:
/// with the real providers on main (Settings money profile unavailable, Product eligibility not
/// host-registered) a positive create is impossible today. Every identity, scale, unit and currency
/// below is SENTETIK / NOT_ENV test data; the number text is technical, not an official document (Ö8).
/// </para>
/// </summary>
public sealed class SalesDraftSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 18);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid Currency = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid MoneyProfile = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");
    private static readonly Guid KgUnit = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a93");
    private static readonly Guid KgProfile = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a94");
    private static readonly Guid Product = Guid.Parse("6e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a95");

    [SqlServerFact]
    public async Task A_draft_with_lines_and_slices_is_stored_replayed_and_audited_in_one_save()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        SalesDraftStoreResult first;
        await using (var a = run.Store(CompanyA))
        {
            first = await a.Store.StoreDraftAsync(run.Envelope("k-1"), Hash("payload-1"),
                documentNo => Draft(owners, documentNo, T0, lines: 2, slicesOnFirstLine: 2));
            Assert.Equal(SalesDraftWriteOutcome.Recorded, first.Outcome);
            // Version 1 at birth, +1 per added line (SALES-P1): two lines → 3. Rev.0 is unchanged.
            Assert.Equal(("SO-2026-000001", 3L, 2, 0), (first.Draft.DocumentNo, first.Draft.Version, first.Draft.Lines.Count, first.Draft.Revision));

            // The same key with the same content: the first stored draft again, nothing new.
            var replay = await a.Store.StoreDraftAsync(run.Envelope("k-1", correlation: "SENTETIK-second-correlation"), Hash("payload-1"),
                _ => throw new InvalidOperationException("a replay never builds"));
            Assert.Equal((SalesDraftWriteOutcome.Replayed, first.Draft.Id, run.Correlation), (replay.Outcome, replay.Draft.Id, replay.OriginalCorrelationId));

            var second = await a.Store.StoreDraftAsync(run.Envelope("k-2"), Hash("payload-2"), documentNo => Draft(owners, documentNo, T0.AddMinutes(1)));
            Assert.Equal("SO-2026-000002", second.Draft.DocumentNo);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftLines"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftDeliverySlices"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
        Assert.Equal((3L, 2L), (await run.ScalarAsync<long>("SELECT [Next] FROM sales.DraftNumbers"), await run.ScalarAsync<long>("SELECT [Version] FROM sales.DraftNumbers")));
        Assert.Equal("1.250", await run.ScalarAsync<string>(
            "SELECT CONVERT(varchar(32), Quantity) FROM sales.DraftLines WHERE DraftId = @id AND [LineNo] = 1", ("@id", first.Draft.Id)));
        Assert.Equal("1.25", await run.ScalarAsync<string>("SELECT QuantityText FROM sales.DraftLines WHERE DraftId = @id AND [LineNo] = 1", ("@id", first.Draft.Id)));
        // One audit event per stored draft, in the same save, under the request's correlation; none for the replay.
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft' AND CorrelationId = @c", ("@c", run.Correlation)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft' AND CorrelationId = N'SENTETIK-second-correlation'"));
        // The sales schema is EXACTLY the set of tables the Sales model maps into it, beside its own migration history —
        // the claim is exhaustive in both directions: no table the model does not know (no approval, shipment, invoice or
        // receipt table: those are other owners' records), and no mapped table missing. The model's set is itself pinned
        // by name (K3 Head B adds the two archived-revision tables), so neither side can grow unnoticed. Ordinal and
        // BIN2 order agree ("DraftR…" before "Drafts", "_" after the letters).
        string[] mapped;
        await using (var model = run.Context(CompanyA))
        {
            mapped = model.Model.GetEntityTypes()
                .Where(entity => entity.GetSchema() == SalesDbContext.Schema)
                .Select(entity => entity.GetTableName()!)
                .Distinct()
                .Append("__EFMigrationsHistory")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        Assert.Equal(["DraftAttempts", "DraftCancellations", "DraftDeliveryScheduleDates", "DraftDeliverySchedules", "DraftDeliverySlices", "DraftLines", "DraftNumbers", "DraftRemainderChangeDecisions", "DraftRemainderChanges", "DraftRevisionLines", "DraftRevisions", "Drafts", "__EFMigrationsHistory"], mapped);
        Assert.Equal(string.Join(',', mapped), await run.ScalarAsync<string>(
            "SELECT STRING_AGG(name, ',') WITHIN GROUP (ORDER BY name COLLATE Latin1_General_BIN2) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'sales')"));
        // ADR-011 §2: the raw key is not a column; the attempt row holds the command name, the key hash and the actor.
        Assert.Equal("ActorId,CommandName,CorrelationId,DraftId,IdempotencyKeyHash,Outcome,RecordedAtUtc,RefusalJson,RequestHash", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(name, ',') WITHIN GROUP (ORDER BY name COLLATE Latin1_General_BIN2) FROM sys.columns WHERE object_id = OBJECT_ID(N'sales.DraftAttempts') AND name <> N'CompanyId'"));
        Assert.Equal(SalesStore.IdempotencyKeyHash("k-1"), await run.ScalarAsync<string>(
            "SELECT IdempotencyKeyHash FROM sales.DraftAttempts WHERE DraftId = @id", ("@id", first.Draft.Id)));
    }

    [SqlServerFact]
    public async Task The_same_key_with_other_content_is_refused_and_a_racing_same_key_loses_cleanly()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreDraftAsync(run.Envelope("k-1"), Hash("payload-1"), documentNo => Draft(owners, documentNo, T0));
            // Same key, other content: refused, and the first answer is not given to this content.
            var mismatch = await Assert.ThrowsAsync<SalesDraftStoreException>(() => a.Store.StoreDraftAsync(run.Envelope("k-1"), Hash("payload-9"),
                _ => throw new InvalidOperationException("a mismatch never builds")));
            Assert.Equal(SalesDraftStoreCodes.SameKeyPayloadMismatch, mismatch.Code);
        }

        await using (var a = run.Store(CompanyA))
        {
            // Same key, same content, ANOTHER actor: the same definite refusal another content gets,
            // so the answer discloses neither the stored draft nor that the key is someone else's
            // (ADR-011 §3); nothing is written for the second actor.
            var otherActor = await Assert.ThrowsAsync<SalesDraftStoreException>(() => a.Store.StoreDraftAsync(
                run.Envelope("k-1", actor: "SENTETIK-other-actor"), Hash("payload-1"), _ => throw new InvalidOperationException("never built")));
            Assert.Equal(SalesDraftStoreCodes.SameKeyPayloadMismatch, otherActor.Code);
        }

        // Two writers with the same new key both pass the attempt lookup and the counter read, then
        // wait for each other at the save: the counter's concurrency token and the attempt key let
        // exactly one through; the other stores nothing and its retry answers from the winner.
        var barrier = new Barrier(2);
        async Task<object> WriteAsync()
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                var result = await writer.Store.StoreDraftAsync(run.Envelope("k-race"), Hash("payload-race"),
                    documentNo => Draft(owners, documentNo, T0.AddMinutes(2)));
                return result.Outcome;
            }
            catch (SalesDraftStoreException exception)
            {
                return exception.Code;
            }
        }

        var results = await Task.WhenAll(Task.Run(WriteAsync), Task.Run(WriteAsync));
        Assert.Single(results, result => result is SalesDraftWriteOutcome.Recorded);
        Assert.Single(results, result => result is string code && code == SalesDraftStoreCodes.ConcurrentChange);

        await using (var a = run.Store(CompanyA))
        {
            var retry = await a.Store.StoreDraftAsync(run.Envelope("k-race"), Hash("payload-race"), _ => throw new InvalidOperationException("a replay never builds"));
            Assert.Equal((SalesDraftWriteOutcome.Replayed, "SO-2026-000002"), (retry.Outcome, retry.Draft.DocumentNo));
            // FindReplayAsync answers the stored draft too, with its first correlation, for the same actor and content.
            var found = await a.Store.FindReplayAsync(run.Envelope("k-race"), Hash("payload-race"));
            Assert.Equal((retry.Draft.Id, run.Correlation), (found!.Draft!.Id, found.OriginalCorrelationId));
            Assert.Null(found.Refusal);
            Assert.Null(await a.Store.FindReplayAsync(run.Envelope("k-new"), Hash("payload-new")));
        }

        // ADR-011 §7: a terminal refusal is recorded under its key and replayed as the same answer; the key
        // cannot then store a draft, and another content under it is the same 409 as for a stored draft.
        var refusal = new SalesDraftTerminalRefusal(409, "version_conflict", [new("lines[0].productId", "version_conflict")]);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.RecordRefusalAsync(run.Envelope("k-stale"), Hash("payload-stale"), refusal);
        }

        await using (var a = run.Store(CompanyA))
        {
            var replayed = await a.Store.FindReplayAsync(run.Envelope("k-stale"), Hash("payload-stale"));
            Assert.Null(replayed!.Draft);
            Assert.Equal((409, "version_conflict", "lines[0].productId", run.Correlation),
                (replayed.Refusal!.Status, replayed.Refusal.Code, replayed.Refusal.Errors[0].Field, replayed.OriginalCorrelationId));
            var taken = await Assert.ThrowsAsync<SalesDraftStoreException>(() => a.Store.StoreDraftAsync(run.Envelope("k-stale"), Hash("payload-stale"),
                documentNo => Draft(owners, documentNo, T0.AddMinutes(5))));
            Assert.Equal(SalesDraftStoreCodes.ConcurrentChange, taken.Code);
            var other = await Assert.ThrowsAsync<SalesDraftStoreException>(() => a.Store.FindReplayAsync(run.Envelope("k-stale"), Hash("payload-other")));
            Assert.Equal(SalesDraftStoreCodes.SameKeyPayloadMismatch, other.Code);
            var again = await Assert.ThrowsAsync<SalesDraftStoreException>(() => a.Store.RecordRefusalAsync(run.Envelope("k-stale"), Hash("payload-stale"), refusal));
            Assert.Equal(SalesDraftStoreCodes.ConcurrentChange, again.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE [Outcome] = 2 AND [DraftId] IS NULL AND ISJSON([RefusalJson]) = 1"));

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE ActorId = N'SENTETIK-other-actor'"));
        Assert.Equal(3L, await run.ScalarAsync<long>("SELECT [Next] FROM sales.DraftNumbers"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft'"));
    }

    [SqlServerFact]
    public async Task Company_B_can_neither_see_nor_replay_company_A_draft_and_the_same_key_is_its_own_attempt()
    {
        await using var run = await Run.StartAsync();
        var ownersA = Owners.For(CompanyA);
        var ownersB = Owners.For(CompanyB);
        SalesOrderDraft draftA;
        await using (var a = run.Store(CompanyA))
        {
            draftA = (await a.Store.StoreDraftAsync(run.Envelope("shared-key"), Hash("payload-a"), documentNo => Draft(ownersA, documentNo, T0))).Draft;
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindDraftAsync(draftA.Id));
            Assert.Empty((await b.Store.ListDraftsAsync(10, null)).Items);
            // The same idempotency key in another company is its own attempt, with its own number text.
            var own = await b.Store.StoreDraftAsync(run.Envelope("shared-key"), Hash("payload-b"), documentNo => Draft(ownersB, documentNo, T0));
            Assert.Equal((SalesDraftWriteOutcome.Recorded, "SO-2026-000001"), (own.Outcome, own.Draft.DocumentNo));
            Assert.NotEqual(draftA.Id, own.Draft.Id);
            // A draft built for another company than the server's is refused before it is stored.
            var foreign = await Assert.ThrowsAsync<SalesDraftStoreException>(() => b.Store.StoreDraftAsync(run.Envelope("k-x"), Hash("payload-x"),
                documentNo => Draft(ownersA, documentNo, T0)));
            Assert.Equal(SalesDraftStoreCodes.CompanyContextMismatch, foreign.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE CompanyId = @c", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE CompanyId = @c", ("@c", CompanyB)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("shared-key"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("k-x"))));
    }

    [SqlServerFact]
    public async Task A_stored_draft_is_rebuilt_through_the_domain_and_listed_newest_first_with_a_company_bound_cursor()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var stored = new List<SalesOrderDraft>();
        await using (var a = run.Store(CompanyA))
        {
            for (var i = 0; i < 3; i++)
            {
                var at = T0.AddMinutes(i);
                stored.Add((await a.Store.StoreDraftAsync(run.Envelope($"k-{i}"), Hash($"payload-{i}"),
                    documentNo => Draft(owners, documentNo, at, lines: 2, slicesOnFirstLine: 2, withReferences: true))).Draft);
            }
        }

        await using (var a = run.Store(CompanyA))
        {
            var original = stored[0];
            var restored = await a.Store.FindDraftAsync(original.Id);
            Assert.NotNull(restored);
            Assert.Equal((original.DocumentNo, original.Version, original.CreatedAtUtc, original.Header, original.Money),
                (restored.DocumentNo, restored.Version, restored.CreatedAtUtc, restored.Header, restored.Money));
            Assert.Equal(original.Lines.Select(Shape), restored.Lines.Select(Shape));
            Assert.Equal(original.Lines[0].RequestedDeliverySlices.Select(slice => (slice.SliceId, slice.QuantityText, slice.Quantity.Value, slice.RequestedDeliveryDate)),
                restored.Lines[0].RequestedDeliverySlices.Select(slice => (slice.SliceId, slice.QuantityText, slice.Quantity.Value, slice.RequestedDeliveryDate)));

            var page = await a.Store.ListDraftsAsync(2, null);
            Assert.Equal([stored[2].Id, stored[1].Id], page.Items.Select(draft => draft.Id));
            Assert.NotNull(page.NextCursor);
            var rest = await a.Store.ListDraftsAsync(2, page.NextCursor);
            Assert.Equal([stored[0].Id], rest.Items.Select(draft => draft.Id));
            Assert.Null(rest.NextCursor);

            await using var b = run.Store(CompanyB);
            var foreign = await Assert.ThrowsAsync<SalesDraftStoreException>(() => b.Store.ListDraftsAsync(2, page.NextCursor));
            Assert.Equal(SalesDraftStoreCodes.InvalidCursor, foreign.Code);
        }
    }

    [SqlServerFact]
    public async Task A_save_that_fails_in_the_database_leaves_nothing_behind()
    {
        await using var run = await Run.StartAsync();
        // A company Organization does not know: the foreign key to organization.Companies refuses the
        // save (547), and the counter, the draft, its lines, the attempt and the audit event roll back together.
        var unknownCompany = Guid.NewGuid();
        var owners = Owners.For(unknownCompany);
        await using (var store = run.Store(unknownCompany))
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => store.Store.StoreDraftAsync(run.Envelope("k-1"), Hash("payload-1"),
                documentNo => Draft(owners, documentNo, T0)));
            Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftNumbers WHERE CompanyId = @c", ("@c", unknownCompany)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftLines"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft'"));
    }

    [SqlServerFact]
    public async Task A_key_that_differs_only_by_surrounding_whitespace_is_refused_and_never_replays_the_stored_attempt()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        // One scope per attempt, as one request is one scope: EF's change tracker would otherwise
        // refuse "K-1" beside "k-1" in the same context, although the table's binary collation keeps
        // them apart. The database, not the tracker, is the identity here.
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.StoreDraftAsync(run.Envelope("k-1"), Hash("payload-1"), documentNo => Draft(owners, documentNo, T0));
        }

        await using (var a = run.Store(CompanyA))
        {
            // SQL Server's equality ignores trailing spaces even under the binary collation, so the
            // store refuses a non-canonical key before it reaches the attempt table (the ACC-3 lesson).
            await Assert.ThrowsAsync<ArgumentException>(() => a.Store.StoreDraftAsync(run.Envelope("k-1 "), Hash("payload-9"),
                _ => throw new InvalidOperationException("not reached")));
        }

        await using (var a = run.Store(CompanyA))
        {
            // A different spelling is a different attempt under the binary collation.
            var other = await a.Store.StoreDraftAsync(run.Envelope("K-1"), Hash("payload-9"), documentNo => Draft(owners, documentNo, T0.AddMinutes(1)));
            Assert.Equal(SalesDraftWriteOutcome.Recorded, other.Outcome);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
    }

    private static (int LineNo, Guid Id, string QuantityText, decimal Quantity, ProductSnapshot Product, UnitProfileSnapshot OrderUnit,
        DateOnly? RequestedDeliveryDate, string? Note, CommercialDraftFields Commercial, int Slices) Shape(SalesOrderDraftLine line) =>
        (line.LineNo, line.Id, line.QuantityText, line.Quantity.Value, line.Product, line.OrderUnit, line.RequestedDeliveryDate, line.Note,
            line.Commercial, line.RequestedDeliverySlices.Count);

    private static string Hash(string seed) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A draft of the company from SYNTHETIC owner snapshots; the domain accepts it, the store stores it as accepted.</summary>
    private static SalesOrderDraft Draft(Owners owners, string documentNo, DateTimeOffset at, int lines = 1, int slicesOnFirstLine = 0,
        bool withReferences = false)
    {
        var header = new SalesDraftHeader(owners.Party, owners.Account, Currency, Day,
            RequestedDeliveryDate: withReferences ? Day.AddDays(7) : null, Note: withReferences ? "SENTETIK not" : null,
            DeliveryAddress: withReferences ? new OwnerReference(owners.Address, "2") : null);
        var money = new MoneyProfileSnapshot(owners.Company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), owners.Company, documentNo, header, money, at);
        var unit = new UnitProfileSnapshot(owners.Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(owners.Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        for (var i = 0; i < lines; i++)
        {
            var slices = i == 0 && slicesOnFirstLine > 0
                ? Enumerable.Range(1, slicesOnFirstLine).Select(n => new DeliverySliceInput(Guid.NewGuid(), KgUnit, "0.5", 0.5m, Day.AddDays(n))).ToArray()
                : null;
            var quantity = 1.25m + i;
            draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit,
                quantity.ToString(CultureInfo.InvariantCulture), quantity,
                RequestedDeliveryDate: withReferences ? Day.AddDays(10) : null,
                Note: withReferences ? "SENTETIK satır notu" : null,
                Commercial: withReferences ? new CommercialDraftFields("12.50", KgUnit, "5") : null,
                RequestedDeliverySlices: slices), at);
        }

        return draft;
    }

    /// <summary>The synthetic owner identities of one company.</summary>
    private sealed record Owners(Guid Company, Guid Party, Guid Account, Guid Address)
    {
        public static Owners For(Guid company) => new(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
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
        public string Actor { get; } = Guid.NewGuid().ToString("D");
        public string Correlation { get; } = $"sales-p2-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Sales's own migration
            // runs after it, as its foreign keys need Organization's tables. The host does not
            // register the Sales module yet (that is the API slice), so the migration is applied here.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new SalesDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public SalesDraftWriteEnvelope Envelope(string key, string? correlation = null, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], correlation ?? Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(Options(Factory), companyContext);
        }

        public StoreScope Store(Guid company, IInterceptor? extra = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var builder = new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (extra is not null)
            {
                builder.AddInterceptors(extra);
            }

            var dbContext = new SalesDbContext(builder.Options, companyContext);
            return new StoreScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
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

        private static DbContextOptions<SalesDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options;
    }

    private sealed class StoreScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>Holds each save until both writers have reached it, so both pass the attempt and counter reads first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }
}
