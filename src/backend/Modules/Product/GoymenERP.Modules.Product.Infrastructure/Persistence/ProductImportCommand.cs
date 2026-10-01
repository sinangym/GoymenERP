using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>Frozen apply identity. Raw workbook bytes and the raw idempotency key are never retained here.</summary>
public sealed record ProductImportCommandIntent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ProductImportCommandIntent(string key, ProductImportApplyBindingV1 binding)
    {
        ValidateKey(key);
        Binding = Normalize(binding);
        var request = JsonSerializer.SerializeToElement(new
        {
            operation = ProductImportOperation,
            request = Binding,
        }, Json);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteCanonical(writer, request);
        KeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        RequestHash = Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
    }

    public const string ProductImportOperation = "product.import.v1";
    public ProductImportApplyBindingV1 Binding { get; }
    public string KeyHash { get; }
    public string RequestHash { get; }

    public static ProductImportCommandIntent Create(string key, ProductImportApplyBindingV1 binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(key, binding);
    }

    private static ProductImportApplyBindingV1 Normalize(ProductImportApplyBindingV1 binding)
    {
        ArgumentNullException.ThrowIfNull(binding.Mapping);
        var mapping = new ProductImportColumnMappingV1(
            Header(binding.Mapping.LegacyCode), Header(binding.Mapping.Name), Header(binding.Mapping.GroupId));
        if (new[] { mapping.LegacyCode, mapping.Name, mapping.GroupId }.Distinct(StringComparer.Ordinal).Count() != 3)
            throw Invalid("mapping");
        return new(Hex(binding.FileSha256, "fileSha256"), Hex(binding.PreviewToken, "previewToken"), mapping);
    }

    private static string Header(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid("mapping");
        var normalized = value.Trim();
        if (normalized.Any(char.IsControl)) throw Invalid("mapping");
        return normalized;
    }

    private static string Hex(string value, string field)
    {
        if (value is null || value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))
            || !string.Equals(value, value.ToUpperInvariant(), StringComparison.Ordinal))
            throw Invalid(field);
        return value;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128
            || key.Any(character => character < 33 || character > 126 || character == ','))
            throw Invalid("idempotencyKey");
    }

    private static ProductValidationException Invalid(string field) =>
        new([new(field, ProductImportCodesV1.InvalidMetadata)]);

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    public override string ToString() => nameof(ProductImportCommandIntent);
}

public sealed record ProductImportPreparedCommand(
    GroupSharedMasterCommandContext Context,
    ProductImportCommandIntent Intent,
    ProductImportResultV1? Replay,
    string? OriginalCorrelationId)
{
    public override string ToString() => nameof(ProductImportPreparedCommand);
}

