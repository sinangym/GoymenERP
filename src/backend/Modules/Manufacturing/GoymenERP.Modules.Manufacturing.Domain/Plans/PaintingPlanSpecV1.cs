using System.Collections.ObjectModel;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>An opaque, versioned StockItem/Consumable reference selected for one paint layer.</summary>
public sealed record PaintingMaterialReferenceSnapshotV1
{
    public PaintingMaterialReferenceSnapshotV1(Guid materialId, string expectedMaterialVersion)
    {
        MaterialId = RequireId(materialId, nameof(materialId));
        ExpectedMaterialVersion = RequireVersion(expectedMaterialVersion, nameof(expectedMaterialVersion));
    }

    public Guid MaterialId { get; }
    public string ExpectedMaterialVersion { get; }

    private static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException("Boya malzemesi kimliği boş olamaz.", name) : value;

    private static string RequireVersion(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Boya malzemesi sürümü boş olamaz.", name)
            : value;
}

/// <summary>An opaque, source-stamped colour reference; no RAL or free text is inferred from it.</summary>
public sealed record PaintingColourReferenceSnapshotV1
{
    public PaintingColourReferenceSnapshotV1(Guid colourReferenceId, string sourceVersion)
    {
        ColourReferenceId = RequireId(colourReferenceId, nameof(colourReferenceId));
        SourceVersion = RequireVersion(sourceVersion, nameof(sourceVersion));
    }

    public Guid ColourReferenceId { get; }
    public string SourceVersion { get; }

    private static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException("Renk referansı kimliği boş olamaz.", name) : value;

    private static string RequireVersion(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Renk kaynağı sürümü boş olamaz.", name)
            : value;
}

/// <summary>The versioned painted-surface fact used by one layer.</summary>
public sealed record PaintingSurfaceSnapshotV1
{
    public PaintingSurfaceSnapshotV1(Guid sourceId, string sourceVersion, string surfaceReference, decimal? areaSquareMetres)
    {
        SourceId = sourceId == Guid.Empty
            ? throw new ArgumentException("Yüzey kaynağı kimliği boş olamaz.", nameof(sourceId))
            : sourceId;
        SourceVersion = string.IsNullOrWhiteSpace(sourceVersion)
            ? throw new ArgumentException("Yüzey kaynağı sürümü boş olamaz.", nameof(sourceVersion))
            : sourceVersion;
        SurfaceReference = string.IsNullOrWhiteSpace(surfaceReference)
            ? throw new ArgumentException("Yüzey referansı boş olamaz.", nameof(surfaceReference))
            : surfaceReference;
        AreaSquareMetres = areaSquareMetres;
    }

    public Guid SourceId { get; }
    public string SourceVersion { get; }
    public string SurfaceReference { get; }
    public decimal? AreaSquareMetres { get; }
}

/// <summary>An optional, source-stamped litre-to-kilogram conversion for the selected paint material.</summary>
public sealed record PaintingDensitySnapshotV1
{
    public PaintingDensitySnapshotV1(Guid sourceId, string sourceVersion, decimal kilogramsPerLitre)
    {
        SourceId = sourceId == Guid.Empty
            ? throw new ArgumentException("Yoğunluk kaynağı kimliği boş olamaz.", nameof(sourceId))
            : sourceId;
        SourceVersion = string.IsNullOrWhiteSpace(sourceVersion)
            ? throw new ArgumentException("Yoğunluk kaynağı sürümü boş olamaz.", nameof(sourceVersion))
            : sourceVersion;
        KilogramsPerLitre = kilogramsPerLitre;
    }

    public Guid SourceId { get; }
    public string SourceVersion { get; }
    public decimal KilogramsPerLitre { get; }
}

/// <summary>
/// One immutable source layer. Collection order is authoritative and is never derived from another field.
/// Nullable formula inputs represent an explicitly unavailable source; they are never defaulted.
/// </summary>
public sealed record PaintingLayerSnapshotV1
{
    public PaintingLayerSnapshotV1(
        Guid layerId,
        PaintingMaterialReferenceSnapshotV1? material,
        PaintingColourReferenceSnapshotV1? colour,
        PaintingSurfaceSnapshotV1? surface,
        decimal? dryFilmMicrons,
        decimal? volumetricSolidsRatio,
        decimal? transferEfficiency,
        PaintingDensitySnapshotV1? density = null)
    {
        LayerId = layerId == Guid.Empty
            ? throw new ArgumentException("Kat kimliği boş olamaz.", nameof(layerId))
            : layerId;
        Material = material;
        Colour = colour;
        Surface = surface;
        DryFilmMicrons = dryFilmMicrons;
        VolumetricSolidsRatio = volumetricSolidsRatio;
        TransferEfficiency = transferEfficiency;
        Density = density;
    }

    public Guid LayerId { get; }
    public PaintingMaterialReferenceSnapshotV1? Material { get; }
    public PaintingColourReferenceSnapshotV1? Colour { get; }
    public PaintingSurfaceSnapshotV1? Surface { get; }
    public decimal? DryFilmMicrons { get; }
    public decimal? VolumetricSolidsRatio { get; }
    public decimal? TransferEfficiency { get; }
    public PaintingDensitySnapshotV1? Density { get; }
}

/// <summary>
/// D-M016=A immutable input for the pure GDC-PNT/2 calculator. The recipe and ordered layers are
/// historical plan facts. No current Product, colour, recipe or Inventory record is read through this contract.
/// </summary>
public sealed record PaintingPlanSpecV1
{
    public PaintingPlanSpecV1(
        Guid recipeId,
        string recipeVersion,
        IEnumerable<PaintingLayerSnapshotV1> orderedLayers,
        decimal? plannedCleaningLitres)
    {
        RecipeId = recipeId == Guid.Empty
            ? throw new ArgumentException("Boya reçetesi kimliği boş olamaz.", nameof(recipeId))
            : recipeId;
        RecipeVersion = string.IsNullOrWhiteSpace(recipeVersion)
            ? throw new ArgumentException("Boya reçetesi sürümü boş olamaz.", nameof(recipeVersion))
            : recipeVersion;

        ArgumentNullException.ThrowIfNull(orderedLayers);
        var copiedLayers = orderedLayers.ToArray();
        if (copiedLayers.Any(layer => layer is null))
        {
            throw new ArgumentException("Kat listesi null öğe içeremez.", nameof(orderedLayers));
        }

        if (copiedLayers.Select(layer => layer.LayerId).Distinct().Count() != copiedLayers.Length)
        {
            throw new ArgumentException("Kat kimlikleri tekil olmalıdır.", nameof(orderedLayers));
        }

        OrderedLayers = new ReadOnlyCollection<PaintingLayerSnapshotV1>(copiedLayers);
        PlannedCleaningLitres = plannedCleaningLitres;
    }

    public Guid RecipeId { get; }
    public string RecipeVersion { get; }
    public IReadOnlyList<PaintingLayerSnapshotV1> OrderedLayers { get; }
    public decimal? PlannedCleaningLitres { get; }
}
