using Microsoft.EntityFrameworkCore;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

// Product-owned context. No DI/runtime registration or migration is included.
// Public writes remain closed. The narrow participant borrows the shared host's
// transaction and flushes Product only; audit/terminal and commit stay in the host.
public sealed class ProductDbContext(DbContextOptions<ProductDbContext> options) : DbContext(options)
{
    // Canonical prefix + '-' + the longest positive Int64 decimal sequence.
    private static readonly int MaximumCodeLength = ProductCode.MaximumPrefixLength + 1
        + long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
    public DbSet<ProductRow> Products => Set<ProductRow>();
    public DbSet<ProductCodeRegistration> CodeRegistrations => Set<ProductCodeRegistration>();
    public DbSet<ProductCodeGroupRow> CodeGroups => Set<ProductCodeGroupRow>();
    public DbSet<ProductCodeCounter> CodeCounters => Set<ProductCodeCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<ProductRow>();
        product.ToTable("Products", "product", table =>
        {
            table.HasCheckConstraint("CK_Products_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [GroupId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Products_OptionalReferences", "([ClassId] IS NULL OR [ClassId] <> '00000000-0000-0000-0000-000000000000') AND ([FamilyId] IS NULL OR [FamilyId] <> '00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint("CK_Products_LifecycleVersion", "[Lifecycle] IN (0,1,2) AND [Version] >= 1");
            table.HasCheckConstraint("CK_Products_ReferencesJson", "ISJSON([ReferencesJson], OBJECT) = 1");
            table.HasCheckConstraint("CK_Products_Timestamps", "[UpdatedAtUtc] >= [CreatedAtUtc] AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [UpdatedAtUtc]) = 0");
            foreach (var role in new[] { "Purchasable", "Sellable", "ProductionInput", "ProductionOutput" })
                table.HasCheckConstraint($"CK_Products_{role}",
                    $"([{role}Provenance] = 0 AND [{role}Value] IS NULL) OR ([{role}Provenance] IN (1,2) AND [{role}Value] IS NOT NULL)");
        });
        product.HasKey(row => row.Id);
        product.Property(row => row.Id).ValueGeneratedNever();
        product.Property(row => row.Code).HasMaxLength(MaximumCodeLength).IsUnicode(false).UseCollation("Latin1_General_100_BIN2").IsRequired();
        product.Property(row => row.Name).HasMaxLength(Domain.Products.Product.MaximumNameLength).UseCollation("Turkish_100_CI_AS").IsRequired();
        product.Property(row => row.ReferencesJson).HasColumnType("nvarchar(max)").IsRequired();
        product.Property(row => row.Version).IsConcurrencyToken();
        product.HasIndex(row => row.Code).IsUnique();
        product.HasIndex(row => new { row.Name, row.Id });
        product.HasIndex(row => new { row.UpdatedAtUtc, row.Id });
        product.HasIndex(row => new { row.GroupId, row.Id });
        product.HasOne<ProductCodeGroupRow>().WithMany().HasForeignKey(row => row.GroupId).OnDelete(DeleteBehavior.Restrict);

        var group = modelBuilder.Entity<ProductCodeGroupRow>();
        group.ToTable("CodeGroups", "product", table =>
            table.HasCheckConstraint("CK_CodeGroups_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'"));
        group.HasKey(row => row.Id);
        group.Property(row => row.Id).ValueGeneratedNever();
        group.Property(row => row.Name).HasMaxLength(200).UseCollation("Turkish_100_CI_AS").IsRequired();
        group.Property(row => row.Prefix).HasMaxLength(ProductCode.MaximumPrefixLength).IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2").IsRequired();

        var counter = modelBuilder.Entity<ProductCodeCounter>();
        counter.ToTable("CodeCounters", "product", table =>
            table.HasCheckConstraint("CK_CodeCounters_Sequence", "[LastSequence] >= 0"));
        counter.HasKey(row => row.Prefix);
        counter.Property(row => row.Prefix).HasMaxLength(ProductCode.MaximumPrefixLength).IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");
        counter.Property(row => row.LastSequence).IsConcurrencyToken();

        var code = modelBuilder.Entity<ProductCodeRegistration>();
        code.ToTable("CodeRegistrations", "product");
        code.HasKey(row => row.Code);
        code.Property(row => row.Code).HasMaxLength(MaximumCodeLength).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        code.HasOne<ProductRow>().WithMany().HasForeignKey(row => row.ProductId).OnDelete(DeleteBehavior.Restrict);
        code.HasIndex(row => row.ProductId).IsUnique().HasFilter("[IsCurrent] = 1");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardWrite();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardWrite();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardWrite()
    {
        // Public writes never enroll a context. Only the narrow participant may
        // flush; the host separately proves audit/terminal completion at commit.
        throw new InvalidOperationException("Public Product writes have no atomic writer enrollment.");
    }

    internal Task<int> FlushEnlistedAsync(ProductDraftParticipant participant, CancellationToken cancellationToken)
    {
        // The participant has a private constructor, owns this exact fresh context,
        // validates its borrowed transaction and the complete Product-only change
        // set. This path does not enable any public SaveChanges overload.
        ArgumentNullException.ThrowIfNull(participant);
        participant.ValidateFlush(this);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }
}
