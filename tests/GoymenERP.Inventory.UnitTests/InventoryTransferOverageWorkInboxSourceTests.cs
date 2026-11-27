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
/// WP6 (AI-D31, AI-D32; KLN-D24, KLN-D107, KLN-D119): the transfer excess Work inbox source lists the context company's receipt
/// excesses without a decision (request 1) and its corrected re-requests without a decision, oldest first, with the owner's
/// "may not decide" set (receiver; re-requester) and the stored kilograms/pieces as rule numbers. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventoryTransferOverageWorkInboxSourceTests
{
    private const string Kind = "inventory.transfer-overage.v1";

    [Fact]
    public void The_source_declares_its_kind_the_overage_approve_permission_and_the_transfer_registration_adds_it()
    {
        var fixture = new F();
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        Assert.Equal(("inventory", Kind, "inventory.stock-transfer.overage-approve", false),
            (source.Module, source.Kind, source.ApproverPermission, ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.Equal(InventoryPermissionManifestV1.StockTransferOverageApprove, source.ApproverPermission);
        Assert.Equal(ErpPermissions.InventoryStockTransferOverageApprove, source.ApproverPermission);
        var services = new ServiceCollection().AddInventoryTransfers();
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(InventoryTransferOverageWorkInboxSourceV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(InventoryWorkInboxFence));
    }

    [Fact]
    public async Task Undecided_receipt_excesses_are_listed_oldest_first_and_decided_or_excess_free_lines_are_not()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        fixture.TransferLine(F.Id(1), F.Id(12), F.Id(22), 2);
        fixture.TransferLine(F.Id(1), F.Id(13), F.Id(23), 3);
        fixture.TransferLine(F.Id(2), F.Id(14), F.Id(24), 4);
        Receipt(fixture, F.Id(1), F.Id(11), overageKg: 5m, minutes: 30);
        Receipt(fixture, F.Id(1), F.Id(12), overageKg: 0m, minutes: 1);
        Receipt(fixture, F.Id(1), F.Id(13), overageKg: 2m, minutes: 2);
        Decision(fixture, F.Id(1), F.Id(13), requestNumber: 1);
        Receipt(fixture, F.Id(2), F.Id(14), overageKg: 1.5m, minutes: 10);
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, Kind);
        Assert.Equal(["ETK-4", "ETK-1"], result.Items!.Select(item => item.DocumentLabel));
        var first = result.Items![0];
        Assert.Equal((InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(2), F.Id(14)), F.T0.AddMinutes(10).ToUniversalTime(),
                "/onaylarim/inventory.transfer-overage.v1/" + InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(2), F.Id(14)).ToString("D"), F.Receiver),
            (first.ItemId, first.WaitingSinceUtc, first.Link, first.PreparedBy));
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal(["inventory-label", "inventory-transfer"], first.References.Select(reference => reference.Role));
        Assert.Equal(("ETK-4", F.Id(24), "/stok/etiket/" + F.Id(24).ToString("D")),
            (first.References[0].Label, first.References[0].RecordId, first.References[0].Link));
        Assert.Equal(F.Id(2), first.References[1].RecordId);
    }

    [Fact]
    public async Task The_rule_numbers_are_the_stored_dispatched_received_and_excess_kilograms_and_pieces()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        fixture.TransferLine(F.Id(1), F.Id(12), F.Id(22), 2);
        Receipt(fixture, F.Id(1), F.Id(11), overageKg: 5.25m, minutes: 1, dispatchedKg: 100m, receivedKg: 105.25m);
        Receipt(fixture, F.Id(1), F.Id(12), overageKg: 4m, minutes: 2, dispatchedKg: 40m, receivedKg: 44m,
            pieces: (10, 11, 1));
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal([new WorkApprovalRuleResultV1("inventory.transfer-overage-received", "100", "105.25", "5.25", "kg")],
            result.Items![0].Rules);
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1("inventory.transfer-overage-received", "40", "44", "4", "kg"),
                new WorkApprovalRuleResultV1("inventory.transfer-overage-pieces", "10", "11", "1", "adet"),
            ],
            result.Items![1].Rules);
    }

    [Fact]
    public async Task A_corrected_rerequest_waits_since_it_was_recorded_and_excludes_both_the_rerequester_and_the_receiver()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        fixture.TransferLine(F.Id(1), F.Id(12), F.Id(22), 2);
        Receipt(fixture, F.Id(1), F.Id(11), overageKg: 6m, minutes: 1);
        Decision(fixture, F.Id(1), F.Id(11), requestNumber: 1, decision: 2);
        Renewal(fixture, F.Id(1), F.Id(11), requestNumber: 2, overageKg: 4.5m, minutes: 50, pieces: 2);
        Receipt(fixture, F.Id(1), F.Id(12), overageKg: 1m, minutes: 2);
        Decision(fixture, F.Id(1), F.Id(12), requestNumber: 1, decision: 2);
        Renewal(fixture, F.Id(1), F.Id(12), requestNumber: 2, overageKg: 1m, minutes: 3);
        Decision(fixture, F.Id(1), F.Id(12), requestNumber: 2, decision: 1);
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, Kind);
        var item = Assert.Single(result.Items!);
        Assert.Equal((F.T0.AddMinutes(50).ToUniversalTime(), F.Requester), (item.WaitingSinceUtc, item.PreparedBy));
        Assert.Equal([F.Receiver], item.ExcludedDeciders);
        Assert.True(item.Excludes(F.Receiver) && item.Excludes(F.Requester) && !item.Excludes(F.Approver));
        Assert.Equal(
            [
                new WorkApprovalRuleResultV1("inventory.transfer-overage-renewal", "6", "4.5", null, "kg"),
                new WorkApprovalRuleResultV1("inventory.transfer-overage-pieces", null, null, "2", "adet"),
            ],
            item.Rules);
    }

    [Fact]
    public async Task A_rerequest_by_the_receiver_names_one_maker_and_no_extra_exclusion()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        Receipt(fixture, F.Id(1), F.Id(11), overageKg: 6m, minutes: 1);
        Decision(fixture, F.Id(1), F.Id(11), requestNumber: 1, decision: 2);
        Renewal(fixture, F.Id(1), F.Id(11), requestNumber: 2, overageKg: 3m, minutes: 5, by: F.Receiver);
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var item = Assert.Single((await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None)).Items!);

        Assert.Equal((F.Receiver, (IReadOnlyList<string>?)null), (item.PreparedBy, item.ExcludedDeciders));
    }

    [Fact]
    public async Task Another_companys_excess_is_never_listed()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        fixture.TransferLine(F.Id(2), F.Id(12), F.Id(22), 2, F.CompanyB);
        Receipt(fixture, F.Id(1), F.Id(11), overageKg: 1m, minutes: 5);
        Receipt(fixture, F.Id(2), F.Id(12), overageKg: 1m, minutes: 1, companyId: F.CompanyB);
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal(["ETK-1"], result.Items!.Select(item => item.DocumentLabel));
    }

    [Fact]
    public async Task Without_the_company_wide_permission_or_with_another_context_the_answer_is_denied_before_any_read()
    {
        var fixture = new F();
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);
        fixture.Authority.Answer = (_, _) => PermissionScope.Denied;
        var denied = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.SiteOnly;
        var siteOnly = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Authority.Answer = (_, _) => InventoryWorkInboxTestAuthority.CompanyWide;
        var otherCompany = await source.ListPendingAsync(fixture.Context(companyId: F.CompanyB), 10, CancellationToken.None);
        fixture.User.IsAuthenticated = false;
        var anonymous = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.All(new[] { denied, siteOnly, otherCompany, anonymous },
            result => Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome));
        Assert.Equal(0, fixture.Rows.Reads);
    }

    [Fact]
    public async Task A_failed_read_or_a_lost_grant_after_the_read_answers_without_items()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        Receipt(fixture, F.Id(1), F.Id(11), overageKg: 1m, minutes: 5);
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);
        fixture.Rows.FailReads = true;
        var failed = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Rows.FailReads = false;
        var calls = 0;
        fixture.Authority.Answer = (_, _) => ++calls == 1 ? InventoryWorkInboxTestAuthority.CompanyWide : PermissionScope.Denied;
        var revoked = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal((NativeApprovalQueueOutcomeV1.Unavailable, NativeApprovalQueueOutcomeV1.Denied), (failed.Outcome, revoked.Outcome));
        Assert.Null(revoked.Items);
    }

    [Fact]
    public async Task A_record_breaking_the_owners_invariants_is_unavailable_never_a_partial_list()
    {
        var fixture = new F();
        fixture.TransferLine(F.Id(1), F.Id(11), F.Id(21), 1);
        fixture.TransferLine(F.Id(1), F.Id(12), F.Id(22), 2);
        Receipt(fixture, F.Id(1), F.Id(11), overageKg: 1m, minutes: 5);
        Receipt(fixture, F.Id(1), F.Id(12), overageKg: 6m, minutes: 6);
        Decision(fixture, F.Id(1), F.Id(12), requestNumber: 1, decision: 2);
        // KLN-D107: a re-request never exceeds the receipt's first declared excess.
        Renewal(fixture, F.Id(1), F.Id(12), requestNumber: 2, overageKg: 7m, minutes: 7);
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task More_excesses_than_the_maximum_answer_the_oldest_across_both_request_kinds_and_has_more()
    {
        var fixture = new F();
        for (var n = 0; n < 3; n++)
        {
            fixture.TransferLine(F.Id(1), F.Id(60 + n), F.Id(70 + n), 60 + n);
            Receipt(fixture, F.Id(1), F.Id(60 + n), overageKg: 1m, minutes: 10 + n);
        }

        fixture.TransferLine(F.Id(1), F.Id(63), F.Id(73), 63);
        Receipt(fixture, F.Id(1), F.Id(63), overageKg: 2m, minutes: 0);
        Decision(fixture, F.Id(1), F.Id(63), requestNumber: 1, decision: 2);
        Renewal(fixture, F.Id(1), F.Id(63), requestNumber: 2, overageKg: 1m, minutes: 5);
        var source = new InventoryTransferOverageWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 2, CancellationToken.None);

        Assert.Equal(["ETK-63", "ETK-60"], result.Items!.Select(item => item.DocumentLabel));
        Assert.True(result.HasMore);
    }

    [Fact]
    public void The_item_identity_is_stable_per_kind_and_line_and_never_the_line_id()
    {
        var overage = InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11));

        Assert.Equal(overage, InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(1), F.Id(11)));
        Assert.NotEqual(overage, InventoryWorkInboxFence.TransferLineItemId("inventory.transfer-loss.v1", F.Id(1), F.Id(11)));
        Assert.NotEqual(overage, InventoryWorkInboxFence.TransferLineItemId(Kind, F.Id(2), F.Id(11)));
        Assert.NotEqual(F.Id(11), overage);
        Assert.Equal(8, overage.Version);
    }

    [Fact]
    public async Task Both_queries_translate_for_sql_server_with_the_explicit_company_filter()
    {
        var fixture = new F();
        using var rows = new InventorySqlTranslatingWorkInboxRows(F.CompanyA);
        var source = new InventoryTransferOverageWorkInboxSourceV1(rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal(2, rows.Sql.Count);
        Assert.Contains("[TransferReceipts]", rows.Sql[0], StringComparison.Ordinal);
        Assert.Contains("[TransferOverageRenewals]", rows.Sql[1], StringComparison.Ordinal);
        Assert.All(rows.Sql, sql => Assert.Contains("[TransferOverageDecisions]", sql, StringComparison.Ordinal));
    }

    private static void Receipt(F fixture, Guid transferId, Guid lineId, decimal overageKg, int minutes,
        decimal dispatchedKg = 100m, decimal? receivedKg = null, (int Dispatched, int Received, int Overage)? pieces = null,
        Guid? companyId = null) =>
        fixture.Rows.ReceiptRows.Add(new InventoryTransferReceiptRow
        {
            CompanyId = companyId ?? F.CompanyA, TransferId = transferId, LineId = lineId, DispatchedKg = dispatchedKg,
            ReceivedKg = receivedKg ?? dispatchedKg + overageKg, OverageKg = overageKg,
            DispatchedPieceCount = pieces?.Dispatched, ReceivedPieceCount = pieces?.Received, OveragePieceCount = pieces?.Overage,
            LabelVersionAfterReceipt = 3, ReceivedAtUtc = F.T0, PayloadHash = "SENTETIK", RecordedAtUtc = F.T0.AddMinutes(minutes),
            RecordedBy = F.Receiver, CorrelationId = "SENTETIK",
        });

    private static void Decision(F fixture, Guid transferId, Guid lineId, int requestNumber, int decision = 1) =>
        fixture.Rows.OverageDecisionRows.Add(new InventoryTransferOverageDecisionRow
        {
            CompanyId = F.CompanyA, TransferId = transferId, LineId = lineId, RequestNumber = requestNumber, Decision = decision,
            Reason = "SENTETIK", DecidedAtUtc = F.T0, PayloadHash = "SENTETIK", RecordedAtUtc = F.T0, DecidedBy = F.Approver,
            CorrelationId = "SENTETIK",
        });

    private static void Renewal(F fixture, Guid transferId, Guid lineId, int requestNumber, decimal overageKg, int minutes,
        int? pieces = null, string by = F.Requester) =>
        fixture.Rows.OverageRenewalRows.Add(new InventoryTransferOverageRenewalRow
        {
            CompanyId = F.CompanyA, TransferId = transferId, LineId = lineId, RequestNumber = requestNumber, OverageKg = overageKg,
            OveragePieceCount = pieces, Reason = "SENTETIK", RequestedAtUtc = F.T0, PayloadHash = "SENTETIK",
            RecordedAtUtc = F.T0.AddMinutes(minutes), RecordedBy = by, CorrelationId = "SENTETIK",
        });
}
