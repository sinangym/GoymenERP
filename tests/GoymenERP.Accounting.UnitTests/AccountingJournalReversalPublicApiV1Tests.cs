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

    [Theory]
    [InlineData("0001-01-01", "2026-09-26", null, "transactionDate")]
    [InlineData("2026-09-25", "0001-01-01", null, "accountingDate")]
    [InlineData("2026-09-25", "2026-09-26", "0001-01-01", "valueDate")]
    public async Task Minimum_date_is_the_routes_declared_400_before_the_lifecycle(
        string transactionDate,
        string accountingDate,
        string? valueDate,
        string field)
    {
        // The policy refuses DateOnly default/minimum with its internal code; the public adapter answers it with the
        // route's declared accounting_request_invalid instead (Org review 5326853074 P2-2).
        var lifecycle = new CapturingLifecycle();
        var service = new AccountingJournalReversalPublicServiceV1(lifecycle);

        var result = await service.ProposeAsync(
            Request(transactionDate, valueDate) with { AccountingDate = accountingDate }, "proposal-key");

        Assert.Equal(AccountingApiStatus.Invalid, result.Status);
        Assert.Equal(AccountingApiCodes.RequestInvalid, result.Code);
        Assert.Equal(field, result.Field);
        Assert.Null(lifecycle.Proposal);
    }

    [Fact]
    public async Task Over_long_reason_is_the_routes_declared_400_and_the_exact_bound_passes()
    {
        var lifecycle = new CapturingLifecycle();
        var service = new AccountingJournalReversalPublicServiceV1(lifecycle);
        var bound = AccountingJournalReversalPolicyV1.ReasonMaximumLength;

        var tooLong = await service.ProposeAsync(
            Request() with { Reason = new string('r', bound + 1) }, "proposal-key-long");
        Assert.Equal(AccountingApiStatus.Invalid, tooLong.Status);
        Assert.Equal(AccountingApiCodes.RequestInvalid, tooLong.Code);
        Assert.Equal("reason", tooLong.Field);
        Assert.Null(lifecycle.Proposal);

        // Surrounding whitespace is not counted, exactly as the policy trims before measuring.
        var exact = await service.ProposeAsync(
            Request() with { Reason = "  " + new string('r', bound) + "  " }, "proposal-key-exact");
        Assert.Equal(AccountingApiStatus.Created, exact.Status);
        Assert.NotNull(lifecycle.Proposal);
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

    [Fact]
    public async Task Non_positive_approval_version_is_refused_with_the_expected_version_field()
    {
        var lifecycle = new CapturingLifecycle();
        var service = new AccountingJournalReversalPublicServiceV1(lifecycle);

        var result = await service.ApproveAsync(ProposalId,
            new ApproveAccountingJournalReversalRequestV1(0), "approval-key");

        Assert.Equal(AccountingApiStatus.Invalid, result.Status);
        Assert.Equal("expectedVersion", result.Field);
        Assert.Null(lifecycle.ApprovalProposalId);
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
