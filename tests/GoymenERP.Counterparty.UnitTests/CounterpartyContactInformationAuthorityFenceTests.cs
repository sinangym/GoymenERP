using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyContactInformationAuthorityFenceTests
{
    private static readonly Guid CompanyA = Guid.Parse("ca000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("ca000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("company-access")]
    public async Task Fresh_fence_denies_permission_or_request_context_drift(string drift)
    {
        var user = new MutableUser("actor-a", CompanyA, CompanyB);
        var company = new MutableCompany(CompanyA);
        var fence = CounterpartyContactInformationAuthorityFence.Capture(user, company);
        var authority = new Authority((permission, askedCompany) =>
        {
            Assert.Equal(CompanyA, askedCompany);
            switch (drift)
            {
                case "permission":
                    return PermissionScope.Denied;
                case "actor":
                    user.UserId = "actor-b";
                    break;
                case "company":
                    company.Set(CompanyB);
                    break;
                case "company-access":
                    user.AllowedCompanyIds = new HashSet<Guid>();
                    break;
            }

            return Granted();
        });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fence.RequireStillAsync(
            user,
            company,
            authority,
            [ErpPermissions.CounterpartyPartiesUpdateBasic],
            CancellationToken.None));
        Assert.Single(authority.Calls);
    }

    [Fact]
    public async Task Stable_fence_revalidates_each_existing_permission_for_the_captured_company()
    {
        var user = new MutableUser("actor-a", CompanyA);
        var company = new MutableCompany(CompanyA);
        var authority = new Authority((_, _) => Granted());
        var fence = CounterpartyContactInformationAuthorityFence.Capture(user, company);

        await fence.RequireStillAsync(
            user,
            company,
            authority,
            [
                ErpPermissions.CounterpartyPartiesUpdateBasic,
                ErpPermissions.CounterpartyPartiesRead,
            ],
            CancellationToken.None);

        (string Permission, Guid CompanyId)[] expected =
            [
                (ErpPermissions.CounterpartyPartiesUpdateBasic, CompanyA),
                (ErpPermissions.CounterpartyPartiesRead, CompanyA),
            ];
        Assert.Equal(expected, authority.Calls);
    }

    [Fact]
    public async Task Cancellation_propagates_without_measuring_authority()
    {
        var user = new MutableUser("actor-a", CompanyA);
        var company = new MutableCompany(CompanyA);
        var authority = new Authority((_, _) => Granted());
        var fence = CounterpartyContactInformationAuthorityFence.Capture(user, company);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fence.RequireStillAsync(
            user,
            company,
            authority,
            [ErpPermissions.CounterpartyPartiesRead],
            cancellation.Token));
        Assert.Empty(authority.Calls);
    }

    [Fact]
    public void Capture_requires_an_authenticated_actor_with_current_company_access()
    {
        var company = new MutableCompany(CompanyA);
        var anonymous = new MutableUser(null, CompanyA) { IsAuthenticated = false };
        var foreign = new MutableUser("actor-a", CompanyB);

        Assert.Throws<UnauthorizedAccessException>(() =>
            CounterpartyContactInformationAuthorityFence.Capture(anonymous, company));
        Assert.Throws<UnauthorizedAccessException>(() =>
            CounterpartyContactInformationAuthorityFence.Capture(foreign, company));
    }

    private static PermissionScope Granted() => new(
        true,
        true,
        new HashSet<string>(StringComparer.Ordinal) { "SENTETIK-CONTACT-AUTHORITY" },
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    private sealed class Authority(Func<string, Guid, PermissionScope> answer)
        : IErpAuthorityRevalidationService
    {
        public List<(string Permission, Guid CompanyId)> Calls { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((permission, companyId));
            return Task.FromResult(answer(permission, companyId));
        }
    }

    private sealed class MutableUser(string? userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;

        public string? UserId { get; set; } = userId;

        public IReadOnlySet<Guid> AllowedCompanyIds { get; set; } = companies.ToHashSet();

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved { get; private set; } = true;

        public Guid CompanyId { get; private set; } = companyId;

        public void Set(Guid companyId)
        {
            CompanyId = companyId;
            IsResolved = companyId != Guid.Empty;
        }
    }
}
