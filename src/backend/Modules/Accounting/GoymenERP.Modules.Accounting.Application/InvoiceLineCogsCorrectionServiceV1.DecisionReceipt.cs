using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class InvoiceLineCogsCorrectionServiceV1
{
    public const string CogsApproveCommand = "accounting.invoice-line-cogs.approve.v1";
    public static string CogsApprovalKeyHash(string key) => AccountingApiService.Sha256(key.Trim());
    public static string CogsApprovalPayloadHash(Guid invoiceId, Guid lineId, Guid proposalId, long version) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new { command = CogsApproveCommand, invoiceId, lineId,
            payload = new { proposalId, request = new ApproveInvoiceLineCogsCorrectionV1(version) } }, Json));
    public static string CogsOriginalResultDigest(AccountingApiResult<InvoiceLineCogsCorrectionViewV1> answer) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(answer with { IsReplay = false, OriginalCorrelationId = null }, Json));
    private async Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> AttachCogsReceiptAsync(
        AccountingApiResult<InvoiceLineCogsCorrectionViewV1> answer, Guid invoiceId, Guid lineId, Guid proposalId,
        ApproveInvoiceLineCogsCorrectionV1 request, string key, ApprovalCapture captured, CancellationToken token)
    {
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is null || store is not IInvoiceLineCogsDecisionStoreV1 reader) return answer;
        await ApprovalFenceAsync(captured, token);
        var command = await commands.FindCommandAsync(CogsApproveCommand, CogsApprovalKeyHash(key), token);
        await ApprovalFenceAsync(captured, token);
        var facts = await reader.ReadCogsDecisionAsync(proposalId, ct => ApprovalFenceAsync(captured, ct), token);
        await ApprovalFenceAsync(captured, token);
        var receipt = BindCogsApprovalReceipt(command, facts, answer, invoiceId, lineId, proposalId, request.ExpectedVersion, key, captured.ActorId);
        return answer with { Resource = answer.Resource with { ApprovalReceipt = receipt } };
    }
    public static InvoiceLineCogsApprovalReceiptV1? BindCogsApprovalReceipt(AccountingCommandRecord? command,
        InvoiceLineCogsDecisionSourceV1? facts, AccountingApiResult<InvoiceLineCogsCorrectionViewV1> answer,
        Guid invoiceId, Guid lineId, Guid proposalId, long expectedVersion, string key, Guid actor)
    {
        if (facts?.Proposal is not { } p || !ValidCogsDecisionSource(facts, p.CompanyId, proposalId)
            || p.Status != "Approved" || p.InvoiceId != invoiceId || p.InvoiceLineId != lineId || expectedVersion != 1
            || actor == Guid.Empty || actor == p.PreparedBy || p.ApprovedBy != actor || p.ApprovedAtUtc is not { } approvedAt
            || command is null || command.CommandName != CogsApproveCommand || string.IsNullOrWhiteSpace(key)
            || command.IdempotencyKeyHash != CogsApprovalKeyHash(key) || command.RequestHash != CogsApprovalPayloadHash(invoiceId, lineId, proposalId, expectedVersion)
            || !Guid.TryParse(command.ActorId, out var storedActor) || storedActor != actor || string.IsNullOrWhiteSpace(command.OriginalCorrelationId)) return null;
        AccountingApiResult<InvoiceLineCogsCorrectionViewV1>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>(command.ResultJson, Json);
            if (stored is null || !JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))) return null;
        }
        catch (JsonException) { return null; }
        if (stored.Status != AccountingApiStatus.Ok || stored.Resource is null || stored.IsReplay || stored.OriginalCorrelationId is not null
            || stored.Code is not null || stored.Field is not null || stored.ExpectedVersion is not null || stored.CurrentVersion is not null
            || answer.Status != AccountingApiStatus.Ok || answer.Resource is null || answer.Code is not null || answer.Field is not null
            || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || answer.OriginalCorrelationId != (answer.IsReplay ? command.OriginalCorrelationId : null)
            || JsonSerializer.Serialize(stored.Resource, Json) != JsonSerializer.Serialize(p, Json)
            || JsonSerializer.Serialize(answer.Resource, Json) != JsonSerializer.Serialize(p, Json)) return null;
        return new(p.CompanyId, proposalId, invoiceId, lineId, p.PreparedBy, p.PreparedAtUtc, CogsApproveCommand,
            command.IdempotencyKeyHash, command.RequestHash, actor, 1, 2, command.OriginalCorrelationId, approvedAt,
            CogsSourceFactsDigest(facts), AccountingApiService.Sha256(command.ResultJson));
    }
}
