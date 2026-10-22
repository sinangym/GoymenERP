using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

public sealed class ConsumableStockProfileContractsV1Tests
{
    private static readonly Guid CompanyId = Guid.Parse("97000000-0000-4000-8000-000000000001");
    private static readonly Guid ConsumableId = Guid.Parse("97000000-0000-4000-8000-000000000002");
    private static readonly Guid UnitId = Guid.Parse("97000000-0000-4000-8000-000000000003");
    private static readonly Guid ProfileId = Guid.Parse("97000000-0000-4000-8000-000000000004");
    private static readonly DateOnly EffectiveOn = new(2026, 10, 22);

    [Theory]
    [InlineData(ConsumableInventoryRepresentationV1.StockTracked)]
    [InlineData(ConsumableInventoryRepresentationV1.DirectExpense)]
    public void Snapshot_preserves_owner_version_lifecycle_representation_and_settings_profile(
        ConsumableInventoryRepresentationV1 representation)
    {
        var profile = Profile();

        var snapshot = Snapshot(representation, profile: profile);

        Assert.Equal(CompanyId, snapshot.CompanyId);
        Assert.Equal(ConsumableId, snapshot.ConsumableId);
        Assert.Equal("consumable-v7", snapshot.ConsumableVersion);
        Assert.Equal("Active", snapshot.LifecycleCode);
        Assert.Equal(representation, snapshot.Representation);
        Assert.Same(profile, snapshot.UnitProfile);
        Assert.Equal(EffectiveOn, snapshot.EffectiveOn);
    }

    [Fact]
    public void Representation_is_closed_to_stock_tracked_and_direct_expense()
    {
        Assert.Equal(
            new[]
            {
                ConsumableInventoryRepresentationV1.StockTracked,
                ConsumableInventoryRepresentationV1.DirectExpense,
            },
            Enum.GetValues<ConsumableInventoryRepresentationV1>());

        AssertCode(ConsumableStockProfileCodesV1.InvalidRepresentation,
            () => Snapshot((ConsumableInventoryRepresentationV1)0));
    }

    [Fact]
    public void Company_consumable_and_version_are_required()
    {
        AssertCode(ConsumableStockProfileCodesV1.InvalidCompany,
            () => Snapshot(companyId: Guid.Empty));
        AssertCode(ConsumableStockProfileCodesV1.InvalidConsumable,
            () => Snapshot(consumableId: Guid.Empty));
        AssertCode(ConsumableStockProfileCodesV1.InvalidVersion,
            () => Snapshot(version: ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Active")]
    [InlineData("Active ")]
    [InlineData("Act\u0001ive")]
    public void Lifecycle_is_explicit_and_canonical_without_selecting_owner_states(string lifecycle)
    {
        AssertCode(ConsumableStockProfileCodesV1.InvalidLifecycle,
            () => Snapshot(lifecycle: lifecycle));
    }

    [Fact]
    public void Unit_profile_must_belong_to_the_same_company()
    {
        AssertCode(ConsumableStockProfileCodesV1.InvalidUnitProfile,
            () => Snapshot(profile: Profile(companyId: Guid.NewGuid())));
    }

    [Fact]
    public void Unit_and_profile_identity_and_version_are_required()
    {
        AssertCode(ConsumableStockProfileCodesV1.InvalidUnitProfile,
            () => Snapshot(profile: Profile(unitId: Guid.Empty)));
        AssertCode(ConsumableStockProfileCodesV1.InvalidUnitProfile,
            () => Snapshot(profile: Profile(profileId: Guid.Empty)));
        AssertCode(ConsumableStockProfileCodesV1.InvalidUnitProfile,
            () => Snapshot(profile: Profile(profileVersion: 0)));
    }

    [Fact]
    public void Unit_profile_codes_and_scale_are_validated_without_defaulting()
    {
        AssertCode(ConsumableStockProfileCodesV1.InvalidUnitProfile,
            () => Snapshot(profile: Profile(unitCode: "")));
        AssertCode(ConsumableStockProfileCodesV1.InvalidUnitProfile,
            () => Snapshot(profile: Profile(dimensionCode: " volume")));
        AssertCode(ConsumableStockProfileCodesV1.InvalidUnitProfile,
            () => Snapshot(profile: Profile(scale: 29)));
    }

    [Fact]
    public void Inactive_profile_fails_closed()
    {
        AssertCode(ConsumableStockProfileCodesV1.InactiveUnitProfile,
            () => Snapshot(profile: Profile(isActive: false)));
    }

    [Theory]
    [InlineData("2026-10-19")]
    [InlineData("2026-10-25")]
    public void Profile_must_be_effective_on_the_snapshot_date(string date)
    {
        AssertCode(ConsumableStockProfileCodesV1.UnitProfileNotEffective,
            () => Snapshot(effectiveOn: DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Direct_expense_does_not_create_an_inventory_stock_item_or_conversion()
    {
        var snapshot = Snapshot(ConsumableInventoryRepresentationV1.DirectExpense);

        Assert.Equal(ConsumableInventoryRepresentationV1.DirectExpense, snapshot.Representation);
        Assert.DoesNotContain(snapshot.GetType().GetProperties(), property =>
            property.PropertyType == typeof(InventoryStockItemV2)
            || property.Name.Contains("Quantity", StringComparison.Ordinal)
            || property.Name.Contains("Conversion", StringComparison.Ordinal));
    }

    private static ConsumableStockProfileSnapshotV1 Snapshot(
        ConsumableInventoryRepresentationV1 representation = ConsumableInventoryRepresentationV1.StockTracked,
        Guid? companyId = null,
        Guid? consumableId = null,
        string version = "consumable-v7",
        string lifecycle = "Active",
        UnitProfileV1? profile = null,
        DateOnly? effectiveOn = null) =>
        new(
            companyId ?? CompanyId,
            consumableId ?? ConsumableId,
            version,
            lifecycle,
            representation,
            profile ?? Profile(),
            effectiveOn ?? EffectiveOn);

    private static UnitProfileV1 Profile(
        Guid? companyId = null,
        Guid? unitId = null,
        string unitCode = "L",
        string dimensionCode = "Volume",
        Guid? profileId = null,
        long profileVersion = 3,
        int scale = 2,
        bool isActive = true) =>
        new(
            companyId ?? CompanyId,
            unitId ?? UnitId,
            unitCode,
            dimensionCode,
            false,
            new ProfileVersionV1(profileId ?? ProfileId, profileVersion),
            scale,
            isActive,
            new DateOnly(2026, 10, 20),
            new DateOnly(2026, 10, 24));

    private static void AssertCode(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<ConsumableStockProfileContractExceptionV1>(action).Code);
}
