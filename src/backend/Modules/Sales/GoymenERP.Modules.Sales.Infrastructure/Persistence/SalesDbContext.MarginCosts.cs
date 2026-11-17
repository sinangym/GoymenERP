using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

internal sealed class DraftMarginCostRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DraftId { get; set; }
    public Guid LineId { get; set; }
    public int LineNo { get; set; }
    public int Revision { get; set; }
    public Guid ProductId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; }
    public Guid? CurrencyId { get; set; }
    public decimal? CostPerUnit { get; set; }
    public int? UnavailableReason { get; set; }
    public string SourceProof { get; set; } = null!;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class SalesDbContext
{
    internal const string MarginCostIdentityConstraint = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND [LineNo] >= 1 AND [Quantity] > 0";
    internal const string MarginCostValueConstraint = "([UnavailableReason] IS NULL AND [CurrencyId] IS NOT NULL AND [CurrencyId] <> '00000000-0000-0000-0000-000000000000' AND [CostPerUnit] IS NOT NULL AND [CostPerUnit] >= 0) OR ([UnavailableReason] IS NOT NULL AND [UnavailableReason] IN (1,2,3,5,6,7,8,9,10,11,12,13,14) AND [CurrencyId] IS NULL AND [CostPerUnit] IS NULL)";
    internal const string MarginCostProofConstraint = "ISJSON([SourceProof]) = 1 AND LEN([SourceProof]) BETWEEN 2 AND 65536";

    internal DbSet<DraftMarginCostRow> DraftMarginCosts => Set<DraftMarginCostRow>();

    private void ConfigureMarginCosts(ModelBuilder modelBuilder)
    {
        var cost = modelBuilder.Entity<DraftMarginCostRow>();
        cost.ToTable("DraftMarginCosts", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftMarginCosts_Identity", MarginCostIdentityConstraint);
            table.HasCheckConstraint("CK_DraftMarginCosts_Value", MarginCostValueConstraint);
            table.HasCheckConstraint("CK_DraftMarginCosts_Proof", MarginCostProofConstraint);
        });
        cost.HasKey(item => new { item.CompanyId, item.DraftId, item.LineId, item.Revision });
        cost.Property(item => item.Quantity).HasPrecision(18, 3);
        cost.Property(item => item.CostPerUnit).HasPrecision(19, 4);
        cost.Property(item => item.SourceProof).IsRequired();
        cost.HasOne<DraftLineRow>().WithMany()
            .HasForeignKey(item => new { item.CompanyId, item.DraftId, item.LineNo, item.LineId })
            .HasPrincipalKey(item => new { item.CompanyId, item.DraftId, item.LineNo, item.LineId })
            .OnDelete(DeleteBehavior.Restrict);
        cost.HasQueryFilter(item => item.CompanyId == companyContext.CompanyId);
    }
}

public sealed partial class SalesStore
{
    private void AppendMarginCosts(SalesOrderDraft draft, int previousCount)
    {
        foreach (var cost in draft.MarginCosts.Skip(previousCount))
            dbContext.DraftMarginCosts.Add(new DraftMarginCostRow
            {
                CompanyId = draft.CompanyId, DraftId = draft.Id, LineId = cost.LineId,
                LineNo = draft.Lines.Single(item => item.Id == cost.LineId).LineNo,
                Revision = cost.Revision, ProductId = cost.ProductId, UnitId = cost.UnitId, Quantity = cost.Quantity,
                CurrencyId = cost.CurrencyId, CostPerUnit = cost.CostPerUnit,
                UnavailableReason = cost.UnavailableReason is { } reason ? (int)reason : null,
                SourceProof = cost.SourceProof, RecordedAtUtc = cost.RecordedAtUtc,
            });
    }
}
