using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using F = GoymenERP.Logistics.UnitTests.LogisticsWorkInboxTestFixture;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsDispatchNoteWorkInboxDecisionSourceTests
{
    [Theory]
    [InlineData(1)] [InlineData(500)] [InlineData(501)]
    public async Task Human_proof_reads_every_native_line_without_reference_bound_truncation_or_prices(int count)
    {
        var f = Fixture(count); var source = new LogisticsDispatchNoteWorkInboxSourceV1(f.Rows, f.Fence);
        var snapshot = await source.ReadDecisionAsync(f.Context(), F.Id(11), default);
        Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal("3", snapshot.SourceVersion);
        var proof = Assert.IsType<WorkLogisticsDispatchNoteProofV1>(snapshot.LogisticsDispatchNoteProof);
        Assert.Equal(count, proof.Note.Lines.Count); Assert.Single(snapshot.Item.References); Assert.Single(snapshot.Item.Rules);
        Assert.Equal(F.Id(10000 + count), proof.Note.Lines[^1].ShipmentId);
        Assert.Equal(("1.250", "0.750"), (proof.Note.Lines[0].Quantity, proof.Note.Lines[0].QuantityKg));
        Assert.Equal("/lojistik/yukleme-talimatlari/" + F.Id(1), snapshot.Item.Link); // Staged: native routing is unchanged.
        var wire = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("logisticsDispatchNote", wire); Assert.DoesNotContain("quantity", wire);
        Assert.DoesNotContain("price", wire, StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData(2)] [InlineData(3)]
    public async Task Immutable_maker_and_lines_remain_readable_after_native_approval_or_cancellation(int status)
    {
        var f = Fixture(2); var note = f.Rows.DispatchNoteRows[0]; note.Status = status; note.SeriesCode = "IRS"; note.SequenceNo = long.MaxValue;
        f.Rows.InstructionRows[0].Version = long.MaxValue;
        var snapshot = await new LogisticsDispatchNoteWorkInboxSourceV1(f.Rows, f.Fence).ReadDecisionAsync(f.Context(), F.Id(11), default);
        Assert.NotNull(snapshot); Assert.False(snapshot.Pending); Assert.Equal(long.MaxValue.ToString(), snapshot.SourceVersion);
        Assert.Equal(F.Maker, snapshot.Item.PreparedBy); Assert.Equal(2, snapshot.LogisticsDispatchNoteProof!.Note.Lines.Count);
        Assert.Equal("IRS-9223372036854775807", snapshot.LogisticsDispatchNoteProof.Note.Number);
    }
    [Fact]
    public async Task Foreign_company_or_unknown_item_returns_no_record_without_cross_company_proof()
    {
        var f = Fixture(1); f.Rows.DispatchNoteRows[0].CompanyId = F.CompanyB;
        var source = new LogisticsDispatchNoteWorkInboxSourceV1(f.Rows, f.Fence);
        Assert.Null(await source.ReadDecisionAsync(f.Context(), F.Id(11), default));
        Assert.Null(await source.ReadDecisionAsync(f.Context(), Guid.NewGuid(), default));
    }
    [Fact]
    public async Task Revoked_permission_denies_before_read_and_changed_version_during_full_line_read_is_unavailable()
    {
        var f = Fixture(1); var source = new LogisticsDispatchNoteWorkInboxSourceV1(f.Rows, f.Fence);
        f.Authority.Answer = (_, _) => GoymenERP.BuildingBlocks.Security.PermissionScope.Denied;
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(f.Context(), F.Id(11), default));
        Assert.Equal(0, f.Rows.Reads);
        f.Authority.Answer = (_, _) => LogisticsWorkInboxTestAuthority.CompanyWide;
        f.Rows.AfterRead = read => { if (read == 2) f.Rows.InstructionRows[0].Version++; };
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(f.Context(), F.Id(11), default));
    }
    [Theory]
    [InlineData("empty")] [InlineData("duplicate")] [InlineData("foreign-instruction")] [InlineData("missing-unit")]
    public async Task Incomplete_or_foreign_line_set_never_becomes_partial_human_proof(string malformed)
    {
        var f = Fixture(2);
        if (malformed == "empty") f.Rows.DispatchNoteLineRows.Clear();
        if (malformed == "duplicate") f.Rows.DispatchNoteLineRows[1].ShipmentId = f.Rows.DispatchNoteLineRows[0].ShipmentId;
        if (malformed == "foreign-instruction") f.Rows.DispatchNoteLineRows[0].InstructionId = F.Id(999);
        if (malformed == "missing-unit") f.Rows.DispatchNoteLineRows[0].UnitId = Guid.Empty;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LogisticsDispatchNoteWorkInboxSourceV1(f.Rows, f.Fence).ReadDecisionAsync(f.Context(), F.Id(11), default));
    }
    private static F Fixture(int count)
    {
        var f = new F(); f.Instruction(F.Id(1));
        f.Rows.DispatchNoteRows.Add(new() { CompanyId = F.CompanyA, InstructionId = F.Id(1), DispatchNoteId = F.Id(11),
            Status = 1, CreatedBy = F.Maker, CreatedAtUtc = F.T0, NoteNo = 1, SharesBefore = 1, CreatedStep = 1 });
        for (var i = 1; i <= count; i++) f.Rows.DispatchNoteLineRows.Add(new DispatchNoteLineRow
        {
            CompanyId = F.CompanyA, InstructionId = F.Id(1), DispatchNoteId = F.Id(11), LineNo = i, ShipmentId = F.Id(10000 + i),
            InstructionLineId = F.Id(2), SourceOrderId = F.Id(3), SourceOrderLineId = F.Id(4), ProductId = F.Id(5), UnitId = F.Id(6),
            Quantity = 1.250m, QuantityKg = i == 1 ? 0.750m : null,
        });
        return f;
    }
}
