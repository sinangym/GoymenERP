using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryTransferPolicyHostPortTests
{
    private static readonly Guid Company = Guid.Parse("dd946c8f-f49c-4127-9bc2-e9173051f0f1");
    private static readonly Guid Transfer = Guid.Parse("d384b48c-c438-4f26-bc61-8023901f1a22");
    private static readonly Guid Location = Guid.Parse("a73880de-a9c7-43df-9d10-49a2b65c5cf2");
    private static readonly DateTimeOffset At = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(LogisticsTransferEDispatchAssociationV1.Unknown)]
    [InlineData(LogisticsTransferEDispatchAssociationV1.Linked)]
    public void Missing_or_legacy_official_evidence_never_becomes_no_link_or_not_sent(LogisticsTransferEDispatchAssociationV1 association)
    {
        var actual = LogisticsInventoryTransferCancelGate.Map(new(Company, Transfer, Location, At),
            new(Company, Transfer, Location, association, 1, null, null, false, null,
                LogisticsTransferDepartureV1.SourceVersion + "/1"));
        Assert.Equal(InventoryTransferEDispatchLinkV1.Unreadable, actual.EDispatchLink);
        Assert.Equal(InventoryTransferDepartureFactStatusV1.Unreadable, actual.EDispatchSentToGib.Status);
    }

    [Fact]
    public void Explicit_owner_no_link_fact_maps_only_the_recorded_physical_exit_without_a_GIB_success_claim()
    {
        var actual = LogisticsInventoryTransferCancelGate.Map(new(Company, Transfer, Location, At),
            new(Company, Transfer, Location, LogisticsTransferEDispatchAssociationV1.NotLinked, 2, null, At.AddMinutes(1), false, null,
                LogisticsTransferDepartureV1.SourceVersion + "/2"));
        Assert.Equal(InventoryTransferEDispatchLinkV1.NotLinked, actual.EDispatchLink);
        Assert.Equal(InventoryTransferDepartureFactStatusV1.NotOccurred, actual.EDispatchSentToGib.Status);
        Assert.Null(actual.EDispatchSentToGib.OccurredAtUtc);
        Assert.Equal(At.AddMinutes(1), actual.VehicleExit.OccurredAtUtc);
    }

    [Fact]
    public void A_foreign_or_undated_official_send_is_unreadable_even_if_the_owner_boolean_says_sent()
    {
        foreach (var proof in new[]
        {
            new LogisticsTransferEDispatchEvidenceV1(Guid.NewGuid(), Transfer, Guid.NewGuid(), 1, true, At, true, At.AddMinutes(1), null),
            new LogisticsTransferEDispatchEvidenceV1(Company, Transfer, Guid.NewGuid(), 1, true, At, true, null, null),
            new LogisticsTransferEDispatchEvidenceV1(Company, Transfer, Guid.NewGuid(), 1, true, At, false, At.AddMinutes(1), null),
        })
        {
            var actual = LogisticsInventoryTransferCancelGate.Map(new(Company, Transfer, Location, At),
                new(Company, Transfer, Location, LogisticsTransferEDispatchAssociationV1.Linked, 1, null, null, false, proof,
                    LogisticsTransferDepartureV1.SourceVersion + "/1"));
            Assert.Equal(InventoryTransferEDispatchLinkV1.Unreadable, actual.EDispatchLink);
        }
    }
}
