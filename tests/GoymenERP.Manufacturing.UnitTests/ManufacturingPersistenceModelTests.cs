using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

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
        [typeof(PlanRow), typeof(PlanInputRow), typeof(OrderRow), typeof(OrderInputRow), typeof(FactRow), typeof(CommandRow)];

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
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingCommands", StringComparison.Ordinal));
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
    [InlineData("edit-command-result")]
    [InlineData("delete-command")]
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
            case "edit-command-result":
                Attach(context, Command()).ResultJson = "{}";
                break;
            case "delete-command":
                context.Commands.Remove(Attach(context, Command()));
                break;
            default:
                Attach(context, Order()).CompanyId = CompanyB;
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_kilogram_limit_is_the_mass_columns_decimal_18_3_bound()
    {
        // decimal(18,3) holds 15 integer digits: the largest storable mass is 10^15 - 0.001, which is
        // what the store's limit (10^15, exclusive) and the theory below assume.
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var table in new[] { "Facts", "PlanInputs", "OrderInputs", "Plans", "Orders" })
        {
            var entity = model.GetEntityTypes().Single(type => type.GetTableName() == table);
            var mass = entity.GetProperties().Single(property => property.Name is "MassKg" or "PlannedKg" or "TargetKg");
            Assert.Equal((int?)18, mass.GetPrecision());
            Assert.Equal((int?)3, mass.GetScale());
        }
    }

    [Theory]
    [InlineData("1000000000000000", true)]
    [InlineData("999999999999999999", true)]
    [InlineData("999999999999999.999", false)]
    public async Task A_mass_beyond_the_column_is_refused_with_its_own_code_before_the_database(string text, bool refused)
    {
        // The limit is checked before any read: a context on an unreachable server answers the refusal
        // itself; a mass that fits goes on to the database and fails there instead (no server here).
        using var context = Context();
        var store = new ManufacturingStore(context, CompanyContextOf(CompanyA), new GoymenERP.Modules.Audit.Infrastructure.Services.AuditEventCollector(),
            TimeProvider.System);
        var mass = new GoymenERP.Modules.Manufacturing.Domain.Common.Kilograms(
            decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
        var metadata = new GoymenERP.Modules.Manufacturing.Application.ManufacturingCommandMetadata(
            "synthetic-actor", "synthetic.manufacturing.test-write", ["SYNTHETIC"], "synthetic-correlation", null,
            "synthetic-key", "Web");

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            store.IssueInputAsync(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), mass, At, metadata));

        var storeError = error as GoymenERP.Modules.Manufacturing.Application.ManufacturingStoreException;
        Assert.Equal(refused, storeError?.Code == GoymenERP.Modules.Manufacturing.Application.ManufacturingStoreCodes.ValueDoesNotFit);
    }

    private static CompanyContext CompanyContextOf(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return context;
    }

    private static T Attach<T>(ManufacturingDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    [Fact]
    public void A_command_is_one_per_company_command_and_key_hash_and_the_actor_compares_exactly()
    {
        // ADR-011 §2: the key is (company, command name, key hash); the raw key has no column.
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var commands = model.FindEntityType(typeof(CommandRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"],
            commands.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.DoesNotContain(commands.GetProperties(), property => property.Name == "IdempotencyKey");
        Assert.Equal(ManufacturingDbContext.IdentityCollation, commands.FindProperty(nameof(CommandRow.ActorId))!.GetCollation());
        Assert.Contains(commands.GetCheckConstraints(), check => check.Name == "CK_Commands_Hashes");
    }

    private static CommandRow Command() => new()
    {
        CompanyId = CompanyA, CommandName = "manufacturing.input.issue.v1", IdempotencyKeyHash = new string('A', 64),
        ActorId = "synthetic-actor", RequestHash = new string('B', 64), ResultJson = "{\"status\":2}",
        OriginalCorrelationId = "synthetic-correlation", RecordedAtUtc = At,
    };

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
