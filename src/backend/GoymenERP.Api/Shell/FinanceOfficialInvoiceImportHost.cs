using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Api.Shell;

public static class FinanceOfficialInvoiceImportHostExtensions
{
    public static IServiceCollection AddFinanceOfficialInvoiceImportHost(this IServiceCollection services)
    {
        services.AddSingleton<IFinanceOfficialInvoiceImportParserV1, FinanceOfficialInvoiceImportParserV1>();
        services.AddScoped<FinanceOfficialInvoiceImportAuthority>();
        services.AddScoped<FinanceOfficialInvoiceImportPendingApprovalAuthority>();
        services.AddScoped<IFinanceOfficialInvoiceImportApprovalAuthorityV1>(provider =>
            provider.GetRequiredService<FinanceOfficialInvoiceImportAuthority>());
        services.AddScoped<IFinanceOpenItemSourceAuthority>(provider =>
            provider.GetRequiredService<FinanceOfficialInvoiceImportAuthority>());

        services.AddScoped<FinanceOfficialInvoiceImportApprovalServiceV1>(CreateApprovalService);
        services.AddScoped<IFinanceOfficialInvoiceImportApprovalServiceV1>(provider =>
            provider.GetRequiredService<FinanceOfficialInvoiceImportApprovalServiceV1>());
        services.AddScoped<IApprovedFinanceOpenItemSourcePort>(provider =>
            provider.GetRequiredService<FinanceOfficialInvoiceImportApprovalServiceV1>());
        services.AddScoped<IApprovedFinanceOpenItemSourceCatalogPort>(provider =>
            provider.GetRequiredService<FinanceOfficialInvoiceImportApprovalServiceV1>());

        services.AddScoped<IFinanceOfficialInvoiceImportReadPortV1, FinanceOfficialInvoiceImportReadPortV1>();
        services.AddScoped<IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1>(CreatePendingApprovalReadService);
        services.AddScoped<IFinanceOpenItemSourceAdmissionService>(CreateAdmissionService);
        services.AddScoped<IFinanceOpenItemSourceSelectionService, FinanceOpenItemSourceSelectionService>();
        services.AddScoped<IFinanceOfficialInvoiceImportApplicationServiceV1,
            FinanceOfficialInvoiceImportApplicationServiceV1>();
        return services;
    }

    private static FinanceOfficialInvoiceImportApprovalServiceV1 CreateApprovalService(IServiceProvider provider)
    {
        var authority = provider.GetRequiredService<FinanceOfficialInvoiceImportAuthority>();
        var correlation = provider.GetRequiredService<ICorrelationContext>();
        return new FinanceOfficialInvoiceImportApprovalServiceV1(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IFinanceOfficialInvoiceImportParserV1>(),
            authority,
            authority,
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceOfficialInvoiceImportAuditEnvelopesV1(
                Envelope(FinanceOfficialInvoiceImportPermissionNamesV1.Upload,
                    authority.RoleCodesOf(FinanceOfficialInvoiceImportAuthorityActionV1.Upload), correlation),
                Envelope(FinanceOfficialInvoiceImportPermissionNamesV1.Approve,
                    authority.RoleCodesOf(FinanceOfficialInvoiceImportAuthorityActionV1.Approve), correlation)),
            provider.GetRequiredService<TimeProvider>());
    }

    private static FinanceOfficialInvoiceImportPendingApprovalReadServiceV1 CreatePendingApprovalReadService(
        IServiceProvider provider) =>
        new(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IFinanceOfficialInvoiceImportParserV1>(),
            provider.GetRequiredService<FinanceOfficialInvoiceImportPendingApprovalAuthority>());

