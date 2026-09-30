using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Search;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// AT-002: the Sales-owned global search source refuses before any read when the Sales draft read is not held company-wide
/// (the context below points at an unreachable server, so a read that happened would fail the test), fails closed on an
/// unbound or unknown permission name, and reads only its own tenant-filtered, price-free columns.
/// </summary>
public sealed class SalesOrderGlobalSearchSourceTests
{
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly GlobalSearchRequestV1 Request = new GlobalSearchRequestV1("SO-2026", 5);

    [Fact]
    public async Task A_denied_read_answers_not_authorized_without_reading()
    {
        var authority = new Authority(PermissionScope.Denied);
        var source = Source(authority);

        var answer = await source.SearchAsync(Request);

        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, answer.Outcome);
        Assert.Empty(answer.Hits);
        Assert.False(answer.HasMore);
        Assert.Equal([(SalesPermissionManifestV1.DraftsRead, CompanyA)], authority.Asked);
    }

    [Fact]
    public async Task A_site_scoped_read_is_not_the_company_wide_read_the_list_measures()
    {
        var siteScoped = PermissionScope.Denied with { IsGranted = true, IsCompanyWide = false };

        var answer = await Source(new Authority(siteScoped)).SearchAsync(Request);

        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, answer.Outcome);
    }

    [Fact]
    public async Task Without_a_resolved_company_or_access_to_it_nothing_is_measured_or_read()
    {
        var authority = new Authority(Granted);
        var unresolved = await Source(authority, resolved: false).SearchAsync(Request);
        var foreign = await Source(authority, user: new User(CompanyB)).SearchAsync(Request);
        var anonymous = await Source(authority, user: new User(CompanyA) { Authenticated = false }).SearchAsync(Request);

        Assert.All(new[] { unresolved, foreign, anonymous }, answer => Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, answer.Outcome));
        Assert.Empty(authority.Asked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sales.drafts.unknown")]
    public async Task An_unbound_or_unknown_read_name_is_a_setup_gap_never_a_grant(string? permission)
    {
        var authority = new Authority(Granted);
        var source = Source(authority, bindings: new SalesDraftServiceBindings(ReadPermission: permission));

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.SearchAsync(Request));
        Assert.Empty(authority.Asked);
    }

    [Fact]
    public void The_read_is_tenant_filtered_by_document_number_newest_first_and_selects_no_commercial_column()
    {
        using var context = Context(resolved: true);

        var sql = SalesOrderGlobalSearchSource.Query(context, SalesOrderGlobalSearchSource.Normalize("  so-2026 ")).ToQueryString();

        Assert.Contains("[CompanyId] = @", sql, StringComparison.Ordinal);
        Assert.Contains("[DocumentNo] LIKE", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("[CreatedAtUtc] DESC", sql, StringComparison.Ordinal);
        foreach (var commercial in new[] { "Price", "Discount", "Amount", "Money", "Currency", "Note", "DraftLines" })
        {
            Assert.DoesNotContain(commercial, sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(1, 1, "Preparing")]
    [InlineData(1, 2, "PendingApproval")]
    [InlineData(1, 3, "Approved")]
    [InlineData(1, 4, "Rejected")]
    [InlineData(1, 5, "Withdrawn")]
    [InlineData(2, 3, "Cancelled")]
    public void The_state_is_the_owners_name_with_a_cancelled_lifecycle_winning(int state, int approval, string expected)
    {
        Assert.Equal(expected, SalesOrderGlobalSearchSource.StateName(state, approval));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 9)]
    [InlineData(3, 1)]
    public void An_unknown_stored_state_fails_closed(int state, int approval)
    {
        Assert.Throws<InvalidOperationException>(() => SalesOrderGlobalSearchSource.StateName(state, approval));
    }

    [Fact]
    public void The_term_is_compared_as_the_invariant_upper_case_document_number()
    {
        Assert.Equal("SO-2026-0001", SalesOrderGlobalSearchSource.Normalize("  so-2026-0001 "));
    }

    private static PermissionScope Granted => PermissionScope.Denied with { IsGranted = true, IsCompanyWide = true };

    private static SalesOrderGlobalSearchSource Source(Authority authority, bool resolved = true, User? user = null,
        SalesDraftServiceBindings? bindings = null)
    {
        var company = Company(resolved);
        return new SalesOrderGlobalSearchSource(Context(company), company, user ?? new User(CompanyA), authority,
            bindings ?? new SalesDraftServiceBindings());
    }

    private static CompanyContext Company(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        return company;
    }

    private static SalesDbContext Context(bool resolved) => Context(Company(resolved));

    /// <summary>A context of the given company against a server that does not exist: any read would throw.</summary>
    private static SalesDbContext Context(CompanyContext company)
    {
        var options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new SalesDbContext(options, company);
    }

    private sealed class Authority(PermissionScope scope) : IErpAuthorityRevalidationService
    {
        public List<(string Permission, Guid CompanyId)> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add((permission, companyId));
            return Task.FromResult(scope);
        }
    }

    private sealed class User(params Guid[] companies) : ICurrentUser
    {
        public bool Authenticated { get; init; } = true;

        public bool IsAuthenticated => Authenticated;

        public string? UserId => Authenticated ? "SENTETIK-search-user" : null;

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
