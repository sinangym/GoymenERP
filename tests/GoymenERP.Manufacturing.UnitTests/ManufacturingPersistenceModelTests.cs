using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
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
        [typeof(PlanRow), typeof(PlanInputRow), typeof(PlanSlitLineRow), typeof(PlanCutLineRow),
            typeof(PlanRevisionRow), typeof(PlanRevisionInputRow), typeof(PlanRevisionCutLineRow),
            typeof(OrderRow), typeof(OrderInputRow), typeof(OrderClosureDecisionRow), typeof(FactRow), typeof(CommandRow)];

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
        Assert.False(context.Database.HasPendingModelChanges());
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

        Assert.Equal(["CompanyId", "OrderId"],
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
            Assert.Equal("[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2)", constraint.Sql);
        }
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
