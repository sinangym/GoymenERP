using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>What kind of plan output line an output declaration is bound to (F-009 D2, URT-D30).</summary>
public enum PlanOutputLineKind
{
    /// <summary>A slit line of the plan's slitting spec, named by its 1-based position in the spec.</summary>
    SlitLine = 1,

    /// <summary>A coil-to-sheet cut line of the plan's cut-to-length spec, named by its client line id.</summary>
    CutLine = 2,

    /// <summary>
    /// The output class's own line inside the order (ASSUMPTION-URT-F009-1, reversible): a class that has no line in
    /// the plan (second quality, usable remnant, scrap, process loss) counts as one line per class. A good product of
    /// a plan that carries no output line at all binds to this line as well.
    /// </summary>
    ClassLine = 3,
}

/// <summary>
/// F-009 D2 (URT-D30, analysis §3): the plan output line an output declaration counts towards. A line is the plan's
/// output product line, not a parent roll: the correction bound of a line is derived from the net mass of the
/// declarations bound to it. A declaration stored before D2 carries no reference ("satırsız") and its correction is
/// refused fail-closed (<see cref="Common.ManufacturingRuleCodes.OutputLineMissing"/>); no line is assigned to it
/// retroactively here.
/// </summary>
public sealed record PlanOutputLineReference
{
    private PlanOutputLineReference(PlanOutputLineKind kind, int? slitLineSequence, Guid? cutLineId)
    {
        Kind = kind;
        SlitLineSequence = slitLineSequence;
        CutLineId = cutLineId;
    }

    public PlanOutputLineKind Kind { get; }

    /// <summary>The 1-based position of the slit line in the plan's slitting spec; set only for <see cref="PlanOutputLineKind.SlitLine"/>.</summary>
    public int? SlitLineSequence { get; }

    /// <summary>The cut line's id in the plan's cut-to-length spec; set only for <see cref="PlanOutputLineKind.CutLine"/>.</summary>
    public Guid? CutLineId { get; }

    /// <summary>The class's own line (<see cref="PlanOutputLineKind.ClassLine"/>).</summary>
    public static PlanOutputLineReference ClassLine { get; } = new(PlanOutputLineKind.ClassLine, null, null);

    public static PlanOutputLineReference ForSlitLine(int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        return new PlanOutputLineReference(PlanOutputLineKind.SlitLine, sequence, null);
    }

    public static PlanOutputLineReference ForCutLine(Guid lineId)
    {
        if (lineId == Guid.Empty)
        {
            throw new ArgumentException("Kesim satırı kimliği boş olamaz.", nameof(lineId));
        }

        return new PlanOutputLineReference(PlanOutputLineKind.CutLine, null, lineId);
    }

    /// <summary>
    /// Rebuilds a stored reference from its three columns; a combination the factories cannot produce is refused with
    /// <see cref="ArgumentException"/> (the store turns it into a stored-record refusal).
    /// </summary>
    public static PlanOutputLineReference FromStored(PlanOutputLineKind kind, int? slitLineSequence, Guid? cutLineId) =>
        kind switch
        {
            PlanOutputLineKind.SlitLine when slitLineSequence is { } sequence && cutLineId is null => ForSlitLine(sequence),
            PlanOutputLineKind.CutLine when cutLineId is { } lineId && slitLineSequence is null => ForCutLine(lineId),
            PlanOutputLineKind.ClassLine when slitLineSequence is null && cutLineId is null => ClassLine,
            _ => throw new ArgumentException("Kayıtlı plan çıktı satırı referansı geçerli değil.", nameof(kind)),
        };

    /// <summary>
    /// The plan's own output lines (slit lines by position, cut lines by id) as references, in plan order. A plan with
    /// neither spec has none; its outputs bind to their class line.
    /// </summary>
    public static IReadOnlyList<PlanOutputLineReference> LinesOf(SlittingSpec? slittingSpec, CutToLengthSpec? cutToLengthSpec)
    {
        var lines = new List<PlanOutputLineReference>();
        if (slittingSpec is not null)
        {
            lines.AddRange(Enumerable.Range(1, slittingSpec.Lines.Count).Select(ForSlitLine));
        }

        if (cutToLengthSpec is not null)
        {
            lines.AddRange(cutToLengthSpec.Lines.Select(line => ForCutLine(line.LineId)));
        }

        return lines.AsReadOnly();
    }
}
