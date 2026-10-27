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
    /// <summary>Kilogram. The only unit with an approved profile today (GDC, Ö1/Ö2; GSP and GIN, SAT-D18).</summary>
    public static readonly Guid Kilogram = Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983");
}
