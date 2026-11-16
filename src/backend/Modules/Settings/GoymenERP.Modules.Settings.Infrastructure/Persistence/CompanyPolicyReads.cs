using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the company policy providers and administration share. Every query runs under the company query filter, so only
/// the server-resolved company's rows are ever seen.
/// </summary>
internal static class CompanyPolicyReads
{
    /// <summary>The history of one key, newest first.</summary>
    public static Task<CompanyPolicyVersionRow[]> LoadAsync(SettingsDbContext database, string policyKey,
        CancellationToken cancellationToken) =>
        database.CompanyPolicyVersions.AsNoTracking().Where(row => row.PolicyKey == policyKey)
            .OrderByDescending(row => row.Version).ToArrayAsync(cancellationToken);

    /// <summary>The latest version of one key (the value in force), or null when none is recorded.</summary>
    public static Task<CompanyPolicyVersionRow?> LatestAsync(SettingsDbContext database, string policyKey,
        CancellationToken cancellationToken) =>
        database.CompanyPolicyVersions.AsNoTracking().Where(row => row.PolicyKey == policyKey)
            .OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);

    /// <summary>The latest credit account mapping of one purpose, or null ("eşleme eksik").</summary>
    public static Task<FinanceCreditAccountMappingVersionRow?> LatestCreditMappingAsync(SettingsDbContext database,
        int purpose, CancellationToken cancellationToken) =>
        database.FinanceCreditAccountMappingVersions.AsNoTracking().Where(row => row.Purpose == purpose)
            .OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
}
