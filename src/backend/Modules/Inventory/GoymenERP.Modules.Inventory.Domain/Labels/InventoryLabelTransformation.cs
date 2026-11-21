using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Domain.Labels;

public enum InventoryLabelTransformationKind
{
    Split = 1,
    Merge = 2,

    /// <summary>
    /// B-03 (DEC-PRDCT-003/007): a Manufacturing output label descends from the input labels issued to the same
    /// manufacturing order. Only the Manufacturing realization participant records it; the repack workflow here
    /// refuses it, because a production edge carries raw kilograms without the split/merge mass balance.
    /// </summary>
    Manufacturing = 3,
}

/// <summary>A whole source label. <paramref name="Pieces"/> is its stored piece count (KLN-D119), null when it tracks none.</summary>
public sealed record InventoryLabelTransformationSource(
    MaterialLabel Label,
    StockPosition Position,
    Kilograms Quantity,
    long ExpectedVersion,
    PieceCount? Pieces = null);

/// <summary>A new output label. <paramref name="Pieces"/> is its piece count when the sources track pieces (KLN-D119).</summary>
public sealed record InventoryLabelTransformationOutput(
    MaterialLabel Label,
    StockPosition Position,
    Kilograms Quantity,
    PieceCount? Pieces = null);

/// <summary>
/// One same-company physical repack event (DEC-LBL-002/003, AT-038). A split consumes one whole
/// source label into at least two new labels; a merge consumes at least two whole source labels
/// into one new label. Cross-company transfer, cross-lot mixing and product conversion are separate
/// workflows and are deliberately refused here. Every output carries the production status its sources
/// pass on (KLN-D46, <see cref="InheritedProductionStatus"/>).
/// </summary>
public sealed class InventoryLabelTransformation
{
    private readonly InventoryLabelTransformationSource[] sources;
    private readonly InventoryLabelTransformationOutput[] outputs;

    public InventoryLabelTransformation(
        Guid id,
        Guid companyId,
        InventoryLabelTransformationKind kind,
        DateTimeOffset occurredAtUtc,
        IEnumerable<InventoryLabelTransformationSource> sources,
        IEnumerable<InventoryLabelTransformationOutput> outputs,
        string reason)
    {
        Id = RequireId(id, "Dönüşüm");
        CompanyId = RequireId(companyId, "Şirket");
        Kind = Enum.IsDefined(kind) && kind != InventoryLabelTransformationKind.Manufacturing
            ? kind
            : throw Invalid("Etiket dönüşüm türü tanımsız.");
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("Etiket dönüşüm zamanı UTC olmalıdır.");
        }

