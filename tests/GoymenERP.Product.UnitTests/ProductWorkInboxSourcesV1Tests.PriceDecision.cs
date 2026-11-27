using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.WorkInbox;

namespace GoymenERP.Product.UnitTests;

public sealed partial class ProductWorkInboxSourcesV1Tests
{
    [Fact]
    public async Task Price_purpose_preserves_version_id_header_version_target_number_and_every_preparer_without_raw_prices()
    {
        var list = PriceList(CompanyA, "SENTETIK list"); list.Version = 7;
        var target = Version(CompanyA, list.Id, 2, PriceListVersionState.Draft, Start, "[\"SENTETIK-maker\",\"SENTETIK-editor\"]");
        await SeedAsync(CompanyA, db => { db.PriceLists.Add(list); db.Versions.Add(target); });
        var source = new ProductPriceListVersionWorkInboxSourceV1(Context(CompanyA), Fence());
        var snapshot = await source.ReadDecisionAsync(Caller(), target.VersionId, default);
        Assert.NotNull(snapshot); Assert.Equal("7", snapshot.SourceVersion); Assert.True(snapshot.Pending);
        Assert.Equal((list.Id, target.VersionId, 2), (snapshot.ProductPriceListProof!.PriceListId, snapshot.ProductPriceListProof.VersionId, snapshot.ProductPriceListProof.VersionNo));
        Assert.Equal(["SENTETIK-editor"], snapshot.Item.ExcludedDeciders);
        Assert.Equal("/fiyat-listeleri/" + list.Id, snapshot.Item.Link);
        Assert.Contains(ProductPriceListPermissionsV1.Read, asked); Assert.Contains(ProductPriceListPermissionsV1.Publish, asked);
        Assert.DoesNotContain("productPriceListProof", JsonSerializer.Serialize(snapshot, ProductPriceListWire.Json));
    }

    [Theory]
    [InlineData(null)] [InlineData("actor")] [InlineData("duplicate")] [InlineData("wrong-target")]
    public async Task Price_purpose_old_expected_header_selects_only_original_actor_and_payload_after_unrelated_history_grows(string? fault)
    {
        var list = PriceList(CompanyA, "SENTETIK later title"); list.Version = 400;
        var target = Version(CompanyA, list.Id, 1, PriceListVersionState.Published, Start, "[\"SENTETIK-maker\"]");
        target.PublishedBy = "SENTETIK-checker";
        var original = new PriceListViewV1(list.Id, "SENTETIK original title", "TRY", "Sales", "ExcludingVat", "4", Start, Start,
            1, null, [new(target.VersionId, 1, "Published", "Active", "2026-12-01", null, Start, target.CreatedBy,
                target.PublishedAtUtc, target.PublishedBy, [])], PriceListScopeViewV1.Company);
        if (fault == "wrong-target") original = original with { Versions = [original.Versions[0] with { VersionId = Guid.NewGuid() }] };
        await SeedAsync(CompanyA, db =>
        {
            db.PriceLists.Add(list); db.Versions.Add(target);
            db.Attempts.Add(new() { CompanyId = CompanyA, CommandName = ProductPriceListCommandsV1.PublishV1,
                IdempotencyKeyHash = new string('A', 64), RequestHash = ProductPriceListWire.PublishRequestHash(list.Id, new("3", 1)),
                ActorId = fault == "actor" ? "SENTETIK-other" : "SENTETIK-checker", Outcome = 1, PriceListId = list.Id, ResultJson = JsonSerializer.Serialize(original, ProductPriceListWire.Json),
                CorrelationId = "SENTETIK original", RecordedAtUtc = Start });
            if (fault == "duplicate") db.Attempts.Add(new() { CompanyId = CompanyA, CommandName = ProductPriceListCommandsV1.PublishV1,
                IdempotencyKeyHash = new string('C', 64), RequestHash = ProductPriceListWire.PublishRequestHash(list.Id, new("3", 1)), ActorId = "SENTETIK-checker",
                Outcome = 1, PriceListId = list.Id, ResultJson = JsonSerializer.Serialize(original, ProductPriceListWire.Json), CorrelationId = "SENTETIK duplicate", RecordedAtUtc = Start });
            for (var i = 0; i < 135; i++) db.Attempts.Add(new() { CompanyId = CompanyA, CommandName = ProductPriceListCommandsV1.PublishV1,
                IdempotencyKeyHash = i.ToString("X64", CultureInfo.InvariantCulture), RequestHash = new string('b', 64), ActorId = "SENTETIK-checker",
                Outcome = 1, PriceListId = list.Id, ResultJson = JsonSerializer.Serialize(original, ProductPriceListWire.Json), CorrelationId = "SENTETIK", RecordedAtUtc = Start });
        });
        var source = new ProductPriceListVersionWorkInboxSourceV1(Context(CompanyA), Fence());
        if (fault is not null)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionVersionAsync(Caller(), target.VersionId, "3", default));
            return;
        }
        var snapshot = await source.ReadDecisionVersionAsync(Caller(), target.VersionId, "3", default);
        Assert.NotNull(snapshot); Assert.Equal("3", snapshot.SourceVersion); Assert.False(snapshot.Pending);
        Assert.Equal(target.CreatedBy, snapshot.Item.PreparedBy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionVersionAsync(Caller(), target.VersionId, "5", default));
    }

    [Theory]
    [InlineData(ProductPriceListPermissionsV1.Read)] [InlineData(ProductPriceListPermissionsV1.Publish)]
    public async Task Price_purpose_requires_both_native_human_permissions_including_after_owner_read(string denied)
    {
        var list = PriceList(CompanyA, "SENTETIK list"); var target = Version(CompanyA, list.Id, 1, PriceListVersionState.Draft, Start, "[\"SENTETIK-maker\"]");
        await SeedAsync(CompanyA, db => { db.PriceLists.Add(list); db.Versions.Add(target); });
        foreach (var late in new[] { false, true })
        {
            var authority = new PricePurposeAuthority(denied, late);
            var source = new ProductPriceListVersionWorkInboxSourceV1(Context(CompanyA), new(Resolved(CompanyA), new User(), authority));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Caller(), target.VersionId, default));
            Assert.True(authority.Calls > 0);
        }
    }
    private sealed class PricePurposeAuthority(string denied, bool late) : IErpAuthorityRevalidationService
    {
        public int Calls;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Calls++; return Task.FromResult(new PermissionScope(!(permission == denied && (!late || Calls > 2)), true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
}
