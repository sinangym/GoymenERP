using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Authorized human facts only. The native Int32 version remains numeric in its sixteen-field proposal.</summary>
public sealed record WorkJournalReversalDecisionPreviewV1(AccountingJournalReversalProposalViewV1 Proposal,
    string SourceFactsDigest, string PreviewDigest);
