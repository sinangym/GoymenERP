using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// The duplicate-key contract of the store's save (#333 SİPARİŞ P3-1 and INFO-1): the colliding id is read from
/// the named key's OWN error, which must carry exactly one GUID (a single-column key). Anything else is null, so
/// the answer falls back to the retryable one — never to a made-up conflict.
/// </summary>
public sealed class InventoryDuplicateKeyTests
{
    private const string Lot = "7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a62";
    private const string Label = "7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a99";

    [Fact]
    public void The_named_keys_own_error_with_one_guid_gives_the_id()
    {
        Assert.Equal(Guid.Parse(Lot), InventoryStore.DuplicateKey([(2627, PrimaryKey("PK_Lots", "inventory.Lots", Lot))], "'PK_Lots'"));
    }

    [Fact]
    public void A_lot_and_a_label_colliding_in_one_batch_each_give_their_own_id()
    {
        (int, string)[] errors =
        [
            (2627, PrimaryKey("PK_Lots", "inventory.Lots", Lot)),
            (3621, "The statement has been terminated."),
            (2627, PrimaryKey("PK_Labels", "inventory.Labels", Label)),
            (3621, "The statement has been terminated."),
        ];

        Assert.Equal(Guid.Parse(Lot), InventoryStore.DuplicateKey(errors, "'PK_Lots'"));
        Assert.Equal(Guid.Parse(Label), InventoryStore.DuplicateKey(errors, "'PK_Labels'"));
    }

    [Fact]
    public void No_own_error_two_own_errors_or_a_composite_key_is_not_read()
    {
        // The key is not named by any duplicate-key error.
        Assert.Null(InventoryStore.DuplicateKey([(2627, PrimaryKey("PK_Labels", "inventory.Labels", Label))], "'PK_Lots'"));
        // The key is named, but not by a duplicate-key error.
        Assert.Null(InventoryStore.DuplicateKey([(547, $"The INSERT statement conflicted with 'PK_Lots' ({Lot})")], "'PK_Lots'"));
        // Two duplicate-key errors of the same key: which one is not decidable.
        Assert.Null(InventoryStore.DuplicateKey(
            [(2627, PrimaryKey("PK_Lots", "inventory.Lots", Lot)), (2627, PrimaryKey("PK_Lots", "inventory.Lots", Label))], "'PK_Lots'"));
        // A composite key value (two GUIDs in the key's own error): the single-column contract no longer holds.
        Assert.Null(InventoryStore.DuplicateKey([(2627, PrimaryKey("PK_Lots", "inventory.Lots", $"{Lot}, {Label}"))], "'PK_Lots'"));
        // No GUID at all.
        Assert.Null(InventoryStore.DuplicateKey([(2627, PrimaryKey("PK_Lots", "inventory.Lots", "42"))], "'PK_Lots'"));
        Assert.Null(InventoryStore.DuplicateKey([], "'PK_Lots'"));
    }

    private static string PrimaryKey(string constraint, string table, string value) =>
        $"Violation of PRIMARY KEY constraint '{constraint}'. Cannot insert duplicate key in object '{table}'. The duplicate key value is ({value}).";
}
