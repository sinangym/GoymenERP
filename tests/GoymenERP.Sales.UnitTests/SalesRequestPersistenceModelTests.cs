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
/// API-20 / API-21: the sales request's persistence model without a database — its tables in the Sales schema under the
/// company filter, the keys and unique identities, the attempt-table widening (exactly one target per stored outcome), the
/// write guard (a request moves only along its decision; its lines and counters never change otherwise), and the migration's
/// guarded rollback. The relational behaviour is the SQL Server acceptance class. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesRequestPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_request_tables_are_in_the_sales_schema_filtered_by_the_company_and_keyed_by_their_identities()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(RequestRow), typeof(RequestLineRow), typeof(DocumentCounterRow), typeof(RequestAttemptRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(SalesDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains(entity.GetForeignKeys(), key => key.Properties.Any(property => property.Name == "CompanyId"));
        }

        var requests = context.Model.FindEntityType(typeof(RequestRow))!;
        Assert.Equal("Requests", requests.GetTableName());
        Assert.True(UniqueIndex(requests, "UX_Requests_DocumentNo"));
        Assert.True(requests.FindProperty(nameof(RequestRow.Version))!.IsConcurrencyToken);
        var lines = context.Model.FindEntityType(typeof(RequestLineRow))!;
        Assert.Equal(["CompanyId", "RequestId", "LineNo"], lines.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(UniqueIndex(lines, "UX_RequestLines_LineId"));
        Assert.Contains(lines.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(RequestRow));
        var counters = context.Model.FindEntityType(typeof(DocumentCounterRow))!;
        Assert.Equal(["CompanyId", "Series"], counters.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.True(counters.FindProperty(nameof(DocumentCounterRow.Version))!.IsConcurrencyToken);

        // ADR-SAL-001 §2: the request commands have their OWN attempt table in the draft attempt's shape — the same identity
        // (company, command, key hash) under the binary collation, a request target by foreign key, one create attempt per
        // request — and the draft's attempt table knows nothing of requests.
        var attempts = context.Model.FindEntityType(typeof(RequestAttemptRow))!;
        Assert.Equal("RequestAttempts", attempts.GetTableName());
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(attempts.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(RequestRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "RequestId"]));
        var createIndex = attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_RequestAttempts_RequestId");
        Assert.True(createIndex.IsUnique);
        Assert.Equal("[RequestId] IS NOT NULL AND [CommandName] = N'sales.request.create.v1'", createIndex.GetFilter());
        Assert.Null(attempts.FindProperty("IdempotencyKey"));
        var design = context.GetService<IDesignTimeModel>().Model;
        var designAttempts = design.FindEntityType(typeof(RequestAttemptRow))!;
        Assert.Equal(SalesDbContext.IdentityCollation, designAttempts.FindProperty(nameof(RequestAttemptRow.CommandName))!.GetCollation());
        Assert.Equal(SalesDbContext.IdentityCollation, designAttempts.FindProperty(nameof(RequestAttemptRow.IdempotencyKeyHash))!.GetCollation());
        var checks = designAttempts.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(SalesDbContext.RequestAttemptOutcomeConstraint, checks["CK_RequestAttempts_Outcome"]);
        Assert.Equal(SalesDbContext.RequestAttemptCommandConstraint, checks["CK_RequestAttempts_Command"]);
        Assert.Equal("[ResultJson] IS NULL", checks["CK_RequestAttempts_Result"]);
        Assert.Equal([SalesDbContext.RequestAttemptImmutabilityTrigger],
            design.FindEntityType(typeof(RequestAttemptRow))!.GetDeclaredTriggers().Select(trigger => trigger.GetDatabaseName()));
        var draftAttempts = context.Model.FindEntityType(typeof(DraftAttemptRow))!;
        Assert.Null(draftAttempts.FindProperty("RequestId"));
        Assert.DoesNotContain(draftAttempts.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(RequestRow));
        Assert.Equal(SalesDbContext.RequestDecisionConstraint,
            design.FindEntityType(typeof(RequestRow))!.GetCheckConstraints().Single(check => check.Name == "CK_Requests_Decision").Sql);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    public void A_decision_passes_the_write_guard_moving_only_the_state_its_facts_the_version_and_the_time(int from, int to)
    {
        using var context = Context();
        var row = Attach(context, Request(from));
        row.State = to;
        row.DecidedBy = "SENTETIK-approver";
        row.DecidedAtUtc = At.AddMinutes(1);
        row.RejectionReason = to == 3 ? "neden" : null;
        row.Version = 3;
        row.UpdatedAtUtc = At.AddMinutes(1);

        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("rejected-to-approved")]
    [InlineData("approved-to-submitted")]
    [InlineData("party")]
    [InlineData("business-date")]
    [InlineData("document-no")]
    [InlineData("line-quantity")]
    [InlineData("counter-series")]
    [InlineData("delete-request")]
    [InlineData("other-company")]
    [InlineData("attempt-update")]
    [InlineData("attempt-delete")]
    public void Anything_else_on_a_request_its_lines_or_its_counter_is_refused_before_the_database(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "rejected-to-approved":
                Attach(context, Request(3)).State = 2;
                break;
            case "approved-to-submitted":
                Attach(context, Request(2)).State = 1;
                break;
            case "party":
                Attach(context, Request(1)).PartyId = Guid.NewGuid();
                break;
            case "business-date":
                Attach(context, Request(1)).BusinessDate = new DateOnly(2026, 10, 27);
                break;
            case "document-no":
                Attach(context, Request(1)).DocumentNo = "SR-2026-000002";
                break;
            case "line-quantity":
                var line = Attach(context, Line());
                line.Quantity = 2m;
                line.QuantityText = "2";
                break;
            case "counter-series":
                Attach(context, new DocumentCounterRow { CompanyId = CompanyA, Series = "SR", Next = 2, Version = 1, UpdatedAtUtc = At })
                    .CompanyId = CompanyB;
                break;
            case "delete-request":
                context.Remove(Attach(context, Request(1)));
                break;
            case "other-company":
                context.Requests.Add(Request(1, CompanyB));
                break;
            case "attempt-update":
                Attach(context, Attempt()).ActorId = "SENTETIK-other";
                break;
            case "attempt-delete":
                context.Remove(Attach(context, Attempt()));
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_counter_moves_its_counter_only()
    {
        using var context = Context();
        var counter = Attach(context, new DocumentCounterRow { CompanyId = CompanyA, Series = "SR", Next = 2, Version = 1, UpdatedAtUtc = At });
        counter.Next = 3;
        counter.Version = 2;
        counter.UpdatedAtUtc = At.AddMinutes(1);

        Assert.Throws<GuardPassedException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_request_migration_down_refuses_with_51065_before_it_drops_anything()
    {
        var down = new AddSalesRequests().DownOperations;
        var guard = Assert.IsType<SqlOperation>(down[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [sales].[Requests]) OR EXISTS (SELECT 1 FROM [sales].[RequestAttempts])", guard.Sql,
            StringComparison.Ordinal);
        Assert.Contains("THROW 51065,", guard.Sql, StringComparison.Ordinal);
        Assert.All(down.Skip(1), operation => Assert.IsNotType<SqlOperation>(operation));
        var up = new AddSalesRequests().UpOperations;
        Assert.Contains(up, operation => operation is CreateTableOperation { Name: "Requests", Schema: "sales" });
        Assert.Contains(up, operation => operation is CreateTableOperation { Name: "RequestLines", Schema: "sales" });
        Assert.Contains(up, operation => operation is CreateTableOperation { Name: "RequestAttempts", Schema: "sales" });
        // ADR-SAL-001: no existing table is touched (the draft's attempt table stays as it is).
        Assert.DoesNotContain(up, operation => operation is ITableMigrationOperation { Table: not ("Requests" or "RequestLines" or "RequestAttempts" or "DocumentCounters") });
        // No existing row is rewritten: the Up path's only raw SQL is the attempt table's immutability trigger (ADR-SAL-001 §6).
        Assert.DoesNotContain(up, operation => operation is UpdateDataOperation or DeleteDataOperation);
        var trigger = Assert.Single(up.OfType<SqlOperation>());
        Assert.Contains("CREATE TRIGGER [sales].[TR_RequestAttempts_RejectMutation]", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains("AFTER UPDATE, DELETE", trigger.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51067,", trigger.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_request_list_cursor_is_not_a_draft_cursor_and_back()
    {
        var id = Guid.NewGuid();
        var requestCursor = SalesStore.ListCursor.Encode(CompanyA, At, id, SalesStore.ListCursor.RequestOrdering);
        Assert.Equal((At, id), SalesStore.ListCursor.Decode(requestCursor, CompanyA, SalesStore.ListCursor.RequestOrdering));
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor,
            Assert.Throws<SalesDraftStoreException>(() => SalesStore.ListCursor.Decode(requestCursor, CompanyA)).Code);
        var draftCursor = SalesStore.ListCursor.Encode(CompanyA, At, id);
        Assert.Equal(SalesDraftStoreCodes.InvalidCursor,
            Assert.Throws<SalesDraftStoreException>(() => SalesStore.ListCursor.Decode(draftCursor, CompanyA, SalesStore.ListCursor.RequestOrdering)).Code);
        Assert.Equal("SR-2026-000042", SalesStore.SeriesDocumentNo(SalesStore.RequestSeries, At, 42));
    }

    private static bool UniqueIndex(IEntityType entity, string name) =>
        entity.GetIndexes().Any(index => index.IsUnique && index.GetDatabaseName() == name);

    private static T Attach<T>(SalesDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static RequestRow Request(int state, Guid? company = null) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company ?? CompanyA, DocumentNo = "SR-2026-000001", State = state,
        Version = state == 1 ? 1 : 2, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8],
        DecidedBy = state == 1 ? null : "SENTETIK-approver", DecidedAtUtc = state == 1 ? null : At,
        RejectionReason = state == 3 ? "neden" : null,
        PartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), BusinessDate = new DateOnly(2026, 10, 26),
        CreatedAtUtc = At, UpdatedAtUtc = At, CreatedBy = "SENTETIK-preparer", CorrelationId = "SENTETIK-correlation",
    };

    private static RequestAttemptRow Attempt() => new()
    {
        CompanyId = CompanyA, CommandName = SalesRequestCommands.CreateV1, IdempotencyKeyHash = new string('A', 64),
        RequestHash = new string('0', 64), ActorId = "SENTETIK-preparer", Outcome = 1, RequestId = Guid.NewGuid(),
        CorrelationId = "SENTETIK-correlation", RecordedAtUtc = At,
    };

    private static RequestLineRow Line() => new()
    {
        CompanyId = CompanyA, RequestId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
        ProductVersion = "7", ProductCode = "GDC-001", ProductName = "Sentetik bant", ProductEffectiveOn = new DateOnly(2026, 10, 26),
        StockUnitProfileVersion = "4", SalesUnitProfileVersion = "4", OrderUnitBindingProfileVersion = "4",
        OrderUnitCompanyId = CompanyA, OrderUnitId = Guid.NewGuid(), OrderUnitCode = "kg", OrderUnitDimensionCode = "weight",
        OrderUnitProfileId = Guid.NewGuid(), OrderUnitProfileVersion = "4", OrderUnitIsKilogram = true, OrderUnitProfileScale = 3,
        OrderUnitScale = 3, OrderUnitIsActive = true, OrderUnitEffectiveFrom = new DateOnly(2026, 9, 1), Quantity = 1m, QuantityText = "1",
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
