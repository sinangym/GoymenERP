using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// Sales's own tables in the <c>sales</c> schema (T03 / SALES-P2, ADR-002, ADR-009).
/// <list type="bullet">
/// <item>Every row is a tenant row of the company that owns the draft: a global query filter on the
/// server-resolved company, and a write guard that refuses any row of another company, any company
/// change and any write without a resolved company.</item>
/// <item>ADR-011 / R3 identities: the attempt key (company, command name, uppercase SHA-256 of the
/// trimmed idempotency key) is a primary key under a binary collation — the raw key is never stored,
/// the same key of another company or another command is another attempt; the document number is
/// unique per company; a client line / slice id is unique per draft.</item>
/// <item>A draft, its lines, its slices and its attempt record are append-only in this slice; only
/// the draft's version may move (approval / revision are T33). The number counter changes its
/// counter only. Nothing is deleted.</item>
/// </list>
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "sales";

    /// <summary>Binary collation: equality is exact, as the domain's ordinal identity comparison.</summary>
    public const string IdentityCollation = "Latin1_General_100_BIN2";

    /// <summary>
    /// SALES-A1 (CK_Drafts_Approval): Preparing (1) carries no fact; PendingApproval (2) the submission
    /// only; Approved (3) the submission and the decision, no reason; Rejected (4) the submission and the
    /// decision, the reason optional. D2 (Sinan 5749046401): Withdrawn (5) the decision — the withdrawer and
    /// the time — with the submission either whole or absent (withdrawn from Preparing), the reason optional.
    /// The same rule the domain restores by (<c>RestoreApproval</c>).
    /// </summary>
    internal const string ApprovalConstraint =
        "([ApprovalState] = 1 AND [SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 2 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND ([RejectionReason] IS NULL OR LEN([RejectionReason]) > 0)) "
        + "OR ([ApprovalState] = 5 AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND (([SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL) OR ([SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL)) AND ([RejectionReason] IS NULL OR LEN([RejectionReason]) > 0))";

    internal const string CustomerAcceptanceConstraint =
        "([CustomerAcceptanceRequired] = 0 AND [CustomerAcceptedRevision] IS NULL AND [CustomerAcceptedBy] IS NULL AND [CustomerAcceptedAtUtc] IS NULL) "
        + "OR ([CustomerAcceptanceRequired] = 1 AND (([CustomerAcceptedRevision] IS NULL AND [CustomerAcceptedBy] IS NULL AND [CustomerAcceptedAtUtc] IS NULL) "
        + "OR ([CustomerAcceptedRevision] IS NOT NULL AND [CustomerAcceptedRevision] >= 0 AND [CustomerAcceptedRevision] <= [Revision] "
        + "AND ([CustomerAcceptedRevision] < [Revision] OR [ApprovalState] = 3) "
        + "AND [CustomerAcceptedBy] IS NOT NULL AND LEN([CustomerAcceptedBy]) > 0 AND [CustomerAcceptedAtUtc] IS NOT NULL)))";

    /// <summary>
    /// UX_DraftAttempts_DraftId: a stored draft is born from exactly one create attempt. Since SALES-A1 the
    /// state commands (submit / approve / reject) record their own attempts against the same draft, one per
    /// key and as many as the axis allows (a rejected draft is submitted again), so the uniqueness is
    /// filtered to the create command; the attempt key (CompanyId, CommandName, IdempotencyKeyHash) stays the
    /// primary key for every command.
    /// </summary>
    internal const string CreateAttemptFilter = "[DraftId] IS NOT NULL AND [CommandName] = N'" + SalesDraftCommands.CreateV1 + "'";

    /// <summary>
    /// A successful attempt always identifies its draft and has no refusal. New create-time terminal
    /// refusals have no draft and new mutation refusals identify the existing draft; null remains valid
    /// for legacy mutation refusals written before that identity was retained, so upgrades preserve replay.
    /// </summary>
    internal const string AttemptOutcomeConstraint =
        "([Outcome] = 1 AND [DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) "
        + "OR ([Outcome] = 2 AND ([DraftId] IS NULL OR [DraftId] <> '00000000-0000-0000-0000-000000000000') "
        + "AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)";
    internal const string AttemptResultConstraint =
        "([CommandName] = N'sales.order-draft.approve-price-floor-exceptions.v1' AND [Outcome] = 1 "
        + "AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson]) = 1) OR "
        + "(([CommandName] <> N'sales.order-draft.approve-price-floor-exceptions.v1' OR [Outcome] <> 1) AND [ResultJson] IS NULL)";

    internal DbSet<DraftNumberRow> DraftNumbers => Set<DraftNumberRow>();

    internal DbSet<DraftRow> Drafts => Set<DraftRow>();

    internal DbSet<DraftLineRow> DraftLines => Set<DraftLineRow>();

    internal DbSet<DraftDeliverySliceRow> DraftDeliverySlices => Set<DraftDeliverySliceRow>();

    internal DbSet<DraftAttemptRow> DraftAttempts => Set<DraftAttemptRow>();

    internal DbSet<DraftRevisionRow> DraftRevisions => Set<DraftRevisionRow>();

    internal DbSet<DraftRevisionLineRow> DraftRevisionLines => Set<DraftRevisionLineRow>();

    /// <summary>DEC-SAL-003: the recorded delivery-schedule changes (append-only).</summary>
    internal DbSet<DraftDeliveryScheduleRow> DraftDeliverySchedules => Set<DraftDeliveryScheduleRow>();

    /// <summary>DEC-SAL-003: the complete dates of each recorded schedule (append-only).</summary>
    internal DbSet<DraftDeliveryScheduleDateRow> DraftDeliveryScheduleDates => Set<DraftDeliveryScheduleDateRow>();

    /// <summary>DEC-SAL-005 / AT-007: the recorded cancellation of an approved order (append-only, at most one per draft).</summary>
    internal DbSet<DraftCancellationRow> DraftCancellations => Set<DraftCancellationRow>();

    /// <summary>Partial delivery (user decision 2026-09-30): the requested remainder changes (append-only).</summary>
    internal DbSet<DraftRemainderChangeRow> DraftRemainderChanges => Set<DraftRemainderChangeRow>();

    /// <summary>Partial delivery: the decision on each remainder change (append-only, at most one per change).</summary>
    internal DbSet<DraftRemainderDecisionRow> DraftRemainderDecisions => Set<DraftRemainderDecisionRow>();

    /// <summary>CK_DraftRemainderChanges_Kind: a cancel carries no quantity; a revise carries a positive quantity and its text.</summary>
    internal const string RemainderKindConstraint =
        "([Kind] = 1 AND [RequestedQuantity] IS NULL AND [RequestedQuantityText] IS NULL) "
        + "OR ([Kind] = 2 AND [RequestedQuantity] > 0 AND [RequestedQuantityText] IS NOT NULL AND LEN([RequestedQuantityText]) > 0)";

    /// <summary>CK_DraftRemainderChanges_Floor: the owner's totals in their own order, a recorded shipment (a partly delivered line).</summary>
    internal const string RemainderFloorConstraint =
        "[DeliveredQuantity] >= 0 AND [DeliveredQuantity] <= [ShippedQuantity] AND [ShippedQuantity] <= [LoadedQuantity] AND [ShippedQuantity] > 0";

    /// <summary>
    /// CK_DraftRemainderDecisions_Outcome: an approval (2) carries the resulting quantity and the owner's totals read at the
    /// approval, no reason; a rejection (3) carries none of those and an optional non-blank reason.
    /// </summary>
    internal const string RemainderOutcomeConstraint =
        "([Outcome] = 2 AND [ResultQuantity] > 0 AND [ResultQuantityText] IS NOT NULL AND LEN([ResultQuantityText]) > 0 AND [Reason] IS NULL "
        + "AND [LoadedQuantity] IS NOT NULL AND [ShippedQuantity] IS NOT NULL AND [DeliveredQuantity] IS NOT NULL "
        + "AND [DeliveredQuantity] >= 0 AND [DeliveredQuantity] <= [ShippedQuantity] AND [ShippedQuantity] <= [LoadedQuantity] AND [ResultQuantity] >= [LoadedQuantity]) "
        + "OR ([Outcome] = 3 AND [ResultQuantity] IS NULL AND [ResultQuantityText] IS NULL AND [LoadedQuantity] IS NULL AND [ShippedQuantity] IS NULL "
        + "AND [DeliveredQuantity] IS NULL AND ([Reason] IS NULL OR LEN([Reason]) > 0))";

    /// <summary>
    /// CK_Drafts_State: the kind is Order; the lifecycle is Draft (1) or, since DEC-SAL-005, Cancelled (2) — and only an
    /// APPROVED order (approval state 3) is ever cancelled; the revision is a counter.
    /// </summary>
    internal const string StateConstraint = "[Kind] = 1 AND [State] IN (1, 2) AND [Revision] >= 0 AND ([State] = 1 OR [ApprovalState] = 3)";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureDraftNumbers(modelBuilder);
        ConfigureDrafts(modelBuilder);
        ConfigureDraftLines(modelBuilder);
        ConfigureDraftDeliverySlices(modelBuilder);
        ConfigureDraftAttempts(modelBuilder);
        ConfigureDraftRevisions(modelBuilder);
        ConfigureDraftDeliverySchedules(modelBuilder);
        ConfigureDraftCancellations(modelBuilder);
        ConfigureDraftRemainderChanges(modelBuilder);
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

    private void ConfigureDraftNumbers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftNumberRow>();
        entity.ToTable("DraftNumbers", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftNumbers_Identity", "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_DraftNumbers_Next", "[Next] >= 1");
            table.HasCheckConstraint("CK_DraftNumbers_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.CompanyId);
        entity.Property(x => x.CompanyId).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftRow>();
        entity.ToTable("Drafts", Schema, table =>
        {
            table.HasCheckConstraint("CK_Drafts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [MoneyProfileId] <> '00000000-0000-0000-0000-000000000000'");
            // K3 Head B: the header's Revision is a counter (0 at creation, +1 per archived approved revision), no longer
            // R3's "= 0" pin; Kind stays pinned. AddDraftRevisions widens the constraint. DEC-SAL-005: the lifecycle State is
            // Draft or Cancelled, and Cancelled only beside an approved order (AddDraftCancellations widens it again).
            table.HasCheckConstraint("CK_Drafts_State", StateConstraint);
            // SALES-A1: the approval facts follow the approval state exactly (a row cannot say "approved" without a decision).
            table.HasCheckConstraint("CK_Drafts_Approval", ApprovalConstraint);
            table.HasCheckConstraint("CK_Drafts_CustomerAcceptance", CustomerAcceptanceConstraint);
            table.HasCheckConstraint("CK_Drafts_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_Drafts_Text",
                "LEN([DocumentNo]) > 0 AND LEN([MoneyOperation]) > 0 AND LEN([MoneyProfileVersion]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_Drafts_Money", "[MoneyScale] >= 0 AND ([MoneyEffectiveThrough] IS NULL OR [MoneyEffectiveThrough] >= [MoneyEffectiveFrom])");
            table.HasCheckConstraint("CK_Drafts_References",
                "(([DeliveryAddressId] IS NULL AND [DeliveryAddressVersion] IS NULL) OR ([DeliveryAddressId] IS NOT NULL AND [DeliveryAddressVersion] IS NOT NULL)) AND (([ContactId] IS NULL AND [ContactVersion] IS NULL) OR ([ContactId] IS NOT NULL AND [ContactVersion] IS NOT NULL)) "
                + "AND (([ResponsibleId] IS NULL AND [ResponsibleVersion] IS NULL) OR ([ResponsibleId] IS NOT NULL AND [ResponsibleVersion] IS NOT NULL)) AND (([PaymentTermsId] IS NULL AND [PaymentTermsVersion] IS NULL) OR ([PaymentTermsId] IS NOT NULL AND [PaymentTermsVersion] IS NOT NULL)) "
                + "AND (([SourceStrategyId] IS NULL AND [SourceStrategyVersion] IS NULL) OR ([SourceStrategyId] IS NOT NULL AND [SourceStrategyVersion] IS NOT NULL)) AND (([PackageId] IS NULL AND [PackageVersion] IS NULL) OR ([PackageId] IS NOT NULL AND [PackageVersion] IS NOT NULL))");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.DocumentNo).HasMaxLength(SalesOrderDraft.MaximumDocumentNoLength).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.BusinessDate).HasColumnType("date");
        entity.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        entity.Property(x => x.Note).HasMaxLength(SalesOrderDraft.MaximumNoteLength);
        foreach (var version in new[]
                 {
                     nameof(DraftRow.DeliveryAddressVersion), nameof(DraftRow.ContactVersion), nameof(DraftRow.ResponsibleVersion),
                     nameof(DraftRow.PaymentTermsVersion), nameof(DraftRow.SourceStrategyVersion), nameof(DraftRow.PackageVersion),
                 })
        {
            entity.Property<string?>(version).HasMaxLength(128);
        }

        entity.Property(x => x.MoneyOperation).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MoneyProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.MoneyEffectiveOn).HasColumnType("date");
        entity.Property(x => x.MoneyEffectiveFrom).HasColumnType("date");
        entity.Property(x => x.MoneyEffectiveThrough).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SubmittedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength);
        entity.Property(x => x.DecidedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength);
        entity.Property(x => x.CustomerAcceptedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength);
        entity.Property(x => x.RejectionReason).HasMaxLength(SalesOrderDraft.MaximumRejectionReasonLength);
        entity.HasIndex(x => new { x.CompanyId, x.ApprovalState, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_Drafts_CompanyId_ApprovalState_CreatedAtUtc_Id");
        entity.HasIndex(x => new { x.CompanyId, x.DocumentNo }).IsUnique().HasDatabaseName("UX_Drafts_DocumentNo");
        entity.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_Drafts_CompanyId_CreatedAtUtc_Id");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDraftLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftLineRow>();
        entity.ToTable("DraftLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_DraftLines_Identity",
                "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitProfileId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_DraftLines_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
            table.HasCheckConstraint("CK_DraftLines_Scale",
                "[OrderUnitProfileScale] >= 0 AND [OrderUnitScale] >= 0 AND [OrderUnitScale] <= [OrderUnitProfileScale] AND [OrderUnitScale] <= 3");
            table.HasCheckConstraint("CK_DraftLines_Price", "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)");
            // AT-018 / AT-021: the pricing detail exists only beside a price.
            table.HasCheckConstraint("CK_DraftLines_Pricing", "[PricingJson] IS NULL OR ([UnitPrice] IS NOT NULL AND ISJSON([PricingJson]) = 1)");
            table.HasCheckConstraint("CK_DraftLines_Window",
                "[OrderUnitEffectiveThrough] IS NULL OR [OrderUnitEffectiveThrough] >= [OrderUnitEffectiveFrom]");
        });
        entity.HasKey(x => new { x.CompanyId, x.DraftId, x.LineNo });
        entity.HasIndex(x => new { x.CompanyId, x.DraftId, x.LineId }).IsUnique().HasDatabaseName("UX_DraftLines_LineId");
        entity.Property(x => x.ProductVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ProductCode).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ProductName).HasMaxLength(256).IsRequired();
        entity.Property(x => x.ProductEffectiveOn).HasColumnType("date");
        entity.Property(x => x.StockUnitProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.SalesUnitProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.OrderUnitBindingProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.OrderUnitCode).HasMaxLength(64).IsRequired();
        entity.Property(x => x.OrderUnitDimensionCode).HasMaxLength(64).IsRequired();
        entity.Property(x => x.OrderUnitProfileVersion).HasMaxLength(128).IsRequired();
        entity.Property(x => x.OrderUnitEffectiveFrom).HasColumnType("date");
        entity.Property(x => x.OrderUnitEffectiveThrough).HasColumnType("date");
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        entity.Property(x => x.Note).HasMaxLength(SalesOrderDraft.MaximumNoteLength);
        entity.Property(x => x.UnitPrice).HasMaxLength(128);
        entity.Property(x => x.DiscountPercent).HasMaxLength(128);
        entity.Property(x => x.PricingJson).HasColumnType("nvarchar(max)");
        entity.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDraftDeliverySlices(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftDeliverySliceRow>();
        entity.ToTable("DraftDeliverySlices", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftDeliverySlices_SliceNo", "[SliceNo] >= 1");
            table.HasCheckConstraint("CK_DraftDeliverySlices_Identity",
                "[SliceId] <> '00000000-0000-0000-0000-000000000000' AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_DraftDeliverySlices_Quantity", "[Quantity] > 0 AND LEN([QuantityText]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.DraftId, x.LineNo, x.SliceNo });
        entity.HasIndex(x => new { x.CompanyId, x.DraftId, x.LineNo, x.SliceId }).IsUnique().HasDatabaseName("UX_DraftDeliverySlices_SliceId");
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        entity.HasOne<DraftLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.LineNo })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureDraftAttempts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftAttemptRow>();
        entity.ToTable("DraftAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftAttempts_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CommandName]) > 0 "
                + "AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_DraftAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            // ADR-011 §7: new mutation refusals bind to their draft; legacy null refusals remain replayable.
            table.HasCheckConstraint("CK_DraftAttempts_Outcome", AttemptOutcomeConstraint);
            table.HasCheckConstraint("CK_DraftAttempts_Result", AttemptResultConstraint);
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        entity.HasIndex(x => new { x.CompanyId, x.DraftId }).IsUnique().HasDatabaseName("UX_DraftAttempts_DraftId")
            .HasFilter(CreateAttemptFilter);
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // DEC-SAL-003: the recorded delivery-schedule changes and their dates — append-only history beside the draft.
    private void ConfigureDraftDeliverySchedules(ModelBuilder modelBuilder)
    {
        var schedule = modelBuilder.Entity<DraftDeliveryScheduleRow>();
        schedule.ToTable("DraftDeliverySchedules", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftDeliverySchedules_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ScheduleNo] >= 1 AND [DraftVersion] >= 2 AND LEN([ChangedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        schedule.HasKey(x => new { x.CompanyId, x.DraftId, x.ScheduleNo });
        schedule.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        schedule.Property(x => x.ChangedBy).HasMaxLength(128).IsRequired();
        schedule.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        schedule.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        schedule.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var date = modelBuilder.Entity<DraftDeliveryScheduleDateRow>();
        date.ToTable("DraftDeliveryScheduleDates", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftDeliveryScheduleDates_Position", "[LineNo] >= 1 AND [SliceNo] >= 0");
            table.HasCheckConstraint("CK_DraftDeliveryScheduleDates_Slice",
                "([SliceNo] = 0 AND [SliceId] IS NULL) "
                + "OR ([SliceNo] >= 1 AND [SliceId] IS NOT NULL AND [SliceId] <> '00000000-0000-0000-0000-000000000000' AND [RequestedDeliveryDate] IS NOT NULL)");
        });
        date.HasKey(x => new { x.CompanyId, x.DraftId, x.ScheduleNo, x.LineNo, x.SliceNo });
        date.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        date.HasOne<DraftDeliveryScheduleRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.ScheduleNo })
            .HasPrincipalKey(x => new { x.CompanyId, x.DraftId, x.ScheduleNo })
            .OnDelete(DeleteBehavior.Restrict);
        date.HasOne<DraftLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.LineNo })
            .OnDelete(DeleteBehavior.Restrict);
        date.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // DEC-SAL-005 / AT-007: the recorded cancellation — append-only history beside the draft, at most one per draft.
    private void ConfigureDraftCancellations(ModelBuilder modelBuilder)
    {
        var cancellation = modelBuilder.Entity<DraftCancellationRow>();
        cancellation.ToTable("DraftCancellations", Schema, table =>
        {
            // The smallest version a cancellation can produce: create (1), one line (2), submit (3), approve (4), cancel (5).
            table.HasCheckConstraint("CK_DraftCancellations_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [Revision] >= 0 AND [DraftVersion] >= 5 AND LEN([CancelledBy]) > 0 AND LEN([Reason]) > 0 "
                + "AND LEN([ImpactToken]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DraftCancellations_Impact", "ISJSON([ImpactJson]) = 1");
        });
        cancellation.HasKey(x => new { x.CompanyId, x.DraftId });
        cancellation.Property(x => x.CancelledBy).HasMaxLength(SalesOrderDraft.MaximumActorLength).IsRequired();
        cancellation.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumCancellationReasonLength).IsRequired();
        cancellation.Property(x => x.ImpactToken).HasMaxLength(128).IsRequired();
        cancellation.Property(x => x.ImpactJson).HasColumnType("nvarchar(max)").IsRequired();
        cancellation.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        cancellation.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        cancellation.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // Partial delivery (user decision 2026-09-30): the remainder changes and their decisions — append-only history beside the
    // draft. The line's approved quantity lives on the line row (the quantity revision's writable columns).
    private void ConfigureDraftRemainderChanges(ModelBuilder modelBuilder)
    {
        var change = modelBuilder.Entity<DraftRemainderChangeRow>();
        change.ToTable("DraftRemainderChanges", Schema, table =>
        {
            // The smallest version a request can produce: create (1), one line (2), submit (3), approve (4), request (5).
            table.HasCheckConstraint("CK_DraftRemainderChanges_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ChangeNo] >= 1 AND [Revision] >= 0 AND [LineNo] >= 1 "
                + "AND [DraftVersion] >= 5 AND LEN([OrderedQuantityText]) > 0 AND LEN([Reason]) > 0 AND LEN([RequestedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DraftRemainderChanges_Kind", RemainderKindConstraint);
            table.HasCheckConstraint("CK_DraftRemainderChanges_Floor", RemainderFloorConstraint);
        });
        change.HasKey(x => new { x.CompanyId, x.DraftId, x.ChangeNo });
        change.HasIndex(x => new { x.CompanyId, x.DraftId, x.DraftVersion }).IsUnique().HasDatabaseName("UX_DraftRemainderChanges_DraftVersion");
        change.Property(x => x.OrderedQuantityText).HasMaxLength(128).IsRequired();
        change.Property(x => x.RequestedQuantity).HasPrecision(18, 3);
        change.Property(x => x.RequestedQuantityText).HasMaxLength(128);
        change.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumRemainderReasonLength).IsRequired();
        change.Property(x => x.RequestedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength).IsRequired();
        change.Property(x => x.LoadedQuantity).HasPrecision(18, 3);
        change.Property(x => x.ShippedQuantity).HasPrecision(18, 3);
        change.Property(x => x.DeliveredQuantity).HasPrecision(18, 3);
        change.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        change.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        change.HasOne<DraftLineRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.LineNo })
            .OnDelete(DeleteBehavior.Restrict);
        change.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var decision = modelBuilder.Entity<DraftRemainderDecisionRow>();
        decision.ToTable("DraftRemainderDecisions", Schema, table =>
        {
            // The smallest version a decision can produce: the request's (5) plus the decision (6).
            table.HasCheckConstraint("CK_DraftRemainderDecisions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ChangeNo] >= 1 AND [DraftVersion] >= 6 AND LEN([DecidedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DraftRemainderDecisions_Outcome", RemainderOutcomeConstraint);
        });
        decision.HasKey(x => new { x.CompanyId, x.DraftId, x.ChangeNo });
        decision.HasIndex(x => new { x.CompanyId, x.DraftId, x.DraftVersion }).IsUnique().HasDatabaseName("UX_DraftRemainderDecisions_DraftVersion");
        decision.Property(x => x.DecidedBy).HasMaxLength(SalesOrderDraft.MaximumActorLength).IsRequired();
        decision.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumRemainderReasonLength);
        decision.Property(x => x.ResultQuantity).HasPrecision(18, 3);
        decision.Property(x => x.ResultQuantityText).HasMaxLength(128);
        decision.Property(x => x.LoadedQuantity).HasPrecision(18, 3);
        decision.Property(x => x.ShippedQuantity).HasPrecision(18, 3);
        decision.Property(x => x.DeliveredQuantity).HasPrecision(18, 3);
        decision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        decision.HasOne<DraftRemainderChangeRow>().WithOne()
            .HasForeignKey<DraftRemainderDecisionRow>(x => new { x.CompanyId, x.DraftId, x.ChangeNo })
            .HasPrincipalKey<DraftRemainderChangeRow>(x => new { x.CompanyId, x.DraftId, x.ChangeNo })
            .OnDelete(DeleteBehavior.Restrict);
        decision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    // K3 Head B: the archived approved revisions and their lines — append-only history beside the draft.
    private void ConfigureDraftRevisions(ModelBuilder modelBuilder)
    {
        var revision = modelBuilder.Entity<DraftRevisionRow>();
        revision.ToTable("DraftRevisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftRevisions_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [Revision] >= 0 AND LEN([SubmittedBy]) > 0 AND LEN([ApprovedBy]) > 0 AND LEN([ArchivedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_DraftRevisions_Money",
                "[MoneyProfileId] <> '00000000-0000-0000-0000-000000000000' AND LEN([MoneyProfileVersion]) > 0 AND [MoneyScale] >= 0");
            // DEC-SAL-002: a quantity revision's reason is non-blank; a price revision archives with none (AddRevisionReasons).
            table.HasCheckConstraint("CK_DraftRevisions_Reason", "[Reason] IS NULL OR LEN([Reason]) > 0");
        });
        revision.HasKey(x => new { x.CompanyId, x.DraftId, x.Revision });
        revision.Property(x => x.SubmittedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.ApprovedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.ArchivedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        revision.Property(x => x.MoneyProfileVersion).HasMaxLength(128).IsRequired();
        revision.Property(x => x.Reason).HasMaxLength(SalesOrderDraft.MaximumRevisionReasonLength);
        revision.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        revision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<DraftRevisionLineRow>();
        line.ToTable("DraftRevisionLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftRevisionLines_Identity",
                "[LineNo] >= 1 AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OrderUnitId] <> '00000000-0000-0000-0000-000000000000' AND LEN([QuantityText]) > 0");
            table.HasCheckConstraint("CK_DraftRevisionLines_Price",
                "([UnitPrice] IS NULL AND [PriceUnitId] IS NULL) OR ([UnitPrice] IS NOT NULL AND [PriceUnitId] IS NOT NULL)");
            table.HasCheckConstraint("CK_DraftRevisionLines_Pricing",
                "[PricingJson] IS NULL OR ([UnitPrice] IS NOT NULL AND ISJSON([PricingJson]) = 1)");
        });
        line.HasKey(x => new { x.CompanyId, x.DraftId, x.Revision, x.LineNo });
        line.HasIndex(x => new { x.CompanyId, x.DraftId, x.Revision, x.LineId }).IsUnique().HasDatabaseName("UX_DraftRevisionLines_LineId");
        line.Property(x => x.ProductVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        line.Property(x => x.UnitPrice).HasMaxLength(128);
        line.Property(x => x.DiscountPercent).HasMaxLength(128);
        line.Property(x => x.PricingJson).HasColumnType("nvarchar(max)");
        line.HasOne<DraftRevisionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId, x.Revision })
            .HasPrincipalKey(x => new { x.CompanyId, x.DraftId, x.Revision })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ApplyWriteGuards()
    {
        SalesAuditEventTenantGuard.Validate(this, companyContext);

        foreach (var entry in ChangeTracker.Entries<ISalesTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Satış taslağı kayıtları silinmez; değişiklik yeni bir revizyondur (T33).");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Satış kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Satış kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    DraftNumberRow => [nameof(DraftNumberRow.Next), nameof(DraftNumberRow.UpdatedAtUtc)],
                    // SALES-A1: the header moves only along the approval axis (state, facts, time; the version is the token);
                    // K3 Head B: and its commercial revision number, which the archived history explains; DEC-SAL-005: and its
                    // lifecycle State, Draft → Cancelled only (checked below), which the appended cancellation row explains.
                    DraftRow =>
                    [
                        nameof(DraftRow.UpdatedAtUtc), nameof(DraftRow.ApprovalState), nameof(DraftRow.SubmittedBy), nameof(DraftRow.SubmittedAtUtc),
                        nameof(DraftRow.DecidedBy), nameof(DraftRow.DecidedAtUtc), nameof(DraftRow.RejectionReason), nameof(DraftRow.Revision),
                        nameof(DraftRow.State), nameof(DraftRow.CustomerAcceptedRevision), nameof(DraftRow.CustomerAcceptedBy),
                        nameof(DraftRow.CustomerAcceptedAtUtc),
                    ],
                    // K3 Head B — a DELIBERATE narrowing of "lines never change", declared here and in the PR: ONLY the three
                    // commercial columns may change, under a commercial revision; DEC-SAL-002 widens it by exactly the ordered
                    // quantity and its verbatim text, under a quantity revision (the approved values stay in the archived
                    // revision's lines). Identity, product, unit and slice columns stay immutable (the persistence test pins both halves).
                    DraftLineRow =>
                    [
                        nameof(DraftLineRow.UnitPrice), nameof(DraftLineRow.PriceUnitId), nameof(DraftLineRow.DiscountPercent),
                        // AT-018 / AT-021: the pricing detail moves with the three commercial columns, under a commercial revision.
                        nameof(DraftLineRow.PricingJson),
                        nameof(DraftLineRow.Quantity), nameof(DraftLineRow.QuantityText),
                    ],
                    // DEC-SAL-003: the delivery schedule is APPEND-ONLY — a change adds a schedule and its dates and moves
                    // only the header's version (the token) and UpdatedAtUtc, both already admitted above. Nothing of an
                    // added schedule may change afterwards: no column is admitted.
                    DraftDeliveryScheduleRow => [],
                    DraftDeliveryScheduleDateRow => [],
                    // DEC-SAL-005: a recorded cancellation is APPEND-ONLY — no column of it may change afterwards.
                    DraftCancellationRow => [],
                    // Partial delivery: a remainder request and its decision are APPEND-ONLY — no column may change afterwards.
                    DraftRemainderChangeRow => [],
                    DraftRemainderDecisionRow => [],
                    _ => [],
                };
                if (entry.Entity is DraftRow)
                {
                    var state = entry.Property(nameof(DraftRow.State));
                    if (state.IsModified && !((int)state.OriginalValue! == (int)SalesDraftState.Draft && (int)state.CurrentValue! == (int)SalesDraftState.Cancelled))
                    {
                        throw new InvalidOperationException("Satış siparişinin yaşam döngüsü yalnız Taslak → İptal yönünde değişir.");
                    }

                    var acceptedRevision = entry.Property(nameof(DraftRow.CustomerAcceptedRevision));
                    var acceptedBy = entry.Property(nameof(DraftRow.CustomerAcceptedBy));
                    var acceptedAt = entry.Property(nameof(DraftRow.CustomerAcceptedAtUtc));
                    if (acceptedRevision.IsModified || acceptedBy.IsModified || acceptedAt.IsModified)
                    {
                        var required = entry.Property(nameof(DraftRow.CustomerAcceptanceRequired));
                        var approval = entry.Property(nameof(DraftRow.ApprovalState));
                        var revision = entry.Property(nameof(DraftRow.Revision));
                        var currentAcceptedRevision = (int?)acceptedRevision.CurrentValue;
                        var originalAcceptedRevision = (int?)acceptedRevision.OriginalValue;
                        if (required.IsModified || required.CurrentValue is not true || approval.IsModified
                            || approval.CurrentValue is not (int)InternalApprovalState.Approved || revision.IsModified
                            || currentAcceptedRevision is null || currentAcceptedRevision != (int)revision.CurrentValue!
                            || originalAcceptedRevision is { } previous && previous >= currentAcceptedRevision
                            || acceptedBy.CurrentValue is not string actor || string.IsNullOrWhiteSpace(actor)
                            || acceptedAt.CurrentValue is not DateTimeOffset)
                        {
                            throw new InvalidOperationException("Müşteri kabulü yalnız onaylı güncel revizyona ileri bir geçiş olarak kaydedilir.");
                        }
                    }
                }

                var changed = entry.Properties
                    .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                    .Select(property => property.Metadata.Name)
                    .Where(name => !allowed.Contains(name))
                    .ToArray();
                if (changed.Length > 0 || allowed.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Satış kaydının yalnız izinli alanı değişir; satırlar, dilimler ve deneme kayıtları değişmez.");
                }
            }
        }
    }
}
