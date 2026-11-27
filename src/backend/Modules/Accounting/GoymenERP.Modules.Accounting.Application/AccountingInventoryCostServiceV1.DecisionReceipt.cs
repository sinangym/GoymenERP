using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingInventoryCostServiceV1
{
    public const string InventoryCostApproveCommand = "accounting.inventory-cost.approve.v1";
    public static string InventoryCostApprovalKeyHash(string key) => AccountingApiService.Sha256(key.Trim());
    public static string InventoryCostApprovalPayloadHash(Guid id, AccountingInventoryCostKindV1 kind, long version) =>
        AccountingApiService.Sha256(InventoryCostApproveCommand + "|" + JsonSerializer.Serialize(new { id, kind, version }, Json));
    public static string InventoryCostOriginalResultDigest(AccountingApiResult<AccountingInventoryCostProposalV1> answer) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(answer with { IsReplay = false, OriginalCorrelationId = null }, Json));
    private async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> AttachInventoryCostReceiptAsync(
        AccountingApiResult<AccountingInventoryCostProposalV1> answer, Guid id, AccountingInventoryCostKindV1 kind,
        long version, string key, ApprovalCapture captured, CancellationToken token)
    {
        // A real Stale closure is a durable native 409, not an approval success.
        if (answer.Resource is null || answer.Status is not (AccountingApiStatus.Ok or AccountingApiStatus.Conflict)
            || version == long.MaxValue || store is not IAccountingInventoryCostDecisionStoreV1 reader) return answer;
        await ApprovalFenceAsync(captured, token);
        var command = await commands.FindCommandAsync(InventoryCostApproveCommand, InventoryCostApprovalKeyHash(key), token);
        await ApprovalFenceAsync(captured, token);
        AccountingInventoryCostDecisionSourceV1? facts;
        try { facts = await reader.ReadInventoryCostDecisionAsync(id, kind, version + 1, ct => ApprovalFenceAsync(captured, ct), token); }
        catch (AccountingStoreException) { await ApprovalFenceAsync(captured, token); return answer; }
        await ApprovalFenceAsync(captured, token);
        var receipt = BindInventoryCostApprovalReceipt(command, facts, answer, id, kind, version, key, captured.ActorId);
        return answer with { Resource = answer.Resource with { ApprovalReceipt = receipt } };
    }
    public static AccountingInventoryCostApprovalReceiptV1? BindInventoryCostApprovalReceipt(AccountingCommandRecord? command,
        AccountingInventoryCostDecisionSourceV1? source, AccountingApiResult<AccountingInventoryCostProposalV1> answer,
        Guid id, AccountingInventoryCostKindV1 kind, long expectedVersion, string key, Guid actor)
    {
        if (source?.Proposal is not { } p || !ValidInventoryCostDecisionSource(source, p.CompanyId, id, kind)
            || p.Status == "Prepared" || expectedVersion < 2 || expectedVersion == long.MaxValue || p.Version != expectedVersion + 1
            || source.Prepared.Version != expectedVersion || actor == Guid.Empty || actor == source.Prepared.Preparation!.PreparedBy
            || command is null || command.CommandName != InventoryCostApproveCommand || string.IsNullOrWhiteSpace(key)
            || command.IdempotencyKeyHash != InventoryCostApprovalKeyHash(key) || command.RequestHash != InventoryCostApprovalPayloadHash(id, kind, expectedVersion)
            || !Guid.TryParse(command.ActorId, out var storedActor) || storedActor != actor || string.IsNullOrWhiteSpace(command.OriginalCorrelationId)
            || p.Status == "Posted" && source.JournalCorrelationId != command.OriginalCorrelationId
            || p.Status == "Stale" && p.StaleClosure?.CorrelationId != command.OriginalCorrelationId
            || p.Status != "Stale" && p.ApprovedBy != actor) return null;
        AccountingApiResult<AccountingInventoryCostProposalV1>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<AccountingApiResult<AccountingInventoryCostProposalV1>>(command.ResultJson, Json);
            if (stored is null || !JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))) return null;
        }
        catch (JsonException) { return null; }
        var expected = Result(p);
        if (stored.IsReplay || stored.OriginalCorrelationId is not null || stored.Resource is null
            || JsonSerializer.Serialize(stored, Json) != JsonSerializer.Serialize(expected, Json)
            || InventoryCostOriginalResultDigest(answer) != AccountingApiService.Sha256(JsonSerializer.Serialize(stored, Json))
            || answer.OriginalCorrelationId != (answer.IsReplay ? command.OriginalCorrelationId : null)) return null;
        var preparation = source.Prepared.Preparation!;
        return new(p.CompanyId, id, kind, p.EventId, p.InvoiceId, preparation.PreparedBy, preparation.PreparedAtUtc,
            source.PreparedRecordedAtUtc, command.CommandName, command.IdempotencyKeyHash, command.RequestHash, actor,
            expectedVersion, p.Version, p.Status, command.OriginalCorrelationId,
            p.Status == "Stale" ? p.StaleClosure!.ClosedAtUtc : p.ApprovedAtUtc!.Value,
            InventoryCostSourceFactsDigest(source), InventoryCostOriginalResultDigest(stored));
    }
}
