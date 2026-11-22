using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>
/// W1-C host composition: Work's record-access, person-directory and digest-schedule ports over the owners' own surfaces.
/// Work never reads another module's tables; each owner answers with its own authorization. The collaboration scheduler stays
/// off unless <c>Work:CollaborationScheduler:Enabled</c> is true (a human gate).
/// </summary>
public static class WorkCollaborationHostV1
{
    public static IServiceCollection AddWorkCollaborationHostV1(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWorkCollaborationV1(configuration);
        services.AddScoped<IWorkRecordAccessPortV1, WorkOwnerRecordAccessPortV1>();
        services.AddScoped<IWorkPersonDirectoryV1, WorkIdentityPersonDirectoryV1>();
        services.AddScoped<IWorkDigestScheduleReaderV1, WorkUnavailableDigestScheduleReaderV1>();
        return services;
    }
}

/// <summary>
/// The current actor: the owner's authorized GET-by-id (its permission, company fence and not-found rules). A named user: the
/// owner's read permission held company-wide by that user NOW (Identity measures it); every one of these five read permissions
/// is company-wide only and none of the owners filters a record by person, so holding it is what "can read this record" means
/// once the actor has found the record in this company. Not found and forbidden both answer <see cref="WorkRecordAccessOutcomeV1.NotReadable"/>;
/// an owner failure answers <see cref="WorkRecordAccessOutcomeV1.Unavailable"/>.
/// </summary>
internal sealed class WorkOwnerRecordAccessPortV1(IServiceProvider services, ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IUserPermissionHolderReadPortV1 holders) : IWorkRecordAccessPortV1
{
    internal static string ReadPermission(WorkRecordTypeV1 type) => type switch
    {
        WorkRecordTypeV1.SalesOrderDraft => ErpPermissions.SalesDraftsRead,
        WorkRecordTypeV1.PurchaseOrder => ErpPermissions.PurchasingOrdersRead,
        WorkRecordTypeV1.StockLabel => ErpPermissions.InventoryLabelsRead,
        WorkRecordTypeV1.ManufacturingPlan => ErpPermissions.ManufacturingPlansRead,
        WorkRecordTypeV1.Counterparty => ErpPermissions.CounterpartyPartiesRead,
        _ => throw new InvalidOperationException("Unknown record type."),
    };

    public async Task<WorkRecordAccessOutcomeV1> CheckCurrentActorAsync(Guid companyId, WorkRecordRefV1 record,
        CancellationToken cancellationToken = default)
    {
        if (!Matches(companyId)) return WorkRecordAccessOutcomeV1.NotReadable;
        try
        {
            var outcome = record.Type switch
            {
                WorkRecordTypeV1.SalesOrderDraft => await SalesAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.PurchaseOrder => await PurchaseAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.StockLabel => await LabelAsync(companyId, record.RecordId, cancellationToken),
                WorkRecordTypeV1.ManufacturingPlan => await PlanAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.Counterparty => await services.GetRequiredService<ICounterpartyReadService>()
                    .GetAuthorizedPartyAsync(record.RecordId, cancellationToken) is null
                    ? WorkRecordAccessOutcomeV1.NotReadable : WorkRecordAccessOutcomeV1.Readable,
                _ => WorkRecordAccessOutcomeV1.NotReadable,
            };
            return Matches(companyId) ? outcome : WorkRecordAccessOutcomeV1.NotReadable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return WorkRecordAccessOutcomeV1.Unavailable; }
    }

    public async Task<WorkRecordAccessOutcomeV1> CheckUserAsync(Guid companyId, WorkRecordRefV1 record, Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!Matches(companyId) || userId == Guid.Empty) return WorkRecordAccessOutcomeV1.NotReadable;
        var answer = await holders.HoldsAsync(new UserPermissionHolderRequestV1(companyId, user.UserId!, userId, ReadPermission(record.Type)),
            cancellationToken);
        if (!Matches(companyId)) return WorkRecordAccessOutcomeV1.NotReadable;
        return answer.Outcome switch
        {
            UserDirectoryOutcomeV1.Found => answer.Holds ? WorkRecordAccessOutcomeV1.Readable : WorkRecordAccessOutcomeV1.NotReadable,
            UserDirectoryOutcomeV1.Denied => WorkRecordAccessOutcomeV1.NotReadable,
            _ => WorkRecordAccessOutcomeV1.Unavailable,
        };
    }

    private bool Matches(Guid companyId) => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
        && user.IsAuthenticated && user.CanAccessCompany(companyId);

    private async Task<WorkRecordAccessOutcomeV1> SalesAsync(Guid id, CancellationToken token)
    {
        try { _ = await services.GetRequiredService<ISalesDraftService>().GetDraftDetailAsync(id, token); return WorkRecordAccessOutcomeV1.Readable; }
        catch (SalesDraftServiceException exception) { return Map(exception.Status); }
    }

    private async Task<WorkRecordAccessOutcomeV1> PurchaseAsync(Guid id, CancellationToken token)
    {
        try { _ = await services.GetRequiredService<IPurchasingOrderReadServiceV1>().GetOrderDetailAsync(id, token); return WorkRecordAccessOutcomeV1.Readable; }
        catch (PurchasingServiceException exception) { return Map(exception.Status); }
    }

    private async Task<WorkRecordAccessOutcomeV1> LabelAsync(Guid companyId, Guid id, CancellationToken token)
    {
        // Inventory's label GET has no service: the endpoint policy is the permission and the store is company-filtered.
        var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.InventoryLabelsRead, companyId, token);
        if (scope is not { IsGranted: true, IsCompanyWide: true }) return WorkRecordAccessOutcomeV1.NotReadable;
        var label = await services.GetRequiredService<IInventoryLabelStore>().FindLabelAsync(id, token);
        return label is not null && label.CompanyId == companyId ? WorkRecordAccessOutcomeV1.Readable : WorkRecordAccessOutcomeV1.NotReadable;
    }

    private async Task<WorkRecordAccessOutcomeV1> PlanAsync(Guid id, CancellationToken token)
    {
        var result = await services.GetRequiredService<ManufacturingCommandService>().GetPlanAsync(id, token);
        return result.Status switch
        {
            ManufacturingApiStatus.Ok when result.Resource is not null => WorkRecordAccessOutcomeV1.Readable,
            ManufacturingApiStatus.Denied or ManufacturingApiStatus.NotFound or ManufacturingApiStatus.Invalid => WorkRecordAccessOutcomeV1.NotReadable,
            _ => WorkRecordAccessOutcomeV1.Unavailable,
        };
    }

    private static WorkRecordAccessOutcomeV1 Map(int status) => status is 401 or 403 or 404
        ? WorkRecordAccessOutcomeV1.NotReadable
        : WorkRecordAccessOutcomeV1.Unavailable;
}