    private static FinanceOpenItemSourceAdmissionService CreateAdmissionService(IServiceProvider provider)
    {
        var permissions = provider.GetRequiredService<FinancePermissionPort>();
        var correlation = provider.GetRequiredService<ICorrelationContext>();
        return new FinanceOpenItemSourceAdmissionService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IFinanceOpenItemSourceAuthority>(),
            provider.GetRequiredService<IApprovedFinanceOpenItemSourcePort>(),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            provider.GetRequiredService<IAuditEventCollector>(),
            Envelope(FinancePermissionNames.DraftRead, permissions.RoleCodesOf(FinanceAction.ReadSources), correlation),
            provider.GetRequiredService<TimeProvider>());
    }

    private static FinanceAuditEnvelope Envelope(string permission,
        IReadOnlyCollection<string> roleCodes, ICorrelationContext correlation) =>
        new(permission, roleCodes, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
}

/// <summary>
/// Fresh company-wide authority for the two new import commands. Approved-source reads and admission
/// deliberately reuse Finance's existing source-read action; this host declares no third permission.
/// </summary>
public sealed class FinanceOfficialInvoiceImportAuthority(
    IErpAuthorityRevalidationService authority,
    FinancePermissionPort financePermissions,
    ICompanyContext companyContext,
    ICurrentUser currentUser) :
    IFinanceOfficialInvoiceImportApprovalAuthorityV1,
    IFinanceOpenItemSourceAuthority
{
    private readonly Dictionary<FinanceOfficialInvoiceImportAuthorityActionV1, IReadOnlyCollection<string>> roleCodes = [];

    public IReadOnlyCollection<string> RoleCodesOf(FinanceOfficialInvoiceImportAuthorityActionV1 action) =>
        new RoleCodesView(this, action);

    public async Task<PermissionDecision> CheckAsync(FinanceContext context,
        FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (batchId == Guid.Empty) return PermissionDecision.Denied;

        return await CheckCompanyWideAsync(context, action, cancellationToken);
    }

    internal async Task<PermissionDecision> CheckCompanyWideAsync(FinanceContext context,
        FinanceOfficialInvoiceImportAuthorityActionV1 action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context)) return PermissionDecision.Denied;

        var permission = action switch
        {
            FinanceOfficialInvoiceImportAuthorityActionV1.Upload =>
                FinanceOfficialInvoiceImportPermissionNamesV1.Upload,
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve =>
                FinanceOfficialInvoiceImportPermissionNamesV1.Approve,
            _ => null,
        };
        if (permission is null || !ErpPermissions.IsKnown(permission)) return PermissionDecision.Unavailable;

        var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !SameContext(context)) return PermissionDecision.Denied;
        roleCodes[action] = [.. scope.RoleCodes.Order(StringComparer.Ordinal)];
        return PermissionDecision.Allowed;
    }

    Task<PermissionDecision> IFinanceOpenItemSourceAuthority.CheckAsync(FinanceContext context, Guid batchId,
        CancellationToken cancellationToken) =>
        financePermissions.CheckAsync(context, FinanceAction.ReadSources, batchId, cancellationToken);

    private IReadOnlyCollection<string> Current(FinanceOfficialInvoiceImportAuthorityActionV1 action) =>
        roleCodes.TryGetValue(action, out var codes) ? codes : [];

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private sealed class RoleCodesView(FinanceOfficialInvoiceImportAuthority owner,
        FinanceOfficialInvoiceImportAuthorityActionV1 action) : IReadOnlyCollection<string>
    {
        public int Count => owner.Current(action).Count;
        public IEnumerator<string> GetEnumerator() => owner.Current(action).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>
/// Read-only pending-list adapter. The list has no batch identity, but it still performs the same
/// fresh company-wide approval-permission fence. Commands keep the stricter non-empty batch rule.
/// </summary>
public sealed class FinanceOfficialInvoiceImportPendingApprovalAuthority(
    FinanceOfficialInvoiceImportAuthority authority) : IFinanceOfficialInvoiceImportApprovalAuthorityV1
{
    public Task<PermissionDecision> CheckAsync(FinanceContext context,
        FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken) =>
        batchId == Guid.Empty
            ? authority.CheckCompanyWideAsync(context, action, cancellationToken)
            : authority.CheckAsync(context, action, batchId, cancellationToken);
}
