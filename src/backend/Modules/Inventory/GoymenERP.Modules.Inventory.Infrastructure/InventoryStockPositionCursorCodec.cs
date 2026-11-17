using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryStockPositionCursorRuntimeOptions(string? SigningKeyBase64);

internal sealed class InventoryStockPositionCursorCodec : IInventoryStockPositionCursorCodec, IInventoryTransferReceiptDocumentCursorV1,
    IInventoryTransferReceiptWorkCursorV1, IDisposable
{
    private const int MaximumLength = 4096;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
    };
    private readonly byte[] key;
    private readonly TimeProvider clock;

    public InventoryStockPositionCursorCodec(
        InventoryStockPositionCursorRuntimeOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        key = ReadKey(options.SigningKeyBase64);
    }

    public bool IsAvailable => key.Length >= 32;

    public string Protect(Guid companyId, string actorId, InventoryStockPositionReadQuery query,
        InventoryStockPositionPosition position)
    {
        if (!IsAvailable || companyId == Guid.Empty || string.IsNullOrWhiteSpace(actorId))
        {
            throw new InventoryStockPositionException(503, InventoryStockPositionCodesV1.Unavailable);
        }
        ValidatePosition(position);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorState(
            1, companyId, actorId, BindingHash(query), clock.GetUtcNow(), position), Json);
        var result = $"{Encode(payload)}.{Encode(HMACSHA256.HashData(key, payload))}";
        return result.Length <= MaximumLength
            ? result
            : throw new InventoryStockPositionException(503, InventoryStockPositionCodesV1.Unavailable);
    }

    public InventoryStockPositionPosition Unprotect(Guid companyId, string actorId,
        InventoryStockPositionReadQuery query, string cursor)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(cursor) || cursor.Length > MaximumLength
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
                || state.ActorId != actorId || state.BindingHash != BindingHash(query)
                || state.IssuedAtUtc.Offset != TimeSpan.Zero || state.IssuedAtUtc > now
                || now - state.IssuedAtUtc >= Lifetime)
            {
                throw Invalid();
            }
            ValidatePosition(state.Position);
            return state.Position;
        }
        catch (FormatException) { throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(key);

    string IInventoryTransferReceiptDocumentCursorV1.Protect(Guid companyId, string actorId, Guid transferId, Guid afterDocumentId)
    {
        if (!IsAvailable || companyId == Guid.Empty || transferId == Guid.Empty || afterDocumentId == Guid.Empty || string.IsNullOrWhiteSpace(actorId))
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new ReceiptCursorState(1, InventoryTransferReceiptDocumentV1.SourceVersion,
            companyId, actorId, transferId, afterDocumentId, clock.GetUtcNow()), Json);
        return $"{Encode(payload)}.{Encode(HMACSHA256.HashData(key, payload))}";
    }
    Guid IInventoryTransferReceiptDocumentCursorV1.Unprotect(Guid companyId, string actorId, Guid transferId, string cursor)
    {
        if (!IsAvailable) throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        try
        {
            if (string.IsNullOrWhiteSpace(cursor) || cursor != cursor.Trim() || cursor.Length > MaximumLength) throw ReceiptInvalid();
            var parts = cursor.Split('.'); if (parts.Length != 2) throw ReceiptInvalid();
            var payload = Decode(parts[0]); var signature = Decode(parts[1]);
            if (signature.Length != 32 || !CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, payload))) throw ReceiptInvalid();
            var state = JsonSerializer.Deserialize<ReceiptCursorState>(payload, Json); var now = clock.GetUtcNow();
            if (state is null || state.Schema != 1 || state.Purpose != InventoryTransferReceiptDocumentV1.SourceVersion
                || state.CompanyId != companyId || state.ActorId != actorId || state.TransferId != transferId || state.AfterDocumentId == Guid.Empty
                || state.IssuedAtUtc.Offset != TimeSpan.Zero || state.IssuedAtUtc > now || now - state.IssuedAtUtc >= Lifetime) throw ReceiptInvalid();
            return state.AfterDocumentId;
        }
        catch (InventoryStockPositionException) { throw ReceiptInvalid(); }
        catch (FormatException) { throw ReceiptInvalid(); }
        catch (JsonException) { throw ReceiptInvalid(); }
    }
    private static InventoryTransferException ReceiptInvalid() => new(400, InventoryTransferReceiptDocumentV1.Invalid);
    private sealed record ReceiptCursorState(int Schema, string Purpose, Guid CompanyId, string ActorId,
        Guid TransferId, Guid AfterDocumentId, DateTimeOffset IssuedAtUtc);

    string IInventoryTransferReceiptWorkCursorV1.Protect(Guid companyId, string actorId, string purpose, string? query, int limit, Guid afterTransferId)
    {
        if (!IsAvailable || companyId == Guid.Empty || afterTransferId == Guid.Empty || string.IsNullOrWhiteSpace(actorId)
            || InventoryTransferReceiptWorkPurposesV1.Permission(purpose) is null)
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new WorkCursorState(1, "inventory.transfer.receipt-work.v1", companyId,
            actorId, purpose, query, limit, afterTransferId, clock.GetUtcNow()), Json);
        return $"{Encode(payload)}.{Encode(HMACSHA256.HashData(key, payload))}";
    }
    Guid IInventoryTransferReceiptWorkCursorV1.Unprotect(Guid companyId, string actorId, string purpose, string? query, int limit, string cursor)
    {
        if (!IsAvailable) throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        try
        {
            if (string.IsNullOrWhiteSpace(cursor) || cursor != cursor.Trim() || cursor.Length > MaximumLength) throw WorkInvalid();
            var parts = cursor.Split('.'); if (parts.Length != 2) throw WorkInvalid();
            var payload = Decode(parts[0]); var signature = Decode(parts[1]);
            if (signature.Length != 32 || !CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, payload))) throw WorkInvalid();
            var state = JsonSerializer.Deserialize<WorkCursorState>(payload, Json); var now = clock.GetUtcNow();
            if (state is null || state.Schema != 1 || state.Surface != "inventory.transfer.receipt-work.v1" || state.CompanyId != companyId
                || state.ActorId != actorId || state.Purpose != purpose || state.Query != query || state.Limit != limit || state.AfterTransferId == Guid.Empty
                || state.IssuedAtUtc.Offset != TimeSpan.Zero || state.IssuedAtUtc > now || now - state.IssuedAtUtc >= Lifetime) throw WorkInvalid();
            return state.AfterTransferId;
        }
        catch (InventoryStockPositionException) { throw WorkInvalid(); }
        catch (FormatException) { throw WorkInvalid(); }
        catch (JsonException) { throw WorkInvalid(); }
    }
    private static InventoryTransferException WorkInvalid() => new(400, InventoryTransferCodesV1.ValidationFailed);
    private sealed record WorkCursorState(int Schema, string Surface, Guid CompanyId, string ActorId, string Purpose,
        string? Query, int Limit, Guid AfterTransferId, DateTimeOffset IssuedAtUtc);

    private static string BindingHash(InventoryStockPositionReadQuery query) => Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(query with { After = null }, Json)));

    private static void ValidatePosition(InventoryStockPositionPosition position)
    {
        if (position.LabelId == Guid.Empty || string.IsNullOrWhiteSpace(position.LabelCode)
            || position.LabelCode.Length > Domain.Labels.MaterialLabel.MaximumCodeLength
            || !string.Equals(position.LabelCode, position.LabelCode.Trim(), StringComparison.Ordinal)
            || position.LabelCode.Any(char.IsControl))
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
            throw new InvalidOperationException("Inventory stock-position cursor key must be at least 32 bytes.");
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Inventory stock-position cursor key must be valid base64.", exception);
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

    private static InventoryStockPositionException Invalid() =>
        new(400, InventoryStockPositionCodesV1.InvalidQuery);

    private sealed record CursorState(int Schema, Guid CompanyId, string ActorId,
        string BindingHash, DateTimeOffset IssuedAtUtc, InventoryStockPositionPosition Position);
}
