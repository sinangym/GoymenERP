using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkApprovedSourceCatalogMakerExceptionTests
{
    [Theory]
    [InlineData("accounting", "accounting.approved-source-catalog.v1", false)]
    [InlineData("Accounting", "accounting.approved-source-catalog.v1", true)]
    [InlineData("finance", "accounting.approved-source-catalog.v1", true)]
    [InlineData("accounting", "accounting.approved-source-catalog.v2", true)]
    [InlineData("accounting", "accounting.posting-mapping.v1", true)]
    [InlineData("accounting", "accounting.journal-reversal.v1", true)]
    public void Native_catalog_exception_preserves_creator_metadata_and_explicit_exclusions(string module, string kind, bool makerExcluded)
    {
        var item = Item(module, kind);
        Assert.Equal(makerExcluded, item.Excludes("SENTETIK-maker"));
        Assert.True(item.Excludes("SENTETIK-excluded"));
        Assert.False(item.Excludes("SENTETIK-other"));
        Assert.Equal("SENTETIK-maker", item.PreparedBy);
        Assert.Equal(["SENTETIK-excluded"], item.ExcludedDeciders);
        Assert.True((item with { ExcludedDeciders = ["SENTETIK-maker"] }).Excludes("SENTETIK-maker"));
    }

    [Fact]
    public void Catalog_scalar_proof_never_changes_the_public_snapshot_wire()
    {
        var item = Item("accounting", "accounting.approved-source-catalog.v1");
        var snapshot = new WorkApprovalDecisionSnapshotV1(Guid.NewGuid(), item, "1", true);
        var proof = new WorkApprovedSourceCatalogProofV1(snapshot.CompanyId, item.ItemId, Guid.NewGuid(),
            item.WaitingSinceUtc, "Prepared", 1, 0, new string('A', 64), new string('B', 64));
        var privateSnapshot = snapshot with { ApprovedSourceCatalogProof = proof };
        Assert.Same(proof, privateSnapshot.ApprovedSourceCatalogProof);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(snapshot, options), JsonSerializer.Serialize(privateSnapshot, options));
    }

    private static WorkInboxItemV1 Item(string module, string kind) => new(module, kind, Guid.NewGuid(),
        "SENTETIK", DateTimeOffset.UnixEpoch, "/SENTETIK", "SENTETIK-maker", [], [], ["SENTETIK-excluded"]);
}
