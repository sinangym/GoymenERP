using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Shell;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using Microsoft.Extensions.DependencyInjection;
using F = GoymenERP.Logistics.UnitTests.LogisticsWorkInboxTestFixture;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the over-plan shipment Work inbox source lists only Pending exceptions of Issued
/// instructions of the context company, oldest proposal first, the proposer's own included, with the line's stored instructed
/// quantity and the proposed excess as its rule numbers. SENTETIK / NOT_ENV.
/// </summary>
public sealed class LogisticsPlanExcessWorkInboxSourceTests
{
    [Fact]
    public void The_source_declares_its_kind_the_approve_permission_and_the_module_registers_it()
    {
        var fixture = new F();
        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        // WP1: the duration class is the Work rule table's (YOL-D18 default: hour scale), never the source's declaration.
        Assert.Equal(("logistics", "logistics.plan-excess.v1", "logistics.shipments.plan-excess.approve", false),
            (source.Module, source.Kind, source.ApproverPermission, ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.Equal(LogisticsPermissionManifestV1.ShipmentPlanExcessApprove, source.ApproverPermission);
        var services = new ServiceCollection().AddLogisticsModule(new LogisticsWorkInboxTestSettings());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(LogisticsPlanExcessWorkInboxSourceV1));
    }

    [Fact]
    public async Task Only_pending_exceptions_of_issued_instructions_are_listed_oldest_first_with_the_approval_screen_link()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        fixture.Instruction(F.Id(2), status: 3);
        Line(fixture, F.Id(1), F.Id(31));
        Line(fixture, F.Id(2), F.Id(32));
        Exception(fixture, F.Id(1), F.Id(31), F.Id(41), status: 1, minutes: 20);
        Exception(fixture, F.Id(1), F.Id(31), F.Id(42), status: 1, minutes: 5);
        Exception(fixture, F.Id(1), F.Id(31), F.Id(43), status: 2, minutes: 1);
        Exception(fixture, F.Id(2), F.Id(32), F.Id(44), status: 1, minutes: 0);
        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        F.AssertContractShape(result, "logistics", "logistics.plan-excess.v1");
        Assert.Equal([F.Id(42), F.Id(41)], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        Assert.Equal(("/onaylarim/logistics.plan-excess.v1/" + F.Id(42).ToString("D"), F.Id(42).ToString("D"), F.T0.AddMinutes(5).ToUniversalTime()),
            (first.Link, first.DocumentLabel, first.WaitingSinceUtc));
        Assert.Equal(("loading-instruction", F.Id(1)), (Assert.Single(first.References).Role, first.References[0].RecordId));
        Assert.Equal(LogisticsWorkInboxFence.InstructionLink(F.Id(1)), first.References[0].Link);
    }

    [Fact]
    public async Task The_rule_numbers_are_the_stored_instructed_quantity_the_proposed_excess_and_the_approved_excess_in_kg()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        Line(fixture, F.Id(1), F.Id(31), instructed: 1250.5m, approvedExcess: 40m, kilogram: true, blocked: true);
        Line(fixture, F.Id(1), F.Id(32), instructed: 12m, kilogram: false);
        Exception(fixture, F.Id(1), F.Id(31), F.Id(41), status: 1, minutes: 1, quantity: 75.250m);
        Exception(fixture, F.Id(1), F.Id(32), F.Id(42), status: 1, minutes: 2, quantity: 3m);
        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal(
            [
                new WorkApprovalRuleResultV1("logistics.plan-excess-quantity", "1250.5", null, "75.25", "kg"),
                new WorkApprovalRuleResultV1("logistics.plan-excess-already-approved", null, "40", null, "kg"),
                new WorkApprovalRuleResultV1("logistics.plan-excess-line-blocked", null, null, null, null),
            ],
            result.Items![0].Rules);
        Assert.Equal([new WorkApprovalRuleResultV1("logistics.plan-excess-quantity", "12", null, "3", null)], result.Items![1].Rules);
    }

    [Fact]
    public async Task Another_companys_exception_is_never_listed()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        fixture.Instruction(F.Id(2), F.CompanyB);
        Line(fixture, F.Id(1), F.Id(31));
        Line(fixture, F.Id(2), F.Id(32), companyId: F.CompanyB);
        Exception(fixture, F.Id(1), F.Id(31), F.Id(41), status: 1, minutes: 5);
        Exception(fixture, F.Id(2), F.Id(32), F.Id(42), status: 1, minutes: 1, companyId: F.CompanyB);
        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal([F.Id(41)], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_the_company_wide_approve_permission_the_answer_is_denied_without_a_read()
    {
        var fixture = new F();
        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        fixture.Authority.Answer = (_, _) => PermissionScope.Denied;
        var denied = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Authority.Answer = (_, _) => LogisticsWorkInboxTestAuthority.SiteOnly;
        var siteOnly = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);
        fixture.Authority.Answer = (_, _) => throw new InvalidOperationException("SENTETIK authority failure");
        var failed = await source.ListPendingAsync(fixture.Context(), 10, CancellationToken.None);

        Assert.Equal((NativeApprovalQueueOutcomeV1.Denied, NativeApprovalQueueOutcomeV1.Denied, NativeApprovalQueueOutcomeV1.Unavailable),
            (denied.Outcome, siteOnly.Outcome, failed.Outcome));
        Assert.Equal(0, fixture.Rows.Reads);
    }

