using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal const string UnorderedAcceptanceIdentityCheckV1 = "[CompanyId] <> " + EmptyGuid + " AND [AcceptanceId] <> " + EmptyGuid + " AND [AcceptanceLineId] <> " + EmptyGuid + " AND [Version] = 1";
    internal const string UnorderedAcceptanceJsonCheckV1 = "ISJSON([RecordJson]) = 1 AND LEN([RecordHash]) = 64";
    internal const string UnorderedAcceptanceAttemptIdentityCheckV1 = "[CompanyId] <> " + EmptyGuid + " AND [AcceptanceId] <> " + EmptyGuid + " AND [AcceptanceLineId] <> " + EmptyGuid;
    internal const string UnorderedAcceptanceAttemptReceiptCheckV1 = "LEN([KeyHash]) = 64 AND LEN([RequestHash]) = 64 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([RecordJson]) = 1";

    internal DbSet<PurchaseUnorderedAcceptanceRow> UnorderedAcceptances => Set<PurchaseUnorderedAcceptanceRow>();
    internal DbSet<PurchaseUnorderedAcceptanceAttemptRow> UnorderedAcceptanceAttempts => Set<PurchaseUnorderedAcceptanceAttemptRow>();

    private void ConfigureUnorderedAcceptances(ModelBuilder builder)
    {
        var fact = builder.Entity<PurchaseUnorderedAcceptanceRow>();
        fact.ToTable("UnorderedAcceptanceFacts", Schema, table =>
        {
            table.HasCheckConstraint("CK_UnorderedAcceptanceFacts_Identity", UnorderedAcceptanceIdentityCheckV1);
            table.HasCheckConstraint("CK_UnorderedAcceptanceFacts_Json", UnorderedAcceptanceJsonCheckV1);
        });
        fact.HasKey(x => new { x.CompanyId, x.AcceptanceId, x.AcceptanceLineId });
        fact.Property(x => x.Version).IsConcurrencyToken();
        fact.Property(x => x.RecordJson).HasColumnType("nvarchar(max)");
        fact.Property(x => x.RecordHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        fact.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        fact.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var attempt = builder.Entity<PurchaseUnorderedAcceptanceAttemptRow>();
        attempt.ToTable("UnorderedAcceptanceAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_UnorderedAcceptanceAttempts_Identity", UnorderedAcceptanceAttemptIdentityCheckV1);
            table.HasCheckConstraint("CK_UnorderedAcceptanceAttempts_Receipt", UnorderedAcceptanceAttemptReceiptCheckV1);
        });
        attempt.HasKey(x => new { x.CompanyId, x.KeyHash }); // This table owns only RecordCommand.
        attempt.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation);
        attempt.Property(x => x.CorrelationId).HasMaxLength(128);
        attempt.Property(x => x.RecordJson).HasColumnType("nvarchar(max)");
        attempt.HasIndex(x => new { x.CompanyId, x.AcceptanceId, x.AcceptanceLineId }).IsUnique();
        attempt.HasOne<PurchaseUnorderedAcceptanceRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AcceptanceId, x.AcceptanceLineId }).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void GuardUnorderedAcceptances()
    {
        foreach (var entry in ChangeTracker.Entries<PurchaseUnorderedAcceptanceRow>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Siparişsiz kabul olgusu değiştirilemez.");
            if (entry.State == EntityState.Added) _ = PurchaseUnorderedAcceptanceStoreV1.Restore(entry.Entity);
        }
        foreach (var entry in ChangeTracker.Entries<PurchaseUnorderedAcceptanceAttemptRow>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Siparişsiz kabul makbuzu değiştirilemez.");
            if (entry.State != EntityState.Added) continue;
            var record = PurchaseUnorderedAcceptanceStoreV1.Restore(entry.Entity.RecordJson);
            if (record.Source.CompanyId != entry.Entity.CompanyId || record.Source.AcceptanceId != entry.Entity.AcceptanceId
                || record.Source.AcceptanceLineId != entry.Entity.AcceptanceLineId || record.RecordedBy != entry.Entity.ActorId
                || PurchasingRequestHash.Compute(PurchaseUnorderedAcceptanceOwnerContractV1.RecordCommand, record.Source.AcceptanceId,
                    PurchaseUnorderedAcceptanceOwnerContractV1.Request(record)) != entry.Entity.RequestHash)
                throw new InvalidOperationException("Siparişsiz kabul makbuzunun kaynağı uyuşmuyor.");
        }
    }
}

internal sealed class PurchaseUnorderedAcceptanceRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; } public Guid AcceptanceId { get; set; } public Guid AcceptanceLineId { get; set; }
    public long Version { get; set; } public string RecordJson { get; set; } = ""; public string RecordHash { get; set; } = "";
}
internal sealed class PurchaseUnorderedAcceptanceAttemptRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; } public string KeyHash { get; set; } = ""; public string RequestHash { get; set; } = "";
    public Guid AcceptanceId { get; set; } public Guid AcceptanceLineId { get; set; }
    public string ActorId { get; set; } = ""; public string CorrelationId { get; set; } = ""; public string RecordJson { get; set; } = "";
}
