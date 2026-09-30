using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Xunit.Sdk;
using ProductEntity = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests.SqlAcceptance;

// These intentionally corrupted observations test the acceptance assertions and
// closed-connection boundary only. They do not execute SQL or simulate SQL PASS.
public sealed class ProductSqlEvidenceBoundaryTests
{
    private static readonly Guid GroupId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid InstanceId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
    private static readonly Guid ProductId = Guid.Parse("dddddddd-0000-0000-0000-000000000003");
    private const string RunId = "product-u0102-abcdef012345";
    private const string Database = "GoymenERP_Product_U0102_abcdef012345";
    private static readonly CodeGroupState Group = new(GroupId, "Sentetik grup", "TEST");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-12T16:00:00Z");

    [Theory]
    [InlineData("c006-abcdef012345", Database)]
    [InlineData(RunId, "GoymenCariC006_abcdef012345")]
    [InlineData(RunId, "GoymenERP_Product_U0102_000000000000")]
    public void Foreign_or_mismatched_fixture_identity_is_refused_before_a_connection_exists(string run, string database) =>
        Assert.Throws<ArgumentException>(() => new ProductSqlFixture(run, InstanceId, database, GroupId));

    [Fact]
    public async Task Snapshot_refuses_a_closed_connection_without_opening_it()
    {
        var fixture = new ProductSqlFixture(RunId, InstanceId, Database, GroupId);
        await using var connection = new SqlConnection();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProductSqlSnapshot.ReadAsync(fixture, connection, null, default));
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void A_different_or_duplicate_code_group_cannot_be_seeded_as_the_contract_group()
    {
        var fixture = new ProductSqlFixture(RunId, InstanceId, Database, GroupId);
        var group = ProductCodeGroupRow.Define(GroupId, Group.Name, Group.Prefix);
        Assert.Throws<ArgumentException>(() => fixture.BindCodeGroups([ProductCodeGroupRow.Define(Guid.NewGuid(), "Yanlış grup", "TEST")]));
        Assert.Throws<ArgumentException>(() => fixture.BindCodeGroups([group, group]));
        Assert.Equal(GroupId, Assert.Single(fixture.BindCodeGroups([group])).Id);
        Assert.Throws<ArgumentException>(() => new ProductSqlFixture(RunId, InstanceId, Database, Guid.Empty));
    }

