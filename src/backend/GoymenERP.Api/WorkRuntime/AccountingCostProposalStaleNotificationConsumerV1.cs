using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>YOL-D40: one price-free owner event notifies its immutable preparer through WP2's single store.</summary>
internal sealed class AccountingCostProposalStaleNotificationConsumerV1(IWorkNotificationDeliveryV1 delivery) : IWorkInboxConsumerV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly string[] Fields = ["companyId", "proposalId", "proposalVersion", "preparedBy", "reasonCode", "reasonText", "closedAtUtc", "costKind"];
    public async Task HandleAsync(WorkInboxEventV1 message, CancellationToken cancellationToken)
    {
        AccountingCostProposalStalePayloadV1 value;
        try
        {
            using var json = JsonDocument.Parse(message.PayloadJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var fields = json.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
            if (fields.Length != Fields.Length || !fields.ToHashSet(StringComparer.Ordinal).SetEquals(Fields)) throw new JsonException();
            value = JsonSerializer.Deserialize<AccountingCostProposalStalePayloadV1>(message.PayloadJson, Json) ?? throw new JsonException();
            if (message.SourceName != AccountingCostProposalStaleEventV1.SourceName || message.EventType != AccountingCostProposalStaleEventV1.EventType
                || message.Version != 1 || message.EventId == Guid.Empty || message.CompanyId == Guid.Empty
                || value.CompanyId != message.CompanyId || value.ProposalId == Guid.Empty || value.PreparedBy == Guid.Empty
                || !Enum.IsDefined(value.CostKind) || value.ProposalVersion < 3 || value.ReasonCode != AccountingCostProposalStaleEventV1.ReasonCode
                || value.ReasonText != AccountingCostProposalStaleEventV1.ReasonText || value.ClosedAtUtc != message.OccurredAtUtc
                || value.ClosedAtUtc == default || value.ClosedAtUtc.Offset != TimeSpan.Zero
                || string.IsNullOrWhiteSpace(message.CorrelationId) || message.CorrelationId.Length > 128 || message.CausationId?.Length > 128)
                throw new JsonException();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw new OutboxPermanentFailureException("notification_stale_cost_contract_invalid", "Accounting stale-cost closure evidence is invalid."); }
        await delivery.DeliverAsync(new(message.CompanyId, message.SourceName, message.EventId, message.EventType, message.Version,
            message.PayloadJson, WorkNotificationKindV1.AccountingCostProposalStale, value.ProposalId,
            value.ProposalVersion.ToString(CultureInfo.InvariantCulture), value.ReasonCode, value.ReasonText, value.ClosedAtUtc,
            message.CorrelationId, message.CausationId, [], PreparedByUserId: value.PreparedBy, CostKind: (int)value.CostKind), cancellationToken);
    }
}
