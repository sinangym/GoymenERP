using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class NormalJournalDraftServiceV1
{
    private async Task<AccountingApiResult<NormalJournalDraftViewV1>> AttachNormalApprovalReceiptAsync(
        AccountingApiResult<NormalJournalDraftViewV1> answer, Guid id, long expectedVersion, Guid periodId,
        string key, Guid company, Guid actor, CancellationToken token)
    {
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is null
            || drafts is not IAccountingNormalJournalDraftDecisionStoreV1 reader) return answer;
        await ReceiptFence(token);
        var command = await commands.FindCommandAsync(NormalJournalCommandNamesV1.ApproveDraft,
            AccountingApiService.Sha256(key.Trim()), token);
        await ReceiptFence(token);
        var source = await reader.ReadNormalDraftDecisionAsync(id, ReceiptFence, token);
        await ReceiptFence(token);
        var proof = source is null || source.CompanyId != company || source.Id != id ? null
            : BindNormalApprovalReceipt(command, source, answer, expectedVersion, periodId, key, actor);
        return answer with { Resource = answer.Resource with { ApprovalReceipt = proof } };
        Task ReceiptFence(CancellationToken cancellationToken) => FenceAsync(permissions.ApprovePermission, actor, cancellationToken, company);
    }

    /// <summary>Only the original persisted result supplies decision fields. Current source supplies retained immutable facts.</summary>
    public static NormalJournalDraftApprovalReceiptV1? BindNormalApprovalReceipt(
        AccountingCommandRecord? command, NormalJournalDraftRecordV1 source,
        AccountingApiResult<NormalJournalDraftViewV1> answer, long expectedVersion, Guid periodId, string key, Guid actor)
    {
        if (command is null || !ValidNormalDecisionRecord(source) || expectedVersion <= 0 || expectedVersion == long.MaxValue || periodId == Guid.Empty
            || actor == Guid.Empty || string.IsNullOrWhiteSpace(key) || source.PreparedByUserId == actor
            || command.CommandName != NormalJournalCommandNamesV1.ApproveDraft
            || command.IdempotencyKeyHash != AccountingApiService.Sha256(key.Trim())
            || !Guid.TryParse(command.ActorId, out var commandActor) || commandActor != actor || string.IsNullOrWhiteSpace(command.OriginalCorrelationId)
            || command.RequestHash != AccountingApiService.Sha256(NormalJournalCommandNamesV1.ApproveDraft + "|"
                + JsonSerializer.Serialize(new { draftId = source.Id, expectedVersion, periodId }, Json))) return null;
        AccountingApiResult<NormalJournalDraftViewV1>? stored;
        try { stored = JsonSerializer.Deserialize<AccountingApiResult<NormalJournalDraftViewV1>>(command.ResultJson, Json); }
        catch (JsonException) { return null; }
        if (stored is not { Status: AccountingApiStatus.Ok, IsReplay: false, Code: null, Resource: { } original }
            || stored.OriginalCorrelationId is not null || stored.Field is not null || stored.ExpectedVersion is not null || stored.CurrentVersion is not null
            || answer.Code is not null || answer.Field is not null || answer.ExpectedVersion is not null || answer.CurrentVersion is not null
            || !answer.IsReplay && answer.OriginalCorrelationId is not null || answer.Status != AccountingApiStatus.Ok || answer.Resource is not { } actual
            || original.CompanyId != source.CompanyId || original.DraftId != source.Id
            || original.Version != expectedVersion + 1 || source.Version < original.Version || original.Status != "Approved"
            || original.ApprovedByUserId != actor || original.ApprovedAtUtc is not { } at
            || original.PeriodId != periodId || original.PostedJournalId != source.Id
            || original.Lines is null || actual.Lines is null
            || NormalViewFactsDigest(original) != NormalViewFactsDigest(View(source))
            || JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(actual, Json)
            || answer.IsReplay && answer.OriginalCorrelationId != command.OriginalCorrelationId) return null;
        return new(source.CompanyId, source.Id, command.CommandName, command.IdempotencyKeyHash, command.RequestHash,
            actor, expectedVersion, original.Version, periodId, source.Id, command.OriginalCorrelationId, at,
            NormalSourceFactsDigest(source), AccountingApiService.Sha256(command.ResultJson));
    }
}
