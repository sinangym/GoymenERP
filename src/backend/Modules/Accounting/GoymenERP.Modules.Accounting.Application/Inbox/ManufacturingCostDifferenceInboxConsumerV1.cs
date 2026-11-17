using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;

namespace GoymenERP.Modules.Accounting.Application.Inbox;

/// <summary>Inventory's actual signed TL result becomes a durable, unposted suggestion in Accounting.</summary>
public sealed class ManufacturingCostDifferenceInboxConsumerV1(
    IManufacturingCostDifferenceStoreV1 store, ICompanyContext company) : IWorkInboxConsumerV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken)
    {
        var source = Read(envelope, company);
        await store.AcceptAsync(envelope, source, cancellationToken);
        if (!company.IsResolved || company.CompanyId != source.CompanyId)
            throw new InvalidOperationException("Muhasebe maliyet farkı kabulünde şirket bağlamı değişti.");
    }

    public static ManufacturingCostDifferenceEventV1 Read(WorkInboxEventV1 envelope, ICompanyContext company)
    {
        if (envelope.SourceName != ManufacturingCostDifferenceInboxV1.SourceName
            || envelope.EventType != ManufacturingCostDifferenceInboxV1.EventType || envelope.Version != 1)
            throw Invalid("unsupported_event");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || envelope.CompanyId != company.CompanyId)
            throw Invalid("company_context_mismatch");
        if (envelope.EventId == Guid.Empty || envelope.OccurredAtUtc == default
            || envelope.OccurredAtUtc.Offset != TimeSpan.Zero || !Text(envelope.CorrelationId, 128))
            throw Invalid("invalid_envelope");
        ManufacturingCostDifferenceEventV1? source;
        try { source = JsonSerializer.Deserialize<ManufacturingCostDifferenceEventV1>(envelope.PayloadJson, Json); }
        catch (JsonException) { throw Invalid("invalid_payload"); }
        if (source is null || source.CompanyId != envelope.CompanyId || source.CostDifferenceEventId != envelope.EventId
            || source.ManufacturingOrderId == Guid.Empty || source.GenerationId == Guid.Empty
            || !Version(source.GenerationVersion) || !Version(source.ClosedOrderVersion)
            || source.State is not (3 or 4) || !Text(source.InputFingerprint, 128) || !Text(source.OriginalCorrelationId, 128)
            || source.Outputs is not { Count: > 0 and <= 10000 }) throw Invalid("invalid_payload");
        var outputs = new HashSet<Guid>();
        foreach (var output in source.Outputs)
        {
            if (output is null || output.OutputId == Guid.Empty || output.ReceiptMovementId == Guid.Empty
                || output.LabelId == Guid.Empty || !outputs.Add(output.OutputId) || output.NetQuantityKg <= 0
                || output.RemainingQuantityKg < 0 || output.ConsumedQuantityKg < 0
                || output.NetQuantityKg != output.RemainingQuantityKg + output.ConsumedQuantityKg
                || output.ConsumedTlDifference is null || output.OriginalTlAmount is null
                || output.RemainingTlValue is null || output.TlUnitCostPerKg is null || output.Positions is null)
                throw Invalid("cost_difference_source_unreadable");
            // Recognition and its reversal keep their actual signs; no USD conversion or old-issue rewrite.
            if (source.State == 3 && output.ConsumedTlDifference < 0
                || source.State == 4 && output.ConsumedTlDifference > 0)
                throw Invalid("cost_difference_sign_invalid");
        }
        try { _ = source.Outputs.Sum(output => output.ConsumedTlDifference!.Value); }
        catch (OverflowException) { throw Invalid("cost_difference_amount_unreadable"); }
        return source;
    }

    private static bool Version(string? value) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var parsed) && parsed > 0
        && parsed.ToString(CultureInfo.InvariantCulture) == value;
    private static bool Text(string? value, int length) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= length && value == value.Trim();
    private static OutboxPermanentFailureException Invalid(string code) => new(code, "Üretim maliyet farkı kaynağı doğrulanamadı.");
}
