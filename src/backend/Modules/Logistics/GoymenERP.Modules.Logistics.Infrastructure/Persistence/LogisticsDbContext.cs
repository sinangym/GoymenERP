using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// Logistics's own tables in the <c>logistics</c> schema (LOG-4a; ADR-002, ADR-009, ADR-011; the Sales
/// SALES-P2 shape). Every row is a tenant row of the instruction's company: a global query filter on the
/// server-resolved company and a write guard that refuses any row of another company, any company change,
/// any delete and any column move outside the axis a command may move (the header's status / version /
/// time; a line's four share totals and block flag; a dispatch note's forward-only lifecycle columns and its lines' release flag;
/// the dispatch-note series' last number; shares, weighbridge records and attempts append only). The attempt key
/// (company, command, key hash) is a primary key under a binary collation; the raw key is never stored.
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class LogisticsDbContext(DbContextOptions<LogisticsDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "logistics";
    public const string IdentityCollation = "Latin1_General_100_BIN2";
    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    internal DbSet<LoadingInstructionRow> Instructions => Set<LoadingInstructionRow>();

    internal DbSet<LoadingInstructionLineRow> Lines => Set<LoadingInstructionLineRow>();

    internal DbSet<LoadingShareRow> Shares => Set<LoadingShareRow>();

    internal DbSet<LoadingAttemptRow> Attempts => Set<LoadingAttemptRow>();

    internal DbSet<VehicleWeighingRow> Weighings => Set<VehicleWeighingRow>();

    internal DbSet<DispatchNoteRow> DispatchNotes => Set<DispatchNoteRow>();

    internal DbSet<DispatchNoteLineRow> DispatchNoteLines => Set<DispatchNoteLineRow>();

    internal DbSet<DispatchNoteSeriesRow> DispatchNoteSeries => Set<DispatchNoteSeriesRow>();

    internal DbSet<CustomerReturnRow> CustomerReturns => Set<CustomerReturnRow>();

    internal DbSet<CustomerReturnAttemptRow> CustomerReturnAttempts => Set<CustomerReturnAttemptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureInstructions(modelBuilder);
        ConfigureLines(modelBuilder);
        ConfigureShares(modelBuilder);
        ConfigureAttempts(modelBuilder);
        ConfigureWeighings(modelBuilder);
        ConfigureDispatchNotes(modelBuilder);
        ConfigureCustomerReturns(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigureInstructions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LoadingInstructionRow>();
        entity.ToTable("LoadingInstructions", Schema, table =>
        {
            table.HasCheckConstraint("CK_LoadingInstructions_Identity",
                $"[Id] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND [CustomerPartyId] <> {EmptyGuid} AND [CompanyAccountId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_LoadingInstructions_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_LoadingInstructions_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_LoadingInstructions_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.EffectiveOn).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_LoadingInstructions_CompanyId_CreatedAtUtc_Id");
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_LoadingInstructions_CompanyId_Status_CreatedAtUtc_Id");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LoadingInstructionLineRow>();
        entity.ToTable("LoadingInstructionLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_LoadingInstructionLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_LoadingInstructionLines_Identity",
                $"[LineId] <> {EmptyGuid} AND [SourceOrderId] <> {EmptyGuid} AND [SourceOrderLineId] <> {EmptyGuid} "
                + $"AND [ProductId] <> {EmptyGuid} AND [UnitId] <> {EmptyGuid} AND ([AllocationId] IS NULL OR [AllocationId] <> {EmptyGuid})");
            table.HasCheckConstraint("CK_LoadingInstructionLines_Revision", "[SourceOrderRevision] >= 0");
            // §7.4 / §13: instructed ≥ loaded ≥ shipped ≥ delivered ≥ 0, never counted twice.
            table.HasCheckConstraint("CK_LoadingInstructionLines_Shares",
                "[Instructed] > 0 AND [Loaded] >= 0 AND [Shipped] >= 0 AND [Delivered] >= 0 "
                + "AND [Loaded] <= [Instructed] AND [Shipped] <= [Loaded] AND [Delivered] <= [Shipped]");
        });
        entity.HasKey(x => new { x.CompanyId, x.InstructionId, x.LineNo });
        entity.HasIndex(x => new { x.CompanyId, x.InstructionId, x.LineId }).IsUnique().HasDatabaseName("UX_LoadingInstructionLines_LineId");
        entity.HasIndex(x => new { x.CompanyId, x.SourceOrderId, x.SourceOrderLineId }).HasDatabaseName("IX_LoadingInstructionLines_SourceOrderLine");
        foreach (var quantity in new[] { nameof(LoadingInstructionLineRow.Instructed), nameof(LoadingInstructionLineRow.Loaded),
                     nameof(LoadingInstructionLineRow.Shipped), nameof(LoadingInstructionLineRow.Delivered) })
        {
            entity.Property<decimal>(quantity).HasPrecision(18, 3);
        }

        entity.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureShares(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LoadingShareRow>();
        entity.ToTable("LoadingShares", Schema, table =>
        {
            table.HasCheckConstraint("CK_LoadingShares_ShareNo", "[ShareNo] >= 1");
            table.HasCheckConstraint("CK_LoadingShares_Identity", $"[ShareId] <> {EmptyGuid} AND [LineId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_LoadingShares_Quantity", "[Quantity] > 0");
            // A load names how it was measured; a delivery names its source shipment; a shipment names neither.
            table.HasCheckConstraint("CK_LoadingShares_Kind",
                "([Kind] = 1 AND [Measurement] IN (1, 2) AND [SourceShipmentId] IS NULL) "
                + "OR ([Kind] = 2 AND [Measurement] IS NULL AND [SourceShipmentId] IS NULL) "
                + $"OR ([Kind] = 3 AND [Measurement] IS NULL AND [SourceShipmentId] IS NOT NULL AND [SourceShipmentId] <> {EmptyGuid})");
        });
        entity.HasKey(x => new { x.CompanyId, x.InstructionId, x.ShareNo });
        entity.HasIndex(x => new { x.CompanyId, x.InstructionId, x.ShareId }).IsUnique().HasDatabaseName("UX_LoadingShares_ShareId");
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureAttempts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LoadingAttemptRow>();
        entity.ToTable("LoadingAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_LoadingAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_LoadingAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            // ADR-011 §7: a stored result or a recorded terminal refusal, never both.
            table.HasCheckConstraint("CK_LoadingAttempts_Outcome",
                $"([Outcome] = 1 AND [InstructionId] IS NOT NULL AND [InstructionId] <> {EmptyGuid} AND [RefusalJson] IS NULL) "
                + "OR ([Outcome] = 2 AND [InstructionId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        // One create attempt per stored instruction; the mutation commands' attempts share the instruction id (the Sales lesson).
        entity.HasIndex(x => new { x.CompanyId, x.InstructionId }).IsUnique().HasDatabaseName("UX_LoadingAttempts_InstructionId")
            .HasFilter("[InstructionId] IS NOT NULL AND [CommandName] = N'" + LogisticsCommands.CreateV1 + "'");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// AT-053: one manual weighbridge record per instruction (the key), append-only. SQL Server holds the calculation's
    /// shape too: the direction decides which weighing is the gross, the gross is strictly heavier (so the net is
    /// positive), net = gross − tare (WGT-001) and difference = net − loaded (WGT-002, signed). No tolerance column.
    /// </summary>
    private void ConfigureWeighings(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<VehicleWeighingRow>();
        entity.ToTable("VehicleWeighings", Schema, table =>
        {
            table.HasCheckConstraint("CK_VehicleWeighings_Identity", $"[WeighingId] <> {EmptyGuid} AND LEN([RecordedBy]) > 0");
            table.HasCheckConstraint("CK_VehicleWeighings_Direction", "[Direction] IN (1, 2)");
            table.HasCheckConstraint("CK_VehicleWeighings_Weights",
                "[FirstWeighingKg] > 0 AND [SecondWeighingKg] > 0 AND [NetKg] > 0 AND [LoadedKgAtWeighing] >= 0 AND [SharesBefore] >= 0");
            table.HasCheckConstraint("CK_VehicleWeighings_Net",
                "([Direction] = 1 AND [SecondWeighingKg] > [FirstWeighingKg] AND [NetKg] = [SecondWeighingKg] - [FirstWeighingKg]) "
                + "OR ([Direction] = 2 AND [FirstWeighingKg] > [SecondWeighingKg] AND [NetKg] = [FirstWeighingKg] - [SecondWeighingKg])");
            table.HasCheckConstraint("CK_VehicleWeighings_Difference", "[DifferenceKg] = [NetKg] - [LoadedKgAtWeighing]");
        });
        entity.HasKey(x => new { x.CompanyId, x.InstructionId });
        entity.HasIndex(x => new { x.CompanyId, x.WeighingId }).IsUnique().HasDatabaseName("UX_VehicleWeighings_WeighingId");
        foreach (var weight in new[] { nameof(VehicleWeighingRow.FirstWeighingKg), nameof(VehicleWeighingRow.SecondWeighingKg),
                     nameof(VehicleWeighingRow.NetKg), nameof(VehicleWeighingRow.LoadedKgAtWeighing), nameof(VehicleWeighingRow.DifferenceKg) })
        {
            entity.Property<decimal>(weight).HasPrecision(18, 3);
        }

        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// AT-055: dispatch notes and their lines, company-scoped and bound to their instruction by the company-leading key, and the
    /// company's number series. Never deleted. A note row moves only along its lifecycle (user decision 2026-09-30): approval,
    /// cancellation and the e-İrsaliye number each fill their own columns once (check constraints hold each group's shape and the
    /// status that goes with it); a line row moves only its <c>Released</c> flag, once, when its note is cancelled. The single-link
    /// guard lives in SQL Server: <c>UX_DispatchNoteLines_ShipmentId</c> (company, instruction, shipment) is UNIQUE over the lines
    /// that are not released, so a definite shipment is on at most one non-cancelled note even against a writer that skipped the
    /// aggregate, and a cancelled note's shipments can be put on a new one. The system number (series + sequence) and the
    /// e-İrsaliye number are each unique per company (filtered unique keys over the rows that carry one).
    /// </summary>
    private void ConfigureDispatchNotes(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<DispatchNoteRow>();
        note.ToTable("DispatchNotes", Schema, table =>
        {
            table.HasCheckConstraint("CK_DispatchNotes_Identity", $"[DispatchNoteId] <> {EmptyGuid} AND LEN([CreatedBy]) > 0");
            table.HasCheckConstraint("CK_DispatchNotes_Status", DispatchNoteSchema.StatusCheck);
            // A draft is made from at least one recorded shipment, so at least one share precedes it.
            table.HasCheckConstraint("CK_DispatchNotes_Order", DispatchNoteSchema.OrderCheck);
            table.HasCheckConstraint("CK_DispatchNotes_Approval", DispatchNoteSchema.ApprovalCheck);
            table.HasCheckConstraint("CK_DispatchNotes_Cancellation", DispatchNoteSchema.CancellationCheck);
            table.HasCheckConstraint("CK_DispatchNotes_EDispatchNumber", DispatchNoteSchema.EDispatchNumberCheck);
        });
        note.HasKey(x => new { x.CompanyId, x.InstructionId, x.DispatchNoteId });
        note.HasIndex(x => new { x.CompanyId, x.DispatchNoteId }).IsUnique().HasDatabaseName("UX_DispatchNotes_DispatchNoteId");
        note.HasIndex(x => new { x.CompanyId, x.InstructionId, x.NoteNo }).IsUnique().HasDatabaseName("UX_DispatchNotes_NoteNo");
        note.HasIndex(x => new { x.CompanyId, x.SeriesCode, x.SequenceNo }).IsUnique().HasDatabaseName("UX_DispatchNotes_Number")
            .HasFilter(DispatchNoteSchema.NumberFilter);
        note.HasIndex(x => new { x.CompanyId, x.EDispatchNumber }).IsUnique().HasDatabaseName("UX_DispatchNotes_EDispatchNumber")
            .HasFilter(DispatchNoteSchema.EDispatchNumberFilter);
        note.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        note.Property(x => x.SeriesCode).HasMaxLength(8);
        note.Property(x => x.ApprovedBy).HasMaxLength(128);
        note.Property(x => x.CancelledBy).HasMaxLength(128);
        note.Property(x => x.CancelReason).HasMaxLength(500);
        note.Property(x => x.EDispatchNumber).HasMaxLength(16).IsUnicode(false);
        note.Property(x => x.EDispatchNumberSetBy).HasMaxLength(128);
        note.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        note.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<DispatchNoteLineRow>();
        line.ToTable("DispatchNoteLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_DispatchNoteLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_DispatchNoteLines_Identity",
                $"[ShipmentId] <> {EmptyGuid} AND [InstructionLineId] <> {EmptyGuid} AND [SourceOrderId] <> {EmptyGuid} "
                + $"AND [SourceOrderLineId] <> {EmptyGuid} AND [ProductId] <> {EmptyGuid} AND [UnitId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_DispatchNoteLines_Quantity", "[Quantity] > 0");
        });
        line.HasKey(x => new { x.CompanyId, x.InstructionId, x.DispatchNoteId, x.LineNo });
        // AT-055: "Aynı sevkiyat gerçekleşmesi iki kez irsaliyeye bağlanamaz" — one ACTIVE dispatch-note line per shipment; a
        // cancelled note's lines are released and leave the key.
        line.HasIndex(x => new { x.CompanyId, x.InstructionId, x.ShipmentId }).IsUnique().HasDatabaseName("UX_DispatchNoteLines_ShipmentId")
            .HasFilter(DispatchNoteSchema.ActiveLineFilter);
        line.HasIndex(x => new { x.CompanyId, x.SourceOrderId }).HasDatabaseName("IX_DispatchNoteLines_SourceOrder");
        line.Property(x => x.Quantity).HasPrecision(18, 3);
        line.HasOne<DispatchNoteRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId, x.DispatchNoteId })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var series = modelBuilder.Entity<DispatchNoteSeriesRow>();
        series.ToTable("DispatchNoteSeries", Schema, table =>
            table.HasCheckConstraint("CK_DispatchNoteSeries_Sequence", DispatchNoteSchema.SeriesCheck));
        series.HasKey(x => new { x.CompanyId, x.SeriesCode });
        series.Property(x => x.SeriesCode).HasMaxLength(8);
        // The optimistic token: an approval's UPDATE matches only the number it read, so two approvals never share a number.
        series.Property(x => x.LastSequenceNo).IsConcurrencyToken();
        series.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        series.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureCustomerReturns(ModelBuilder modelBuilder)
    {
        var item = modelBuilder.Entity<CustomerReturnRow>();
        item.ToTable("CustomerReturns", Schema, table =>
        {
            table.HasCheckConstraint("CK_CustomerReturns_Identity",
                $"[CustomerReturnId] <> {EmptyGuid} AND [LoadingInstructionId] <> {EmptyGuid} AND [LoadingInstructionLineId] <> {EmptyGuid} "
                + $"AND [SourceDeliveryId] <> {EmptyGuid} AND [SourceShipmentId] <> {EmptyGuid} AND [SourceOrderId] <> {EmptyGuid} "
                + $"AND [SourceOrderLineId] <> {EmptyGuid} AND [UnitId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_CustomerReturns_Quantity",
                "[QuantityUnit] IN (1, 2) AND [Quantity] > 0 AND [SourceDeliveredQuantity] > 0 AND [Quantity] <= [SourceDeliveredQuantity]");
            table.HasCheckConstraint("CK_CustomerReturns_State", "([Status] = 1 AND [Version] = 1 AND [DecidedBy] IS NULL "
                + "AND [DecidedAtUtc] IS NULL AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL) "
                + "OR ([Status] = 2 AND [Version] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL "
                + "AND [InventoryMovementId] IS NOT NULL AND [OriginalLabelId] IS NOT NULL AND [OriginalShipmentMovementId] IS NOT NULL) "
                + "OR ([Status] = 3 AND [Version] = 2 AND [DecidedBy] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL "
                + "AND [InventoryMovementId] IS NULL AND [OriginalLabelId] IS NULL AND [OriginalShipmentMovementId] IS NULL)");
            table.HasCheckConstraint("CK_CustomerReturns_Actors", "LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0");
        });
        item.HasKey(x => new { x.CompanyId, x.CustomerReturnId });
        item.Property(x => x.Version).IsConcurrencyToken();
        item.Property(x => x.RowVersion).IsRowVersion();
        item.Property(x => x.Quantity).HasPrecision(18, 3);
        item.Property(x => x.SourceDeliveredQuantity).HasPrecision(18, 3);
        item.Property(x => x.Reason).IsRequired();
        item.Property(x => x.RequestedBy).HasMaxLength(128).IsRequired();
        item.Property(x => x.DecidedBy).HasMaxLength(128);
        item.HasIndex(x => new { x.CompanyId, x.SourceDeliveryId, x.Status, x.CustomerReturnId })
            .HasDatabaseName("IX_CustomerReturns_DeliveryStatus");
        item.HasIndex(x => new { x.CompanyId, x.SourceOrderId, x.SourceOrderLineId })
            .HasDatabaseName("IX_CustomerReturns_SourceOrderLine");
        item.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LoadingInstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        item.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<CustomerReturnAttemptRow>();
        attempt.ToTable("CustomerReturnAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_CustomerReturnAttempts_Identity",
                $"[CustomerReturnId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_CustomerReturnAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_CustomerReturnAttempts_Result", "ISJSON([ResultJson]) = 1");
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        attempt.HasOne<CustomerReturnRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CustomerReturnId }).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ApplyWriteGuards()
    {
        LogisticsAuditEventTenantGuard.Validate(this, companyContext);
        foreach (var entry in ChangeTracker.Entries<ILogisticsTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Sevkiyat kayıtları silinmez; değişiklik yeni bir kayıttır.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Sevkiyat kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Sevkiyat kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    LoadingInstructionRow => [nameof(LoadingInstructionRow.Status), nameof(LoadingInstructionRow.UpdatedAtUtc)],
                    LoadingInstructionLineRow =>
                    [
                        nameof(LoadingInstructionLineRow.Loaded), nameof(LoadingInstructionLineRow.Shipped),
                        nameof(LoadingInstructionLineRow.Delivered), nameof(LoadingInstructionLineRow.IsBlocked),
                    ],
                    // AT-055 lifecycle: the status and each set-once group (approval + number, cancellation, e-İrsaliye number).
                    DispatchNoteRow => [nameof(DispatchNoteRow.Status), .. DispatchNoteSchema.SetOnceColumns],
                    DispatchNoteLineRow => [nameof(DispatchNoteLineRow.Released)],
                    DispatchNoteSeriesRow => [nameof(DispatchNoteSeriesRow.LastSequenceNo), nameof(DispatchNoteSeriesRow.UpdatedAtUtc)],
                    CustomerReturnRow =>
                    [
                        nameof(CustomerReturnRow.Status), nameof(CustomerReturnRow.Version), nameof(CustomerReturnRow.DecidedBy),
                        nameof(CustomerReturnRow.DecidedAtUtc), nameof(CustomerReturnRow.DecisionNote),
                        nameof(CustomerReturnRow.InventoryMovementId), nameof(CustomerReturnRow.OriginalLabelId),
                        nameof(CustomerReturnRow.OriginalShipmentMovementId),
                    ],
                    _ => [],
                };
                var changed = entry.Properties
                    .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                    .Select(property => property.Metadata.Name)
                    .Where(name => !allowed.Contains(name))
                    .ToArray();
                if (changed.Length > 0 || allowed.Length == 0)
                {
                    throw new InvalidOperationException("Sevkiyat kaydının yalnız izinli alanı değişir; paylar ve deneme kayıtları eklenir, değişmez.");
                }

                RequireForwardOnly(entry);
            }
        }
    }

    /// <summary>
    /// AT-055: a dispatch note only moves forward — a set-once column is written from empty exactly once (never cleared or
    /// rewritten), the status only climbs (Draft → Approved → Cancelled, or Draft → Cancelled), a line is released once (never
    /// re-linked in place), and the series only climbs by one.
    /// </summary>
    private static void RequireForwardOnly(EntityEntry<ILogisticsTenantRow> entry)
    {
        switch (entry.Entity)
        {
            case DispatchNoteRow:
                foreach (var column in DispatchNoteSchema.SetOnceColumns)
                {
                    var property = entry.Property(column);
                    if (property.IsModified && (property.OriginalValue is not null || property.CurrentValue is null))
                    {
                        throw new InvalidOperationException("İrsaliyenin onay, iptal ve e-İrsaliye alanları bir kez yazılır; değiştirilmez.");
                    }
                }

                var status = entry.Property(nameof(DispatchNoteRow.Status));
                if (status.IsModified && (int)status.CurrentValue! <= (int)status.OriginalValue!)
                {
                    throw new InvalidOperationException("İrsaliye durumu yalnız ileri gider (taslak → onaylı → iptal).");
                }

                break;
            case DispatchNoteLineRow:
                var released = entry.Property(nameof(DispatchNoteLineRow.Released));
                if (released.IsModified && ((bool)released.OriginalValue! || !(bool)released.CurrentValue!))
                {
                    throw new InvalidOperationException("İrsaliye satırı yalnız bir kez serbest bırakılır.");
                }

                break;
            case DispatchNoteSeriesRow:
                var sequence = entry.Property(nameof(DispatchNoteSeriesRow.LastSequenceNo));
                if (sequence.IsModified && (long)sequence.CurrentValue! != (long)sequence.OriginalValue! + 1)
                {
                    throw new InvalidOperationException("İrsaliye serisi yalnız birer birer ilerler.");
                }

                break;
            case CustomerReturnRow:
                var returnStatus = entry.Property(nameof(CustomerReturnRow.Status));
                var returnVersion = entry.Property(nameof(CustomerReturnRow.Version));
                if (!returnStatus.IsModified || (int)returnStatus.OriginalValue! != 1
                    || (int)returnStatus.CurrentValue! is not (2 or 3)
                    || !returnVersion.IsModified || (long)returnVersion.OriginalValue! != 1L
                    || (long)returnVersion.CurrentValue! != 2L)
                    throw new InvalidOperationException("Musteri iadesi yalniz bekliyor durumundan onayli veya reddedildi durumuna bir kez gecer.");
                break;
        }
    }
}
