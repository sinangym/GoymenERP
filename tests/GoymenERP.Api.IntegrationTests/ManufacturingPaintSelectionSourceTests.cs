using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ManufacturingPaintSelectionSourceTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static PlanPaintSelectionV1 Choice() => new(Guid.NewGuid(), "12", "3", "source:3", Guid.NewGuid(), Guid.NewGuid(), "4", "5");

    [Fact]
    public async Task Actual_product_membership_and_supplier_are_both_required_but_optional_color_is_not_read()
    {
        var f = new Fixture(); var answer = await f.Read();
        Assert.Equal(ManufacturingPaintSelectionOutcomeV1.Available, answer.Outcome);
        Assert.Equal(f.Selection, answer.Selection); Assert.Equal(1, f.Suppliers.Calls); Assert.Equal(0, f.Colors.Calls);
        f.Suppliers.Active = false;
        Assert.Equal(ManufacturingPaintSelectionOutcomeV1.Ineligible, (await f.Read()).Outcome);
    }

    [Fact]
    public async Task Stale_or_missing_product_proof_never_becomes_free_text_membership_or_queries_a_supplier()
    {
        var f = new Fixture(); f.Products.EntryVersion = "4";
        Assert.Equal(ManufacturingPaintSelectionOutcomeV1.VersionConflict, (await f.Read()).Outcome);
        Assert.Equal(0, f.Suppliers.Calls);
        f.Products.Outcome = ProductPaintMembershipReadOutcomeV1.NoSource;
        Assert.Equal(ManufacturingPaintSelectionOutcomeV1.Unavailable, (await f.Read()).Outcome);
        Assert.Equal(0, f.Suppliers.Calls);
    }

    [Fact]
    public async Task Foreign_company_answers_and_actor_swaps_release_no_owner_selection()
    {
        var f = new Fixture(); f.Products.CompanyId = Guid.NewGuid();
        var answer = await f.Read(); Assert.Equal(ManufacturingPaintSelectionOutcomeV1.Unavailable, answer.Outcome);
        Assert.Null(answer.Selection); Assert.Equal(0, f.Suppliers.Calls);
        f.Products.CompanyId = Company; f.Products.DuringRead = () => f.User.Id = "synthetic-other";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(f.Read); Assert.Equal(0, f.Suppliers.Calls);
    }

    [Fact]
    public async Task Optional_selected_color_must_still_match_its_active_owner_version()
    {
        var f = new Fixture(); f.Selection = f.Selection with { ColorId = Guid.NewGuid(), ColorVersion = "2" };
        f.Colors.Version = 3;
        Assert.Equal(ManufacturingPaintSelectionOutcomeV1.VersionConflict, (await f.Read()).Outcome);
        f.Colors.Version = 2;
        Assert.Equal(ManufacturingPaintSelectionOutcomeV1.Available, (await f.Read()).Outcome);
    }

    [Fact]
    public async Task Catalog_authority_revoked_after_product_read_returns_no_candidates()
    {
        var f = new Fixture(); f.Products.DuringRead = () => f.Authority.Granted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Source.ListAsync(Company, null, default));
        Assert.Equal(0, f.Suppliers.Calls);
    }

    private sealed class Fixture
    {
        public PlanPaintSelectionV1 Selection = Choice();
        public readonly User User = new(); public readonly Products Products = new();
        public readonly Suppliers Suppliers = new(); public readonly Colors Colors = new(); public readonly Authority Authority = new();
        public readonly ManufacturingPaintSelectionSourceV1 Source;
        public Fixture()
        {
            var company = new CompanyContext(); company.Set(Company);
            Source = new(company, User, Products, Suppliers, new Counterparties(), new Sessions(), Colors, Authority);
        }
        public Task<ManufacturingPaintSelectionAnswerV1> Read() => Source.ValidateAsync(Company, Selection, default);
    }
    private sealed class Products : IProductPaintMembershipReaderV1
    {
        public string EntryVersion = "3"; public Guid CompanyId = Company; public Action? DuringRead;
        public ProductPaintMembershipReadOutcomeV1 Outcome = ProductPaintMembershipReadOutcomeV1.Available;
        public Task<ProductPaintMembershipReadV1> GetAsync(Guid id, CancellationToken cancellationToken = default)
        { DuringRead?.Invoke(); return Task.FromResult(new ProductPaintMembershipReadV1(Outcome, CompanyId, id,
            Outcome == ProductPaintMembershipReadOutcomeV1.Available ? new(id, "12", EntryVersion, "PNT", "Sentetik boya", "source:3") : null, "source:3")); }
        public Task<ProductPaintMembershipCatalogV1> ListAsync(CancellationToken cancellationToken = default)
        { DuringRead?.Invoke(); return Task.FromResult(new ProductPaintMembershipCatalogV1(ProductPaintMembershipCatalogOutcomeV1.Available, CompanyId, "1", [], "source:3")); }
    }
    private sealed class Suppliers : IPurchasingPartyAccountPortV1
    {
        public int Calls; public bool Active = true;
        public Task<PurchasingPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid accountId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new PurchasingPartyAccountAnswerV1(PurchasingPartyAccountOutcomeV1.Found,
            new(partyId, accountId, Company, Active, true, true, "4", "5"))); }
    }
    private sealed class Colors : ISettingsReferenceListReaderV1
    {
        public int Calls; public long Version = 2;
        public Task<SettingsReferenceListLookupV1> GetAsync(Guid companyId, SettingsReferenceListKindV1 kind, Guid id, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new SettingsReferenceListLookupV1(SettingsReferenceListLookupOutcomeV1.Found, new(id, "RAL", "Beyaz", true, Version))); }
        public Task<SettingsReferenceListV1> ListAsync(Guid companyId, SettingsReferenceListKindV1 kind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Sessions : IActorSessionBindingResolver
    { public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("synthetic-session"); }
    private sealed class Counterparties : ICounterpartyReadService
    {
        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
            => Task.FromResult(Granted && permission == ErpPermissions.ManufacturingPlansWrite && companyId == Company
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
    private sealed class User : ICurrentUser
    { public string Id = "synthetic-planner"; public string? UserId => Id; public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => id == Company; }
}
