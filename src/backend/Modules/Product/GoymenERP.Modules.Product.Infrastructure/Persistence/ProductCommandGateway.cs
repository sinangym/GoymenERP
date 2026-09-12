using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductPreparedCommand(
    GroupSharedMasterCommandContext Context, ProductCommandIntent Intent,
    ProductMutationResponse? Replay, string? OriginalCorrelationId)
{
    public override string ToString() => nameof(ProductPreparedCommand);
}

/// <summary>
/// Product consumer of the accepted shared read/replay boundary. Missing replay
/// returns preparation only: the atomic writer must reauthorize and reserve the
/// global key in its transaction. This class never calls the privileged reader.
/// </summary>
public sealed class ProductCommandGateway(
    IGroupSharedMasterAuthorizationService authority,
    IGroupSharedCommandReplayResolver replayResolver)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
    };

    public async Task<GroupSharedMasterReadContext> AuthorizeReadAsync(CancellationToken cancellationToken)
    {
        var snapshot = await authority.GetSnapshotAsync(GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.Read, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return GroupSharedMasterReadContext.FromServerDecision(snapshot);
    }

    public Task<ProductPreparedCommand> PrepareCreateAsync(string key, CreateProductDraftRequest request,
        CancellationToken cancellationToken) => ResolveAsync(GroupSharedMasterOperation.DraftCreate,
            ProductCommandIntent.CreateDraft(key, request), null, cancellationToken);

    public Task<ProductPreparedCommand> PrepareEditAsync(string key, Guid productId, EditProductDraftRequest request,
        CancellationToken cancellationToken) => ResolveAsync(GroupSharedMasterOperation.DraftBasicEdit,
            ProductCommandIntent.EditDraft(key, productId, request), productId, cancellationToken);

    // The owner uses this after a whole-attempt rollback/unique race and before
    // relying on any prepared replay. It reuses the canonical response validator
    // and never reconstructs an intent from mutable HTTP inputs or a raw key.
    public async Task<ProductPreparedCommand> RefreshAsync(ProductPreparedCommand prepared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(prepared.Context);
        ArgumentNullException.ThrowIfNull(prepared.Intent);
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = JsonSerializer.Deserialize<FrozenCommandEnvelope>(prepared.Intent.CanonicalRequestJson, Json)
            ?? throw new InvalidOperationException("The frozen Product envelope is missing.");
        var operation = envelope.Operation switch
        {
            "product.draft.create.v1" when envelope.ProductId is null => GroupSharedMasterOperation.DraftCreate,
            "product.draft.basic-edit.v1" when envelope.ProductId is { } id && id != Guid.Empty => GroupSharedMasterOperation.DraftBasicEdit,
            _ => throw new InvalidOperationException("The frozen Product operation or target is invalid."),
        };
        if (prepared.Context.Operation != operation || envelope.Request.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The prepared authority differs from its frozen Product operation.");
        var refreshed = await ResolveAsync(operation, prepared.Intent, envelope.ProductId, cancellationToken);
        if (refreshed.Context.LocalActorId != prepared.Context.LocalActorId
            || refreshed.Context.OriginCompanyId != prepared.Context.OriginCompanyId)
            throw new UnauthorizedAccessException("A Product attempt cannot move to another actor or origin.");
        return refreshed;
    }

    private sealed record FrozenCommandEnvelope(string Operation, JsonElement Request, Guid? ProductId = null);

    private async Task<ProductPreparedCommand> ResolveAsync(GroupSharedMasterOperation operation,
        ProductCommandIntent intent, Guid? expectedProductId, CancellationToken cancellationToken)
    {
        var result = await replayResolver.ResolveAsync(operation, intent.KeyHash, intent.RequestHash, cancellationToken);
        switch (result.Kind)
        {
            case GroupSharedCommandReplayKind.Conflict:
                throw new ProductOperationException(ProductApiV1.IdempotencyConflict, 409);
            case GroupSharedCommandReplayKind.AuthorizedMissing:
                return new(result.Context, intent, null, null);
            case GroupSharedCommandReplayKind.MatchingReplay:
                var terminal = result.Terminal ?? throw new InvalidOperationException("A matching replay has no terminal.");
                var expectedStatus = operation == GroupSharedMasterOperation.DraftCreate ? 201 : 200;
                if (terminal.StatusCode != expectedStatus || terminal.SchemaVersion != 1)
                    throw new InvalidOperationException("Stored Product response status/schema is unsupported.");
                var response = JsonSerializer.Deserialize<ProductMutationResponse>(terminal.ResponseJson, Json);
                ValidateResponse(response, expectedProductId);
                // Only the documented replay indicator changes. UUID, code, version
                // and values remain the canonical response of the original write.
                return new(result.Context, intent, response! with { IsReplay = true }, terminal.OriginalCorrelationId);
            default:
                throw new InvalidOperationException("Unknown global command replay result.");
        }
    }

    private static void ValidateResponse(ProductMutationResponse? response, Guid? expectedProductId)
    {
        var product = response?.Card?.Product;
        if (product is null || response!.Card.References is null || product.Id == Guid.Empty
            || (expectedProductId.HasValue && product.Id != expectedProductId.Value)
            || product.Lifecycle != "Draft" || product.Roles is null || product.Labels is null
            || product.Completeness is null || product.Completeness.MissingFields is null
            || product.Completeness.Evaluation is not ("NotEvaluated" or "Incomplete" or "Complete"))
            throw new InvalidOperationException("Stored Product response is invalid.");

        // Reuse materialization guards for the complete core, including role
        // provenance, nil references, canonical code, dates and lossless version.
        _ = Domain.Products.Product.Restore(product.Id, product.Code, product.Name,
            product.GroupId, product.ClassId, product.FamilyId,
            new(RestoreRole(product.Roles.Purchasable), RestoreRole(product.Roles.Sellable),
                RestoreRole(product.Roles.ProductionInput), RestoreRole(product.Roles.ProductionOutput)),
            ProductLifecycle.Draft, ProductVersion.Parse(product.Version), product.CreatedAtUtc, product.UpdatedAtUtc);
    }

    private static ProductRoleState RestoreRole(ProductRoleView? role) => (role?.Provenance, role?.Value) switch
    {
        ("not-evaluated", null) => ProductRoleState.NotEvaluated,
        ("manual", bool value) => ProductRoleState.Manual(value),
        ("derived", bool value) => ProductRoleState.Derived(value),
        _ => throw new InvalidOperationException("Stored Product response role is invalid."),
    };
}
