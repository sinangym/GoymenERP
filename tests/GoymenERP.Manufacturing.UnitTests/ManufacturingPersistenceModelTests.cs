using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// The persistent model without a database: schema, tenant filters, keys, the hand-written
/// migration against the model, and the write guard, which refuses before any connection is
/// opened. What SQL Server itself enforces is proven by the relational acceptance tests, not here.
/// </summary>
public sealed class ManufacturingPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);

    private static readonly Type[] TenantRows =
        [typeof(PlanRow), typeof(PlanInputRow), typeof(OrderRow), typeof(OrderInputRow), typeof(FactRow)];

    [Fact]
    public void Every_manufacturing_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in TenantRows)
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(ManufacturingDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Single(audit.GetDeclaredQueryFilters());
    }

    [Fact]
    public void Work_centers_plans_and_orders_are_referenced_by_company_and_id()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(PlanRow), typeof(OrderRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.GetTableName() == "WorkCenters"
                && key.PrincipalEntityType.GetSchema() == "organization"
                && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "WorkCenterId"]));
            Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
            Assert.True(entity.FindProperty("RowVersion")!.IsConcurrencyToken);
        }

        foreach (var (type, column, principal) in new[]
        {
            (typeof(PlanInputRow), "PlanId", typeof(PlanRow)),
            (typeof(OrderRow), "PlanId", typeof(PlanRow)),
            (typeof(OrderInputRow), "OrderId", typeof(OrderRow)),
            (typeof(FactRow), "OrderId", typeof(OrderRow)),
        })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == principal
                && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", column])
                && key.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"]));
        }

        // A fact id is one fact per company and kind: the key, not a check in code.
        var facts = context.Model.FindEntityType(typeof(FactRow))!;
        Assert.Equal(["CompanyId", "Kind", "Id"], facts.FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Fact]
    public void The_migration_is_discoverable_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_InitialManufacturing", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("no-company-context")]
    [InlineData("empty-company")]
    public void A_row_of_another_company_or_without_a_company_is_refused_before_the_database(string fault)
    {
        using var context = Context(resolved: fault != "no-company-context");
        var plan = Plan();
        plan.CompanyId = fault switch { "other-company" => CompanyB, "empty-company" => Guid.Empty, _ => CompanyA };
        context.Plans.Add(plan);

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("delete-fact")]
    [InlineData("edit-fact-mass")]
    [InlineData("edit-plan-work-center")]
    [InlineData("edit-order-plan")]
    [InlineData("edit-order-input")]
    [InlineData("edit-order-company")]
    public void Stored_manufacturing_is_never_deleted_and_only_its_allowed_fields_change(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete-fact":
                context.Facts.Remove(Attach(context, Fact()));
                break;
            case "edit-fact-mass":
                Attach(context, Fact()).MassKg = 2m;
                break;
            case "edit-plan-work-center":
                Attach(context, Plan()).WorkCenterId = Guid.NewGuid();
                break;
            case "edit-order-plan":
                Attach(context, Order()).PlanId = Guid.NewGuid();
                break;
            case "edit-order-input":
                Attach(context, new OrderInputRow
                {
                    CompanyId = CompanyA, OrderId = Guid.NewGuid(), ParentRollId = Guid.NewGuid(), Sequence = 1, PlannedKg = 1m,
                }).PlannedKg = 2m;
                break;
            default:
                Attach(context, Order()).CompanyId = CompanyB;
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static T Attach<T>(ManufacturingDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static PlanRow Plan() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, WorkCenterId = Guid.NewGuid(), Process = 1, Status = 1, Version = 1,
        PayloadHash = new string('0', 64), CreatedAtUtc = At, UpdatedAtUtc = At,
    };

    private static OrderRow Order() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, PlanId = Guid.NewGuid(), WorkCenterId = Guid.NewGuid(), Process = 1,
        Status = 1, Version = 1, CreatedAtUtc = At, UpdatedAtUtc = At,
    };

    private static FactRow Fact() => new()
    {
        CompanyId = CompanyA, Kind = (int)FactKind.InputIssue, Id = Guid.NewGuid(), OrderId = Guid.NewGuid(),
        ParentRollId = Guid.NewGuid(), MassKg = 1m, OccurredAtUtc = At, PayloadHash = new string('0', 64),
        RecordedAtUtc = At, RecordedBy = "synthetic-actor", CorrelationId = "synthetic-correlation",
    };

    /// <summary>A context of company A, or of no resolved company at all.</summary>
    private static ManufacturingDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var options = new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new ManufacturingDbContext(options, company);
    }
}
