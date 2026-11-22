using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>SAT-D437 (AK-4): one theoretical kg/unit snapshot of a piece line, pinned at an internal approval; append-only.</summary>
internal sealed class DraftLineKilogramsPerUnitRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DraftId { get; set; }
    public Guid LineId { get; set; }
    public int LineNo { get; set; }
    public int Revision { get; set; }
    public Guid ProductId { get; set; }
    public Guid UnitId { get; set; }
    public decimal KilogramsPerUnit { get; set; }
    public string SourceProof { get; set; } = null!;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

public sealed partial class SalesDbContext
{
    internal const string KilogramsPerUnitIdentityConstraint = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [DraftId] <> '00000000-0000-0000-0000-000000000000' AND [LineId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [UnitId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] >= 0 AND [LineNo] >= 1";
    internal const string KilogramsPerUnitValueConstraint = "[KilogramsPerUnit] > 0";
    internal const string KilogramsPerUnitProofConstraint = "ISJSON([SourceProof]) = 1 AND LEN([SourceProof]) BETWEEN 2 AND 65536";

    internal DbSet<DraftLineKilogramsPerUnitRow> DraftLineKilogramsPerUnit => Set<DraftLineKilogramsPerUnitRow>();

    private void ConfigureKilogramsPerUnit(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DraftLineKilogramsPerUnitRow>();
        entity.ToTable("DraftLineKilogramsPerUnit", Schema, table =>
        {
            table.HasCheckConstraint("CK_DraftLineKilogramsPerUnit_Identity", KilogramsPerUnitIdentityConstraint);
            table.HasCheckConstraint("CK_DraftLineKilogramsPerUnit_Value", KilogramsPerUnitValueConstraint);
            table.HasCheckConstraint("CK_DraftLineKilogramsPerUnit_Proof", KilogramsPerUnitProofConstraint);
        });
        entity.HasKey(item => new { item.CompanyId, item.DraftId, item.LineId, item.Revision });
        entity.Property(item => item.KilogramsPerUnit).HasPrecision(18, SalesLineKilogramsPerUnitSnapshot.KilogramsPerUnitScale);
        entity.Property(item => item.SourceProof).IsRequired();
        entity.HasOne<DraftLineRow>().WithMany()
            .HasForeignKey(item => new { item.CompanyId, item.DraftId, item.LineNo, item.LineId })
            .HasPrincipalKey(item => new { item.CompanyId, item.DraftId, item.LineNo, item.LineId })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(item => item.CompanyId == companyContext.CompanyId);
    }
}

public sealed partial class SalesStore
{
    private void AppendKilogramsPerUnit(SalesOrderDraft draft, int previousCount)
    {
        foreach (var snapshot in draft.KilogramsPerUnitSnapshots.Skip(previousCount))
            dbContext.DraftLineKilogramsPerUnit.Add(new DraftLineKilogramsPerUnitRow
            {
                CompanyId = draft.CompanyId, DraftId = draft.Id, LineId = snapshot.LineId,
                LineNo = draft.Lines.Single(item => item.Id == snapshot.LineId).LineNo,
                Revision = snapshot.Revision, ProductId = snapshot.ProductId, UnitId = snapshot.UnitId,
                KilogramsPerUnit = snapshot.KilogramsPerUnit, SourceProof = snapshot.SourceProof,
                RecordedAtUtc = snapshot.RecordedAtUtc,
            });
    }

    private static void RestoreKilogramsPerUnit(SalesOrderDraft draft, IEnumerable<DraftLineKilogramsPerUnitRow> rows)
    {
        foreach (var row in rows.Where(item => item.DraftId == draft.Id).OrderBy(item => item.Revision).ThenBy(item => item.LineNo))
            draft.RestoreKilogramsPerUnit(new SalesLineKilogramsPerUnitSnapshot(row.LineId, row.Revision, row.ProductId, row.UnitId,
                row.KilogramsPerUnit, row.SourceProof, row.RecordedAtUtc));
    }
}
