using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal DbSet<PurchaseReceiptAdjustmentRow> ReceiptAdjustments => Set<PurchaseReceiptAdjustmentRow>();
    internal DbSet<PurchaseOrderCancellationHistoryRow> CancellationHistory => Set<PurchaseOrderCancellationHistoryRow>();

    private void ConfigureReceiptAdjustments(ModelBuilder modelBuilder)
    {
        var adjustment = modelBuilder.Entity<PurchaseReceiptAdjustmentRow>();
        adjustment.ToTable("PurchaseReceiptAdjustments", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseReceiptAdjustments_Identity",
                $"[CompanyId] <> {EmptyGuid} AND [AdjustmentId] <> {EmptyGuid} AND [OrderId] <> {EmptyGuid} AND [AcceptanceId] <> {EmptyGuid} AND [LineId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_PurchaseReceiptAdjustments_Quantities",
                "[UndoQuantity] > 0 AND [NewAcceptedQuantity] >= 0 AND [NetOpenedQuantity] >= 0");
            table.HasCheckConstraint("CK_PurchaseReceiptAdjustments_Replacement",
                $"([NewAcceptanceId] IS NULL AND [NewAcceptedQuantity] = 0) OR ([NewAcceptanceId] IS NOT NULL AND [NewAcceptanceId] <> {EmptyGuid} AND [NewAcceptedQuantity] > 0 AND [IsSupplierReturn] = 0)");
            table.HasCheckConstraint("CK_PurchaseReceiptAdjustments_Text",
                "[OrderVersionAfter] >= 2 AND LEN([RecordedBy]) > 0 AND LEN([Reason]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
        });
        adjustment.HasKey(x => new { x.CompanyId, x.AdjustmentId });
        adjustment.HasIndex(x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter }).IsUnique();
        adjustment.Property(x => x.UndoQuantity).HasPrecision(18, 3);
        adjustment.Property(x => x.NewAcceptedQuantity).HasPrecision(18, 3);
        adjustment.Property(x => x.NetOpenedQuantity).HasPrecision(18, 3);
        adjustment.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        adjustment.Property(x => x.Reason).HasMaxLength(4000).IsRequired();
        adjustment.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        adjustment.Property(x => x.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        adjustment.HasOne<PurchaseAcceptanceRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId }).OnDelete(DeleteBehavior.Restrict);
        adjustment.HasOne<PurchaseAcceptanceRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.NewAcceptanceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.AcceptanceId }).OnDelete(DeleteBehavior.Restrict);
        adjustment.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        modelBuilder.Entity<PurchaseAttemptRow>().HasOne<PurchaseReceiptAdjustmentRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReceiptAdjustmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.AdjustmentId }).OnDelete(DeleteBehavior.Restrict);

        var cancellation = modelBuilder.Entity<PurchaseOrderCancellationHistoryRow>();
        cancellation.ToTable("PurchaseOrderCancellationHistory", Schema, table =>
            table.HasCheckConstraint("CK_PurchaseOrderCancellationHistory_Facts",
                "[OrderVersionAfter] >= 2 AND LEN([RecordedBy]) > 0 AND LEN([Reason]) > 0"));
        cancellation.HasKey(x => new { x.CompanyId, x.OrderId, x.OrderVersionAfter });
        cancellation.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        cancellation.Property(x => x.Reason).HasMaxLength(4000).IsRequired();
        cancellation.HasOne<PurchaseOrderRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.OrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        cancellation.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        modelBuilder.Entity<PurchaseSupplierReturnRow>().Property(x => x.UndoQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<PurchaseSupplierReturnAttemptRow>().Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<PurchaseSupplierReturnAttemptRow>().ToTable("PurchaseSupplierReturnAttempts", Schema, table =>
            table.HasCheckConstraint("CK_PurchaseSupplierReturnAttempts_ResultSnapshot",
                "[ResultJson] IS NULL OR ([Outcome] = 1 AND ISJSON([ResultJson]) = 1)"));
        modelBuilder.Entity<PurchaseSupplierReturnRow>().ToTable("PurchaseSupplierReturns", Schema, table =>
            table.HasCheckConstraint("CK_PurchaseSupplierReturns_AdjustmentMetadata",
                "([ExpectedPurchaseOrderVersion] IS NULL AND [UndoQuantity] IS NULL) OR ([ExpectedPurchaseOrderVersion] IS NOT NULL AND [ExpectedPurchaseOrderVersion] >= 1 AND [UndoQuantity] IS NOT NULL AND [UndoQuantity] > 0)"));
    }
}
