using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// Finance's own tables in the <c>finance</c> schema (FIN-T04a, ADR-002).
/// <list type="bullet">
/// <item>Every row is a tenant row: a global query filter on the server-resolved company, and a
/// write guard that refuses any row of another company, any company change and any write without a
/// resolved company.</item>
/// <item>A receipt draft changes only its preparation fields, version and update trace; nothing is
/// deleted. A command record is append-only.</item>
/// <item>Idempotency: (company, actor, key) is the command record's key, so one key is one attempt
/// per actor and company under a race.</item>
/// </list>
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "finance";

    /// <summary>Binary collation: an idempotency key and an actor id compare exactly (no case folding).</summary>
    public const string IdentityCollation = "Latin1_General_100_BIN2";

    internal DbSet<ReceiptDraftRow> ReceiptDrafts => Set<ReceiptDraftRow>();

    internal DbSet<FinanceCommandRow> Commands => Set<FinanceCommandRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureReceiptDrafts(modelBuilder);
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

    private void ConfigureReceiptDrafts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ReceiptDraftRow>();
        entity.ToTable("ReceiptDrafts", Schema, table =>
        {
            table.HasCheckConstraint("CK_ReceiptDrafts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [PartyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CompanyAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [TreasuryAccountId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ReceiptDrafts_Text",
                "LEN([AmountText]) > 0 AND LEN([ProfileId]) > 0 AND LEN([ProfileVersion]) > 0 "
                + "AND LEN([CreatedBy]) > 0 AND LEN([UpdatedBy]) > 0");
            table.HasCheckConstraint("CK_ReceiptDrafts_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        // No local digit limit: the shared ExactPositiveDecimalV1 contract sets none (Finance examples README).
        entity.Property(x => x.AmountText).HasColumnType("varchar(max)").IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileId).HasMaxLength(36).IsUnicode(false).IsRequired();
        entity.Property(x => x.ProfileVersion).HasMaxLength(20).IsUnicode(false).IsRequired();
        entity.Property(x => x.OccurredOn).HasColumnType("date");
        entity.Property(x => x.ValueOn).HasColumnType("date");
        entity.Property(x => x.BankReference).HasMaxLength(128);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.UpdatedBy).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.UpdatedAtUtc, x.Id });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureCommands(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FinanceCommandRow>();
        entity.ToTable("Commands", Schema, table =>
        {
            table.HasCheckConstraint("CK_Commands_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([ActorId]) > 0 AND LEN([IdempotencyKey]) > 0 AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0");
            table.HasCheckConstraint("CK_Commands_PayloadHash", "LEN([PayloadHash]) = 64");
            table.HasCheckConstraint("CK_Commands_ResultJson", "ISJSON([ResultJson]) = 1");
        });
        entity.HasKey(x => new { x.CompanyId, x.ActorId, x.IdempotencyKey });
        entity.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.Property(x => x.IdempotencyKey).HasMaxLength(128).UseCollation(IdentityCollation);
        entity.Property(x => x.CommandName).HasMaxLength(64).IsRequired();
        entity.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.DraftId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ReceiptDraftRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ApplyWriteGuards()
    {
        FinanceAuditEventTenantGuard.Validate(this, companyContext);

        foreach (var entry in ChangeTracker.Entries<IFinanceTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Finans kayıtları silinmez.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Finans kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Finans kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    ReceiptDraftRow =>
                    [
                        nameof(ReceiptDraftRow.PartyId), nameof(ReceiptDraftRow.CompanyAccountId),
                        nameof(ReceiptDraftRow.TreasuryAccountId), nameof(ReceiptDraftRow.CurrencyId),
                        nameof(ReceiptDraftRow.AmountText), nameof(ReceiptDraftRow.ProfileId),
                        nameof(ReceiptDraftRow.ProfileVersion), nameof(ReceiptDraftRow.OccurredOn),
                        nameof(ReceiptDraftRow.ValueOn), nameof(ReceiptDraftRow.BankReference),
                        nameof(ReceiptDraftRow.Description), nameof(ReceiptDraftRow.UpdatedAtUtc),
                        nameof(ReceiptDraftRow.UpdatedBy),
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
                    throw new InvalidOperationException(
                        "Finans kaydının yalnız izinli alanı değişir; komut kayıtları değişmez.");
                }
            }
        }
    }
}
