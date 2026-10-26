namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// The versioned package standard selected for a tube/profile plan. It is plan provenance only:
/// calculating a suggestion from it creates no physical package or label.
/// </summary>
public sealed record TubeProfilePackageStandardSnapshotV1
{
    public TubeProfilePackageStandardSnapshotV1(Guid sourceId, string sourceVersion, int piecesPerPackage)
    {
        SourceId = RequireId(sourceId, nameof(sourceId));
        SourceVersion = RequireVersion(sourceVersion, nameof(sourceVersion));
        if (piecesPerPackage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(piecesPerPackage), piecesPerPackage,
                "Paket içi adet pozitif olmalıdır.");
        }

        PiecesPerPackage = piecesPerPackage;
    }

    public Guid SourceId { get; }
    public string SourceVersion { get; }
    public int PiecesPerPackage { get; }

    private static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException("Paket standardı kaynak kimliği boş olamaz.", name) : value;

    private static string RequireVersion(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Paket standardı kaynak sürümü boş olamaz.", name)
            : value;
}

/// <summary>The versioned line-speed fact selected for a tube/profile plan.</summary>
public sealed record TubeProfileLineSpeedSnapshotV1
{
    public TubeProfileLineSpeedSnapshotV1(Guid sourceId, string sourceVersion, decimal metresPerMinute)
    {
        SourceId = RequireId(sourceId, nameof(sourceId));
        SourceVersion = RequireVersion(sourceVersion, nameof(sourceVersion));
        if (metresPerMinute <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(metresPerMinute), metresPerMinute,
                "Hat hızı pozitif olmalıdır.");
        }

        MetresPerMinute = metresPerMinute;
    }

    public Guid SourceId { get; }
    public string SourceVersion { get; }
    public decimal MetresPerMinute { get; }

    private static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException("Hat hızı kaynak kimliği boş olamaz.", name) : value;

    private static string RequireVersion(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Hat hızı kaynak sürümü boş olamaz.", name)
            : value;
}

/// <summary>
/// The owner-verified unit kilograms per piece selected for a tube/profile plan. Manufacturing keeps
/// the supplied value exactly; its precision policy belongs to the snapshot owner.
/// </summary>
public sealed record TubeProfileVerifiedUnitKgSnapshotV1
{
    public TubeProfileVerifiedUnitKgSnapshotV1(Guid sourceId, string sourceVersion, decimal unitKg)
    {
        SourceId = RequireId(sourceId, nameof(sourceId));
        SourceVersion = RequireVersion(sourceVersion, nameof(sourceVersion));
        if (unitKg <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitKg), unitKg,
                "Doğrulanmış birim kg pozitif olmalıdır.");
        }

        UnitKg = unitKg;
    }

    public Guid SourceId { get; }
    public string SourceVersion { get; }
    public decimal UnitKg { get; }

    private static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException("Birim kg kaynak kimliği boş olamaz.", name) : value;

    private static string RequireVersion(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Birim kg kaynak sürümü boş olamaz.", name)
            : value;
}

/// <summary>
/// Immutable tube/profile plan intent for D-M015=A. Target pieces and length are authoritative.
/// Package grouping, duration and theoretical mass use three independent, optional owner snapshots;
/// no Product geometry, density, tolerance, machine or default is embedded here.
/// </summary>
public sealed record TubeProfilePlanSpecV1
{
    public const int MillimetreScale = 3;

    public TubeProfilePlanSpecV1(
        Guid outputProductId,
        string expectedProductVersion,
        decimal targetLengthMm,
        int targetPieceCount,
        TubeProfilePackageStandardSnapshotV1? packageStandardSnapshot = null,
        TubeProfileLineSpeedSnapshotV1? lineSpeedSnapshot = null,
        TubeProfileVerifiedUnitKgSnapshotV1? verifiedUnitKgSnapshot = null)
    {
        if (outputProductId == Guid.Empty)
        {
            throw new ArgumentException("Çıktı ürün kimliği boş olamaz.", nameof(outputProductId));
        }

        if (string.IsNullOrWhiteSpace(expectedProductVersion))
        {
            throw new ArgumentException("Beklenen ürün sürümü boş olamaz.", nameof(expectedProductVersion));
        }

        if (targetLengthMm <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLengthMm), targetLengthMm,
                "Hedef boy pozitif olmalıdır.");
        }

        if (decimal.Round(targetLengthMm, MillimetreScale, MidpointRounding.ToZero) != targetLengthMm)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLengthMm), targetLengthMm,
                $"Hedef boy en fazla {MillimetreScale} ondalık taşımalıdır.");
        }

        if (targetPieceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetPieceCount), targetPieceCount,
                "Hedef adet pozitif olmalıdır.");
        }

        OutputProductId = outputProductId;
        ExpectedProductVersion = expectedProductVersion;
        TargetLengthMm = targetLengthMm;
        TargetPieceCount = targetPieceCount;
        PackageStandardSnapshot = packageStandardSnapshot;
        LineSpeedSnapshot = lineSpeedSnapshot;
        VerifiedUnitKgSnapshot = verifiedUnitKgSnapshot;
    }

    public Guid OutputProductId { get; }
    public string ExpectedProductVersion { get; }
    public decimal TargetLengthMm { get; }
    public int TargetPieceCount { get; }
    public TubeProfilePackageStandardSnapshotV1? PackageStandardSnapshot { get; }
    public TubeProfileLineSpeedSnapshotV1? LineSpeedSnapshot { get; }
    public TubeProfileVerifiedUnitKgSnapshotV1? VerifiedUnitKgSnapshot { get; }
}
