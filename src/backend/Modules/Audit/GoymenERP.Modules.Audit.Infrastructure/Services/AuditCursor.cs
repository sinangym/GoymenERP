using System.Globalization;
using System.Text;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

internal sealed record AuditCursor(DateTimeOffset OccurredAtUtc, Guid Id)
{
    public static string Encode(DateTimeOffset occurredAtUtc, Guid id)
    {
        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{occurredAtUtc.UtcTicks:D19}|{id:D}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string value, out AuditCursor cursor)
    {
        cursor = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        try
        {
            var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(
                normalized.Length + ((4 - (normalized.Length % 4)) % 4),
                '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var parts = decoded.Split('|', StringSplitOptions.None);
            if (parts.Length != 2
                || !long.TryParse(
                    parts[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var utcTicks)
                || utcTicks < DateTimeOffset.MinValue.UtcTicks
                || utcTicks > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(parts[1], "D", out var id)
                || id == Guid.Empty)
            {
                return false;
            }

            cursor = new AuditCursor(new DateTimeOffset(utcTicks, TimeSpan.Zero), id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
