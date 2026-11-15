using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// FIN-S16 (M-05 Hesap Planı): the active approved account plan is read under its own narrow company-wide name,
/// measured fresh before and after the store read; the accounts are released exactly as the approved mirror names
/// them, ordered by code, and a missing active plan is an empty Ok, not an error.
/// </summary>
public sealed class AccountPlanReadServiceV1Tests
{
    private static readonly Guid Company = Guid.Parse("b826d058-c52a-4d63-919a-0c0f19a59721");
    private static readonly Guid OtherCompany = Guid.Parse("778929d2-a4da-42a2-a3ed-74207279240b");
    private static readonly Guid PackageId = Guid.Parse("75ab518f-8af3-4f13-a3ee-39e96e1646b8");
    private static readonly Guid AccountA = Guid.Parse("e9afaa24-aec6-4961-95c1-99b6a925c4a8");
    private static readonly Guid AccountB = Guid.Parse("ba87cad2-80fe-4379-b377-b0cf35158744");
    private static readonly Guid Approver = Guid.Parse("fb06d96e-a69d-4394-a025-62d703657a5a");
    private const string Actor = "1b0a3c5e-9d0f-4c7e-8a51-6e2f4d8b7c90";
    private const string Sha256 = "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12";
    private static readonly DateTimeOffset ActivatedAt = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_active_plan_is_released_ordered_by_code_with_its_provenance()
    {
        var authority = new SequencedAuthority(Granted(), Granted());
        var reader = new FakeReader(Record(Company, ApprovedAccountingSourceCatalogStatusV1.Active));

        var result = await Service(reader, authority).ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Ok, result.Status);
        var view = Assert.IsType<AccountPlanViewV1>(result.Resource);
        Assert.Equal(new AccountPlanSourceViewV1(PackageId, 3, "plan-2026", Sha256, ActivatedAt, 2), view.Source);
        Assert.Equal(
            [new AccountPlanAccountViewV1(AccountB, "100.01", "Kasa"), new AccountPlanAccountViewV1(AccountA, "120.01", "Alıcılar")],
            view.Accounts);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task No_active_plan_is_an_empty_ok_and_not_a_not_found()
    {
        var result = await Service(new FakeReader(null), new SequencedAuthority(Granted(), Granted())).ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Ok, result.Status);
        var view = Assert.IsType<AccountPlanViewV1>(result.Resource);
        Assert.Null(view.Source);
        Assert.Empty(view.Accounts);
    }

    [Fact]
    public async Task Without_the_grant_the_store_is_never_read()
    {
        var reader = new FakeReader(Record(Company, ApprovedAccountingSourceCatalogStatusV1.Active));

        var result = await Service(reader, new SequencedAuthority(PermissionScope.Denied)).ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(AccountingApiCodes.AuthorityDenied, result.Code);
        Assert.Null(result.Resource);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task A_site_scoped_grant_is_not_enough()
    {
        var reader = new FakeReader(Record(Company, ApprovedAccountingSourceCatalogStatusV1.Active));
        var siteScoped = Granted() with { IsCompanyWide = false };

        var result = await Service(reader, new SequencedAuthority(siteScoped)).ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task Authority_revoked_during_the_read_discards_the_plan()
    {
        var reader = new FakeReader(Record(Company, ApprovedAccountingSourceCatalogStatusV1.Active));

        var result = await Service(reader, new SequencedAuthority(Granted(), PermissionScope.Denied)).ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task Company_change_during_the_read_discards_the_plan()
    {
        var company = new MutableCompany(Company);
        var reader = new FakeReader(Record(Company, ApprovedAccountingSourceCatalogStatusV1.Active),
            () => company.Set(OtherCompany));
        var service = new AccountPlanReadServiceV1(reader, new SequencedAuthority(Granted(), Granted()), company,
            new MutableUser(Actor, Company), new ResolvedCorrelation());

        var result = await service.ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Another_companys_or_a_non_active_package_is_refused()
    {
        var foreign = await Service(new FakeReader(Record(OtherCompany, ApprovedAccountingSourceCatalogStatusV1.Active)),
            new SequencedAuthority(Granted(), Granted())).ReadActiveAsync();
        var superseded = await Service(new FakeReader(Record(Company, ApprovedAccountingSourceCatalogStatusV1.Superseded)),
            new SequencedAuthority(Granted(), Granted())).ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Denied, foreign.Status);
        Assert.Null(foreign.Resource);
        Assert.Equal(AccountingApiStatus.Denied, superseded.Status);
        Assert.Null(superseded.Resource);
    }

    [Fact]
    public async Task A_store_company_mismatch_is_a_denial()
    {
        var reader = new FakeReader(null, () => throw new AccountingStoreException(
            AccountingStoreCodes.CompanyContextMismatch, "company changed"));

        var result = await Service(reader, new SequencedAuthority(Granted(), Granted())).ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Denied, result.Status);
    }

    [Fact]
    public async Task An_unauthenticated_or_uncorrelated_caller_is_denied_before_any_read()
    {
        var reader = new FakeReader(Record(Company, ApprovedAccountingSourceCatalogStatusV1.Active));
        var anonymous = new MutableUser(Actor, Company) { IsAuthenticated = false };
        var unauthenticated = await new AccountPlanReadServiceV1(reader, new SequencedAuthority(Granted()),
            new MutableCompany(Company), anonymous, new ResolvedCorrelation()).ReadActiveAsync();
        var uncorrelated = await new AccountPlanReadServiceV1(reader, new SequencedAuthority(Granted()),
            new MutableCompany(Company), new MutableUser(Actor, Company), new ResolvedCorrelation { IsResolved = false })
            .ReadActiveAsync();

        Assert.Equal(AccountingApiStatus.Denied, unauthenticated.Status);
        Assert.Equal(AccountingApiStatus.Denied, uncorrelated.Status);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public void The_read_name_is_the_registered_narrow_company_wide_name()
    {
        Assert.Equal("accounting.account-plan.read", AccountPlanPermissionNamesV1.Read);
        Assert.Equal([AccountPlanPermissionNamesV1.Read], AccountPlanPermissionNamesV1.All);
        Assert.True(ErpPermissions.IsKnown(AccountPlanPermissionNamesV1.Read));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(AccountPlanPermissionNamesV1.Read));
        Assert.False(ErpPermissions.IsCritical(AccountPlanPermissionNamesV1.Read));
    }

    private static AccountPlanReadServiceV1 Service(IActiveAccountPlanReaderV1 reader, IErpAuthorityRevalidationService authority) =>
        new(reader, authority, new MutableCompany(Company), new MutableUser(Actor, Company), new ResolvedCorrelation());

    private static ApprovedAccountingSourceCatalogRecordV1 Record(Guid company, ApprovedAccountingSourceCatalogStatusV1 status)
    {
        var mirror = new AccountPlanMirrorV1(company, "plan-2026", Sha256, Approver, ActivatedAt,
        [
            new AccountPlanMirrorRowV1(new AccountRef(company, AccountA), "120.01", "Alıcılar"),
            new AccountPlanMirrorRowV1(new AccountRef(company, AccountB), "100.01", "Kasa"),
        ]);
        return new ApprovedAccountingSourceCatalogRecordV1(
            PackageId, company, status, 2, 3, "template-v1", "plan-2026", Sha256, Sha256, Approver,
            ActivatedAt.AddHours(-1), Approver, ActivatedAt, new ApprovedAccountingSourcePackageV1(mirror, []));
    }

    private static PermissionScope Granted() => new(
        true,
        true,
        new HashSet<string>(["accounting-reader"], StringComparer.Ordinal),
        new HashSet<Guid>(),
        new HashSet<Guid>(),
        new HashSet<Guid>());

    private sealed class FakeReader(ApprovedAccountingSourceCatalogRecordV1? record, Action? duringRead = null)
        : IActiveAccountPlanReaderV1
    {
        public int Calls { get; private set; }

        public Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActiveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            duringRead?.Invoke();
            return Task.FromResult(record);
        }
    }

    private sealed class SequencedAuthority(params PermissionScope[] scopes) : IErpAuthorityRevalidationService
    {
        public int Calls { get; private set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccountPlanPermissionNamesV1.Read, permission);
            Assert.Equal(Company, companyId);
            cancellationToken.ThrowIfCancellationRequested();
            var answer = scopes[Math.Min(Calls, scopes.Length - 1)];
            Calls++;
            return Task.FromResult(answer);
        }
    }

    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved { get; private set; } = true;
        public Guid CompanyId { get; private set; } = companyId;

        public void Set(Guid value)
        {
            CompanyId = value;
            IsResolved = value != Guid.Empty;
        }
    }

    private sealed class MutableUser(string actorId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
    }

    private sealed class ResolvedCorrelation : ICorrelationContext
    {
        public bool IsResolved { get; set; } = true;
        public string CorrelationId => "fin-s16-account-plan-read";
        public string? CausationId => null;
        public string SourceChannel => "test";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
            string? userAgent) => throw new NotSupportedException();
    }
}
