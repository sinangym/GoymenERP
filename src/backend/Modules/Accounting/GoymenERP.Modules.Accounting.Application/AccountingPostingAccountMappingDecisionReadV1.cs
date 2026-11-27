namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Activation-purpose read; it grants neither general mapping Read nor Prepare.</summary>
public interface IAccountingPostingAccountMappingDecisionReadV1
{
    Task<AccountingApiResult<AccountingPostingAccountMappingDecisionDetailV1>> ReadDecisionAsync(long mappingVersion,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record AccountingPostingMappingPlanReferenceV1(string SourceVersion, string SourceSha256);
/// <summary>Current approved plan context, never substituted for the mapping's prepared plan reference.</summary>
public sealed record AccountingPostingMappingDecisionPlanV1(Guid CompanyId, AccountingPostingMappingPlanReferenceV1 Reference,
    IReadOnlyList<AccountingPostingMappingAccountV1> Accounts);
public sealed record AccountingPostingAccountMappingDecisionDetailV1(AccountingPostingAccountMappingViewV1 Mapping,
    AccountingPostingMappingPlanReferenceV1 PreparedAccountPlan, AccountingPostingMappingDecisionPlanV1 CurrentActiveAccountPlan,
    long ExpectedVersion, long ExpectedActiveMappingVersion, string SourceFactsDigest, string PreviewDigest);
public sealed record AccountingPostingAccountMappingDecisionSourceV1(AccountingPostingAccountMappingRecordV1 Record,
    long ActiveMappingVersion, AccountingPostingMappingDecisionPlanV1 CurrentActiveAccountPlan);
public interface IAccountingPostingAccountMappingDecisionStoreV1
{
    Task<AccountingPostingAccountMappingRecordV1?> ReadActivationReceiptSourceAsync(long mappingVersion,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
    Task<AccountingPostingAccountMappingDecisionSourceV1?> ReadActivationDecisionAsync(long mappingVersion,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
}
/// <summary>Private exact original command proof. Current active-plan state is deliberately absent.</summary>
public sealed record AccountingPostingAccountMappingActivationReceiptV1(Guid CompanyId, long MappingVersion, string CommandName,
    string IdempotencyKeyHash, string PayloadHash, Guid ActorId, long ExpectedVersion, long ExpectedActiveMappingVersion,
    long ResultVersion, string OriginalCorrelationId, DateTimeOffset ActivatedAtUtc, string SourceFactsDigest, string OriginalResultDigest);
