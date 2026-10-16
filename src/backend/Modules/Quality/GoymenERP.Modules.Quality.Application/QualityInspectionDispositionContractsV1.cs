namespace GoymenERP.Modules.Quality.Application;

/// <summary>
/// The disposition values owned by Quality. A disposition is a decision fact only: it is not an
/// Inventory movement, document presence, transfer acceptance, permission grant or production activation.
/// </summary>
public static class QualityDispositionKindsV1
{
    public const string Release = "Release";
    public const string Reject = "Reject";

    public static bool IsKnown(string value) =>
        string.Equals(value, Release, StringComparison.Ordinal)
        || string.Equals(value, Reject, StringComparison.Ordinal);
}

/// <summary>
/// The immutable owner binding that a later Quality runtime must validate before asking Inventory
/// to close its own Quality hold. Evidence references remain evidence and never imply a release.
/// </summary>
public sealed class QualityInspectionDispositionV1
{
    public const int MaximumActorLength = 128;
    public const int MaximumReasonLength = 1000;
    public const int MaximumEvidenceVersionLength = 128;

    public QualityInspectionDispositionV1(
        Guid dispositionId,
        Guid companyId,
        Guid inspectionId,
        Guid labelId,
        string expectedLabelVersion,
        Guid qualityHoldId,
        string expectedHoldVersion,
        string preparedByActorId,
        string decidedByActorId,
        string kind,
        string reason,
        string? evidenceVersion)
    {
        DispositionId = Required(dispositionId, nameof(dispositionId));
        CompanyId = Required(companyId, nameof(companyId));
        InspectionId = Required(inspectionId, nameof(inspectionId));
        LabelId = Required(labelId, nameof(labelId));
        ExpectedLabelVersion = RequiredVersion(expectedLabelVersion, nameof(expectedLabelVersion));
        QualityHoldId = Required(qualityHoldId, nameof(qualityHoldId));
        ExpectedHoldVersion = RequiredVersion(expectedHoldVersion, nameof(expectedHoldVersion));
        PreparedByActorId = RequiredText(preparedByActorId, MaximumActorLength, nameof(preparedByActorId));
        DecidedByActorId = RequiredText(decidedByActorId, MaximumActorLength, nameof(decidedByActorId));
        if (string.Equals(PreparedByActorId, DecidedByActorId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The inspection preparer cannot decide the same disposition.",
                nameof(decidedByActorId));
        }

        Kind = QualityDispositionKindsV1.IsKnown(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), "The disposition kind is not supported.");
        Reason = RequiredText(reason, MaximumReasonLength, nameof(reason));
        EvidenceVersion = OptionalText(evidenceVersion, MaximumEvidenceVersionLength, nameof(evidenceVersion));
    }

    public Guid DispositionId { get; }
    public Guid CompanyId { get; }
    public Guid InspectionId { get; }
    public Guid LabelId { get; }
    public string ExpectedLabelVersion { get; }
    public Guid QualityHoldId { get; }
    public string ExpectedHoldVersion { get; }
    public string PreparedByActorId { get; }
    public string DecidedByActorId { get; }
    public string Kind { get; }
    public string Reason { get; }
    public string? EvidenceVersion { get; }

    private static Guid Required(Guid value, string parameterName) =>
        value != Guid.Empty ? value : throw new ArgumentException("A non-empty identity is required.", parameterName);

    private static string RequiredVersion(string value, string parameterName) =>
        RequiredText(value, MaximumEvidenceVersionLength, parameterName);

    private static string RequiredText(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentOutOfRangeException(parameterName, $"The value cannot exceed {maximumLength} characters.");
    }

    private static string? OptionalText(string? value, int maximumLength, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        return RequiredText(value, maximumLength, parameterName);
    }
}
