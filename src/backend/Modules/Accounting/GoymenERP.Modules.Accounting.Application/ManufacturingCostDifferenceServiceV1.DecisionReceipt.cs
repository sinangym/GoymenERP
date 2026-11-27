using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class ManufacturingCostDifferenceServiceV1
{
    public static string CostDifferenceApprovalKeyHash(string key) => AccountingApiService.Sha256(key.Trim());
    public static string CostDifferenceApprovalPayloadHash(Guid id, long version) => AccountingApiService.Sha256(
        ManufacturingCostDifferenceCommandNamesV1.Approve + "|" + JsonSerializer.Serialize(new { id, version }, Json));
    public static string CostDifferenceOriginalResultDigest(AccountingApiResult<ManufacturingCostDifferenceViewV1> answer) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(answer with { IsReplay = false, OriginalCorrelationId = null }, Json));

    private async Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> AttachCostDifferenceReceiptAsync(
        AccountingApiResult<ManufacturingCostDifferenceViewV1> answer, Guid id, long expectedVersion, string key,
        ApprovalCapture captured, CancellationToken token)
    {
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is null || store is not IManufacturingCostDifferenceDecisionStoreV1 reader) return answer;
        await ApprovalFenceAsync(captured, token);
        var command = await commands.FindCommandAsync(ManufacturingCostDifferenceCommandNamesV1.Approve, CostDifferenceApprovalKeyHash(key), token);
        await ApprovalFenceAsync(captured, token);
        var source = await reader.ReadCostDifferenceDecisionAsync(id, ct => ApprovalFenceAsync(captured, ct), token);
        await ApprovalFenceAsync(captured, token);
        var receipt = source is null || !ValidCostDifferenceSource(source, captured.CompanyId, id) ? null
            : BindCostDifferenceApprovalReceipt(command, source, answer, expectedVersion, key, captured.ActorId);
        return answer with { Resource = answer.Resource with { ApprovalReceipt = receipt } };
    }

    public static ManufacturingCostDifferenceApprovalReceiptV1? BindCostDifferenceApprovalReceipt(AccountingCommandRecord? command,
        ManufacturingCostDifferenceDecisionSourceV1 source, AccountingApiResult<ManufacturingCostDifferenceViewV1> answer,
        long expectedVersion, string key, Guid actor)
    {
        if (source.Proposal is not { } p || expectedVersion < 1 || expectedVersion == long.MaxValue
            || string.IsNullOrWhiteSpace(key) || actor == Guid.Empty || p.PreparedBy == actor
            || !ValidCostDifferenceSource(source, p.CompanyId, p.ProposalId) || p.Status != "Posted" || command is null
            || command.CommandName != ManufacturingCostDifferenceCommandNamesV1.Approve
            || command.IdempotencyKeyHash != CostDifferenceApprovalKeyHash(key)
            || command.RequestHash != CostDifferenceApprovalPayloadHash(p.ProposalId, expectedVersion)
            || !Guid.TryParse(command.ActorId, out var storedActor) || storedActor != actor
            || string.IsNullOrWhiteSpace(command.OriginalCorrelationId) || command.OriginalCorrelationId != source.JournalCorrelationId) return null;
        AccountingApiResult<ManufacturingCostDifferenceViewV1>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<AccountingApiResult<ManufacturingCostDifferenceViewV1>>(command.ResultJson, Json);
            if (!JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))) return null;
        }
        catch (JsonException) { return null; }
        if (stored is not { Status: AccountingApiStatus.Ok, Resource: { } original, IsReplay: false, Code: null, Field: null,
                ExpectedVersion: null, CurrentVersion: null, OriginalCorrelationId: null }
            || answer is not { Status: AccountingApiStatus.Ok, Resource: not null, Code: null, Field: null, ExpectedVersion: null, CurrentVersion: null }
            || (answer.IsReplay ? answer.OriginalCorrelationId != command.OriginalCorrelationId : answer.OriginalCorrelationId is not null)
            || original.Status != "Posted" || original.Version != expectedVersion + 1 || original.ApprovedBy != actor
            || original.JournalId is not { } journal || journal == Guid.Empty
            || JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(p, Json)
            || JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(answer.Resource, Json)) return null;
        return new(p.CompanyId, p.ProposalId, journal, p.PreparedBy!.Value, source.PreparedAtUtc!.Value,
            command.CommandName, command.IdempotencyKeyHash, command.RequestHash, actor, expectedVersion, original.Version,
            command.OriginalCorrelationId, source.ApprovedAtUtc!.Value, CostDifferenceSourceFactsDigest(source), AccountingApiService.Sha256(command.ResultJson));
    }
}
