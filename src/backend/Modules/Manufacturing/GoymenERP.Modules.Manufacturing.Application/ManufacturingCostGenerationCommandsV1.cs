using GoymenERP.BuildingBlocks.Security;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Manufacturing.Application;

public sealed record ManufacturingCostGenerationHistoryV1(Guid OrderId, long OrderVersion,
    IReadOnlyList<ManufacturingCostGenerationViewV1> Generations);
public sealed record ManufacturingCostQuantityRatioV1(string Numerator, string Denominator);
public sealed record ManufacturingCostGenerationOutputViewV1(Guid OutputId, Guid LabelId, string NetKg, string? RemainingKg,
    string? ConsumedKg, string? OriginalTlAmount, string? RemainingTlValue, string? ConsumedTlDifference, string? UsdUnitCostPerKg,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManufacturingCostQuantityRatioV1? RemainingKgRatio = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManufacturingCostQuantityRatioV1? ConsumedKgRatio = null);
public sealed record ManufacturingCostGenerationViewV1(Guid CompanyId, Guid OrderId, Guid GenerationId, long ClosedOrderVersion,
    long Revision, string InventoryVersion, string State, string InputFingerprint, string CalculatorVersion,
    DateTimeOffset RecordedAtUtc, string Reason, string CorrelationId, Guid? CostDifferenceEventId,
    IReadOnlyList<ManufacturingCostGenerationOutputViewV1> Outputs);

