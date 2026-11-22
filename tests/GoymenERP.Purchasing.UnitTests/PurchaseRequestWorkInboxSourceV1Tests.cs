using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.WorkInbox;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// WP7 (AI-D31/AI-D32, ALS-D2, D4): the purchase request Work inbox source lists every Submitted request of the company,
/// oldest submission first, measured with <c>purchasing.requests.approve</c>; the preparer is the maker and the submitter is
/// in the owner's "may not decide" set. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseRequestWorkInboxSourceV1Tests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 7, 0, 0, TimeSpan.Zero);
    private const string Submitter = "SENTETIK-submitter";

    [Fact]
    public async Task Only_submitted_requests_of_the_caller_company_are_listed_oldest_submission_first()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var older = Request(CompanyA, PurchaseRequestStatus.Submitted, Start);
        var newer = Request(CompanyA, PurchaseRequestStatus.Submitted, Start.AddHours(2));
        await world.SeedAsync(CompanyA, db => db.Requests.AddRange(newer, older,
            Request(CompanyA, PurchaseRequestStatus.Draft, Start.AddDays(-1)),
            Approved(Request(CompanyA, PurchaseRequestStatus.Approved, Start.AddDays(-2)))));
        await world.SeedAsync(CompanyB, db => db.Requests.Add(Request(CompanyB, PurchaseRequestStatus.Submitted, Start.AddDays(-3))));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.False(result.HasMore);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        AssertContractShape("purchasing", first);
        Assert.Equal("purchasing.request.v1", first.Kind);
        Assert.Equal("/satinalma/talepler/" + older.Id.ToString("D"), first.Link);
        Assert.Equal(Start, first.WaitingSinceUtc);
        Assert.Equal(PurchasingWorkInboxFence.ShortReference(older.Id), first.DocumentLabel);
        Assert.Equal([new WorkApprovalRuleResultV1(PurchaseRequestWorkInboxSourceV1.ApprovalRequiredRule, null, null, null, null)],
            first.Rules);
        Assert.Equal(PurchasingPermissionManifestV1.RequestsApprove, world.LastPermission);
    }

    [Fact]
    public async Task The_preparer_is_the_maker_and_the_submitter_may_not_decide_either()
    {
        var world = new PurchasingWorkInboxTestWorld { UserId = Submitter };
        var request = Request(CompanyA, PurchaseRequestStatus.Submitted, Start);
        await world.SeedAsync(CompanyA, db => db.Requests.Add(request));

        var item = Assert.Single((await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None)).Items!);

        Assert.Equal(Maker, item.PreparedBy);
        Assert.Equal([Submitter], item.ExcludedDeciders);
        Assert.True(item.Excludes(Maker));
        Assert.True(item.Excludes(Submitter));
        Assert.False(item.Excludes(Approver));
    }

    [Fact]
    public async Task A_submitted_request_without_a_recorded_submitter_makes_the_read_unavailable()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var request = Request(CompanyA, PurchaseRequestStatus.Submitted, Start);
        request.SubmittedBy = null;
        await world.SeedAsync(CompanyA, db => db.Requests.Add(request));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
    }

    [Fact]
    public async Task More_submitted_requests_than_requested_set_has_more()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var rows = Enumerable.Range(0, 3).Select(index => Request(CompanyA, PurchaseRequestStatus.Submitted, Start.AddMinutes(index))).ToArray();
        await world.SeedAsync(CompanyA, db => db.Requests.AddRange(rows));

        var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal([rows[0].Id, rows[1].Id], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_the_request_approval_permission_or_with_a_scoped_grant_the_source_is_denied()
    {
        var denied = new PurchasingWorkInboxTestWorld { Granted = false };
        var scoped = new PurchasingWorkInboxTestWorld { CompanyWide = false };
        foreach (var world in new[] { denied, scoped })
        {
            await world.SeedAsync(CompanyA, db => db.Requests.Add(Request(CompanyA, PurchaseRequestStatus.Submitted, Start)));

            var result = await Source(world).ListPendingAsync(world.Caller(CompanyA), 5, CancellationToken.None);

            Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
            Assert.Null(result.Items);
        }
    }

    [Fact]
    public void The_domain_exclusion_set_is_both_makers_and_null_when_either_is_unknown()
    {
        Assert.Equal([Maker, Submitter], PurchaseRequest.DeciderExclusions(Maker, Submitter));
        Assert.Equal([Maker], PurchaseRequest.DeciderExclusions(Maker, Maker));
        Assert.Null(PurchaseRequest.DeciderExclusions(Maker, null));
        Assert.Null(PurchaseRequest.DeciderExclusions(" ", Submitter));
    }

    private static PurchaseRequestWorkInboxSourceV1 Source(PurchasingWorkInboxTestWorld world) =>
        new(world.Context(CompanyA), world.Fence(CompanyA));

    private static PurchaseRequestRow Request(Guid company, PurchaseRequestStatus status, DateTimeOffset changedAt) => new()
    {
        Id = Guid.NewGuid(), CompanyId = company, Status = (int)status, Version = status == PurchaseRequestStatus.Draft ? 1 : 2,
        RowVersion = [1], EffectiveOn = new DateOnly(2026, 11, 20), CreatedAtUtc = changedAt.AddHours(-1), UpdatedAtUtc = changedAt,
        CreatedBy = Maker, CorrelationId = "SENTETIK-correlation",
        SubmittedBy = status == PurchaseRequestStatus.Draft ? null : Submitter,
    };

    private static PurchaseRequestRow Approved(PurchaseRequestRow row)
    {
        row.Version = 3;
        row.ApprovedBy = Approver;
        row.ApprovedAtUtc = row.UpdatedAtUtc;
        return row;
    }
}
