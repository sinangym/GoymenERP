using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Shell;

/// <summary>
/// S5 API-36 host composition: Sales declares <see cref="ISalesFinanceAdmissionPortV1"/> as the consumer, Finance owns the F2a admission
/// port; this host (the only place that knows both) adapts one to the other. Finance's admission is fenced here by a fresh company-wide
/// measurement of <c>sales.invoices.finalize</c> (SAT-D208: Sales commits first and re-drives with the same key).
/// </summary>
public static class SalesFinanceAdmissionHostExtensions
{
    public static IServiceCollection AddSalesFinanceAdmissionHost(this IServiceCollection services)
    {
        services.AddScoped<SalesFinanceAdmissionAuthority>();
        services.AddScoped<IFinanceInvoiceOpenItemAdmissionAuthorityV1>(provider =>
            provider.GetRequiredService<SalesFinanceAdmissionAuthority>());
        services.AddScoped<IFinanceInvoiceOpenItemAdmissionPortV1>(CreateAdmissionService);
        services.AddScoped<ISalesFinanceAdmissionPortV1, SalesFinanceAdmissionAdapterV1>();
        return services;
    }

    private static FinanceInvoiceOpenItemAdmissionService CreateAdmissionService(IServiceProvider provider)
    {
        var authority = provider.GetRequiredService<SalesFinanceAdmissionAuthority>();
        var correlation = provider.GetRequiredService<ICorrelationContext>();
        return new FinanceInvoiceOpenItemAdmissionService(
            provider.GetRequiredService<FinanceDbContext>(),
            provider.GetRequiredService<ICompanyContext>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IFinancePartyAccountPort>(),
            authority,
            provider.GetRequiredService<IAuditEventCollector>(),
            new FinanceAuditEnvelope(ErpPermissions.SalesInvoicesFinalize, authority.RoleCodes, correlation.SourceChannel,
                correlation.ClientIpAddress, correlation.UserAgent),
            provider.GetRequiredService<TimeProvider>());
    }
}

/// <summary>Maps Finance's command outcome onto Sales' small answer; only a succeeded admission is "admitted".</summary>
public sealed class SalesFinanceAdmissionAdapterV1(IFinanceInvoiceOpenItemAdmissionPortV1 finance) : ISalesFinanceAdmissionPortV1
{
    public async Task<SalesFinanceAdmissionAnswerV1> AdmitAsync(SalesFinanceAdmissionRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = new FinanceInvoiceOpenItemAdmissionV1(
            request.AdmissionId,
            FinanceInvoiceProducerV1.Sales,
            request.InvoiceId,
            request.InvoiceVersion,
            request.PartyId,
            request.CompanyAccountId,
            request.CurrencyId,
            request.ExchangeRate,
            [.. request.Instalments.Select(item => new FinanceInvoiceInstallmentV1(item.OpenItemId, item.InstallmentId,
                item.SequenceNo, item.DueDate, item.Amount, item.FunctionalAmount))]);
        var result = await finance.AdmitAsync(
            new FinanceContext(request.CompanyId, request.ActorId, request.CorrelationId, request.CausationId),
            new CommandMetadata(request.IdempotencyKey), invoice, cancellationToken);
        return Map(result);
    }

    internal static SalesFinanceAdmissionAnswerV1 Map(FinanceOpenItemAdmissionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            CommandOutcome.Succeeded => new SalesFinanceAdmissionAnswerV1(SalesFinanceAdmissionOutcomeV1.Admitted, "admitted"),
            CommandOutcome.ValidationFailed => new SalesFinanceAdmissionAnswerV1(SalesFinanceAdmissionOutcomeV1.Refused,
                result.Issues.Count > 0 ? result.Issues[0].Code : "refused"),
            CommandOutcome.IdempotencyConflict => new SalesFinanceAdmissionAnswerV1(SalesFinanceAdmissionOutcomeV1.KeyConflict, "key_conflict"),
            CommandOutcome.Denied => new SalesFinanceAdmissionAnswerV1(SalesFinanceAdmissionOutcomeV1.Retry, "denied"),
            CommandOutcome.VersionConflict => new SalesFinanceAdmissionAnswerV1(SalesFinanceAdmissionOutcomeV1.Retry, "version_conflict"),
            _ => SalesFinanceAdmissionAnswerV1.Unavailable,
        };
    }
}

/// <summary>Fresh company-wide measurement of <c>sales.invoices.finalize</c> for Finance's admission; fails closed.</summary>
public sealed class SalesFinanceAdmissionAuthority(
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : IFinanceInvoiceOpenItemAdmissionAuthorityV1
{
    private IReadOnlyCollection<string> roleCodes = [];

    public IReadOnlyCollection<string> RoleCodes => new RoleCodesView(this);

    public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer, Guid invoiceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (invoiceId == Guid.Empty || !SameContext(context)) return PermissionDecision.Denied;
        if (producer != FinanceInvoiceProducerV1.Sales || !ErpPermissions.IsKnown(ErpPermissions.SalesInvoicesFinalize))
        {
            return PermissionDecision.Unavailable;
        }

        var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.SalesInvoicesFinalize, context.CompanyId,
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

    private sealed class RoleCodesView(SalesFinanceAdmissionAuthority owner) : IReadOnlyCollection<string>
    {
        public int Count => owner.roleCodes.Count;
        public IEnumerator<string> GetEnumerator() => owner.roleCodes.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
