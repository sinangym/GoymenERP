using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceOfficialInvoiceImportPendingApprovalReadServiceV1Tests
{
    [Fact]
    public async Task Decision_read_includes_terminal_immutable_preview_without_widening_the_pending_port()
    {
        var company = CompanyContext(); await using var db = Database(company); await UploadAsync(db, company);
        var reader = Reader(db, company, "checker", new Authority(Enumerable.Repeat(PermissionDecision.Allowed, 20).ToArray()));
        var pending = await reader.GetDecisionAsync(Context("checker"), Batch, default);
        Assert.Equal(ReadOutcome.Found, pending.Outcome); Assert.Equal("1", pending.Resource!.Batch.Version);
        var owner = new FinanceOfficialInvoiceImportApprovalServiceV1(db, company, new User("checker", Company),
            new FinanceOfficialInvoiceImportParserV1(), new Authority(Enumerable.Repeat(PermissionDecision.Allowed, 8).ToArray()),
            new ReadAuthority(), new NullAuditCollector(), new(new("upload", [], "unit"), new("approve", [], "unit")), TimeProvider.System);
        Assert.Equal(CommandOutcome.Succeeded, (await owner.ApproveCoreAsync(Context("checker"), new("approve-key"), new(Batch, "1"), default)).Outcome);
        var terminal = await reader.GetDecisionAsync(Context("checker"), Batch, default);
        Assert.Equal(ReadOutcome.Found, terminal.Outcome); Assert.Equal("2", terminal.Resource!.Batch.Version);
        Assert.Equal(FinanceOfficialInvoiceImportApprovalStatusV1.Approved, terminal.Resource.Batch.Status);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(pending.Resource.Preview, FinanceJson.CreateOptions()),
            System.Text.Json.JsonSerializer.Serialize(terminal.Resource.Preview, FinanceJson.CreateOptions()));
        Assert.Equal(ReadOutcome.NotFound, (await reader.GetAsync(Context("checker"), Batch, default)).Outcome);
        Assert.Empty((await reader.ListAsync(Context("checker"), null, default)).Resource!.Items);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Every_decision_read_fence_rechecks_authority(int deniedAt)
    {
        var company = CompanyContext(); await using var db = Database(company); await UploadAsync(db, company);
        var decisions = Enumerable.Range(0, 3).Select(index => index == deniedAt ? PermissionDecision.Denied : PermissionDecision.Allowed).ToArray();
        var result = await Reader(db, company, "checker", new Authority(decisions)).GetDecisionAsync(Context("checker"), Batch, default);
        Assert.Equal(ReadOutcome.Denied, result.Outcome); Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Maker_and_foreign_company_decision_reads_do_not_release_preview_rows()
    {
        var company = CompanyContext(); await using var db = Database(company); await UploadAsync(db, company);
        var maker = await Reader(db, company, "maker", new Authority(PermissionDecision.Allowed, PermissionDecision.Allowed))
            .GetDecisionAsync(Context("maker"), Batch, default);
        Assert.Equal(ReadOutcome.Denied, maker.Outcome); Assert.Null(maker.Resource);
        var foreign = await Reader(db, company, "checker", new Authority(PermissionDecision.Allowed))
            .GetDecisionAsync(Context("checker") with { CompanyId = Guid.NewGuid() }, Batch, default);
        Assert.Equal(ReadOutcome.Denied, foreign.Outcome); Assert.Null(foreign.Resource);
    }

    [Fact]
    public async Task Approval_appearing_during_projection_is_unavailable_instead_of_a_mixed_pending_snapshot()
    {
        var company = CompanyContext(); await using var db = Database(company); await UploadAsync(db, company);
        var calls = 0;
        var reader = Reader(db, company, "checker", new DuringRead(async () =>
        {
            if (++calls != 2) return;
            // A native append-only approval is inserted at the controlled read boundary, never a mutable row rewrite.
            var row = await db.OfficialInvoiceImportBatches.AsNoTracking().SingleAsync();
            db.OfficialInvoiceImportApprovals.Add(new() { Id = Guid.NewGuid(), CompanyId = Company, BatchId = Batch,
                UploadedBy = row.UploadedBy, ApprovedBy = "another-checker", ApprovedAtUtc = DateTimeOffset.UtcNow,
                CorrelationId = "SENTETIK-concurrent-read" });
            await db.SaveChangesAsync();
        }));
        var result = await reader.GetDecisionAsync(Context("checker"), Batch, default);
        Assert.Equal(ReadOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
    }

    private sealed class DuringRead(Func<Task> callback) : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceOfficialInvoiceImportAuthorityActionV1 a, Guid id, CancellationToken t)
        { await callback(); return PermissionDecision.Allowed; }
    }
}
