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
/// WP5 (YOL-D41; SAT-D304): the opening-cost Work inbox source lists the context company's undecided submissions, oldest
/// first, the submitter as the owner's maker, without the unit cost; the link opens the inbox's own detail (no owner screen).
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryOpeningCostWorkInboxSourceTests
{
    private const string Kind = "inventory.opening-cost.v1";

    [Fact]
    public void The_source_declares_its_kind_the_approve_permission_and_the_opening_cost_registration_adds_it_once()
    {
        var fixture = new F();
        var source = new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        Assert.Equal(("inventory", Kind, "inventory.opening-cost.approve", false),
            (source.Module, source.Kind, source.ApproverPermission, ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.Equal(InventoryPermissionManifestV1.OpeningCostApprove, source.ApproverPermission);
        var services = new ServiceCollection().AddInventoryTransfers().AddInventoryOpeningCosts();
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(InventoryOpeningCostWorkInboxSourceV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(InventoryWorkInboxFence));
    }

    [Fact]
    public async Task Undecided_submissions_are_listed_oldest_first_and_decided_or_foreign_ones_are_not()
    {
        var fixture = new F();
        var newer = Submission(fixture, F.Id(1), minutes: 30, recordedBy: F.Requester);
        var older = Submission(fixture, F.Id(2), minutes: 5, recordedBy: F.Approver);
        var decided = Submission(fixture, F.Id(3), minutes: 1);
        fixture.Rows.OpeningCostDecisionRows.Add(new OpeningCostDecisionRow
        {
            CompanyId = F.CompanyA, OpeningCostId = decided.Id, Decision = 1, Reason = "SENTETIK", CostedStockKg = 10m,
            PayloadHash = "SENTETIK", DecidedAtUtc = F.T0, DecidedBy = F.Approver, CorrelationId = "SENTETIK",
        });
        Submission(fixture, F.Id(4), minutes: 0, companyId: F.CompanyB);
        var source = new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, Kind);
        Assert.Equal([older.Id, newer.Id], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(F.Approver, first.PreparedBy);
        Assert.Null(first.ExcludedDeciders);
        Assert.True(first.Excludes(F.Approver));
        Assert.Equal("/onaylarim/inventory.opening-cost.v1/" + older.Id.ToString("D"), first.Link);
        Assert.True(NativeApprovalQueueRulesV1.IsValidLink(first.Link));
        Assert.Equal(F.T0.AddMinutes(5).ToUniversalTime(), first.WaitingSinceUtc);
        Assert.Equal([new WorkApprovalRuleResultV1(InventoryOpeningCostWorkInboxSourceV1.ApprovalRequiredRule, null, null, null, null)],
            first.Rules);
        Assert.Equal(["location", "product"], first.References.Select(reference => reference.Role));
        Assert.DoesNotContain(first.References, reference => reference.Label.Contains("12.5", StringComparison.Ordinal));
        Assert.Equal(older.LocationId, first.Scope!.LocationId);
    }

    [Fact]
    public async Task Without_the_company_wide_approve_grant_the_source_is_denied_and_reads_nothing()
    {
        var fixture = new F();
        Submission(fixture, F.Id(1), minutes: 1);
        fixture.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.SiteOnly;
        var source = new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Equal(0, fixture.Rows.Reads);
    }

    [Fact]
    public async Task A_submission_without_its_submitter_or_a_failed_read_makes_the_answer_unavailable()
    {
        var fixture = new F();
        Submission(fixture, F.Id(1), minutes: 1);
        Submission(fixture, F.Id(2), minutes: 2, recordedBy: " ");
        var source = new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable,
            (await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None)).Outcome);

        fixture.Rows.OpeningCostRows.RemoveAt(1);
        fixture.Rows.FailReads = true;
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable,
            (await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task More_than_the_page_says_has_more()
    {
        var fixture = new F();
        Submission(fixture, F.Id(1), minutes: 1);
        Submission(fixture, F.Id(2), minutes: 2);
        Submission(fixture, F.Id(3), minutes: 3);
        var source = new InventoryOpeningCostWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 2, CancellationToken.None);

        Assert.True(result.HasMore);
        Assert.Equal([F.Id(1), F.Id(2)], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task The_query_translates_for_SQL_Server_with_the_company_filter()
    {
        using var rows = new InventorySqlTranslatingWorkInboxRows(F.CompanyA);
        var fixture = new F();
        var source = new InventoryOpeningCostWorkInboxSourceV1(rows, fixture.Fence);

        await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        var sql = Assert.Single(rows.Sql);
        Assert.Contains("[OpeningCosts]", sql, StringComparison.Ordinal);
        Assert.Contains("[OpeningCostDecisions]", sql, StringComparison.Ordinal);
        Assert.Contains("[CompanyId]", sql, StringComparison.Ordinal);
    }

    private static OpeningCostRow Submission(F fixture, Guid id, int minutes, string recordedBy = F.Requester, Guid? companyId = null)
    {
        var row = new OpeningCostRow
        {
            Id = id,
            CompanyId = companyId ?? F.CompanyA,
            LocationId = F.DestinationLocation,
            ProductId = Guid.Parse("55555555-5555-4555-8555-555555555555"),
            UnitCostPerKg = 12.5m,
            Reason = "SENTETIK açılış maliyeti",
            PayloadHash = "SENTETIK",
            RecordedAtUtc = F.T0.AddMinutes(minutes),
            RecordedBy = recordedBy,
            CorrelationId = "SENTETIK",
        };
        fixture.Rows.OpeningCostRows.Add(row);
        return row;
    }
}
