using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.WorkInbox;
using Microsoft.Extensions.DependencyInjection;
using F = GoymenERP.Inventory.UnitTests.InventoryWorkInboxTestFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// WP6 (YOL-D41; SAT-D304, SAT-D372): the opening cost Work inbox source lists the context company's undecided submissions,
/// oldest first, the submitter as the owner's maker, the stored unit cost as the rule number, and links to the Work inbox
/// filtered to its kind (no owner screen; the inbox decision WP8 covers it). SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryOpeningCostWorkInboxSourceTests
{
    private const string Kind = "inventory.opening-cost.v1";

    [Fact]
    public void The_source_declares_its_kind_the_opening_cost_approve_permission_and_the_registration_adds_it()
    {
        var fixture = new F();
        var source = new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        Assert.Equal(("inventory", Kind, "inventory.opening-cost.approve", false),
            (source.Module, source.Kind, source.ApproverPermission, ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.Equal(InventoryPermissionManifestV1.OpeningCostApprove, source.ApproverPermission);
        Assert.True(ErpPermissions.IsKnown(source.ApproverPermission));
        Assert.Contains(new ServiceCollection().AddInventoryTransfers(), descriptor =>
            descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(InventoryOpeningCostWorkInboxSourceV1));
    }

    [Fact]
    public async Task Undecided_submissions_are_listed_oldest_first_with_the_submitter_as_maker_and_the_inbox_link()
    {
        var fixture = new F();
        Submission(fixture, F.Id(1), minutes: 30, unitCost: 41.2575m);
        Submission(fixture, F.Id(2), minutes: 10);
        Submission(fixture, F.Id(3), minutes: 1);
        fixture.Rows.OpeningCostDecisionRows.Add(new OpeningCostDecisionRow
        {
            CompanyId = F.CompanyA, OpeningCostId = F.Id(3), Decision = 2, Reason = "SENTETIK", PayloadHash = "SENTETIK",
            DecidedAtUtc = F.T0, DecidedBy = F.Approver, CorrelationId = "SENTETIK",
        });
        Submission(fixture, F.Id(4), minutes: 0, companyId: F.CompanyB);
        var source = new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, Kind);
        Assert.Equal([F.Id(2), F.Id(1)], result.Items!.Select(item => item.ItemId));
        var later = result.Items![1];
        Assert.Equal(("/onaylarim/kuyruk?tur=inventory.opening-cost.v1", F.Requester, F.Id(1).ToString("D"),
                F.T0.AddMinutes(30).ToUniversalTime(), (IReadOnlyList<string>?)null),
            (later.Link, later.PreparedBy, later.DocumentLabel, later.WaitingSinceUtc, later.ExcludedDeciders));
        Assert.Equal([new WorkApprovalRuleResultV1("inventory.opening-cost-submitted", null, "41.2575", null, null)], later.Rules);
        Assert.Equal(["product", "location"], later.References.Select(reference => reference.Role));
        Assert.All(later.References, reference => Assert.Null(reference.Link));
    }

    [Fact]
    public async Task Without_the_company_wide_permission_it_is_denied_before_any_read_and_a_broken_record_is_unavailable()
    {
        var fixture = new F();
        Submission(fixture, F.Id(1), minutes: 1, unitCost: 0m);
        var source = new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence);
        fixture.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.SiteOnly;
        var siteOnly = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        var reads = fixture.Rows.Reads;
        fixture.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.CompanyWide;
        var broken = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal((NativeApprovalQueueOutcomeV1.Denied, 0, NativeApprovalQueueOutcomeV1.Unavailable),
            (siteOnly.Outcome, reads, broken.Outcome));
    }

    [Fact]
    public async Task More_submissions_than_the_maximum_answer_the_oldest_and_has_more_and_the_query_translates()
    {
        var fixture = new F();
        for (var n = 0; n < 3; n++)
        {
            Submission(fixture, F.Id(10 + n), minutes: 10 - n);
        }

        var result = await new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence)
            .ListPendingAsync(fixture.Context(), 2, CancellationToken.None);
        using var rows = new InventorySqlTranslatingWorkInboxRows(F.CompanyA);
        var translated = await new InventoryOpeningCostWorkInboxSourceV1(rows, fixture.Fence)
            .ListPendingAsync(fixture.Context(), 2, CancellationToken.None);

        Assert.Equal([F.Id(12), F.Id(11)], result.Items!.Select(item => item.ItemId));
        Assert.True(result.HasMore);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, translated.Outcome);
        Assert.Contains("[OpeningCostDecisions]", Assert.Single(rows.Sql), StringComparison.Ordinal);
    }

    private static void Submission(F fixture, Guid id, int minutes, decimal unitCost = 10m, Guid? companyId = null) =>
        fixture.Rows.OpeningCostRows.Add(new OpeningCostRow
        {
            Id = id, CompanyId = companyId ?? F.CompanyA, LocationId = F.DestinationLocation, ProductId = F.Id(9001),
            UnitCostPerKg = unitCost, Reason = "SENTETIK", PayloadHash = "SENTETIK", RecordedAtUtc = F.T0.AddMinutes(minutes),
            RecordedBy = F.Requester, CorrelationId = "SENTETIK",
        });
}
