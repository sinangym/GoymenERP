using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FIN-T04b: the real Finance permission port without a database. Every check is a fresh #276
/// measurement; Allowed needs a granted, company-wide scope of the request's company and actor. SENTETIK.
/// </summary>
public sealed class FinancePermissionPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Actor = "SENTETIK-actor";

    [Fact]
    public void Each_action_has_its_owner_name_and_record_and_reverse_have_none()
    {
        Assert.Equal("finance.manual-receipt.draft.save", FinancePermissionPort.NameOf(FinanceAction.SaveDraft));
        Assert.Equal("finance.manual-receipt.draft.read", FinancePermissionPort.NameOf(FinanceAction.ReadSources));
        Assert.Equal("finance.manual-receipt.amount.read", FinancePermissionPort.NameOf(FinanceAction.ReadAmount));
        Assert.Equal(ErpPermissions.FinanceBankAccountRead, FinancePermissionPort.NameOf(FinanceAction.ReadBankAccounts));
        Assert.Equal(ErpPermissions.FinanceBankAccountPropose, FinancePermissionPort.NameOf(FinanceAction.ProposeBankAccount));
        Assert.Equal(ErpPermissions.FinanceBankAccountApprove, FinancePermissionPort.NameOf(FinanceAction.ApproveBankAccount));
        // OPEN-009: no name is invented for the money-recording actions.
        Assert.Null(FinancePermissionPort.NameOf(FinanceAction.Record));
        Assert.Null(FinancePermissionPort.NameOf(FinanceAction.Reverse));
    }

    [Theory]
    [InlineData(true, true, PermissionDecision.Allowed)]
    [InlineData(true, false, PermissionDecision.Denied)]
    [InlineData(false, false, PermissionDecision.Denied)]
    public async Task Only_a_granted_company_wide_scope_is_allowed(bool granted, bool companyWide, PermissionDecision expected)
    {
        var authority = new FixedAuthority(granted, companyWide, "ROLE-B", "ROLE-A");
        var port = Port(authority);

        var decision = await port.CheckAsync(Context(), FinanceAction.ReadBankAccounts, null, CancellationToken.None);

        Assert.Equal(expected, decision);
        // Asked afresh, for the registered name and the request's company.
        Assert.Equal(new[] { (ErpPermissions.FinanceBankAccountRead, Company) }, authority.Asked);
    }

    [Theory]
    [InlineData(FinanceAction.Record)]
    [InlineData(FinanceAction.Reverse)]
    public async Task An_action_without_a_name_is_unavailable_and_asks_nothing(FinanceAction action)
    {
        var authority = new FixedAuthority(true, true);

        Assert.Equal(PermissionDecision.Unavailable, await Port(authority).CheckAsync(Context(), action, null, CancellationToken.None));
        Assert.Empty(authority.Asked);
    }

    [Theory]
    [InlineData("other-actor")]
    [InlineData("other-company")]
    public async Task A_context_that_is_not_the_request_s_is_denied_before_any_measurement(string fault)
    {
        var authority = new FixedAuthority(true, true);
        var context = fault == "other-actor"
            ? Context() with { ActorId = "SENTETIK-someone-else" }
            : Context() with { CompanyId = Guid.NewGuid() };

        Assert.Equal(PermissionDecision.Denied,
            await Port(authority).CheckAsync(context, FinanceAction.ReadBankAccounts, null, CancellationToken.None));
        Assert.Empty(authority.Asked);
    }

    [Fact]
    public async Task The_audit_roles_are_the_ones_that_granted_that_action_only()
    {
        var authority = new FixedAuthority(true, true, "ROLE-B", "ROLE-A");
        var port = Port(authority);
        // Names registered on main (FS0) keep this independent of the central registration of the draft names.
        var proposeRoles = port.RoleCodesOf(FinanceAction.ProposeBankAccount);
        Assert.Empty(proposeRoles);

        await port.CheckAsync(Context(), FinanceAction.ReadBankAccounts, null, CancellationToken.None);
        // A read's roles are not the write's.
        Assert.Empty(proposeRoles);

        await port.CheckAsync(Context(), FinanceAction.ProposeBankAccount, null, CancellationToken.None);
        Assert.Equal(new[] { "ROLE-A", "ROLE-B" }, proposeRoles);
    }

    private static FinanceContext Context() => new FinanceContext(Company, Actor, "SENTETIK-correlation", null);

    private static FinancePermissionPort Port(FixedAuthority authority)
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new FinancePermissionPort(authority, company, new FixedUser(Actor, Company));
    }

    private sealed class FixedAuthority(bool granted, bool companyWide, params string[] roles) : IErpAuthorityRevalidationService
    {
        public List<(string Permission, Guid Company)> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((permission, companyId));
            return Task.FromResult(new PermissionScope(granted, companyWide,
                new HashSet<string>(roles, StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
