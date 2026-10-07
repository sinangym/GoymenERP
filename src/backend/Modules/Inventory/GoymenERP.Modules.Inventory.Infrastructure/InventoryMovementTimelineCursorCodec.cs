using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryMovementTimelineCursorRuntimeOptions(string? SigningKeyBase64);

internal sealed class InventoryMovementTimelineCursorCodec : IInventoryMovementTimelineCursorCodec, IDisposable
{
    private const int MaximumLength = 4096;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
    };
    private readonly byte[] key;
    private readonly TimeProvider clock;

    public InventoryMovementTimelineCursorCodec(
        InventoryMovementTimelineCursorRuntimeOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        key = ReadKey(options.SigningKeyBase64);
    }

    public bool IsAvailable => key.Length >= 32;

    public string Protect(Guid companyId, string actorId, int limit, InventoryMovementTimelinePosition position)
    {
        ValidateBinding(companyId, actorId, limit);
        ValidatePosition(position);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorState(
            1, companyId, actorId, limit, clock.GetUtcNow(), position), Json);
        var result = $"{Encode(payload)}.{Encode(HMACSHA256.HashData(key, payload))}";
        return result.Length <= MaximumLength
            ? result
            : throw Unavailable();
    }

    public InventoryMovementTimelinePosition Unprotect(
        Guid companyId,
        string actorId,
        int limit,
        string cursor)
    {
        ValidateBinding(companyId, actorId, limit);
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > MaximumLength
            || !string.Equals(cursor, cursor.Trim(), StringComparison.Ordinal))
        {
            throw Invalid();
        }

        try
        {
            var parts = cursor.Split('.');
            if (parts.Length != 2) throw Invalid();
            var payload = Decode(parts[0]);
            var signature = Decode(parts[1]);
            if (signature.Length != 32
                || !CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, payload)))
            {
                throw Invalid();
            }

            var state = JsonSerializer.Deserialize<CursorState>(payload, Json);
            var now = clock.GetUtcNow();
            if (state is null || state.Schema != 1 || state.CompanyId != companyId
                || state.ActorId != actorId || state.Limit != limit
                || state.IssuedAtUtc.Offset != TimeSpan.Zero || state.IssuedAtUtc > now
                || now - state.IssuedAtUtc >= Lifetime)
            {
                throw Invalid();
            }
            ValidatePosition(state.Position);
            return state.Position;
        }
        catch (FormatException)
        {
            throw Invalid();
        }
        catch (JsonException)
        {
            throw Invalid();
        }
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(key);

    private void ValidateBinding(Guid companyId, string actorId, int limit)
    {
        if (!IsAvailable || companyId == Guid.Empty || string.IsNullOrWhiteSpace(actorId)
            || limit is < 1 or > 100)
        {
            throw Unavailable();
        }
    }

    private static void ValidatePosition(InventoryMovementTimelinePosition position)
    {
        if (position.MovementId == Guid.Empty || position.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid();
        }
    }

    private static byte[] ReadKey(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return [];
        try
        {
            var value = Convert.FromBase64String(configured);
            if (value.Length >= 32) return value;
            CryptographicOperations.ZeroMemory(value);
            throw new InvalidOperationException("Inventory movement-timeline cursor key must be at least 32 bytes.");
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Inventory movement-timeline cursor key must be valid base64.", exception);
        }
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_')))
        {
            throw Invalid();
        }
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/')
            .PadRight((value.Length + 3) / 4 * 4, '='));
        if (Encode(bytes) != value) throw Invalid();
        return bytes;
    }

    private static InventoryMovementTimelineException Invalid() =>
        new(400, InventoryMovementTimelineCodesV1.InvalidQuery);

    private static InventoryMovementTimelineException Unavailable() =>
        new(503, InventoryMovementTimelineCodesV1.Unavailable);

    private sealed record CursorState(
        int Schema,
        Guid CompanyId,
        string ActorId,
        int Limit,
        DateTimeOffset IssuedAtUtc,
        InventoryMovementTimelinePosition Position);
}
