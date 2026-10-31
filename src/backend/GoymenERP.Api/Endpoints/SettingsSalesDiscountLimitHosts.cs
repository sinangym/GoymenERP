using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-D63/D66 (API-12): the central adapter that gives the Sales limit check the current actor's limit groups from the
/// Settings owner's person → group bindings in the server-resolved company. Sales reads no Settings table; Settings fences
/// the company and the actor itself. A Settings refusal is passed on as the Sales port's failure (denied → 403,
/// otherwise 503), never as "no group".
/// </summary>
public sealed class SalesDiscountLimitGroupHost(ISettingsSalesDiscountLimitGroupMembershipReaderV1 reader)
    : ISalesDiscountLimitGroupSourceV1
{
    public async Task<IReadOnlyList<Guid>> GetCurrentActorLimitGroupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await reader.GetCurrentActorLimitGroupIdsAsync(cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
        {
            throw new SalesDiscountLimitGroupSourceException(exception.Code == "settings_access_denied");
        }
    }
}

/// <summary>
/// SAT-D66: the central adapter between the Settings limit administration and the Identity owner's people: the people active
/// in the server-resolved company (the binding editor's picker; id + display name only) and the companies in which one
/// person is an active member now (the multi-company binding write). Identity re-fences the company and the caller.
/// </summary>
public sealed class SettingsPersonDirectoryHost(
    IUserDirectoryReadPortV1 directory,
    IUserCompanyMembershipReadPortV1 memberships) : ISettingsPersonDirectoryV1
{
    public async Task<SettingsPersonSearchAnswerV1> SearchActiveAsync(Guid companyId, string actorId, string? query, int limit,
        CancellationToken cancellationToken = default)
    {
        var answer = await directory.SearchActiveAsync(
            new UserDirectorySearchRequestV1(companyId, actorId, query, limit), cancellationToken);
        if (answer?.Items is null)
        {
            return new SettingsPersonSearchAnswerV1(SettingsPersonDirectoryOutcomeV1.Unavailable, [], false);
        }

        return answer.Outcome switch
        {
            UserDirectoryOutcomeV1.Found when answer.Items.All(item => item is { IsActiveInCompany: true }) =>
                new SettingsPersonSearchAnswerV1(SettingsPersonDirectoryOutcomeV1.Found,
                    [.. answer.Items.Select(item => new SalesDiscountLimitPersonV1(item.UserId, item.DisplayName))],
                    answer.HasMore),
            UserDirectoryOutcomeV1.Denied =>
                new SettingsPersonSearchAnswerV1(SettingsPersonDirectoryOutcomeV1.Denied, [], false),
            _ => new SettingsPersonSearchAnswerV1(SettingsPersonDirectoryOutcomeV1.Unavailable, [], false),
        };
    }

    public async Task<SettingsPersonCompaniesAnswerV1> GetActiveCompaniesAsync(Guid contextCompanyId, string actorId,
        Guid userId, IReadOnlyList<Guid> companyIds, CancellationToken cancellationToken = default)
    {
        var answer = await memberships.GetActiveCompaniesAsync(
            new UserCompanyMembershipRequestV1(contextCompanyId, actorId, userId, companyIds), cancellationToken);
        if (answer?.ActiveCompanyIds is null)
        {
            return new SettingsPersonCompaniesAnswerV1(SettingsPersonDirectoryOutcomeV1.Unavailable, []);
        }

        return answer.Outcome switch
        {
            UserDirectoryOutcomeV1.Found when answer.ActiveCompanyIds.All(companyIds.Contains) =>
                new SettingsPersonCompaniesAnswerV1(SettingsPersonDirectoryOutcomeV1.Found, answer.ActiveCompanyIds),
            UserDirectoryOutcomeV1.Denied => new SettingsPersonCompaniesAnswerV1(SettingsPersonDirectoryOutcomeV1.Denied, []),
            _ => new SettingsPersonCompaniesAnswerV1(SettingsPersonDirectoryOutcomeV1.Unavailable, []),
        };
    }
}
