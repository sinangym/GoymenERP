using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SALES-P2 persistent model without a database: schema, tenant filters, the R3 identities, the
/// snapshot, the write guard (refuses before any connection), the store's argument guards, the
/// technical document number and the opaque list cursor. What SQL Server enforces is proven by the
/// relational acceptance tests, not here. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_sales_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in new[]
                 {
                     typeof(DraftNumberRow), typeof(DraftRow), typeof(DraftLineRow), typeof(DraftDeliverySliceRow), typeof(DraftAttemptRow),
                     typeof(DraftRevisionRow), typeof(DraftRevisionLineRow),
                     typeof(DraftDeliveryScheduleRow), typeof(DraftDeliveryScheduleDateRow),
                 })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(SalesDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
            Assert.Contains(entity.GetForeignKeys(), key => key.Properties.Any(property => property.Name == "CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Single(audit.GetDeclaredQueryFilters());
        var companies = context.Model.FindEntityType(typeof(OrganizationCompanyReference))!;
        Assert.Equal(("organization", "Companies"), (companies.GetSchema(), companies.GetTableName()));
    }

    [Fact]
    public void The_r3_identities_are_the_attempt_key_the_document_number_and_the_client_line_and_slice_ids()
    {
        using var context = Context();
        var attempts = context.Model.FindEntityType(typeof(DraftAttemptRow))!;
        // ADR-011 §2: (company, command name, key hash); no raw key column exists.
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Null(attempts.FindProperty("IdempotencyKey"));
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(DraftAttemptRow))!;
        Assert.Equal(SalesDbContext.IdentityCollation, design.FindProperty(nameof(DraftAttemptRow.CommandName))!.GetCollation());
        Assert.Equal(SalesDbContext.IdentityCollation, design.FindProperty(nameof(DraftAttemptRow.IdempotencyKeyHash))!.GetCollation());
        Assert.Equal((160, 64, 128), (attempts.FindProperty(nameof(DraftAttemptRow.CommandName))!.GetMaxLength(),
            attempts.FindProperty(nameof(DraftAttemptRow.IdempotencyKeyHash))!.GetMaxLength(),
            attempts.FindProperty(nameof(DraftAttemptRow.ActorId))!.GetMaxLength()));
        Assert.Equal((64, false), (attempts.FindProperty(nameof(DraftAttemptRow.RequestHash))!.GetMaxLength(),
            attempts.FindProperty(nameof(DraftAttemptRow.RequestHash))!.IsUnicode()));
        Assert.True(UniqueIndex(attempts, "UX_DraftAttempts_DraftId"));
        // ADR-011 §7: an attempt is a stored draft or a recorded terminal refusal; the draft index covers stored drafts only.
        // SALES-A1: it covers the CREATE attempt only (a draft is born from exactly one); the state commands' attempts
        // share the draft id, one per key, and stay unique by the primary key.
        Assert.Equal("[DraftId] IS NOT NULL AND [CommandName] = N'sales.order-draft.create.v1'",
            attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DraftAttempts_DraftId").GetFilter());
        Assert.Equal(SalesDbContext.CreateAttemptFilter,
            attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DraftAttempts_DraftId").GetFilter());
        Assert.True(attempts.FindProperty(nameof(DraftAttemptRow.DraftId))!.IsNullable);
        Assert.NotNull(attempts.FindProperty(nameof(DraftAttemptRow.Outcome)));
        Assert.NotNull(attempts.FindProperty(nameof(DraftAttemptRow.RefusalJson)));
        Assert.Equal(SalesDbContext.AttemptOutcomeConstraint,
            design.GetCheckConstraints().Single(constraint => constraint.Name == "CK_DraftAttempts_Outcome").Sql);

        var drafts = context.Model.FindEntityType(typeof(DraftRow))!;
        Assert.Equal(["Id"], drafts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(drafts.GetKeys(), key => !key.IsPrimaryKey() && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"]));
        Assert.True(UniqueIndex(drafts, "UX_Drafts_DocumentNo"));
        Assert.True(drafts.FindProperty(nameof(DraftRow.Version))!.IsConcurrencyToken);
        var rowVersion = drafts.FindProperty(nameof(DraftRow.RowVersion))!;
        Assert.Equal((true, ValueGenerated.OnAddOrUpdate), (rowVersion.IsConcurrencyToken, rowVersion.ValueGenerated));

        var lines = context.Model.FindEntityType(typeof(DraftLineRow))!;
        Assert.Equal(["CompanyId", "DraftId", "LineNo"], lines.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(lines, "UX_DraftLines_LineId"));
        // Ö2: the stored quantity is decimal(18,3), the text beside it is what the client sent.
        var quantity = lines.FindProperty(nameof(DraftLineRow.Quantity))!;
        Assert.Equal(18, quantity.GetPrecision());
        Assert.Equal(3, quantity.GetScale());

        var slices = context.Model.FindEntityType(typeof(DraftDeliverySliceRow))!;
        Assert.Equal(["CompanyId", "DraftId", "LineNo", "SliceNo"], slices.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(slices, "UX_DraftDeliverySlices_SliceId"));

        var numbers = context.Model.FindEntityType(typeof(DraftNumberRow))!;
        Assert.Equal(["CompanyId"], numbers.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(numbers.FindProperty(nameof(DraftNumberRow.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public void The_migration_is_discoverable_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_InitialSales", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddAttemptOutcome", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDeliverySchedules", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("no-company-context")]
    [InlineData("empty-company")]
    public void A_row_of_another_company_or_without_a_company_is_refused_before_the_database(string fault)
    {
        using var context = Context(resolved: fault != "no-company-context");
        var row = Draft();
        row.CompanyId = fault switch { "other-company" => CompanyB, "empty-company" => Guid.Empty, _ => CompanyA };
        context.Drafts.Add(row);

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("delete-draft")]
    [InlineData("edit-draft-header")]
    [InlineData("edit-draft-company")]
    [InlineData("edit-line")]
    [InlineData("edit-slice")]
    [InlineData("edit-attempt")]
    [InlineData("edit-counter-company")]
    [InlineData("edit-revision")]
    [InlineData("delete-revision")]
    [InlineData("edit-revision-line")]
    [InlineData("edit-schedule")]
    [InlineData("delete-schedule")]
    [InlineData("edit-schedule-date")]
    [InlineData("delete-schedule-date")]
    public void Drafts_lines_slices_and_attempts_never_change_and_nothing_is_deleted(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete-draft":
                context.Drafts.Remove(Attach(context, Draft()));
                break;
            case "edit-draft-header":
                Attach(context, Draft()).PartyId = Guid.NewGuid();
                break;
            case "edit-draft-company":
                Attach(context, Draft()).CompanyId = CompanyB;
                break;
            case "edit-line":
                Attach(context, Line()).Quantity = 2m;
                break;
            case "edit-slice":
                Attach(context, Slice()).RequestedDeliveryDate = new DateOnly(2026, 10, 1);
                break;
            case "edit-attempt":
                Attach(context, Attempt()).RequestHash = new string('1', 64);
                break;
            case "edit-revision":
                Attach(context, Revision()).ApprovedBy = "someone-else";
                break;
            case "delete-revision":
                context.DraftRevisions.Remove(Attach(context, Revision()));
                break;
            case "edit-revision-line":
                Attach(context, RevisionLine()).UnitPrice = "1";
                break;
            // DEC-SAL-003: a recorded delivery schedule is append-only — no column of it may change, nothing is deleted.
            case "edit-schedule":
                Attach(context, Schedule()).RequestedDeliveryDate = new DateOnly(2026, 11, 1);
                break;
            case "delete-schedule":
                context.DraftDeliverySchedules.Remove(Attach(context, Schedule()));
                break;
            case "edit-schedule-date":
                Attach(context, ScheduleDate()).RequestedDeliveryDate = new DateOnly(2026, 11, 1);
                break;
            case "delete-schedule-date":
                context.DraftDeliveryScheduleDates.Remove(Attach(context, ScheduleDate()));
                break;
            default:
                Attach(context, new DraftNumberRow { CompanyId = CompanyA, Next = 2, Version = 1, UpdatedAtUtc = At }).CompanyId = CompanyB;
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    /// <summary>
    /// K3 Head B — the DELIBERATE narrowing of "lines never change": ONLY the three commercial columns of a line
    /// and the header's revision number pass the write guard (a commercial revision changes them); the guard is what
    /// would have refused them before this head, so the cases above (quantity, header, slices, attempts, history)
    /// stay refused and these reach the provider pipeline. "Reached" is MEASURED by the context's sentinel interceptor
    /// (<see cref="GuardPassedException"/>): it fires only after the guard let the save through and before any
    /// connection. The provider itself is never a witness here — its connection failure differs by platform (a plain
    /// SqlException on Windows, EF's transient-failure InvalidOperationException on Linux), which is exactly what a
    /// type-based "anything but the guard's exception" assertion could not tell apart.
    /// </summary>
    [Theory]
    [InlineData("line-price")]
    [InlineData("line-discount")]
    [InlineData("header-revision")]
    public void Only_a_lines_commercial_columns_and_the_headers_revision_pass_the_write_guard(string change)
    {
        using var context = Context();
        switch (change)
        {
            case "line-price":
                var line = Attach(context, Line());
                line.UnitPrice = "12.50";
                line.PriceUnitId = Guid.NewGuid();
                break;
            case "line-discount":
                Attach(context, Line()).DiscountPercent = "5";
                break;
            default:
                Attach(context, Draft()).Revision = 1;
                break;
        }

        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }

    [Fact]
    public async Task The_store_refuses_a_non_canonical_key_a_malformed_hash_and_an_unresolved_company_before_any_read()
    {
        using var context = Context();
        var store = Store(context);
        var hash = new string('a', 64);

        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope(" k-1"), hash, _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope("k-1 "), hash, _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope(""), hash, _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope("k-1"), new string('A', 64), _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope("k-1"), "abc", _ => throw new InvalidOperationException("not reached")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreDraftAsync(Envelope("k-1") with { ActorId = " " }, hash, _ => throw new InvalidOperationException("not reached")));
        Assert.Equal(SalesDraftStoreCodes.ValueDoesNotFit, (await Assert.ThrowsAsync<SalesDraftStoreException>(
            () => store.StoreDraftAsync(Envelope(new string('k', 201)), hash, _ => throw new InvalidOperationException("not reached")))).Code);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ListDraftsAsync(0, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.ListDraftsAsync(101, null));

        using var unresolved = Context(resolved: false);
        var unresolvedStore = Store(unresolved);
        Assert.Equal(SalesDraftStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<SalesDraftStoreException>(
            () => unresolvedStore.StoreDraftAsync(Envelope("k-1"), hash, _ => throw new InvalidOperationException("not reached")))).Code);
        Assert.Equal(SalesDraftStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<SalesDraftStoreException>(
            () => unresolvedStore.FindDraftAsync(Guid.NewGuid()))).Code);
        Assert.Equal(SalesDraftStoreCodes.CompanyContextMismatch, (await Assert.ThrowsAsync<SalesDraftStoreException>(
            () => unresolvedStore.ListDraftsAsync(10, null))).Code);
    }

    [Fact]
    public void The_idempotency_key_is_stored_as_the_uppercase_sha256_of_its_trimmed_text()
    {
        // ADR-011 §2: no raw key; trimmed, then SHA-256, uppercase hex. Case in the key itself still counts.
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("k-1")));
        Assert.Equal(expected, SalesStore.IdempotencyKeyHash("k-1"));
        Assert.Equal(64, expected.Length);
        Assert.Equal(expected, expected.ToUpperInvariant());
        Assert.Equal(expected, SalesStore.IdempotencyKeyHash(" k-1 "));
        Assert.NotEqual(expected, SalesStore.IdempotencyKeyHash("K-1"));
        Assert.Equal("sales.order-draft.create.v1", SalesDraftCommands.CreateV1);
    }

    [Fact]
    public void The_document_number_is_technical_company_text_from_the_counter_and_the_utc_year()
    {
        Assert.Equal("SO-2026-000007", SalesStore.DocumentNo(new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.FromHours(3)), 7));
        // The year is UTC's: an Istanbul new-year hour is still the old year's number text.
        Assert.Equal("SO-2026-000008", SalesStore.DocumentNo(new DateTimeOffset(2027, 1, 1, 1, 0, 0, TimeSpan.FromHours(3)), 8));
        Assert.Equal("SO-2026-1000000", SalesStore.DocumentNo(At, 1_000_000));
        Assert.True(SalesStore.DocumentNo(At, long.MaxValue).Length <= SalesOrderDraft.MaximumDocumentNoLength);
    }

    [Fact]
    public void The_list_cursor_round_trips_and_is_bound_to_the_company_and_the_ordering()
    {
        var id = Guid.NewGuid();
        var cursor = SalesStore.ListCursor.Encode(CompanyA, At, id);
        Assert.DoesNotContain(cursor, character => character is '+' or '/' or '=');
        Assert.Equal((At, id), SalesStore.ListCursor.Decode(cursor, CompanyA));

        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, CodeOf(() => SalesStore.ListCursor.Decode(cursor, CompanyB)));
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, CodeOf(() => SalesStore.ListCursor.Decode("not a cursor!", CompanyA)));
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, CodeOf(() => SalesStore.ListCursor.Decode(Base64Url("{\"x\":1}"), CompanyA)));
        var otherOrdering = Base64Url("{\"company\":\"" + CompanyA + "\",\"ordering\":\"id asc\",\"createdAtUtc\":\"2026-09-18T09:00:00.0000000Z\",\"id\":\"" + id + "\"}");
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor, CodeOf(() => SalesStore.ListCursor.Decode(otherOrdering, CompanyA)));
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesDraftStoreException>(action).Code;

    private static string Base64Url(string json) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool UniqueIndex(IEntityType entity, string name) =>
        entity.GetIndexes().Single(index => index.GetDatabaseName() == name).IsUnique;

    private static SalesDraftWriteEnvelope Envelope(string key) =>
        new("synthetic-actor", "SENTETIK-sales-permission", ["SENTETIK-SALES"], "synthetic-correlation", null, key, "Web");

    /// <summary>The store over a context and the company context that context was built with.</summary>
    private static SalesStore Store(SalesDbContext context) =>
        new(context, Companies[context], new AuditEventCollector(), TimeProvider.System);

    private static readonly Dictionary<SalesDbContext, ICompanyContext> Companies = [];

    private static T Attach<T>(SalesDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static DraftRow Draft() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, DocumentNo = "SO-2026-000001", Kind = 1, State = 1, Revision = 0, Version = 1,
        RowVersion = [1, 2, 3, 4, 5, 6, 7, 8], PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(),
        BusinessDate = new DateOnly(2026, 9, 18), MoneyOperation = SalesOrderDraft.MoneyOperation, MoneyProfileId = Guid.NewGuid(),
        MoneyProfileVersion = "3", MoneyEffectiveOn = new DateOnly(2026, 9, 18), MoneyIsActive = true, MoneyEffectiveFrom = new DateOnly(2026, 9, 1),
        MoneyScale = 2, CreatedAtUtc = At, UpdatedAtUtc = At, CreatedBy = "synthetic-actor", CorrelationId = "synthetic-correlation",
    };

    private static DraftLineRow Line() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductVersion = "7",
        ProductCode = "GDC-001", ProductName = "Sentetik bant", ProductEffectiveOn = new DateOnly(2026, 9, 18), OrderUnitCode = "kg",
        OrderUnitDimensionCode = "weight", OrderUnitProfileVersion = "4", Quantity = 1m, QuantityText = "1",
    };

    private static DraftDeliverySliceRow Slice() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), LineNo = 1, SliceNo = 1, SliceId = Guid.NewGuid(), OrderUnitId = Guid.NewGuid(),
        Quantity = 1m, QuantityText = "1", RequestedDeliveryDate = new DateOnly(2026, 9, 25),
    };

    private static DraftRevisionRow Revision() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), Revision = 0, SubmittedBy = "prep", SubmittedAtUtc = At, ApprovedBy = "mgr", ApprovedAtUtc = At,
        MoneyProfileId = Guid.NewGuid(), MoneyProfileVersion = "3", MoneyScale = 2, ArchivedAtUtc = At, ArchivedBy = "pricer", CorrelationId = "synthetic-correlation",
    };

    private static DraftRevisionLineRow RevisionLine() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), Revision = 0, LineNo = 1, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductVersion = "7",
        OrderUnitId = Guid.NewGuid(), QuantityText = "1", UnitPrice = "12.50", PriceUnitId = Guid.NewGuid(), DiscountPercent = null,
    };

    /// <summary>
    /// DEC-SAL-003: a delivery-schedule change APPENDS a schedule and its dates and moves only the header's version and
    /// UpdatedAtUtc (both admitted before this head); the existing line and slice rows are never rewritten, so the save
    /// reaches the provider pipeline without widening what the guard admits.
    /// </summary>
    [Fact]
    public void A_schedule_change_passes_the_write_guard_by_appending_rows_and_moving_only_the_headers_version()
    {
        using var context = Context();
        var header = Attach(context, Draft());
        header.Version = 2;
        header.UpdatedAtUtc = At.AddMinutes(1);
        context.DraftDeliverySchedules.Add(Schedule());
        context.DraftDeliveryScheduleDates.Add(ScheduleDate());

        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_schedule_tables_are_keyed_by_draft_schedule_line_and_slice_and_bound_to_the_draft_and_its_lines()
    {
        using var context = Context();
        var schedules = context.Model.FindEntityType(typeof(DraftDeliveryScheduleRow))!;
        Assert.Equal(["CompanyId", "DraftId", "ScheduleNo"], schedules.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal("DraftDeliverySchedules", schedules.GetTableName());
        var dates = context.Model.FindEntityType(typeof(DraftDeliveryScheduleDateRow))!;
        Assert.Equal(["CompanyId", "DraftId", "ScheduleNo", "LineNo", "SliceNo"], dates.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(dates.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DraftDeliveryScheduleRow));
        Assert.Contains(dates.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DraftLineRow));
        Assert.Contains(schedules.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DraftRow));
        var design = context.GetService<IDesignTimeModel>().Model;
        Assert.Contains(design.FindEntityType(typeof(DraftDeliveryScheduleDateRow))!.GetCheckConstraints(),
            constraint => constraint.Name == "CK_DraftDeliveryScheduleDates_Slice");
    }

    private static DraftDeliveryScheduleRow Schedule() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), ScheduleNo = 1, RequestedDeliveryDate = new DateOnly(2026, 10, 1),
        ChangedBy = "synthetic-planner", ChangedAtUtc = At, DraftVersion = 2, CorrelationId = "synthetic-correlation",
    };

    private static DraftDeliveryScheduleDateRow ScheduleDate() => new()
    {
        CompanyId = CompanyA, DraftId = Guid.NewGuid(), ScheduleNo = 1, LineNo = 1, SliceNo = 1, SliceId = Guid.NewGuid(),
        RequestedDeliveryDate = new DateOnly(2026, 10, 2),
    };

    private static DraftAttemptRow Attempt() => new()
    {
        CompanyId = CompanyA, CommandName = SalesDraftCommands.CreateV1, IdempotencyKeyHash = new string('A', 64),
        RequestHash = new string('0', 64), ActorId = "synthetic-actor", Outcome = 1, DraftId = Guid.NewGuid(),
        CorrelationId = "synthetic-correlation", RecordedAtUtc = At,
    };

    /// <summary>A context of company A, or of no resolved company at all.</summary>
    private static SalesDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .AddInterceptors(new GuardPassedInterceptor())
            .Options;
        var context = new SalesDbContext(options, company);
        Companies[context] = company;
        return context;
    }

    /// <summary>
    /// Thrown the moment a save reaches the provider pipeline — AFTER the context's write guard passed (it runs before
    /// <c>base.SaveChanges</c>) and BEFORE any connection is opened. A guard refusal (InvalidOperationException) never
    /// reaches it, so the two theories above are told apart by type on every platform: a refusal that leaked through
    /// surfaces as this sentinel, never as the refusal's own type.
    /// </summary>
    private sealed class GuardPassedException() : Exception("the Sales write guard passed; the provider pipeline was reached");

    private sealed class GuardPassedInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => throw new GuardPassedException();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new GuardPassedException();
    }
}
