using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
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
        // YOL-D25 (WP2): the digest times are a Settings key now (09:00/13:00/17:00 version 0, business days only).
        services.AddScoped<IWorkDigestScheduleReaderV1, WorkSettingsDigestScheduleReaderV1>();
        services.AddWorkApprovalFollowUpHostV1();
        services.AddScoped<IWorkRecordDisposalPortV1, WorkUnavailableRecordDisposalPortV1>();
        return services;
    }
}

/// <summary>
/// The current actor: the owner's authorized GET-by-id (its permission, company fence and not-found rules). A named user: the
/// owner's read permissions held company-wide by that user NOW (Identity measures it); every one of these read permissions
/// is company-wide only and none of the owners filters a record by person, so holding it is what "can read this record" means
/// once the actor has found the record in this company. Not found and forbidden both answer <see cref="WorkRecordAccessOutcomeV1.NotReadable"/>;
/// an owner failure answers <see cref="WorkRecordAccessOutcomeV1.Unavailable"/>.
/// </summary>
internal sealed class WorkOwnerRecordAccessPortV1(IServiceProvider services, ICompanyContext company, ICurrentUser user,
    ICorrelationContext correlation, IUserPermissionHolderReadPortV1 holders) : IWorkRecordAccessPortV1
{
    /// <summary>
    /// The owner's read permissions for a record kind, ALL of which the owner's GET-by-id demands company-wide (WP9: the
    /// stock label's authorized detail read also needs the balances read; the sales invoice and the incoming invoice reads
    /// hard-require the price read). A named user can read the record when they hold every one of them now.
    /// </summary>
    internal static IReadOnlyList<string> ReadPermissions(WorkRecordTypeV1 type) => type switch
    {
        WorkRecordTypeV1.SalesOrderDraft => [ErpPermissions.SalesDraftsRead],
        WorkRecordTypeV1.PurchaseOrder => [ErpPermissions.PurchasingOrdersRead],
        WorkRecordTypeV1.StockLabel => [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead],
        WorkRecordTypeV1.ManufacturingPlan => [ErpPermissions.ManufacturingPlansRead],
        WorkRecordTypeV1.Counterparty => [ErpPermissions.CounterpartyPartiesRead],
        WorkRecordTypeV1.SalesInvoice => [ErpPermissions.SalesInvoicesRead, ErpPermissions.SalesOrdersPriceRead],
        WorkRecordTypeV1.LoadingInstruction => [ErpPermissions.LogisticsLoadingInstructionsRead],
        WorkRecordTypeV1.PurchaseIncomingInvoice => [ErpPermissions.PurchasingInvoicesRead, ErpPermissions.PurchasingOrdersPriceRead],
        WorkRecordTypeV1.ManufacturingOrder => [ErpPermissions.ManufacturingOrdersRead],
        WorkRecordTypeV1.FinanceManualReceipt => [ErpPermissions.FinanceManualReceiptDraftRead],
        WorkRecordTypeV1.FinanceManualBankPayment => [ErpPermissions.FinanceManualBankPaymentDraftRead],
        WorkRecordTypeV1.SalesQuote => [ErpPermissions.SalesQuotesRead],
        WorkRecordTypeV1.PurchaseGoodsReceipt => [ErpPermissions.PurchasingOrdersRead],
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
                WorkRecordTypeV1.StockLabel => await LabelAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.ManufacturingPlan => await PlanAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.Counterparty => await services.GetRequiredService<ICounterpartyReadService>()
                    .GetAuthorizedPartyAsync(record.RecordId, cancellationToken) is null
                    ? WorkRecordAccessOutcomeV1.NotReadable : WorkRecordAccessOutcomeV1.Readable,
                WorkRecordTypeV1.SalesInvoice => await SalesStatusAsync(() => services.GetRequiredService<ISalesInvoiceService>()
                    .GetInvoiceAsync(record.RecordId, cancellationToken)),
                WorkRecordTypeV1.SalesQuote => await SalesStatusAsync(() => services.GetRequiredService<ISalesQuoteService>()
                    .GetQuoteDetailAsync(record.RecordId, cancellationToken)),
                WorkRecordTypeV1.LoadingInstruction => await InstructionAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.PurchaseGoodsReceipt => await GoodsReceiptAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.PurchaseIncomingInvoice => await IncomingInvoiceAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.ManufacturingOrder => await ManufacturingOrderAsync(record.RecordId, cancellationToken),
                WorkRecordTypeV1.FinanceManualReceipt => FinanceMap((await services.GetRequiredService<IManualReceiptService>()
                    .DetailAuthorizedAsync(FinanceContext(companyId), new SourceDetailRequest(record.RecordId), cancellationToken)).Outcome),
                WorkRecordTypeV1.FinanceManualBankPayment => FinanceMap((await services.GetRequiredService<IManualBankPaymentService>()
                    .DetailAuthorizedAsync(FinanceContext(companyId), record.RecordId, cancellationToken)).Outcome),
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
        foreach (var permission in ReadPermissions(record.Type))
        {
            var answer = await holders.HoldsAsync(new UserPermissionHolderRequestV1(companyId, user.UserId!, userId, permission),
                cancellationToken);
            if (!Matches(companyId)) return WorkRecordAccessOutcomeV1.NotReadable;
            switch (answer.Outcome)
            {
                case UserDirectoryOutcomeV1.Found when answer.Holds: continue;
                case UserDirectoryOutcomeV1.Found or UserDirectoryOutcomeV1.Denied: return WorkRecordAccessOutcomeV1.NotReadable;
                default: return WorkRecordAccessOutcomeV1.Unavailable;
            }
        }

        return WorkRecordAccessOutcomeV1.Readable;
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

    /// <summary>
    /// WP9 / D8: Inventory's own authorized label read port (<see cref="IInventoryLabelDetailsV1"/>: authentication, company
    /// context and both label and balance reads company-wide) — never Inventory's store.
    /// </summary>
    private async Task<WorkRecordAccessOutcomeV1> LabelAsync(Guid id, CancellationToken token)
    {
        try { _ = await services.GetRequiredService<IInventoryLabelDetailsV1>().GetAsync(id, token); return WorkRecordAccessOutcomeV1.Readable; }
        catch (InventoryLabelDetailException exception) { return Map(exception.StatusCode); }
    }

    private static async Task<WorkRecordAccessOutcomeV1> SalesStatusAsync<T>(Func<Task<T>> read)
    {
        try { _ = await read(); return WorkRecordAccessOutcomeV1.Readable; }
        catch (SalesDraftServiceException exception) { return Map(exception.Status); }
    }

    private async Task<WorkRecordAccessOutcomeV1> InstructionAsync(Guid id, CancellationToken token)
    {
        try { _ = await services.GetRequiredService<ILogisticsInstructionService>().GetInstructionDetailAsync(id, token); return WorkRecordAccessOutcomeV1.Readable; }
        catch (LogisticsInstructionServiceException exception) { return Map(exception.Status); }
    }

    private async Task<WorkRecordAccessOutcomeV1> IncomingInvoiceAsync(Guid id, CancellationToken token)
    {
        try { _ = await services.GetRequiredService<IPurchaseIncomingInvoiceWorkflowV1>().ReadAsync(id, token); return WorkRecordAccessOutcomeV1.Readable; }
        catch (PurchasingServiceException exception) { return Map(exception.Status); }
    }

    /// <summary>YOL-D42: Purchasing's read port by acceptance id (its authorized receipt read); never Purchasing's tables.</summary>
    private async Task<WorkRecordAccessOutcomeV1> GoodsReceiptAsync(Guid id, CancellationToken token)
    {
        try { _ = await services.GetRequiredService<IPurchaseGoodsReceiptRecordReadPortV1>().ReadByAcceptanceAsync(id, token); return WorkRecordAccessOutcomeV1.Readable; }
        catch (PurchasingServiceException exception) { return Map(exception.Status); }
    }

    private async Task<WorkRecordAccessOutcomeV1> ManufacturingOrderAsync(Guid id, CancellationToken token)
    {
        var result = await services.GetRequiredService<ManufacturingCommandService>().GetOrderAsync(id, token);
        return ManufacturingMap(result.Status, result.Resource is not null);
    }

    private FinanceContext FinanceContext(Guid companyId) => new(companyId, user.UserId!,
        correlation.IsResolved ? correlation.CorrelationId : Guid.NewGuid().ToString("D"), correlation.IsResolved ? correlation.CausationId : null);

    private static WorkRecordAccessOutcomeV1 FinanceMap(ReadOutcome outcome) => outcome switch
    {
        ReadOutcome.Found => WorkRecordAccessOutcomeV1.Readable,
        ReadOutcome.Denied or ReadOutcome.NotFound => WorkRecordAccessOutcomeV1.NotReadable,
        _ => WorkRecordAccessOutcomeV1.Unavailable,
    };

    private static WorkRecordAccessOutcomeV1 ManufacturingMap(ManufacturingApiStatus status, bool hasResource) => status switch
    {
        ManufacturingApiStatus.Ok when hasResource => WorkRecordAccessOutcomeV1.Readable,
        ManufacturingApiStatus.Denied or ManufacturingApiStatus.NotFound or ManufacturingApiStatus.Invalid => WorkRecordAccessOutcomeV1.NotReadable,
        _ => WorkRecordAccessOutcomeV1.Unavailable,
    };

    private async Task<WorkRecordAccessOutcomeV1> PlanAsync(Guid id, CancellationToken token)
    {
        var result = await services.GetRequiredService<ManufacturingCommandService>().GetPlanAsync(id, token);
        return ManufacturingMap(result.Status, result.Resource is not null);
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
/// YOL-D26: no owner states a record disposal yet (the record disposal policy is not in force), so every record answers
/// Unavailable and the Work note disposal path refuses even if its configuration key were on.
/// </summary>
internal sealed class WorkUnavailableRecordDisposalPortV1 : IWorkRecordDisposalPortV1
{
    public Task<WorkRecordDisposalOutcomeV1> CheckDisposedAsync(Guid companyId, WorkRecordRefV1 record, CancellationToken cancellationToken = default) =>
        Task.FromResult(WorkRecordDisposalOutcomeV1.Unavailable);
}
