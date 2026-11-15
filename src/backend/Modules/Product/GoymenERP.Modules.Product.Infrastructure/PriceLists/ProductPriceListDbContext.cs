using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// AT-018 price lists: Product's COMPANY-scoped tables in the <c>product</c> schema, beside (not inside) the global
/// Product card context. Every row is a tenant row of the list's company: a global query filter on the server-resolved
/// company and a write guard that refuses any row of another company, any company change, and any column move outside
/// the axis a command may move — the header's name/version/time; a Draft version's validity and its publication; a
/// Draft version's lines replaced as a whole (the only delete this context allows). Published versions and their lines
/// never change. The attempt key (company, command, key hash) is a primary key under a binary collation; the raw key is
/// never stored. The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// The Product card and Organization's company table are mapped read-only for foreign keys and checks. One exception to the
/// company scope (SAT-D149, EK20): the item type of the SHARED product card (<c>ProductCardItemTypes</c>) is keyed by the product
/// alone, written only from a resolved company (recorded as the acting company) under its own write guard. The "Ebat/kalınlık"
/// row of the shared card (BS-9, <c>ProductCardDimensions</c>) follows the same rule. The structural profile weight table (KLN-D54)
/// is company-scoped and insert-only: an import writes the next version, nothing is edited or deleted.
/// Migrations have their own history table so that this context never shares one with <c>ProductDbContext</c>.
/// </summary>
public sealed class ProductPriceListDbContext(DbContextOptions<ProductPriceListDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "product";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_PriceLists";
    public const string IdentityCollation = "Latin1_General_100_BIN2";
    public const string NameCollation = "Turkish_100_CI_AS";
    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    /// <summary>
    /// BS-9 / KLN-D48: each section kind holds exactly the fields its family needs — built from the one rule table of
    /// <see cref="ProductDimensionShape"/> (ProductDimensionSectionKindsV1).
    /// </summary>
    internal static readonly string DimensionShapeCheck = ProductDimensionShape.SqlCheck();

    /// <summary>Every stored measure is positive; a missing one is NULL, never zero.</summary>
    internal const string DimensionMeasureCheck =
        "([ThicknessMm] IS NULL OR [ThicknessMm] > 0) AND ([WidthMm] IS NULL OR [WidthMm] > 0) "
        + "AND ([LengthMm] IS NULL OR [LengthMm] > 0) AND ([HeightMm] IS NULL OR [HeightMm] > 0) "
        + "AND ([OuterDiameterMm] IS NULL OR [OuterDiameterMm] > 0) AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) "
        + "AND ([KgPerMetre] IS NULL OR [KgPerMetre] > 0) AND ([MeshSpacingMm] IS NULL OR [MeshSpacingMm] > 0)";

    /// <summary>
    /// KLN-D48: a known structural series, a size designation of ASCII capitals / digits / dots (binary collation) and a surface
    /// text that is not blank.
    /// </summary>
    internal const string DimensionTextCheck =
        "([ProfileSeries] IS NULL OR [ProfileSeries] IN ('UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM')) "
        + "AND ([ProfileSize] IS NULL OR (LEN([ProfileSize]) > 0 AND [ProfileSize] NOT LIKE '%[^A-Z0-9.]%')) "
        + "AND ([SurfaceFinish] IS NULL OR LEN([SurfaceFinish]) > 0)";

    /// <summary>KLN-D54: a weight table row names a known series, a size designation like the card's and a positive kg/m.</summary>
    internal const string ProfileWeightRowCheck =
        "[ProfileSeries] IN ('UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM') "
        + "AND LEN([ProfileSize]) > 0 AND [ProfileSize] NOT LIKE '%[^A-Z0-9.]%' AND [KgPerMetre] > 0";

    internal DbSet<PriceListRow> PriceLists => Set<PriceListRow>();

    internal DbSet<PriceListVersionRow> Versions => Set<PriceListVersionRow>();

    internal DbSet<PriceListLineRow> Lines => Set<PriceListLineRow>();

    internal DbSet<PriceListAttemptRow> Attempts => Set<PriceListAttemptRow>();

    internal DbSet<PriceListProductReference> Products => Set<PriceListProductReference>();

    internal DbSet<ProductCardItemTypeRow> ItemTypes => Set<ProductCardItemTypeRow>();

    internal DbSet<ProductItemTypeAttemptRow> ItemTypeAttempts => Set<ProductItemTypeAttemptRow>();

    internal DbSet<ProductCardDimensionRow> Dimensions => Set<ProductCardDimensionRow>();

    internal DbSet<ProductDimensionAttemptRow> DimensionAttempts => Set<ProductDimensionAttemptRow>();

    internal DbSet<ProductProfileWeightTableRow> ProfileWeightTables => Set<ProductProfileWeightTableRow>();

    internal DbSet<ProductProfileWeightRow> ProfileWeightRows => Set<ProductProfileWeightRow>();

    internal DbSet<ProductProfileWeightAttemptRow> ProfileWeightAttempts => Set<ProductProfileWeightAttemptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<PriceListCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        var product = modelBuilder.Entity<PriceListProductReference>();
        product.ToTable("Products", Schema, table => table.ExcludeFromMigrations());
        product.HasKey(x => x.Id);
        product.Property(x => x.Code).HasMaxLength(64).IsUnicode(false);
        product.Property(x => x.Name).HasMaxLength(250);
        product.Property(x => x.ReferencesJson).HasColumnType("nvarchar(max)");

        ConfigureLists(modelBuilder);
        ConfigureVersions(modelBuilder);
        ConfigureLines(modelBuilder);
        ConfigureAttempts(modelBuilder);
        ConfigureItemTypes(modelBuilder);
        ConfigureDimensions(modelBuilder);
        ConfigureProfileWeights(modelBuilder);
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

    private void ConfigureLists(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PriceListRow>();
        entity.ToTable("PriceLists", Schema, table =>
        {
            table.HasCheckConstraint("CK_PriceLists_Identity", $"[Id] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PriceLists_Name", "LEN([Name]) > 0");
            table.HasCheckConstraint("CK_PriceLists_Currency", "LEN([CurrencyCode]) = 3");
            // Fail-closed context (AT-018): the Sales channel and prices excluding VAT only, until decided otherwise.
            table.HasCheckConstraint("CK_PriceLists_Channel", "[Channel] = 1");
            table.HasCheckConstraint("CK_PriceLists_VatBasis", "[VatBasis] = 1");
            // MOD-02 (SAT-D9): a company-wide list names nobody; a customer-specific list names the party AND its company
            // account. A customer-group list waits for the Cari card's group field (SAT-D57) and is refused here as well; agreement
            // prices are the Sales contract's, never a Product list (SAT-D58).
            table.HasCheckConstraint("CK_PriceLists_Scope",
                "([ScopeKind] = 1 AND [ScopePartyId] IS NULL AND [ScopeCompanyAccountId] IS NULL) "
                + $"OR ([ScopeKind] = 3 AND [ScopePartyId] IS NOT NULL AND [ScopePartyId] <> {EmptyGuid} "
                + $"AND [ScopeCompanyAccountId] IS NOT NULL AND [ScopeCompanyAccountId] <> {EmptyGuid})");
            table.HasCheckConstraint("CK_PriceLists_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_PriceLists_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Name).HasMaxLength(200).UseCollation(NameCollation).IsRequired();
        entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
        // Every list stored before MOD-02 is company-wide: the column arrives with that value and is always written explicitly.
        entity.Property(x => x.ScopeKind).HasDefaultValue(1).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        // One name per company (Turkish case-insensitive): a list is chosen by its name on the order line. The columns are
        // NOT NULL, so the filter is stated explicitly as none rather than left to the provider.
        entity.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique().HasFilter(null).HasDatabaseName("UX_PriceLists_CompanyId_Name");
        entity.HasIndex(x => new { x.CompanyId, x.UpdatedAtUtc, x.Id }).HasDatabaseName("IX_PriceLists_CompanyId_UpdatedAtUtc_Id");
        entity.HasIndex(x => new { x.CompanyId, x.ScopeKind, x.ScopeCompanyAccountId }).HasDatabaseName("IX_PriceLists_CompanyId_Scope");
        entity.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureVersions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PriceListVersionRow>();
        entity.ToTable("PriceListVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_PriceListVersions_Identity", $"[VersionId] <> {EmptyGuid} AND [VersionNo] >= 1");
            table.HasCheckConstraint("CK_PriceListVersions_Validity", "[ValidThrough] IS NULL OR [ValidThrough] >= [ValidFrom]");
            // A Draft carries no publication facts; a Published version carries both.
            table.HasCheckConstraint("CK_PriceListVersions_State",
                "([State] = 1 AND [PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL) "
                + "OR ([State] = 2 AND [PublishedAtUtc] IS NOT NULL AND [PublishedBy] IS NOT NULL AND LEN([PublishedBy]) > 0)");
            table.HasCheckConstraint("CK_PriceListVersions_Text", "LEN([CreatedBy]) > 0 AND ISJSON([PreparerActorIdsJson]) = 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.PriceListId, x.VersionNo });
        entity.HasIndex(x => new { x.CompanyId, x.VersionId }).IsUnique().HasFilter(null).HasDatabaseName("UX_PriceListVersions_VersionId");
        // At most one Draft per list, held by the database as well as by the aggregate.
        entity.HasIndex(x => new { x.CompanyId, x.PriceListId }).IsUnique().HasFilter("[State] = 1")
            .HasDatabaseName("UX_PriceListVersions_OneDraft");
        // Published starts are unique per list (the aggregate requires them strictly increasing).
        entity.HasIndex(x => new { x.CompanyId, x.PriceListId, x.ValidFrom }).IsUnique().HasFilter("[State] = 2")
            .HasDatabaseName("UX_PriceListVersions_PublishedValidFrom");
        entity.Property(x => x.ValidFrom).HasColumnType("date");
        entity.Property(x => x.ValidThrough).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.PreparerActorIdsJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.PublishedBy).HasMaxLength(128);
        entity.HasOne<PriceListRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PriceListId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PriceListLineRow>();
        entity.ToTable("PriceListLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_PriceListLines_Identity",
                $"[LineId] <> {EmptyGuid} AND [ProductId] <> {EmptyGuid} AND [PriceUnitId] <> {EmptyGuid} AND [LineNo] >= 1");
            table.HasCheckConstraint("CK_PriceListLines_Price",
                "[UnitPrice] > 0 AND ([FloorUnitPrice] IS NULL OR ([FloorUnitPrice] > 0 AND [FloorUnitPrice] <= [UnitPrice]))");
            table.HasCheckConstraint("CK_PriceListLines_QuantityTier",
                "[LowerQuantity] >= 0 AND ([UpperQuantity] IS NULL OR [UpperQuantity] > [LowerQuantity])");
        });
        // Keyed by the client line id, so a Draft revision updates a kept line in place, removes a dropped one and adds a new
        // one without ever re-using a key in one save. Product/unit uniqueness within a version is the aggregate's rule (a
        // unique index here would collide transiently when two Draft lines swap products in one save).
        entity.HasKey(x => new { x.CompanyId, x.PriceListId, x.VersionNo, x.LineId });
        entity.HasIndex(x => new { x.CompanyId, x.PriceListId, x.VersionNo, x.LineNo }).HasDatabaseName("IX_PriceListLines_LineNo");
        entity.HasIndex(x => new { x.CompanyId, x.ProductId }).HasDatabaseName("IX_PriceListLines_CompanyId_ProductId");
        entity.HasIndex(x => new { x.CompanyId, x.PriceListId, x.VersionNo, x.ProductId, x.PriceUnitId, x.LowerQuantity })
            .HasDatabaseName("IX_PriceListLines_QuantityTier");
        entity.Property(x => x.LowerQuantity).HasPrecision(18, 3);
        entity.Property(x => x.UpperQuantity).HasPrecision(18, 3);
        entity.Property(x => x.UnitPrice).HasPrecision(19, 4);
        entity.Property(x => x.FloorUnitPrice).HasPrecision(19, 4);
        entity.HasOne<PriceListVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PriceListId, x.VersionNo })
            .HasPrincipalKey(x => new { x.CompanyId, x.PriceListId, x.VersionNo })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PriceListProductReference>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureAttempts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PriceListAttemptRow>();
        entity.ToTable("PriceListAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_PriceListAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_PriceListAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            // ADR-011 §7: a stored result or a recorded terminal refusal, never both.
            table.HasCheckConstraint("CK_PriceListAttempts_Outcome",
                $"([Outcome] = 1 AND [PriceListId] IS NOT NULL AND [PriceListId] <> {EmptyGuid} AND [ResultJson] IS NOT NULL "
                + "AND ISJSON([ResultJson]) = 1 AND [RefusalJson] IS NULL) "
                + "OR ([Outcome] = 2 AND [PriceListId] IS NULL AND [ResultJson] IS NULL AND [RefusalJson] IS NOT NULL "
                + "AND ISJSON([RefusalJson]) = 1)");
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        entity.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        // One create attempt per stored list (the Sales #338 lesson); mutation attempts share the list id.
        entity.HasIndex(x => new { x.CompanyId, x.PriceListId }).IsUnique().HasDatabaseName("UX_PriceListAttempts_PriceListId")
            .HasFilter("[PriceListId] IS NOT NULL AND [CommandName] = N'" + ProductPriceListCommandsV1.CreateV1 + "'");
        entity.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PriceListRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PriceListId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// SAT-D149 (EK20): the product's item type on the SHARED card (one value for all companies, keyed by the product; no company
    /// filter, like the global card it extends) and its command answers (company-scoped: a command runs in one company). The tax
    /// per item type is the Settings owner's default (SAT-D139); nothing tax-related is stored here.
    /// </summary>
    private void ConfigureItemTypes(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProductCardItemTypeRow>();
        entity.ToTable("ProductCardItemTypes", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductCardItemTypes_Identity",
                $"[ProductId] <> {EmptyGuid} AND [UpdatedInCompanyId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_ProductCardItemTypes_ItemType", "[ItemType] IN ('Steel', 'Scrap', 'Service')");
            table.HasCheckConstraint("CK_ProductCardItemTypes_Version", "[Version] >= 1 AND LEN([UpdatedBy]) > 0");
        });
        entity.HasKey(x => x.ProductId);
        entity.Property(x => x.ProductId).ValueGeneratedNever();
        entity.Property(x => x.ItemType).HasMaxLength(16).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.UpdatedBy).HasMaxLength(128).IsRequired();
        entity.HasOne<PriceListProductReference>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.UpdatedInCompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        var attempt = modelBuilder.Entity<ProductItemTypeAttemptRow>();
        attempt.ToTable("ProductItemTypeAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductItemTypeAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [ProductId] <> {EmptyGuid} AND LEN([CommandName]) > 0 "
                + "AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_ProductItemTypeAttempts_Hashes",
                "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_ProductItemTypeAttempts_Answer",
                "[StatusCode] BETWEEN 200 AND 599 AND ISJSON([AnswerJson]) = 1");
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.AnswerJson).HasColumnType("nvarchar(max)").IsRequired();
        attempt.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// BS-9: the "Ebat/kalınlık" row of the SHARED product card (keyed by the product alone, no company filter, like the item type)
    /// and its command answers (company-scoped). Measures are millimetres (three decimals) and kg/m (six decimals); the density is
    /// the company Settings value and is not stored here.
    /// </summary>
    private void ConfigureDimensions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProductCardDimensionRow>();
        entity.ToTable("ProductCardDimensions", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductCardDimensions_Identity",
                $"[ProductId] <> {EmptyGuid} AND [UpdatedInCompanyId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_ProductCardDimensions_Shape", DimensionShapeCheck);
            table.HasCheckConstraint("CK_ProductCardDimensions_Measures", DimensionMeasureCheck);
            table.HasCheckConstraint("CK_ProductCardDimensions_Text", DimensionTextCheck);
            table.HasCheckConstraint("CK_ProductCardDimensions_Version", "[Version] >= 1 AND LEN([UpdatedBy]) > 0");
        });
        entity.HasKey(x => x.ProductId);
        entity.Property(x => x.ProductId).ValueGeneratedNever();
        entity.Property(x => x.SectionKind).HasMaxLength(32).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ThicknessMm).HasPrecision(18, 3);
        entity.Property(x => x.WidthMm).HasPrecision(18, 3);
        entity.Property(x => x.LengthMm).HasPrecision(18, 3);
        entity.Property(x => x.HeightMm).HasPrecision(18, 3);
        entity.Property(x => x.OuterDiameterMm).HasPrecision(18, 3);
        entity.Property(x => x.WallThicknessMm).HasPrecision(18, 3);
        entity.Property(x => x.KgPerMetre).HasPrecision(18, 6);
        entity.Property(x => x.ProfileSeries).HasMaxLength(8).IsUnicode(false).UseCollation(IdentityCollation);
        entity.Property(x => x.ProfileSize).HasMaxLength(ProductDimensionInputLimitsV1.ProfileSizeMaximumLength).IsUnicode(false)
            .UseCollation(IdentityCollation);
        entity.Property(x => x.MeshSpacingMm).HasPrecision(18, 3);
        entity.Property(x => x.SurfaceFinish).HasMaxLength(ProductDimensionInputLimitsV1.SurfaceFinishMaximumLength);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.UpdatedBy).HasMaxLength(128).IsRequired();
        entity.HasOne<PriceListProductReference>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.UpdatedInCompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        var attempt = modelBuilder.Entity<ProductDimensionAttemptRow>();
        attempt.ToTable("ProductDimensionAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductDimensionAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [ProductId] <> {EmptyGuid} AND LEN([CommandName]) > 0 "
                + "AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_ProductDimensionAttempts_Hashes",
                "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_ProductDimensionAttempts_Answer",
                "[StatusCode] BETWEEN 200 AND 599 AND ISJSON([AnswerJson]) = 1");
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.AnswerJson).HasColumnType("nvarchar(max)").IsRequired();
        attempt.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// KLN-D54: the company's structural profile weight table — an insert-only header per imported version, its rows (series,
    /// size designation, kg/m with six decimals) and the import command answers. All company rows under the company filter.
    /// </summary>
    private void ConfigureProfileWeights(ModelBuilder modelBuilder)
    {
        var table = modelBuilder.Entity<ProductProfileWeightTableRow>();
        table.ToTable("ProductProfileWeightTables", Schema, builder =>
        {
            builder.HasCheckConstraint("CK_ProductProfileWeightTables_Identity", $"[CompanyId] <> {EmptyGuid} AND [Version] >= 1");
            builder.HasCheckConstraint("CK_ProductProfileWeightTables_Text",
                "LEN([SourceLabel]) > 0 AND LEN([Reason]) > 0 AND LEN([ImportedBy]) > 0 AND LEN([CorrelationId]) > 0");
            builder.HasCheckConstraint("CK_ProductProfileWeightTables_RowCount", "[RowCount] >= 1");
        });
        table.HasKey(x => new { x.CompanyId, x.Version });
        table.Property(x => x.Version).ValueGeneratedNever();
        table.Property(x => x.SourceLabel).HasMaxLength(ProductProfileWeightInputLimitsV1.SourceLabelMaximumLength).IsRequired();
        table.Property(x => x.Reason).HasMaxLength(ProductProfileWeightInputLimitsV1.ReasonMaximumLength).IsRequired();
        table.Property(x => x.ImportedBy).HasMaxLength(128).IsRequired();
        table.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        table.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        table.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var row = modelBuilder.Entity<ProductProfileWeightRow>();
        row.ToTable("ProductProfileWeightRows", Schema, builder =>
            builder.HasCheckConstraint("CK_ProductProfileWeightRows_Row", ProfileWeightRowCheck));
        row.HasKey(x => new { x.CompanyId, x.TableVersion, x.ProfileSeries, x.ProfileSize });
        row.Property(x => x.ProfileSeries).HasMaxLength(8).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        row.Property(x => x.ProfileSize).HasMaxLength(ProductDimensionInputLimitsV1.ProfileSizeMaximumLength).IsUnicode(false)
            .UseCollation(IdentityCollation).IsRequired();
        row.Property(x => x.KgPerMetre).HasPrecision(18, 6);
        row.HasOne<ProductProfileWeightTableRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TableVersion })
            .OnDelete(DeleteBehavior.Restrict);
        row.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<ProductProfileWeightAttemptRow>();
        attempt.ToTable("ProductProfileWeightAttempts", Schema, builder =>
        {
            builder.HasCheckConstraint("CK_ProductProfileWeightAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            builder.HasCheckConstraint("CK_ProductProfileWeightAttempts_Hashes",
                "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            builder.HasCheckConstraint("CK_ProductProfileWeightAttempts_Answer",
                "[StatusCode] BETWEEN 200 AND 599 AND ISJSON([AnswerJson]) = 1");
        });
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        attempt.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(x => x.AnswerJson).HasColumnType("nvarchar(max)").IsRequired();
        attempt.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ApplyWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit kayıtları değiştirilemez veya silinemez.");
            }

            if (entry.State == EntityState.Added
                && (entry.Entity.CompanyId == Guid.Empty || !companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId))
            {
                throw new InvalidOperationException("Audit kaydı aktif şirket bağlamıyla eşleşmelidir.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<PriceListProductReference>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Ürün kartı fiyat listesi bağlamından yazılmaz.");
            }
        }

        // SAT-D149: the shared card's item type is written only from a resolved company (recorded as the acting company), is
        // never deleted, and only the item type and the update facts move (its version is the token).
        foreach (var entry in ChangeTracker.Entries<ProductCardItemTypeRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
                || entry.Entity.UpdatedInCompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Ürün kartının kalem tipi silinmez ve yalnız aktif şirket bağlamında, o şirket adına yazılır.");
            }

            string[] allowed =
            [
                nameof(ProductCardItemTypeRow.ItemType), nameof(ProductCardItemTypeRow.UpdatedAtUtc),
                nameof(ProductCardItemTypeRow.UpdatedBy), nameof(ProductCardItemTypeRow.UpdatedInCompanyId),
            ];
            if (entry.State == EntityState.Modified && entry.Properties.Any(property =>
                    property.IsModified && !property.Metadata.IsConcurrencyToken && !allowed.Contains(property.Metadata.Name)))
            {
                throw new InvalidOperationException("Ürün kartının kalem tipinde yalnız tip ve güncelleme bilgisi değişir.");
            }
        }

        // BS-9: the shared card's "Ebat/kalınlık" row follows the item type's rule: written only from a resolved company (recorded
        // as the acting company), never deleted, and only the section, the measures and the update facts move.
        foreach (var entry in ChangeTracker.Entries<ProductCardDimensionRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
                || entry.Entity.UpdatedInCompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException(
                    "Ürün kartının ebat/kalınlık satırı silinmez ve yalnız aktif şirket bağlamında, o şirket adına yazılır.");
            }

            string[] allowed =
            [
                nameof(ProductCardDimensionRow.SectionKind), nameof(ProductCardDimensionRow.ThicknessMm),
                nameof(ProductCardDimensionRow.WidthMm), nameof(ProductCardDimensionRow.LengthMm),
                nameof(ProductCardDimensionRow.HeightMm), nameof(ProductCardDimensionRow.OuterDiameterMm),
                nameof(ProductCardDimensionRow.WallThicknessMm), nameof(ProductCardDimensionRow.KgPerMetre),
                nameof(ProductCardDimensionRow.ProfileSeries), nameof(ProductCardDimensionRow.ProfileSize),
                nameof(ProductCardDimensionRow.MeshSpacingMm), nameof(ProductCardDimensionRow.SurfaceFinish),
                nameof(ProductCardDimensionRow.UpdatedAtUtc), nameof(ProductCardDimensionRow.UpdatedBy),
                nameof(ProductCardDimensionRow.UpdatedInCompanyId),
            ];
            if (entry.State == EntityState.Modified && entry.Properties.Any(property =>
                    property.IsModified && !property.Metadata.IsConcurrencyToken && !allowed.Contains(property.Metadata.Name)))
            {
                throw new InvalidOperationException("Ürün kartının ebat/kalınlık satırında yalnız ölçüler ve güncelleme bilgisi değişir.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<IProductPriceListTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity.CompanyId == Guid.Empty || !companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Fiyat listesi kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Deleted
                || (entry.State == EntityState.Modified && entry.Entity is PriceListLineRow))
            {
                RequireDraftLineChange(entry);
                continue;
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    PriceListRow => [nameof(PriceListRow.Name), nameof(PriceListRow.UpdatedAtUtc)],
                    PriceListVersionRow when (int)entry.OriginalValues[nameof(PriceListVersionRow.State)]! == 1 =>
                    [
                        nameof(PriceListVersionRow.ValidFrom), nameof(PriceListVersionRow.ValidThrough), nameof(PriceListVersionRow.State),
                        nameof(PriceListVersionRow.PreparerActorIdsJson), nameof(PriceListVersionRow.PublishedAtUtc),
                        nameof(PriceListVersionRow.PublishedBy),
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
                    throw new InvalidOperationException("Fiyat listesinin yalnız izinli alanı değişir; yayımlanmış sürüm ve satırları değişmez.");
                }
            }
        }
    }

    /// <summary>
    /// Only a line of a version that is (and stays, in this save) a Draft may be changed or removed — a Draft's lines are
    /// replaced as a whole — and a kept line never changes its identity or version.
    /// </summary>
    private void RequireDraftLineChange(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<IProductPriceListTenantRow> entry)
    {
        if (entry.Entity is not PriceListLineRow line)
        {
            throw new InvalidOperationException("Fiyat listesi kayıtları silinmez; taslak satırları dışında hiçbir şey kaldırılmaz.");
        }

        var owner = ChangeTracker.Entries<PriceListVersionRow>().SingleOrDefault(entry =>
            entry.Entity.CompanyId == line.CompanyId && entry.Entity.PriceListId == line.PriceListId && entry.Entity.VersionNo == line.VersionNo);
        if (owner is null || (int)owner.OriginalValues[nameof(PriceListVersionRow.State)]! != 1 || owner.Entity.State != 1)
        {
            throw new InvalidOperationException("Yalnız taslak sürümün satırları yenilenir; yayımlanmış sürümün satırı silinmez.");
        }

        string[] fixedColumns = [nameof(PriceListLineRow.CompanyId), nameof(PriceListLineRow.PriceListId), nameof(PriceListLineRow.VersionNo),
            nameof(PriceListLineRow.LineId)];
        if (entry.State == EntityState.Modified && entry.Properties.Any(property => property.IsModified && fixedColumns.Contains(property.Metadata.Name)))
        {
            throw new InvalidOperationException("Taslak satırının kimliği ve sürümü değişmez.");
        }
    }
}
