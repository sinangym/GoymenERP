using GoymenERP.Modules.Identity.Domain.Assignments;

namespace GoymenERP.Identity.UnitTests;

public sealed class ProductMasterAssignmentTests
{
    [Theory]
    [InlineData(ProductMasterOperation.Read)]
    [InlineData(ProductMasterOperation.DraftCreate)]
    [InlineData(ProductMasterOperation.DraftBasicEdit)]
    [InlineData(ProductMasterOperation.Activate)]
    [InlineData(ProductMasterOperation.Deactivate)]
    [InlineData(ProductMasterOperation.Reactivate)]
    public void AssignmentIsOneExplicitOperationAndRevocationPreservesOriginalDecision(ProductMasterOperation operation)
    {
        var grant = Assignment(operation);
        var original = grant.DecisionId;
        var decision = Guid.NewGuid();
        grant.Revoke(decision, "explicit-revocation", DateTimeOffset.UtcNow);
        Assert.Equal(operation, grant.Operation);
        Assert.False(grant.IsActive);
        Assert.Equal(original, grant.DecisionId);
        Assert.Equal("explicit-grant", grant.DecisionReference);
        Assert.Equal(decision, grant.RevocationDecisionId);
        Assert.Throws<ArgumentException>(() => grant.Revoke(Guid.NewGuid(), "implicit-repeat", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UnknownOperationAndEmptyOrNoncanonicalProvenanceFailClosed()
    {
        Assert.Throws<ArgumentException>(() => Assignment((ProductMasterOperation)99));
        Assert.Throws<ArgumentException>(() => Assignment(ProductMasterOperation.Read, Guid.Empty));
        Assert.Throws<ArgumentException>(() => Assignment(ProductMasterOperation.Read, reference: " unknown "));
        Assert.Throws<ArgumentException>(() => Assignment(ProductMasterOperation.Read,
            until: DateTimeOffset.MinValue));
        var grant = Assignment(ProductMasterOperation.Read);
        Assert.Throws<ArgumentException>(() => grant.Revoke(grant.DecisionId, "same-decision", DateTimeOffset.UtcNow));
    }

    private static ProductMasterAssignment Assignment(ProductMasterOperation operation,
        Guid? issuer = null, string reference = "explicit-grant", DateTimeOffset? until = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), operation, issuer ?? Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), reference, DateTimeOffset.UtcNow, until);
}
