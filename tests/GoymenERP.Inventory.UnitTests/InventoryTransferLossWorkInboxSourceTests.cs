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
/// WP6 (AI-D31, AI-D32; KLN-D23 (c), KLN-D97, KLN-D108): the transit loss Work inbox source lists the context company's loss
/// requests with neither a decision nor a withdrawal, oldest first, the requester as the owner's maker, the dispatch kilograms
/// as the rule number and a renewal marked as such. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventoryTransferLossWorkInboxSourceTests
{
    private const string Kind = "inventory.transfer-loss.v1";

    [Fact]
    public void The_source_declares_its_kind_the_loss_approve_permission_and_the_transfer_registration_adds_it()
    {
        var fixture = new F();
        var source = new InventoryTransferLossWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        Assert.Equal(("inventory", Kind, "inventory.stock-transfer.loss-approve", false),
            (source.Module, source.Kind, source.ApproverPermission, ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.Equal(InventoryPermissionManifestV1.StockTransferLossApprove, source.ApproverPermission);
        Assert.Equal(ErpPermissions.InventoryStockTransferLossApprove, source.ApproverPermission);
        var services = new ServiceCollection().AddInventoryTransfers();
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(InventoryTransferLossWorkInboxSourceV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task Open_requests_are_listed_oldest_first_and_decided_or_withdrawn_requests_are_not()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        fixture.TransferLine(F.Id(1), F.Id(12), F.Id(22), 2);
        fixture.TransferLine(F.Id(1), F.Id(13), F.Id(23), 3);
        fixture.TransferLine(F.Id(2), F.Id(14), F.Id(24), 4);
        Request(fixture, F.Id(1), F.Id(11), 1, minutes: 30);
        Request(fixture, F.Id(1), F.Id(12), 1, minutes: 1);
        fixture.Rows.LossDecisionRows.Add(new InventoryTransferLossDecisionRow
        {
            CompanyId = F.CompanyA, TransferId = F.Id(1), LineId = F.Id(12), RequestNumber = 1, Decision = 1, Reason = "SENTETIK",
            DecidedAtUtc = F.T0, PayloadHash = "SENTETIK", RecordedAtUtc = F.T0, DecidedBy = F.Approver, CorrelationId = "SENTETIK",
        });
        Request(fixture, F.Id(1), F.Id(13), 1, minutes: 2);
        fixture.Rows.LossWithdrawalRows.Add(new InventoryTransferLossWithdrawalRow
        {
            CompanyId = F.CompanyA, TransferId = F.Id(1), LineId = F.Id(13), RequestNumber = 1, Reason = "SENTETIK",
            WithdrawnAtUtc = F.T0, PayloadHash = "SENTETIK", RecordedAtUtc = F.T0, WithdrawnBy = F.Requester,
            CorrelationId = "SENTETIK",
        });
        Request(fixture, F.Id(2), F.Id(14), 1, minutes: 10);
        Dispatch(fixture, F.Id(2), F.Id(14), 98.5m);
        var source = new InventoryTransferLossWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, Kind);
        Assert.Equal(["ETK-4", "ETK-1"], result.Items!.Select(item => item.DocumentLabel));
        var first = result.Items![0];
        Assert.Equal((InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(2), F.Id(14)), F.T0.AddMinutes(10).ToUniversalTime(),
                $"/onaylarim/inventory.transfer-loss.v1/{InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(2), F.Id(14)):D}", F.Requester, (IReadOnlyList<string>?)null),
            (first.ItemId, first.WaitingSinceUtc, first.Link, first.PreparedBy, first.ExcludedDeciders));
        Assert.Equal([new WorkApprovalRuleResultV1("inventory.transfer-loss-request", null, "98.5", null, "kg")], first.Rules);
        Assert.Equal([new WorkApprovalRuleResultV1("inventory.transfer-loss-request", null, null, null, null)],
            result.Items![1].Rules);
        Assert.Equal(["inventory-label", "inventory-transfer"], first.References.Select(reference => reference.Role));
        Assert.Equal("/stok/transferler/kayip-onaylari", Assert.Single(first.References, reference => reference.Role == "inventory-transfer").Link);
    }

    [Fact]
    public async Task A_new_request_after_a_closed_one_waits_anew_and_says_it_is_renewed()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        Request(fixture, F.Id(1), F.Id(11), 1, minutes: 1);
        fixture.Rows.LossDecisionRows.Add(new InventoryTransferLossDecisionRow
        {
            CompanyId = F.CompanyA, TransferId = F.Id(1), LineId = F.Id(11), RequestNumber = 1, Decision = 2, Reason = "SENTETIK",
            DecidedAtUtc = F.T0, PayloadHash = "SENTETIK", RecordedAtUtc = F.T0, DecidedBy = F.Approver, CorrelationId = "SENTETIK",
        });
        Request(fixture, F.Id(1), F.Id(11), 2, minutes: 40, by: "SENTETIK-second-requester");
        Dispatch(fixture, F.Id(1), F.Id(11), 50m);
        var source = new InventoryTransferLossWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var item = Assert.Single((await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None)).Items!);

        Assert.Equal((F.T0.AddMinutes(40).ToUniversalTime(), "SENTETIK-second-requester"), (item.WaitingSinceUtc, item.PreparedBy));
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1("inventory.transfer-loss-request", null, "50", null, "kg"),
                new WorkApprovalRuleResultV1("inventory.transfer-loss-renewed", null, null, null, null),
            ],
            item.Rules);
    }

    [Fact]
    public async Task Another_companys_request_is_never_listed_and_two_open_requests_on_one_line_are_unavailable()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        fixture.TransferLine(F.Id(2), F.Id(12), F.Id(22), 2, F.CompanyB);
        Request(fixture, F.Id(1), F.Id(11), 1, minutes: 5);
        Request(fixture, F.Id(2), F.Id(12), 1, minutes: 1, companyId: F.CompanyB);
        var source = new InventoryTransferLossWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var own = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        Request(fixture, F.Id(1), F.Id(11), 2, minutes: 6);
        var broken = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal(["ETK-1"], own.Items!.Select(item => item.DocumentLabel));
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, broken.Outcome);
    }

    [Fact]
    public async Task Without_the_company_wide_permission_the_answer_is_denied_before_any_read_and_a_failed_read_is_unavailable()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        Request(fixture, F.Id(1), F.Id(11), 1, minutes: 5);
        var source = new InventoryTransferLossWorkInboxSourceV1(fixture.Rows, fixture.Fence);
        fixture.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.SiteOnly;
        var siteOnly = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        var readsAfterRefusal = fixture.Rows.Reads;
        fixture.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.CompanyWide;
        fixture.Rows.FailReads = true;
        var failed = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal((NativeApprovalQueueOutcomeV1.Denied, 0, NativeApprovalQueueOutcomeV1.Unavailable),
            (siteOnly.Outcome, readsAfterRefusal, failed.Outcome));
        Assert.All(fixture.Authority.Calls, call => Assert.Equal(("inventory.stock-transfer.loss-approve", F.CompanyA), call));
    }

    [Fact]
    public async Task More_requests_than_the_maximum_answer_the_oldest_and_has_more()
    {
        var fixture = new F();
        for (var n = 0; n < 3; n++)
        {
            fixture.TransferLine(F.Id(1), F.Id(60 + n), F.Id(70 + n), 60 + n);
            Request(fixture, F.Id(1), F.Id(60 + n), 1, minutes: 10 - n);
        }

        var source = new InventoryTransferLossWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 2, CancellationToken.None);

        Assert.Equal(["ETK-62", "ETK-61"], result.Items!.Select(item => item.DocumentLabel));
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task The_query_translates_for_sql_server_with_the_explicit_company_filter()
    {
        var fixture = new F();
        using var rows = new InventorySqlTranslatingWorkInboxRows(F.CompanyA);
        var source = new InventoryTransferLossWorkInboxSourceV1(rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        var sql = Assert.Single(rows.Sql);
        Assert.Contains("[TransferLossRequests]", sql, StringComparison.Ordinal);
        Assert.Contains("[TransferLossWithdrawals]", sql, StringComparison.Ordinal);
        Assert.Contains("[TransferDispatchLines]", sql, StringComparison.Ordinal);
    }

    private static void Request(F fixture, Guid transferId, Guid lineId, int requestNumber, int minutes,
        string by = F.Requester, Guid? companyId = null) =>
        fixture.Rows.LossRequestRows.Add(new InventoryTransferLossRequestRow
        {
            CompanyId = companyId ?? F.CompanyA, TransferId = transferId, LineId = lineId, RequestNumber = requestNumber,
            Explanation = "SENTETIK", RequestedAtUtc = F.T0, PayloadHash = "SENTETIK", RecordedAtUtc = F.T0.AddMinutes(minutes),
            RecordedBy = by, CorrelationId = "SENTETIK",
        });

    private static void Dispatch(F fixture, Guid transferId, Guid lineId, decimal actualKg) =>
        fixture.Rows.DispatchLineRows.Add(new InventoryTransferDispatchLineRow
        {
            CompanyId = F.CompanyA, TransferId = transferId, LineId = lineId, LabelVersionAfterDispatch = 2, ActualKg = actualKg,
        });
}
