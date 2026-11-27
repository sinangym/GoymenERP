using System.Text.Json;
using System.Text.Json.Nodes;
namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingInventoryCostServiceV1
{
    public const string InventoryCostCancellationApproveCommand = "accounting.inventory-cost.approve-component-cancellation.v1";
    public static string CancellationApprovalPayloadHash(Guid id, ApproveInventoryCostCancellationV1 request) =>
        AccountingInventoryCostSourceValidationV1.Sha256(InventoryCostCancellationApproveCommand + "|" + JsonSerializer.Serialize(new { id, request }, Json));
    private async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> AttachCancellationReceiptAsync(
        AccountingApiResult<AccountingInventoryCostProposalV1> answer, Guid id, ApproveInventoryCostCancellationV1 request,
        string key, ApprovalCapture captured, CancellationToken token)
    {
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is null || store is not IAccountingInventoryCostCancellationDecisionStoreV1 reader) return answer;
        await ApprovalFenceAsync(captured, token);
        var command = await commands.FindCommandAsync(InventoryCostCancellationApproveCommand, InventoryCostApprovalKeyHash(key), token);
        await ApprovalFenceAsync(captured, token);
        AccountingInventoryCostCancellationDecisionDetailV1? detail;
        try { detail = await reader.ReadCancellationDecisionAsync(id, request.ExpectedCancellationVersion, ct => ApprovalFenceAsync(captured, ct), token); }
        catch (AccountingStoreException) { await ApprovalFenceAsync(captured, token); return answer; }
        await ApprovalFenceAsync(captured, token);
        return answer with { Resource = answer.Resource with { CancellationApprovalReceipt = BindCancellationApprovalReceipt(command, detail, answer, id, request, key, captured.ActorId) } };
    }
    public static AccountingInventoryCostCancellationApprovalReceiptV1? BindCancellationApprovalReceipt(AccountingCommandRecord? command,
        AccountingInventoryCostCancellationDecisionDetailV1? detail, AccountingApiResult<AccountingInventoryCostProposalV1> answer,
        Guid id, ApproveInventoryCostCancellationV1 request, string key, Guid actor)
    {
        if (!ValidCancellationDecisionDetail(detail) || detail is null || command is null || string.IsNullOrWhiteSpace(key)
            || detail.OriginalProposal.ProposalId != id || detail.OriginalProposal.Version != request.ExpectedVersion
            || detail.PreparedCancellation.Version != request.ExpectedCancellationVersion || request.ExpectedCancellationVersion == long.MaxValue
            || detail.Cancellation.Status != "Approved" || detail.Cancellation.Version != request.ExpectedCancellationVersion + 1
            || detail.Cancellation.ApprovedBy != actor || actor == Guid.Empty || actor == detail.PreparedCancellation.PreparedBy
            || command.CommandName != InventoryCostCancellationApproveCommand || command.IdempotencyKeyHash != InventoryCostApprovalKeyHash(key)
            || command.RequestHash != CancellationApprovalPayloadHash(id, request) || !Guid.TryParse(command.ActorId, out var originalActor) || originalActor != actor
            || string.IsNullOrWhiteSpace(command.OriginalCorrelationId)) return null;
        AccountingApiResult<AccountingInventoryCostProposalV1>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<AccountingApiResult<AccountingInventoryCostProposalV1>>(command.ResultJson, Json);
            if (stored is null || !JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))) return null;
        }
        catch (JsonException) { return null; }
        var original = detail.OriginalProposal with { Status = "CancelledByComponentReversal", CancellationId = detail.Cancellation.CancellationId, ComponentCancellation = detail.Cancellation };
        if (JsonSerializer.Serialize(stored, Json) != JsonSerializer.Serialize(Result(original), Json)
            || InventoryCostOriginalResultDigest(answer) != InventoryCostOriginalResultDigest(stored)
            || answer.OriginalCorrelationId != (answer.IsReplay ? command.OriginalCorrelationId : null)) return null;
        var before = detail.PreparedCancellation;
        return new(original.CompanyId, id, before.Id, before.CancellationId, command.CommandName, command.IdempotencyKeyHash,
            command.RequestHash, actor, request.ExpectedVersion, request.ExpectedCancellationVersion, original.Version, detail.Cancellation.Version,
            before.PreparedBy, before.PreparedAtUtc, detail.PreparedRecordedAtUtc, detail.Cancellation.ApprovedAtUtc!.Value,
            command.OriginalCorrelationId, detail.SourceFactsDigest, InventoryCostOriginalResultDigest(stored));
    }
}
