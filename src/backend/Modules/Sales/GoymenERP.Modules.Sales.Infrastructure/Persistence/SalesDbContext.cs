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
    /// decision, the reason optional. The same rule the domain restores by (<c>RestoreApproval</c>).
    /// </summary>
    internal const string ApprovalConstraint =
        "([ApprovalState] = 1 AND [SubmittedBy] IS NULL AND [SubmittedAtUtc] IS NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 2 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NULL AND [DecidedAtUtc] IS NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 3 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND [RejectionReason] IS NULL) "
        + "OR ([ApprovalState] = 4 AND [SubmittedBy] IS NOT NULL AND LEN([SubmittedBy]) > 0 AND [SubmittedAtUtc] IS NOT NULL AND [DecidedBy] IS NOT NULL AND LEN([DecidedBy]) > 0 AND [DecidedAtUtc] IS NOT NULL AND ([RejectionReason] IS NULL OR LEN([RejectionReason]) > 0))";

    /// <summary>
    /// UX_DraftAttempts_DraftId: a stored draft is born from exactly one create attempt. Since SALES-A1 the
    /// state commands (submit / approve / reject) record their own attempts against the same draft, one per
    /// key and as many as the axis allows (a rejected draft is submitted again), so the uniqueness is
    /// filtered to the create command; the attempt key (CompanyId, CommandName, IdempotencyKeyHash) stays the
    /// primary key for every command.
    /// </summary>
    internal const string CreateAttemptFilter = "[DraftId] IS NOT NULL AND [CommandName] = N'" + SalesDraftCommands.CreateV1 + "'";

    internal DbSet<DraftNumberRow> DraftNumbers => Set<DraftNumberRow>();

    internal DbSet<DraftRow> Drafts => Set<DraftRow>();

    internal DbSet<DraftLineRow> DraftLines => Set<DraftLineRow>();

    internal DbSet<DraftDeliverySliceRow> DraftDeliverySlices => Set<DraftDeliverySliceRow>();

    internal DbSet<DraftAttemptRow> DraftAttempts => Set<DraftAttemptRow>();

    internal DbSet<DraftRevisionRow> DraftRevisions => Set<DraftRevisionRow>();

    internal DbSet<DraftRevisionLineRow> DraftRevisionLines => Set<DraftRevisionLineRow>();

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
            table.HasCheckConstraint("CK_Drafts_State", "[Kind] = 1 AND [State] = 1 AND [Revision] = 0");
            // SALES-A1: the approval facts follow the approval state exactly (a row cannot say "approved" without a decision).
            table.HasCheckConstraint("CK_Drafts_Approval", ApprovalConstraint);
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
            // ADR-011 §7: an attempt is either a stored draft or a recorded terminal refusal, never both.
            table.HasCheckConstraint("CK_DraftAttempts_Outcome",
                "([Outcome] = 1 AND [DraftId] IS NOT NULL AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [RefusalJson] IS NULL) "
                + "OR ([Outcome] = 2 AND [DraftId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
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
        });
        revision.HasKey(x => new { x.CompanyId, x.DraftId, x.Revision });
        revision.Property(x => x.SubmittedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.ApprovedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.ArchivedBy).HasMaxLength(128).IsRequired();
        revision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        revision.Property(x => x.MoneyProfileVersion).HasMaxLength(128).IsRequired();
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
        });
        line.HasKey(x => new { x.CompanyId, x.DraftId, x.Revision, x.LineNo });
        line.HasIndex(x => new { x.CompanyId, x.DraftId, x.Revision, x.LineId }).IsUnique().HasDatabaseName("UX_DraftRevisionLines_LineId");
        line.Property(x => x.ProductVersion).HasMaxLength(128).IsRequired();
        line.Property(x => x.QuantityText).HasMaxLength(128).IsRequired();
        line.Property(x => x.UnitPrice).HasMaxLength(128);
        line.Property(x => x.DiscountPercent).HasMaxLength(128);
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
                    // K3 Head B: and its commercial revision number, which the archived history explains.
                    DraftRow =>
                    [
                        nameof(DraftRow.UpdatedAtUtc), nameof(DraftRow.ApprovalState), nameof(DraftRow.SubmittedBy), nameof(DraftRow.SubmittedAtUtc),
                        nameof(DraftRow.DecidedBy), nameof(DraftRow.DecidedAtUtc), nameof(DraftRow.RejectionReason), nameof(DraftRow.Revision),
                    ],
                    // K3 Head B — a DELIBERATE narrowing of "lines never change", declared here and in the PR: ONLY the three
                    // commercial columns may change, under a commercial revision; identity, product, unit and quantity
                    // columns stay immutable (the persistence test pins both halves).
                    DraftLineRow => [nameof(DraftLineRow.UnitPrice), nameof(DraftLineRow.PriceUnitId), nameof(DraftLineRow.DiscountPercent)],
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
                        "Satış kaydının yalnız izinli alanı değişir; satırlar, dilimler ve deneme kayıtları değişmez.");
                }
            }
        }
    }
}
