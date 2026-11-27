using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingSourceVoucherServiceV1
{
    public const string SourceVoucherApproveCommand = "accounting.source-voucher.approve.v1";
    public static string SourceVoucherApprovalKeyHash(string key) => AccountingApiService.Sha256(key.Trim());
    public static string SourceVoucherApprovalPayloadHash(Guid id, long version) => AccountingApiService.Sha256(
        SourceVoucherApproveCommand + "|" + JsonSerializer.Serialize(new { id, version }, Json));
    public static string SourceVoucherOriginalResultDigest(AccountingApiResult<AccountingSourceVoucherViewV1> answer) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(answer with { IsReplay = false, OriginalCorrelationId = null }, Json));
    private async Task<AccountingApiResult<AccountingSourceVoucherViewV1>> AttachSourceVoucherReceiptAsync(
        AccountingApiResult<AccountingSourceVoucherViewV1> answer, Guid id, long expectedVersion, string key,
        ApprovalCapture captured, CancellationToken token, AccountingCommandRecord? originalCommand = null)
    {
        if (answer is not { Status: AccountingApiStatus.Ok, Resource: not null }
            || store is not IAccountingSourceVoucherDecisionStoreV1 reader) return answer;
        await ApprovalFenceAsync(captured, token);
        var command = originalCommand ?? await commands.FindCommandAsync(SourceVoucherApproveCommand, SourceVoucherApprovalKeyHash(key), token);
        await ApprovalFenceAsync(captured, token);
        AccountingSourceVoucherDecisionSourceV1? source;
        try { source = await reader.ReadSourceVoucherDecisionAsync(id, ct => ApprovalFenceAsync(captured, ct), token); }
        catch (AccountingStoreException) { await ApprovalFenceAsync(captured, token); return answer; }
        await ApprovalFenceAsync(captured, token);
        return answer with { Resource = answer.Resource with { ApprovalReceipt = BindSourceVoucherApprovalReceipt(
            command, source, answer, id, expectedVersion, key, captured.ActorId) } };
    }
    public static AccountingSourceVoucherApprovalReceiptV1? BindSourceVoucherApprovalReceipt(AccountingCommandRecord? command,
        AccountingSourceVoucherDecisionSourceV1? source, AccountingApiResult<AccountingSourceVoucherViewV1> answer,
        Guid id, long expectedVersion, string key, Guid actor)
    {
        if (source?.StoredVoucher is not { } current || !ValidSourceVoucherSource(source, current.CompanyId, id)
            || expectedVersion != 1 || actor == Guid.Empty || actor == current.SourceActorId || string.IsNullOrWhiteSpace(key)
            || command is null || command.CommandName != SourceVoucherApproveCommand
            || command.IdempotencyKeyHash != SourceVoucherApprovalKeyHash(key)
            || command.RequestHash != SourceVoucherApprovalPayloadHash(id, expectedVersion)
            || !Guid.TryParse(command.ActorId, out var storedActor) || storedActor != actor
            || string.IsNullOrWhiteSpace(command.OriginalCorrelationId) || !source.OriginalJournalBound
            || source.OriginalJournalCorrelationId != command.OriginalCorrelationId) return null;
        AccountingApiResult<AccountingSourceVoucherViewV1>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<AccountingApiResult<AccountingSourceVoucherViewV1>>(command.ResultJson, Json);
            if (stored is null || !JsonNode.DeepEquals(JsonNode.Parse(command.ResultJson), JsonSerializer.SerializeToNode(stored, Json))) return null;
        }
        catch (JsonException) { return null; }
        if (stored is not { Status: AccountingApiStatus.Ok, Resource: { } original, IsReplay: false, Code: null, Field: null,
                ExpectedVersion: null, CurrentVersion: null, OriginalCorrelationId: null }
            || answer is not { Status: AccountingApiStatus.Ok, Resource: not null, Code: null, Field: null, ExpectedVersion: null, CurrentVersion: null }
            || original.Id != id || original.CompanyId != current.CompanyId || original.Status != "Posted" || original.Version != expectedVersion + 1
            || original.ApprovedBy != actor || original.ApprovedBy != current.ApprovedBy || original.ApprovedAtUtc != current.ApprovedAtUtc
            || original.ApprovedAtUtc is not { } approvedAt || original.JournalId != id || original.JournalId != current.JournalId
            || original.PeriodId != current.PeriodId || original.PeriodId is null || original.CancellationReason is not null
            || original.CancellationId is not null || original.ReversalProposal is not null
            || JsonSerializer.Serialize(CommercialVoucher(original), Json) != JsonSerializer.Serialize(CommercialVoucher(current), Json)
            || SourceVoucherOriginalResultDigest(answer) != SourceVoucherOriginalResultDigest(stored)
            || answer.OriginalCorrelationId != (answer.IsReplay ? command.OriginalCorrelationId : null)) return null;
        // Period readiness and cancellation are current presentation only, never original receipt evidence.
        return new(current.CompanyId, id, current.SourceDocumentId, current.InvoiceId, current.SourceActorId, source.CreatedAtUtc,
            command.CommandName, command.IdempotencyKeyHash, command.RequestHash, actor, expectedVersion, original.Version,
            id, command.OriginalCorrelationId, approvedAt, SourceVoucherSourceFactsDigest(source), SourceVoucherOriginalResultDigest(stored));
    }
}
