namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D437 (AK-4, <c>docs/delivery/ADET_KG_PAKETI_v1.md</c>): the theoretical kilograms per order unit the Product owner
/// answered for a non-kilogram (piece) line at the internal approval of <see cref="Revision"/>. Append-only: it is written once
/// per (line, revision), kept with the document and never re-read — a later card edit does not move it (KIM-D47). A revision
/// re-reads only a line whose product or unit changed (the SAT-D269/D271 re-pin rule); an unchanged line keeps its figure.
/// <see cref="SourceProof"/> is the owner's evidence (card/formula/dimension/weight-table/policy/profile versions) as JSON.
/// </summary>
public sealed record SalesLineKilogramsPerUnitSnapshot(
    Guid LineId, int Revision, Guid ProductId, Guid UnitId, decimal KilogramsPerUnit, string SourceProof,
    DateTimeOffset RecordedAtUtc)
{
    /// <summary>The stored precision (the column's scale): the Settings coefficient decimals are at most this many.</summary>
    public const int KilogramsPerUnitScale = 6;

    /// <summary>The column's largest value (decimal(18,6)).</summary>
    public const decimal MaximumKilogramsPerUnit = 999999999999.999999m;

    public const int MaximumSourceProofLength = 65536;

    public void Validate()
    {
        if (LineId == Guid.Empty || ProductId == Guid.Empty || UnitId == Guid.Empty || Revision < 0
            || KilogramsPerUnit <= 0m || KilogramsPerUnit > MaximumKilogramsPerUnit
            || decimal.Round(KilogramsPerUnit, KilogramsPerUnitScale, MidpointRounding.AwayFromZero) != KilogramsPerUnit
            || string.IsNullOrWhiteSpace(SourceProof) || SourceProof.Length > MaximumSourceProofLength
            || RecordedAtUtc == default || RecordedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Teorik kg/birim anlık görüntüsü geçersiz.");
    }
}

public sealed partial class SalesOrderDraft
{
    private readonly List<SalesLineKilogramsPerUnitSnapshot> kilogramsPerUnit = [];

    /// <summary>SAT-D437: every recorded theoretical kg/unit snapshot of the order's lines, in recording order.</summary>
    public IReadOnlyList<SalesLineKilogramsPerUnitSnapshot> KilogramsPerUnitSnapshots => kilogramsPerUnit.AsReadOnly();

    /// <summary>
    /// SAT-D437: the figure the line carries now — the latest snapshot at or before the current revision that still names the
    /// line's product and unit. Null when none was recorded (a draft not yet approved, a kg line, a line whose product or unit
    /// changed since, or an order approved before AK-4: never back-filled).
    /// </summary>
    public SalesLineKilogramsPerUnitSnapshot? KilogramsPerUnitOf(Guid lineId)
    {
        var line = lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (line is null) return null;
        return kilogramsPerUnit
            .Where(item => item.LineId == lineId && item.Revision <= Revision)
            .OrderByDescending(item => item.Revision)
            .FirstOrDefault() is { } latest
            && latest.ProductId == line.Product.ProductId && latest.UnitId == line.OrderUnit.UnitId
            ? latest
            : null;
    }

    /// <summary>
    /// SAT-D437: whether the line needs its own figure at an approval — a non-kilogram order unit and no snapshot that still
    /// names its product and unit. A kilogram line never does (its theoretical kg is its quantity).
    /// </summary>
    public bool NeedsKilogramsPerUnit(SalesOrderDraftLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return !line.OrderUnit.IsKilogram && KilogramsPerUnitOf(line.Id) is null;
    }

    /// <summary>SAT-D437: pins the figures read at the internal approval of the current revision (same save as the approval).</summary>
    public void PinKilogramsPerUnit(IReadOnlyList<SalesLineKilogramsPerUnitSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (ApprovalState != InternalApprovalState.Approved)
            throw new InvalidOperationException("Teorik kg/birim yalnız iç onayla sabitlenir.");
        if (snapshots.Select(item => item.LineId).Distinct().Count() != snapshots.Count)
            throw new InvalidOperationException("Aynı satırın teorik kg/birim değeri bir onayda iki kez sabitlenemez.");
        foreach (var snapshot in snapshots)
        {
            snapshot.Validate();
            var line = Lines.SingleOrDefault(candidate => candidate.Id == snapshot.LineId);
            if (line is null || line.OrderUnit.IsKilogram || snapshot.Revision != Revision
                || snapshot.ProductId != line.Product.ProductId || snapshot.UnitId != line.OrderUnit.UnitId
                || snapshot.RecordedAtUtc != Approval.DecidedAtUtc
                || kilogramsPerUnit.Any(item => item.LineId == snapshot.LineId && item.Revision == snapshot.Revision))
                throw new InvalidOperationException("Teorik kg/birim değeri onaylanan satırla uyuşmuyor.");
        }

        kilogramsPerUnit.AddRange(snapshots);
    }

    /// <summary>Restores a stored snapshot (store rebuild only); history is never changed.</summary>
    public void RestoreKilogramsPerUnit(SalesLineKilogramsPerUnitSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        if (snapshot.Revision > Revision
            || kilogramsPerUnit.Any(item => item.LineId == snapshot.LineId && item.Revision == snapshot.Revision))
            throw new InvalidOperationException("Teorik kg/birim geçmişi değiştirilemez.");
        kilogramsPerUnit.Add(snapshot);
    }
}
