using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class NormalJournalDraftServiceV1 : INormalJournalDraftDecisionReadV1
{
    public async Task<AccountingApiResult<NormalJournalDraftDecisionDetailV1>> ReadDecisionAsync(
        Guid draftId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        if (draftId == Guid.Empty) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, "draftId");
        if (!HasContext() || ActorGuid() is not { } actor) return DecisionDenied();
        var company = companyContext.CompanyId;
        try
        {
            await ReadFence(cancellationToken);
            if (drafts is not IAccountingNormalJournalDraftDecisionStoreV1 reader)
                return new(AccountingApiStatus.Unavailable, null, false, null);
            var source = await reader.ReadNormalDraftDecisionAsync(draftId, ReadFence, cancellationToken);
            await ReadFence(cancellationToken);
            if (source is null) return new(AccountingApiStatus.NotFound, null, false, null);
            var record = source.Record;
            if (record.CompanyId != company || record.Id != draftId || !ValidNormalDecisionRecord(record)) return new(AccountingApiStatus.Unavailable, null, false, null);
            // ROL-D14/YOL-D23: approval-purpose disclosure excludes the actual draft maker.
            if (record.PreparedByUserId == actor) return DecisionDenied();
            var view = View(record);
            return new(AccountingApiStatus.Ok, new(view, NormalSourceFactsDigest(record), NormalPreviewDigest(view)) { IsCreditSource = source.IsCreditSource }, false, null);
        }
        catch (NormalJournalAuthorityDeniedException) { return DecisionDenied(); }
        async Task ReadFence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await freshConsumerFence(token);
            token.ThrowIfCancellationRequested();
            // The last consumer await must not release a stale successful native permission check.
            await FenceAsync(permissions.ApprovePermission, actor, token, company);
            token.ThrowIfCancellationRequested();
        }
        static AccountingApiResult<NormalJournalDraftDecisionDetailV1> DecisionDenied() =>
            new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
    }

    public static string NormalPreviewDigest(NormalJournalDraftViewV1 view) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(view, Json));

    public static string NormalSourceFactsDigest(NormalJournalDraftRecordV1 record) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new
        {
            record.Id, record.CompanyId, record.PreparedByUserId, record.PreparedAtUtc,
            record.AccountPlanSourceVersion, record.AccountPlanSourceSha256,
            record.AccountPlanApprovedByUserId, record.AccountPlanApprovedAtUtc,
            record.PostingRuleApprovedByUserId, record.PostingRuleApprovedAtUtc, record.Request,
        }, Json));

    private static bool ValidNormalDecisionRecord(NormalJournalDraftRecordV1 record) =>
        record.Id != Guid.Empty && record.CompanyId != Guid.Empty && record.PreparedByUserId != Guid.Empty && record.Version > 0
        && record.Status is NormalJournalDraftStatusV1.Draft or NormalJournalDraftStatusV1.Approved
        && record.Request is { } request && request.JournalId == record.Id && request.Source is { } source && source.CompanyId == record.CompanyId
        && source.SourceId != Guid.Empty && request.CurrencyId != Guid.Empty
        && request.Lines is { Count: >= 2 } && request.Lines.All(line => line.Account is { } account
            && account.CompanyId == record.CompanyId && account.AccountId != Guid.Empty && line.Amount is not null);

    private static string NormalViewFactsDigest(NormalJournalDraftViewV1 view) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new
        {
            view.DraftId, view.CompanyId, view.PreparedByUserId, view.PreparedAtUtc,
            view.SourceId, view.PostingPurpose, view.PostingRuleVersion, view.AccountPlanSourceVersion,
            view.AccountPlanSourceSha256, view.CurrencyId, view.TransactionDate, view.AccountingDate,
            view.ValueDate, view.Lines,
        }, Json));
}
