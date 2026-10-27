using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-22…24 (No 162, 163): the sales quote's persistence model without a database — its tables in the Sales schema under the
/// company filter, the keys and unique identities (one order per quote, a request under at most one live quote), the attempt
/// table's third target, the write guard (a quote moves only along its lifecycle; revisions and lines are append-only; a link
/// only deactivates; an order's quote source never changes), and the migration's guarded rollback. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesQuotePersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_quote_tables_are_in_the_sales_schema_filtered_by_the_company_and_keyed_by_their_identities()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(QuoteRow), typeof(QuoteRevisionRow), typeof(QuoteLineRow), typeof(QuoteRequestRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(SalesDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
        }

        var quotes = context.Model.FindEntityType(typeof(QuoteRow))!;
        Assert.Equal("Quotes", quotes.GetTableName());
        Assert.True(UniqueIndex(quotes, "UX_Quotes_DocumentNo"));
        Assert.True(quotes.FindProperty(nameof(QuoteRow.Version))!.IsConcurrencyToken);
        // No foreign key from the quote to its order: the order carries the reference (no cycle), the quote the fact.
        Assert.DoesNotContain(quotes.GetForeignKeys(), key => key.Properties.Any(property => property.Name == nameof(QuoteRow.ConvertedDraftId)));
        var revisions = context.Model.FindEntityType(typeof(QuoteRevisionRow))!;
        Assert.Equal(["CompanyId", "QuoteId", "Revision"], revisions.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var lines = context.Model.FindEntityType(typeof(QuoteLineRow))!;
        Assert.Equal(["CompanyId", "QuoteId", "Revision", "LineNo"], lines.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(lines, "UX_QuoteLines_LineId"));
        Assert.Contains(lines.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(QuoteRevisionRow));

        // No 163: a request is under at most ONE open or converted quote (a lost quote's link is inactive).
        var links = context.Model.FindEntityType(typeof(QuoteRequestRow))!;
        Assert.Equal(["CompanyId", "QuoteId", "RequestId"], links.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var active = links.GetIndexes().Single(index => index.GetDatabaseName() == "UX_QuoteRequests_ActiveRequest");
        Assert.True(active.IsUnique);
        Assert.Equal(SalesDbContext.ActiveQuoteRequestFilter, active.GetFilter());
        Assert.Contains(links.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(RequestRow));

        // No 162: one order per quote, by a filtered unique index on the order's source and a foreign key to the quote.
        var drafts = context.Model.FindEntityType(typeof(DraftRow))!;
        var source = drafts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_Drafts_SourceQuoteId");
        Assert.True(source.IsUnique);
        Assert.Equal(["CompanyId", "SourceQuoteId"], source.Properties.Select(property => property.Name));
        Assert.Equal(SalesDbContext.SourceQuoteFilter, source.GetFilter());
        Assert.Contains(drafts.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(QuoteRow));

        // ADR-SAL-001 §2: the quote commands have their OWN attempt table in the draft attempt's shape; the draft's attempt
        // table knows nothing of quotes.
        var attempts = context.Model.FindEntityType(typeof(QuoteAttemptRow))!;
        Assert.Equal(("QuoteAttempts", SalesDbContext.Schema), (attempts.GetTableName(), attempts.GetSchema()));
        Assert.Single(attempts.GetDeclaredQueryFilters());
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(attempts.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(QuoteRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "QuoteId"]));
        var createIndex = attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_QuoteAttempts_QuoteId");
        Assert.True(createIndex.IsUnique);
        Assert.Equal("[QuoteId] IS NOT NULL AND [CommandName] = N'sales.quote.create.v1'", createIndex.GetFilter());
        var draftAttempts = context.Model.FindEntityType(typeof(DraftAttemptRow))!;
        Assert.Null(draftAttempts.FindProperty("QuoteId"));
        Assert.DoesNotContain(draftAttempts.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(QuoteRow));

        var design = context.GetService<IDesignTimeModel>().Model;
        var designAttempts = design.FindEntityType(typeof(QuoteAttemptRow))!;
        Assert.Equal(SalesDbContext.IdentityCollation, designAttempts.FindProperty(nameof(QuoteAttemptRow.CommandName))!.GetCollation());
        Assert.Equal(SalesDbContext.IdentityCollation, designAttempts.FindProperty(nameof(QuoteAttemptRow.IdempotencyKeyHash))!.GetCollation());
        var attemptChecks = designAttempts.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(SalesDbContext.QuoteAttemptOutcomeConstraint, attemptChecks["CK_QuoteAttempts_Outcome"]);
        Assert.Equal(SalesDbContext.QuoteAttemptCommandConstraint, attemptChecks["CK_QuoteAttempts_Command"]);
        Assert.Equal("[ResultJson] IS NULL", attemptChecks["CK_QuoteAttempts_Result"]);
        Assert.Equal([SalesDbContext.QuoteAttemptImmutabilityTrigger], designAttempts.GetDeclaredTriggers().Select(trigger => trigger.GetDatabaseName()));
        // The conversion is the ORDER's command (ADR-SAL-001 §3): its attempt is a draft attempt, never a quote attempt.
        Assert.DoesNotContain(SalesQuoteCommands.ConvertV1, SalesDbContext.QuoteAttemptCommandConstraint, StringComparison.Ordinal);
        Assert.Equal(SalesDbContext.AttemptOutcomeConstraint,
            design.FindEntityType(typeof(DraftAttemptRow))!.GetCheckConstraints().Single(check => check.Name == "CK_DraftAttempts_Outcome").Sql);
        var quoteChecks = design.FindEntityType(typeof(QuoteRow))!.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(SalesDbContext.QuoteLifecycleConstraint, quoteChecks["CK_Quotes_Lifecycle"]);
        Assert.Contains("CK_Drafts_SourceQuote", design.FindEntityType(typeof(DraftRow))!.GetCheckConstraints().Select(check => check.Name));
    }

    [Theory]
    [InlineData("revise")]
    [InlineData("lose")]
    [InlineData("convert")]
    [InlineData("deactivate-link")]
    public void A_lifecycle_step_passes_the_write_guard(string step)
    {
        using var context = Context();
        switch (step)
        {
            case "revise":
                var revised = Attach(context, Quote(1));
                revised.Revision = 1;
                revised.Version = 2;
                revised.UpdatedAtUtc = At.AddMinutes(1);
                break;
            case "lose":
                var lost = Attach(context, Quote(1));
                lost.State = 3;
                lost.LossReason = "Fiyat";
                lost.LostBy = "SENTETIK-seller";
                lost.LostAtUtc = At.AddMinutes(1);
                lost.Version = 2;
                lost.UpdatedAtUtc = At.AddMinutes(1);
                break;
            case "convert":
                var converted = Attach(context, Quote(1));
                converted.State = 2;
                converted.ConvertedDraftId = Guid.NewGuid();
                converted.ConvertedBy = "SENTETIK-seller";
                converted.ConvertedAtUtc = At.AddMinutes(1);
                converted.Version = 2;
                converted.UpdatedAtUtc = At.AddMinutes(1);
                break;
            case "deactivate-link":
                Attach(context, Link(true)).IsActive = false;
                break;
        }

        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("lost-to-open")]
    [InlineData("converted-to-lost")]
    [InlineData("revision-backwards")]
    [InlineData("party")]
    [InlineData("money-profile")]
    [InlineData("document-no")]
    [InlineData("revision-terms")]
    [InlineData("line-price")]
    [InlineData("reactivate-link")]
    [InlineData("draft-source")]
    [InlineData("delete-quote")]
    [InlineData("other-company")]
    [InlineData("attempt-update")]
    [InlineData("attempt-delete")]
    public void Anything_else_on_a_quote_its_revisions_lines_links_or_an_orders_source_is_refused_before_the_database(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "lost-to-open":
                Attach(context, Quote(3)).State = 1;
                break;
            case "converted-to-lost":
                Attach(context, Quote(2)).State = 3;
                break;
            case "revision-backwards":
                var row = Quote(1);
                row.Revision = 2;
                row.Version = 3;
                Attach(context, row).Revision = 1;
                break;
            case "party":
                Attach(context, Quote(1)).PartyId = Guid.NewGuid();
                break;
            case "money-profile":
                Attach(context, Quote(1)).MoneyProfileVersion = "4";
                break;
            case "document-no":
                Attach(context, Quote(1)).DocumentNo = "SQ-2026-000002";
                break;
            case "revision-terms":
                Attach(context, Revision()).ValidUntil = new DateOnly(2026, 12, 1);
                break;
            case "line-price":
                Attach(context, Line()).UnitPrice = "13.00";
                break;
            case "reactivate-link":
                Attach(context, Link(false)).IsActive = true;
                break;
            case "draft-source":
                var draft = new DraftRow
                {
                    Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8],
                    SourceQuoteId = Guid.NewGuid(), SourceQuoteRevision = 0, CreatedAtUtc = At, UpdatedAtUtc = At,
                };
                Attach(context, draft).SourceQuoteId = Guid.NewGuid();
                break;
            case "delete-quote":
                context.Remove(Attach(context, Quote(1)));
                break;
            case "other-company":
                context.Quotes.Add(Quote(1, CompanyB));
                break;
            case "attempt-update":
                Attach(context, Attempt()).RequestHash = new string('1', 64);
                break;
            case "attempt-delete":
                context.Remove(Attach(context, Attempt()));
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_quote_migration_down_refuses_with_51066_before_it_drops_anything()
    {
        var down = new AddSalesQuotes().DownOperations;
        var guard = Assert.IsType<SqlOperation>(down[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [sales].[Quotes])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("EXISTS (SELECT 1 FROM [sales].[QuoteAttempts])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[SourceQuoteId] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("N'sales.quote.convert.v1'", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51066,", guard.Sql, StringComparison.Ordinal);
        Assert.All(down.Skip(1), operation => Assert.IsNotType<SqlOperation>(operation));
        var up = new AddSalesQuotes().UpOperations;
        foreach (var table in new[] { "Quotes", "QuoteRevisions", "QuoteLines", "QuoteRequests", "QuoteAttempts" })
        {
            Assert.Contains(up, operation => operation is CreateTableOperation { Schema: "sales" } create && create.Name == table);
        }

        Assert.Contains(up, operation => operation is AddColumnOperation { Table: "Drafts", Name: "SourceQuoteId", IsNullable: true });
        // ADR-SAL-001: the draft's attempt table is not touched (the conversion writes its existing shape).
        Assert.DoesNotContain(up, operation => operation is ITableMigrationOperation { Table: "DraftAttempts" or "RequestAttempts" });
        // No existing row is rewritten: the Up path's only raw SQL is the quote attempt table's immutability trigger (§6).
        Assert.DoesNotContain(up, operation => operation is UpdateDataOperation or DeleteDataOperation);
        var trigger = Assert.Single(up.OfType<SqlOperation>());
        Assert.Contains("CREATE TRIGGER [sales].[TR_QuoteAttempts_RejectMutation]", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains("AFTER UPDATE, DELETE", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51068,", trigger.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_quote_list_cursor_is_neither_a_request_nor_a_draft_cursor()
    {
        var id = Guid.NewGuid();
        var quoteCursor = SalesStore.ListCursor.Encode(CompanyA, At, id, SalesStore.ListCursor.QuoteOrdering);
        Assert.Equal((At, id), SalesStore.ListCursor.Decode(quoteCursor, CompanyA, SalesStore.ListCursor.QuoteOrdering));
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor,
            Assert.Throws<SalesDraftStoreException>(() => SalesStore.ListCursor.Decode(quoteCursor, CompanyA, SalesStore.ListCursor.RequestOrdering)).Code);
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor,
            Assert.Throws<SalesDraftStoreException>(() => SalesStore.ListCursor.Decode(quoteCursor, CompanyA)).Code);
        Assert.Equal("SQ-2026-000007", SalesStore.SeriesDocumentNo(SalesStore.QuoteSeries, At, 7));
    }

    private static bool UniqueIndex(IEntityType entity, string name) =>
        entity.GetIndexes().Any(index => index.IsUnique && index.GetDatabaseName() == name);

    private static T Attach<T>(SalesDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static QuoteRow Quote(int state, Guid? company = null) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company ?? CompanyA, DocumentNo = "SQ-2026-000001", State = state, Revision = 0,
        Version = state == 1 ? 1 : 2, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8],
        PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), CurrencyId = Guid.NewGuid(), BusinessDate = new DateOnly(2026, 10, 27),
        MoneyOperation = "sales.order-draft.v1", MoneyProfileId = Guid.NewGuid(), MoneyProfileVersion = "3",
        MoneyEffectiveOn = new DateOnly(2026, 10, 27), MoneyIsActive = true, MoneyEffectiveFrom = new DateOnly(2026, 9, 1), MoneyScale = 2,
        LossReason = state == 3 ? "Fiyat" : null, LostBy = state == 3 ? "SENTETIK-seller" : null, LostAtUtc = state == 3 ? At : null,
        ConvertedDraftId = state == 2 ? Guid.NewGuid() : null, ConvertedBy = state == 2 ? "SENTETIK-seller" : null,
        ConvertedAtUtc = state == 2 ? At : null,
        CreatedAtUtc = At, UpdatedAtUtc = At, CreatedBy = "SENTETIK-seller", CorrelationId = "SENTETIK-correlation",
    };

    private static QuoteAttemptRow Attempt() => new()
    {
        CompanyId = CompanyA, CommandName = SalesQuoteCommands.CreateV1, IdempotencyKeyHash = new string('A', 64),
        RequestHash = new string('0', 64), ActorId = "SENTETIK-preparer", Outcome = 1, QuoteId = Guid.NewGuid(),
        CorrelationId = "SENTETIK-correlation", RecordedAtUtc = At,
    };

    private static QuoteRevisionRow Revision() => new()
    {
        CompanyId = CompanyA, QuoteId = Guid.NewGuid(), Revision = 0, RevisedBy = "SENTETIK-seller", RevisedAtUtc = At,
        CorrelationId = "SENTETIK-correlation",
    };

    private static QuoteRequestRow Link(bool active) => new()
    {
        CompanyId = CompanyA, QuoteId = Guid.NewGuid(), RequestId = Guid.NewGuid(), IsActive = active,
    };

    private static QuoteLineRow Line() => new()
    {
        CompanyId = CompanyA, QuoteId = Guid.NewGuid(), Revision = 0, LineNo = 1, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
        ProductVersion = "7", ProductCode = "GDC-001", ProductName = "Sentetik bant", ProductEffectiveOn = new DateOnly(2026, 10, 27),
        StockUnitProfileVersion = "4", SalesUnitProfileVersion = "4", OrderUnitBindingProfileVersion = "4",
        OrderUnitCompanyId = CompanyA, OrderUnitId = Guid.NewGuid(), OrderUnitCode = "kg", OrderUnitDimensionCode = "weight",
        OrderUnitProfileId = Guid.NewGuid(), OrderUnitProfileVersion = "4", OrderUnitIsKilogram = true, OrderUnitProfileScale = 3,
        OrderUnitScale = 3, OrderUnitIsActive = true, OrderUnitEffectiveFrom = new DateOnly(2026, 9, 1), Quantity = 1m, QuantityText = "1",
        UnitPrice = "12.50", PriceUnitId = Guid.NewGuid(),
    };

    private static SalesDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .AddInterceptors(new GuardPassedInterceptor())
            .Options;
        return new SalesDbContext(options, company);
    }

    /// <summary>Thrown when a save reaches the provider pipeline, i.e. AFTER the write guard passed and before any connection.</summary>
    private sealed class GuardPassedException() : Exception("the Sales write guard passed");

    private sealed class GuardPassedInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => throw new GuardPassedException();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new GuardPassedException();
    }
}
