using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

public sealed record RecordManufacturingNonKgInputRequestV1(Guid FactId, long ExpectedVersion, long ExpectedRevision,
    string Kind, string? Item, string Unit, string? Quantity, DateTimeOffset? OccurredAtUtc, Guid? ReplacesFactId, string? Reason);
public sealed record ManufacturingNonKgInputHistoryV1(Guid CompanyId, Guid OrderId, long OrderVersion, long Revision,
    IReadOnlyList<ManufacturingNonKgInputConsumptionV1> Facts)
{
    public void RequireValid()
    {
        if (CompanyId == Guid.Empty || OrderId == Guid.Empty || OrderVersion < 1 || Facts is null || Revision != Facts.Count)
            throw new ArgumentException("Gerçek sarf geçmişi geçersiz.");
        var seen = new Dictionary<Guid, ManufacturingNonKgInputConsumptionV1>(); var replaced = new HashSet<Guid>();
        for (var index = 0; index < Facts.Count; index++)
        {
            var fact = Facts[index]; fact.RequireValid();
            if (fact.Revision != index + 1 || !seen.TryAdd(fact.FactId, fact)) throw new ArgumentException("Gerçek sarf geçmişi sıralı değil.");
            if (fact.ReplacesFactId is { } original && (!seen.TryGetValue(original, out var previous) || !replaced.Add(original)
                || previous.Kind != fact.Kind || previous.Unit != fact.Unit)) throw new ArgumentException("Gerçek sarf düzeltme bağı geçersiz.");
        }
    }
}

public interface IManufacturingNonKgInputStoreV1
{
    Task<ManufacturingNonKgInputHistoryV1?> ReadNonKgInputsAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<ManufacturingNonKgInputHistoryV1> RecordNonKgInputAsync(Guid orderId, long expectedVersion, long expectedRevision,
        ManufacturingNonKgInputConsumptionV1 fact, ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public sealed partial class ManufacturingCommandService
{
    public async Task<ManufacturingApiResult<ManufacturingNonKgInputHistoryV1>> GetNonKgInputsAsync(Guid orderId, CancellationToken token)
    {
        if (orderId == Guid.Empty) return Invalid<ManufacturingNonKgInputHistoryV1>("orderId");
        if (!HasContext() || ActorGuid() is not { } actor) return Denied<ManufacturingNonKgInputHistoryV1>();
        var company = companyContext.CompanyId;
        if (await FenceCapturedAsync<ManufacturingNonKgInputHistoryV1>(ErpPermissions.ManufacturingOrdersRead, company, actor, token) is { } denied) return denied;
        var order = await orders.FindOrderAsync(orderId, token);
        if (order is null) return NotFoundResult<ManufacturingNonKgInputHistoryV1>();
        if (nonKgInputs is null) return new(ManufacturingApiStatus.Unavailable, null, false, null);
        ManufacturingNonKgInputHistoryV1? history;
        try
        {
            history = await nonKgInputs.ReadNonKgInputsAsync(orderId, token);
            if (history is null || history.CompanyId != company || history.OrderId != orderId || history.OrderVersion != order.Version)
                return new(ManufacturingApiStatus.Unavailable, null, false, null);
            history.RequireValid();
            if ((await orders.FindOrderAsync(orderId, token))?.Version != order.Version) return new(ManufacturingApiStatus.Unavailable, null, false, null);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new(ManufacturingApiStatus.Unavailable, null, false, null); }
        if (await FenceCapturedAsync<ManufacturingNonKgInputHistoryV1>(ErpPermissions.ManufacturingOrdersRead, company, actor, token) is { } finalDenied) return finalDenied;
        return new(ManufacturingApiStatus.Ok, history, false, null);
    }

    public async Task<ManufacturingApiResult<ManufacturingNonKgInputHistoryV1>> RecordNonKgInputAsync(Guid orderId,
        RecordManufacturingNonKgInputRequestV1? request, string key, CancellationToken token)
    {
        if (request is null || orderId == Guid.Empty || request.ExpectedVersion < 1 || request.ExpectedRevision < 0
            || !Enum.TryParse<ManufacturingNonKgInputKindV1>(request.Kind, false, out var kind) || !Enum.IsDefined(kind) || kind.ToString() != request.Kind
            || !Enum.TryParse<ManufacturingNonKgInputUnitV1>(request.Unit, false, out var unit) || !Enum.IsDefined(unit) || unit.ToString() != request.Unit
            || request.OccurredAtUtc is not { } occurred || occurred.Offset != TimeSpan.Zero)
            return Invalid<ManufacturingNonKgInputHistoryV1>("nonKgInput");
        ManufacturingNonKgInputConsumptionV1 fact;
        try
        {
            fact = new(request.FactId, checked(request.ExpectedRevision + 1), kind, request.Item?.Trim() ?? "", unit,
                request.Quantity ?? "", occurred, request.ReplacesFactId, request.Reason?.Trim() ?? "");
            fact.RequireValid();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) { return Invalid<ManufacturingNonKgInputHistoryV1>("nonKgInput"); }
        return await RunAsync<ManufacturingNonKgInputHistoryV1>(ErpPermissions.ManufacturingFactsRecord, key,
            "manufacturing.order.non-kg-input-record.v1", new { orderId, request.ExpectedVersion, request.ExpectedRevision, fact },
            ManufacturingApiStatus.Ok, async (metadata, _, _) =>
            {
                if (await orders.FindOrderAsync(orderId, token) is null) return NotFoundResult<ManufacturingNonKgInputHistoryV1>();
                if (nonKgInputs is null) return new(ManufacturingApiStatus.Unavailable, null, false, null);
                return new(ManufacturingApiStatus.Ok, await nonKgInputs.RecordNonKgInputAsync(orderId, request.ExpectedVersion,
                    request.ExpectedRevision, fact, metadata, token), false, null);
            }, stored => (ManufacturingNonKgInputHistoryV1)stored, token);
    }
}
