using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class FinanceJournalSourceServiceTests
{
    private static readonly Guid Company = Guid.Parse("b2892000-0000-4000-8000-000000000001");
    [Fact]
    public async Task Denied_read_never_reads_the_source_store()
    {
        var fixture = new Fixture(false); var result = await fixture.Service.ListAsync(default);
        Assert.Equal(AccountingApiStatus.Denied, result.Status); Assert.Equal(0, fixture.Store.Calls);
    }
    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("permission")]
    [InlineData("foreign-row")]
    public async Task Late_actor_company_authority_or_foreign_row_exposes_no_source(string change)
    {
        var fixture = new Fixture(true);
        fixture.Store.DuringRead = () =>
        {
            if (change == "actor") fixture.User.UserId = "SENTETIK-other";
            if (change == "company") fixture.Company.Id = Guid.NewGuid();
            if (change == "permission") fixture.Authority.Allowed = false;
            if (change == "foreign-row") fixture.Store.Items = [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                NormalJournalSourceKindsV1.RecordedExpensePayment, "3", Guid.NewGuid(), "100.01", "SENTETIK", "1",
                new(2026, 11, 17), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Received", "BindingUnavailable", null)];
        };
        var result = await fixture.Service.ListAsync(default);
        Assert.Equal(AccountingApiStatus.Denied, result.Status); Assert.Null(result.Resource);
    }
    [Fact]
    public async Task Empty_authorized_company_is_an_explicit_empty_success_without_creating_a_journal()
    {
        var fixture = new Fixture(true); var result = await fixture.Service.ListAsync(default);
        Assert.Equal(AccountingApiStatus.Ok, result.Status); Assert.Equal(Company, result.Resource!.CompanyId);
        Assert.Empty(result.Resource.Items); Assert.Equal(2, fixture.Authority.Calls);
    }
    private sealed class Fixture
    {
        public Store Store { get; } = new(); public User User { get; } = new(); public MutableCompany Company { get; } = new();
        public Authority Authority { get; } public FinanceJournalSourceServiceV1 Service { get; }
        public Fixture(bool allowed) { Authority = new() { Allowed = allowed }; Service = new(Store, Authority, Company, User); }
    }
    private sealed class Store : IFinanceJournalSourceStoreV1
    {
        public int Calls; public Action? DuringRead; public IReadOnlyList<FinanceJournalSourceViewV1> Items = [];
        public Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<FinanceJournalSourceViewV1>> ListAsync(CancellationToken token)
        { Calls++; DuringRead?.Invoke(); return Task.FromResult(Items); }
    }
    private sealed class MutableCompany : ICompanyContext { public Guid Id = Company; public Guid CompanyId => Id; public bool IsResolved => Id != Guid.Empty; public void Set(Guid id) => Id = id; }
    private sealed class User : ICurrentUser
    {
        public string? UserId { get; set; } = "SENTETIK-reader"; public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid id) => id == Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed; public int Calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        {
            Calls++; Assert.Equal(ErpPermissions.AccountingJournalsRead, permission); Assert.Equal(Company, companyId);
            return Task.FromResult(Allowed ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
        }
    }
}
