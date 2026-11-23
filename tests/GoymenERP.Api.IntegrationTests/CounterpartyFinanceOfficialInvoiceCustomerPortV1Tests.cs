using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyFinanceOfficialInvoiceCustomerPortV1Tests
{
    private static readonly Guid Company = Guid.Parse("f5360000-0000-4000-8000-000000000001");
    private static readonly Guid Party = Guid.Parse("f5360000-0000-4000-8000-000000000002");
    private static readonly Guid Account = Guid.Parse("f5360000-0000-4000-8000-000000000003");
    private static FinanceContext Context() => new(Company, "SENTETIK-maker", "SENTETIK", null);

    [Fact]
    public async Task Official_invoice_customer_is_the_real_company_party_without_inventing_a_historical_active_or_role_rule()
    {
        var company = CompanyContext(); var user = new User();
        var port = new CounterpartyFinanceOfficialInvoiceCustomerPortV1(company, user, new Owner(Detail()));
        Assert.Equal(ReadOutcome.Found, await port.CheckCompanyPartyAsync(Context(), Party, default));
        var foreign = Detail() with { CompanyAccounts = [new(Account, Guid.NewGuid(), PartyRole.Customer, true, "4")] };
        Assert.Equal(ReadOutcome.NotFound, await new CounterpartyFinanceOfficialInvoiceCustomerPortV1(company, user, new Owner(foreign))
            .CheckCompanyPartyAsync(Context(), Party, default));
        Assert.Equal(ReadOutcome.Unavailable, await new CounterpartyFinanceOfficialInvoiceCustomerPortV1(company, user, new Owner(Detail() with { Version = "invalid" }))
            .CheckCompanyPartyAsync(Context(), Party, default));
    }

    [Fact]
    public async Task Changed_actor_or_company_after_owner_read_does_not_release_membership_or_not_found()
    {
        var company = new MutableCompany(Company); var user = new User();
        var owner = new Owner(Detail()) { DuringRead = () => user.UserId = "SENTETIK-other" };
        Assert.Equal(ReadOutcome.Denied, await new CounterpartyFinanceOfficialInvoiceCustomerPortV1(company, user, owner).CheckCompanyPartyAsync(Context(), Party, default));
        var foreignCompany = Guid.NewGuid();
        owner = new Owner(null) { DuringRead = () => company.Set(foreignCompany) };
        Assert.Equal(ReadOutcome.Denied, await new CounterpartyFinanceOfficialInvoiceCustomerPortV1(company, new User(), owner).CheckCompanyPartyAsync(Context(), Party, default));
        Assert.Equal(foreignCompany, company.CompanyId);
    }
    private static CompanyContext CompanyContext() { var value = new CompanyContext(); value.Set(Company); return value; }
    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }
    private static CounterpartyDetailView Detail() => new(Party, "SENTETIK-1", PartyKind.LegalEntity,
        "SENTETIK kaynak cari", null, false, "3", [new(Account, Company, PartyRole.None, false, "4")]);
    private sealed class Owner(CounterpartyDetailView? value) : ICounterpartyReadService
    {
        public Action? DuringRead { get; init; }
        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
        { DuringRead?.Invoke(); return Task.FromResult(value); }
        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = "SENTETIK-maker";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
