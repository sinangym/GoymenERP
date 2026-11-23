using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PurchaseInvoiceFinanceAdmissionHostTests
{
    private static readonly Guid Company = Guid.Parse("7cd10000-0000-4000-8000-000000000001");
    private static readonly Guid Invoice = Guid.Parse("7cd10000-0000-4000-8000-000000000002");

    [Theory]
    [InlineData(CommandOutcome.Succeeded, PurchaseInvoiceFinanceAdmissionOutcomeV1.Admitted)]
    [InlineData(CommandOutcome.Denied, PurchaseInvoiceFinanceAdmissionOutcomeV1.Refused)]
    [InlineData(CommandOutcome.ValidationFailed, PurchaseInvoiceFinanceAdmissionOutcomeV1.Refused)]
    [InlineData(CommandOutcome.IdempotencyConflict, PurchaseInvoiceFinanceAdmissionOutcomeV1.KeyConflict)]
    [InlineData(CommandOutcome.VersionConflict, PurchaseInvoiceFinanceAdmissionOutcomeV1.Retry)]
    [InlineData(CommandOutcome.Unavailable, PurchaseInvoiceFinanceAdmissionOutcomeV1.Unavailable)]
    public async Task The_adapter_preserves_the_supplier_official_amounts_and_only_Finance_success_is_admitted(
        CommandOutcome outcome, PurchaseInvoiceFinanceAdmissionOutcomeV1 expected)
    {
        var finance = new FinancePort(outcome);
        var port = new PurchaseInvoiceFinanceAdmissionAdapter(finance);
        var request = new PurchaseInvoiceFinanceAdmissionRequestV1(Company, "SENTETIK-actor", "correlation", "cause", "stable-key",
            Guid.NewGuid(), Invoice, "3", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "40.25",
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 12, 1), "10.01", "402.90")]);
        var result = await port.AdmitAsync(request);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(new FinanceContext(Company, request.ActorId, request.CorrelationId, request.CausationId), finance.Context);
        Assert.Equal(request.IdempotencyKey, finance.Metadata!.IdempotencyKey);
        Assert.Equal((FinanceInvoiceProducerV1.Purchasing, request.AdmissionId, Invoice, "3", request.SupplierPartyId, request.CompanyAccountId),
            (finance.Invoice!.Producer, finance.Invoice.AdmissionId, finance.Invoice.InvoiceId, finance.Invoice.InvoiceVersion,
                finance.Invoice.PartyId, finance.Invoice.CompanyAccountId));
        Assert.Equal("40.25", finance.Invoice.ExchangeRate);
        Assert.Equal(("10.01", "402.90"), (finance.Invoice.Installments[0].Amount, finance.Invoice.Installments[0].FunctionalAmount));
    }

    [Fact]
    public void Purchasing_registration_does_not_replace_the_Sales_Finance_authority_or_audit_composition()
    {
        var services = new ServiceCollection();
        services.AddSalesFinanceAdmissionHost();
        var authority = Assert.Single(services, item => item.ServiceType == typeof(IFinanceInvoiceOpenItemAdmissionAuthorityV1));
        var admission = Assert.Single(services, item => item.ServiceType == typeof(IFinanceInvoiceOpenItemAdmissionPortV1));
        services.AddPurchaseInvoiceFinanceAdmissionHost();
        Assert.Same(authority, Assert.Single(services, item => item.ServiceType == typeof(IFinanceInvoiceOpenItemAdmissionAuthorityV1)));
        Assert.Same(admission, Assert.Single(services, item => item.ServiceType == typeof(IFinanceInvoiceOpenItemAdmissionPortV1)));
        Assert.Single(services, item => item.ServiceType == typeof(IPurchaseInvoiceFinanceAdmissionPortV1));
    }

    [Fact]
    public async Task The_Finance_fence_refuses_cross_company_and_wrong_producer_before_querying_authority()
    {
        var company = new MutableCompany();
        var revalidation = new Authority();
        var fence = new PurchaseInvoiceFinanceAdmissionAuthority(revalidation, company, new User());
        Assert.Equal(PermissionDecision.Denied,
            await fence.CheckAsync(new(Guid.NewGuid(), "SENTETIK-actor", "c", null), FinanceInvoiceProducerV1.Purchasing, Invoice, default));
        Assert.Equal(PermissionDecision.Unavailable,
            await fence.CheckAsync(Context(), FinanceInvoiceProducerV1.Sales, Invoice, default));
        Assert.Empty(revalidation.Calls);
    }

    [Fact]
    public async Task The_Finance_fence_rechecks_revocation_and_rejects_company_or_actor_drift_during_the_read()
    {
        var company = new MutableCompany();
        var user = new User(); var revalidation = new Authority();
        var fence = new PurchaseInvoiceFinanceAdmissionAuthority(revalidation, company, user);
        Assert.Equal(PermissionDecision.Allowed, await fence.CheckAsync(Context(), FinanceInvoiceProducerV1.Purchasing, Invoice, default));
        Assert.Equal(new[] { "SENTETIK-ROLE" }, fence.RoleCodes);
        revalidation.Answer = PermissionScope.Denied;
        Assert.Equal(PermissionDecision.Denied, await fence.CheckAsync(Context(), FinanceInvoiceProducerV1.Purchasing, Invoice, default));
        revalidation.Answer = Allowed(); revalidation.DuringRead = () => user.Actor = "SENTETIK-other";
        Assert.Equal(PermissionDecision.Denied, await fence.CheckAsync(Context(), FinanceInvoiceProducerV1.Purchasing, Invoice, default));
        user.Actor = "SENTETIK-actor"; revalidation.DuringRead = () => company.CompanyId = Guid.NewGuid();
        Assert.Equal(PermissionDecision.Denied, await fence.CheckAsync(Context(), FinanceInvoiceProducerV1.Purchasing, Invoice, default));
        Assert.All(revalidation.Calls, permission => Assert.Equal(PurchaseIncomingInvoicePermissionNamesV1.Finalize, permission));
    }

    private static FinanceContext Context() => new(Company, "SENTETIK-actor", "c", null);
    private static PermissionScope Allowed() => new(true, true, new HashSet<string> { "SENTETIK-ROLE" },
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }
    private sealed class User : ICurrentUser
    {
        public string Actor { get; set; } = "SENTETIK-actor";
        public bool IsAuthenticated => true;
        public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public List<string> Calls { get; } = [];
        public PermissionScope Answer { get; set; } = Allowed();
        public Action? DuringRead { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Calls.Add(permission); DuringRead?.Invoke(); return Task.FromResult(Answer); }
    }
    private sealed class FinancePort(CommandOutcome outcome) : IFinanceInvoiceOpenItemAdmissionPortV1
    {
        public FinanceContext? Context { get; private set; }
        public CommandMetadata? Metadata { get; private set; }
        public FinanceInvoiceOpenItemAdmissionV1? Invoice { get; private set; }
        public Task<FinanceOpenItemAdmissionResult> AdmitAsync(FinanceContext context, CommandMetadata metadata,
            FinanceInvoiceOpenItemAdmissionV1 invoice, CancellationToken cancellationToken)
        {
            Context = context; Metadata = metadata; Invoice = invoice;
            return Task.FromResult(new FinanceOpenItemAdmissionResult(outcome, [], false, context.CorrelationId, []));
        }
    }
}
