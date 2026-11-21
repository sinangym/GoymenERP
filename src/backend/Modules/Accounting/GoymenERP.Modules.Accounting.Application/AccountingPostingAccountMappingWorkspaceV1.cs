namespace GoymenERP.Modules.Accounting.Application;

public sealed record AccountingPostingMappingAccountV1(Guid AccountId, string Code, string Name);
public sealed record AccountingCogsMappingDefaultV1(string MappingKey, string AccountCode, Guid? AccountId);
public sealed record AccountingPostingAccountMappingWorkspaceV1(Guid CompanyId, long LatestMappingVersion,
    string AccountPlanState, string? AccountPlanSourceVersion, string? AccountPlanSourceSha256,
    IReadOnlyList<AccountingPostingMappingAccountV1> Accounts,
    IReadOnlyList<AccountingCogsMappingDefaultV1> CogsDefaults,
    AccountingPostingAccountMappingViewV1? Active,
    IReadOnlyList<AccountingPostingAccountMappingViewV1> Prepared);

/// <summary>Read-only choices for the existing mapping authority. Defaults are suggestions from a real approved
/// plan; they never create an account, an active mapping or a grant.</summary>
public interface IAccountingPostingAccountMappingWorkspaceStoreV1
{
    Task<AccountingPostingAccountMappingWorkspaceV1> WorkspaceAsync(CancellationToken cancellationToken = default);
}

public static class AccountingCogsMappingDefaultsV1
{
    public static string Key(InvoiceLineCogsClassificationV1 classification) => classification switch
    {
        InvoiceLineCogsClassificationV1.TradeGoods => AccountingPostingAccountMappingKeysV1.TradeGoods,
        InvoiceLineCogsClassificationV1.Manufactured => AccountingPostingAccountMappingKeysV1.Manufactured,
        _ => throw new ArgumentOutOfRangeException(nameof(classification)),
    };

    // SAT-D419's explicit defaults. No prefix match to an arbitrary sub-account is allowed.
    public static IReadOnlyList<AccountingCogsMappingDefaultV1> FromApprovedPlan(AccountPlanMirrorV1? plan) =>
    [
        new(AccountingPostingAccountMappingKeysV1.TradeGoods, "621", plan?.FindByCode("621")?.Account.AccountId),
        new(AccountingPostingAccountMappingKeysV1.Manufactured, "620", plan?.FindByCode("620")?.Account.AccountId),
    ];
}
