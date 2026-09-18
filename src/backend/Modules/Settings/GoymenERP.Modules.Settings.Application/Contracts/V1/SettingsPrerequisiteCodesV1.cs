namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Published <see cref="SettingsPrerequisiteException.Code"/> values a consumer may map to its own
/// public answer. A consumer maps these constants, never a provider class and never message text.
/// </summary>
public static class SettingsPrerequisiteCodesV1
{
    /// <summary>
    /// The company's approved source is reachable but holds no profile for the unit asked about
    /// (including an empty unit id). Kept apart from <c>settings_provider_unavailable</c> (there is
    /// no source to ask) and <c>settings_profile_invalid</c> (the source's data is malformed): it is
    /// a permanent answer about this identity, not a transient failure.
    /// </summary>
    public const string UnitProfileNotFound = "settings_unit_profile_not_found";
}
