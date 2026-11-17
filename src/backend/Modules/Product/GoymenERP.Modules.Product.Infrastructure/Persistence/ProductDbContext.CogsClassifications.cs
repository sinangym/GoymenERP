using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed partial class ProductDbContext
{
    public DbSet<ProductCogsClassificationRow> CogsClassifications => Set<ProductCogsClassificationRow>();
    private static void ConfigureCogsClassifications(ModelBuilder model)
    {
        var row = model.Entity<ProductCogsClassificationRow>();
        row.ToTable("CogsClassifications", "product", table =>
        {
            table.HasCheckConstraint("CK_CogsClassifications_Identity", "[ProductId] <> '00000000-0000-0000-0000-000000000000' AND [OriginCompanyId] <> '00000000-0000-0000-0000-000000000000' AND [ActorId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_CogsClassifications_Version", "[Version] >= 1 AND [RecordedProductVersion] >= 1");
            table.HasCheckConstraint("CK_CogsClassifications_Kind", "[Kind] IN (1,2)");
            table.HasCheckConstraint("CK_CogsClassifications_Provenance", "LEN([AuthorityRevision]) > 0 AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0");
        });
        row.HasKey(x => new { x.ProductId, x.Version });
        row.Property(x => x.ProductId).ValueGeneratedNever();
        row.Property(x => x.AuthorityRevision).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        row.Property(x => x.Reason).HasMaxLength(1000);
        row.Property(x => x.CorrelationId).HasMaxLength(128).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        row.HasOne<ProductRow>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
    internal Task<int> FlushCogsEnlistedAsync(ProductCogsClassificationParticipant participant, CancellationToken token)
    {
        participant.ValidateFlush(this);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess: true, token);
    }
}
