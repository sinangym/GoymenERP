using System.Globalization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

// Purpose-only projections: native DTOs and command JSON retain their original numeric versions.
public sealed record WorkAccountingPeriodOpeningPreviewV1(WorkAccountingPeriodViewV1 Period,
    string SourceFactsDigest, string PreviewDigest)
{
    internal static WorkAccountingPeriodOpeningPreviewV1 FromNative(AccountingPeriodOpeningDecisionDetailV1 detail) =>
        new(WorkAccountingPeriodViewV1.FromNative(detail.Period), detail.SourceFactsDigest, detail.PreviewDigest);
}
public sealed record WorkAccountingPeriodReopeningPreviewV1(WorkAccountingPeriodReopeningViewV1 Reopening,
    WorkAccountingPeriodViewV1 Period, WorkAccountingPeriodClosureViewV1 Closure, bool LaterPeriodClosed, string SourceFactsDigest, string PreviewDigest)
{
    internal static WorkAccountingPeriodReopeningPreviewV1 FromNative(AccountingPeriodReopeningDecisionDetailV1 detail) =>
        new(WorkAccountingPeriodReopeningViewV1.FromNative(detail.Reopening), WorkAccountingPeriodViewV1.FromNative(detail.Period),
            WorkAccountingPeriodClosureViewV1.FromNative(detail.Closure), detail.LaterPeriodClosed!.Value, detail.SourceFactsDigest, detail.PreviewDigest);
}
public sealed record WorkAccountingPeriodViewV1(Guid PeriodId, Guid CompanyId, DateOnly StartsOn, DateOnly EndsOn,
    string Status, string Version, Guid PreparedByUserId, DateTimeOffset PreparedAtUtc, Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc, Guid? ClosedByUserId, DateTimeOffset? ClosedAtUtc, bool IsOwnProposal)
{
    internal static WorkAccountingPeriodViewV1 FromNative(PeriodViewV1 v) => new(v.PeriodId, v.CompanyId, v.StartsOn,
        v.EndsOn, v.Status, v.Version.ToString(CultureInfo.InvariantCulture), v.PreparedByUserId, v.PreparedAtUtc,
        v.ApprovedByUserId, v.ApprovedAtUtc, v.ClosedByUserId, v.ClosedAtUtc, v.IsOwnProposal);
}
public sealed record WorkAccountingPeriodReopeningViewV1(Guid Id, Guid CompanyId, Guid PeriodId, Guid ClosureId,
    string ClosedPeriodVersion, Guid ProposedBy, DateTimeOffset ProposedAtUtc, string Reason, Guid? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc, string Version)
{
    internal static WorkAccountingPeriodReopeningViewV1 FromNative(AccountingPeriodReopeningViewV1 v) => new(v.Id,
        v.CompanyId, v.PeriodId, v.ClosureId, v.ClosedPeriodVersion.ToString(CultureInfo.InvariantCulture), v.ProposedBy,
        v.ProposedAtUtc, v.Reason, v.ApprovedBy, v.ApprovedAtUtc, v.Version.ToString(CultureInfo.InvariantCulture));
}
public sealed record WorkAccountingPeriodClosureViewV1(Guid Id, Guid CompanyId, Guid PeriodId, string ClosedPeriodVersion,
    Guid TargetPeriodId, Guid CarryVoucherId, string SourceHash, int LineCount, Guid ClosedBy, DateTimeOffset ClosedAtUtc,
    Guid? ReopeningId, Guid? ReopenedBy, DateTimeOffset? ReopenedAtUtc)
{
    internal static WorkAccountingPeriodClosureViewV1 FromNative(AccountingPeriodClosureViewV1 v) => new(v.Id,
        v.CompanyId, v.PeriodId, v.ClosedPeriodVersion.ToString(CultureInfo.InvariantCulture), v.TargetPeriodId,
        v.CarryVoucherId, v.SourceHash, v.LineCount, v.ClosedBy, v.ClosedAtUtc, v.ReopeningId, v.ReopenedBy, v.ReopenedAtUtc);
}
