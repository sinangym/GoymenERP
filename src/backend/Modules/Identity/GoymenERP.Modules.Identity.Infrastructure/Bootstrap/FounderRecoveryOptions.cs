using System.Globalization;

namespace GoymenERP.Modules.Identity.Infrastructure.Bootstrap;

/// <summary>
/// <c>Identity:FounderRecovery</c> (spec ID-2r, KIM-D43). <see cref="TemporaryPasswordLifetime"/> is the lifetime of an
/// unused temporary password issued by <c>recover-founder reset-password</c>. An empty or missing value means the
/// decided default (<see cref="DefaultTemporaryPasswordLifetime"/>); a value that is not a positive <c>TimeSpan</c> is
/// invalid and the command refuses with <c>configuration_missing</c> (no temporary password without an end,
/// fail-closed). The value is read only when a temporary password is issued; a login compares only the stored end.
/// </summary>
public sealed class FounderRecoveryOptions
{
    public const string SectionName = "Identity:FounderRecovery";

    /// <summary>KIM-D43: the lifetime when the setting is empty.</summary>
    public static readonly TimeSpan DefaultTemporaryPasswordLifetime = TimeSpan.FromHours(72);

    /// <summary>Kept as text so an unreadable value refuses the command instead of failing the options binding.</summary>
    public string? TemporaryPasswordLifetime { get; set; }

    /// <summary>The effective lifetime, or <see langword="null"/> when the configured value is invalid.</summary>
    public TimeSpan? ResolveTemporaryPasswordLifetime()
    {
        if (string.IsNullOrWhiteSpace(TemporaryPasswordLifetime))
        {
            return DefaultTemporaryPasswordLifetime;
        }

        return TimeSpan.TryParse(TemporaryPasswordLifetime.Trim(), CultureInfo.InvariantCulture, out var lifetime)
            && lifetime > TimeSpan.Zero
                ? lifetime
                : null;
    }
}
