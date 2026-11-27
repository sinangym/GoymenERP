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

    // UNIT-SCOPE: technical identities only. No company profile, precision, family default or conversion is implied.
    /// <summary>Litre. UUIDv5 name "unit:litre" in the existing unit-profiles namespace.</summary>
    public static readonly Guid Litre = Guid.Parse("c92ed9ca-7d92-5a55-a199-6f2d580b87f6");
    /// <summary>Takım (kit). UUIDv5 name "unit:kit"; not interchangeable with a piece or set.</summary>
    public static readonly Guid Kit = Guid.Parse("c6ff7aab-f645-50df-ad31-dbd2fb30fbc5");
    /// <summary>Set. UUIDv5 name "unit:set"; its content is not inferred.</summary>
    public static readonly Guid Set = Guid.Parse("7bb486df-a533-5f54-b5fa-a86277488bc9");
    /// <summary>Hour. UUIDv5 name "unit:hour"; no billing or time conversion rule is implied.</summary>
    public static readonly Guid Hour = Guid.Parse("addb2ea5-bf3f-519b-98d4-7e5e568f7aaf");
    /// <summary>Day. UUIDv5 name "unit:day"; no working-day, shift or hour conversion is implied.</summary>
    public static readonly Guid Day = Guid.Parse("ac628f64-616f-5099-a22a-43620e571e63");
    /// <summary>One service unit. UUIDv5 name "unit:service"; no physical stock meaning is implied.</summary>
    public static readonly Guid Service = Guid.Parse("da00d1f1-4649-59e1-889e-f8d9accd280a");

    public static IReadOnlyList<UnitIdentityV1> Standard { get; } = Array.AsReadOnly<UnitIdentityV1>(
    [new(Kilogram, "kg", "weight", true), new(Tonne, "t", "weight", false),
        new(Metre, "m", "length", false), new(Piece, "adet", "count", false),
        new(Litre, "l", "volume", false), new(Kit, "takım", "count", false), new(Set, "set", "count", false),
        new(Hour, "saat", "time", false), new(Day, "gün", "time", false), new(Service, "hizmet", "count", false)]);
}
