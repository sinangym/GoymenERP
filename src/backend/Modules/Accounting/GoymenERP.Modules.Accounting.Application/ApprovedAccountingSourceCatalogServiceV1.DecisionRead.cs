using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class ApprovedAccountingSourceCatalogServiceV1 : IApprovedAccountingSourceCatalogDecisionReadV1
{
    public async Task<AccountingApiResult<ApprovedAccountingSourceCatalogDecisionDetailV1>> ReadDecisionAsync(Guid packageId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        if (packageId == Guid.Empty) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, "packageId");
        if (!companyContext.IsResolved || !TryContext(out var company, out var actor)) return ReadDenied();
        try
        {
            await ActivationFenceAsync(company, actor, cancellationToken);
            await ReadFence(cancellationToken);
            if (catalogs is not IApprovedAccountingSourceCatalogDecisionStoreV1 reader) return ReadUnavailable();
            var source = await reader.ReadActivationDecisionAsync(packageId, ReadFence, cancellationToken);
            await ReadFence(cancellationToken);
            if (source is null) return new(AccountingApiStatus.NotFound, null, false, null);
            if (source.Record.CompanyId != company || source.Record.PackageId != packageId || source.ActiveCatalogVersion < 0
                || !ValidCatalogDecisionRecord(source.Record)) return ReadUnavailable();
            var view = View(source.Record); var package = CatalogDecisionPackage(source.Record.Package);
            return new(AccountingApiStatus.Ok, new(view, package, view.Version, source.ActiveCatalogVersion,
                CatalogSourceFactsDigest(source.Record), CatalogPreviewDigest(view, package, view.Version, source.ActiveCatalogVersion)), false, null);
        }
        catch (NormalJournalAuthorityDeniedException) { return ReadDenied(); }
        catch (AccountingStoreException error) when (error.Code == AccountingStoreCodes.ProviderUnavailable) { return ReadUnavailable(); }
        async Task ReadFence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            await ActivationFenceAsync(company, actor, token); token.ThrowIfCancellationRequested();
        }
        static AccountingApiResult<ApprovedAccountingSourceCatalogDecisionDetailV1> ReadDenied() => new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
        static AccountingApiResult<ApprovedAccountingSourceCatalogDecisionDetailV1> ReadUnavailable() => new(AccountingApiStatus.Unavailable, null, false, null);
    }

    public static ApprovedAccountingSourceCatalogDecisionPackageV1 CatalogDecisionPackage(ApprovedAccountingSourcePackageV1 package) => new(
        [.. package.Mirror.Rows.Select(row => new ApprovedAccountingSourceCatalogDecisionAccountV1(row.Account.AccountId, row.Code, row.Name))],
        [.. package.Bindings.Select(binding => new ApprovedAccountingSourceCatalogDecisionBindingV1(binding.SourceId, binding.SourceKind,
            binding.SourceVersion, binding.OwnerProfileId, binding.OwnerProfileVersion, binding.CurrencyId,
            binding.Rule.PostingPurpose, binding.Rule.Version,
            [.. binding.Rule.Mappings.Select(mapping => new ApprovedAccountingSourceCatalogDecisionMappingV1(mapping.MappingKey, mapping.Side.ToString(), mapping.Account.AccountId))]))]);
    public static string CatalogSourceFactsDigest(ApprovedAccountingSourceCatalogRecordV1 source) => AccountingApiService.Sha256(JsonSerializer.Serialize(new
    {
        source.PackageId, source.CompanyId, source.TemplateVersion, source.AccountPlanSourceVersion,
        source.AccountsSourceSha256, source.PostingMappingsSourceSha256, source.PreparedByUserId, source.PreparedAtUtc,
        Package = CatalogDecisionPackage(source.Package),
    }, Json));
    public static string CatalogPreviewDigest(ApprovedAccountingSourceCatalogViewV1 view, ApprovedAccountingSourceCatalogDecisionPackageV1 package,
        long expectedPackageVersion, long expectedActiveCatalogVersion) => AccountingApiService.Sha256(JsonSerializer.Serialize(new
        { Catalog = view, Package = package, ExpectedPackageVersion = expectedPackageVersion, ExpectedActiveCatalogVersion = expectedActiveCatalogVersion }, Json));

    private static bool ValidCatalogDecisionRecord(ApprovedAccountingSourceCatalogRecordV1 source)
    {
        if (source is null || source.PackageId == Guid.Empty || source.CompanyId == Guid.Empty || source.Version <= 0 || !Enum.IsDefined(source.Status)
            || source.PreparedByUserId == Guid.Empty || source.PreparedAtUtc == default || source.PreparedAtUtc.Offset != TimeSpan.Zero
            || source.TemplateVersion != ApprovedAccountingSourcePackageTemplateV1.Version || !Text(source.AccountPlanSourceVersion)
            || !Sha(source.AccountsSourceSha256) || !Sha(source.PostingMappingsSourceSha256)
            || source.Package is not { Mirror: { } mirror, Bindings: { } bindings } || bindings.Count == 0 || bindings.Any(binding => binding is null)
            || mirror.CompanyId != source.CompanyId || mirror.SourceVersion != source.AccountPlanSourceVersion
            || !string.Equals(mirror.SourceSha256, source.AccountsSourceSha256, StringComparison.OrdinalIgnoreCase)) return false;
        // Prepared has no activation facts; Active/Superseded retain the exact activation facts.
        if (source.Status == ApprovedAccountingSourceCatalogStatusV1.Prepared)
        { if (source.CatalogVersion is not null || source.ActivatedByUserId is not null || source.ActivatedAtUtc is not null) return false; }
        else if (source.CatalogVersion is not > 0 || source.Version <= 1 || source.ActivatedByUserId is not { } activated || activated == Guid.Empty
            || source.ActivatedAtUtc is not { } at || at == default || at.Offset != TimeSpan.Zero) return false;
        return bindings.All(binding => binding.CompanyId == source.CompanyId && binding.SourceId != Guid.Empty
            && NormalJournalSourceKindsV1.IsSupported(binding.SourceKind) && Text(binding.SourceVersion) && Text(binding.OwnerProfileId)
            && Text(binding.OwnerProfileVersion) && binding.CurrencyId != Guid.Empty && binding.Rule is { } rule
            && rule.Mirror.Matches(mirror) && rule.Mappings.Count != 0)
            && bindings.Select(binding => (binding.SourceId, binding.SourceKind, binding.SourceVersion, binding.OwnerProfileId, binding.OwnerProfileVersion, binding.CurrencyId)).Distinct().Count() == bindings.Count;
        static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim();
    }
}
