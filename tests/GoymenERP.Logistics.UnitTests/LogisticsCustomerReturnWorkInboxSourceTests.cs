using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using Microsoft.Extensions.DependencyInjection;
using F = GoymenERP.Logistics.UnitTests.LogisticsWorkInboxTestFixture;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// WP6 (AI-D31, AI-D32, DALGA_PLANI_v2 1.4): the physical customer return Work inbox source lists only Pending returns of the
/// context company, oldest request first, the requester's own included (Work hides the owner's maker); its rule numbers are the
/// stored returned and delivered quantities in the line's unit. SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsCustomerReturnWorkInboxSourceTests
{
    [Fact]
    public void The_source_declares_its_kind_the_decision_permission_and_the_module_registers_it()
    {
        var fixture = new F();
        var source = new LogisticsCustomerReturnWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        Assert.Equal(("logistics", "logistics.customer-return.v1", "logistics.customer-returns.decision", false),
            (source.Module, source.Kind, source.ApproverPermission, ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.Equal(LogisticsPermissionManifestV1.CustomerReturnsDecision, source.ApproverPermission);
        Assert.True(ErpPermissions.IsKnown(source.ApproverPermission));
        var services = new ServiceCollection().AddLogisticsModule(new LogisticsWorkInboxTestSettings());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(LogisticsCustomerReturnWorkInboxSourceV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task Only_pending_returns_are_listed_oldest_first_with_the_instruction_link_and_the_requester_as_maker()
    {
        var fixture = new F();
        Return(fixture, F.Id(31), F.Id(1), status: 1, minutes: 30);
        Return(fixture, F.Id(32), F.Id(2), status: 1, minutes: 10);
        Return(fixture, F.Id(33), F.Id(1), status: 2, minutes: 1);
        Return(fixture, F.Id(34), F.Id(1), status: 3, minutes: 2);
        var source = new LogisticsCustomerReturnWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, "logistics", "logistics.customer-return.v1");
        Assert.Equal([F.Id(32), F.Id(31)], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(("/lojistik/yukleme-talimatlari/" + F.Id(2).ToString("D"), F.Id(32).ToString("D"),
                F.T0.AddMinutes(10).ToUniversalTime(), F.Maker),
            (first.Link, first.DocumentLabel, first.WaitingSinceUtc, first.PreparedBy));
        Assert.Null(first.ExcludedDeciders);
        Assert.Equal(["loading-instruction"], first.References.Select(reference => reference.Role));
        Assert.Equal(F.Id(2), first.References[0].RecordId);
    }

    [Fact]
    public async Task The_rule_numbers_are_the_stored_delivered_and_returned_quantity_in_the_lines_unit()
    {
        var fixture = new F();
        Return(fixture, F.Id(31), F.Id(1), status: 1, minutes: 1, unit: 1, quantity: 120.5m, delivered: 1000m);
        Return(fixture, F.Id(32), F.Id(1), status: 1, minutes: 2, unit: 2, quantity: 3m, delivered: 12m);
        var source = new LogisticsCustomerReturnWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal([new WorkApprovalRuleResultV1("logistics.customer-return-request", "1000", "120.5", null, "kg")],
            result.Items![0].Rules);
        Assert.Equal([new WorkApprovalRuleResultV1("logistics.customer-return-request", "12", "3", null, "adet")],
            result.Items![1].Rules);
    }

    [Fact]
    public async Task Another_companys_return_is_never_listed()
    {
        var fixture = new F();
        Return(fixture, F.Id(31), F.Id(1), status: 1, minutes: 5);
        Return(fixture, F.Id(32), F.Id(2), status: 1, minutes: 1, companyId: F.CompanyB);
        var source = new LogisticsCustomerReturnWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal([F.Id(31)], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_the_company_wide_decision_permission_the_answer_is_denied_before_any_read()
    {
        var fixture = new F();
        Return(fixture, F.Id(31), F.Id(1), status: 1, minutes: 5);
        var source = new LogisticsCustomerReturnWorkInboxSourceV1(fixture.Rows, fixture.Fence);
        fixture.Authority.Answer = (_, _) => PermissionScope.Denied;
        var denied = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Authority.Answer = (_, _) => LogisticsWorkInboxTestAuthority.SiteOnly;
        var siteOnly = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Authority.Answer = (_, _) => LogisticsWorkInboxTestAuthority.CompanyWide;
        var otherUser = await source.ListPendingAsync(fixture.Context(userId: "SENTETIK-other"), 10, CancellationToken.None);

        Assert.All(new[] { denied, siteOnly, otherUser }, result => Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome));
        Assert.Equal(0, fixture.Rows.Reads);
        Assert.All(fixture.Authority.Calls, call => Assert.Equal(("logistics.customer-returns.decision", F.CompanyA), call));
    }

    [Fact]
    public async Task A_failed_read_is_unavailable_and_a_record_breaking_the_owners_invariants_is_unavailable_not_partial()
    {
        var fixture = new F();
        Return(fixture, F.Id(31), F.Id(1), status: 1, minutes: 5);
        Return(fixture, F.Id(32), F.Id(1), status: 1, minutes: 6, unit: 9);
        var source = new LogisticsCustomerReturnWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var broken = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Rows.FailReads = true;
        var failed = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal((NativeApprovalQueueOutcomeV1.Unavailable, NativeApprovalQueueOutcomeV1.Unavailable),
            (broken.Outcome, failed.Outcome));
    }

    [Fact]
    public async Task More_returns_than_the_maximum_answer_the_oldest_and_has_more()
    {
        var fixture = new F();
        for (var n = 0; n < 3; n++)
        {
            Return(fixture, F.Id(40 + n), F.Id(1), status: 1, minutes: 10 - n);
        }

        var source = new LogisticsCustomerReturnWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 2, CancellationToken.None);

        Assert.Equal([F.Id(42), F.Id(41)], result.Items!.Select(item => item.ItemId));
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task The_query_translates_for_sql_server_with_the_explicit_company_filter()
    {
        var fixture = new F();
        using var rows = new LogisticsSqlTranslatingWorkInboxRows(F.CompanyA);
        var source = new LogisticsCustomerReturnWorkInboxSourceV1(rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        var sql = Assert.Single(rows.Sql);
        Assert.Contains("[CustomerReturns]", sql, StringComparison.Ordinal);
        Assert.Contains("[CompanyId] = @", sql, StringComparison.Ordinal);
    }

    private static void Return(F fixture, Guid returnId, Guid instructionId, int status, int minutes, int unit = 1,
        decimal quantity = 10m, decimal delivered = 100m, Guid? companyId = null) =>
        fixture.Rows.CustomerReturnRows.Add(new CustomerReturnRow
        {
            CompanyId = companyId ?? F.CompanyA, CustomerReturnId = returnId, LoadingInstructionId = instructionId,
            LoadingInstructionLineId = F.Id(500), SourceDeliveryId = F.Id(501), SourceShipmentId = F.Id(502),
            SourceOrderId = F.Id(503), SourceOrderLineId = F.Id(504), UnitId = F.Id(505), QuantityUnit = unit, Quantity = quantity,
            SourceDeliveredQuantity = delivered, Reason = "SENTETIK", RequestedBy = F.Maker, OccurredAtUtc = F.T0,
            RequestedAtUtc = F.T0.AddMinutes(minutes), Status = status, Version = status == 1 ? 1 : 2,
            DecidedBy = status == 1 ? null : F.Approver, DecidedAtUtc = status == 1 ? null : F.T0.AddHours(1),
        });
}
