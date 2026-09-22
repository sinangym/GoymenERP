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
    // PRD-FAM-1: the family registry. Read-only at runtime; no default row is seeded.
    public DbSet<ProductFamilyRow> Families => Set<ProductFamilyRow>();
    public DbSet<ProductTemplateRow> Templates => Set<ProductTemplateRow>();
    public DbSet<ProductTemplateFamilyRow> TemplateFamilies => Set<ProductTemplateFamilyRow>();
    public DbSet<ProductTemplateFieldRow> TemplateFields => Set<ProductTemplateFieldRow>();
    public DbSet<ProductDictionaryEntryRow> DictionaryEntries => Set<ProductDictionaryEntryRow>();

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
        // A card names a released family or none (PRD-FAM-1); no card held a family before the registry.
        product.HasOne<ProductFamilyRow>().WithMany().HasForeignKey(row => row.FamilyId).OnDelete(DeleteBehavior.Restrict);

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

        ConfigureFamilyRegistry(modelBuilder);
    }

    private static void ConfigureFamilyRegistry(ModelBuilder modelBuilder)
    {
        const string Binary = "Latin1_General_100_BIN2";
        const string Turkish = "Turkish_100_CI_AS";

        var family = modelBuilder.Entity<ProductFamilyRow>();
        family.ToTable("Families", "product", table =>
        {
            table.HasCheckConstraint("CK_Families_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Families_Text",
                "LEN([Code]) > 0 AND LEN([Name]) > 0 AND LEN([Cins]) > 0 AND LEN([Tur]) > 0 AND LEN([Tip]) > 0 "
                + "AND LEN([RuleSet]) > 0 AND LEN([SourceRef]) > 0");
        });
        family.HasKey(row => row.Id);
        family.Property(row => row.Id).ValueGeneratedNever();
        family.Property(row => row.Code).HasMaxLength(64).IsUnicode(false).UseCollation(Binary).IsRequired();
        family.Property(row => row.Name).HasMaxLength(200).UseCollation(Turkish).IsRequired();
        family.Property(row => row.Cins).HasMaxLength(64).UseCollation(Turkish).IsRequired();
        family.Property(row => row.Tur).HasMaxLength(64).UseCollation(Turkish).IsRequired();
        family.Property(row => row.Tip).HasMaxLength(64).UseCollation(Turkish).IsRequired();
        family.Property(row => row.RuleSet).HasMaxLength(64).IsUnicode(false).UseCollation(Binary).IsRequired();
        family.Property(row => row.SourceRef).HasMaxLength(64).IsUnicode(false).IsRequired();
        family.HasIndex(row => row.Code).IsUnique();

        var template = modelBuilder.Entity<ProductTemplateRow>();
        template.ToTable("Templates", "product", table =>
        {
            table.HasCheckConstraint("CK_Templates_Identity", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [Version] >= 1");
            table.HasCheckConstraint("CK_Templates_Text", "LEN([Code]) > 0 AND LEN([Name]) > 0 AND LEN([SourceRef]) > 0");
        });
        template.HasKey(row => new { row.Id, row.Version });
        template.Property(row => row.Code).HasMaxLength(64).IsUnicode(false).UseCollation(Binary).IsRequired();
        template.Property(row => row.Name).HasMaxLength(200).UseCollation(Turkish).IsRequired();
        template.Property(row => row.SourceRef).HasMaxLength(64).IsUnicode(false).IsRequired();
        template.HasIndex(row => new { row.Code, row.Version }).IsUnique();

        var templateFamily = modelBuilder.Entity<ProductTemplateFamilyRow>();
        templateFamily.ToTable("TemplateFamilies", "product");
        templateFamily.HasKey(row => new { row.TemplateId, row.TemplateVersion, row.FamilyId });
        templateFamily.HasOne<ProductTemplateRow>().WithMany()
            .HasForeignKey(row => new { row.TemplateId, row.TemplateVersion })
            .OnDelete(DeleteBehavior.Restrict);
        templateFamily.HasOne<ProductFamilyRow>().WithMany().HasForeignKey(row => row.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);

        var field = modelBuilder.Entity<ProductTemplateFieldRow>();
        field.ToTable("TemplateFields", "product", table =>
        {
            table.HasCheckConstraint("CK_TemplateFields_Kind",
                "([Kind] = 1 AND [Dictionary] IS NULL AND [AllowsCandidate] = 0 AND [ParentField] IS NULL) "
                + "OR ([Kind] = 2 AND [Dictionary] IS NOT NULL AND LEN([Dictionary]) > 0)");
            table.HasCheckConstraint("CK_TemplateFields_Condition",
                "([AppliesWhenField] IS NULL AND [AppliesWhenCode] IS NULL) "
                + "OR ([AppliesWhenField] IS NOT NULL AND [AppliesWhenCode] IS NOT NULL)");
            table.HasCheckConstraint("CK_TemplateFields_Key", "LEN([Key]) > 0 AND [Position] >= 1");
            // A candidate never satisfies a required field (ORG #334 P3-1).
            table.HasCheckConstraint("CK_TemplateFields_Candidate", "[Required] = 0 OR [AllowsCandidate] = 0");
        });
        field.HasKey(row => new { row.TemplateId, row.TemplateVersion, row.Key });
        field.Property(row => row.Key).HasMaxLength(64).IsUnicode(false).UseCollation(Binary);
        field.Property(row => row.Role).HasMaxLength(32).IsUnicode(false).UseCollation(Binary);
        field.Property(row => row.Dictionary).HasMaxLength(64).IsUnicode(false).UseCollation(Binary);
        field.Property(row => row.AppliesWhenField).HasMaxLength(64).IsUnicode(false).UseCollation(Binary);
        field.Property(row => row.AppliesWhenCode).HasMaxLength(64).UseCollation(Binary);
        field.Property(row => row.ParentField).HasMaxLength(64).IsUnicode(false).UseCollation(Binary);
        field.HasIndex(row => new { row.TemplateId, row.TemplateVersion, row.Position }).IsUnique();
        field.HasOne<ProductTemplateRow>().WithMany()
            .HasForeignKey(row => new { row.TemplateId, row.TemplateVersion })
            .OnDelete(DeleteBehavior.Restrict);

        var entry = modelBuilder.Entity<ProductDictionaryEntryRow>();
        entry.ToTable("DictionaryEntries", "product", table =>
        {
            table.HasCheckConstraint("CK_DictionaryEntries_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_DictionaryEntries_Text",
                "LEN([Dictionary]) > 0 AND LEN([Code]) > 0 AND LEN([DisplayName]) > 0 AND LEN([SourceRef]) > 0 "
                + "AND ([ParentCode] IS NULL OR LEN([ParentCode]) > 0)");
            // A mass only when the source states one; the explicit "none" is an approved value.
            table.HasCheckConstraint("CK_DictionaryEntries_Mass", "[MassGm2] IS NULL OR [MassGm2] > 0");
            table.HasCheckConstraint("CK_DictionaryEntries_ExplicitNone", "[IsExplicitNone] = 0 OR [Status] = 1");
        });
        entry.HasKey(row => new { row.Dictionary, row.Code });
        entry.Property(row => row.Dictionary).HasMaxLength(64).IsUnicode(false).UseCollation(Binary);
        entry.Property(row => row.Code).HasMaxLength(64).UseCollation(Binary);
        entry.Property(row => row.DisplayName).HasMaxLength(200).UseCollation(Turkish).IsRequired();
        entry.Property(row => row.SourceRef).HasMaxLength(64).IsUnicode(false).IsRequired();
        entry.Property(row => row.ParentCode).HasMaxLength(64).UseCollation(Binary);
        entry.Property(row => row.MassGm2).HasPrecision(9, 3);
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

    internal Task<int> AddTechnicalCardFixtureAsync(CancellationToken cancellationToken)
    {
        // Fixed test data only; no arbitrary release input and no public SaveChanges enrollment.
        if (ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A technical fixture requires a clean Product context.");
        var definition = Fixtures.ProductTechnicalCardFixture.Definition();
        Families.AddRange(definition.Families.Select(value => new ProductFamilyRow(value)));
        foreach (var value in definition.Templates)
        {
            var template = new ProductTemplateRow(value);
            Templates.Add(template);
            TemplateFamilies.AddRange(value.FamilyIds.Select(id => new ProductTemplateFamilyRow(template, id)));
            TemplateFields.AddRange(value.Fields.Select((field, index) => new ProductTemplateFieldRow(template, field, index + 1)));
        }
        DictionaryEntries.AddRange(definition.Entries.Select(value => new ProductDictionaryEntryRow(value)));
        return base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    internal Task<int> AddSalesCandidateFixtureAsync(CancellationToken cancellationToken)
    {
        // Fixed test-only card, group, counter and registration; no arbitrary input or public writer enrollment.
        if (ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A sales fixture requires a clean Product context.");
        var group = Fixtures.ProductSalesCandidateFixture.Group();
        var counter = ProductCodeCounter.ForGroup(group);
        var row = ProductRow.FromRecord(Fixtures.ProductSalesCandidateFixture.Record());
        if (counter.Advance().Value != row.Code) throw new InvalidOperationException("Invalid fixture code.");
        CodeGroups.Add(group);
        CodeCounters.Add(counter);
        Products.Add(row);
        CodeRegistrations.Add(ProductCodeRegistration.Current(row));
        return base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    internal Task<int> AddFixtureCodeGroupAsync(ProductCodeGroupRow group, CancellationToken cancellationToken)
    {
        // The gated browser-suite code-group port (Fixtures/ProductCodeGroupFixtureStore) only: the change
        // set is exactly this one added group on an otherwise clean context — no card, code, counter or
        // edit. This path does not enable any public SaveChanges overload either.
        ArgumentNullException.ThrowIfNull(group);
        if (ChangeTracker.Entries().Any())
            throw new InvalidOperationException("A fixture code group is flushed only from a clean Product context.");
        CodeGroups.Add(group);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }
}
