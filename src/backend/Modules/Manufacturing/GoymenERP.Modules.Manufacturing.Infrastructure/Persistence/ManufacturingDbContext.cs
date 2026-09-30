using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>
/// Manufacturing's own tables in the <c>manufacturing</c> schema (MFG-5; ADR-002, INV-2 pattern).
/// <list type="bullet">
/// <item>Every row is a tenant row of its company: a global query filter on the server-resolved
/// company, and a write guard that refuses any row of another company, any company change and any
/// write without a resolved company.</item>
/// <item>A work center is referenced by (company, work center) against Organization's alternate
/// key, so a plan or order can never point at another company's work center; a company by its id.
/// Parent rolls and outputs are Inventory label identities only: no foreign key to Inventory in
/// this slice (T15 integration).</item>
/// <item>Plans and orders keep their content; only the approval / close and the version change.
/// Inputs and facts are append-only. Nothing is ever deleted. MFG-PLAN-EDIT: a Draft plan's revision is appended
/// (<c>PlanRevisions</c>, <c>PlanRevisionInputs</c>); the plan row keeps its creation content.</item>
/// <item>A fact id is unique per company and kind (primary key), so a retried or duplicated slip
/// cannot be stored twice even by a racing writer.</item>
/// </list>
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class ManufacturingDbContext(DbContextOptions<ManufacturingDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "manufacturing";

    internal DbSet<PlanRow> Plans => Set<PlanRow>();

    internal DbSet<PlanInputRow> PlanInputs => Set<PlanInputRow>();

    internal DbSet<PlanSlitLineRow> PlanSlitLines => Set<PlanSlitLineRow>();

    /// <summary>MFG-PLAN-EDIT: the recorded Draft revisions of plans (append-only).</summary>
    internal DbSet<PlanRevisionRow> PlanRevisions => Set<PlanRevisionRow>();

    /// <summary>MFG-PLAN-EDIT: the complete planned inputs of each recorded revision (append-only).</summary>
    internal DbSet<PlanRevisionInputRow> PlanRevisionInputs => Set<PlanRevisionInputRow>();

    internal DbSet<OrderRow> Orders => Set<OrderRow>();

    internal DbSet<OrderInputRow> OrderInputs => Set<OrderInputRow>();

    internal DbSet<OrderClosureDecisionRow> OrderClosureDecisions => Set<OrderClosureDecisionRow>();

    internal DbSet<FactRow> Facts => Set<FactRow>();

    internal DbSet<CommandRow> Commands => Set<CommandRow>();

    /// <summary>Binary collation: an actor id compares exactly, as the command service compares it.</summary>
    public const string IdentityCollation = "Latin1_General_100_BIN2";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureOrganizationReferences(modelBuilder);
        ConfigurePlans(modelBuilder);
        ConfigurePlanInputs(modelBuilder);
        ConfigurePlanSlitLines(modelBuilder);
        ConfigurePlanRevisions(modelBuilder);
        ConfigureOrders(modelBuilder);
        ConfigureOrderInputs(modelBuilder);
        ConfigureOrderClosureDecisions(modelBuilder);
        ConfigureFacts(modelBuilder);
        ConfigureCommands(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureOrganizationReferences(ModelBuilder modelBuilder)
    {
        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        var workCenter = modelBuilder.Entity<OrganizationWorkCenterReference>();
        workCenter.ToTable("WorkCenters", "organization", table => table.ExcludeFromMigrations());
        workCenter.HasKey(x => new { x.CompanyId, x.Id });
    }

    private void ConfigurePlans(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlanRow>();
        entity.ToTable("Plans", Schema, table =>
        {
            table.HasCheckConstraint("CK_Plans_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Plans_Enums", "[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2)");
            table.HasCheckConstraint("CK_Plans_Target",
                "([TargetBasis] IS NULL AND [TargetKg] IS NULL) OR ([TargetBasis] IN (1, 2) AND [TargetKg] > 0)");
            table.HasCheckConstraint("CK_Plans_Approval",
                "([Status] = 1 AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL) "
                + "OR ([Status] = 2 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_Plans_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_Plans_WindingTarget", "[WindingTargetKg] IS NULL OR [WindingTargetKg] > 0");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.TargetKg).HasPrecision(18, 3);
        entity.Property(x => x.WindingTargetKg).HasPrecision(18, 3);
        entity.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        WorkCenterOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigurePlanInputs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlanInputRow>();
        entity.ToTable("PlanInputs", Schema, table =>
        {
            table.HasCheckConstraint("CK_PlanInputs_Roll", "[ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PlanInputs_Mass", "[PlannedKg] > 0");
            table.HasCheckConstraint("CK_PlanInputs_Sequence", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_PlanInputs_LabelVersion",
                "[ExpectedLabelVersion] IS NULL OR [ExpectedLabelVersion] >= 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.PlanId, x.ParentRollId });
        entity.HasIndex(x => new { x.CompanyId, x.PlanId, x.Sequence }).IsUnique();
        entity.Property(x => x.PlannedKg).HasPrecision(18, 3);
        entity.HasOne<PlanRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PlanId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>MFG-6: a plan's slit lines, bound by (company, plan) like its inputs; append-only.</summary>
    private void ConfigurePlanSlitLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlanSlitLineRow>();
        entity.ToTable("PlanSlitLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_PlanSlitLines_Width", "[BandWidthMm] > 0");
            table.HasCheckConstraint("CK_PlanSlitLines_Count", "[BandCount] >= 1");
            table.HasCheckConstraint("CK_PlanSlitLines_Sequence", "[Sequence] >= 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.PlanId, x.Sequence });
        entity.Property(x => x.BandWidthMm).HasPrecision(18, 3);
        entity.HasOne<PlanRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PlanId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// MFG-PLAN-EDIT: a plan's recorded Draft revisions and their complete inputs, bound by (company, plan) like the
    /// plan's own inputs; the revision's work center by (company, work center) to Organization's alternate key, as the
    /// plan's. Append-only: the write guard admits no column of either table.
    /// </summary>
    private void ConfigurePlanRevisions(ModelBuilder modelBuilder)
    {
        var revision = modelBuilder.Entity<PlanRevisionRow>();
        revision.ToTable("PlanRevisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_PlanRevisions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PlanId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000' AND [RevisionNo] >= 1 AND [PlanVersion] >= 2");
            table.HasCheckConstraint("CK_PlanRevisions_Target",
                "([TargetBasis] IS NULL AND [TargetKg] IS NULL) OR ([TargetBasis] IN (1, 2) AND [TargetKg] > 0)");
            table.HasCheckConstraint("CK_PlanRevisions_Text",
                "LEN([Reason]) > 0 AND LEN([RevisedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        revision.HasKey(x => new { x.CompanyId, x.PlanId, x.RevisionNo });
        revision.Property(x => x.TargetKg).HasPrecision(18, 3);
        revision.Property(x => x.Reason).HasMaxLength(ManufacturingStoreCodes.PlanRevisionReasonMaximumLength).IsRequired();
        revision.Property(x => x.RevisedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        revision.HasOne<PlanRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PlanId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        WorkCenterOf(revision);
        revision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var input = modelBuilder.Entity<PlanRevisionInputRow>();
        input.ToTable("PlanRevisionInputs", Schema, table =>
        {
            table.HasCheckConstraint("CK_PlanRevisionInputs_Roll", "[ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_PlanRevisionInputs_Mass", "[PlannedKg] > 0");
            table.HasCheckConstraint("CK_PlanRevisionInputs_Sequence", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_PlanRevisionInputs_LabelVersion", "[ExpectedLabelVersion] >= 1");
        });
        input.HasKey(x => new { x.CompanyId, x.PlanId, x.RevisionNo, x.ParentRollId });
        input.HasIndex(x => new { x.CompanyId, x.PlanId, x.RevisionNo, x.Sequence }).IsUnique();
        input.Property(x => x.PlannedKg).HasPrecision(18, 3);
        input.HasOne<PlanRevisionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PlanId, x.RevisionNo })
            .HasPrincipalKey(x => new { x.CompanyId, x.PlanId, x.RevisionNo })
            .OnDelete(DeleteBehavior.Restrict);
        input.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOrders(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OrderRow>();
        entity.ToTable("Orders", Schema, table =>
        {
            table.HasCheckConstraint("CK_Orders_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PlanId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [WorkCenterId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Orders_Enums", "[Process] IN (1, 2, 3, 4, 5) AND [Status] IN (1, 2)");
            table.HasCheckConstraint("CK_Orders_Target",
                "([TargetBasis] IS NULL AND [TargetKg] IS NULL) OR ([TargetBasis] IN (1, 2) AND [TargetKg] > 0)");
            table.HasCheckConstraint("CK_Orders_Close",
                "([Status] = 1 AND [ClosedByUserId] IS NULL AND [ClosedAtUtc] IS NULL) "
                + "OR ([Status] = 2 AND [ClosedByUserId] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL)");
            table.HasCheckConstraint("CK_Orders_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.TargetKg).HasPrecision(18, 3);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.PlanId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PlanRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PlanId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        WorkCenterOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOrderInputs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OrderInputRow>();
        entity.ToTable("OrderInputs", Schema, table =>
        {
            table.HasCheckConstraint("CK_OrderInputs_Roll", "[ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OrderInputs_Mass", "[PlannedKg] > 0");
            table.HasCheckConstraint("CK_OrderInputs_Sequence", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_OrderInputs_LabelVersion",
                "[ExpectedLabelVersion] IS NULL OR [ExpectedLabelVersion] >= 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.OrderId, x.ParentRollId });
        entity.HasIndex(x => new { x.CompanyId, x.OrderId, x.Sequence }).IsUnique();
        entity.Property(x => x.PlannedKg).HasPrecision(18, 3);
        OrderOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOrderClosureDecisions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OrderClosureDecisionRow>();
        entity.ToTable("OrderClosureDecisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_OrderClosureDecisions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PolicyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderVersion] >= 1 AND [PolicyVersion] >= 1 AND [Process] IN (1, 2, 3, 4, 5) "
                + "AND LEN([MaximumFirePercent]) > 0 AND [IsActive] = 1");
            table.HasCheckConstraint("CK_OrderClosureDecisions_Window",
                "([EffectiveThrough] IS NULL OR [EffectiveThrough] >= [EffectiveFrom]) "
                + "AND [SelectedForDate] >= [EffectiveFrom] "
                + "AND ([EffectiveThrough] IS NULL OR [SelectedForDate] <= [EffectiveThrough])");
            table.HasCheckConstraint("CK_OrderClosureDecisions_Approval",
                "([ApprovedByUserId] IS NULL AND [ApprovalReason] IS NULL AND [ApprovalOrderVersion] IS NULL "
                + "AND [ApprovalPolicyId] IS NULL AND [ApprovalPolicyVersion] IS NULL AND [ApprovedAtUtc] IS NULL) "
                + "OR ([ApprovedByUserId] IS NOT NULL AND [ApprovedByUserId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([ApprovalReason]) > 0 AND [ApprovalOrderVersion] = [OrderVersion] "
                + "AND [ApprovalPolicyId] = [PolicyId] AND [ApprovalPolicyVersion] = [PolicyVersion] "
                + "AND [ApprovedAtUtc] IS NOT NULL)");
        });
        entity.HasKey(x => new { x.CompanyId, x.OrderId });
        entity.Property(x => x.MaximumFirePercent).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ApprovalReason)
            .HasMaxLength(ClosureExceptionApprovalV1.ReasonMaximumLength);
        OrderOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureFacts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FactRow>();
        entity.ToTable("Facts", Schema, table =>
        {
            table.HasCheckConstraint("CK_Facts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ParentRollId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Facts_Kind", "[Kind] IN (1, 2, 3, 4)");
            // Issue, return and output carry a positive mass; a WIP reading may be zero, never negative.
            table.HasCheckConstraint("CK_Facts_Mass", "([Kind] = 4 AND [MassKg] >= 0) OR ([Kind] IN (1, 2, 3) AND [MassKg] > 0)");
            table.HasCheckConstraint("CK_Facts_Output",
                "([Kind] = 3 AND [OutputClass] IN (1, 2, 3, 4, 5) AND [CostStatus] IN (1, 2, 3)) "
                + "OR ([Kind] <> 3 AND [OutputClass] IS NULL AND [CostStatus] IS NULL)");
            table.HasCheckConstraint("CK_Facts_Text", "LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.Kind, x.Id });
        entity.Property(x => x.MassKg).HasPrecision(18, 3);
        entity.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.OrderId });
        OrderOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// ADR-011 command log: one record per (company, command name, key hash); the key only as its
    /// uppercase SHA-256, the request hash and the actor beside it, the answer as JSON.
    /// </summary>
    private void ConfigureCommands(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CommandRow>();
        entity.ToTable("Commands", Schema, table =>
        {
            table.HasCheckConstraint("CK_Commands_Hashes",
                "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_Commands_Text",
                "LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([OriginalCorrelationId]) > 0");
            table.HasCheckConstraint("CK_Commands_ResultJson", "ISJSON([ResultJson]) = 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.OriginalCorrelationId).HasMaxLength(128).IsRequired();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>A row at one work center, bound by (company, work center) to Organization's alternate key.</summary>
    private static void WorkCenterOf<TRow>(EntityTypeBuilder<TRow> entity)
        where TRow : class, IManufacturingTenantRow
    {
        entity.HasOne<OrganizationWorkCenterReference>().WithMany()
            .HasForeignKey("CompanyId", "WorkCenterId")
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>A row of one order, bound by (company, order) so it can never name another company's order.</summary>
    private static void OrderOf<TRow>(EntityTypeBuilder<TRow> entity)
        where TRow : class, IManufacturingTenantRow
    {
        entity.HasOne<OrderRow>().WithMany()
            .HasForeignKey("CompanyId", "OrderId")
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void ApplyWriteGuards()
    {
        ManufacturingAuditEventTenantGuard.Validate(this, companyContext);

        foreach (var entry in ChangeTracker.Entries<IManufacturingTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Üretim kayıtları silinmez; düzeltme yeni bir olgudur.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Üretim kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Üretim kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    PlanRow => [nameof(PlanRow.Status), nameof(PlanRow.ApprovedByUserId), nameof(PlanRow.ApprovedAtUtc),
                        nameof(PlanRow.Version), nameof(PlanRow.UpdatedAtUtc)],
                    OrderRow => [nameof(OrderRow.Status), nameof(OrderRow.ClosedByUserId), nameof(OrderRow.ClosedAtUtc),
                        nameof(OrderRow.Version), nameof(OrderRow.UpdatedAtUtc)],
                    // MFG-PLAN-EDIT: a revision is APPEND-ONLY — it adds its rows and moves only the plan's version and
                    // update time (admitted above). Nothing of a recorded revision may change afterwards.
                    PlanRevisionRow => [],
                    PlanRevisionInputRow => [],
                    _ => [],
                };
                var changed = entry.Properties
                    .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                    .Select(property => property.Metadata.Name)
                    .Where(name => !allowed.Contains(name))
                    .ToArray();
                if (changed.Length > 0 || allowed.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Üretim kaydının yalnız izinli alanı değişir; girdiler ve olgular değişmez.");
                }
            }
        }
    }
}
