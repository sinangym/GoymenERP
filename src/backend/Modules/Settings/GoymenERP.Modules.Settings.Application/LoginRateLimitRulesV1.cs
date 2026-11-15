using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// The pure input rules of the group-wide login attempt limit (KIM-D34). They only check the technical shape of a value; which
/// limit the group uses is a Settings value, never chosen here.
/// </summary>
public static class LoginRateLimitRulesV1
{
    /// <summary>An attempt count inside the technical bounds.</summary>
    public static bool IsPermitLimit(int? value) =>
        value is >= LoginRateLimitInputLimitsV1.MinimumPermitLimit and <= LoginRateLimitInputLimitsV1.MaximumPermitLimit;

    /// <summary>A window length in seconds inside the technical bounds.</summary>
    public static bool IsWindowSeconds(int? value) =>
        value is >= LoginRateLimitInputLimitsV1.MinimumWindowSeconds and <= LoginRateLimitInputLimitsV1.MaximumWindowSeconds;

    /// <summary>One of the published partition names, spelled exactly (no case folding, no trimming).</summary>
    public static bool IsPartition(string? value) =>
        value is not null && LoginRateLimitPartitionsV1.All.Contains(value, StringComparer.Ordinal);
}
