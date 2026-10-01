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
    public void Each_action_has_its_owner_name_FIN_T06_has_two_names_and_legacy_Reverse_has_none()
    {
        Assert.Equal("finance.manual-receipt.draft.save", FinancePermissionPort.NameOf(FinanceAction.SaveDraft));
        Assert.Equal("finance.manual-receipt.draft.read", FinancePermissionPort.NameOf(FinanceAction.ReadSources));
        Assert.Equal("finance.manual-receipt.amount.read", FinancePermissionPort.NameOf(FinanceAction.ReadAmount));
        Assert.Equal(ErpPermissions.FinanceBankAccountRead, FinancePermissionPort.NameOf(FinanceAction.ReadBankAccounts));
        Assert.Equal(ErpPermissions.FinanceBankAccountPropose, FinancePermissionPort.NameOf(FinanceAction.ProposeBankAccount));
        Assert.Equal(ErpPermissions.FinanceBankAccountApprove, FinancePermissionPort.NameOf(FinanceAction.ApproveBankAccount));
        // Record's name is NOT invented here: card #66 5743678389 §1 names it and sources it to Ö4
        // (Sinan 5722637822), which answers #254 §5's OPEN-009 question FOR THIS COMMAND ONLY — one narrow
        // authorised operator, no second approval, no amount threshold. A name grants nothing: the central
        // writer registers it and a human assigns it.
        Assert.Equal("finance.manual-receipt.record", FinancePermissionPort.NameOf(FinanceAction.Record));
        Assert.Equal("finance.manual-receipt.reverse.propose",
            FinancePermissionPort.NameOf(FinanceAction.ProposeReversal));
        Assert.Equal("finance.manual-receipt.reverse.approve",
            FinancePermissionPort.NameOf(FinanceAction.ApproveReversal));
        // The superseded one-step action stays nameless and therefore cannot bypass B1's two commands.
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

    /// <summary>
    /// The port's "I cannot tell" branch: no name at all, so the authority is never consulted. Record used to
    /// sit here too — first because it had no name, then, after the card gave it one, because the central
    /// registry did not know that name. Now that <see cref="ErpPermissions.FinanceManualReceiptRecord"/> is
    /// registered, Record has left this branch (see the case below), but the branch itself has NOT gone away:
    /// The unpublished legacy direct Reverse is still on it, and it stays pinned. Retiring the fail-closed
    /// branch because FIN-T06 replaced that path with proposal/approval commands would hide a future bypass.
    /// <para>
    /// UNMAPPED, not "unregistered": the two are different inputs to the same answer, and the port reaches
    /// Unavailable from either. This must never become Denied — "I cannot tell" is not "you may not".
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(FinanceAction.Reverse)]
    public async Task An_unmapped_action_is_unavailable_and_asks_nothing(FinanceAction action)
    {
        // The reason, pinned rather than assumed: no name exists for the superseded one-step action.
        // FIN-T06's proposal and approval actions are separately named and measured below.
        Assert.Null(FinancePermissionPort.NameOf(action));

        var authority = new FixedAuthority(true, true);

        Assert.Equal(PermissionDecision.Unavailable, await Port(authority).CheckAsync(Context(), action, null, CancellationToken.None));
        Assert.Empty(authority.Asked);
    }

    /// <summary>
    /// Record after the registration: the name is KNOWN, so the port asks — and an actor who does not hold it
    /// is <see cref="PermissionDecision.Denied"/>, not Unavailable. That is the honest answer once the
    /// capability exists: the person simply does not have it.
    /// <para>
    /// The cause is forced and asserted, not inferred, because this Denied is reached by THREE paths — a
    /// missing grant, a grant that is not company-wide, and a context that drifted during the await (the
    /// #276/#366 fence). Asserting only the outcome would leave the case green on the day the fence breaks.
    /// So the authority is driven to "not granted" and the ask itself is pinned: the registered name, the
    /// request's company. This is the <c>Assert.Empty(Asked)</c> idea inverted rather than discarded.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_registered_name_the_actor_does_not_hold_is_denied_and_the_authority_IS_asked()
    {
        Assert.Equal(ErpPermissions.FinanceManualReceiptRecord, FinancePermissionPort.NameOf(FinanceAction.Record));
        Assert.True(ErpPermissions.IsKnown(ErpPermissions.FinanceManualReceiptRecord));

        var authority = new FixedAuthority(granted: false, companyWide: true);

        var decision = await Port(authority).CheckAsync(Context(), FinanceAction.Record, null, CancellationToken.None);

        Assert.Equal(PermissionDecision.Denied, decision);
        // Asked once, with the registered name and the request's own company — so a future context-drift
        // defect cannot hide behind the same Denied.
        Assert.Equal(new[] { (ErpPermissions.FinanceManualReceiptRecord, Company) }, authority.Asked);
    }

    [Theory]
    [InlineData(FinanceAction.ProposeReversal, FinancePermissionNames.ReversePropose)]
    [InlineData(FinanceAction.ApproveReversal, FinancePermissionNames.ReverseApprove)]
    public async Task Each_FIN_T06_command_revalidates_its_own_permission_on_every_call(
        FinanceAction action, string permission)
    {
        var authority = new FixedAuthority(granted: true, companyWide: true, "SENTETIK-FIN-T06");
        var port = Port(authority);

        Assert.Equal(PermissionDecision.Allowed,
            await port.CheckAsync(Context(), action, null, CancellationToken.None));
        Assert.Equal(PermissionDecision.Allowed,
            await port.CheckAsync(Context(), action, null, CancellationToken.None));

        Assert.Equal(new[] { (permission, Company), (permission, Company) }, authority.Asked);
        Assert.Equal(new[] { "SENTETIK-FIN-T06" }, port.RoleCodesOf(action));
    }

    [Theory]
    [InlineData(FinanceAction.ProposeReversal, FinancePermissionNames.ReversePropose)]
    [InlineData(FinanceAction.ApproveReversal, FinancePermissionNames.ReverseApprove)]
    public async Task A_missing_FIN_T06_grant_is_denied_after_a_real_authority_measurement(
        FinanceAction action, string permission)
    {
        var authority = new FixedAuthority(granted: false, companyWide: true);

        Assert.Equal(PermissionDecision.Denied,
            await Port(authority).CheckAsync(Context(), action, null, CancellationToken.None));
        Assert.Equal(new[] { (permission, Company) }, authority.Asked);
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

    [Fact]
    public void Allocation_read_and_write_have_distinct_names()
    {
        Assert.Equal("finance.allocation.read", FinancePermissionPort.NameOf(FinanceAction.ReadAllocation));
        Assert.Equal("finance.allocation.write", FinancePermissionPort.NameOf(FinanceAction.WriteAllocation));
    }

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