/// <summary>Identity's directory and membership ports, fenced on the server-resolved company and the current actor.</summary>
internal sealed class WorkIdentityPersonDirectoryV1(IUserDirectoryReadPortV1 directory, IUserCompanyMembershipReadPortV1 membership,
    ICurrentUser user) : IWorkPersonDirectoryV1
{
    public async Task<WorkPeopleAnswerV1> ResolveAsync(Guid companyId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (user.UserId is not { } actor) return new(WorkPersonOutcomeV1.Denied, [], false);
        var found = new List<WorkPersonV1>();
        foreach (var chunk in userIds.Distinct().Chunk(UserDirectoryLimitsV1.MaximumResolveCount))
        {
            var answer = await directory.ResolveAsync(new UserDirectoryResolveRequestV1(companyId, actor, chunk), cancellationToken);
            if (answer.Outcome != UserDirectoryOutcomeV1.Found) return new(Map(answer.Outcome), [], false);
            found.AddRange(answer.Items.Select(item => new WorkPersonV1(item.UserId, item.DisplayName, item.IsActiveInCompany)));
        }

        return new(WorkPersonOutcomeV1.Found, found, false);
    }

    public async Task<WorkPeopleAnswerV1> SearchAsync(Guid companyId, string? query, CancellationToken cancellationToken = default)
    {
        if (user.UserId is not { } actor) return new(WorkPersonOutcomeV1.Denied, [], false);
        var answer = await directory.SearchActiveAsync(new UserDirectorySearchRequestV1(companyId, actor, query,
            UserDirectoryLimitsV1.MaximumSearchLimit), cancellationToken);
        return answer.Outcome == UserDirectoryOutcomeV1.Found
            ? new(WorkPersonOutcomeV1.Found, answer.Items.Select(item => new WorkPersonV1(item.UserId, item.DisplayName, item.IsActiveInCompany)).ToArray(), answer.HasMore)
            : new(Map(answer.Outcome), [], false);
    }

    public async Task<WorkPersonOutcomeV1> IsActiveMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (user.UserId is not { } actor || userId == Guid.Empty) return WorkPersonOutcomeV1.Denied;
        var answer = await membership.GetActiveCompaniesAsync(new UserCompanyMembershipRequestV1(companyId, actor, userId, [companyId]),
            cancellationToken);
        return answer.Outcome switch
        {
            UserDirectoryOutcomeV1.Found => answer.ActiveCompanyIds.Count == 1 && answer.ActiveCompanyIds[0] == companyId
                ? WorkPersonOutcomeV1.Found : WorkPersonOutcomeV1.Denied,
            UserDirectoryOutcomeV1.Denied => WorkPersonOutcomeV1.Denied,
            _ => WorkPersonOutcomeV1.Unavailable,
        };
    }

    private static WorkPersonOutcomeV1 Map(UserDirectoryOutcomeV1 outcome) => outcome == UserDirectoryOutcomeV1.Denied
        ? WorkPersonOutcomeV1.Denied : WorkPersonOutcomeV1.Unavailable;
}

/// <summary>
/// AI-D29 makes the digest times a Settings parameter; no Settings owner key exists yet, so every company answers Unavailable
/// and the digest class stays held (fail-closed; never shown early, never on a guessed hour). NEEDS-SINAN: the default times.
/// </summary>
internal sealed class WorkUnavailableDigestScheduleReaderV1(ICompanyContext company) : IWorkDigestScheduleReaderV1
{
    public Task<WorkDigestScheduleV1> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(WorkDigestScheduleV1.Unavailable(company.IsResolved ? company.CompanyId : Guid.Empty));
}
