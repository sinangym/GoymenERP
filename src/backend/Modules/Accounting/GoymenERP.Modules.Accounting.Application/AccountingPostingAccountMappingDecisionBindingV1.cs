using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Exact owner formatter and reversible routing carrier; it creates no native identity or grant.</summary>
public static class AccountingPostingAccountMappingDecisionBindingV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.posting-mapping.v1";
    public const string PurposePath = "/onaylarim/accounting.posting-mapping.v1";
    public const string NativeScreenPath = "/muhasebe/hesap-eslemeleri";
    public const string ActivationRequiredRule = "accounting.posting-mapping-activation-required";

    // Custom UUIDv8 transport: all native x16 digits survive unchanged. Company and Kind remain the Work identity scope.
    public static Guid ToWorkItemId(long mappingVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mappingVersion);
        var h = mappingVersion.ToString("x16", CultureInfo.InvariantCulture);
        return Guid.ParseExact($"ac52{h[..4]}-{h[4..8]}-8{h[8..11]}-8{h[11..14]}-{h[14..]}0000000000", "D");
    }
    public static bool TryMappingVersion(Guid itemId, out long mappingVersion)
    {
        mappingVersion = 0; var n = itemId.ToString("N", CultureInfo.InvariantCulture);
        if (n[..4] != "ac52" || n[12] != '8' || n[16] != '8' || n[22..] != "0000000000") return false;
        var h = n[4..12] + n[13..16] + n[17..22];
        if (!long.TryParse(h, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var native) || native <= 0
            || ToWorkItemId(native) != itemId) return false;
        mappingVersion = native; return true;
    }
    public static WorkInboxItemV1? Item(long mappingVersion, Guid preparedBy, DateTimeOffset preparedAtUtc)
    {
        if (mappingVersion <= 0 || preparedBy == Guid.Empty || preparedAtUtc == default || preparedAtUtc.Offset != TimeSpan.Zero) return null;
        var id = ToWorkItemId(mappingVersion); var caption = mappingVersion.ToString(CultureInfo.InvariantCulture);
        return new(ModuleName, KindName, id, caption, preparedAtUtc, PurposePath + "/" + id.ToString("D", CultureInfo.InvariantCulture),
            preparedBy.ToString("D", CultureInfo.InvariantCulture), [new(ActivationRequiredRule, null, null, null, null)],
            [new("posting-account-mapping-version", caption, id, NativeScreenPath)]);
    }
    // Scalar proof validates only format/identity/item matching; the fresh owner detail or original command certifies full facts.
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkPostingAccountMappingProofV1? proof) =>
        proof is not null && proof.CompanyId != Guid.Empty && proof.Version > 0 && proof.ActiveMappingVersion >= 0
        && proof.Status is "Prepared" or "Active" or "Superseded" && Digest(proof.SourceFactsDigest) && Digest(proof.PreviewDigest)
        && Item(proof.MappingVersion, proof.PreparedBy, proof.PreparedAtUtc) is { } expected && SameItem(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingPostingAccountMappingDecisionDetailV1? detail) =>
        AccountingPostingAccountMappingServiceV1.ValidMappingDecisionDetail(detail)
        && Item(detail!.Mapping.MappingVersion, detail.Mapping.PreparedByUserId, detail.Mapping.PreparedAtUtc) is { } expected && SameItem(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingPostingAccountMappingViewV1? original,
        AccountingPostingAccountMappingActivationReceiptV1? receipt) =>
        AccountingPostingAccountMappingServiceV1.ValidMappingOriginalReceipt(original, receipt)
        && Item(original!.MappingVersion, original.PreparedByUserId, original.PreparedAtUtc) is { } expected && SameItem(item, expected);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool SameItem(WorkInboxItemV1? item, WorkInboxItemV1 expected) => item is not null
        && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
        && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link
        && item.PreparedBy == expected.PreparedBy && item.Scope is null && item.Rules is not null && item.References is not null
        && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References)
        && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
}

