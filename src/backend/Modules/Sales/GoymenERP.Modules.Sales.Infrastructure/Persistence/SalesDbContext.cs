using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
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
/// <item>R3 identities: the attempt key (company, idempotency key) is a primary key under a binary
/// collation, so the same key of another company is another attempt and a spelling is one spelling;
/// the document number is unique per company; a client line / slice id is unique per draft.</item>
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

    internal DbSet<DraftNumberRow> DraftNumbers => Set<DraftNumberRow>();

    internal DbSet<DraftRow> Drafts => Set<DraftRow>();

    internal DbSet<DraftLineRow> DraftLines => Set<DraftLineRow>();

    internal DbSet<DraftDeliverySliceRow> DraftDeliverySlices => Set<DraftDeliverySliceRow>();

    internal DbSet<DraftAttemptRow> DraftAttempts => Set<DraftAttemptRow>();

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
            table.HasCheckConstraint("CK_Drafts_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_Drafts_Text",
                "LEN([DocumentNo]) > 0 AND LEN([MoneyOperation]) > 0 AND LEN([MoneyProfileVersion]) > 0 AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_Drafts_Money", "[MoneyScale] >= 0 AND ([MoneyEffectiveThrough] IS NULL OR [MoneyEffectiveThrough] >= [MoneyEffectiveFrom])");
            table.HasCheckConstraint("CK_Drafts_References",
                "(([DeliveryAddressId] IS NULL) = ([DeliveryAddressVersion] IS NULL)) AND (([ContactId] IS NULL) = ([ContactVersion] IS NULL)) "
                + "AND (([ResponsibleId] IS NULL) = ([ResponsibleVersion] IS NULL)) AND (([PaymentTermsId] IS NULL) = ([PaymentTermsVersion] IS NULL)) "
                + "AND (([SourceStrategyId] IS NULL) = ([SourceStrategyVersion] IS NULL)) AND (([PackageId] IS NULL) = ([PackageVersion] IS NULL))");
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
            table.HasCheckConstraint("CK_DraftLines_Price", "([UnitPrice] IS NULL) = ([PriceUnitId] IS NULL)");
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
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND LEN([IdempotencyKey]) > 0 "
                + "AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND LEN([CorrelationId]) > 0 AND LEN([RecordedBy]) > 0");
            table.HasCheckConstraint("CK_DraftAttempts_RequestHash", "LEN([RequestHash]) = 64");
        });
        entity.HasKey(x => new { x.CompanyId, x.IdempotencyKey });
        entity.Property(x => x.IdempotencyKey).HasMaxLength(200).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.DraftId }).IsUnique().HasDatabaseName("UX_DraftAttempts_DraftId");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<DraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
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
                    DraftRow => [nameof(DraftRow.UpdatedAtUtc)],
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
