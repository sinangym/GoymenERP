using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Shell;

public static class SalesFinanceCancellationHostExtensions
{
    public static IServiceCollection AddSalesFinanceCancellationHost(this IServiceCollection services)
    {
        services.AddScoped<SalesFinanceCancellationAuthorityV1>();
        services.AddScoped<IFinanceInvoiceOpenItemCancellationAuthorityV1>(p => p.GetRequiredService<SalesFinanceCancellationAuthorityV1>());
        services.AddScoped<IFinanceInvoiceOpenItemCancellationPortV1>(p =>
        {
            var authority = p.GetRequiredService<SalesFinanceCancellationAuthorityV1>();
            var correlation = p.GetRequiredService<ICorrelationContext>();
            return new FinanceInvoiceOpenItemCancellationService(p.GetRequiredService<FinanceDbContext>(),
                p.GetRequiredService<ICompanyContext>(), p.GetRequiredService<ICurrentUser>(), authority,
                p.GetRequiredService<IAuditEventCollector>(), new FinanceAuditEnvelope(ErpPermissions.SalesInvoicesFinalize,
                    authority.RoleCodes, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent), p.GetRequiredService<TimeProvider>());
        });
        services.AddScoped<ISalesFinanceCancellationPortV1, SalesFinanceCancellationAdapterV1>();
        return services;
    }
}

/// <summary>Separate-step delivery. No prices or plan are reconstructed after the Sales cancellation.
/// An original Waiting admission is offered under its original identities before cancellation is retried.
/// Allocation release is possible only when the explicit retry carried the user's confirmation.</summary>
public sealed class SalesFinanceCancellationAdapterV1(ISalesFinanceAdmissionPortV1 admission,
    IFinanceInvoiceOpenItemCancellationPortV1 cancellation, ISalesOfficialInvoiceCancellationStoreV1 sales,
    SalesFinanceCancellationAuthorityV1 authority) : ISalesFinanceCancellationPortV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<SalesFinanceCancellationAnswerV1> CancelAsync(SalesFinanceCancellationRequestV1 request, CancellationToken cancellationToken = default)
    {
        var context = new FinanceContext(request.CompanyId, request.ActorId, request.CorrelationId, request.CausationId);
        async Task Fence(CancellationToken ct)
        {
            if (await authority.MeasureAsync(context, ct) != PermissionDecision.Allowed) throw new UnauthorizedAccessException();
        }
        await Fence(cancellationToken);
        var source = await sales.ReadDeliveryAsync(request.CancellationId, Fence, cancellationToken);
        await Fence(cancellationToken);
        if (source is null || source.Cancellation.CompanyId != request.CompanyId || source.Cancellation.Reason != request.Reason
            || source.Cancellation.InvoiceId != request.Admission.InvoiceId
            || JsonSerializer.Serialize(source.Admission, Json) != JsonSerializer.Serialize(request.Admission, Json))
            return new("Pending", "cancellation_source_unavailable");
        if (!request.Admission.WasAdmitted)
        {
            var original = request.Admission;
            var answer = await admission.AdmitAsync(new(request.CompanyId, request.ActorId, request.CorrelationId,
                request.CausationId, Key("admit", request), original.AdmissionId, original.InvoiceId, original.InvoiceVersion,
                original.PartyId, original.CompanyAccountId, original.CurrencyId,
                original.ExchangeRate?.ToString("0.############################", CultureInfo.InvariantCulture), original.Instalments,
                original.OfficialInvoiceNo, original.Ettn), cancellationToken);
            await Fence(cancellationToken);
            if (answer.Outcome != SalesFinanceAdmissionOutcomeV1.Admitted) return new("Pending", "original_admission_pending");
        }
        var result = await cancellation.CancelAsync(context, new(Key("cancel", request)),
            new(request.CancellationId, FinanceInvoiceProducerV1.Sales, request.Admission.InvoiceId,
                request.Admission.InvoiceVersion, request.Reason, request.ReleaseActiveAllocations), cancellationToken);
        await Fence(cancellationToken);
        if (result.Outcome == CommandOutcome.Succeeded) return new("Cancelled");
        if (result.Issues.Any(x => x.Code == "finance_invoice_cancellation_has_allocations")) return new("AllocationConfirmationRequired");
        return new("Pending", "finance_cancellation_pending");
    }
    private static string Key(string step, SalesFinanceCancellationRequestV1 request) => "sales-cancel-" + step + ":"
        + request.CancellationId.ToString("N") + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.IdempotencyKey)))[..32];
}

/// <summary>Only the existing finalize authority and a real recorded Sales cancellation authorize this producer's call.
/// Neither approval authority in Accounting nor an external cancellation permission is inferred.</summary>
public sealed class SalesFinanceCancellationAuthorityV1(IErpAuthorityRevalidationService authority,
    ICompanyContext company, ICurrentUser user, ISalesOfficialInvoiceCancellationStoreV1 store)
    : IFinanceInvoiceOpenItemCancellationAuthorityV1
{
    private IReadOnlyCollection<string> roles = [];
    public IReadOnlyCollection<string> RoleCodes => new Roles(this);
    internal async Task<PermissionDecision> MeasureAsync(FinanceContext context, CancellationToken ct)
    {
        if (!Current(context)) return PermissionDecision.Denied;
        if (!ErpPermissions.IsKnown(ErpPermissions.SalesInvoicesFinalize)) return PermissionDecision.Unavailable;
        var answer = await authority.RevalidatePermissionScopeAsync(ErpPermissions.SalesInvoicesFinalize, context.CompanyId, ct);
        if (!answer.IsGranted || !answer.IsCompanyWide || !Current(context)) return PermissionDecision.Denied;
        roles = [.. answer.RoleCodes.Order(StringComparer.Ordinal)]; return PermissionDecision.Allowed;
    }
    public async Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer, Guid invoiceId, CancellationToken ct)
    {
        if (producer != FinanceInvoiceProducerV1.Sales || invoiceId == Guid.Empty) return PermissionDecision.Denied;
        var permission = await MeasureAsync(context, ct);
        if (permission != PermissionDecision.Allowed) return permission;
        async Task Fence(CancellationToken token) { if (await MeasureAsync(context, token) != PermissionDecision.Allowed) throw new UnauthorizedAccessException(); }
        try
        {
            var normal = await store.ReadForInvoiceAsync(SalesOfficialInvoiceKindV1.Normal, invoiceId, Fence, ct);
            var subcontract = await store.ReadForInvoiceAsync(SalesOfficialInvoiceKindV1.Subcontract, invoiceId, Fence, ct);
            if ((normal is null) == (subcontract is null)) return PermissionDecision.Unavailable;
            return await MeasureAsync(context, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Current(context) ? PermissionDecision.Unavailable : PermissionDecision.Denied; }
    }
    private bool Current(FinanceContext context) => company.IsResolved && company.CompanyId != Guid.Empty
        && company.CompanyId == context.CompanyId && user.IsAuthenticated && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
    private sealed class Roles(SalesFinanceCancellationAuthorityV1 owner) : IReadOnlyCollection<string>
    {
        public int Count => owner.roles.Count;
        public IEnumerator<string> GetEnumerator() => owner.roles.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
