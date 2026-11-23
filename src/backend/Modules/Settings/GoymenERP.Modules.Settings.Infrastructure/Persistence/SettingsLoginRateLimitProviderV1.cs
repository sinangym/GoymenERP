using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsLoginRateLimitProviderV1"/> (KIM-D34). The only source is the stored group-wide login attempt
/// limit versions; the latest one answers and none recorded is <c>null</c> (the host keeps its configured starting value). It
/// runs on the anonymous login path, so it asks for no user and no company; the record carries no company data. Order:
/// cancelled → <see cref="OperationCanceledException"/>; an unreadable store → <c>settings_provider_unavailable</c>; malformed
/// stored data → <c>settings_profile_invalid</c>.
/// </summary>
public sealed class SettingsLoginRateLimitProviderV1 : ISettingsLoginRateLimitProviderV1
{
    private readonly SettingsDbContext _database;

    public SettingsLoginRateLimitProviderV1(SettingsDbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<SettingsLoginRateLimitV1?> GetLoginRateLimitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoginRateLimitVersionRow? row;
        try
        {
            row = await LoginRateLimitReads.LatestAsync(_database, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProviderUnavailable);
        }

        if (row is null)
        {
            return null;
        }

        if (row.Version <= 0 || row.ProfileId == Guid.Empty
            || !LoginRateLimitRulesV1.IsPermitLimit(row.PermitLimit)
            || !LoginRateLimitRulesV1.IsWindowSeconds(row.WindowSeconds)
            || !LoginRateLimitRulesV1.IsPartition(row.PartitionBy)
            || !LoginRateLimitRulesV1.TryReadTrustedProxies(row.TrustedProxies, out var trustedProxies))
        {
            throw new SettingsPrerequisiteException(LoginRateLimitProblemCodesV1.ProfileInvalid);
        }

        return new SettingsLoginRateLimitV1(
            new ProfileVersionV1(row.ProfileId, row.Version),
            row.PermitLimit,
            TimeSpan.FromSeconds(row.WindowSeconds),
            row.PartitionBy,
            trustedProxies);
    }
}
