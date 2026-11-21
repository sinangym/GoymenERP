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
/// F-019 / API-11 v2 host composition: Finance owns the customer return acceptance port
/// (<see cref="IFinanceInvoiceAcceptancePortV2"/>); this host fences it with a fresh company-wide measurement of
/// <c>sales.returns.finalize</c> (SAT-D290: the return invoice is finalised by that grant; SAT-D208: Sales commits first
/// and re-drives with the same key). FIN-S10 (FNS-D44) adds the read side of the same port,
/// <see cref="IFinanceCustomerReturnInstallmentReadPortV2"/> (the screen's nearest-due prefill), answered by the same
/// Finance service under the same fence; no new permission is introduced. SAT-S2 composes the Sales consumer port
/// (<see cref="ISalesCustomerReturnFinancePortV1"/>) onto both through <see cref="SalesCustomerReturnFinanceAdapterV1"/>, so
/// Sales' customer return finalisation (API-40) no longer answers 503 in a host with this composition.
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
        services.AddScoped<ISalesCustomerReturnFinancePortV1, SalesCustomerReturnFinanceAdapterV1>();
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
/// Adapts Sales' customer return consumer port onto Finance's acceptance port v2 and its installment read (API-11 v2,
/// FNS-D44/FNS-D81). The acceptance identity is the return document itself; only a succeeded Finance outcome is "admitted".
/// </summary>
public sealed class SalesCustomerReturnFinanceAdapterV1(
    IFinanceInvoiceAcceptancePortV2 acceptance,
    IFinanceCustomerReturnInstallmentReadPortV2 installments) : ISalesCustomerReturnFinancePortV1
{
    private const string RefusedReason = "refused";

    public async Task<SalesCustomerReturnFinanceAnswerV1> OfferAsync(SalesCustomerReturnFinanceOfferV1 offer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var result = await acceptance.AcceptAsync(
            new FinanceContext(offer.CompanyId, offer.ActorId, offer.CorrelationId, offer.CausationId),
            new CommandMetadata(offer.IdempotencyKey), ToAcceptance(offer), cancellationToken);
        return Map(offer.CustomerReturnId, result);
    }

    public async Task<SalesCustomerReturnInstallmentAnswerV1> ReadInstallmentsAsync(SalesCustomerReturnInstallmentQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var prefill = await installments.ReadAsync(
            new FinanceContext(query.CompanyId, query.ActorId, query.CorrelationId, query.CausationId),
            new FinanceCustomerReturnInstallmentQueryV2(FinanceInvoiceProducerV1.Sales, FinanceInvoiceAcceptanceKindV2.CustomerReturn,
                query.CustomerReturnId, query.OriginalInvoiceId, query.OriginalInvoiceVersion, query.Amount),
            cancellationToken);
        return Map(prefill);
    }

    internal static FinanceInvoiceAcceptanceV2 ToAcceptance(SalesCustomerReturnFinanceOfferV1 offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return new FinanceInvoiceAcceptanceV2(
            offer.CustomerReturnId,
            FinanceInvoiceProducerV1.Sales,
            FinanceInvoiceAcceptanceKindV2.CustomerReturn,
            offer.CustomerReturnId,
            offer.DocumentVersion,
            offer.OriginalInvoiceId,
            offer.OriginalInvoiceVersion,
            offer.PartyId,
            offer.CompanyAccountId,
            offer.CurrencyId,
            offer.Amount,
            offer.FunctionalAmount,
            offer.BusinessDate,
            offer.Allocations is null
                ? null
                : [.. offer.Allocations.Select(item => new FinanceInvoiceAcceptanceAllocationV2(item.OpenItemId, item.Amount))]);
    }

    internal static SalesCustomerReturnFinanceAnswerV1 Map(Guid customerReturnId, FinanceInvoiceAcceptanceResultV2 result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var (outcome, reason) = Map(result.Outcome, result.Issues);
        return new SalesCustomerReturnFinanceAnswerV1(customerReturnId, outcome, reason);
    }

    internal static SalesCustomerReturnInstallmentAnswerV1 Map(FinanceCustomerReturnInstallmentPrefillV2 prefill)
    {
        ArgumentNullException.ThrowIfNull(prefill);
        var (outcome, reason) = Map(prefill.Outcome, prefill.Issues);
        if (outcome != SalesCustomerReturnFinanceOutcomeV1.Admitted)
        {
            return new SalesCustomerReturnInstallmentAnswerV1(outcome, reason, false, null, null, []);
        }

        return new SalesCustomerReturnInstallmentAnswerV1(outcome, reason, prefill.SelectionAllowed, prefill.AppliedAmount,
            prefill.AdvanceAmount, Installments(prefill.Installments));
    }

    // A malformed Finance answer (null list or item) is carried through as null so Sales fails closed (503) on it.
    private static IReadOnlyList<SalesCustomerReturnFinanceInstallmentV1> Installments(
        IReadOnlyList<FinanceCustomerReturnInstallmentV2>? items)
    {
        if (items is null)
        {
            return null!;
        }

        var mapped = new List<SalesCustomerReturnFinanceInstallmentV1>(items.Count);
        foreach (var item in items)
        {
            mapped.Add(item is null
                ? null!
                : new SalesCustomerReturnFinanceInstallmentV1(item.OpenItemId, item.DueOn, item.OriginalAmount,
                    item.OutstandingAmount, item.PrefillAmount));
        }

        return mapped;
    }

    private static (SalesCustomerReturnFinanceOutcomeV1 Outcome, string? Reason) Map(CommandOutcome outcome,
        IReadOnlyList<ValidationIssue>? issues) => outcome switch
    {
        CommandOutcome.Succeeded => (SalesCustomerReturnFinanceOutcomeV1.Admitted, null),
        CommandOutcome.ValidationFailed => (SalesCustomerReturnFinanceOutcomeV1.Refused,
            issues is { Count: > 0 } && issues[0] is { } first && !string.IsNullOrWhiteSpace(first.Code) ? first.Code : RefusedReason),
        CommandOutcome.IdempotencyConflict => (SalesCustomerReturnFinanceOutcomeV1.KeyConflict, null),
        CommandOutcome.VersionConflict => (SalesCustomerReturnFinanceOutcomeV1.Retry, null),
        CommandOutcome.Denied => (SalesCustomerReturnFinanceOutcomeV1.Denied, null),
        _ => (SalesCustomerReturnFinanceOutcomeV1.Unavailable, null),
    };
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
