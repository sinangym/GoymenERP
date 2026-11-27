using System.Globalization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

// Human purpose transport only. Native mapping identity, versions, payload hashes and receipts stay unchanged.
public sealed record WorkPostingAccountMappingPreviewV1(WorkPostingAccountMappingViewV1 Mapping,
    AccountingPostingMappingPlanReferenceV1 PreparedAccountPlan,
    AccountingPostingMappingDecisionPlanV1 CurrentActiveAccountPlan,
    string ExpectedVersion, string ExpectedActiveMappingVersion, string SourceFactsDigest, string PreviewDigest)
{
    internal static WorkPostingAccountMappingPreviewV1 FromNative(AccountingPostingAccountMappingDecisionDetailV1 detail) =>
        new(WorkPostingAccountMappingViewV1.FromNative(detail.Mapping), detail.PreparedAccountPlan,
            detail.CurrentActiveAccountPlan, detail.ExpectedVersion.ToString(CultureInfo.InvariantCulture),
            detail.ExpectedActiveMappingVersion.ToString(CultureInfo.InvariantCulture), detail.SourceFactsDigest, detail.PreviewDigest);
}

public sealed record WorkPostingAccountMappingViewV1(Guid CompanyId, string MappingVersion, DateOnly EffectiveFrom,
    string Status, string Version, string AccountPlanSourceVersion, string AccountPlanSourceSha256,
    Guid PreparedByUserId, DateTimeOffset PreparedAtUtc, Guid? ActivatedByUserId, DateTimeOffset? ActivatedAtUtc,
    IReadOnlyList<AccountingPostingAccountMappingRowViewV1> Rows)
{
    internal static WorkPostingAccountMappingViewV1 FromNative(AccountingPostingAccountMappingViewV1 view) =>
        new(view.CompanyId, view.MappingVersion.ToString(CultureInfo.InvariantCulture), view.EffectiveFrom,
            view.Status, view.Version.ToString(CultureInfo.InvariantCulture), view.AccountPlanSourceVersion,
            view.AccountPlanSourceSha256, view.PreparedByUserId, view.PreparedAtUtc, view.ActivatedByUserId, view.ActivatedAtUtc, view.Rows);
}

public sealed record WorkPostingAccountMappingSelectionV1(string ExpectedActiveMappingVersion);
