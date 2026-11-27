namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Activation-purpose read only; it grants no general catalog/Prepare permission.</summary>
public interface IApprovedAccountingSourceCatalogDecisionReadV1
{
    Task<AccountingApiResult<ApprovedAccountingSourceCatalogDecisionDetailV1>> ReadDecisionAsync(Guid packageId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed record ApprovedAccountingSourceCatalogDecisionDetailV1(ApprovedAccountingSourceCatalogViewV1 Catalog,
    ApprovedAccountingSourceCatalogDecisionPackageV1 Package, long ExpectedPackageVersion, long ExpectedActiveCatalogVersion,
    string SourceFactsDigest, string PreviewDigest);
/// <summary>The complete persisted package, with the account mirror emitted once rather than per binding.</summary>
public sealed record ApprovedAccountingSourceCatalogDecisionPackageV1(IReadOnlyList<ApprovedAccountingSourceCatalogDecisionAccountV1> Accounts,
    IReadOnlyList<ApprovedAccountingSourceCatalogDecisionBindingV1> Bindings);
public sealed record ApprovedAccountingSourceCatalogDecisionAccountV1(Guid AccountId, string Code, string Name);
public sealed record ApprovedAccountingSourceCatalogDecisionBindingV1(Guid SourceId, string SourceKind, string SourceVersion,
    string OwnerProfileId, string OwnerProfileVersion, Guid CurrencyId, string PostingPurpose, string PostingRuleVersion,
    IReadOnlyList<ApprovedAccountingSourceCatalogDecisionMappingV1> Mappings);
public sealed record ApprovedAccountingSourceCatalogDecisionMappingV1(string MappingKey, string Side, Guid AccountId);
public sealed record ApprovedAccountingSourceCatalogDecisionSourceV1(ApprovedAccountingSourceCatalogRecordV1 Record, long ActiveCatalogVersion);
public interface IApprovedAccountingSourceCatalogDecisionStoreV1
{
    Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActivationReceiptSourceAsync(Guid packageId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
    Task<ApprovedAccountingSourceCatalogDecisionSourceV1?> ReadActivationDecisionAsync(Guid packageId,
        Func<CancellationToken, Task> freshReadFence, CancellationToken cancellationToken = default);
}
/// <summary>Private proof of the exact original Commands result, never a replacement made from current status.</summary>
public sealed record ApprovedAccountingSourceCatalogActivationReceiptV1(Guid CompanyId, Guid PackageId, string CommandName,
    string IdempotencyKeyHash, string PayloadHash, Guid ActorId, long ExpectedPackageVersion, long ExpectedActiveCatalogVersion,
    long ResultVersion, long CatalogVersion, string OriginalCorrelationId, DateTimeOffset ActivatedAtUtc,
    string SourceFactsDigest, string OriginalResultDigest);
