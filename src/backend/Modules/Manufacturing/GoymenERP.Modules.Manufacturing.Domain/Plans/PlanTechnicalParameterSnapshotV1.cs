namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// Product-owned technical facts captured for one planned Inventory label. This is immutable plan
/// provenance: a reopen or replay uses these exact values and never asks Product for a newer answer.
/// It deliberately carries no density, speed, kerf, tolerance, scrap or process-policy value.
/// </summary>
public sealed record PlanTechnicalParameterSnapshotV1
{
    public const int MaximumProductVersionLength = 128;
    public const int MaximumCodeLength = 64;
    public const int MaximumRevisionLength = 64;

    public PlanTechnicalParameterSnapshotV1(
        Guid productId,
        string productVersion,
        Guid familyId,
        string familyCode,
        string familySourceRevision,
        Guid templateId,
        string templateVersion,
        string templateCode,
        string templateSourceRevision,
        Guid kilogramUnitCompanyId,
        Guid kilogramUnitId,
        string kilogramUnitCode,
        Guid kilogramProfileId,
        string kilogramProfileVersion,
        DateOnly effectiveOn,
        decimal thicknessMm,
        decimal widthMm,
        decimal? lengthMm = null)
    {
        ProductId = RequireId(productId, nameof(productId));
        ProductVersion = RequireText(productVersion, MaximumProductVersionLength, nameof(productVersion));
        FamilyId = RequireId(familyId, nameof(familyId));
        FamilyCode = RequireText(familyCode, MaximumCodeLength, nameof(familyCode));
        FamilySourceRevision = RequireText(familySourceRevision, MaximumRevisionLength, nameof(familySourceRevision));
        TemplateId = RequireId(templateId, nameof(templateId));
        TemplateVersion = RequireText(templateVersion, MaximumRevisionLength, nameof(templateVersion));
        TemplateCode = RequireText(templateCode, MaximumCodeLength, nameof(templateCode));
        TemplateSourceRevision = RequireText(templateSourceRevision, MaximumRevisionLength,
            nameof(templateSourceRevision));
        KilogramUnitCompanyId = RequireId(kilogramUnitCompanyId, nameof(kilogramUnitCompanyId));
        KilogramUnitId = RequireId(kilogramUnitId, nameof(kilogramUnitId));
        KilogramUnitCode = RequireText(kilogramUnitCode, MaximumCodeLength, nameof(kilogramUnitCode));
        KilogramProfileId = RequireId(kilogramProfileId, nameof(kilogramProfileId));
        KilogramProfileVersion = RequireText(kilogramProfileVersion, MaximumRevisionLength,
            nameof(kilogramProfileVersion));
        if (effectiveOn == default)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveOn), effectiveOn,
                "Teknik snapshot tarihi boş olamaz.");
        }

        EffectiveOn = effectiveOn;
        ThicknessMm = RequireMeasure(thicknessMm, nameof(thicknessMm));
        WidthMm = RequireMeasure(widthMm, nameof(widthMm));
        LengthMm = lengthMm is null ? null : RequireMeasure(lengthMm.Value, nameof(lengthMm));
    }

    public Guid ProductId { get; }
    public string ProductVersion { get; }
    public Guid FamilyId { get; }
    public string FamilyCode { get; }
    public string FamilySourceRevision { get; }
    public Guid TemplateId { get; }
    public string TemplateVersion { get; }
    public string TemplateCode { get; }
    public string TemplateSourceRevision { get; }
    public Guid KilogramUnitCompanyId { get; }
    public Guid KilogramUnitId { get; }
    public string KilogramUnitCode { get; }
    public Guid KilogramProfileId { get; }
    public string KilogramProfileVersion { get; }
    public DateOnly EffectiveOn { get; }
    public decimal ThicknessMm { get; }
    public decimal WidthMm { get; }
    public decimal? LengthMm { get; }

    private static Guid RequireId(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException("Teknik snapshot kimliği boş olamaz.", name) : value;

    private static string RequireText(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl))
        {
            throw new ArgumentException("Teknik snapshot metni geçerli değil.", name);
        }

        return value;
    }

    private static decimal RequireMeasure(decimal value, string name)
    {
        // Product admits a measure when reducing its encoded scale to three decimals changes no
        // numeric value. Keep the same exact rule here: 2.0000 is valid, while 2.0001 is refused;
        // neither value is rounded into a different technical fact.
        if (value <= 0m || decimal.Round(value, 3, MidpointRounding.ToZero) != value)
        {
            throw new ArgumentOutOfRangeException(name, value,
                "Teknik ölçü pozitif ve en fazla üç ondalıklı olmalıdır.");
        }

        return value;
    }
}
