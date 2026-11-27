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

    [Fact]
    public void Mapping_scalar_proof_keeps_constructor_and_public_wire_without_weakening_maker_exclusion()
    {
        var item = Item("accounting", "accounting.posting-mapping.v1");
        var snapshot = new WorkApprovalDecisionSnapshotV1(Guid.NewGuid(), item, "2", true);
        var proof = new WorkPostingAccountMappingProofV1(snapshot.CompanyId, long.MaxValue, Guid.NewGuid(),
            item.WaitingSinceUtc, "Prepared", 2, 1, new string('C', 64), new string('D', 64));
        var privateSnapshot = snapshot with { PostingAccountMappingProof = proof };
        Assert.Same(proof, privateSnapshot.PostingAccountMappingProof);
        Assert.Equal(long.MaxValue, privateSnapshot.PostingAccountMappingProof.MappingVersion);
        Assert.True(privateSnapshot.Item.Excludes("SENTETIK-maker"));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.Serialize(snapshot, options), JsonSerializer.Serialize(privateSnapshot, options));
    }

    private static WorkInboxItemV1 Item(string module, string kind) => new(module, kind, Guid.NewGuid(),
        "SENTETIK", DateTimeOffset.UnixEpoch, "/SENTETIK", "SENTETIK-maker", [], [], ["SENTETIK-excluded"]);
}
