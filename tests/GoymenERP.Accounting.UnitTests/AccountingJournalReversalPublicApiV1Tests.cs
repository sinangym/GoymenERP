using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingJournalReversalPublicApiV1Tests
{
    [Fact]
    public async Task Exact_iso_dates_are_mapped_losslessly_to_the_owner_request()
    {
        var lifecycle = new CapturingLifecycle();
        var service = new AccountingJournalReversalPublicServiceV1(lifecycle);
        var request = Request(valueDate: "2026-09-27");

        var result = await service.ProposeAsync(request, "proposal-key");

        Assert.Equal(AccountingApiStatus.Created, result.Status);
        Assert.NotNull(lifecycle.Proposal);
        Assert.Equal(new DateOnly(2026, 9, 25), lifecycle.Proposal.TransactionDate);
        Assert.Equal(new DateOnly(2026, 9, 26), lifecycle.Proposal.AccountingDate);
        Assert.Equal(new DateOnly(2026, 9, 27), lifecycle.Proposal.ValueDate);
        Assert.Equal("proposal-key", lifecycle.IdempotencyKey);
    }

    [Theory]
    [InlineData("25.09.2026", "transactionDate")]
    [InlineData("2026-9-25", "transactionDate")]
    [InlineData("2026-02-30", "transactionDate")]
    public async Task Non_exact_or_impossible_date_is_refused_before_the_lifecycle(
        string transactionDate,
        string field)
    {
        var lifecycle = new CapturingLifecycle();
        var service = new AccountingJournalReversalPublicServiceV1(lifecycle);

        var result = await service.ProposeAsync(Request(transactionDate), "proposal-key");

        Assert.Equal(AccountingApiStatus.Invalid, result.Status);
        Assert.Equal(AccountingApiCodes.RequestInvalid, result.Code);
        Assert.Equal(field, result.Field);
        Assert.Null(lifecycle.Proposal);
    }

    [Fact]
    public async Task Approval_dto_forwards_only_proposal_version_and_idempotency_key()
    {
        var lifecycle = new CapturingLifecycle();
        var service = new AccountingJournalReversalPublicServiceV1(lifecycle);

        var result = await service.ApproveAsync(ProposalId,
            new ApproveAccountingJournalReversalRequestV1(7), "approval-key");

        Assert.Equal(AccountingApiStatus.Ok, result.Status);
        Assert.Equal(ProposalId, lifecycle.ApprovalProposalId);
        Assert.Equal(7, lifecycle.ExpectedVersion);
        Assert.Equal("approval-key", lifecycle.IdempotencyKey);
    }

    private static readonly Guid ProposalId = Guid.Parse("b1000000-0000-4000-8000-000000000001");

    private static ProposeAccountingJournalReversalRequestV1 Request(
        string transactionDate = "2026-09-25",
        string? valueDate = null) => new(
        ProposalId,
        Guid.Parse("b1000000-0000-4000-8000-000000000002"),
        Guid.Parse("b1000000-0000-4000-8000-000000000003"),
        transactionDate,
        "2026-09-26",
        valueDate,
        "Synthetic reversal reason");

    private sealed class CapturingLifecycle : IAccountingJournalReversalLifecycleV1
    {
        public ProposeAccountingJournalReversalV1? Proposal { get; private set; }
        public Guid? ApprovalProposalId { get; private set; }
        public int? ExpectedVersion { get; private set; }
        public string? IdempotencyKey { get; private set; }

        public Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ProposeAsync(
            ProposeAccountingJournalReversalV1? request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Proposal = request;
            IdempotencyKey = idempotencyKey;
            return Task.FromResult(Result(AccountingApiStatus.Created));
        }

        public Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ApproveAsync(
            Guid proposalId,
            int expectedVersion,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            ApprovalProposalId = proposalId;
            ExpectedVersion = expectedVersion;
            IdempotencyKey = idempotencyKey;
            return Task.FromResult(Result(AccountingApiStatus.Ok));
        }

        private static AccountingApiResult<AccountingJournalReversalProposalViewV1> Result(
            AccountingApiStatus status) =>
            new(status, new AccountingJournalReversalProposalViewV1(
                ProposalId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                new DateOnly(2026, 9, 25),
                new DateOnly(2026, 9, 26),
                null,
                "Synthetic reversal reason",
                Guid.NewGuid(),
                DateTimeOffset.Parse("2026-09-25T12:00:00Z"),
                "Pending",
                null,
                null,
                null,
                1), false, null);
    }
}
