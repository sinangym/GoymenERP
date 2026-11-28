using System.Text.Json;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Organization.UnitTests;

public sealed partial class CompanyTaxIdApprovalSafetyTests
{
    // InMemory proves only the read/binder protocol. Commands below are explicitly synthetic, never SQL acceptance.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Purpose_reads_exact_pending_or_terminal_identity_without_general_organization_read(bool terminal)
    {
        await using var run = await PurposeRun.Start(terminal);
        var answer = await run.Reader.ReadDecisionAsync(RequestId, _ => Task.CompletedTask);
        Assert.Equal(OrganizationCompanyTaxIdDecisionReadStatusV1.Found, answer.Status);
        var detail = Assert.IsType<OrganizationCompanyTaxIdDecisionDetailV1>(answer.Detail);
        Assert.Equal(RequestId, detail.Request.Id); Assert.Equal(terminal ? "approved" : "pending", detail.Request.Status);
        Assert.Equal(1, detail.CompanyVersionAtRequest); Assert.Equal(terminal ? 2 : 1, detail.CompanyVersion);
        Assert.Equal(!terminal, detail.IsCurrent); Assert.Equal(64, detail.SourceFactsDigest.Length);
        Assert.Equal(0, run.Audit.Writes); Assert.Equal(0, run.Audit.Discards); Assert.Empty(run.Db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("permission")] [InlineData("actor")] [InlineData("company")] [InlineData("membership")] [InlineData("maker")]
    public async Task Purpose_current_authority_and_maker_refusals_do_not_write_or_clear_caller_audit(string fault)
    {
        await using var run = await PurposeRun.Start(false); run.Audit.Enqueue(Draft());
        if (fault == "maker") run.User.Actor = "SENTETIK-maker";
        var calls = 0;
        var answer = await run.Reader.ReadDecisionAsync(RequestId, _ =>
        {
            calls++;
            if (fault == "permission") run.Authority.Granted = false;
            if (fault == "actor") run.User.Actor = "SENTETIK-other";
            if (fault == "company") run.Company.Set(Guid.NewGuid());
            if (fault == "membership") run.User.Member = false;
            return Task.CompletedTask;
        });
        Assert.Equal(OrganizationCompanyTaxIdDecisionReadStatusV1.Denied, answer.Status); Assert.Null(answer.Detail);
        Assert.True(calls > 0); Assert.Single(run.Audit.Drafts); Assert.Equal(0, run.Audit.Discards); Assert.Equal(0, run.Audit.Writes);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Purpose_completed_consumer_cancellation_never_releases_a_found_answer(int boundary)
    {
        await using var run = await PurposeRun.Start(false); using var cancel = new CancellationTokenSource(); var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.Reader.ReadDecisionAsync(RequestId, _ =>
        { if (++calls == boundary) cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Equal(boundary, calls); Assert.Equal(0, run.Audit.Discards); Assert.Empty(run.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Last_completed_consumer_revocation_is_reread_and_unknown_request_is_distinct()
    {
        await using var run = await PurposeRun.Start(false); var calls = 0;
        var denied = await run.Reader.ReadDecisionAsync(RequestId, _ =>
        { if (++calls == 3) run.Authority.Granted = false; return Task.CompletedTask; });
        Assert.Equal(OrganizationCompanyTaxIdDecisionReadStatusV1.Denied, denied.Status); Assert.Null(denied.Detail);
        run.Authority.Granted = true;
        Assert.Equal(OrganizationCompanyTaxIdDecisionReadStatusV1.NotFound,
            (await run.Reader.ReadDecisionAsync(Guid.NewGuid(), _ => Task.CompletedTask)).Status);
    }

    [Theory]
    [InlineData("none")] [InlineData("key")] [InlineData("actor")] [InlineData("version")] [InlineData("decision")]
    [InlineData("note")] [InlineData("correlation")] [InlineData("unknown-root")] [InlineData("private-root")]
    [InlineData("wrong-response")] [InlineData("foreign-company")]
    [InlineData("schema")]
    public async Task Private_receipt_requires_exact_native_command_response_and_retained_decided_row(string fault)
    {
        await using var run = await PurposeRun.Start(true, fault);
        var original = new CommandExecutionResult(CommandResponses.For(CommandResult.Succeeded), true,
            fault == "correlation" ? "wrong" : "SENTETIK-original");
        var receipt = await run.Reader.ReadDecisionReceiptAsync(RequestId,
            new(fault == "version" ? 2 : 1, fault == "decision" ? "reject" : "approve", fault == "note" ? "other" : null),
            fault == "key" ? "other-key" : "SENTETIK-proof-key", original, _ => Task.CompletedTask);
        if (fault != "none") Assert.Null(receipt);
        else
        {
            Assert.NotNull(receipt); Assert.Equal(1, receipt.ExpectedCompanyVersion); Assert.Equal(2, receipt.RequestVersion);
            Assert.Equal("SENTETIK-checker", receipt.DecidedBy); Assert.Equal("SENTETIK-original", receipt.OriginalCorrelationId);
            Assert.Equal((await run.Reader.ReadDecisionAsync(RequestId, _ => Task.CompletedTask)).Detail!.SourceFactsDigest, receipt.SourceFactsDigest);
        }
        Assert.Equal(0, run.Audit.Writes); Assert.Equal(0, run.Audit.Discards); Assert.Empty(run.Db.ChangeTracker.Entries());
    }

    private sealed class PurposeRun : IAsyncDisposable
    {
        internal readonly MutableCompany Company = new(); internal readonly User User = new(); internal readonly Authority Authority = new();
        internal readonly TestAudit Audit = new(); internal OrganizationDbContext Db { get; }
        internal IOrganizationCompanyTaxIdDecisionReadV1 Reader { get; }
        private PurposeRun(OrganizationDbContext db, MutableCompany company)
        {
            Db = db; Company = company;
            Reader = Assert.IsAssignableFrom<IOrganizationCompanyTaxIdDecisionReadV1>(new OrganizationMasterDataService(Db, User,
                Company, Authority, Authority, new Eligibility(), Audit, NullLogger<OrganizationMasterDataService>.Instance).WithCompanyTaxIdApprovalSafety(Audit));
        }
        internal static async Task<PurposeRun> Start(bool terminal, string fault = "none")
        {
            var companyContext = new MutableCompany(); companyContext.Set(CompanyId); var bootstrap = new OrganizationBootstrapWriteScope();
            var db = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("SENTETIK-tax-purpose-" + Guid.NewGuid()).Options, companyContext, bootstrap);
            var company = new Company(CompanyId, "SENTETIK", "Sentetik şirket", "9000000017", false, false);
            var row = new CompanyTaxIdChangeRequest(RequestId, CompanyId, company.Vkn, "9000000033", 1, "SENTETIK reason", "SENTETIK-maker", DateTimeOffset.UtcNow);
            if (terminal) row.Approve(company, "SENTETIK-checker", null, DateTimeOffset.UtcNow);
            using (bootstrap.BeginBootstrapWrite())
            {
                db.Companies.Add(company); db.CompanyTaxIdChangeRequests.Add(row);
                if (terminal)
                {
                    var response = JsonSerializer.Serialize(CommandResponses.For(CommandResult.Succeeded));
                    if (fault == "unknown-root") response = response[..^1] + ",\"unexpected\":true}";
                    if (fault == "private-root") response = response[..^1] + ",\"approvalReceipt\":{}}";
                    if (fault == "wrong-response") response = JsonSerializer.Serialize(CommandResponses.For(CommandResult.NotFound));
                    db.CommandExecutions.Add(CommandExecution.Complete(new(Guid.NewGuid(), fault == "foreign-company" ? Guid.NewGuid() : CompanyId,
                        OrganizationMasterDataService.DecideCompanyTaxIdChangeCommandName, IdempotencyHash.ComputeKey("SENTETIK-proof-key"),
                        IdempotencyHash.ComputePayload(new { CompanyId, RequestId, Decision = "approve", ExpectedVersion = 1L, Note = (string?)null }),
                        fault == "actor" ? "SENTETIK-other" : "SENTETIK-checker", CommandResult.Succeeded.ToString(), 204,
                        response, "SENTETIK-original", DateTimeOffset.UtcNow, fault == "schema" ? 2 : 1)));
                }
                await db.SaveChangesAsync();
            }
            db.ChangeTracker.Clear(); return new(db, companyContext);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
