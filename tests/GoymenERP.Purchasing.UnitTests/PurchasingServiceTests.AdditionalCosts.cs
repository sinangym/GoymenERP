using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Expense_reads_require_fresh_price_permission_and_reject_company_membership_loss_after_the_owner_read()
    {
        var source = new ServiceCommandSource { HasService = false }; var store = new AdditionalCostCommandStore(source.Currency);
        var denied = new FixedAuthority(); denied.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var service = new PurchaseAdditionalCostServiceV1(new MutableCompanyContext(), new FixedUser(), denied,
            store, source, source, source, source, TimeProvider.System);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.SourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, store.OrderId))).Status);
        Assert.Equal(0, store.Reads);
        var user = new FixedUser(); var context = new MutableCompanyContext();
        store.AfterRead = () => user.Allowed.Clear();
        service = new(context, user, new FixedAuthority(), store, source, source, source, source, TimeProvider.System);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.SourceAsync(PurchaseAdditionalCostSourceKind.GoodsOrder, store.OrderId))).Status);
        Assert.Equal(1, store.Reads);
    }
    [Fact]
    public async Task Expense_recording_refuses_missing_composition_instead_of_bypassing_the_selected_existing_write_authority()
    {
        var source = new ServiceCommandSource { HasService = false }; var store = new AdditionalCostCommandStore(source.Currency);
        var service = new PurchaseAdditionalCostServiceV1(new MutableCompanyContext(), new FixedUser(), new FixedAuthority(),
            store, source, source, source, source, TimeProvider.System);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.RecordAsync(AdditionalCostRequest(store),
            new("SENTETIK-write-closed", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Status);
        Assert.Equal(0, store.Reads); Assert.Null(store.Saved);
    }
    [Fact]
    public async Task Actual_empty_expense_type_catalog_uses_description_and_an_active_catalog_requires_an_explicit_type()
    {
        var empty = new ServiceCommandSource { HasService = false }; var store = new AdditionalCostCommandStore(empty.Currency);
        var service = ExpenseFixture(empty, store);
        var saved = await service.RecordAsync(AdditionalCostRequest(store), new("SENTETIK-empty-type", "SENTETIK-correlation", null, DateTimeOffset.UtcNow));
        Assert.Null(saved.Cost.ExpenseTypeId); Assert.Equal("SENTETIK nakliye", saved.Cost.Description);
        var active = new ServiceCommandSource(); var activeStore = new AdditionalCostCommandStore(active.Currency);
        Assert.Equal(422, (await Assert.ThrowsAsync<PurchasingServiceException>(() => ExpenseFixture(active, activeStore).RecordAsync(AdditionalCostRequest(activeStore),
            new("SENTETIK-type-required", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Status);
        Assert.Null(activeStore.Saved);
    }
    [Fact]
    public async Task Expense_catalog_failure_is_not_an_empty_catalog_and_stale_VAT_selection_does_not_write_a_fact()
    {
        var outage = new ServiceCommandSource { AfterList = () => throw new SettingsPrerequisiteException("settings_provider_unavailable") };
        var outageStore = new AdditionalCostCommandStore(outage.Currency);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => ExpenseFixture(outage, outageStore).RecordAsync(AdditionalCostRequest(outageStore),
            new("SENTETIK-catalog-outage", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Status);
        Assert.Null(outageStore.Saved);
        var empty = new ServiceCommandSource { HasService = false }; var staleStore = new AdditionalCostCommandStore(empty.Currency);
        Assert.Equal("purchasing_additional_cost_tax_version_conflict", (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            ExpenseFixture(empty, staleStore).RecordAsync(AdditionalCostRequest(staleStore) with { ExpectedVatListVersion = "2" },
                new("SENTETIK-tax-stale", "SENTETIK-correlation", null, DateTimeOffset.UtcNow)))).Code);
        Assert.Null(staleStore.Saved);
    }
    // Explicit synthetic authorization binding for the isolated contract test; not a project permission decision or a grant.
    private static PurchaseAdditionalCostServiceV1 ExpenseFixture(ServiceCommandSource source, AdditionalCostCommandStore store) =>
        new(new MutableCompanyContext(), new FixedUser(), new FixedAuthority(), store, source, source, source, source, TimeProvider.System,
            new(PurchasingPermissionManifestV1.OrdersRevise, "SENTETIK-contract-only"));
    private static RecordPurchaseAdditionalCostV1 AdditionalCostRequest(AdditionalCostCommandStore store) => new(Guid.NewGuid(), "0", store.OrderId,
        PurchaseAdditionalCostSourceKind.GoodsOrder, "3", null, Party, Account, Day, null, null, "SENTETIK nakliye", "100.01", "SENTETIK-20", "1", "SENTETIK belge");
    private sealed class AdditionalCostCommandStore(Guid currency) : IPurchaseAdditionalCostStoreV1
    {
        public Guid OrderId { get; } = Guid.NewGuid(); public PurchaseAdditionalCostState? Saved { get; private set; } public int Reads { get; private set; }
        public Action? AfterRead { get; set; }
        public Task<PurchaseAdditionalCostSourceV1?> ReadSourceAsync(PurchaseAdditionalCostSourceKind kind, Guid order, Guid? acceptance, CancellationToken token)
        { Reads++; AfterRead?.Invoke(); return Task.FromResult<PurchaseAdditionalCostSourceV1?>(new(Company, order, 3, "SENTETIK-SIP-1", kind, acceptance, null,
            new(currency, "TRY", Guid.NewGuid(), 1, 2, 4))); }
        public Task<IReadOnlyList<PurchaseAdditionalCostState>> ListAsync(PurchaseAdditionalCostSourceKind kind, Guid order, CancellationToken token) => throw new NotSupportedException();
        public Task<PurchaseAdditionalCostWriteV1?> ReplayAsync(PurchasingWriteEnvelope envelope, string hash, CancellationToken token)
        { Reads++; return Task.FromResult<PurchaseAdditionalCostWriteV1?>(null); }
        public async Task<PurchaseAdditionalCostWriteV1> RecordAsync(Guid id, PurchasingWriteEnvelope envelope, string hash,
            Func<CancellationToken, Task<PurchaseAdditionalCostState>> create, Func<CancellationToken, Task> fence, CancellationToken token)
        { var value = await create(token); await fence(token); Saved = value; return new(value, null, false, envelope.CorrelationId); }
    }
}