public sealed class ProductImportCommandGateway(IGroupSharedCommandReplayResolver replayResolver)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
    };

    public Task<ProductImportPreparedCommand> PrepareAsync(string key, ProductImportApplyBindingV1 binding,
        CancellationToken cancellationToken) => ResolveAsync(ProductImportCommandIntent.Create(key, binding), cancellationToken);

    public async Task<ProductImportPreparedCommand> RefreshAsync(ProductImportPreparedCommand prepared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var refreshed = await ResolveAsync(prepared.Intent, cancellationToken);
        if (refreshed.Context.LocalActorId != prepared.Context.LocalActorId
            || refreshed.Context.OriginCompanyId != prepared.Context.OriginCompanyId)
            throw new UnauthorizedAccessException("A Product import cannot move to another actor or origin.");
        return refreshed;
    }

    private async Task<ProductImportPreparedCommand> ResolveAsync(ProductImportCommandIntent intent,
        CancellationToken cancellationToken)
    {
        var resolved = await replayResolver.ResolveAsync(GroupSharedMasterOperation.DraftCreate,
            intent.KeyHash, intent.RequestHash, cancellationToken);
        return resolved.Kind switch
        {
            GroupSharedCommandReplayKind.Conflict => throw new ProductOperationException(ProductApiV1.IdempotencyConflict, 409),
            GroupSharedCommandReplayKind.AuthorizedMissing => new(resolved.Context, intent, null, null),
            GroupSharedCommandReplayKind.MatchingReplay => Replay(resolved, intent),
            _ => throw new InvalidOperationException("Unknown Product import replay result."),
        };
    }

    private static ProductImportPreparedCommand Replay(GroupSharedCommandReplayResult resolved,
        ProductImportCommandIntent intent)
    {
        var terminal = resolved.Terminal ?? throw new InvalidOperationException("A matching import replay has no terminal.");
        if (terminal.StatusCode != 201 || terminal.SchemaVersion != 1)
            throw new InvalidOperationException("Stored Product import response status/schema is unsupported.");
        var response = JsonSerializer.Deserialize<ProductImportResultV1>(terminal.ResponseJson, Json);
        ValidateResponse(response);
        var valid = response!;
        if (!string.Equals(valid.OriginalCorrelationId, terminal.OriginalCorrelationId, StringComparison.Ordinal))
            throw new InvalidOperationException("Stored Product import correlation is inconsistent.");
        return new(resolved.Context, intent,
            valid with { IsReplay = true, OriginalCorrelationId = terminal.OriginalCorrelationId },
            terminal.OriginalCorrelationId);
    }

    private static void ValidateResponse(ProductImportResultV1? response)
    {
        if (response is null || response.IsReplay || string.IsNullOrWhiteSpace(response.OriginalCorrelationId)
            || response.Products is null || response.Products.Count is 0 or > ProductImportApiV1.MaximumRows
            || response.Products.Any(item => item.ProductId == Guid.Empty || item.GroupId == Guid.Empty
                || string.IsNullOrWhiteSpace(item.LegacyCode)))
            throw new InvalidOperationException("Stored Product import response is invalid.");
        try
        {
            foreach (var item in response.Products)
            {
                _ = ProductCode.Parse(item.Code);
                _ = Domain.Products.Product.NormalizeName(item.Name);
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Stored Product import response contains invalid values.", exception);
        }
        if (response.Products.Select(item => item.ProductId).Distinct().Count() != response.Products.Count
            || response.Products.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != response.Products.Count
            || response.Products.Select(item => item.LegacyCode).Distinct(StringComparer.Ordinal).Count() != response.Products.Count)
            throw new InvalidOperationException("Stored Product import response contains duplicate identities.");
    }
}

/// <summary>
/// Implemented by the shared host with one borrowed SQL transaction containing Product, canonical audits and the
/// idempotency terminal. The runner owns response replay headers and reauthorization immediately before commit.
/// </summary>
public interface IProductImportCommandRunner
{
    Task<ProductImportResultV1> RunAsync(ProductImportPreparedCommand prepared, ReadOnlyMemory<byte> workbook,
        CancellationToken cancellationToken);
}

public sealed class ProductImportService(
    IProductImportPreviewServiceV1 preview,
    ProductImportCommandGateway gateway,
    IProductImportCommandRunner commands) : IProductImportServiceV1
{
    public Task<ProductImportPreviewV1> PreviewAsync(ReadOnlyMemory<byte> workbook,
        ProductImportColumnMappingV1 mapping, CancellationToken cancellationToken) =>
        preview.PreviewAsync(workbook, mapping, cancellationToken);

    public async Task<ProductImportResultV1> ApplyAsync(ReadOnlyMemory<byte> workbook,
        ProductImportApplyBindingV1 binding, string idempotencyKey, CancellationToken cancellationToken)
    {
        // The participant must receive the same immutable byte sequence that was hashed by the prepared binding.
        var frozenWorkbook = workbook.ToArray();
        var prepared = await gateway.PrepareAsync(idempotencyKey, binding, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(frozenWorkbook),
                Convert.FromHexString(prepared.Intent.Binding.FileSha256)))
            throw new ProductValidationException([new("fileSha256", ProductImportCodesV1.PreviewMismatch)]);
        return await commands.RunAsync(prepared, frozenWorkbook, cancellationToken);
    }
}
