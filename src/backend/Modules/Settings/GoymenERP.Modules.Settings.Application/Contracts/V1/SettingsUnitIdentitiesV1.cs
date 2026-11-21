namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Settings-owned unit identities. A unit identity is company-independent (kg is kg in every
/// company); what a company may use, from when and at what scale is its UNIT PROFILE, served by
/// <see cref="ISettingsPrerequisiteProvider.GetUnitProfileAsync"/>.
/// <para>
/// Technical identifiers are the implementer's work (Sinan's Ö2 decision, #66 5722637822). They
/// are assigned once and never reused, derived as UUIDv5 so the derivation can be re-checked:
/// namespace = UUIDv5(URL, "https://github.com/sinangym/GoymenERP/settings/unit-profiles")
/// = <c>18b5cbb9-98ca-5999-b418-aa53c41f0ffb</c>, name "unit:kg".
/// </para>
/// </summary>
public static class SettingsUnitIdentitiesV1
{
    /// <summary>Kilogram; existing approved company profiles are unchanged.</summary>
    public static readonly Guid Kilogram = Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983");
    /// <summary>Metric tonne (Master §28.1). UUIDv5 name "unit:tonne"; identity only, no company profile.</summary>
    public static readonly Guid Tonne = Guid.Parse("ab3cea5c-850e-515f-8de5-16f192ad7771");
    /// <summary>Metre (ALS-D43/D51). UUIDv5 name "unit:metre"; identity only, no company profile.</summary>
    public static readonly Guid Metre = Guid.Parse("564b68b4-02a8-5e72-af2a-4f5cb9b91702");
    /// <summary>Piece (ALS-D43/D51). UUIDv5 name "unit:piece"; identity only, no company profile.</summary>
    public static readonly Guid Piece = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5");

    public static IReadOnlyList<UnitIdentityV1> Standard { get; } = Array.AsReadOnly<UnitIdentityV1>(
    [new(Kilogram, "kg", "weight", true), new(Tonne, "t", "weight", false),
        new(Metre, "m", "length", false), new(Piece, "adet", "count", false)]);
}
