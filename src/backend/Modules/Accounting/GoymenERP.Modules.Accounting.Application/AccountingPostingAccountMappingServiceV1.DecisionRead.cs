using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingPostingAccountMappingServiceV1 : IAccountingPostingAccountMappingDecisionReadV1
{
    public async Task<AccountingApiResult<AccountingPostingAccountMappingDecisionDetailV1>> ReadDecisionAsync(long mappingVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        if (mappingVersion <= 0) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, "mappingVersion");
        if (!TryContext(out var company, out var actor)) return ReadDenied();
        try
        {
            await ReadFence(cancellationToken);
            if (mappings is not IAccountingPostingAccountMappingDecisionStoreV1 reader) return ReadUnavailable();
            var source = await reader.ReadActivationDecisionAsync(mappingVersion, ReadFence, cancellationToken);
            // Freeze owner collections before a consumer callback can change a provider-owned list.
            if (source is not null)
            {
                if (source.Record is null || source.CurrentActiveAccountPlan is null
                    || source.Record.Rows is null || source.CurrentActiveAccountPlan.Accounts is null) return ReadUnavailable();
                source = source with { Record = FreezeMappingRecord(source.Record), CurrentActiveAccountPlan = source.CurrentActiveAccountPlan with
                    { Accounts = Array.AsReadOnly(source.CurrentActiveAccountPlan.Accounts.ToArray()) } };
            }
            await ReadFence(cancellationToken);
            if (source is null) return new(AccountingApiStatus.NotFound, null, false, null);
            if (source.Record.CompanyId != company || source.Record.MappingVersion != mappingVersion
                || source.ActiveMappingVersion < 0 || !ValidMappingDecisionRecord(source.Record)
                || (source.Record.Status == AccountingPostingAccountMappingStatusV1.Active) != (source.ActiveMappingVersion == mappingVersion)
                || !ValidCurrentMappingPlan(source.CurrentActiveAccountPlan, company)) return ReadUnavailable();
            // The native maker-checker rule has no grant-based exception. This read never records a refusal.
            if (source.Record.PreparedByUserId == actor) return new(AccountingApiStatus.Denied, null, false,
                AccountingPostingAccountMappingCodesV1.SelfActivation, "activation");
            var mapping = View(source.Record);
            var detail = new AccountingPostingAccountMappingDecisionDetailV1(mapping,
                new(mapping.AccountPlanSourceVersion, mapping.AccountPlanSourceSha256), source.CurrentActiveAccountPlan,
                mapping.Version, source.ActiveMappingVersion, MappingSourceFactsDigest(source.Record),
                MappingPreviewDigest(mapping, source.CurrentActiveAccountPlan, mapping.Version, source.ActiveMappingVersion));
            await ReadFence(cancellationToken);
            return new(AccountingApiStatus.Ok, detail, false, null);
        }
        catch (NormalJournalAuthorityDeniedException) { return ReadDenied(); }
        catch (AccountingStoreException error) when (error.Code == AccountingStoreCodes.ProviderUnavailable) { return ReadUnavailable(); }
        async Task ReadFence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            await ActivationFenceAsync(company, actor, token); token.ThrowIfCancellationRequested();
        }
        static AccountingApiResult<AccountingPostingAccountMappingDecisionDetailV1> ReadDenied() => new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
        static AccountingApiResult<AccountingPostingAccountMappingDecisionDetailV1> ReadUnavailable() => new(AccountingApiStatus.Unavailable, null, false, null);
    }

    public static string MappingSourceFactsDigest(AccountingPostingAccountMappingRecordV1 source) => AccountingApiService.Sha256(JsonSerializer.Serialize(new
    {
        source.CompanyId, source.MappingVersion, source.EffectiveFrom, source.AccountPlanSourceVersion, source.AccountPlanSourceSha256,
        source.PreparedByUserId, source.PreparedAtUtc, Rows = source.Rows.OrderBy(row => row.LineNumber).ToArray(),
    }, Json));
    public static string MappingPreviewDigest(AccountingPostingAccountMappingViewV1 mapping, AccountingPostingMappingDecisionPlanV1 currentActiveAccountPlan,
        long expectedVersion, long expectedActiveMappingVersion) => AccountingApiService.Sha256(JsonSerializer.Serialize(new
        { Mapping = mapping, CurrentActiveAccountPlan = currentActiveAccountPlan, ExpectedVersion = expectedVersion, ExpectedActiveMappingVersion = expectedActiveMappingVersion }, Json));
    private static AccountingPostingAccountMappingRecordV1 FreezeMappingRecord(AccountingPostingAccountMappingRecordV1 source) =>
        source with { Rows = Array.AsReadOnly(source.Rows.ToArray()) };

    private static bool ValidMappingDecisionRecord(AccountingPostingAccountMappingRecordV1 source)
    {
        if (source is null || source.CompanyId == Guid.Empty || source.MappingVersion <= 0 || source.Version <= 0 || source.EffectiveFrom == default
            || !Enum.IsDefined(source.Status) || !DecisionText(source.AccountPlanSourceVersion) || !DecisionSha(source.AccountPlanSourceSha256)
            || source.PreparedByUserId == Guid.Empty || source.PreparedAtUtc == default || source.PreparedAtUtc.Offset != TimeSpan.Zero
            || source.Rows is null || source.Rows.Any(row => row is null || !DecisionText(row.AccountCode))
            || !source.Rows.OrderBy(row => row.LineNumber).Select(row => row.LineNumber).SequenceEqual(Enumerable.Range(1, source.Rows.Count))) return false;
        if (source.Status == AccountingPostingAccountMappingStatusV1.Prepared)
        { if (source.Version != 1 || source.ActivatedByUserId is not null || source.ActivatedAtUtc is not null) return false; }
        else if (source.Status == AccountingPostingAccountMappingStatusV1.Active && source.Version != 2
            || source.Status == AccountingPostingAccountMappingStatusV1.Superseded && source.Version != 3
            || source.ActivatedByUserId is not { } activated || activated == Guid.Empty || activated == source.PreparedByUserId
            || source.ActivatedAtUtc is not { } at || at == default || at.Offset != TimeSpan.Zero) return false;
        return AccountingPostingAccountMappingPayloadV1.Validate(source.Rows.Select(row => new AccountingPostingAccountMappingRowRequestV1(
            row.Purpose, row.MappingKey, row.ItemType, row.LinePurpose, row.IsExport, row.AccountId, row.VatRateCode)).ToArray(), out _) is null;
    }
    private static bool ValidCurrentMappingPlan(AccountingPostingMappingDecisionPlanV1 plan, Guid company) =>
        plan is not null && plan.CompanyId == company && plan.Reference is { } reference && DecisionText(reference.SourceVersion) && DecisionSha(reference.SourceSha256)
        && plan.Accounts is { Count: > 0 } accounts && accounts.All(row => row is not null && row.AccountId != Guid.Empty && DecisionText(row.Code) && !string.IsNullOrWhiteSpace(row.Name))
        && accounts.Select(row => row.AccountId).Distinct().Count() == accounts.Count
        && accounts.Select(row => row.Code).Distinct(StringComparer.Ordinal).Count() == accounts.Count;
    private static bool DecisionText(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim();
    private static bool DecisionSha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
