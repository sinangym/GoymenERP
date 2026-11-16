using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// KIM-D39: the one reading of "exactly one active user in the company" the segregation of duties provider and administration
/// share, through the Identity owner's port. Fail-closed: no composed port, an owner failure, a missing or unknown answer →
/// <c>settings_provider_unavailable</c>; a refused fence → <c>settings_access_denied</c>. Never answers "single" by default.
/// </summary>
internal static class SegregationOfDutiesSingleUserCheck
{
    public static async Task<bool> HasSingleActiveUserAsync(ISettingsCompanyActiveUsersV1? activeUsers, Guid companyId,
        string actorId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (activeUsers is null || string.IsNullOrWhiteSpace(actorId))
        {
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
        }

        SettingsCompanyActiveUsersAnswerV1? answer;
        try
        {
            answer = await activeUsers.HasSingleActiveUserAsync(companyId, actorId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
        }

        if (answer is null)
        {
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
        }

        return answer.Outcome switch
        {
            SettingsPersonDirectoryOutcomeV1.Found => answer.HasSingleActiveUser,
            SettingsPersonDirectoryOutcomeV1.Denied =>
                throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.AccessDenied),
            _ => throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable),
        };
    }
}
