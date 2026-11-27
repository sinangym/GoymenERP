using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class CashExchangeBookValueProofTests
{
    [Theory] [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)] [InlineData(1, true)]
    public async Task Decision_source_reuses_the_queue_formatter_and_original_trace_for_pending_terminal_and_legacy(int status, bool legacy)
    {
        var row = Proposal(legacy); var transfer = Source() with { DocumentNumber = new("SENTETIK-CEX-1", "1") };
        row.TransferSourceJson = System.Text.Json.JsonSerializer.Serialize(transfer, Json); row.TransferSourceHash = CashExchangeBookValueServiceV1.Hash(row.TransferSourceJson);
        var decision = status == 1 ? null : Decision(row); if (decision is not null) { decision.Approved = status == 2; decision.Reason = status == 3 ? "SENTETIK ret" : null; }
        var detail = BookSourceDetail(transfer, CashExchangeBookValueServiceV1.View(row, decision)); var owner = new BookDecisionOwner(detail);
        var company = new CompanyContext(); company.Set(Company); var user = new BookDecisionUser("SENTETIK-accountant-checker"); var authority = new BookDecisionAuthority();
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("book-purpose-" + Guid.NewGuid()).Options, company);
        var source = new FinanceCashExchangeBookValueWorkInboxSourceV1(db, new(company, user, authority), owner);
        var context = new WorkInboxSourceContextV1(Company, user.UserId, "SENTETIK-source-trace", "SENTETIK-source-cause");
        var snapshot = await source.ReadDecisionAsync(context, row.Id, default); Assert.NotNull(snapshot);
        var queued = FinanceCashExchangeBookValueWorkInboxSourceV1.Item(row, new Dictionary<Guid, string?> { [row.TransferId] = "SENTETIK-CEX-1" }); Assert.NotNull(queued);
        Assert.Equal(queued.DocumentLabel, snapshot.Item.DocumentLabel); Assert.Equal(queued.Rules, snapshot.Item.Rules); Assert.Equal(queued.References, snapshot.Item.References);
        Assert.Equal("SENTETIK-CEX-1", snapshot.Item.DocumentLabel); Assert.Equal($"/onaylarim/finance.cash-exchange-book-value.v1/{row.Id:D}", snapshot.Item.Link);
        Assert.Equal("/finans/doviz-defter-degeri-onaylari", FinanceCashExchangeBookValueWorkInboxSourceV1.ScreenPath);
        Assert.Equal(status == 1, snapshot.Pending); Assert.Equal(status == 1 ? "1" : "2", snapshot.SourceVersion);
        Assert.True(CashExchangeBookValueServiceV1.MatchesDecisionItem(snapshot.Item, detail)); var forwarded = Assert.IsType<FinanceContext>(owner.Context);
        Assert.Equal(context.CorrelationId, forwarded.CorrelationId); Assert.Equal(context.CausationId, forwarded.CausationId); Assert.Equal(0, owner.Decisions); Assert.False(db.ChangeTracker.HasChanges());
        Assert.All(authority.Permissions, permission => Assert.Equal(CashExchangeBookValuePermissionsV1.Approve, permission));
    }

    [Theory] [InlineData("company")] [InlineData("missing-proof")] [InlineData("facts")] [InlineData("transfer")] [InlineData("evidence")]
    public async Task Decision_source_refuses_misbound_or_unproved_owner_facts(string fault)
    {
        var row = Proposal(); var detail = BookSourceDetail(Source(), CashExchangeBookValueServiceV1.View(row, null));
        detail = fault switch { "company" => detail with { Proposal = detail.Proposal with { CompanyId = Guid.NewGuid() } },
            "missing-proof" => detail with { DecisionProof = null }, "facts" => detail with { DecisionProof = detail.DecisionProof! with { ProposalFactsDigest = "wrong" } },
            "transfer" => detail with { Transfer = detail.Transfer with { RecordedBy = "other" } },
            _ => detail with { Proposal = detail.Proposal with { Evidence = detail.Proposal.Evidence with { DocumentId = Guid.NewGuid() } } } };
        var company = new CompanyContext(); company.Set(Company); var user = new BookDecisionUser("SENTETIK-accountant-checker");
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("book-source-refusal-" + Guid.NewGuid()).Options, company);
        var owner = new BookDecisionOwner(detail); var source = new FinanceCashExchangeBookValueWorkInboxSourceV1(db, new(company, user, new BookDecisionAuthority()), owner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(new(Company, user.UserId, "SENTETIK", null), row.Id, default)); Assert.Equal(0, owner.Decisions);
    }

    [Fact]
    public async Task Valid_Found_maker_is_denied_and_an_absent_owner_never_falls_back_to_general_reads()
    {
        var row = Proposal(); var company = new CompanyContext(); company.Set(Company); var user = new BookDecisionUser(row.PreparedBy);
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("book-source-maker-" + Guid.NewGuid()).Options, company);
        var fence = new FinanceWorkInboxFence(company, user, new BookDecisionAuthority()); var owner = new BookDecisionOwner(BookSourceDetail(Source(), CashExchangeBookValueServiceV1.View(row, null)));
        var context = new WorkInboxSourceContextV1(Company, user.UserId, "SENTETIK", null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new FinanceCashExchangeBookValueWorkInboxSourceV1(db, fence, owner).ReadDecisionAsync(context, row.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FinanceCashExchangeBookValueWorkInboxSourceV1(db, fence).ReadDecisionAsync(context, row.Id, default)); Assert.Equal(0, owner.Decisions);
    }

    [Fact]
    public async Task Decision_source_foreign_context_and_scoped_grant_are_refused_before_the_owner_is_read()
    {
        var row = Proposal(); var company = new CompanyContext(); company.Set(Company); var user = new BookDecisionUser("SENTETIK-accountant-checker");
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("book-source-scope-" + Guid.NewGuid()).Options, company);
        var owner = new BookDecisionOwner(BookSourceDetail(Source(), CashExchangeBookValueServiceV1.View(row, null)));
        var source = new FinanceCashExchangeBookValueWorkInboxSourceV1(db, new(company, user, new BookDecisionAuthority()), owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(new(Guid.NewGuid(), user.UserId, "SENTETIK", null), row.Id, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(new(Company, user.UserId, "SENTETIK", null,
            new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())), row.Id, default));
        Assert.Null(owner.Context); Assert.Equal(0, owner.Decisions);
    }

    [Fact]
    public void Native_receipt_carrier_is_private_and_preserves_the_thirteen_positional_fields()
    {
        var f = new BookReceiptFixture(true); var receipt = f.Restore(); Assert.NotNull(receipt); Assert.Equal(f.Transfer, receipt.TransferSource);
        Assert.Equal(f.Proposal.TransferSourceHash, CashExchangeBookValueServiceV1.Hash(System.Text.Json.JsonSerializer.Serialize(receipt.TransferSource, Json)));
        var (company, proposal, transfer, command, key, payload, actor, expected, version, correlation, at, facts, original) = receipt;
        Assert.Equal((Company, f.Proposal.Id, f.Proposal.TransferId, "1", "2"), (company, proposal, transfer, expected, version));
        Assert.Equal((CashExchangeBookValueServiceV1.DecideCommand, f.Command.KeyHash, f.Command.PayloadHash, f.Context.ActorId, f.Context.CorrelationId,
            f.Decision.DecidedAtUtc, receipt.ProposalFactsDigest, receipt.OriginalReceiptDigest), (command, key, payload, actor, correlation, at, facts, original));
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(receipt, Json));
        Assert.Equal(13, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("transferSource", out _));
    }

    private static CashExchangeBookValueDecisionDetailV1 BookSourceDetail(CashTransferView transfer, CashExchangeBookValueViewV1 proposal)
    {
        var detail = new CashExchangeBookValueDecisionDetailV1(transfer, proposal);
        return detail with { DecisionProof = new(Company, proposal.Id, proposal.TransferId, proposal.PreparedBy, proposal.PreparedAtUtc, proposal.Status.ToString(), proposal.Version,
            proposal.TransferSourceHash, CashExchangeBookValueServiceV1.ProposalFactsDigest(proposal), CashExchangeBookValueServiceV1.DecisionPreviewDigest(detail)) };
    }
    private sealed class BookDecisionUser(string actor) : ICurrentUser
    { public bool IsAuthenticated => true; public string UserId => actor; public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id); }
    private sealed class BookDecisionAuthority : IErpAuthorityRevalidationService
    {
        public readonly List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken cancellationToken = default)
        { Permissions.Add(permission); return Task.FromResult(new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class BookDecisionOwner(CashExchangeBookValueDecisionDetailV1 detail) : ICashExchangeBookValueServiceV1, ICashExchangeBookValueDecisionReadV1
    {
        public FinanceContext? Context; public int Decisions;
        public async Task<ReadResult<CashExchangeBookValueDecisionDetailV1>> ReadDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t)
        { Context = c; await fence(t); await fence(t); return new(ReadOutcome.Found, detail); }
        public Task<CashExchangeBookValueCommandResultV1> DecideAsync(FinanceContext c, CommandMetadata m, DecideCashExchangeBookValueV1 r, CancellationToken t) { Decisions++; throw new NotSupportedException(); }
        public Task<ReadResult<CashExchangeBookValueCandidatePageV1>> ListAsync(FinanceContext c, Guid? id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashExchangeBookValueWorkspaceV1>> GetAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<CashExchangeBookValueCommandResultV1> ProposeAsync(FinanceContext c, CommandMetadata m, ProposeCashExchangeBookValueV1 r, CancellationToken t) => throw new NotSupportedException();
    }
}
