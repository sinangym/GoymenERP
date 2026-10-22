using GoymenERP.Modules.BusinessPurchasing.Application.Consumables;
using GoymenERP.Modules.BusinessPurchasing.Domain.Consumables;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.BusinessPurchasing.UnitTests.Consumables;

public sealed class BusinessConsumableCardV1Tests
{
    private static readonly Guid CompanyId = Guid.Parse("17a9e3ac-6308-49f4-8121-04d872194fc7");
    private static readonly Guid ConsumableId = Guid.Parse("ee4158a1-2c8b-40c1-b229-ed55412a6734");
    private static readonly Guid UnitId = Guid.Parse("bd85575a-80c4-4cea-bccb-54049100290b");
    private static readonly Guid ProfileId = Guid.Parse("2bf94a12-7cd5-46e0-91b9-80c428bce27a");

    [Theory]
    [InlineData(BusinessConsumableInventoryRepresentationV1.StockTracked)]
    [InlineData(BusinessConsumableInventoryRepresentationV1.DirectExpense)]
    public void Card_preserves_only_supported_inventory_representations(
        BusinessConsumableInventoryRepresentationV1 representation)
    {
        var card = CreateCard(representation: representation);

        Assert.Equal(CompanyId, card.CompanyId);
        Assert.Equal(ConsumableId, card.ConsumableId);
        Assert.Equal("card-v7", card.Version);
        Assert.Equal("Active", card.LifecycleCode);
        Assert.Equal(representation, card.InventoryRepresentation);
        Assert.Equal(UnitId, card.UnitProfile.UnitId);
        Assert.Equal(ProfileId, card.UnitProfile.ProfileId);
        Assert.Equal(4, card.UnitProfile.ProfileVersion);
    }

    [Fact]
    public void Card_rejects_missing_company_identity() =>
        Assert.Throws<ArgumentException>(() => CreateCard(companyId: Guid.Empty));

    [Fact]
    public void Card_rejects_missing_consumable_identity() =>
        Assert.Throws<ArgumentException>(() => CreateCard(consumableId: Guid.Empty));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Card_rejects_missing_version(string version) =>
        Assert.Throws<ArgumentException>(() => CreateCard(version: version));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Card_rejects_missing_lifecycle(string lifecycleCode) =>
        Assert.Throws<ArgumentException>(() => CreateCard(lifecycleCode: lifecycleCode));

    [Fact]
    public void Card_rejects_unknown_inventory_representation() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateCard(representation: (BusinessConsumableInventoryRepresentationV1)99));

    [Fact]
    public void Card_rejects_missing_profile_reference() =>
        Assert.Throws<ArgumentNullException>(() => new BusinessConsumableCardV1(
            CompanyId,
            ConsumableId,
            "card-v7",
            "Active",
            BusinessConsumableInventoryRepresentationV1.StockTracked,
            null!));

    [Fact]
    public void Profile_reference_rejects_missing_unit_identity() =>
        Assert.Throws<ArgumentException>(() => new BusinessConsumableUnitProfileReferenceV1(Guid.Empty, ProfileId, 4));

    [Fact]
    public void Profile_reference_rejects_missing_profile_identity() =>
        Assert.Throws<ArgumentException>(() => new BusinessConsumableUnitProfileReferenceV1(UnitId, Guid.Empty, 4));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Profile_reference_rejects_non_positive_profile_version(long version) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BusinessConsumableUnitProfileReferenceV1(UnitId, ProfileId, version));

    [Fact]
    public void Selection_preserves_company_version_and_effective_date()
    {
        var effectiveOn = new DateOnly(2026, 10, 22);

        var selection = new BusinessConsumableProfileSelectionV1(
            CompanyId, ConsumableId, "card-v7", effectiveOn);

        Assert.Equal(CompanyId, selection.CompanyId);
        Assert.Equal(ConsumableId, selection.ConsumableId);
        Assert.Equal("card-v7", selection.ExpectedConsumableVersion);
        Assert.Equal(effectiveOn, selection.EffectiveOn);
    }

    [Fact]
    public void Snapshot_accepts_exact_card_and_settings_profile()
    {
        var selection = CreateSelection();
        var card = CreateCard();
        var profile = CreateProfile();

        var snapshot = new BusinessConsumableProfileSnapshotV1(selection, card, profile);

        Assert.Same(selection, snapshot.Selection);
        Assert.Same(card, snapshot.Card);
        Assert.Same(profile, snapshot.UnitProfile);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("consumable")]
    [InlineData("version")]
    [InlineData("unit")]
    [InlineData("profileCompany")]
    [InlineData("profile")]
    [InlineData("profileVersion")]
    [InlineData("nullProfile")]
    [InlineData("inactive")]
    [InlineData("before")]
    [InlineData("after")]
    public void Snapshot_rejects_stale_or_cross_boundary_source_data(string mismatch)
    {
        var selection = CreateSelection(
            companyId: mismatch == "company" ? Guid.NewGuid() : CompanyId,
            consumableId: mismatch == "consumable" ? Guid.NewGuid() : ConsumableId,
            version: mismatch == "version" ? "card-v8" : "card-v7",
            effectiveOn: mismatch switch
            {
                "before" => new DateOnly(2025, 12, 31),
                "after" => new DateOnly(2027, 1, 1),
                _ => new DateOnly(2026, 10, 22),
            });
        var profile = CreateProfile(
            companyId: mismatch == "profileCompany" ? Guid.NewGuid() : CompanyId,
            unitId: mismatch == "unit" ? Guid.NewGuid() : UnitId,
            profileId: mismatch == "profile" ? Guid.NewGuid() : ProfileId,
            profileVersion: mismatch == "profileVersion" ? 5 : 4,
            isActive: mismatch != "inactive");
        if (mismatch == "nullProfile")
            profile = profile with { Profile = null! };

        Assert.Throws<InvalidOperationException>(() =>
            new BusinessConsumableProfileSnapshotV1(selection, CreateCard(), profile));
    }

    private static BusinessConsumableCardV1 CreateCard(
        Guid? companyId = null,
        Guid? consumableId = null,
        string version = "card-v7",
        string lifecycleCode = "Active",
        BusinessConsumableInventoryRepresentationV1 representation =
            BusinessConsumableInventoryRepresentationV1.StockTracked,
        BusinessConsumableUnitProfileReferenceV1? unitProfile = null) =>
        new(
            companyId ?? CompanyId,
            consumableId ?? ConsumableId,
            version,
            lifecycleCode,
            representation,
            unitProfile ?? new BusinessConsumableUnitProfileReferenceV1(UnitId, ProfileId, 4));

    private static BusinessConsumableProfileSelectionV1 CreateSelection(
        Guid? companyId = null,
        Guid? consumableId = null,
        string version = "card-v7",
        DateOnly? effectiveOn = null) =>
        new(
            companyId ?? CompanyId,
            consumableId ?? ConsumableId,
            version,
            effectiveOn ?? new DateOnly(2026, 10, 22));

    private static UnitProfileV1 CreateProfile(
        Guid? companyId = null,
        Guid? unitId = null,
        Guid? profileId = null,
        long profileVersion = 4,
        bool isActive = true) =>
        new(
            companyId ?? CompanyId,
            unitId ?? UnitId,
            "EA",
            "Count",
            false,
            new ProfileVersionV1(profileId ?? ProfileId, profileVersion),
            0,
            isActive,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31));
}
