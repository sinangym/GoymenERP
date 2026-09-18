namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Settings-owned currency identities. A currency identity is company-independent (TRY is TRY in
/// every company); what a company may use, for which operation, from when and at what scale is its
/// MONEY PROFILE, served by <see cref="ISettingsPrerequisiteProvider.GetMoneyProfileAsync"/>.
/// <para>
/// Technical identifiers are the implementer's work (Sinan's Ö2 decision, #66 5722637822). They
/// are assigned once and never reused, derived as UUIDv5 so the derivation can be re-checked:
/// namespace = UUIDv5(URL, "https://github.com/sinangym/GoymenERP/settings/money-profiles")
/// = <c>9df84417-8c25-59c8-a67a-959ac880a890</c>, name "currency:TRY".
/// </para>
/// </summary>
public static class SettingsCurrencyIdentitiesV1
{
    /// <summary>Turkish lira. The only currency with an approved money profile today (GDC/TRY, Ö2).</summary>
    public static readonly Guid TurkishLira = Guid.Parse("9e60dcf0-f4bd-5bca-b464-f9a2db98e31a");
}