        OccurredAtUtc = occurredAtUtc;
        Reason = string.IsNullOrWhiteSpace(reason)
            ? throw Invalid("Etiket dönüşüm gerekçesi zorunludur.")
            : reason.Trim();
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(outputs);
        this.sources = [.. sources];
        this.outputs = [.. outputs];
        Validate();
    }

    public Guid Id { get; }
    public Guid CompanyId { get; }
    public InventoryLabelTransformationKind Kind { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public string Reason { get; }
    public IReadOnlyList<InventoryLabelTransformationSource> Sources => Array.AsReadOnly(sources);
    public IReadOnlyList<InventoryLabelTransformationOutput> Outputs => Array.AsReadOnly(outputs);

    /// <summary>
    /// KLN-D46, confirmed in force by KLN-D63 and KLN-D86 (owner decisions of 2026-11-15; KLN-D86 voids the SC21 "open" wording): the production status (BS-5) every
    /// output label of this repack carries. See <see cref="InheritedProductionStatus(IEnumerable{MaterialLabel})"/>.
    /// </summary>
    public LabelProductionStatus? OutputProductionStatus => InheritedProductionStatus(sources.Select(source => source.Label));

    /// <summary>
    /// KLN-D46, confirmed in force by KLN-D63 and KLN-D86 (owner decisions of 2026-11-15; KLN-D86 voids the SC21 "open" wording): the production status a split or
    /// merge output inherits from its sources. A split has one source, so every child inherits that source's
    /// <see cref="LabelProductionStatus.UsableRemnant"/>; a merge result is
    /// <see cref="LabelProductionStatus.UsableRemnant"/> only when EVERY source is; any source without it (mixed
    /// sources) leaves the output's production status null. The classification (Standard/SecondQuality, BS-14,
    /// URT-D43) is not touched by this rule. Only <see cref="LabelProductionStatus.UsableRemnant"/> is named by the
    /// decision, so no other production status is inherited by assumption.
    /// </summary>
    public static LabelProductionStatus? InheritedProductionStatus(IEnumerable<MaterialLabel> sourceLabels)
    {
        ArgumentNullException.ThrowIfNull(sourceLabels);
        var labels = sourceLabels.ToArray();
        if (labels.Length == 0 || labels.Any(label => label is null))
        {
            throw Invalid("Etiket dönüşümünde üretim durumu mirası için kaynak etiket zorunludur.");
        }

        return labels.All(label => label.ProductionStatus == LabelProductionStatus.UsableRemnant)
            ? LabelProductionStatus.UsableRemnant
            : null;
    }

    private void Validate()
    {
        if (Kind == InventoryLabelTransformationKind.Split && (sources.Length != 1 || outputs.Length < 2))
        {
            throw Invalid("Bölme bir kaynak etiketi en az iki yeni etikete dönüştürmelidir.");
        }

        if (Kind == InventoryLabelTransformationKind.Merge && (sources.Length < 2 || outputs.Length != 1))
        {
            throw Invalid("Birleştirme en az iki kaynak etiketi tek yeni etikete dönüştürmelidir.");
        }

        if (sources.Any(source => source is null || source.Label is null || source.Position is null)
            || outputs.Any(output => output is null || output.Label is null || output.Position is null))
        {
            throw Invalid("Etiket dönüşüm satırı eksik olamaz.");
        }

        var sourceIds = sources.Select(source => source.Label.Id).ToArray();
        var outputIds = outputs.Select(output => output.Label.Id).ToArray();
        if (sourceIds.Distinct().Count() != sourceIds.Length
            || outputIds.Distinct().Count() != outputIds.Length
            || sourceIds.Intersect(outputIds).Any())
        {
            throw Invalid("Kaynak ve çıktı etiket kimlikleri benzersiz ve birbirinden farklı olmalıdır.");
        }

        var baseline = sources[0];
        if (baseline.ExpectedVersion < 1)
        {
            throw Invalid("Kaynak etiket sürümü pozitif olmalıdır.");
        }

        foreach (var source in sources)
        {
            RequireSourceMatches(source, baseline);
        }

        foreach (var output in outputs)
        {
            RequireOutputMatches(output, baseline);
        }

        var sourceTotal = sources.Aggregate(Kilograms.Zero, (sum, source) => sum + source.Quantity);
        var outputTotal = outputs.Aggregate(Kilograms.Zero, (sum, output) => sum + output.Quantity);
        if (sourceTotal != outputTotal)
        {
            throw Invalid("Etiket dönüşümünde kaynak ve çıktı kilogram toplamları eşit olmalıdır.");
        }

        RequirePiecesConserved();
    }

    /// <summary>
    /// KLN-D119 (AK-2): piece-tracking sources give piece-tracking outputs whose pieces add up to the sources' pieces, each
    /// output holding at least one piece; piece-less sources give piece-less outputs (no backfill); a mix is refused.
    /// </summary>
    private void RequirePiecesConserved()
    {
        var trackedSources = sources.Count(source => source.Pieces is not null);
        if (trackedSources != 0 && trackedSources != sources.Length)
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                "Adetli ve adetsiz etiketler birlikte birleştirilemez.");
        }

        if (trackedSources == 0)
        {
            if (outputs.Any(output => output.Pieces is not null))
            {
                throw new InventoryRuleException(InventoryRuleCodes.PieceCountNotTracked,
                    "Adetsiz kaynaktan çıkan etikete adet verilmez.");
            }

            return;
        }

        if (sources.Any(source => source.Pieces!.Value.Value <= 0))
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount, "Adetli kaynak etiketin adedi pozitif olmalıdır.");
        }

        if (outputs.Any(output => output.Pieces is not { Value: > 0 }))
        {
            throw new InventoryRuleException(InventoryRuleCodes.PieceCountRequired,
                "Adetli kaynaktan çıkan her etiket en az bir adet taşır.");
        }

        var sourcePieces = sources.Aggregate(PieceCount.Zero, (sum, source) => sum + source.Pieces!.Value);
        var outputPieces = outputs.Aggregate(PieceCount.Zero, (sum, output) => sum + output.Pieces!.Value);
        if (sourcePieces != outputPieces)
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidPieceCount,
                "Etiket dönüşümünde çıktı adetleri toplamı kaynak adedine eşit olmalıdır.");
        }
    }

    private void RequireSourceMatches(
        InventoryLabelTransformationSource source,
        InventoryLabelTransformationSource baseline)
    {
        if (source.ExpectedVersion < 1 || source.Quantity.Value <= 0m)
        {
            throw Invalid("Kaynak etiket sürümü ve miktarı pozitif olmalıdır.");
        }

        if (source.Label.CompanyId != CompanyId || source.Position.CompanyId != CompanyId
            || source.Position.LabelId != source.Label.Id
            || source.Position.ProductId != source.Label.ProductId)
        {
            throw Invalid("Kaynak etiket ve pozisyon aynı işlem şirketinde ve aynı kimlikte olmalıdır.");
        }

        if (!SameMaterial(source.Label, baseline.Label)
            || !SamePhysicalPosition(source.Position, baseline.Position, compareLabel: false))
        {
            throw Invalid("Kaynak etiketler aynı ürün, lot, iz bilgisi ve fiziksel pozisyonda olmalıdır.");
        }
    }

    private void RequireOutputMatches(
        InventoryLabelTransformationOutput output,
        InventoryLabelTransformationSource baseline)
    {
        if (output.Quantity.Value <= 0m || output.Label.CompanyId != CompanyId
            || output.Position.CompanyId != CompanyId || output.Position.LabelId != output.Label.Id
            || output.Position.ProductId != output.Label.ProductId)
        {
            throw Invalid("Çıktı etiketi pozitif miktarla aynı işlem şirketindeki kendi pozisyonunda olmalıdır.");
        }

        if (!SameMaterial(output.Label, baseline.Label)
            || !SamePhysicalPosition(output.Position, baseline.Position, compareLabel: false))
        {
            throw Invalid("Çıktı etiketi kaynakların ürün, lot, iz bilgisi ve fiziksel pozisyonunu korumalıdır.");
        }

        if (output.Label.Prints.Count != 0 || output.Label.DocumentAttachments.Count != 0)
        {
            throw Invalid("Yeni çıktı etiketi eski baskı veya belge geçmişini kopyalayamaz.");
        }

        if (output.Label.ProductionStatus != OutputProductionStatus)
        {
            throw Invalid("Çıktı etiketinin üretim durumu kaynaklardan miras alınan durumla aynı olmalıdır.");
        }
    }

    private static bool SameMaterial(MaterialLabel left, MaterialLabel right) =>
        left.ProductId == right.ProductId
        && left.LotId == right.LotId
        && string.Equals(left.HeatNumber, right.HeatNumber, StringComparison.Ordinal)
        && string.Equals(left.CoilNumber, right.CoilNumber, StringComparison.Ordinal)
        && left.RequiredDocuments.SetEquals(right.RequiredDocuments);

    private static bool SamePhysicalPosition(StockPosition left, StockPosition right, bool compareLabel = true) =>
        left.CompanyId == right.CompanyId
        && left.Owner == right.Owner
        && left.Custodian == right.Custodian
        && left.LocationId == right.LocationId
        && left.ProductId == right.ProductId
        && left.Block == right.Block
        && (!compareLabel || left.LabelId == right.LabelId);

    private static Guid RequireId(Guid id, string what) =>
        id == Guid.Empty ? throw Invalid($"{what} kimliği zorunludur.") : id;

    private static InventoryRuleException Invalid(string message) =>
        new(InventoryRuleCodes.InvalidLabelTransformation, message);
}