    [Fact]
    public async Task The_proposers_own_exception_is_returned_for_the_proposer_since_work_hides_it_itself()
    {
        var fixture = new F();
        fixture.User.UserId = F.Maker;
        fixture.Instruction(F.Id(1));
        Line(fixture, F.Id(1), F.Id(31));
        Exception(fixture, F.Id(1), F.Id(31), F.Id(41), status: 1, minutes: 5);
        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(userId: F.Maker), 10, CancellationToken.None);

        Assert.Equal(F.Maker, Assert.Single(result.Items!).PreparedBy);
    }

    [Fact]
    public async Task More_exceptions_than_the_maximum_answer_the_oldest_and_has_more()
    {
        var fixture = new F();
        fixture.Instruction(F.Id(1));
        Line(fixture, F.Id(1), F.Id(31));
        for (var n = 0; n < 3; n++)
        {
            Exception(fixture, F.Id(1), F.Id(31), F.Id(50 + n), status: 1, minutes: 10 - n);
        }

        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 2, CancellationToken.None);

        Assert.Equal([F.Id(52), F.Id(51)], result.Items!.Select(item => item.ItemId));
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task The_query_translates_for_sql_server_with_the_explicit_company_filter()
    {
        var fixture = new F();
        using var rows = new LogisticsSqlTranslatingWorkInboxRows(F.CompanyA);
        var source = new LogisticsPlanExcessWorkInboxSourceV1(rows, fixture.Fence);

        var result = await source.ListPendingAsync(fixture.Context(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Contains("[PlanExcessExceptions]", Assert.Single(rows.Sql), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task Decision_purpose_keeps_the_exact_terminal_or_blocked_exception_and_both_native_versions_without_browse_permission(int state)
    {
        var fixture = new F(); var instruction = fixture.Instruction(F.Id(1), status: 3); instruction.Version = 99;
        Line(fixture, F.Id(1), F.Id(31), blocked: true, approvedExcess: 20);
        Exception(fixture, F.Id(1), F.Id(31), F.Id(41), state, 5);
        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);
        var snapshot = await source.ReadDecisionAsync(fixture.Context(), F.Id(41), default);
        Assert.NotNull(snapshot); Assert.Equal(state.ToString(), snapshot.SourceVersion); Assert.Equal(state == 1, snapshot.Pending);
        Assert.Equal("99", snapshot.LogisticsPlanExcess!.InstructionVersion);
        var proof = Assert.IsType<WorkLogisticsPlanExcessProofV1>(snapshot.LogisticsPlanExcessProof);
        Assert.Equal((F.CompanyA, F.Id(41), F.Id(1), F.Id(31), "10", "SENTETIK", F.Maker, F.T0.AddMinutes(5)),
            (proof.CompanyId, proof.ExceptionId, proof.InstructionId, proof.LineId, proof.Quantity, proof.Reason, proof.ProposedBy, proof.ProposedAtUtc));
        Assert.Contains(snapshot.Item.Rules, rule => rule.RuleCode == LogisticsPlanExcessWorkInboxSourceV1.LineBlockedRule);
        Assert.Equal(LogisticsPlanExcessWorkInboxSourceV1.DecisionScreenPath + "/" + F.Id(41).ToString("D"), snapshot.Item.Link);
        Assert.All(fixture.Authority.Calls, call => Assert.Equal(LogisticsPlanExcessV1.ApprovePermission, call.Permission));
        Assert.DoesNotContain("LogisticsPlanExcessProof", System.Text.Json.JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task Decision_purpose_requires_every_company_join_and_current_companywide_authority()
    {
        var fixture = new F(); fixture.Instruction(F.Id(1), F.CompanyB);
        Line(fixture, F.Id(1), F.Id(31), companyId: F.CompanyB);
        Exception(fixture, F.Id(1), F.Id(31), F.Id(41), 1, 0);
        var source = new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence);
        Assert.Null(await source.ReadDecisionAsync(fixture.Context(), F.Id(41), default));
        fixture.Rows.InstructionRows[0].CompanyId = F.CompanyA;
        Assert.Null(await source.ReadDecisionAsync(fixture.Context(), F.Id(41), default));
        fixture.Rows.LineRows[0].CompanyId = F.CompanyA;
        Assert.NotNull(await source.ReadDecisionAsync(fixture.Context(), F.Id(41), default));
        fixture.Authority.Answer = (_, _) => LogisticsWorkInboxTestAuthority.SiteOnly;
        var reads = fixture.Rows.Reads; Assert.Null(await source.ReadDecisionAsync(fixture.Context(), F.Id(41), default)); Assert.Equal(reads, fixture.Rows.Reads);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Decision_purpose_detects_a_changed_exception_or_instruction_in_the_final_read(bool exceptionChanged)
    {
        var fixture = new F(); fixture.Instruction(F.Id(1)); Line(fixture, F.Id(1), F.Id(31));
        Exception(fixture, F.Id(1), F.Id(31), F.Id(41), 1, 0);
        fixture.Authority.Answer = (_, _) =>
        {
            if (fixture.Authority.Calls.Count == 2)
            {
                if (exceptionChanged) { fixture.Rows.PlanExcessRows[0].Status = 2; fixture.Rows.PlanExcessRows[0].Version = 2; }
                else fixture.Rows.InstructionRows[0].Version++;
            }
            return LogisticsWorkInboxTestAuthority.CompanyWide;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LogisticsPlanExcessWorkInboxSourceV1(fixture.Rows, fixture.Fence)
            .ReadDecisionAsync(fixture.Context(), F.Id(41), default));
    }

    [Fact]
    public async Task Decision_purpose_query_translates_with_the_exact_exception_and_all_company_joins()
    {
        var fixture = new F(); using var rows = new LogisticsSqlTranslatingWorkInboxRows(F.CompanyA);
        Assert.Null(await new LogisticsPlanExcessWorkInboxSourceV1(rows, fixture.Fence).ReadDecisionAsync(fixture.Context(), F.Id(41), default));
        var sql = Assert.Single(rows.Sql); Assert.Contains("[PlanExcessExceptions]", sql); Assert.Contains("[LoadingInstructions]", sql);
        Assert.Contains("[ExceptionId]", sql); Assert.Contains("[CompanyId]", sql);
    }

    [Fact]
    public void Decision_purpose_has_the_paired_Work_implementation_and_preserves_native_screen_availability_and_permission()
    {
        var provider = new LogisticsShellCapabilityProvider(); Assert.Equal("1.8", provider.ContractVersion);
        var purpose = Assert.Single(provider.GetDeclarations(), value => value.CapabilityId == LogisticsShellCapabilityProvider.PlanExcessWorkDecisionCapabilityId);
        Assert.Equal("/onaylarim/logistics.plan-excess.v1/:approvalId", purpose.RoutePath); Assert.Null(purpose.ParentCapabilityId);
        Assert.True(purpose.ImplementationAvailable); Assert.Equal([LogisticsPlanExcessV1.ApprovePermission], purpose.RequiredPermissions);
        Assert.Equal(GoymenERP.BuildingBlocks.Shell.ShellCapabilityContextRequirement.SingleCompany, purpose.ContextRequirement);
        Assert.Equal(GoymenERP.BuildingBlocks.Shell.ShellCapabilityScopeRequirement.CompanyWide, purpose.ScopeRequirement);
        var native = Assert.Single(provider.GetDeclarations(), value => value.CapabilityId == LogisticsShellCapabilityProvider.PlanExcessApproveCapabilityId);
        Assert.Equal(LogisticsPlanExcessWorkInboxSourceV1.ScreenPath, native.RoutePath); Assert.False(native.ImplementationAvailable);
    }

    private static void Line(F fixture, Guid instructionId, Guid lineId, decimal instructed = 100m, decimal approvedExcess = 0m,
        bool kilogram = true, bool blocked = false, Guid? companyId = null) =>
        fixture.Rows.LineRows.Add(new LoadingInstructionLineRow
        {
            CompanyId = companyId ?? F.CompanyA, InstructionId = instructionId, LineId = lineId, LineNo = 1, SourceOrderId = F.Id(7001),
            SourceOrderLineId = F.Id(7002), ProductId = F.Id(7003), UnitId = F.Id(7004), UnitIsKilogram = kilogram,
            Instructed = instructed, ApprovedPlanExcess = approvedExcess, IsBlocked = blocked,
        });

    private static void Exception(F fixture, Guid instructionId, Guid lineId, Guid exceptionId, int status, int minutes,
        decimal quantity = 10m, Guid? companyId = null) =>
        fixture.Rows.PlanExcessRows.Add(new LogisticsPlanExcessRow
        {
            CompanyId = companyId ?? F.CompanyA, ExceptionId = exceptionId, InstructionId = instructionId, LineId = lineId,
            Quantity = quantity, Status = status, Version = status, Reason = "SENTETIK", ProposedBy = F.Maker,
            ProposedAtUtc = F.T0.AddMinutes(minutes), ApprovedBy = status == 2 ? F.Approver : null,
            ApprovedAtUtc = status == 2 ? F.T0 : null,
        });
}
