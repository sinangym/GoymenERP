using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using F = GoymenERP.Logistics.UnitTests.LogisticsWorkInboxTestFixture;

namespace GoymenERP.Logistics.UnitTests;

public sealed partial class LogisticsWeighingDifferenceWorkInboxSourceTests
{
    [Fact]
    public async Task Purpose_retains_full_native_actor_set_evaluation_and_printed_note_after_approval_without_current_business_gate()
    {
        var f = DecisionFixture(); var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(f.Rows, f.Fence);
        var snapshot = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(f.Context(), F.Id(21), default));
        Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion); Assert.Equal("IRS-000042", snapshot.Item.DocumentLabel);
        Assert.Equal([F.Maker, "SENTETIK-weighing-enterer"], snapshot.LogisticsWeighingDifferenceProof!.EnteredBy);
        Assert.Single(snapshot.LogisticsWeighingDifferenceProof.Products);
        Assert.Equal(["SENTETIK-weighing-enterer"], snapshot.Item.ExcludedDeciders);
        f.Rows.WeighingDifferenceRows[0].Status = 3; f.Rows.WeighingDifferenceRows[0].Version = 2;
        f.Rows.DispatchNoteRows[0].Status = 3; f.Rows.InstructionRows[0].Status = 4;
        var terminal = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await source.ReadDecisionAsync(f.Context(), F.Id(21), default));
        Assert.False(terminal.Pending); Assert.Equal("2", terminal.SourceVersion);
        Assert.Equal(snapshot.LogisticsWeighingDifferenceProof.RecordedBy, terminal.LogisticsWeighingDifferenceProof!.RecordedBy);
        Assert.DoesNotContain("LogisticsWeighingDifferenceProof", JsonSerializer.Serialize(terminal));
        Assert.Equal(snapshot.Item.Link, terminal.Item.Link); // purpose capability remains staged; native link stays.
    }

    [Theory]
    [InlineData("incomplete")] [InlineData("missing-recorder")] [InlineData("missing-products")]
    [InlineData("product-gap")] [InlineData("overflow")] [InlineData("foreign")]
    public async Task Missing_or_truncated_actor_or_product_proof_never_becomes_an_approval_snapshot(string fault)
    {
        var f = DecisionFixture();
        if (fault == "incomplete") f.Rows.WeighingDifferenceRows[0].EnteredByComplete = false;
        if (fault == "missing-recorder") f.Rows.WeighingDifferenceEnteredByRows.RemoveAll(row => row.ActorId == F.Maker);
        if (fault == "missing-products") f.Rows.WeighingDifferenceProductRows.Clear();
        if (fault == "product-gap") f.Rows.WeighingDifferenceProductRows[0].Ordinal = 2;
        if (fault == "overflow") for (var i = 0; i <= WorkInboxRulesV1.MaxExcludedDecidersPerItem; i++) EnteredBy(f, F.Id(21), $"SENTETIK-extra-{i}");
        if (fault == "foreign") f.Rows.WeighingDifferenceRows[0].CompanyId = F.CompanyB;
        var source = new LogisticsWeighingDifferenceWorkInboxSourceV1(f.Rows, f.Fence);
        var read = () => source.ReadDecisionAsync(f.Context(), F.Id(21), default);
        if (fault == "foreign") Assert.Null(await read()); else await Assert.ThrowsAsync<InvalidOperationException>(read);
    }

    private static F DecisionFixture()
    {
        var f = new F(); f.Instruction(F.Id(1)); Note(f, F.Id(1), F.Id(11), "IRS", 42); Record(f, F.Id(1), F.Id(11), F.Id(21), 2, 1);
        f.Rows.WeighingDifferenceRows[0].RecordedAtUtc = F.T0.ToUniversalTime();
        EnteredBy(f, F.Id(21), F.Maker, "SENTETIK-weighing-enterer");
        f.Rows.WeighingDifferenceProductRows.Add(new() { CompanyId = F.CompanyA, RecordId = F.Id(21), Ordinal = 1,
            ProductId = F.Id(31), NoteKg = 100, ScaleKg = 110, DifferenceKg = 10, RemainingPhysicalKg = 0,
            PhysicallyCoveredKg = 0, ExcessKg = 10, TargetLineNo = 1 });
        return f;
    }
}
