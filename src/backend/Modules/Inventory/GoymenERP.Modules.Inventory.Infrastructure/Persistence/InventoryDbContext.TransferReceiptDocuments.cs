using Microsoft.EntityFrameworkCore;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryDbContext
{
    internal const string ReceiptDocumentStateCheck =
        "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND [TransferId] <> '00000000-0000-0000-0000-000000000000' AND [Version] IN (1,2) AND [SourceTransferVersion] > 0 AND [Status] = [Version] "
        + "AND (([Version]=1 AND [PreviousVersion] IS NULL) OR ([Version]=2 AND [PreviousVersion] IS NOT NULL AND [PreviousVersion]=1)) "
        + "AND ISJSON([LinesJson]) = 1 AND JSON_QUERY([LinesJson],'$[0]') IS NOT NULL "
        + "AND DATEPART(TZOFFSET,[ReceivedAtUtc]) = 0 AND DATEPART(TZOFFSET,[RecordedAtUtc]) = 0 "
        + "AND LEN(LTRIM(RTRIM([RecordedBy]))) > 0 "
        + "AND (([Version]=1 AND [FinalizationReason] IS NULL) OR ([Version]=2 AND [FinalizationReason] IS NOT NULL AND LEN(LTRIM(RTRIM([FinalizationReason])))>0))";
    internal DbSet<InventoryTransferReceiptDocumentVersionRow> TransferReceiptDocumentVersions => Set<InventoryTransferReceiptDocumentVersionRow>();
    private void ConfigureTransferReceiptDocuments(ModelBuilder model)
    {
        var row = model.Entity<InventoryTransferReceiptDocumentVersionRow>();
        row.ToTable("TransferReceiptDocumentVersions", Schema, table => table.HasCheckConstraint("CK_TransferReceiptDocumentVersions_State", ReceiptDocumentStateCheck));
        row.HasKey(x => new { x.CompanyId, x.DocumentId, x.Version });
        row.Property(x => x.LinesJson).HasMaxLength(64000).IsRequired();
        row.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        row.Property(x => x.FinalizationReason).HasMaxLength(InventoryTransferReceiptApiV1.MaximumTextLength);
        row.HasOne<InventoryTransferRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TransferId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<InventoryTransferReceiptDocumentVersionRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DocumentId, x.PreviousVersion })
            .HasPrincipalKey(x => new { x.CompanyId, x.DocumentId, x.Version }).OnDelete(DeleteBehavior.Restrict);
        row.HasIndex(x => new { x.CompanyId, x.TransferId, x.DocumentId, x.Version });
        row.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}

/// <summary>Append-only draft/effective document history. The existing tenant guard rejects update and delete.</summary>
internal sealed class InventoryTransferReceiptDocumentVersionRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public long Version { get; set; }
    public long? PreviousVersion { get; set; }
    public Guid TransferId { get; set; }
    public long SourceTransferVersion { get; set; }
    public int Status { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string LinesJson { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string? FinalizationReason { get; set; }
}
