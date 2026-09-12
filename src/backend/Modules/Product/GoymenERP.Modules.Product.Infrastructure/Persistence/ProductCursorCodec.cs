using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Authenticated continuation state, using the existing HMAC-SHA256 cursor pattern.
/// The host supplies key material and technical lifetime; no key, grant or default
/// retention policy is provisioned here. Fresh read authority is still required.
/// </summary>
public sealed class ProductCursorCodec : IDisposable
{
    private const int MaximumLength = 4096;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
    };
    private readonly byte[] key;
    private readonly TimeProvider clock;
    private readonly TimeSpan lifetime;
    private bool disposed;

    public ProductCursorCodec(ReadOnlySpan<byte> signingKey, TimeProvider clock, TimeSpan lifetime)
    {
        if (signingKey.Length < 32) throw new ArgumentException("A cursor signing key of at least 32 bytes is required.", nameof(signingKey));
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.lifetime = lifetime;
        key = signingKey.ToArray();
    }

    public string Protect(GroupSharedMasterReadContext authority, ProductListQuery query, ProductListPosition position)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(authority);
        query = ProductQueryRules.Normalize(query);
        ValidatePosition(query, position);
        var state = new CursorState(1, authority.LocalActorId, authority.OriginCompanyId, authority.AuthorityRevision,
            QueryHash(query), clock.GetUtcNow(), position);
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, Json);
        var result = Encode(payload) + "." + Encode(HMACSHA256.HashData(key, payload));
        if (result.Length > MaximumLength) throw new InvalidOperationException("Product cursor state exceeds its transport limit.");
        return result;
    }

    public ProductListPosition Unprotect(GroupSharedMasterReadContext authority, ProductListQuery query, string cursor)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(authority);
        query = ProductQueryRules.Normalize(query);
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > MaximumLength || cursor.Any(char.IsWhiteSpace)) throw Invalid();
        var parts = cursor.Split('.');
        if (parts.Length != 2) throw Invalid();
        try
        {
            var payload = Decode(parts[0]);
            var signature = Decode(parts[1]);
            if (signature.Length != 32 || !CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, payload))) throw Invalid();
            var state = JsonSerializer.Deserialize<CursorState>(payload, Json);
            var now = clock.GetUtcNow();
            if (state is null || state.Schema != 1 || state.Actor != authority.LocalActorId
                || state.Origin != authority.OriginCompanyId || state.AuthorityRevision != authority.AuthorityRevision
                || state.QueryHash != QueryHash(query) || state.IssuedAtUtc.Offset != TimeSpan.Zero
                || state.IssuedAtUtc > now || now - state.IssuedAtUtc >= lifetime) throw Invalid();
            ValidatePosition(query, state.Position);
            return state.Position;
        }
        catch (FormatException) { throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }

    private static string QueryHash(ProductListQuery query) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(query with { Cursor = null }, Json)));

    internal static void ValidatePosition(ProductListQuery query, ProductListPosition? position)
    {
        if (position is null || position.Id == Guid.Empty) throw Invalid();
        if (query.Sort == "updatedAtUtc")
        {
            if (position.Text is not null || position.UpdatedAtUtc is null || position.UpdatedAtUtc.Value.Offset != TimeSpan.Zero) throw Invalid();
        }
        else if (position.UpdatedAtUtc is not null || string.IsNullOrEmpty(position.Text)
            || position.Text.Length > Domain.Products.Product.MaximumNameLength || position.Text.Any(char.IsControl)) throw Invalid();
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))) throw Invalid();
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
        if (Encode(bytes) != value) throw Invalid();
        return bytes;
    }
    private static ProductOperationException Invalid() => new(ProductApiV1.InvalidCursor, 400);

    public void Dispose()
    {
        if (disposed) return;
        CryptographicOperations.ZeroMemory(key);
        disposed = true;
    }

    private sealed record CursorState(int Schema, Guid Actor, Guid Origin, string AuthorityRevision,
        string QueryHash, DateTimeOffset IssuedAtUtc, ProductListPosition Position);
}