    [Theory]
    [InlineData("product")]
    [InlineData("group")]
    [InlineData("counter")]
    [InlineData("registration")]
    public void No_change_evidence_detects_mutation_in_each_product_owned_table(string table)
    {
        var row = Row();
        var before = State(row);
        var changed = table switch
        {
            "product" => before with { Products = [row with { Name = "Beklenmeyen" }] },
            "group" => before with { Groups = [Group with { Prefix = "WRONG" }] },
            "counter" => before with { Counters = [new("TEST", 2)] },
            _ => before with { Registrations = [new(row.Code, row.Id, false)] },
        };
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.Unchanged(before, changed));
    }

    [Fact]
    public void A_create_receipt_cannot_hide_a_second_product_or_a_retired_code_collision()
    {
        var first = Row();
        var second = Row(Guid.Parse("dddddddd-0000-0000-0000-000000000004"), 2);
        var empty = new ProductSqlSnapshot([], [Group], [], []);
        var extra = new ProductSqlSnapshot([first, second], [Group], [new("TEST", 2)],
            [new(first.Code, first.Id, true), new(second.Code, second.Id, true)]);
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.Created(empty, extra, Response(first), GroupId));
        var before = State(first) with { Registrations = [new(first.Code, first.Id, true), new(second.Code, first.Id, false)] };
        var after = extra with { Registrations = [.. before.Registrations, new(second.Code, second.Id, true)] };
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.Created(before, after, Response(second), GroupId));
    }

    [Fact]
    public void An_edit_receipt_cannot_hide_a_counter_advance_or_false_becoming_unknown()
    {
        var before = State(Row());
        var changed = before.Products[0] with { Name = "Yeni", Version = 2, UpdatedAtUtc = Now.AddSeconds(1) };
        var after = State(changed);
        ProductSqlAssertions.Edited(before, after, Response(changed));
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.Edited(before,
            after with { Counters = [new("TEST", 2)] }, Response(changed)));
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.Edited(before,
            after with { Products = [changed with { Purchasable = new(null, (int)ProductRoleProvenance.NotEvaluated) }] }, Response(changed)));
    }

    [Fact]
    public void Replay_after_edit_requires_the_original_card_and_no_new_record_effect()
    {
        var original = Row();
        var current = original with { Name = "Düzeltilen", Version = 2, UpdatedAtUtc = Now.AddSeconds(1) };
        var persisted = State(current);
        ProductSqlAssertions.Replayed(persisted, persisted, Response(original), Response(original) with { IsReplay = true });
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.Replayed(persisted, persisted,
            Response(original), Response(current) with { IsReplay = true }));
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.Replayed(persisted,
            persisted with { Counters = [new("TEST", 2)] }, Response(original), Response(original) with { IsReplay = true }));
    }

    [Fact]
    public void Race_receipts_must_account_for_two_unique_products_and_one_shared_prefix_counter()
    {
        var first = Row();
        var second = Row(Guid.Parse("dddddddd-0000-0000-0000-000000000004"), 3);
        var empty = new ProductSqlSnapshot([], [Group], [], []);
        var result = new ProductSqlSnapshot([first, second], [Group], [new("TEST", 3)],
            [new(first.Code, first.Id, true), new(second.Code, second.Id, true)]);
        // A gap is allowed; the assertion must not create a gapless-number policy.
        ProductSqlAssertions.CreatedBatch(empty, result, [Response(first), Response(second)], [GroupId, GroupId]);
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.CreatedBatch(empty,
            result with { Counters = [new("TEST", 3), new("TEST", 3)] }, [Response(first), Response(second)], [GroupId, GroupId]));
    }

    [Fact]
    public void Import_receipt_requires_one_current_and_one_legacy_registration_per_new_draft()
    {
        var first = Row();
        var second = Row(Guid.Parse("dddddddd-0000-0000-0000-000000000004"), 2);
        var before = new ProductSqlSnapshot([], [Group], [], []);
        var after = new ProductSqlSnapshot([first, second], [Group], [new("TEST", 2)],
        [
            new(first.Code, first.Id, true),
            new("OLD-1", first.Id, false),
            new(second.Code, second.Id, true),
            new("OLD-2", second.Id, false),
        ]);
        var imported = new[]
        {
            new ProductImportCreatedV1(first.Id, first.Code, "OLD-1", first.Name, first.GroupId),
            new ProductImportCreatedV1(second.Id, second.Code, "OLD-2", second.Name, second.GroupId),
        };

        ProductSqlAssertions.ImportedBatch(before, after, imported);
        Assert.ThrowsAny<XunitException>(() => ProductSqlAssertions.ImportedBatch(before,
            after with { Registrations = after.Registrations.Where(row => row.Code != "OLD-2").ToArray() }, imported));
    }

    private static ProductStoredRecord Row(Guid? id = null, long sequence = 1) => ProductStoredRecord.Capture(
        ProductEntity.CreateDraft(id ?? ProductId, ProductCode.FromSequence("TEST", sequence), "Sentetik sac", GroupId,
            null, null, ProductRoles.NotEvaluated with { Purchasable = ProductRoleState.Manual(false) }, Now), new());

    private static ProductSqlSnapshot State(ProductStoredRecord row) => new([row], [Group], [new("TEST", 1)], [new(row.Code, row.Id, true)]);

    private static ProductMutationResponse Response(ProductStoredRecord row) => new(new(
        ProductDraftView.FromProduct(row.RestoreProduct(), new(Group.Name, null, null, null)), row.RestoreReferences()), false);
}
