using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>One owner formatter for pending, human GET and genuine original activation receipt matching.</summary>
public static class AccountingApprovedSourceCatalogDecisionBindingV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.approved-source-catalog.v1";
    public const string PurposePath = "/onaylarim/accounting.approved-source-catalog.v1";
    public const string ActivationRequiredRule = "accounting.approved-source-catalog-activation-required";
    public static WorkInboxItemV1? Item(Guid packageId, Guid preparedBy, DateTimeOffset preparedAtUtc)
    {
        if (packageId == Guid.Empty || preparedBy == Guid.Empty || preparedAtUtc == default || preparedAtUtc.Offset != TimeSpan.Zero) return null;
        var caption = packageId.ToString("D", CultureInfo.InvariantCulture)[..8];
        return new(ModuleName, KindName, packageId, caption, preparedAtUtc, PurposePath + "/" + packageId.ToString("D", CultureInfo.InvariantCulture),
            preparedBy.ToString("D", CultureInfo.InvariantCulture),
            [new(ActivationRequiredRule, null, null, null, null)], [new("approved-source-catalog-package", caption, packageId, null)]);
    }
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkApprovedSourceCatalogProofV1? proof) =>
        proof is not null && proof.CompanyId != Guid.Empty && proof.PackageVersion > 0 && proof.ActiveCatalogVersion >= 0
        && proof.Status is "Prepared" or "Active" or "Superseded" && Digest(proof.SourceFactsDigest) && Digest(proof.PreviewDigest)
        && Item(proof.PackageId, proof.PreparedBy, proof.PreparedAtUtc) is { } expected && SameItem(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, ApprovedAccountingSourceCatalogDecisionDetailV1? detail) =>
        ApprovedAccountingSourceCatalogServiceV1.ValidCatalogDecisionDetail(detail)
        && Item(detail!.Catalog.PackageId, detail.Catalog.PreparedByUserId, detail.Catalog.PreparedAtUtc) is { } expected && SameItem(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, ApprovedAccountingSourceCatalogViewV1? original,
        ApprovedAccountingSourceCatalogActivationReceiptV1? receipt) =>
        original is not null && receipt is not null && receipt.CompanyId != Guid.Empty && receipt.PackageId == original.PackageId && receipt.CompanyId == original.CompanyId
        && original.Status == "Active" && receipt.ExpectedPackageVersion > 0 && receipt.ExpectedPackageVersion < long.MaxValue
        && receipt.ExpectedActiveCatalogVersion >= 0 && receipt.ExpectedActiveCatalogVersion < long.MaxValue
        && receipt.ResultVersion == receipt.ExpectedPackageVersion + 1 && original.Version == receipt.ResultVersion
        && receipt.CatalogVersion == receipt.ExpectedActiveCatalogVersion + 1 && original.CatalogVersion == receipt.CatalogVersion
        && receipt.CommandName == ApprovedAccountingSourceCatalogCommandNamesV1.Activate && receipt.ActorId != Guid.Empty
        && original.ActivatedByUserId == receipt.ActorId && original.ActivatedAtUtc == receipt.ActivatedAtUtc
        && receipt.ActivatedAtUtc != default && receipt.ActivatedAtUtc.Offset == TimeSpan.Zero
        && Digest(receipt.SourceFactsDigest) && Digest(receipt.OriginalResultDigest) && Digest(receipt.IdempotencyKeyHash)
        && receipt.PayloadHash == ApprovedAccountingSourceCatalogServiceV1.ActivationPayloadHash(original.PackageId, receipt.ExpectedPackageVersion, receipt.ExpectedActiveCatalogVersion)
        && receipt.OriginalResultDigest == ApprovedAccountingSourceCatalogServiceV1.ActivationOriginalResultDigest(new(AccountingApiStatus.Ok, original, false, null))
        && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
        && Item(original.PackageId, original.PreparedByUserId, original.PreparedAtUtc) is { } expected && SameItem(item, expected);
    private static bool Digest(string? digest) => digest is { Length: 64 } && digest.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool SameItem(WorkInboxItemV1? item, WorkInboxItemV1 expected) => item is not null
        && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
        && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link
        && item.PreparedBy == expected.PreparedBy && item.Scope is null && item.Rules is not null && item.References is not null
        && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References)
        && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
}

public sealed partial class ApprovedAccountingSourceCatalogServiceV1
{
    /// <summary>Validate the typed projection with the same native model, projection and digest; no second parser/calculator.</summary>
    public static bool ValidCatalogDecisionDetail(ApprovedAccountingSourceCatalogDecisionDetailV1? detail)
    {
        if (detail?.Catalog is not { } view || detail.Package is not { Accounts: { } accounts, Bindings: { } bindings }
            || accounts.Any(account => account is null) || bindings.Any(binding => binding is null || binding.Mappings is null || binding.Mappings.Any(mapping => mapping is null))
            || detail.ExpectedPackageVersion != view.Version || detail.ExpectedActiveCatalogVersion < 0) return false;
        try
        {
            if (!Enum.TryParse<ApprovedAccountingSourceCatalogStatusV1>(view.Status, out var status)) return false;
            var actor = view.ActivatedByUserId ?? view.PreparedByUserId; var at = view.ActivatedAtUtc ?? view.PreparedAtUtc;
            var mirror = new AccountPlanMirrorV1(view.CompanyId, view.AccountPlanSourceVersion, view.AccountsSourceSha256, actor, at,
                accounts.Select(account => new AccountPlanMirrorRowV1(new AccountRef(view.CompanyId, account.AccountId), account.Code, account.Name)));
            var package = new ApprovedAccountingSourcePackageV1(mirror, [.. bindings.Select(binding =>
                new ApprovedNormalJournalSourceBindingV1(view.CompanyId, binding.SourceId, binding.SourceKind, binding.SourceVersion,
                    binding.OwnerProfileId, binding.OwnerProfileVersion, binding.CurrencyId, new NormalJournalPostingRuleV1(mirror,
                        binding.PostingPurpose, binding.PostingRuleVersion, actor, at, binding.Mappings.Select(mapping =>
                            new NormalJournalMappingV1(mapping.MappingKey, Enum.Parse<PostingSide>(mapping.Side, ignoreCase: false), new AccountRef(view.CompanyId, mapping.AccountId))))))]);
            var source = new ApprovedAccountingSourceCatalogRecordV1(view.PackageId, view.CompanyId, status, view.Version, view.CatalogVersion,
                view.TemplateVersion, view.AccountPlanSourceVersion, view.AccountsSourceSha256, view.PostingMappingsSourceSha256,
                view.PreparedByUserId, view.PreparedAtUtc, view.ActivatedByUserId, view.ActivatedAtUtc, package);
            return ValidCatalogDecisionRecord(source) && JsonSerializer.Serialize(View(source), Json) == JsonSerializer.Serialize(view, Json)
                && JsonSerializer.Serialize(CatalogDecisionPackage(package), Json) == JsonSerializer.Serialize(detail.Package, Json)
                && detail.SourceFactsDigest == CatalogSourceFactsDigest(source)
                && detail.PreviewDigest == CatalogPreviewDigest(view, detail.Package, detail.ExpectedPackageVersion, detail.ExpectedActiveCatalogVersion);
        }
        catch (AccountingDomainException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
