using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal DbSet<PurchaseOrderServiceMembershipRow> OrderServiceMemberships => Set<PurchaseOrderServiceMembershipRow>();
    internal const string ServiceMembershipIdentityCheck = "[CompanyId] <> " + EmptyGuid + " AND [OrderId] <> " + EmptyGuid
        + " AND [ServiceOrderId] <> " + EmptyGuid + " AND [SupplierPartyId] <> " + EmptyGuid
        + " AND [CompanyAccountId] <> " + EmptyGuid + " AND [CurrencyId] <> " + EmptyGuid
        + " AND [PreparedVersion] > 0 AND [OrderVersionAfter] > 1 AND [MemberNo] > 0";
    internal const string ServiceMembershipSnapshotCheck = "ISJSON([LineIdsJson]) = 1 AND LEFT(LTRIM([LineIdsJson]), 1) = N'['"
        + " AND DATEPART(TZOFFSET, [RecordedAtUtc]) = 0";

    private void ConfigureServiceMemberships(ModelBuilder builder)
    {
        var membership = builder.Entity<PurchaseOrderServiceMembershipRow>();
        membership.ToTable("PurchaseOrderServiceMemberships", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderServiceMemberships_Identity", ServiceMembershipIdentityCheck);
            table.HasCheckConstraint("CK_PurchaseOrderServiceMemberships_Snapshot", ServiceMembershipSnapshotCheck);
        });
        // One native service owner can belong to exactly one commercial parent in its company, forever.
        membership.HasKey(x => new { x.CompanyId, x.ServiceOrderId });
        membership.HasIndex(x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter, x.MemberNo }).IsUnique();
        membership.Property(x => x.LineIdsJson).HasColumnType("nvarchar(max)").IsRequired();
        membership.HasOne<PurchaseOrderRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        membership.HasOne<PurchaseServiceOrderRevisionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ServiceOrderId, x.PreparedVersion })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        membership.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void GuardServiceMemberships()
    {
        foreach (var entry in ChangeTracker.Entries<PurchaseOrderServiceMembershipRow>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("A service membership is immutable; it cannot be removed or rebound.");
            if (entry.State != EntityState.Added) continue;
            var row = entry.Entity;
            _ = PurchasingStore.ServiceMembershipOf(row);
            var prepared = ChangeTracker.Entries<PurchaseServiceOrderRevisionRow>().SingleOrDefault(x =>
                x.Entity.CompanyId == row.CompanyId && x.Entity.OrderId == row.ServiceOrderId && x.Entity.Version == row.PreparedVersion);
            if (prepared is null || prepared.State is not (EntityState.Unchanged or EntityState.Added))
                throw new InvalidOperationException("Membership requires the exact tracked immutable prepared revision.");
            _ = PurchasingStore.PreparedServiceMembership(row, prepared.Entity);
            var parent = ChangeTracker.Entries<PurchaseOrderRow>().SingleOrDefault(x => x.Entity.CompanyId == row.CompanyId && x.Entity.Id == row.OrderId);
            if (parent is null || parent.State != EntityState.Modified || parent.Entity.Status != (int)PurchaseOrderStatus.Draft
                || parent.OriginalValues.GetValue<int>(nameof(PurchaseOrderRow.Status)) != (int)PurchaseOrderStatus.Draft
                || parent.Entity.Version != row.OrderVersionAfter
                || parent.Entity.Version != parent.OriginalValues.GetValue<long>(nameof(PurchaseOrderRow.Version)) + 1
                || parent.Entity.UpdatedAtUtc != row.RecordedAtUtc || parent.Entity.SupplierPartyId != row.SupplierPartyId
                || parent.Entity.CompanyAccountId != row.CompanyAccountId || parent.Entity.CurrencyId != row.CurrencyId)
                throw new InvalidOperationException("A membership requires its exact tracked Draft parent binding slot.");
        }
        foreach (var group in ChangeTracker.Entries<PurchaseOrderServiceMembershipRow>().Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity).GroupBy(x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter }))
        {
            var rows = group.OrderBy(x => x.MemberNo).ToArray();
            if (rows.Select((x, i) => x.MemberNo != i + 1).Any(x => x) || rows.Select(x => x.ServiceOrderId).Distinct().Count() != rows.Length
                || rows.Any(x => x.RecordedAtUtc != rows[0].RecordedAtUtc))
                throw new InvalidOperationException("A membership batch must be complete, ordered and unique.");
        }
    }
}

internal sealed class PurchaseOrderServiceMembershipRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ServiceOrderId { get; set; }
    public long PreparedVersion { get; set; }
    public Guid SupplierPartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public long OrderVersionAfter { get; set; }
    public int MemberNo { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string LineIdsJson { get; set; } = "";
}
