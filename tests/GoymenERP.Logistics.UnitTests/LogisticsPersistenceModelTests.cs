using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// LOG-4a persistent model without a database: schema, tenant filters, the ADR-011 identities, the
/// share-chain check, the write guard (refuses before any connection) and the published shell declarations.
/// What SQL Server enforces is proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsPersistenceModelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void Every_logistics_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(LoadingInstructionRow), typeof(LoadingInstructionLineRow), typeof(LoadingShareRow), typeof(LoadingAttemptRow),
                     typeof(VehicleWeighingRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(LogisticsDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains(entity.GetForeignKeys(), key => key.Properties.Any(property => property.Name == "CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Equal(("organization", "Companies"), (context.Model.FindEntityType(typeof(OrganizationCompanyReference))!.GetSchema(),
            context.Model.FindEntityType(typeof(OrganizationCompanyReference))!.GetTableName()));
    }

    [Fact]
    public void The_identities_are_the_attempt_key_the_line_and_share_ids_and_one_create_attempt_per_instruction()
    {
        using var context = Context();
        // Check constraints, collations and index filters are design-time configuration, not in the read-optimized runtime model.
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var attempts = designModel.FindEntityType(typeof(LoadingAttemptRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        var design = designModel.FindEntityType(typeof(LoadingAttemptRow))!;
        Assert.Equal(LogisticsDbContext.IdentityCollation, design.FindProperty(nameof(LoadingAttemptRow.IdempotencyKeyHash))!.GetCollation());
        Assert.Equal(LogisticsDbContext.IdentityCollation, design.FindProperty(nameof(LoadingAttemptRow.CommandName))!.GetCollation());
        var creates = design.GetIndexes().Single(index => index.GetDatabaseName() == "UX_LoadingAttempts_InstructionId");
        Assert.True(creates.IsUnique);
        Assert.Equal("[InstructionId] IS NOT NULL AND [CommandName] = N'logistics.loading-instruction.create.v1'", creates.GetFilter());
        var lines = designModel.FindEntityType(typeof(LoadingInstructionLineRow))!;
        Assert.True(lines.GetIndexes().Single(index => index.GetDatabaseName() == "UX_LoadingInstructionLines_LineId").IsUnique);
        Assert.Contains(lines.GetCheckConstraints(), check => check.Name == "CK_LoadingInstructionLines_Shares"
            && check.Sql.Contains("[Loaded] <= [Instructed] AND [Shipped] <= [Loaded] AND [Delivered] <= [Shipped]", StringComparison.Ordinal));
        var shares = designModel.FindEntityType(typeof(LoadingShareRow))!;
        Assert.True(shares.GetIndexes().Single(index => index.GetDatabaseName() == "UX_LoadingShares_ShareId").IsUnique);
        Assert.Equal(3, shares.FindProperty(nameof(LoadingShareRow.Quantity))!.GetScale());
        var instructions = designModel.FindEntityType(typeof(LoadingInstructionRow))!;
        Assert.True(instructions.FindProperty(nameof(LoadingInstructionRow.Version))!.IsConcurrencyToken);
        Assert.Equal(LogisticsCommands.CreateV1, "logistics.loading-instruction.create.v1");
    }

    [Fact]
    public void The_weighbridge_record_is_one_per_instruction_with_its_calculation_held_by_checks_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(VehicleWeighingRow))!;
        Assert.Equal(("logistics", "VehicleWeighings"), (design.GetSchema(), design.GetTableName()));
        Assert.Equal(["CompanyId", "InstructionId"], design.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.True(design.GetIndexes().Single(index => index.GetDatabaseName() == "UX_VehicleWeighings_WeighingId").IsUnique);
        Assert.All(new[] { "FirstWeighingKg", "SecondWeighingKg", "NetKg", "LoadedKgAtWeighing", "DifferenceKg" },
            name => Assert.Equal((18, 3), (design.FindProperty(name)!.GetPrecision(), design.FindProperty(name)!.GetScale())));
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_VehicleWeighings_Net"
            && check.Sql.Contains("[Direction] = 1 AND [SecondWeighingKg] > [FirstWeighingKg] AND [NetKg] = [SecondWeighingKg] - [FirstWeighingKg]", StringComparison.Ordinal)
            && check.Sql.Contains("[Direction] = 2 AND [FirstWeighingKg] > [SecondWeighingKg] AND [NetKg] = [FirstWeighingKg] - [SecondWeighingKg]", StringComparison.Ordinal));
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_VehicleWeighings_Difference"
            && check.Sql == "[DifferenceKg] = [NetKg] - [LoadedKgAtWeighing]");
        // No tolerance or verdict column exists (OPEN-004 is not decided).
        Assert.DoesNotContain(design.GetProperties(), property => property.Name.Contains("Tolerance", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Status", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddVehicleWeighings", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_weighing_migration_down_refuses_with_51056_before_it_drops_the_table()
    {
        var down = new GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations.AddVehicleWeighings().DownOperations;
        Assert.Equal(2, down.Count);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [logistics].[VehicleWeighings])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51056,", guard.Sql, StringComparison.Ordinal);
        var drop = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(down[1]);
        Assert.Equal(("logistics", "VehicleWeighings"), (drop.Schema, drop.Name));
    }

    [Fact]
    public async Task A_stored_weighbridge_record_is_never_rewritten_or_deleted()
    {
        await using (var context = Context())
        {
            var row = Weighing();
            context.Weighings.Attach(row);
            row.NetKg = 2m;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            var row = Weighing();
            context.Weighings.Attach(row);
            context.Entry(row).State = EntityState.Deleted;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("silinmez", refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_shipment_is_on_at_most_one_dispatch_note_line_by_a_unique_key_and_the_drafts_are_tenant_rows_of_their_instruction()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var notes = model.FindEntityType(typeof(DispatchNoteRow))!;
        var lines = model.FindEntityType(typeof(DispatchNoteLineRow))!;
        foreach (var entity in new[] { notes, lines })
        {
            Assert.Equal(LogisticsDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.All(entity.GetForeignKeys(), key => Assert.Equal("CompanyId", key.Properties[0].Name));
        }

        Assert.Equal(("DispatchNotes", "DispatchNoteLines"), (notes.GetTableName(), lines.GetTableName()));
        Assert.Equal(["CompanyId", "InstructionId", "DispatchNoteId"], notes.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(["CompanyId", "InstructionId", "DispatchNoteId", "LineNo"], lines.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        // AT-055: the single-link guard, over non-nullable columns, filtered to the lines NOT released — a shipment is on at most one
        // non-cancelled note, and a cancelled note's shipments can be put on a new one (user decision 2026-09-30).
        var single = lines.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DispatchNoteLines_ShipmentId");
        Assert.True(single.IsUnique);
        Assert.Equal("[Released] = 0", single.GetFilter());
        Assert.Equal(["CompanyId", "InstructionId", "ShipmentId"], single.Properties.Select(property => property.Name).ToArray());
        Assert.All(single.Properties, property => Assert.False(property.IsNullable));
        Assert.False(lines.FindProperty(nameof(DispatchNoteLineRow.Released))!.IsNullable);
        Assert.True(notes.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DispatchNotes_DispatchNoteId").IsUnique);
        Assert.Equal((18, 3), (lines.FindProperty(nameof(DispatchNoteLineRow.Quantity))!.GetPrecision(), lines.FindProperty(nameof(DispatchNoteLineRow.Quantity))!.GetScale()));
        Assert.Contains(notes.GetCheckConstraints(), check => check.Name == "CK_DispatchNotes_Status" && check.Sql == "[Status] IN (1, 2, 3)");
        // The system number (company, series, sequence) and the e-İrsaliye number (company, number) are unique when present.
        var number = notes.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DispatchNotes_Number");
        Assert.Equal((true, "[SeriesCode] IS NOT NULL AND [SequenceNo] IS NOT NULL"), (number.IsUnique, number.GetFilter()));
        Assert.Equal(["CompanyId", "SeriesCode", "SequenceNo"], number.Properties.Select(property => property.Name).ToArray());
        var eDispatch = notes.GetIndexes().Single(index => index.GetDatabaseName() == "UX_DispatchNotes_EDispatchNumber");
        Assert.Equal((true, "[EDispatchNumber] IS NOT NULL"), (eDispatch.IsUnique, eDispatch.GetFilter()));
        Assert.Equal(["CompanyId", "EDispatchNumber"], eDispatch.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(["CK_DispatchNotes_Approval", "CK_DispatchNotes_Cancellation", "CK_DispatchNotes_EDispatchNumber", "CK_DispatchNotes_Identity",
                "CK_DispatchNotes_Order", "CK_DispatchNotes_Status"],
            notes.GetCheckConstraints().Select(check => check.Name!).Order(StringComparer.Ordinal).ToArray());
        // The company series: a tenant row whose last number is the approval's optimistic concurrency token.
        var series = model.FindEntityType(typeof(DispatchNoteSeriesRow))!;
        Assert.Equal(("DispatchNoteSeries", LogisticsDbContext.Schema), (series.GetTableName(), series.GetSchema()));
        Assert.Single(series.GetDeclaredQueryFilters());
        Assert.Equal(["CompanyId", "SeriesCode"], series.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.True(series.FindProperty(nameof(DispatchNoteSeriesRow.LastSequenceNo))!.IsConcurrencyToken);
        Assert.All(series.GetForeignKeys(), key => Assert.Equal("CompanyId", key.Properties[0].Name));
        // Price-free: no price or amount column on a note or a line.
        Assert.DoesNotContain(notes.GetProperties().Concat(lines.GetProperties()), property =>
            property.Name.Contains("Price", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDispatchNoteDrafts", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddDispatchNoteLifecycle", StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_dispatch_note_migration_down_refuses_with_51056_before_it_drops_either_table()
    {
        var down = new GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations.AddDispatchNoteDrafts().DownOperations;
        Assert.Equal(3, down.Count);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [logistics].[DispatchNotes]) OR EXISTS (SELECT 1 FROM [logistics].[DispatchNoteLines])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51056,", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(["DispatchNoteLines", "DispatchNotes"],
            down.Skip(1).Select(operation => Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(operation).Name).ToArray());
    }

    [Fact]
    public void The_lifecycle_migration_writes_the_explicit_filters_and_checks_of_the_model_and_its_down_refuses_with_51056_first()
    {
        var migration = new GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations.AddDispatchNoteLifecycle();
        var up = migration.UpOperations;
        // The hand-written filters and checks are the model's own text (DispatchNoteSchema), so the two cannot drift.
        var indexes = up.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>().ToDictionary(index => index.Name, index => index);
        Assert.Equal((true, DispatchNoteSchema.ActiveLineFilter), (indexes["UX_DispatchNoteLines_ShipmentId"].IsUnique, indexes["UX_DispatchNoteLines_ShipmentId"].Filter));
        Assert.Equal((true, DispatchNoteSchema.NumberFilter), (indexes["UX_DispatchNotes_Number"].IsUnique, indexes["UX_DispatchNotes_Number"].Filter));
        Assert.Equal((true, DispatchNoteSchema.EDispatchNumberFilter),
            (indexes["UX_DispatchNotes_EDispatchNumber"].IsUnique, indexes["UX_DispatchNotes_EDispatchNumber"].Filter));
        var checks = up.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>().ToDictionary(check => check.Name, check => check.Sql);
        Assert.Equal(DispatchNoteSchema.StatusCheck, checks["CK_DispatchNotes_Status"]);
        Assert.Equal(DispatchNoteSchema.OrderCheck, checks["CK_DispatchNotes_Order"]);
        Assert.Equal(DispatchNoteSchema.ApprovalCheck, checks["CK_DispatchNotes_Approval"]);
        Assert.Equal(DispatchNoteSchema.CancellationCheck, checks["CK_DispatchNotes_Cancellation"]);
        Assert.Equal(DispatchNoteSchema.EDispatchNumberCheck, checks["CK_DispatchNotes_EDispatchNumber"]);
        var seriesTable = up.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Single();
        Assert.Equal(DispatchNoteSchema.SeriesCheck, Assert.Single(seriesTable.CheckConstraints).Sql);
        // The old unfiltered key is dropped before the column it is filtered on is used; the backfill precedes the new Order check.
        var names = up.Select(operation => operation switch
        {
            Microsoft.EntityFrameworkCore.Migrations.Operations.DropIndexOperation drop => "drop:" + drop.Name,
            Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation => "sql",
            Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation check => "check:" + check.Name,
            _ => operation.GetType().Name,
        }).ToList();
        Assert.True(names.IndexOf("drop:UX_DispatchNoteLines_ShipmentId") < names.IndexOf("sql"));
        Assert.True(names.IndexOf("sql") < names.IndexOf("check:CK_DispatchNotes_Order"));
        Assert.Contains("SET [CreatedStep] = [NoteNo]", Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(up.Single(operation =>
            operation is Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation)).Sql, StringComparison.Ordinal);

        var down = migration.DownOperations;
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(down[0]);
        Assert.Contains("EXISTS (SELECT 1 FROM [logistics].[DispatchNoteSeries])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("EXISTS (SELECT 1 FROM [logistics].[DispatchNotes] WHERE [Status] <> 1)", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("EXISTS (SELECT 1 FROM [logistics].[DispatchNoteLines] WHERE [Released] = 1)", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51056,", guard.Sql, StringComparison.Ordinal);
        Assert.Single(down.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        // Rolled back, the previous unfiltered key returns.
        var restored = Assert.Single(down.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>());
        Assert.Equal(("UX_DispatchNoteLines_ShipmentId", true, null), (restored.Name, restored.IsUnique, restored.Filter));
    }

    [Fact]
    public async Task A_dispatch_note_only_moves_forward_and_the_series_only_by_one()
    {
        // A line is released once, never re-linked in place.
        await using (var context = Context())
        {
            var row = NoteLine();
            row.Released = true;
            context.DispatchNoteLines.Attach(row);
            row.Released = false;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("bir kez serbest", refusal.Message, StringComparison.Ordinal);
        }

        // A set-once column is never rewritten or cleared.
        foreach (var change in new Action<DispatchNoteRow>[] { row => row.CancelReason = "başka", row => row.SequenceNo = null, row => row.EDispatchNumber = "XYZ2026000000009" })
        {
            await using var context = Context();
            var row = Note();
            row.Status = 3;
            row.SeriesCode = "IRS";
            row.SequenceNo = 1;
            row.CancelReason = "gerekçe";
            row.EDispatchNumber = "ABC2026000000001";
            context.DispatchNotes.Attach(row);
            change(row);
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("bir kez yazılır", refusal.Message, StringComparison.Ordinal);
        }

        // The status never goes back (a cancelled note is not revived).
        await using (var context = Context())
        {
            var row = Note();
            row.Status = 3;
            context.DispatchNotes.Attach(row);
            row.Status = 1;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("yalnız ileri", refusal.Message, StringComparison.Ordinal);
        }

        // The series climbs by exactly one.
        await using (var context = Context())
        {
            var row = new DispatchNoteSeriesRow { CompanyId = CompanyA, SeriesCode = "IRS", LastSequenceNo = 4, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            context.DispatchNoteSeries.Attach(row);
            row.LastSequenceNo = 6;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("birer birer", refusal.Message, StringComparison.Ordinal);
        }

        // A note's identity never moves, and nothing is deleted.
        await using (var context = Context())
        {
            var row = Note();
            context.DispatchNotes.Attach(row);
            row.CreatedBy = "başkası";
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            var row = Note();
            context.DispatchNotes.Attach(row);
            context.Entry(row).State = EntityState.Deleted;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("silinmez", refusal.Message, StringComparison.Ordinal);
        }
    }

    private static DispatchNoteRow Note() => new()
    {
        CompanyId = CompanyA, InstructionId = Guid.NewGuid(), DispatchNoteId = Guid.NewGuid(), NoteNo = 1, Status = 1, SharesBefore = 1, CreatedBy = "actor",
        CreatedAtUtc = DateTimeOffset.UtcNow, CreatedStep = 1,
    };

    [Fact]
    public async Task A_stored_dispatch_note_line_is_never_rewritten_or_deleted()
    {
        await using (var context = Context())
        {
            var row = NoteLine();
            context.DispatchNoteLines.Attach(row);
            row.ShipmentId = Guid.NewGuid();
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            var row = NoteLine();
            context.DispatchNoteLines.Attach(row);
            context.Entry(row).State = EntityState.Deleted;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("silinmez", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            var row = NoteLine();
            row.CompanyId = Guid.NewGuid();
            context.DispatchNoteLines.Add(row);
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("aktif şirket", refusal.Message, StringComparison.Ordinal);
        }
    }

    private static DispatchNoteLineRow NoteLine() => new()
    {
        CompanyId = CompanyA, InstructionId = Guid.NewGuid(), DispatchNoteId = Guid.NewGuid(), LineNo = 1, ShipmentId = Guid.NewGuid(),
        InstructionLineId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(), SourceOrderLineId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
        UnitId = Guid.NewGuid(), Quantity = 1m,
    };

    private static VehicleWeighingRow Weighing() => new()
    {
        CompanyId = CompanyA, InstructionId = Guid.NewGuid(), WeighingId = Guid.NewGuid(), Direction = 1, FirstWeighingKg = 1m, SecondWeighingKg = 2m,
        NetKg = 1m, LoadedKgAtWeighing = 1m, DifferenceKg = 0m, SharesBefore = 1, RecordedBy = "SENTETIK", RecordedAtUtc = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task The_write_guard_refuses_another_company_a_delete_and_a_moved_line_identity_before_any_connection()
    {
        await using (var context = Context())
        {
            context.Instructions.Add(Instruction(Guid.NewGuid()));
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("aktif şirket", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context(resolved: false))
        {
            context.Instructions.Add(Instruction(CompanyA));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            var row = Instruction(CompanyA);
            context.Instructions.Attach(row);
            context.Entry(row).State = EntityState.Deleted;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("silinmez", refusal.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            var line = new LoadingInstructionLineRow { CompanyId = CompanyA, InstructionId = Guid.NewGuid(), LineNo = 1, LineId = Guid.NewGuid(),
                SourceOrderId = Guid.NewGuid(), SourceOrderLineId = Guid.NewGuid(), ProductId = Guid.NewGuid(), UnitId = Guid.NewGuid(), Instructed = 1m };
            context.Lines.Attach(line);
            line.Loaded = 1m; // allowed
            line.ProductId = Guid.NewGuid(); // not allowed: identity never moves
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("izinli alan", refusal.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_shell_declarations_are_published_and_bound_to_the_manifest_names()
    {
        var provider = new LogisticsShellCapabilityProvider();
        var declarations = provider.GetDeclarations();
        Assert.Equal("1.2", provider.ContractVersion);
        Assert.Equal(7, declarations.Count);
        Assert.All(declarations, declaration => Assert.True(declaration.ImplementationAvailable));
        Assert.Equal(
            [
                ("logistics.instructions.create", "/lojistik/yukleme-talimatlari/yeni", LogisticsPermissionManifestV1.InstructionsCreate),
                ("logistics.instructions.detail", "/lojistik/yukleme-talimatlari/:instructionId", LogisticsPermissionManifestV1.InstructionsRead),
                ("logistics.instructions.list", "/lojistik/yukleme-talimatlari", LogisticsPermissionManifestV1.InstructionsRead),
                // AT-025: the operations work list's two separate sections, each behind its own read name.
                ("logistics.operations-work.purchase", "/lojistik/operasyon-isleri/satinalma", LogisticsPermissionManifestV1.OperationsPurchaseWorkRead),
                ("logistics.operations-work.sales", "/lojistik/operasyon-isleri/satis", LogisticsPermissionManifestV1.OperationsSalesWorkRead),
            ],
            declarations.Where(declaration => declaration.ParentCapabilityId is null)
                .Select(declaration => (declaration.CapabilityId, declaration.RoutePath, declaration.RequiredPermissions.Single()))
                .OrderBy(item => item.CapabilityId, StringComparer.Ordinal).ToArray());
        Assert.Equal(14, LogisticsPermissionManifestV1.Names.Count);
        Assert.Equal("logistics.shipments.record", LogisticsPermissionManifestV1.ShipmentsRecord);
        Assert.Equal("logistics.deliveries.record", LogisticsPermissionManifestV1.DeliveriesRecord);
        Assert.Equal(14, LogisticsPermissionManifestV1.Names.Distinct(StringComparer.Ordinal).Count());
        // AT-055: the dispatch note's three names (prepare, approve, cancel), each its own and, like the weighbridge record, with no
        // shell declaration (server-measured commands on the detail).
        Assert.Equal("logistics.dispatch-notes.create", LogisticsPermissionManifestV1.DispatchNotesCreate);
        Assert.Equal("logistics.dispatch-notes.approve", LogisticsPermissionManifestV1.DispatchNotesApprove);
        Assert.Equal("logistics.dispatch-notes.cancel", LogisticsPermissionManifestV1.DispatchNotesCancel);
        foreach (var name in new[] { LogisticsPermissionManifestV1.DispatchNotesCreate, LogisticsPermissionManifestV1.DispatchNotesApprove,
                     LogisticsPermissionManifestV1.DispatchNotesCancel })
        {
            Assert.Contains(name, LogisticsPermissionManifestV1.Names);
            Assert.DoesNotContain(declarations, declaration => declaration.RequiredPermissions.Contains(name));
        }
        // AT-053: the weighbridge record has its own name and, like recording a load, no shell declaration (a server-measured command on the detail).
        Assert.Equal("logistics.weighings.record", LogisticsPermissionManifestV1.WeighingsRecord);
        Assert.DoesNotContain(declarations, declaration => declaration.RequiredPermissions.Contains(LogisticsPermissionManifestV1.WeighingsRecord));
        // Master v3 §27.8: L-02 is the instruction list, L-03 the new/edit workspace; the detail is a view of one L-02 row.
        Assert.Equal(
            [
                ("logistics.deliveries.record", "L-02-DETAIL"),
                ("logistics.instructions.create", "L-03"),
                ("logistics.instructions.detail", "L-02-DETAIL"),
                ("logistics.instructions.list", "L-02"),
                ("logistics.operations-work.purchase", "L-09"),
                ("logistics.operations-work.sales", "L-01"),
                ("logistics.shipments.record", "L-02-DETAIL"),
            ],
            declarations.OrderBy(declaration => declaration.CapabilityId, StringComparer.Ordinal)
                .Select(declaration => (declaration.CapabilityId, declaration.ScreenId)).ToArray());
        var shipment = declarations.Single(item => item.CapabilityId == LogisticsShellCapabilityProvider.ShipmentCapabilityId);
        Assert.Equal(LogisticsShellCapabilityProvider.DetailCapabilityId, shipment.ParentCapabilityId);
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.ShipmentsRecord,
            ErpPermissions.InventoryPhysicalAllocationWrite], shipment.RequiredPermissions);
        var delivery = declarations.Single(item => item.CapabilityId == LogisticsShellCapabilityProvider.DeliveryCapabilityId);
        Assert.Equal(LogisticsShellCapabilityProvider.DetailCapabilityId, delivery.ParentCapabilityId);
        Assert.Equal([LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DeliveriesRecord],
            delivery.RequiredPermissions);
    }

    private static LoadingInstructionRow Instruction(Guid company) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, CustomerPartyId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid(), Status = 1, Version = 1,
        EffectiveOn = new DateOnly(2026, 9, 19), CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow, CreatedBy = "SENTETIK", CorrelationId = "SENTETIK",
    };

    private static LogisticsDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        return new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
