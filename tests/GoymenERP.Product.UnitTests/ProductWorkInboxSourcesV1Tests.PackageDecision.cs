using System.Text.Json;
using GoymenERP.Modules.Product.Infrastructure.WorkInbox;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;

namespace GoymenERP.Product.UnitTests;

public sealed partial class ProductWorkInboxSourcesV1Tests
{
    [Fact]
    public async Task Package_purpose_reads_exact_immutable_version_after_later_head_and_keeps_generic_wire_private()
    {
        var row = Package(CompanyA, 1, Start, Guid.NewGuid()); row.EntryCount = 1;
        var product = Guid.NewGuid();
        await SeedAsync(CompanyA, db =>
        {
            db.PackageStandardVersions.Add(row);
            db.PackageStandardLines.Add(new() { CompanyId = CompanyA, Version = 1, ProductId = product, PiecesPerPackage = 12 });
            db.PackageStandardDecisions.Add(new() { CompanyId = CompanyA, Version = 1, DecisionId = Guid.NewGuid(),
                Decision = "Approved", Reason = "SENTETIK decision", DecidedBy = "SENTETIK-checker", DecidedAtUtc = Start, CorrelationId = "SENTETIK" });
            db.PackageStandardVersions.Add(Package(CompanyA, 2, Start.AddDays(1), null));
        });
        var snapshot = await new ProductPackageStandardWorkInboxSourceV1(Context(CompanyA), Fence()).ReadDecisionAsync(Caller(), row.VersionId, default);
        Assert.NotNull(snapshot); Assert.False(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion);
        Assert.Equal(row.CreatedBy, snapshot.Item.PreparedBy);
        var entry = Assert.Single(snapshot.ProductPackageStandardProof!.Table.Entries);
        Assert.Equal((product, 12), (entry.ProductId, entry.PiecesPerPackage));
        Assert.DoesNotContain("productPackageStandardProof", JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("/urun/" + row.EditedProductId, snapshot.Item.Link);
    }

    [Fact]
    public async Task Package_purpose_missing_entries_never_releases_a_partial_human_table()
    {
        var row = Package(CompanyA, 1, Start, null);
        await SeedAsync(CompanyA, db => db.PackageStandardVersions.Add(row));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProductPackageStandardWorkInboxSourceV1(Context(CompanyA), Fence())
            .ReadDecisionAsync(Caller(), row.VersionId, default));
    }

    [Fact]
    public async Task Package_purpose_cannot_read_foreign_company_or_after_native_permission_revocation()
    {
        var row = Package(CompanyB, 1, Start, null);
        await SeedAsync(CompanyB, db => db.PackageStandardVersions.Add(row));
        var source = new ProductPackageStandardWorkInboxSourceV1(Context(CompanyA), Fence());
        Assert.Null(await source.ReadDecisionAsync(Caller(), row.VersionId, default));
        granted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(Caller(), row.VersionId, default));
    }
}
