using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

public sealed partial class ProductPriceListDbContext
{
    internal DbSet<ProductPaintMembershipVersionRow> PaintMembershipVersions => Set<ProductPaintMembershipVersionRow>();
    internal DbSet<ProductPaintMembershipLineRow> PaintMembershipLines => Set<ProductPaintMembershipLineRow>();
    internal DbSet<ProductPaintMembershipAttemptRow> PaintMembershipAttempts => Set<ProductPaintMembershipAttemptRow>();
    internal const string PaintMembershipVersionCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1 "
        + "AND [SourceKind] IN ('Import','Edit') AND LEN([SourceLabel]) > 0 AND LEN([Reason]) > 0 AND LEN([ActorId]) > 0 "
        + "AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 "
        + "AND (([SourceKind] = 'Import' AND [FileName] IS NOT NULL AND LEN([FileName]) > 0 AND LEN([FileSha256]) = 64 AND [FileSha256] IS NOT NULL) "
        + "OR ([SourceKind] = 'Edit' AND [FileName] IS NULL AND [FileSha256] IS NULL))";
    internal const string PaintMembershipLineCheck = "[ProductId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [MembershipVersion] >= 1 AND [MembershipVersion] <= [Version] AND [RecordedProductVersion] >= 1";
    internal const string PaintMembershipAttemptCheck = "LEN([CommandName]) > 0 AND LEN([IdempotencyKeyHash]) = 64 "
        + "AND LEN([RequestHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson], OBJECT) = 1 "
        + "AND [StatusCode] IN (200,400,404,409,422) AND DATEPART(TZOFFSET,[CreatedAtUtc]) = 0";

    private void ConfigurePaintMemberships(ModelBuilder modelBuilder)
    {
        var header = modelBuilder.Entity<ProductPaintMembershipVersionRow>();
        header.ToTable("PaintMembershipVersions", Schema, table => table.HasCheckConstraint("CK_PaintMembershipVersions_State", PaintMembershipVersionCheck));
        header.HasKey(x => new { x.CompanyId, x.Version });
        header.Property(x => x.SourceKind).HasMaxLength(16).IsUnicode(false);
        header.Property(x => x.SourceLabel).HasMaxLength(200);
        header.Property(x => x.FileName).HasMaxLength(255);
        header.Property(x => x.FileSha256).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        header.Property(x => x.Reason).HasMaxLength(4000);
        header.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        header.Property(x => x.CorrelationId).HasMaxLength(128);
        header.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        header.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var line = modelBuilder.Entity<ProductPaintMembershipLineRow>();
        line.ToTable("PaintMembershipLines", Schema, table => table.HasCheckConstraint("CK_PaintMembershipLines_State", PaintMembershipLineCheck));
        line.HasKey(x => new { x.CompanyId, x.Version, x.ProductId });
        line.HasOne<ProductPaintMembershipVersionRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        line.HasOne<ProductPaintMembershipVersionRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MembershipVersion })
            .HasPrincipalKey(x => new { x.CompanyId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        line.HasOne<PriceListProductReference>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        line.HasIndex(x => new { x.CompanyId, x.ProductId, x.Version });
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var attempt = modelBuilder.Entity<ProductPaintMembershipAttemptRow>();
        attempt.ToTable("PaintMembershipAttempts", Schema, table => table.HasCheckConstraint("CK_PaintMembershipAttempts_State", PaintMembershipAttemptCheck));
        attempt.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        attempt.Property(x => x.CommandName).HasMaxLength(100).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(x => x.CorrelationId).HasMaxLength(128);
        attempt.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        attempt.HasOne<PriceListCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}
