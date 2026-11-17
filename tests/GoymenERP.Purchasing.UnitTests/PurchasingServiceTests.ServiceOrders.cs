using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Own_material_service_price_requires_the_actual_kg_profile_and_never_infers_kg_from_a_label()
    {
        var source = new ServiceCommandSource { IsKg = false }; var store = new ServiceCommandStore();
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), new FixedUser(), new FixedAuthority(), store,
            source, source, source, source, TimeProvider.System);
        var request = ServiceCreate(source) with { Kind = PurchaseServiceOrderKind.OwnMaterialSubcontract,
            Materials = [new(Guid.NewGuid(), "1", Product, "1", "10")], ExpectedOutputs = [new(Product, "1", "9")] };
        Assert.Equal(422, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.CreateAsync(request,
            new("SENTETIK-kg-profile", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Status);
        Assert.Null(store.Saved);
    }
    [Fact]
    public async Task Service_catalog_read_rechecks_current_company_membership_after_the_owner_await()
    {
        var user = new FixedUser(); var source = new ServiceCommandSource { AfterList = () => user.Allowed.Clear() };
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), user, new FixedAuthority(), new ServiceCommandStore(),
            source, source, source, source, TimeProvider.System);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.ReferencesAsync())).Status);
        Assert.Equal(0, source.UnitReads);
    }
    [Fact]
    public async Task Unknown_service_type_is_not_replaced_by_an_assumed_service_or_a_default_price()
    {
        var source = new ServiceCommandSource { HasService = false }; var store = new ServiceCommandStore();
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), new FixedUser(), new FixedAuthority(), store,
            source, source, source, source, TimeProvider.System);
        Assert.Equal(422, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.CreateAsync(ServiceCreate(source),
            new("SENTETIK-no-service", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Status);
        Assert.Null(store.Saved);
    }
    [Fact]
    public async Task Subcontract_service_refuses_a_missing_Purchasing_tax_default_instead_of_assuming_zero_or_a_Sales_default()
    {
        var source = new ServiceCommandSource(); var store = new ServiceCommandStore();
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), new FixedUser(), new FixedAuthority(), store,
            source, source, source, source, TimeProvider.System, subcontractTax: source);
        var request = ServiceCreate(source) with { Kind = PurchaseServiceOrderKind.OwnMaterialSubcontract,
            Materials = [new(Guid.NewGuid(), "1", Product, "1", "10")], ExpectedOutputs = [new(Product, "1", "9")] };
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.CreateAsync(request,
            new("SENTETIK-no-tax-default", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Status);
        Assert.Null(store.Saved);
    }
    private static CreatePurchaseServiceOrderV1 ServiceCreate(ServiceCommandSource source) => new(Guid.NewGuid(), "0",
        PurchaseServiceOrderKind.ServicePurchase, Party, Account, Day, source.Currency,
        [new(Guid.NewGuid(), source.ServiceType, "1", KgUnit, "10", null)], [], [], "SENTETIK hizmet");
    // Contract fixtures only. This class does not claim SQL, a real catalog or a number reservation.
    private sealed class ServiceCommandSource : IPurchasingPartyAccountPortV2, ISettingsReferenceListReaderV1,
        ISettingsPrerequisiteProvider, ISettingsUnitIdentityCatalogV1, ISettingsSubcontractTaxDefaultProviderV1, ISettingsTaxRateListReaderV1
    {
        public Guid ServiceType { get; } = Guid.NewGuid(); public Guid Currency { get; } = Guid.NewGuid();
        public bool IsKg { get; init; } = true; public bool HasService { get; init; } = true; public Action? AfterList { get; init; }
        public int UnitReads { get; private set; }
        private SettingsReferenceListEntryV1 Entry => new(ServiceType, "SENTETIK", "SENTETIK hizmet", true, 1);
        public Task<PurchasingPartyAccountAnswerV2> GetAuthorizedAsync(Guid party, Guid account, CancellationToken token = default) =>
            Task.FromResult(new PurchasingPartyAccountAnswerV2(PurchasingPartyAccountOutcomeV1.Found, new(party, account, Company, true, true, true, true, "1", "1")));
        public Task<SettingsReferenceListV1> ListAsync(Guid company, SettingsReferenceListKindV1 kind, CancellationToken token = default)
        { AfterList?.Invoke(); return Task.FromResult(new SettingsReferenceListV1(company, kind, true, HasService ? [Entry] : [])); }
        public Task<SettingsReferenceListLookupV1> GetAsync(Guid company, SettingsReferenceListKindV1 kind, Guid id, CancellationToken token = default) =>
            Task.FromResult(new SettingsReferenceListLookupV1(HasService && id == ServiceType ? SettingsReferenceListLookupOutcomeV1.Found
                : SettingsReferenceListLookupOutcomeV1.NotFound, HasService && id == ServiceType ? Entry : null));
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unit, DateOnly day, CancellationToken token = default) =>
            Task.FromResult(new UnitProfileV1(Company, unit, "kg", IsKg ? "weight" : "count", IsKg, new(KgProfile, 1), 3, true, Day, null));
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currency, string operation, DateOnly day, CancellationToken token = default) =>
            Task.FromResult(new MoneyProfileV1(Company, currency, "TRY", operation, new(Guid.NewGuid(), 1), 2, true, Day, null, 4));
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly day, DateOnly? value, CancellationToken token = default) => throw new NotSupportedException();
        public Task<UnitIdentityV1?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<UnitIdentityV1?>(new(id, "kg", "weight", IsKg));
        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken token = default)
        { UnitReads++; return Task.FromResult<IReadOnlyList<UnitIdentityV1>>([new(KgUnit, "kg", "weight", IsKg)]); }
        public Task<SettingsSubcontractTaxDefaultAnswerV1> GetSubcontractTaxDefaultAsync(Guid company, SubcontractTaxSideV1 side, CancellationToken token = default) =>
            Task.FromResult(new SettingsSubcontractTaxDefaultAnswerV1(company, side, null, 0));
        public Task<SettingsTaxRateListResolutionV1> GetRateListAsync(string kind, DateOnly on, CancellationToken token = default) =>
            Task.FromResult(new SettingsTaxRateListResolutionV1(Company, kind, on, TaxResolutionStatesV1.Resolved, 1, Day,
                [new("SENTETIK-20", "SENTETIK KDV", 20)], null));
    }
    private sealed class ServiceCommandStore : IPurchaseServiceOrderStoreV1
    {
        public PurchaseServiceOrderState? Saved { get; private set; }
        public Task<PurchaseServiceOrderState?> ReadAsync(Guid id, CancellationToken token) => Task.FromResult(Saved);
        public Task<IReadOnlyList<PurchaseServiceOrderState>> ListAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<PurchaseServiceOrderWriteV1?> ReplayAsync(string command, PurchasingWriteEnvelope envelope, string hash, CancellationToken token) =>
            Task.FromResult<PurchaseServiceOrderWriteV1?>(null);
        public async Task<PurchaseServiceOrderWriteV1?> WriteAsync(Guid id, string command, PurchasingWriteEnvelope envelope, string hash, string reason,
            Func<PurchaseServiceOrderState?, CancellationToken, Task<PurchaseServiceOrderState>> mutate, Func<CancellationToken, Task> fence, CancellationToken token)
        {
            var value = await mutate(Saved, token); await fence(token); Saved = value with { DocumentNumber = "SENTETIK-number-fixture" };
            return new(Saved, null, false, envelope.CorrelationId);
        }
    }
}
