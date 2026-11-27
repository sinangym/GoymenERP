using System.Globalization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

// Purpose-only projection. Native catalog DTOs and command receipts retain numeric versions.
public sealed record WorkApprovedSourceCatalogPreviewV1(WorkApprovedSourceCatalogViewV1 Catalog,
    ApprovedAccountingSourceCatalogDecisionPackageV1 Package, string ExpectedPackageVersion,
    string ExpectedActiveCatalogVersion, string SourceFactsDigest, string PreviewDigest)
{
    internal static WorkApprovedSourceCatalogPreviewV1 FromNative(ApprovedAccountingSourceCatalogDecisionDetailV1 detail) =>
        new(WorkApprovedSourceCatalogViewV1.FromNative(detail.Catalog), detail.Package,
            detail.ExpectedPackageVersion.ToString(CultureInfo.InvariantCulture),
            detail.ExpectedActiveCatalogVersion.ToString(CultureInfo.InvariantCulture), detail.SourceFactsDigest, detail.PreviewDigest);
}

public sealed record WorkApprovedSourceCatalogViewV1(Guid PackageId, Guid CompanyId, string Status, string Version,
    string? CatalogVersion, string TemplateVersion, string AccountPlanSourceVersion, string AccountsSourceSha256,
    string PostingMappingsSourceSha256, Guid PreparedByUserId, DateTimeOffset PreparedAtUtc,
    Guid? ActivatedByUserId, DateTimeOffset? ActivatedAtUtc, int AccountCount, int BindingCount)
{
    internal static WorkApprovedSourceCatalogViewV1 FromNative(ApprovedAccountingSourceCatalogViewV1 view) =>
        new(view.PackageId, view.CompanyId, view.Status, view.Version.ToString(CultureInfo.InvariantCulture),
            view.CatalogVersion?.ToString(CultureInfo.InvariantCulture), view.TemplateVersion, view.AccountPlanSourceVersion,
            view.AccountsSourceSha256, view.PostingMappingsSourceSha256, view.PreparedByUserId, view.PreparedAtUtc,
            view.ActivatedByUserId, view.ActivatedAtUtc, view.AccountCount, view.BindingCount);
}

public sealed record WorkApprovedSourceCatalogSelectionV1(string ExpectedActiveCatalogVersion);
