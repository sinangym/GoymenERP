using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

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

    [Theory]
    [InlineData(nameof(OrderRow.CancelledByUserId))]
    [InlineData(nameof(OrderRow.CancelledAtUtc))]
    [InlineData(nameof(OrderRow.CancellationReason))]
    [InlineData(nameof(OrderRow.PlanVersionAfterCancel))]
    public async Task An_existing_order_cancellation_fact_cannot_be_rewritten_before_a_connection_is_opened(string property)
    {
        await using var context = Context();
        var row = new OrderRow
        {
            CompanyId = CompanyA, Id = Guid.NewGuid(), Status = (int)OrderStatus.Cancelled,
            CancelledByUserId = Guid.NewGuid(), CancelledAtUtc = At,
            CancellationReason = "Recorded cancellation", PlanVersionAfterCancel = 3, Version = 2,
        };
        context.Orders.Attach(row);
        switch (property)
        {
            case nameof(OrderRow.CancelledByUserId): row.CancelledByUserId = Guid.NewGuid(); break;
            case nameof(OrderRow.CancelledAtUtc): row.CancelledAtUtc = At.AddMinutes(1); break;
            case nameof(OrderRow.CancellationReason): row.CancellationReason = "Changed cancellation"; break;
            case nameof(OrderRow.PlanVersionAfterCancel): row.PlanVersionAfterCancel = 4; break;
            default: throw new ArgumentOutOfRangeException(nameof(property));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private static readonly Type[] TenantRows =
        [typeof(PlanRow), typeof(PlanInputRow), typeof(PlanSlitLineRow), typeof(PlanCutLineRow),
            typeof(PlanRevisionRow), typeof(PlanRevisionInputRow), typeof(PlanRevisionCutLineRow),
            typeof(OrderRow), typeof(OrderInputRow), typeof(OrderClosureDecisionRow), typeof(OrderReopeningRow), typeof(CostGenerationRow), typeof(FactRow), typeof(OutputReversalRow),
            typeof(MonthlyScrapPriceRow), typeof(OutputLineageEdgeRow), typeof(OutputLabelCodeRow), typeof(PlanPriorityVersionRow),
            typeof(WorkCenterQueueVersionRow), typeof(WorkCenterQueueEntryRow), typeof(CommandRow)];

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
        foreach (var type in new[] { typeof(PlanRow), typeof(OrderRow), typeof(PlanRevisionRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.GetTableName() == "WorkCenters"
                && key.PrincipalEntityType.GetSchema() == "organization"
                && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "WorkCenterId"]));
            if (type != typeof(PlanRevisionRow))
            {
                Assert.True(entity.FindProperty("Version")!.IsConcurrencyToken);
                Assert.True(entity.FindProperty("RowVersion")!.IsConcurrencyToken);
            }
        }

        foreach (var (type, column, principal) in new[]
        {
            (typeof(PlanInputRow), "PlanId", typeof(PlanRow)),
            (typeof(PlanSlitLineRow), "PlanId", typeof(PlanRow)),
            (typeof(PlanCutLineRow), "PlanId", typeof(PlanRow)),
            (typeof(PlanRevisionRow), "PlanId", typeof(PlanRow)),
            (typeof(OrderRow), "PlanId", typeof(PlanRow)),
            (typeof(OrderInputRow), "OrderId", typeof(OrderRow)),
            (typeof(OrderClosureDecisionRow), "OrderId", typeof(OrderRow)),
            (typeof(FactRow), "OrderId", typeof(OrderRow)),
        })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == principal
                && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", column])
                && key.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"]));
        }

        // A fact id is one fact per company and kind: the key, not a check in code.
        // MFG-PLAN-EDIT: a revision is one per (company, plan, number), and its inputs belong to exactly that revision.
        var revisions = context.Model.FindEntityType(typeof(PlanRevisionRow))!;
        Assert.Equal(["CompanyId", "PlanId", "RevisionNo"],
            revisions.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var revisionInputs = context.Model.FindEntityType(typeof(PlanRevisionInputRow))!;
        Assert.Contains(revisionInputs.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PlanRevisionRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PlanId", "RevisionNo"]));
        Assert.False(revisionInputs.FindProperty(nameof(PlanRevisionInputRow.ExpectedLabelVersion))!.IsNullable);
        var revisionCutLines = context.Model.FindEntityType(typeof(PlanRevisionCutLineRow))!;
        Assert.Contains(revisionCutLines.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PlanRevisionRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PlanId", "RevisionNo"]));

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
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddPlanSlittingSpecs", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_ExpandManufacturingProcesses", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingInputLabelVersions", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingPlanRevisions", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingOrderClosureDecision", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingPlanTechnicalSnapshots", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingCommercialContexts", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingPlanCutLines", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingPlanTechnicalLengthSnapshots", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingPlanPreparer", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingPlanInputAllocations", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingPlanSalesOrderLinks", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingPlanDueDate", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingOutputReversals", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingMonthlyScrapPrices", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingOrderDueDate", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingMonthlyScrapPriceVersions", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingOutputLineageEdges", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingOutputLabelCodes", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddManufacturingOrderCancellation", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddManufacturingPlanPriorities", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddManufacturingManualQueues", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddManufacturingOrderReopenings", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddManufacturingCostGenerations", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddManufacturingNonKgInputFacts", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddManufacturingPaintSelections", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void An_output_reversal_is_an_append_only_fact_bound_to_an_output_and_its_rollback_refuses_while_one_exists()
    {
        // F-009 D2 (URT-D28, URT-D30): a declaration's plan output line is three nullable all-or-none columns (null for a
        // declaration stored before D2); a reversal is its own row keyed by (company, id), bound by (company, kind 3,
        // output id) to the output declaration it reverses and by (company, order) to its order. Up is pure DDL; Down
        // refuses (THROW 51284) while a reversal or a line reference exists (forward fix).
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var facts = model.FindEntityType(typeof(FactRow))!;
        foreach (var name in new[]
                 {
                     nameof(FactRow.OutputLineKind), nameof(FactRow.OutputLineSequence), nameof(FactRow.OutputLineId),
                 })
        {
            Assert.True(facts.FindProperty(name)!.IsNullable, name);
        }

        Assert.Equal(ManufacturingDbContext.FactOutputLineCheck,
            facts.GetCheckConstraints().Single(check => check.Name == "CK_Facts_OutputLine").Sql);

        var reversals = model.FindEntityType(typeof(OutputReversalRow))!;
        Assert.Equal(("manufacturing", "OutputReversals"), (reversals.GetSchema(), reversals.GetTableName()));
        Assert.Equal(["CompanyId", "Id"], reversals.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(3, reversals.FindProperty(nameof(OutputReversalRow.MassKg))!.GetScale());
        Assert.Equal(GoymenERP.Modules.Manufacturing.Domain.Orders.OutputReversal.ReasonMaximumLength,
            reversals.FindProperty(nameof(OutputReversalRow.Reason))!.GetMaxLength());
        var checks = reversals.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(ManufacturingDbContext.OutputReversalIdentityCheck, checks["CK_OutputReversals_Identity"]);
        Assert.Equal(ManufacturingDbContext.OutputReversalMassCheck, checks["CK_OutputReversals_Mass"]);
        Assert.Equal(ManufacturingDbContext.OutputReversalTextCheck, checks["CK_OutputReversals_Text"]);
        Assert.Contains(reversals.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(FactRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OutputKind", "OutputId"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(reversals.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(OrderRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OrderId"]));

        var migration = new AddManufacturingOutputReversals();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51284", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[OutputReversals]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[OutputLineKind] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropTableOperation { Name: "OutputReversals" });
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "Facts", Name: "OutputLineKind" });
    }

    [Fact]
    public void A_monthly_scrap_price_is_one_per_company_and_month_and_its_rollback_refuses_while_one_exists()
    {
        // URT-S13 (URT-D47, URT-D65): one append-only row per (company, year, month), keyed by (company, id); the price is
        // decimal(18,4) as the stamped average cost per kg. Up is pure DDL; Down refuses (THROW 51387) while any price is
        // stored (forward fix).
        using var context = Context();
        var prices = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MonthlyScrapPriceRow))!;
        Assert.Equal(("manufacturing", "MonthlyScrapPrices"), (prices.GetSchema(), prices.GetTableName()));
        Assert.Equal(["CompanyId", "Id"], prices.FindPrimaryKey()!.Properties.Select(property => property.Name));
        // URT-D72: the month is unique per version since the versions migration (see the test below).
        var month = Assert.Single(prices.GetIndexes(), index => index.IsUnique);
        Assert.Equal(["CompanyId", "Year", "Month", "Version"], month.Properties.Select(property => property.Name));
        Assert.Equal(ManufacturingDbContext.MonthlyScrapPriceVersionIndex, month.GetDatabaseName());
        var price = prices.FindProperty(nameof(MonthlyScrapPriceRow.TlPerKg))!;
        Assert.Equal(((int?)18, (int?)GoymenERP.Modules.Manufacturing.Domain.Costing.ScrapPriceTlPerKg.Scale),
            (price.GetPrecision(), price.GetScale()));
        Assert.Equal(GoymenERP.Modules.Manufacturing.Domain.Costing.MonthlyScrapPrice.RecordedByMaximumLength,
            prices.FindProperty(nameof(MonthlyScrapPriceRow.RecordedBy))!.GetMaxLength());
        Assert.Equal(ManufacturingDbContext.IdentityCollation,
            prices.FindProperty(nameof(MonthlyScrapPriceRow.RecordedBy))!.GetCollation());
        var checks = prices.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(ManufacturingDbContext.MonthlyScrapPriceIdentityCheck, checks["CK_MonthlyScrapPrices_Identity"]);
        Assert.Equal(ManufacturingDbContext.MonthlyScrapPricePeriodCheck, checks["CK_MonthlyScrapPrices_Period"]);
        Assert.Equal(ManufacturingDbContext.MonthlyScrapPriceTextCheck, checks["CK_MonthlyScrapPrices_Text"]);
        Assert.Contains(prices.GetForeignKeys(), key => key.PrincipalEntityType.GetTableName() == "Companies"
            && key.PrincipalEntityType.GetSchema() == "organization" && key.DeleteBehavior == DeleteBehavior.Restrict);

        var migration = new AddManufacturingMonthlyScrapPrices();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.UpOperations, operation => operation is CreateIndexOperation
            { Name: "IX_MonthlyScrapPrices_CompanyId_Year_Month", IsUnique: true });
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51387", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[MonthlyScrapPrices]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropTableOperation
            { Name: "MonthlyScrapPrices" });
    }

    [Fact]
    public void A_scrap_price_correction_is_a_new_version_with_a_reason_and_its_rollback_refuses_while_one_exists()
    {
        // URT-D72: the month keeps every version; version 1 is the first entry without a reason, each correction is one
        // more with a reason (a check, not only the domain). Up keeps the existing rows as version 1 and is pure DDL; Down
        // refuses (THROW 51627) while any correction is stored, since dropping the version would lose it (forward fix).
        using var context = Context();
        var prices = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MonthlyScrapPriceRow))!;
        var version = prices.FindProperty(nameof(MonthlyScrapPriceRow.Version))!;
        Assert.False(version.IsNullable);
        var reason = prices.FindProperty(nameof(MonthlyScrapPriceRow.Reason))!;
        Assert.True(reason.IsNullable);
        Assert.Equal(GoymenERP.Modules.Manufacturing.Domain.Costing.MonthlyScrapPrice.ReasonMaximumLength,
            reason.GetMaxLength());
        Assert.Equal(ManufacturingDbContext.MonthlyScrapPriceVersionCheck,
            prices.GetCheckConstraints().Single(check => check.Name == "CK_MonthlyScrapPrices_Version").Sql);

        var migration = new AddManufacturingMonthlyScrapPriceVersions();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        // The existing rows are the first entries: the new column starts at version 1, so they pass the check.
        Assert.Contains(migration.UpOperations, operation => operation is AddColumnOperation
            { Table: "MonthlyScrapPrices", Name: "Version", DefaultValue: 1 });
        Assert.Contains(migration.UpOperations, operation => operation is DropIndexOperation
            { Name: "IX_MonthlyScrapPrices_CompanyId_Year_Month" });
        Assert.Contains(migration.UpOperations, operation => operation is CreateIndexOperation
            { Name: ManufacturingDbContext.MonthlyScrapPriceVersionIndex, IsUnique: true });
        Assert.Contains(migration.UpOperations, operation => operation is AddCheckConstraintOperation
            { Name: "CK_MonthlyScrapPrices_Version" });
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51627", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[MonthlyScrapPrices]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Version] > 1", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "MonthlyScrapPrices", Name: "Version" });
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "MonthlyScrapPrices", Name: "Reason" });
    }

    [Fact]
    public void An_output_lineage_edge_is_one_per_output_and_input_label_and_its_rollback_refuses_while_one_exists()
    {
        // URT-S20 (URT-D62, URT-D63): one append-only row per (company, output declaration, input label), bound by
        // (company, kind 3, output id) to the output declaration; the consumed kg is decimal(18,3) and the share is never
        // stored (the calculator derives it). Up is pure DDL; Down refuses (THROW 51543) while any edge is stored (forward
        // fix).
        using var context = Context();
        var edges = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OutputLineageEdgeRow))!;
        Assert.Equal(("manufacturing", "OutputLineageEdges"), (edges.GetSchema(), edges.GetTableName()));
        Assert.Equal(["CompanyId", "OutputId", "InputLabelId"],
            edges.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var consumed = edges.FindProperty(nameof(OutputLineageEdgeRow.ConsumedKg))!;
        Assert.Equal(((int?)18, (int?)GoymenERP.Modules.Manufacturing.Domain.Common.Kilograms.Scale),
            (consumed.GetPrecision(), consumed.GetScale()));
        Assert.DoesNotContain(edges.GetProperties(), property => property.Name.Contains("Share", StringComparison.Ordinal));
        var checks = edges.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(ManufacturingDbContext.OutputLineageEdgeIdentityCheck, checks["CK_OutputLineageEdges_Identity"]);
        Assert.Equal(ManufacturingDbContext.OutputLineageEdgeMassCheck, checks["CK_OutputLineageEdges_Mass"]);
        Assert.Equal(ManufacturingDbContext.OutputLineageEdgeTextCheck, checks["CK_OutputLineageEdges_Text"]);
        Assert.Contains(edges.GetIndexes(), index => index.GetDatabaseName() == ManufacturingDbContext.OutputLineageEdgeInputIndex
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "InputLabelId"]));
        Assert.Contains(edges.GetIndexes(), index => index.GetDatabaseName() == ManufacturingDbContext.OutputLineageEdgeOutputLabelIndex
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OutputLabelId"]));
        Assert.Contains(edges.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(FactRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OutputKind", "OutputId"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);

        var migration = new AddManufacturingOutputLineageEdges();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.UpOperations, operation => operation is CreateTableOperation { Name: "OutputLineageEdges" });
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51543", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[OutputLineageEdges]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropTableOperation
            { Name: "OutputLineageEdges" });
    }

    [Fact]
    public void An_output_label_code_is_one_per_output_unique_per_company_and_its_rollback_refuses_while_one_exists()
    {
        // URT-S21 (URT-D62): one append-only row per (company, output declaration), bound by (company, kind 3, output id)
        // to the output declaration. The code, the label and a generated number are each unique in the company; an entered
        // code stores no number or format, a generated one both. No column names an input label. Up is pure DDL; Down
        // refuses (THROW 51731) while any code is stored (forward fix).
        using var context = Context();
        var codes = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OutputLabelCodeRow))!;
        Assert.Equal(("manufacturing", "OutputLabelCodes"), (codes.GetSchema(), codes.GetTableName()));
        Assert.Equal(["CompanyId", "OutputId"], codes.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.DoesNotContain(codes.GetProperties(), property => property.Name.Contains("Input", StringComparison.Ordinal));
        Assert.Equal(64, codes.FindProperty(nameof(OutputLabelCodeRow.LabelCode))!.GetMaxLength());
        Assert.False(codes.FindProperty(nameof(OutputLabelCodeRow.LabelCode))!.IsNullable);
        Assert.Equal(32, codes.FindProperty(nameof(OutputLabelCodeRow.Format))!.GetMaxLength());
        Assert.True(codes.FindProperty(nameof(OutputLabelCodeRow.Format))!.IsNullable);
        Assert.True(codes.FindProperty(nameof(OutputLabelCodeRow.GeneratedNumber))!.IsNullable);
        var checks = codes.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(ManufacturingDbContext.OutputLabelCodeIdentityCheck, checks["CK_OutputLabelCodes_Identity"]);
        Assert.Equal(ManufacturingDbContext.OutputLabelCodeSourceCheck, checks["CK_OutputLabelCodes_Source"]);
        Assert.Equal(ManufacturingDbContext.OutputLabelCodeTextCheck, checks["CK_OutputLabelCodes_Text"]);
        Assert.Contains(codes.GetIndexes(), index => index.GetDatabaseName() == ManufacturingDbContext.OutputLabelCodeCodeIndex
            && index.IsUnique && index.GetFilter() is null
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LabelCode"]));
        Assert.Contains(codes.GetIndexes(), index => index.GetDatabaseName() == ManufacturingDbContext.OutputLabelCodeNumberIndex
            && index.IsUnique && index.GetFilter() == "[GeneratedNumber] IS NOT NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "GeneratedNumber"]));
        Assert.Contains(codes.GetIndexes(), index => index.GetDatabaseName() == ManufacturingDbContext.OutputLabelCodeLabelIndex
            && index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LabelId"]));
        Assert.Contains(codes.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(FactRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OutputKind", "OutputId"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);

        var migration = new AddManufacturingOutputLabelCodes();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.UpOperations, operation => operation is CreateTableOperation { Name: "OutputLabelCodes" });
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51731", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[OutputLabelCodes]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropTableOperation
            { Name: "OutputLabelCodes" });
    }

    [Fact]
    public void The_plan_input_allocation_is_keyed_by_plan_and_roll_and_its_rollback_refuses_while_one_exists()
    {
        // URT-D21 / URT-D33: one append-only row per (company, plan, approval generation, parent roll); the allocation
        // identity stays unique per company. The original Up is pure DDL; its Down refuses (THROW 51191) while any row exists, because Inventory still
        // holds the allocation the row names (forward fix).
        using var context = Context();
        var allocation = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PlanInputAllocationRow))!;
        Assert.Equal(["CompanyId", "PlanId", "PlanVersion", "ParentRollId"],
            allocation.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(allocation.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "AllocationId"]));
        Assert.Equal(3, allocation.FindProperty(nameof(PlanInputAllocationRow.AllocatedKg))!.GetScale());

        var migration = new AddManufacturingPlanInputAllocations();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51191", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[PlanInputAllocations]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropTableOperation
            { Name: "PlanInputAllocations" });
    }

    [Fact]
    public void The_plan_sales_order_link_is_an_all_or_none_nullable_snapshot_and_its_rollback_refuses_while_one_was_recorded()
    {
        // Üretim U2-b / API-16: the link columns are all set or all null, the link revision starts at 0 and is at least 1
        // while a link is held; Sales's open-supply read walks the filtered (company, sales order) index. Up is pure DDL;
        // Down refuses (THROW 51261) while any plan holds or ever held a link (forward fix).
        using var context = Context();
        var plan = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PlanRow))!;
        foreach (var name in new[]
                 {
                     nameof(PlanRow.SalesOrderId), nameof(PlanRow.SalesOrderLineId), nameof(PlanRow.SalesCustomerPartyId),
                     nameof(PlanRow.SalesCustomerAccountId), nameof(PlanRow.SalesProductId),
                     nameof(PlanRow.SalesOrderLineRevision), nameof(PlanRow.SalesOrderLinkedBy),
                     nameof(PlanRow.SalesOrderLinkedAtUtc),
                 })
        {
            Assert.True(plan.FindProperty(name)!.IsNullable, name);
        }

        Assert.False(plan.FindProperty(nameof(PlanRow.SalesOrderLinkRevision))!.IsNullable);
        var checks = plan.GetCheckConstraints().Select(check => check.Name).ToArray();
        Assert.Contains("CK_Plans_SalesOrderLink", checks);
        Assert.Equal("[SalesOrderLinkRevision] >= 0 AND ([SalesOrderLineId] IS NULL OR [SalesOrderLinkRevision] >= 1)",
            plan.GetCheckConstraints().Single(check => check.Name == "CK_Plans_SalesOrderLinkRevision").Sql);
        var index = plan.GetIndexes().Single(candidate => candidate.GetDatabaseName() == "IX_Plans_SalesOrder");
        Assert.Equal(["CompanyId", "SalesOrderId"], index.Properties.Select(property => property.Name));
        Assert.Equal("[SalesOrderId] IS NOT NULL", index.GetFilter());

        var migration = new AddManufacturingPlanSalesOrderLinks();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51261", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[SalesOrderLinkRevision] > 0", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "Plans", Name: "SalesOrderLineId" });
    }

    [Fact]
    public void The_plan_termin_is_a_nullable_day_counted_by_its_revision_and_its_rollback_refuses_while_one_was_entered()
    {
        // Üretim F-010a (URT-D29): the hand-entered termin is a nullable `date` (no time); its revision starts at 0 and is
        // at least 1 while a termin is held. Up is pure DDL; Down refuses (THROW 51281) while any plan holds or ever held a
        // termin (forward fix).
        using var context = Context();
        var plan = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PlanRow))!;
        var dueDate = plan.FindProperty(nameof(PlanRow.RequestedDueDate))!;
        Assert.True(dueDate.IsNullable);
        Assert.Equal("date", dueDate.GetColumnType());
        var revision = plan.FindProperty(nameof(PlanRow.DueDateRevision))!;
        Assert.False(revision.IsNullable);
        Assert.Equal(0L, revision.GetDefaultValue());
        Assert.Equal(ManufacturingDbContext.PlanDueDateRevisionCheck,
            plan.GetCheckConstraints().Single(check => check.Name == "CK_Plans_DueDateRevision").Sql);

        var migration = new AddManufacturingPlanDueDate();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51281", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[Plans]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[DueDateRevision] > 0", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "Plans", Name: "RequestedDueDate" });
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "Plans", Name: "DueDateRevision" });
    }

    [Fact]
    public void The_order_termin_is_a_nullable_day_counted_by_its_revision_and_its_rollback_refuses_while_one_is_held()
    {
        // URT-D66 (3): the order's termin is a nullable `date` (no time) copied from the plan; its revision starts at 0 and
        // needs a termin once it moved. Up is pure DDL; Down refuses (THROW 51466) while any order holds or ever changed a
        // termin (forward fix).
        using var context = Context();
        var order = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OrderRow))!;
        var dueDate = order.FindProperty(nameof(OrderRow.DueDate))!;
        Assert.True(dueDate.IsNullable);
        Assert.Equal("date", dueDate.GetColumnType());
        var revision = order.FindProperty(nameof(OrderRow.DueDateRevision))!;
        Assert.False(revision.IsNullable);
        Assert.Equal(0L, revision.GetDefaultValue());
        Assert.Equal(ManufacturingDbContext.OrderDueDateRevisionCheck,
            order.GetCheckConstraints().Single(check => check.Name == "CK_Orders_DueDateRevision").Sql);

        var migration = new AddManufacturingOrderDueDate();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51466", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[Orders]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[DueDateRevision] > 0", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[DueDate] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "Orders", Name: "DueDate" });
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "Orders", Name: "DueDateRevision" });
    }

    [Fact]
    public void The_plan_preparer_is_a_nullable_bounded_actor_id_and_its_rollback_refuses_while_an_unrevised_draft_records_it()
    {
        // Üretim U2-d (URT-D2): Plans.CreatedBy is the saving actor, null for a plan stored before U2-d, never empty.
        using var context = Context();
        var plan = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PlanRow))!;
        var createdBy = plan.FindProperty(nameof(PlanRow.CreatedBy))!;
        Assert.True(createdBy.IsNullable);
        Assert.Equal(ProductionPlan.PreparedByMaximumLength, createdBy.GetMaxLength());
        Assert.Equal("[CreatedBy] IS NULL OR LEN([CreatedBy]) > 0",
            plan.GetCheckConstraints().Single(check => check.Name == "CK_Plans_CreatedBy").Sql);

        // Up is pure DDL; Down refuses (THROW 51190) while an unrevised Draft records its preparer, before it drops the
        // column, because dropping it would let that Draft's creator decide it after a later Up.
        var migration = new AddManufacturingPlanPreparer();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51190", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[p].[Status] = 1", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[PlanRevisions]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "Plans", Name: "CreatedBy" });
    }

    [Fact]
    public void Order_cancellation_rollback_refuses_while_a_cancel_or_a_reapproval_exists_before_it_drops_anything()
    {
        // F-009 D5: Up is pure DDL; Down refuses (THROW 51870) first while a cancelled order, a re-approval or a second
        // allocation generation of a plan exists, and only then drops the table and the cancel columns.
        var migration = new AddManufacturingOrderCancellation();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51870", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 3", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[PlanReapprovals]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(DISTINCT [PlanVersion]) > 1", guard.Sql, StringComparison.Ordinal);
        Assert.Contains(migration.DownOperations, operation => operation is DropTableOperation { Name: "PlanReapprovals" });
        Assert.Contains(migration.DownOperations, operation => operation is DropColumnOperation
            { Table: "Orders", Name: "CancellationReason" });
    }

    [Fact]
    public void Technical_length_rollback_checks_both_immutable_snapshot_tables_before_dropping_either_column()
    {
        using var context = Context();
        var migrations = context.Database.GetMigrations().ToArray();
        var current = Assert.Single(migrations,
            migration => migration.EndsWith("_AddManufacturingPlanTechnicalLengthSnapshots", StringComparison.Ordinal));
        var currentIndex = Array.IndexOf(migrations, current);
        Assert.True(currentIndex > 0);

        var rollback = context.GetService<IMigrator>().GenerateScript(current, migrations[currentIndex - 1]);
        var refusal = rollback.IndexOf("THROW 51047", StringComparison.Ordinal);
        var firstDrop = rollback.IndexOf("DROP COLUMN [TechnicalLengthMm]", StringComparison.Ordinal);

        Assert.Contains("FROM [manufacturing].[PlanInputs]", rollback, StringComparison.Ordinal);
        Assert.Contains("FROM [manufacturing].[PlanRevisionInputs]", rollback, StringComparison.Ordinal);
        Assert.Contains("WHERE [TechnicalLengthMm] IS NOT NULL", rollback, StringComparison.Ordinal);
        Assert.True(refusal >= 0 && firstDrop > refusal,
            "Rollback must refuse before it drops immutable technical-length provenance.");
        Assert.Equal(2, rollback.Split("DROP COLUMN [TechnicalLengthMm]", StringSplitOptions.None).Length - 1);
        Assert.Equal(AddManufacturingPlanTechnicalLengthSnapshots.MigrationId, current);
    }

    [Fact]
    public void Cut_lines_store_client_identity_input_order_explicit_sequence_and_three_decimal_length()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var (type, keyNames, indexNames, foreignKeyNames) in new[]
        {
            (typeof(PlanCutLineRow), new[] { "CompanyId", "PlanId", "LineId" },
                new[] { "CompanyId", "PlanId", "Ordinal" }, new[] { "CompanyId", "PlanId" }),
            (typeof(PlanRevisionCutLineRow), new[] { "CompanyId", "PlanId", "RevisionNo", "LineId" },
                new[] { "CompanyId", "PlanId", "RevisionNo", "Ordinal" },
                new[] { "CompanyId", "PlanId", "RevisionNo" }),
        })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal(keyNames, entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
            Assert.Contains(entity.GetIndexes(), index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(indexNames));
            Assert.Contains(entity.GetForeignKeys(), key => key.Properties.Select(property => property.Name)
                .SequenceEqual(foreignKeyNames) && key.DeleteBehavior == DeleteBehavior.Restrict);
            Assert.Equal((18, 3),
                (entity.FindProperty("LengthMm")!.GetPrecision(), entity.FindProperty("LengthMm")!.GetScale()));
            Assert.False(entity.FindProperty("Sequence")!.IsNullable);
            Assert.Contains(entity.GetCheckConstraints(), check => check.Name!.EndsWith("_Identity", StringComparison.Ordinal)
                && !check.Sql.Contains("[Sequence]", StringComparison.Ordinal));
            Assert.Contains(entity.GetCheckConstraints(), check => check.Name!.EndsWith("_Length", StringComparison.Ordinal)
                && check.Sql == "[LengthMm] > 0");
            Assert.Contains(entity.GetCheckConstraints(), check => check.Name!.EndsWith("_Count", StringComparison.Ordinal)
                && check.Sql == "[Count] >= 1");
        }
    }

    [Fact]
    public void Plan_revision_and_order_inputs_store_one_nullable_all_or_none_commercial_context_without_truncation()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var (type, constraintName) in new[]
        {
            (typeof(PlanInputRow), "CK_PlanInputs_CommercialContext"),
            (typeof(PlanRevisionInputRow), "CK_PlanRevisionInputs_CommercialContext"),
            (typeof(OrderInputRow), "CK_OrderInputs_CommercialContext"),
        })
        {
            var entity = model.FindEntityType(type)!;
            foreach (var property in new[]
            {
                nameof(PlanInputRow.CommercialPartyId),
                nameof(PlanInputRow.CommercialCompanyPartyAccountId),
                nameof(PlanInputRow.CommercialPartyVersion),
                nameof(PlanInputRow.CommercialAccountVersion),
            })
            {
                Assert.True(entity.FindProperty(property)!.IsNullable);
            }

            Assert.Null(entity.FindProperty(nameof(PlanInputRow.CommercialPartyVersion))!.GetMaxLength());
            Assert.Null(entity.FindProperty(nameof(PlanInputRow.CommercialAccountVersion))!.GetMaxLength());
            var sql = entity.GetCheckConstraints().Single(check => check.Name == constraintName).Sql;
            Assert.Contains("[CommercialPartyId] IS NULL", sql, StringComparison.Ordinal);
            Assert.Contains("[CommercialCompanyPartyAccountId] <> '00000000-0000-0000-0000-000000000000'", sql,
                StringComparison.Ordinal);
            Assert.Contains("DATALENGTH([CommercialPartyVersion]) > 0", sql, StringComparison.Ordinal);
            Assert.Contains("DATALENGTH([CommercialAccountVersion]) > 0", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Plan_creation_and_revision_inputs_store_the_same_nullable_all_or_none_technical_snapshot()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var (type, constraintName) in new[]
        {
            (typeof(PlanInputRow), "CK_PlanInputs_TechnicalSnapshot"),
            (typeof(PlanRevisionInputRow), "CK_PlanRevisionInputs_TechnicalSnapshot"),
        })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal(PlanTechnicalParameterSnapshotV1.MaximumProductVersionLength,
                entity.FindProperty(nameof(PlanInputRow.TechnicalProductVersion))!.GetMaxLength());
            Assert.Equal(PlanTechnicalParameterSnapshotV1.MaximumCodeLength,
                entity.FindProperty(nameof(PlanInputRow.TechnicalFamilyCode))!.GetMaxLength());
            Assert.Equal(18, entity.FindProperty(nameof(PlanInputRow.TechnicalThicknessMm))!.GetPrecision());
            Assert.Equal(3, entity.FindProperty(nameof(PlanInputRow.TechnicalThicknessMm))!.GetScale());
            Assert.Equal(18, entity.FindProperty(nameof(PlanInputRow.TechnicalLengthMm))!.GetPrecision());
            Assert.Equal(3, entity.FindProperty(nameof(PlanInputRow.TechnicalLengthMm))!.GetScale());
            Assert.True(entity.FindProperty(nameof(PlanInputRow.TechnicalLengthMm))!.IsNullable);
            Assert.True(entity.FindProperty(nameof(PlanInputRow.TechnicalProductId))!.IsNullable);
            var sql = entity.GetCheckConstraints().Single(check => check.Name == constraintName).Sql;
            Assert.Contains("[TechnicalKilogramUnitCompanyId] = [CompanyId]", sql, StringComparison.Ordinal);
            Assert.Contains("[TechnicalThicknessMm] > 0", sql, StringComparison.Ordinal);
            Assert.Contains("[TechnicalWidthMm] > 0", sql, StringComparison.Ordinal);
            Assert.Contains("[TechnicalLengthMm] IS NULL OR [TechnicalLengthMm] > 0", sql,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_order_closure_decision_is_one_append_only_policy_snapshot_with_an_optional_complete_approval()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var decision = model.FindEntityType(typeof(OrderClosureDecisionRow))!;

        Assert.Equal(["CompanyId", "OrderId", "OrderVersion"],
            decision.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(decision.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(OrderRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OrderId"])
            && key.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);

        var maximumFirePercent = decision.FindProperty(nameof(OrderClosureDecisionRow.MaximumFirePercent))!;
        Assert.Equal(64, maximumFirePercent.GetMaxLength());
        Assert.Equal("varchar(64)", maximumFirePercent.GetColumnType());
        Assert.False(maximumFirePercent.IsNullable);

        var constraintNames = decision.GetCheckConstraints().Select(check => check.Name).ToArray();
        Assert.Contains("CK_OrderClosureDecisions_Identity", constraintNames);
        Assert.Contains("CK_OrderClosureDecisions_Window", constraintNames);
        Assert.Contains("CK_OrderClosureDecisions_Approval", constraintNames);
    }

    [Fact]
    public void Plan_and_order_inputs_keep_a_nullable_positive_inventory_label_version()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;

        foreach (var (type, constraintName) in new[]
        {
            (typeof(PlanInputRow), "CK_PlanInputs_LabelVersion"),
            (typeof(OrderInputRow), "CK_OrderInputs_LabelVersion"),
        })
        {
            var entity = model.FindEntityType(type)!;
            Assert.True(entity.FindProperty(nameof(PlanInputRow.ExpectedLabelVersion))!.IsNullable);
            var constraint = entity.GetCheckConstraints()
                .Single(check => check.Name == constraintName);
            Assert.Equal("[ExpectedLabelVersion] IS NULL OR [ExpectedLabelVersion] >= 1", constraint.Sql);
        }
    }

    [Fact]
    public void Plans_and_orders_persist_exactly_the_five_locked_process_values()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var table in new[] { "Plans", "Orders" })
        {
            var entity = model.GetEntityTypes().Single(type => type.GetTableName() == table);
            var constraint = entity.GetCheckConstraints().Single(check => check.Name == $"CK_{table}_Enums");
            // URT-D28 / URT-D33: cancellation is retained for both plans and orders; the five processes are unchanged.
            Assert.Equal("[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2, 3)", constraint.Sql);
        }

        // URT-D33 supersedes URT-D1: one active order per plan; cancelled generations remain as immutable history.
        var orders = model.GetEntityTypes().Single(type => type.GetTableName() == "Orders");
        var activeOrder = Assert.Single(orders.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PlanId"]));
        Assert.Equal("[Status] <> 3", activeOrder.GetFilter());
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
    [InlineData("edit-plan-winding-target")]
    [InlineData("edit-plan-slit-line")]
    [InlineData("delete-plan-slit-line")]
    [InlineData("edit-plan-cut-line")]
    [InlineData("delete-plan-cut-line")]
    [InlineData("edit-order-plan")]
    [InlineData("edit-order-input")]
    [InlineData("edit-order-company")]
    [InlineData("edit-command-result")]
    [InlineData("delete-command")]
    [InlineData("edit-plan-target")]
    [InlineData("edit-plan-revision-reason")]
    [InlineData("edit-plan-revision-work-center")]
    [InlineData("delete-plan-revision")]
    [InlineData("edit-plan-revision-input")]
    [InlineData("delete-plan-revision-input")]
    [InlineData("edit-plan-revision-cut-line")]
    [InlineData("delete-plan-revision-cut-line")]
    [InlineData("edit-order-closure-decision")]
    [InlineData("delete-order-closure-decision")]
    [InlineData("edit-output-reversal-mass")]
    [InlineData("delete-output-reversal")]
    [InlineData("edit-fact-output-line")]
    [InlineData("edit-scrap-price")]
    [InlineData("delete-scrap-price")]
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
            case "edit-plan-winding-target":
                // MFG-6: the slitting spec is plan content.
                Attach(context, Plan()).WindingTargetKg = 5000m;
                break;
            case "edit-plan-slit-line":
                Attach(context, SlitLine()).BandWidthMm = 250m;
                break;
            case "delete-plan-slit-line":
                context.PlanSlitLines.Remove(Attach(context, SlitLine()));
                break;
            case "edit-plan-cut-line":
                Attach(context, CutLine()).Sequence = -7;
                break;
            case "delete-plan-cut-line":
                context.PlanCutLines.Remove(Attach(context, CutLine()));
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
            // MFG-PLAN-EDIT: a revision never rewrites the plan's creation content, and a recorded revision is final.
            case "edit-plan-target":
                Attach(context, Plan()).TargetKg = 900m;
                break;
            case "edit-plan-revision-reason":
                Attach(context, Revision()).Reason = "sonradan değişmiş gerekçe";
                break;
            case "edit-plan-revision-work-center":
                Attach(context, Revision()).WorkCenterId = Guid.NewGuid();
                break;
            case "delete-plan-revision":
                context.PlanRevisions.Remove(Attach(context, Revision()));
                break;
            case "edit-plan-revision-input":
                Attach(context, RevisionInput()).PlannedKg = 2m;
                break;
            case "delete-plan-revision-input":
                context.PlanRevisionInputs.Remove(Attach(context, RevisionInput()));
                break;
            case "edit-plan-revision-cut-line":
                Attach(context, RevisionCutLine()).Count = 2;
                break;
            case "delete-plan-revision-cut-line":
                context.PlanRevisionCutLines.Remove(Attach(context, RevisionCutLine()));
                break;
            case "edit-order-closure-decision":
                Attach(context, ClosureDecision()).MaximumFirePercent = "9.99";
                break;
            case "delete-order-closure-decision":
                context.OrderClosureDecisions.Remove(Attach(context, ClosureDecision()));
                break;
            // F-009 D2: a reversal is a new fact and is final; a declaration's plan output line never changes.
            case "edit-output-reversal-mass":
                Attach(context, Reversal()).MassKg = 2m;
                break;
            case "delete-output-reversal":
                context.OutputReversals.Remove(Attach(context, Reversal()));
                break;
            case "edit-fact-output-line":
                Attach(context, Fact()).OutputLineKind = 3;
                break;
            // URT-S13, URT-D72: a recorded scrap price version is never changed or deleted; a correction is a new version.
            case "edit-scrap-price":
                Attach(context, ScrapPrice()).TlPerKg = 9.5m;
                break;
            case "delete-scrap-price":
                context.MonthlyScrapPrices.Remove(Attach(context, ScrapPrice()));
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
        foreach (var table in new[] { "Facts", "PlanInputs", "OrderInputs", "Plans", "Orders", "PlanRevisions", "PlanRevisionInputs" })
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

    [Theory]
    [InlineData("create", "thickness", "999999999999999.999", false)]
    [InlineData("create", "thickness", "1000000000000000", true)]
    [InlineData("create", "thickness", "1000000000000000.001", true)]
    [InlineData("create", "width", "999999999999999.999", false)]
    [InlineData("create", "width", "1000000000000000", true)]
    [InlineData("create", "width", "1000000000000000.001", true)]
    [InlineData("revise", "thickness", "999999999999999.999", false)]
    [InlineData("revise", "thickness", "1000000000000000", true)]
    [InlineData("revise", "thickness", "1000000000000000.001", true)]
    [InlineData("revise", "width", "999999999999999.999", false)]
    [InlineData("revise", "width", "1000000000000000", true)]
    [InlineData("revise", "width", "1000000000000000.001", true)]
    public async Task Technical_dimensions_use_the_decimal_18_3_exclusive_bound_before_create_or_revision_mutates(
        string operation, string dimension, string text, bool refused)
    {
        using var context = Context();
        var store = new ManufacturingStore(context, CompanyContextOf(CompanyA),
            new GoymenERP.Modules.Audit.Infrastructure.Services.AuditEventCollector(), TimeProvider.System);
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = TechnicalSnapshot(
            dimension == "thickness" ? value : 1m,
            dimension == "width" ? value : 1m);
        var input = new PlannedInput(Guid.NewGuid(),
            new GoymenERP.Modules.Manufacturing.Domain.Common.Kilograms(1m), 1, snapshot);
        var metadata = new GoymenERP.Modules.Manufacturing.Application.ManufacturingCommandMetadata(
            "synthetic-actor", "synthetic.manufacturing.test-write", ["SYNTHETIC"], "synthetic-correlation", null,
            "synthetic-key", "Web");

        var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            if (operation == "create")
            {
                await store.SavePlanAsync(new ProductionPlan(Guid.NewGuid(), CompanyA, Guid.NewGuid(),
                    ManufacturingProcess.Slitting, [input]), metadata);
            }
            else
            {
                await store.RevisePlanAsync(Guid.NewGuid(), 1, Guid.NewGuid(), ManufacturingProcess.Slitting,
                    [input], null, "sentetik gerekçe", metadata);
            }
        });

        var storeError = error as GoymenERP.Modules.Manufacturing.Application.ManufacturingStoreException;
        Assert.Equal(refused,
            storeError?.Code == GoymenERP.Modules.Manufacturing.Application.ManufacturingStoreCodes.ValueDoesNotFit);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static CompanyContext CompanyContextOf(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return context;
    }

    private static PlanTechnicalParameterSnapshotV1 TechnicalSnapshot(decimal thicknessMm, decimal widthMm) => new(
        Guid.Parse("50000000-0000-0000-0000-000000000001"), "2",
        Guid.Parse("50000000-0000-0000-0000-000000000002"), "COIL", "family-r1",
        Guid.Parse("50000000-0000-0000-0000-000000000003"), "3", "COIL-SHEET", "template-r3",
        CompanyA, Guid.Parse("50000000-0000-0000-0000-000000000004"), "kg",
        Guid.Parse("50000000-0000-0000-0000-000000000005"), "1", new DateOnly(2026, 9, 30),
        thicknessMm, widthMm);

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

    private static PlanRevisionRow Revision() => new()
    {
        CompanyId = CompanyA, PlanId = Guid.NewGuid(), RevisionNo = 1, WorkCenterId = Guid.NewGuid(), Reason = "sentetik gerekçe",
        PlanVersion = 2, RevisedBy = "synthetic-actor", RevisedAtUtc = At, CorrelationId = "synthetic-correlation",
    };

    private static PlanRevisionInputRow RevisionInput() => new()
    {
        CompanyId = CompanyA, PlanId = Guid.NewGuid(), RevisionNo = 1, ParentRollId = Guid.NewGuid(), Sequence = 1,
        PlannedKg = 1m, ExpectedLabelVersion = 1,
    };

    private static PlanSlitLineRow SlitLine() => new()
    {
        CompanyId = CompanyA, PlanId = Guid.NewGuid(), Sequence = 1, BandWidthMm = 150m, BandCount = 4,
    };

    private static PlanCutLineRow CutLine() => new()
    {
        CompanyId = CompanyA, PlanId = Guid.NewGuid(), LineId = Guid.NewGuid(), Ordinal = 1,
        Sequence = 40, LengthMm = 6000m, Count = 1,
    };

    private static PlanRevisionCutLineRow RevisionCutLine() => new()
    {
        CompanyId = CompanyA, PlanId = Guid.NewGuid(), RevisionNo = 1, LineId = Guid.NewGuid(), Ordinal = 1,
        Sequence = -7, LengthMm = 2500.5m, Count = 1,
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

    private static OutputReversalRow Reversal() => new()
    {
        CompanyId = CompanyA, Id = Guid.NewGuid(), OrderId = Guid.NewGuid(), OutputKind = (int)FactKind.OutputDeclaration,
        OutputId = Guid.NewGuid(), ParentRollId = Guid.NewGuid(), MassKg = 1m, Reason = "sentetik gerekçe",
        OccurredAtUtc = At, PayloadHash = new string('0', 64), RecordedAtUtc = At, RecordedBy = "synthetic-actor",
        CorrelationId = "synthetic-correlation",
    };

    private static MonthlyScrapPriceRow ScrapPrice() => new()
    {
        CompanyId = CompanyA, Id = Guid.NewGuid(), Year = 2026, Month = 11, TlPerKg = 7.25m, Version = 1,
        RecordedBy = "synthetic-actor", RecordedAtUtc = At, CorrelationId = "synthetic-correlation",
    };

    private static OrderClosureDecisionRow ClosureDecision() => new()
    {
        CompanyId = CompanyA,
        OrderId = Guid.NewGuid(),
        OrderVersion = 1,
        PolicyId = Guid.NewGuid(),
        PolicyVersion = 1,
        Process = 1,
        MaximumFirePercent = "1.25",
        IsActive = true,
        EffectiveFrom = DateOnly.FromDateTime(At.UtcDateTime),
        SelectedForDate = DateOnly.FromDateTime(At.UtcDateTime),
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
