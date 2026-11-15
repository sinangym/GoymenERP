using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Api.Shell;

/// <summary>
/// F-019 / API-11 v2 host composition: Finance owns the customer return acceptance port
/// (<see cref="IFinanceInvoiceAcceptancePortV2"/>); this host fences it with a fresh company-wide measurement of
/// <c>sales.returns.finalize</c> (SAT-D290: the return invoice is finalised by that grant; SAT-D208: Sales commits first
/// and re-drives with the same key). Only the Finance side is composed here: the Sales consumer port
/// (ISalesCustomerReturnFinancePortV1) stays unregistered, so Sales' finalisation keeps answering 503 (SAT-D294 fail-closed)
/// until the Sales side moves its provisional offer onto this port. FIN-S10 (FNS-D44) adds the read side of the same port,
/// <see cref="IFinanceCustomerReturnInstallmentReadPortV2"/> (the screen's nearest-due prefill), answered by the same
/// Finance service under the same fence; no new permission is introduced.
/// </summary>
public static class FinanceInvoiceAcceptanceHostExtensions
{
    public static IServiceCollection AddFinanceInvoiceAcceptanceHost(this IServiceCollection services)
    {
        services.AddScoped<FinanceInvoiceAcceptanceAuthority>();
        services.AddScoped<IFinanceInvoiceAcceptanceAuthorityV2>(provider =>
            provider.GetRequiredService<FinanceInvoiceAcceptanceAuthority>());
        services.AddScoped<IFinanceInvoiceAcceptancePortV2>(CreateAcceptanceService);
        services.AddScoped<IFinanceCustomerReturnInstallmentReadPortV2>(CreateAcceptanceService);
        return services;
    }

    private static FinanceInvoiceAcceptanceService CreateAcceptanceService(IServiceProvider provider)
    {
        var authority = provider.GetRequiredService<FinanceInvoiceAcceptanceAuthority>();
        var correlation = provider.GetRequiredService<ICorrelationContext>();
        return new FinanceInvoiceAcceptanceService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            authority,
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceAuditEnvelope(ErpPermissions.SalesReturnsFinalize, authority.RoleCodes, correlation.SourceChannel,
                correlation.ClientIpAddress, correlation.UserAgent),
            provider.GetRequiredService<TimeProvider>());
    }
}

/// <summary>
/// Fresh company-wide measurement of <c>sales.returns.finalize</c> for Finance's customer return acceptance; fails closed.
/// Only a Sales customer return is mapped; any other producer or kind is unavailable.
/// </summary>
public sealed class FinanceInvoiceAcceptanceAuthority(
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : IFinanceInvoiceAcceptanceAuthorityV2
{
    private IReadOnlyCollection<string> roleCodes = [];

    public IReadOnlyCollection<string> RoleCodes => new RoleCodesView(this);

    public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
        FinanceInvoiceAcceptanceKindV2 kind, Guid documentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (documentId == Guid.Empty || !SameContext(context)) return PermissionDecision.Denied;
        if (producer != FinanceInvoiceProducerV1.Sales || kind != FinanceInvoiceAcceptanceKindV2.CustomerReturn
            || !ErpPermissions.IsKnown(ErpPermissions.SalesReturnsFinalize))
        {
            return PermissionDecision.Unavailable;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.SalesReturnsFinalize, context.CompanyId,
            cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !SameContext(context)) return PermissionDecision.Denied;
        roleCodes = [.. scope.RoleCodes.Order(StringComparer.Ordinal)];
        return PermissionDecision.Allowed;
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private sealed class RoleCodesView(FinanceInvoiceAcceptanceAuthority owner) : IReadOnlyCollection<string>
    {
        public int Count => owner.roleCodes.Count;
        public IEnumerator<string> GetEnumerator() => owner.roleCodes.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