public sealed partial class ManufacturingCommandService
{
    public async Task<ManufacturingApiResult<ManufacturingCostGenerationHistoryV1>> GetCostGenerationsAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty) return Invalid<ManufacturingCostGenerationHistoryV1>("orderId");
        if (!HasContext() || ActorGuid() is not { } actor) return Denied<ManufacturingCostGenerationHistoryV1>();
        var companyId = companyContext.CompanyId;
        if (await FenceCapturedAsync<ManufacturingCostGenerationHistoryV1>(ErpPermissions.ManufacturingReportsRead, companyId, actor, cancellationToken) is { } denied) return denied;
        var order = await orders.FindOrderAsync(orderId, cancellationToken);
        if (order is null) return NotFoundResult<ManufacturingCostGenerationHistoryV1>();
        if (costGenerations is null) return new(ManufacturingApiStatus.Unavailable, null, false, null);
        var result = await costGenerations.ReadCostGenerationsAsync(orderId, cancellationToken);
        if (await FenceCapturedAsync<ManufacturingCostGenerationHistoryV1>(ErpPermissions.ManufacturingReportsRead, companyId, actor, cancellationToken) is { } finalDenied) return finalDenied;
        if (result.Any(item => item.CompanyId != companyId || item.OrderId != orderId)
            || (await orders.FindOrderAsync(orderId, cancellationToken))?.Version != order.Version) return new(ManufacturingApiStatus.Unavailable, null, false, null);
        if (await FenceCapturedAsync<ManufacturingCostGenerationHistoryV1>(ErpPermissions.ManufacturingReportsRead, companyId, actor, cancellationToken) is { } releaseDenied) return releaseDenied;
        return new(ManufacturingApiStatus.Ok, new(orderId, order.Version, result.Select(GenerationView).ToArray()), false, null);
    }

    /// <summary>URT-D58/67: finish a closed order's pending cost effect; this neither prepares nor approves its SMM voucher.</summary>
    public async Task<ManufacturingApiResult<ManufacturingCostGenerationViewV1>> RecognizeCostAsync(Guid orderId,
        RecognizeManufacturingOrderCostRequestV1? request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || request is null || request.ExpectedVersion < 2 || request.ExpectedGenerationRevision < 0)
            return Invalid<ManufacturingCostGenerationViewV1>("expectedVersion");
        if (DecisionReason(request.Reason) is not { } reason) return Invalid<ManufacturingCostGenerationViewV1>("reason");
        if (!HasContext() || ActorGuid() is not { } actor) return Denied<ManufacturingCostGenerationViewV1>();
        var companyId = companyContext.CompanyId;
        if (await FenceCapturedAsync<ManufacturingCostGenerationViewV1>(ErpPermissions.ManufacturingReportsRead, companyId, actor, cancellationToken) is { } denied) return denied;
        var answer = await RunAsync<ManufacturingCostGenerationViewV1>(ErpPermissions.ManufacturingOrdersClose, idempotencyKey,
            "manufacturing.order.cost-recognize.v1", new { orderId, request.ExpectedVersion, request.ExpectedGenerationRevision, reason },
            ManufacturingApiStatus.Ok, async (metadata, _, _) =>
            {
                var order = await orders.FindOrderAsync(orderId, cancellationToken);
                if (order is null) return NotFoundResult<ManufacturingCostGenerationViewV1>();
                if (order.Version != request.ExpectedVersion) throw new GoymenERP.Modules.Manufacturing.Domain.Common.ManufacturingVersionConflictException(request.ExpectedVersion, order.Version);
                if (await FenceCapturedAsync<ManufacturingCostGenerationViewV1>(ErpPermissions.ManufacturingReportsRead, companyId, actor, cancellationToken) is { } writeDenied) return writeDenied;
                if (costRecognition is null) return new(ManufacturingApiStatus.Unavailable, null, false, null);
                var result = await costRecognition.RecognizeAsync(orderId, request.ExpectedVersion, request.ExpectedGenerationRevision, reason, metadata, cancellationToken);
                return new(ManufacturingApiStatus.Ok, GenerationView(result), false, null);
            }, stored => GenerationView((ManufacturingCostGenerationV1)stored), cancellationToken, storedAnswerPermission: ErpPermissions.ManufacturingReportsRead);
        return await FenceCapturedAsync<ManufacturingCostGenerationViewV1>(ErpPermissions.ManufacturingReportsRead, companyId, actor, cancellationToken) is { } releaseDenied ? releaseDenied : answer;
    }

    private static ManufacturingCostGenerationViewV1 GenerationView(ManufacturingCostGenerationV1 row)
    {
        using var document = JsonDocument.Parse(row.ResultJson); var root = document.RootElement;
        var outputs = root.GetProperty("outputs").EnumerateArray().Select(output => new ManufacturingCostGenerationOutputViewV1(
            output.GetProperty("outputId").GetGuid(), output.GetProperty("labelId").GetGuid(), Number(output, "netQuantityKg")!,
            Number(output, "remainingQuantityKg"), Number(output, "consumedQuantityKg"), Number(output, "originalTlAmount"),
            Number(output, "remainingTlValue"), Number(output, "consumedTlDifference"), Number(output, "usdUnitCostPerKg"),
            Ratio(output, "remainingQuantityKg", "remainingQuantityKgRatio"), Ratio(output, "consumedQuantityKg", "consumedQuantityKgRatio"))).ToArray();
        var effectId = root.TryGetProperty("costDifferenceEventId", out var effect) && effect.ValueKind == JsonValueKind.String ? effect.GetGuid() : (Guid?)null;
        return new(row.CompanyId, row.OrderId, row.GenerationId, row.ClosedOrderVersion, row.Revision, row.InventoryVersion,
            row.State.ToString(), row.InputFingerprint, row.CalculatorVersion, row.RecordedAtUtc, row.Reason, row.CorrelationId, effectId, outputs);
        static string? Number(JsonElement value, string name) => value.TryGetProperty(name, out var number) && number.ValueKind != JsonValueKind.Null
            ? number.GetDecimal().ToString("0.############################", CultureInfo.InvariantCulture) : null;
        static ManufacturingCostQuantityRatioV1? Ratio(JsonElement value, string scalarName, string ratioName)
        {
            var scalar = value.GetProperty(scalarName).ValueKind == JsonValueKind.Null ? (decimal?)null : value.GetProperty(scalarName).GetDecimal();
            var ratio = value.TryGetProperty(ratioName, out var field) && field.ValueKind != JsonValueKind.Null
                ? JsonSerializer.Deserialize<InventoryExactCostRatioV1>(field.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null;
            var exact = InventoryExactCostRatioV1.Resolve(scalar, ratio);
            if (exact.Sign < 0) throw new InvalidOperationException("Maliyet neslinin miktar kanıtı geçersiz.");
            return ratio is null ? null : new(ratio.Numerator, ratio.Denominator);
        }
    }
}
