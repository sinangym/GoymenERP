using GoymenERP.Api.Shell;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FinanceOfficialInvoiceImportHostAuthorityTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private const string Actor = "finance-import-actor";

    [Fact]
    public async Task Upload_uses_the_exact_known_company_wide_permission_and_keeps_audit_roles_live()
    {
        var revalidation = new Revalidation(Allowed("FINANCE-UPLOAD"));
        var authority = Create(revalidation);

        var result = await authority.CheckAsync(Context(), FinanceOfficialInvoiceImportAuthorityActionV1.Upload,
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(PermissionDecision.Allowed, result);
        Assert.Equal([FinanceOfficialInvoiceImportPermissionNamesV1.Upload], revalidation.Permissions);
        Assert.Equal(["FINANCE-UPLOAD"], authority.RoleCodesOf(
            FinanceOfficialInvoiceImportAuthorityActionV1.Upload));
    }

    [Fact]
    public async Task Preview_accepts_the_existing_checker_authority_without_granting_upload()
    {
        var revalidation = new Revalidation(PermissionScope.Denied, Allowed("FINANCE-APPROVE"));
        var authority = Create(revalidation);

        var result = await FinanceEndpoints.CheckOfficialInvoicePreviewAuthorityAsync(
            authority, Context(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(PermissionDecision.Allowed, result);
        Assert.Equal(
            [FinanceOfficialInvoiceImportPermissionNamesV1.Upload,
                FinanceOfficialInvoiceImportPermissionNamesV1.Approve],
            revalidation.Permissions);
    }

    [Fact]
    public async Task Narrow_scope_and_changed_request_context_fail_closed()
    {
        var revalidation = new Revalidation(new PermissionScope(true, false, new HashSet<string> { "NARROW" },
            new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        var company = new MutableCompanyContext(Company);
        var user = new User(Actor, Company);
        var permissions = new FinancePermissionPort(revalidation, company, user);
        var authority = new FinanceOfficialInvoiceImportAuthority(revalidation, permissions, company, user);

        Assert.Equal(PermissionDecision.Denied, await authority.CheckAsync(Context(),
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve, Guid.NewGuid(), CancellationToken.None));

        company.Set(Guid.NewGuid());
        Assert.Equal(PermissionDecision.Denied, await authority.CheckAsync(Context(),
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve, Guid.NewGuid(), CancellationToken.None));
        Assert.Single(revalidation.Permissions);
    }

    [Fact]
    public async Task Pending_list_uses_the_same_fresh_company_wide_approve_permission_without_weakening_commands()
    {
        var revalidation = new Revalidation(Allowed("FINANCE-APPROVE"));
        var commandAuthority = Create(revalidation);
        var pendingAuthority = new FinanceOfficialInvoiceImportPendingApprovalAuthority(commandAuthority);

        Assert.Equal(PermissionDecision.Denied, await commandAuthority.CheckAsync(Context(),
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve, Guid.Empty, CancellationToken.None));
        Assert.Empty(revalidation.Permissions);

        Assert.Equal(PermissionDecision.Allowed, await pendingAuthority.CheckAsync(Context(),
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve, Guid.Empty, CancellationToken.None));
        Assert.Equal([FinanceOfficialInvoiceImportPermissionNamesV1.Approve], revalidation.Permissions);
        Assert.Equal(["FINANCE-APPROVE"], commandAuthority.RoleCodesOf(
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve));
    }

    [Fact]
    public async Task Pending_list_fails_closed_for_narrow_or_changed_company_context()
    {
        var revalidation = new Revalidation(
            new PermissionScope(true, false, new HashSet<string> { "NARROW" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()),
            Allowed("FINANCE-APPROVE"));
        var company = new MutableCompanyContext(Company);
        var user = new User(Actor, Company);
        var permissions = new FinancePermissionPort(revalidation, company, user);
        var pendingAuthority = new FinanceOfficialInvoiceImportPendingApprovalAuthority(
            new FinanceOfficialInvoiceImportAuthority(revalidation, permissions, company, user));

        Assert.Equal(PermissionDecision.Denied, await pendingAuthority.CheckAsync(Context(),
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve, Guid.Empty, CancellationToken.None));

        company.Set(Guid.NewGuid());
        Assert.Equal(PermissionDecision.Denied, await pendingAuthority.CheckAsync(Context(),
            FinanceOfficialInvoiceImportAuthorityActionV1.Approve, Guid.Empty, CancellationToken.None));
        Assert.Single(revalidation.Permissions);
    }

    [Fact]
    public async Task Approved_source_reads_reuse_the_existing_finance_source_read_authority()
    {
        var revalidation = new Revalidation(Allowed("FINANCE-READ"));
        var authority = Create(revalidation);

        var result = await ((IFinanceOpenItemSourceAuthority)authority).CheckAsync(
            Context(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(PermissionDecision.Allowed, result);
        Assert.Equal([FinancePermissionNames.DraftRead], revalidation.Permissions);
    }

    private static FinanceOfficialInvoiceImportAuthority Create(Revalidation revalidation)
    {
        var company = new MutableCompanyContext(Company);
        var user = new User(Actor, Company);
        return new FinanceOfficialInvoiceImportAuthority(revalidation,
            new FinancePermissionPort(revalidation, company, user), company, user);
    }

    private static FinanceContext Context() => new(Company, Actor, "finance-import-correlation", null);

    private static PermissionScope Allowed(string role) => new(true, true, new HashSet<string> { role },
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private sealed class Revalidation(params PermissionScope[] answers) : IErpAuthorityRevalidationService
    {
        private readonly Queue<PermissionScope> answers = new(answers);
        public List<string> Permissions { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Permissions.Add(permission);
            return Task.FromResult(answers.Count == 0 ? PermissionScope.Denied : answers.Dequeue());
        }
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
