using System.Globalization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Purpose-only human view. The native long remains canonical text across JavaScript transport.</summary>
public sealed record WorkNormalJournalDraftPreviewV1(
    WorkNormalJournalDraftViewV1 Draft, string SourceFactsDigest, string PreviewDigest)
{
    internal static WorkNormalJournalDraftPreviewV1 FromNative(NormalJournalDraftDecisionDetailV1 detail)
    {
        var view = detail.Draft;
        return new(new(view.DraftId, view.CompanyId, view.Version.ToString(CultureInfo.InvariantCulture), view.Status,
            view.PreparedByUserId, view.PreparedAtUtc, view.ApprovedByUserId, view.ApprovedAtUtc, view.PeriodId,
            view.PostedJournalId, view.SourceId, view.PostingPurpose, view.PostingRuleVersion, view.AccountPlanSourceVersion,
            view.AccountPlanSourceSha256, view.CurrencyId, view.TransactionDate, view.AccountingDate, view.ValueDate, view.Lines),
            detail.SourceFactsDigest, detail.PreviewDigest);
    }
}

public sealed record WorkNormalJournalDraftViewV1(
    Guid DraftId,
    Guid CompanyId,
    string Version,
    string Status,
    Guid PreparedByUserId,
    DateTimeOffset PreparedAtUtc,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    Guid? PeriodId,
    Guid? PostedJournalId,
    Guid SourceId,
    string PostingPurpose,
    string PostingRuleVersion,
    string AccountPlanSourceVersion,
    string AccountPlanSourceSha256,
    Guid CurrencyId,
    DateOnly TransactionDate,
    DateOnly AccountingDate,
    DateOnly? ValueDate,
    IReadOnlyList<NormalJournalDraftLineViewV1> Lines);
