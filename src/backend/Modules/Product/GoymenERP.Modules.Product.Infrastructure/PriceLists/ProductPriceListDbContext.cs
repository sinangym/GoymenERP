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
/// row of the shared card (BS-9, <c>ProductCardDimensions</c>) follows the same rule. The company's kg/m table (URN-D13…D16,
/// <c>ProductProfileWeightImports</c> / <c>ProductProfileWeightLines</c>) is ordinary company-scoped and insert-only, and so is
/// the company's package standard table (B-08 / BS-10, <c>ProductPackageStandardVersions</c> / <c>ProductPackageStandardLines</c>).
/// Migrations have their own history table so that this context never shares one with <c>ProductDbContext</c>.
/// </summary>
public sealed partial class ProductPriceListDbContext(DbContextOptions<ProductPriceListDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "product";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_PriceLists";
    public const string IdentityCollation = "Latin1_General_100_BIN2";
    public const string NameCollation = "Turkish_100_CI_AS";
    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    /// <summary>
    /// BS-9 / KLN-D87: each section kind holds exactly the measures its formula family needs (ProductDimensionSectionKindsV1),
    /// generated from the one rule table of <see cref="ProductDimensionShape"/>. URN-D16 (STK-S37R): a profile no longer stores a
    /// kg/m (it is the company's imported table value); the column stays for round tube and profile rows written before, is never
    /// read and is written NULL from now on. No other kind ever holds one.
    /// </summary>
    internal static readonly string DimensionShapeCheck = ProductDimensionShape.SqlCheck();

    /// <summary>Every stored measure is positive; a missing one is NULL, never zero.</summary>
    internal const string DimensionMeasureCheck =
        "([ThicknessMm] IS NULL OR [ThicknessMm] > 0) AND ([WidthMm] IS NULL OR [WidthMm] > 0) "
        + "AND ([LengthMm] IS NULL OR [LengthMm] > 0) AND ([HeightMm] IS NULL OR [HeightMm] > 0) "
        + "AND ([OuterDiameterMm] IS NULL OR [OuterDiameterMm] > 0) AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) "
        + "AND ([KgPerMetre] IS NULL OR [KgPerMetre] > 0) AND ([MeshSpacingMm] IS NULL OR [MeshSpacingMm] > 0)";

    /// <summary>
    /// KLN-D87: a structural profile series is one of <see cref="ProductProfileSeriesV1"/>; a size designation is ASCII capitals,
    /// digits and dots (binary collation, so the range is exact); a surface text is not blank.
    /// </summary>
    internal const string DimensionTextCheck =
        "([ProfileSeries] IS NULL OR [ProfileSeries] IN ('UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM')) "
        + "AND ([ProfileSize] IS NULL OR (LEN([ProfileSize]) > 0 AND [ProfileSize] NOT LIKE '%[^A-Z0-9.]%')) "
        + "AND ([SurfaceFinish] IS NULL OR LEN([SurfaceFinish]) > 0)";

    /// <summary>
    /// URN-D16 / KLN-D87: a kg/m table row is a profile (width, height, wall) or a round tube (diameter, wall), hollow and positive,
    /// or a structural profile (series and size designation, no measure).
    /// </summary>
    internal const string ProfileWeightLineShapeCheck =
        "([SectionKind] = 'Profile' AND [WidthMm] IS NOT NULL AND [HeightMm] IS NOT NULL AND [OuterDiameterMm] IS NULL "
        + "AND [WallThicknessMm] IS NOT NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL "
        + "AND [WidthMm] > 0 AND [HeightMm] > 0 AND 2 * [WallThicknessMm] <= [WidthMm] AND 2 * [WallThicknessMm] <= [HeightMm]) "
        + "OR ([SectionKind] = 'RoundTube' AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NOT NULL "
        + "AND [WallThicknessMm] IS NOT NULL AND [ProfileSeries] IS NULL AND [ProfileSize] IS NULL "
        + "AND [OuterDiameterMm] > 0 AND 2 * [WallThicknessMm] <= [OuterDiameterMm]) "
        + "OR ([SectionKind] = 'StructuralProfile' AND [WidthMm] IS NULL AND [HeightMm] IS NULL AND [OuterDiameterMm] IS NULL "
        + "AND [WallThicknessMm] IS NULL AND [ProfileSeries] IN ('UPN', 'NPI', 'IPE', 'HEA', 'HEB', 'HEM') "
        + "AND LEN([ProfileSize]) > 0 AND [ProfileSize] NOT LIKE '%[^A-Z0-9.]%')";

    internal DbSet<PriceListRow> PriceLists => Set<PriceListRow>();

    internal DbSet<PriceListVersionRow> Versions => Set<PriceListVersionRow>();

    internal DbSet<PriceListLineRow> Lines => Set<PriceListLineRow>();

    internal DbSet<PriceListAttemptRow> Attempts => Set<PriceListAttemptRow>();

    internal DbSet<PriceListProductReference> Products => Set<PriceListProductReference>();

    internal DbSet<ProductCardItemTypeRow> ItemTypes => Set<ProductCardItemTypeRow>();

    internal DbSet<ProductItemTypeAttemptRow> ItemTypeAttempts => Set<ProductItemTypeAttemptRow>();

    internal DbSet<ProductCardDimensionRow> Dimensions => Set<ProductCardDimensionRow>();

    internal DbSet<ProductDimensionAttemptRow> DimensionAttempts => Set<ProductDimensionAttemptRow>();

    internal DbSet<ProductProfileWeightImportRow> ProfileWeightImports => Set<ProductProfileWeightImportRow>();

    internal DbSet<ProductProfileWeightLineRow> ProfileWeightLines => Set<ProductProfileWeightLineRow>();

    internal DbSet<ProductProfileWeightAttemptRow> ProfileWeightAttempts => Set<ProductProfileWeightAttemptRow>();

    internal DbSet<ProductPackageStandardVersionRow> PackageStandardVersions => Set<ProductPackageStandardVersionRow>();

    internal DbSet<ProductPackageStandardLineRow> PackageStandardLines => Set<ProductPackageStandardLineRow>();

    internal DbSet<ProductPackageStandardAttemptRow> PackageStandardAttempts => Set<ProductPackageStandardAttemptRow>();

    internal DbSet<ProductPackageStandardCustomerLineRow> PackageStandardCustomerLines => Set<ProductPackageStandardCustomerLineRow>();

    internal DbSet<ProductPackageStandardDecisionRow> PackageStandardDecisions => Set<ProductPackageStandardDecisionRow>();

    internal DbSet<ProductPackageStandardImportFileRow> PackageStandardImportFiles => Set<ProductPackageStandardImportFileRow>();

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
        ConfigurePackageStandards(modelBuilder);
        ConfigurePaintMemberships(modelBuilder);
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
            table.HasCheckConstraint("CK_ProductCardDimensions_Texts", DimensionTextCheck);
            table.HasCheckConstraint("CK_ProductCardDimensions_Version", "[Version] >= 1 AND LEN([UpdatedBy]) > 0");
        });
        entity.HasKey(x => x.ProductId);
        entity.Property(x => x.ProductId).ValueGeneratedNever();
        entity.Property(x => x.SectionKind).HasMaxLength(ProductDimensionInputLimitsV1.SectionKindMaximumLength).IsUnicode(false)
            .UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.ThicknessMm).HasPrecision(18, 3);
        entity.Property(x => x.WidthMm).HasPrecision(18, 3);
        entity.Property(x => x.LengthMm).HasPrecision(18, 3);
        entity.Property(x => x.HeightMm).HasPrecision(18, 3);
        entity.Property(x => x.OuterDiameterMm).HasPrecision(18, 3);
        entity.Property(x => x.WallThicknessMm).HasPrecision(18, 3);
        entity.Property(x => x.KgPerMetre).HasPrecision(18, 6);
        entity.Property(x => x.MeshSpacingMm).HasPrecision(18, 3);
        entity.Property(x => x.ProfileSeries).HasMaxLength(ProductDimensionInputLimitsV1.ProfileSeriesMaximumLength).IsUnicode(false)
            .UseCollation(IdentityCollation);
        entity.Property(x => x.ProfileSize).HasMaxLength(ProductDimensionInputLimitsV1.ProfileSizeMaximumLength).IsUnicode(false)
            .UseCollation(IdentityCollation);
        entity.Property(x => x.SurfaceFinish).HasMaxLength(ProductDimensionInputLimitsV1.SurfaceFinishMaximumLength)
            .UseCollation(NameCollation);
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
    /// URN-D13…D16: the company's kg/m table — one insert-only header per import (the newest version is current; the vault
    /// document of the file is linked to its <c>ImportId</c>), its rows (millimetres three decimals, kg/m six) and its command
    /// answers. All company-scoped.
    /// </summary>
    private void ConfigureProfileWeights(ModelBuilder modelBuilder)
    {
        var header = modelBuilder.Entity<ProductProfileWeightImportRow>();
        header.ToTable("ProductProfileWeightImports", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductProfileWeightImports_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [ImportId] <> {EmptyGuid} AND [DocumentId] <> {EmptyGuid} AND [Version] >= 1 "
                + "AND LEN([ImportedBy]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_ProductProfileWeightImports_Source",
                "LEN(LTRIM(RTRIM([SourceLabel]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([FileName]) > 0");
            table.HasCheckConstraint("CK_ProductProfileWeightImports_File",
                "LEN([FileSha256]) = 64 AND [FileSizeBytes] > 0 AND [RowCount] > 0");
        });
        header.HasKey(x => new { x.CompanyId, x.Version });
        header.HasIndex(x => new { x.CompanyId, x.ImportId }).IsUnique();
        header.Property(x => x.Version).ValueGeneratedNever();
        header.Property(x => x.SourceLabel).HasMaxLength(ProductProfileWeightInputLimitsV1.SourceLabelMaximumLength).IsRequired();
        header.Property(x => x.Reason).HasMaxLength(ProductProfileWeightInputLimitsV1.ReasonMaximumLength).IsRequired();
        header.Property(x => x.FileName).HasMaxLength(ProductProfileWeightInputLimitsV1.FileNameMaximumLength).IsRequired();
        header.Property(x => x.FileSha256).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        header.Property(x => x.ImportedBy).HasMaxLength(128).IsRequired();
        header.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        header.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        header.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<ProductProfileWeightLineRow>();
        line.ToTable("ProductProfileWeightLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductProfileWeightLines_Shape", ProfileWeightLineShapeCheck);
            table.HasCheckConstraint("CK_ProductProfileWeightLines_Values",
                "[LineNo] >= 1 AND ([WallThicknessMm] IS NULL OR [WallThicknessMm] > 0) AND [KgPerMetre] > 0");
        });
        line.HasKey(x => new { x.CompanyId, x.ImportVersion, x.LineNo });
        line.HasIndex(x => new
            {
                x.CompanyId, x.ImportVersion, x.SectionKind, x.WidthMm, x.HeightMm, x.OuterDiameterMm, x.WallThicknessMm,
                x.ProfileSeries, x.ProfileSize,
            })
            .IsUnique()
            // NULL is one value here (a profile has no diameter, a round tube no width/height, a structural profile no measure):
            // no default "IS NOT NULL" filter.
            .HasFilter(null)
            .HasDatabaseName("UX_ProductProfileWeightLines_Measures");
        line.Property(x => x.SectionKind).HasMaxLength(ProductDimensionInputLimitsV1.SectionKindMaximumLength).IsUnicode(false)
            .UseCollation(IdentityCollation).IsRequired();
        line.Property(x => x.ProfileSeries).HasMaxLength(ProductDimensionInputLimitsV1.ProfileSeriesMaximumLength).IsUnicode(false)
            .UseCollation(IdentityCollation);
        line.Property(x => x.ProfileSize).HasMaxLength(ProductDimensionInputLimitsV1.ProfileSizeMaximumLength).IsUnicode(false)
            .UseCollation(IdentityCollation);
        line.Property(x => x.WidthMm).HasPrecision(18, 3);
        line.Property(x => x.HeightMm).HasPrecision(18, 3);
        line.Property(x => x.OuterDiameterMm).HasPrecision(18, 3);
        line.Property(x => x.WallThicknessMm).HasPrecision(18, 3);
        line.Property(x => x.KgPerMetre).HasPrecision(18, 6);
        line.HasOne<ProductProfileWeightImportRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ImportVersion })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<ProductProfileWeightAttemptRow>();
        attempt.ToTable("ProductProfileWeightAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductProfileWeightAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_ProductProfileWeightAttempts_Hashes",
                "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_ProductProfileWeightAttempts_Answer",
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
    /// B-08 / BS-10: the company's package standard table — one insert-only header per version (an Excel import with its source
    /// label and file facts, or an ERP edit naming the product), the whole table's entries of that version and the command answers.
    /// All company-scoped; an entry and an edited product reference the shared card.
    /// </summary>
    private void ConfigurePackageStandards(ModelBuilder modelBuilder)
    {
        var header = modelBuilder.Entity<ProductPackageStandardVersionRow>();
        header.ToTable("ProductPackageStandardVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductPackageStandardVersions_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [VersionId] <> {EmptyGuid} AND [Version] >= 1 AND [EntryCount] >= 0 "
                + "AND [CustomerEntryCount] >= 0 "
                + "AND LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
            // SAT-D382 (STK-SC36-c): an edit names the product and, for a customer override, the customer; an import never
            // names a customer. A customer override exists only on a version that requires approval.
            table.HasCheckConstraint("CK_ProductPackageStandardVersions_Source",
                "([SourceKind] = 'Import' AND [SourceLabel] IS NOT NULL AND LEN(LTRIM(RTRIM([SourceLabel]))) > 0 "
                + "AND [FileName] IS NOT NULL AND LEN([FileName]) > 0 AND [FileSha256] IS NOT NULL AND LEN([FileSha256]) = 64 "
                + "AND [FileSizeBytes] IS NOT NULL AND [FileSizeBytes] > 0 AND [EditedProductId] IS NULL "
                + "AND [EditedCustomerId] IS NULL AND [EntryCount] > 0) "
                + "OR ([SourceKind] = 'Edit' AND [SourceLabel] IS NULL AND [FileName] IS NULL AND [FileSha256] IS NULL "
                + $"AND [FileSizeBytes] IS NULL AND [EditedProductId] IS NOT NULL AND [EditedProductId] <> {EmptyGuid} "
                + $"AND ([EditedCustomerId] IS NULL OR [EditedCustomerId] <> {EmptyGuid}))");
            table.HasCheckConstraint("CK_ProductPackageStandardVersions_Approval",
                "[RequiresApproval] = 1 OR ([CustomerEntryCount] = 0 AND [EditedCustomerId] IS NULL)");
        });
        header.HasKey(x => new { x.CompanyId, x.Version });
        header.HasIndex(x => new { x.CompanyId, x.VersionId }).IsUnique();
        header.Property(x => x.Version).ValueGeneratedNever();
        header.Property(x => x.SourceKind).HasMaxLength(16).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        header.Property(x => x.SourceLabel).HasMaxLength(ProductPackageStandardInputLimitsV2.SourceLabelMaximumLength);
        header.Property(x => x.Reason).HasMaxLength(ProductPackageStandardInputLimitsV2.ReasonMaximumLength).IsRequired();
        header.Property(x => x.FileName).HasMaxLength(ProductPackageStandardInputLimitsV2.FileNameMaximumLength);
        header.Property(x => x.FileSha256).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        header.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        header.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        header.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        header.HasOne<PriceListProductReference>().WithMany().HasForeignKey(x => x.EditedProductId)
            .OnDelete(DeleteBehavior.Restrict);
        header.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<ProductPackageStandardLineRow>();
        line.ToTable("ProductPackageStandardLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductPackageStandardLines_Values",
                $"[ProductId] <> {EmptyGuid} AND [PiecesPerPackage] > 0");
        });
        line.HasKey(x => new { x.CompanyId, x.Version, x.ProductId });
        line.HasOne<ProductPackageStandardVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Version })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasOne<PriceListProductReference>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = modelBuilder.Entity<ProductPackageStandardAttemptRow>();
        attempt.ToTable("ProductPackageStandardAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductPackageStandardAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_ProductPackageStandardAttempts_Hashes",
                "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            table.HasCheckConstraint("CK_ProductPackageStandardAttempts_Answer",
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

        ConfigurePackageStandardApprovals(modelBuilder);
    }

    /// <summary>
    /// SAT-D382 / SAT-D383 (STK-SC36-c): the customer overrides of a version, the single approval decision of a version and the
    /// kept import files with their result reports. All company-scoped and insert-only; the kept files are also refused an
    /// update or delete by a database trigger (they live as long as the audit record; no destruction exists).
    /// </summary>
    private void ConfigurePackageStandardApprovals(ModelBuilder modelBuilder)
    {
        var customerLine = modelBuilder.Entity<ProductPackageStandardCustomerLineRow>();
        customerLine.ToTable("ProductPackageStandardCustomerLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductPackageStandardCustomerLines_Values",
                $"[CompanyId] <> {EmptyGuid} AND [CustomerId] <> {EmptyGuid} AND [ProductId] <> {EmptyGuid} "
                + "AND [PiecesPerPackage] > 0");
        });
        customerLine.HasKey(x => new { x.CompanyId, x.Version, x.CustomerId, x.ProductId });
        customerLine.HasOne<ProductPackageStandardVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Version })
            .OnDelete(DeleteBehavior.Restrict);
        customerLine.HasOne<PriceListProductReference>().WithMany().HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        customerLine.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var decision = modelBuilder.Entity<ProductPackageStandardDecisionRow>();
        decision.ToTable("ProductPackageStandardDecisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductPackageStandardDecisions_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [DecisionId] <> {EmptyGuid} AND [Version] >= 1 AND LEN([DecidedBy]) > 0 "
                + "AND LEN([CorrelationId]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0");
            table.HasCheckConstraint("CK_ProductPackageStandardDecisions_Decision",
                "([Decision] = 'Approved' AND [EffectiveFrom] IS NOT NULL) "
                + "OR ([Decision] = 'Rejected' AND [EffectiveFrom] IS NULL)");
        });
        decision.HasKey(x => new { x.CompanyId, x.Version });
        decision.HasIndex(x => new { x.CompanyId, x.DecisionId }).IsUnique();
        decision.Property(x => x.Version).ValueGeneratedNever();
        decision.Property(x => x.Decision).HasMaxLength(16).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        decision.Property(x => x.EffectiveFrom).HasColumnType("date");
        decision.Property(x => x.Reason).HasMaxLength(ProductPackageStandardInputLimitsV2.ReasonMaximumLength).IsRequired();
        decision.Property(x => x.DecidedBy).HasMaxLength(128).IsRequired();
        decision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        decision.HasOne<ProductPackageStandardVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Version })
            .OnDelete(DeleteBehavior.Restrict);
        decision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var file = modelBuilder.Entity<ProductPackageStandardImportFileRow>();
        file.ToTable("ProductPackageStandardImportFiles", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductPackageStandardImportFiles_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [ImportId] <> {EmptyGuid} AND LEN([CreatedBy]) > 0 "
                + "AND LEN([CorrelationId]) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([SourceLabel]))) > 0");
            table.HasCheckConstraint("CK_ProductPackageStandardImportFiles_File",
                "LEN([FileName]) > 0 AND LEN([FileSha256]) = 64 AND [FileSizeBytes] > 0 "
                + $"AND [FileSizeBytes] <= {ProductPackageStandardInputLimitsV2.MaximumFileBytes} "
                + "AND DATALENGTH([Content]) = [FileSizeBytes] AND ISJSON([ReportJson]) = 1");
            table.HasCheckConstraint("CK_ProductPackageStandardImportFiles_Outcome",
                "([Outcome] = 'Accepted' AND [Version] IS NOT NULL AND [ProblemCode] IS NULL) "
                + "OR ([Outcome] = 'Refused' AND [Version] IS NULL AND [ProblemCode] IS NOT NULL AND LEN([ProblemCode]) > 0)");
        });
        file.HasKey(x => new { x.CompanyId, x.ImportId });
        file.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc });
        file.Property(x => x.FileName).HasMaxLength(ProductPackageStandardInputLimitsV2.FileNameMaximumLength).IsRequired();
        file.Property(x => x.FileSha256).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        file.Property(x => x.Content).HasColumnType("varbinary(max)").IsRequired();
        file.Property(x => x.Outcome).HasMaxLength(16).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        file.Property(x => x.ProblemCode).HasMaxLength(160).IsUnicode(false).UseCollation(IdentityCollation);
        file.Property(x => x.ReportJson).HasColumnType("nvarchar(max)").IsRequired();
        file.Property(x => x.SourceLabel).HasMaxLength(ProductPackageStandardInputLimitsV2.SourceLabelMaximumLength).IsRequired();
        file.Property(x => x.Reason).HasMaxLength(ProductPackageStandardInputLimitsV2.ReasonMaximumLength).IsRequired();
        file.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        file.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        file.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        file.HasOne<ProductPackageStandardVersionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Version })
            .OnDelete(DeleteBehavior.Restrict);
        file.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
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

            // URN-D13…D16: an import of the kg/m table, its rows and its answers are insert-only (a new import is a new version).
            if (entry.State != EntityState.Added
                && entry.Entity is ProductProfileWeightImportRow or ProductProfileWeightLineRow or ProductProfileWeightAttemptRow)
            {
                throw new InvalidOperationException("Metre ağırlığı tablosunun içe aktarımı ve satırları değiştirilmez veya silinmez.");
            }

            if (entry.State != EntityState.Added
                && entry.Entity is ProductPaintMembershipVersionRow or ProductPaintMembershipLineRow or ProductPaintMembershipAttemptRow)
                throw new InvalidOperationException("Boya üyeliği sürümleri, satırları ve işlem yanıtları değiştirilmez veya silinmez.");

            // B-08 / BS-10: a package standard version, its entries and its answers are insert-only (a change is a new version).
            if (entry.State != EntityState.Added
                && entry.Entity is ProductPackageStandardVersionRow or ProductPackageStandardLineRow
                    or ProductPackageStandardAttemptRow or ProductPackageStandardCustomerLineRow
                    or ProductPackageStandardDecisionRow or ProductPackageStandardImportFileRow)
            {
                throw new InvalidOperationException("Paket standardı tablosunun sürümü ve satırları değiştirilmez veya silinmez.");
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
