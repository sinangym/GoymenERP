using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>KLN-D66/D67: owner source by ProductId, never inference from a unit code or a historical card revision.</summary>
public sealed class InventoryProductFutureUnitConverterV1(IInventoryProductUnitConversionSourceV1 source)
    : IInventoryFutureUnitConverterV1
{
    public Task<InventoryFutureUnitConversionV1> ConvertAsync(Guid productId, string productVersion, decimal quantity,
        Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Identity needs no product coefficient; preserves the existing F017 contract.
        return fromUnitId != Guid.Empty && fromUnitId == toUnitId && quantity >= 0m
            ? Task.FromResult(new InventoryFutureUnitConversionV1(InventoryFutureUnitConversionOutcomeV1.Converted, quantity))
            : ReadAsync(productId, quantity, fromUnitId, toUnitId, cancellationToken);
    }

    public Task<InventoryFutureUnitConversionV1> ConvertToKilogramsAsync(Guid productId, string productVersion,
        decimal quantity, Guid fromUnitId, CancellationToken cancellationToken = default) =>
        ReadAsync(productId, quantity, fromUnitId, null, cancellationToken);

    private async Task<InventoryFutureUnitConversionV1> ReadAsync(Guid product, decimal quantity, Guid from, Guid? to,
        CancellationToken token)
    {
        if (product == Guid.Empty || from == Guid.Empty || to == Guid.Empty || quantity < 0m)
            return new(InventoryFutureUnitConversionOutcomeV1.NoSource, null);
        InventoryProductUnitConversionAnswerV1 answer;
        try { answer = await source.ConvertAsync(product, quantity, from, to, token); }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (InventoryAllocationException) { throw; }
        catch (Exception error)
        { throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, innerException: error); }
        return answer switch
        {
            { Outcome: InventoryProductUnitConversionOutcomeV1.Converted, Quantity: >= 0m } =>
                new(InventoryFutureUnitConversionOutcomeV1.Converted, answer.Quantity, answer.RemainderKg),
            { Outcome: InventoryProductUnitConversionOutcomeV1.NoSource, Quantity: null } =>
                new(InventoryFutureUnitConversionOutcomeV1.NoSource, null),
            { Outcome: InventoryProductUnitConversionOutcomeV1.Denied } => throw new UnauthorizedAccessException(),
            _ => throw new InventoryAllocationException(503, InventoryFutureAllocationCodesV1.ServiceUnavailable),
        };
    }
}
