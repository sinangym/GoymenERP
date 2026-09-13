using System.Text.Json;
using GoymenERP.CariAcceptance006;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class ProductSourceCaptureTests
{
    [Fact]
    public void FixtureInventory_IsFrozenAndLimitedToExplicitProductSources()
    {
        var input = new[] { "tests/GoymenERP.Product.UnitTests/SqlAcceptance/ProductSqlFixture.cs",
            "tools/GoymenERP.CariAcceptance006/SourceBinding.Product.cs" };
        var frozen = SourceBinding.ValidateProductFixturePaths(input);
        input[0] = "src/frontend/foreign.ts";
        Assert.Equal("tests/GoymenERP.Product.UnitTests/SqlAcceptance/ProductSqlFixture.cs", frozen[0]);
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("tools/GoymenERP.CariAcceptance006/../outside.cs")]
    [InlineData("tools/GoymenERP.CariAcceptance006//Factory.cs")]
    [InlineData("tools\\GoymenERP.CariAcceptance006\\Factory.cs")]
    [InlineData("C:/tools/GoymenERP.CariAcceptance006/Factory.cs")]
    [InlineData("tools/GoymenERP.CariAcceptance006/secret.dpapi")]
    [InlineData("src/frontend/foreign.cs")]
    public void ForeignOrAmbiguousFixturePath_IsRejected(string path) =>
        Assert.Throws<InvalidOperationException>(() => SourceBinding.ValidateProductFixturePaths([path]));

    [Fact]
    public void EmptyDuplicateOrUnsortedInventory_IsRejected()
    {
        const string a = "tools/GoymenERP.CariAcceptance006/A.cs";
        const string b = "tools/GoymenERP.CariAcceptance006/B.cs";
        Assert.Throws<InvalidOperationException>(() => SourceBinding.ValidateProductFixturePaths([]));
        Assert.Throws<InvalidOperationException>(() => SourceBinding.ValidateProductFixturePaths([a, a]));
        Assert.Throws<InvalidOperationException>(() => SourceBinding.ValidateProductFixturePaths([b, a]));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"compiledHead\":\"other\",\"diskArtifacts\":{\"GoymenERP.Api.dll\":\"hash\"}}")]
    [InlineData("{\"compiledHead\":\"head\",\"diskArtifacts\":{\"GoymenERP.Api.dll\":\"changed\"}}")]
    [InlineData("{\"compiledHead\":\"head\",\"diskArtifacts\":{\"GoymenERP.Other.dll\":\"hash\"}}")]
    [InlineData("{\"compiledHead\":\"head\",\"diskArtifacts\":{\"GoymenERP.Api.dll\":\"hash\"},\"extra\":true}")]
    public void AnySourceDifference_IsRejected(string changed)
    {
        using var accepted = JsonDocument.Parse("{\"compiledHead\":\"head\",\"diskArtifacts\":{\"GoymenERP.Api.dll\":\"hash\"}}");
        using var current = JsonDocument.Parse(changed);
        Assert.Throws<InvalidOperationException>(() => SourceBinding.RequireProductSourceMatch(accepted.RootElement, current.RootElement));
    }

    [Fact]
    public void EquivalentPropertyOrder_DoesNotChangeSourceMeaning()
    {
        using var accepted = JsonDocument.Parse("{\"compiledHead\":\"head\",\"diskArtifacts\":{\"GoymenERP.Api.dll\":\"hash\"}}");
        using var observed = JsonDocument.Parse("{\"diskArtifacts\":{\"GoymenERP.Api.dll\":\"hash\"},\"compiledHead\":\"head\"}");
        SourceBinding.RequireProductSourceMatch(accepted.RootElement, observed.RootElement);
    }
}
