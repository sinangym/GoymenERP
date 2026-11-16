using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// KIM-D39: the central adapter that tells the Settings segregation of duties owner whether the server-resolved company has
/// exactly one active user, from the Identity owner's membership predicate. Settings reads no Identity table; Identity
/// re-fences the company and the caller. A missing or unknown answer is <see cref="SettingsPersonDirectoryOutcomeV1.Unavailable"/>,
/// never "single user".
/// </summary>
public sealed class SettingsCompanyActiveUsersHost(ICompanySingleActiveMemberReadPortV1 members) : ISettingsCompanyActiveUsersV1
{
    public async Task<SettingsCompanyActiveUsersAnswerV1> HasSingleActiveUserAsync(Guid companyId, string actorId,
        CancellationToken cancellationToken = default)
    {
        var answer = await members.GetAsync(new CompanySingleActiveMemberRequestV1(companyId, actorId), cancellationToken);
        if (answer is null)
        {
            return new SettingsCompanyActiveUsersAnswerV1(SettingsPersonDirectoryOutcomeV1.Unavailable, false);
        }

        return answer.Outcome switch
        {
            UserDirectoryOutcomeV1.Found =>
                new SettingsCompanyActiveUsersAnswerV1(SettingsPersonDirectoryOutcomeV1.Found, answer.HasSingleActiveMember),
            UserDirectoryOutcomeV1.Denied => new SettingsCompanyActiveUsersAnswerV1(SettingsPersonDirectoryOutcomeV1.Denied, false),
            _ => new SettingsCompanyActiveUsersAnswerV1(SettingsPersonDirectoryOutcomeV1.Unavailable, false),
        };
    }
}
