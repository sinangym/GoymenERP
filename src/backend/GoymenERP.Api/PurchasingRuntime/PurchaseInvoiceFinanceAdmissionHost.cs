using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>Producer-specific composition. Does not replace Sales' admission authority or its audit envelope.</summary>
public static class PurchaseInvoiceFinanceAdmissionHost
{
    public static IServiceCollection AddPurchaseInvoiceFinanceAdmissionHost(this IServiceCollection services)
    {
        services.AddScoped<PurchaseInvoiceFinanceAdmissionAuthority>();
        services.AddScoped<IPurchaseInvoiceFinanceAdmissionPortV1>(provider =>
        {
            var authority = provider.GetRequiredService<PurchaseInvoiceFinanceAdmissionAuthority>();
            var correlation = provider.GetRequiredService<ICorrelationContext>();
            var finance = new FinanceInvoiceOpenItemAdmissionService(provider.GetRequiredService<FinanceDbContext>(),
                provider.GetRequiredService<ICompanyContext>(), provider.GetRequiredService<ICurrentUser>(),
                provider.GetRequiredService<IFinancePartyAccountPort>(), authority,
                provider.GetRequiredService<IAuditEventCollector>(),
                new FinanceAuditEnvelope(PurchaseIncomingInvoicePermissionNamesV1.Finalize, authority.RoleCodes,
                    correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent),
                provider.GetRequiredService<TimeProvider>());
            return new PurchaseInvoiceFinanceAdmissionAdapter(finance);
        });
        return services;
    }
}

public sealed class PurchaseInvoiceFinanceAdmissionAdapter(IFinanceInvoiceOpenItemAdmissionPortV1 finance)
    : IPurchaseInvoiceFinanceAdmissionPortV1
{
    public async Task<PurchaseInvoiceFinanceAdmissionAnswerV1> AdmitAsync(PurchaseInvoiceFinanceAdmissionRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await finance.AdmitAsync(new(request.CompanyId, request.ActorId, request.CorrelationId, request.CausationId),
            new(request.IdempotencyKey), new(request.AdmissionId, FinanceInvoiceProducerV1.Purchasing, request.InvoiceId,
                request.FinalizedVersion, request.SupplierPartyId, request.CompanyAccountId, request.CurrencyId, request.OfficialRate,
                request.Installments.Select(item => new FinanceInvoiceInstallmentV1(item.OpenItemId, item.InstallmentId,
                    item.Sequence, item.DueOn, item.Amount, item.FunctionalAmount)).ToArray()), cancellationToken);
        return result.Outcome switch
        {
            CommandOutcome.Succeeded => new(PurchaseInvoiceFinanceAdmissionOutcomeV1.Admitted, "admitted"),
            CommandOutcome.IdempotencyConflict => new(PurchaseInvoiceFinanceAdmissionOutcomeV1.KeyConflict, "key_conflict"),
            CommandOutcome.VersionConflict => new(PurchaseInvoiceFinanceAdmissionOutcomeV1.Retry, "version_conflict"),
            CommandOutcome.Denied => new(PurchaseInvoiceFinanceAdmissionOutcomeV1.Refused, "denied"),
            CommandOutcome.ValidationFailed => new(PurchaseInvoiceFinanceAdmissionOutcomeV1.Refused,
                result.Issues.FirstOrDefault()?.Code ?? "refused"),
            _ => PurchaseInvoiceFinanceAdmissionAnswerV1.Unavailable,
        };
    }
}

public sealed class PurchaseInvoiceFinanceAdmissionAuthority(IErpAuthorityRevalidationService authority,
    ICompanyContext company, ICurrentUser user) : IFinanceInvoiceOpenItemAdmissionAuthorityV1
{
    private IReadOnlyCollection<string> currentRoles = [];
    public IReadOnlyCollection<string> RoleCodes => new RoleView(this);

    public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
        Guid invoiceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context) || invoiceId == Guid.Empty) return PermissionDecision.Denied;
        if (producer != FinanceInvoiceProducerV1.Purchasing || !ErpPermissions.IsKnown(PurchaseIncomingInvoicePermissionNamesV1.Finalize))
            return PermissionDecision.Unavailable;
        var scope = await authority.RevalidatePermissionScopeAsync(PurchaseIncomingInvoicePermissionNamesV1.Finalize,
            context.CompanyId, cancellationToken);
        if (!SameContext(context) || !scope.IsGranted || !scope.IsCompanyWide) return PermissionDecision.Denied;
        currentRoles = scope.RoleCodes.Order(StringComparer.Ordinal).ToArray();
        return PermissionDecision.Allowed;
    }

    private bool SameContext(FinanceContext context) => company.IsResolved && company.CompanyId != Guid.Empty
        && company.CompanyId == context.CompanyId && user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.UserId)
        && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);

    private sealed class RoleView(PurchaseInvoiceFinanceAdmissionAuthority owner) : IReadOnlyCollection<string>
    {
        public int Count => owner.currentRoles.Count;
        public IEnumerator<string> GetEnumerator() => owner.currentRoles.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