public sealed partial class AccountingPostingAccountMappingServiceV1
{
    /// <summary>The existing native model, full projection and owner hashes; no second parser or account chooser.</summary>
    public static bool ValidMappingDecisionDetail(AccountingPostingAccountMappingDecisionDetailV1? detail)
    {
        if (detail?.Mapping is not { } view || !TryDecisionMappingRecord(view, out var source)
            || detail.ExpectedVersion != view.Version || detail.ExpectedActiveMappingVersion < 0
            || (view.Status == "Active") != (detail.ExpectedActiveMappingVersion == view.MappingVersion)
            || detail.PreparedAccountPlan != new AccountingPostingMappingPlanReferenceV1(view.AccountPlanSourceVersion, view.AccountPlanSourceSha256)
            || !ValidCurrentMappingPlan(detail.CurrentActiveAccountPlan, view.CompanyId)) return false;
        return detail.SourceFactsDigest == MappingSourceFactsDigest(source!)
            && detail.PreviewDigest == MappingPreviewDigest(view, detail.CurrentActiveAccountPlan, detail.ExpectedVersion, detail.ExpectedActiveMappingVersion);
    }
    public static bool ValidMappingOriginalReceipt(AccountingPostingAccountMappingViewV1? original, AccountingPostingAccountMappingActivationReceiptV1? receipt)
    {
        if (original is null || receipt is null || !TryDecisionMappingRecord(original, out var source)
            || original.Status != "Active" || receipt.CompanyId != original.CompanyId || receipt.MappingVersion != original.MappingVersion
            || receipt.ExpectedVersion <= 0 || receipt.ExpectedVersion == long.MaxValue || receipt.ExpectedActiveMappingVersion < 0
            || receipt.ResultVersion != receipt.ExpectedVersion + 1 || original.Version != receipt.ResultVersion
            || receipt.ActorId == Guid.Empty || receipt.ActorId == original.PreparedByUserId || receipt.ActorId != original.ActivatedByUserId
            || receipt.ActivatedAtUtc != original.ActivatedAtUtc || receipt.ActivatedAtUtc == default || receipt.ActivatedAtUtc.Offset != TimeSpan.Zero
            || receipt.CommandName != AccountingPostingAccountMappingCommandNamesV1.Activate || !DecisionSha(receipt.IdempotencyKeyHash)
            || !DecisionSha(receipt.OriginalResultDigest) || !DecisionSha(receipt.SourceFactsDigest) || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)) return false;
        return receipt.PayloadHash == ActivationPayloadHash(original.MappingVersion, receipt.ExpectedVersion, receipt.ExpectedActiveMappingVersion)
            && receipt.SourceFactsDigest == MappingSourceFactsDigest(source!)
            && receipt.OriginalResultDigest == ActivationOriginalResultDigest(new(AccountingApiStatus.Ok, original, false, null));
    }
    private static bool TryDecisionMappingRecord(AccountingPostingAccountMappingViewV1 view, out AccountingPostingAccountMappingRecordV1? source)
    {
        source = null;
        if (view.Rows is null || view.Rows.Any(row => row is null) || !Enum.TryParse<AccountingPostingAccountMappingStatusV1>(view.Status, out var status)) return false;
        var record = new AccountingPostingAccountMappingRecordV1(view.CompanyId, view.MappingVersion, view.EffectiveFrom, status, view.Version,
            view.AccountPlanSourceVersion, view.AccountPlanSourceSha256, view.PreparedByUserId, view.PreparedAtUtc, view.ActivatedByUserId, view.ActivatedAtUtc,
            view.Rows.Select((row, index) => new AccountingPostingAccountMappingEntryV1(index + 1, row.Purpose, row.MappingKey, row.ItemType, row.LinePurpose,
                row.IsExport, row.AccountId, row.AccountCode, row.VatRateCode)).ToArray());
        if (!ValidMappingDecisionRecord(record) || JsonSerializer.Serialize(View(record), Json) != JsonSerializer.Serialize(view, Json)) return false;
        source = record; return true;
    }
}
