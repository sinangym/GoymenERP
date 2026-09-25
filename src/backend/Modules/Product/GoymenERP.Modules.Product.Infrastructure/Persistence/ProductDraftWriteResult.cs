using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Product facts for the canonical Audit and Idempotency owners, not a second
/// ledger. The host adds verified request metadata and original correlation.
/// Constructed from the same canonical response returned after transaction commit.
/// </summary>
public sealed record ProductDraftWriteResult
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal ProductDraftWriteResult(ProductCardView? before, ProductCardView after, string reason, string? eventType = null)
    {
        Response = new(after, false);
        StatusCode = before is null ? 201 : 200;
        ResponseJson = JsonSerializer.Serialize(Response, Json);
        ProductId = after.Product.Id;
        VersionBefore = before is null ? null : ProductVersion.Parse(before.Product.Version);
        VersionAfter = ProductVersion.Parse(after.Product.Version);
        BeforeJson = before is null ? "{}" : JsonSerializer.Serialize(before, Json);
        AfterJson = JsonSerializer.Serialize(after, Json);
        Reason = reason;
        EventType = eventType ?? (before is null ? "product.draft.created.v1" : "product.draft.basic-edited.v1");
    }

    public ProductMutationResponse Response { get; }
    public int StatusCode { get; }
    public string ResponseJson { get; }
    public Guid ProductId { get; }
    public long? VersionBefore { get; }
    public long VersionAfter { get; }
    public string BeforeJson { get; }
    public string AfterJson { get; }
    public string Reason { get; }
    public string EventType { get; }
    public override string ToString() => nameof(ProductDraftWriteResult);
}
