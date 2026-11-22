using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Infrastructure;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Infrastructure.WorkInbox;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the manufacturing plan Work inbox source lists the context company's Draft plans (first
/// approval) and its Approved plans released by a cancelled order and not yet re-approved (re-approval), oldest wait first,
/// the preparer's own included, under <c>manufacturing.plans.approve</c> held company-wide. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ManufacturingPlanWorkInboxSourceTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private const string Approver = "33333333-3333-4333-8333-333333333333";
    private const string Maker = "44444444-4444-4444-8444-444444444444";
    private const string Reviser = "55555555-5555-4555-8555-555555555555";
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.FromHours(3));

    private readonly CompanyContext company = new();
    private readonly ManufacturingWorkInboxTestUser user = new(Approver, CompanyA);
    private readonly ManufacturingWorkInboxTestAuthority authority = new();
    private readonly ManufacturingInMemoryWorkInboxRows rows = new();

    public ManufacturingPlanWorkInboxSourceTests() => company.Set(CompanyA);

    [Fact]
    public void The_source_declares_its_kind_and_the_approve_permission_and_the_module_registers_it()
    {
        var source = Source();

        // WP1: the duration class is the Work rule table's (YOL-D18 default: hour scale), never the source's declaration.
        Assert.Equal(("manufacturing", "manufacturing.plan.v1", "manufacturing.plans.approve", false),
            (source.Module, source.Kind, source.ApproverPermission, ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.Equal(ManufacturingPermissionManifestV1.PlansApprove, source.ApproverPermission);
        Assert.Equal(ErpPermissions.ManufacturingPlansApprove, source.ApproverPermission);
        var services = new ServiceCollection().AddManufacturingModule(new ManufacturingWorkInboxTestSettings());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)
            && descriptor.ImplementationType == typeof(ManufacturingPlanWorkInboxSourceV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task Draft_plans_wait_since_creation_or_last_revision_and_other_states_are_not_listed()
    {
        Plan(Id(1), status: 1, minutes: 30);
        Plan(Id(2), status: 1, minutes: 0, version: 2);
        Revision(Id(2), revisionNo: 1, planVersion: 2, minutes: 40);
        Plan(Id(3), status: 2, minutes: 5);
        Plan(Id(4), status: 3, minutes: 6);
        Order(Id(3), Id(90), status: 1);

        var result = await Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        AssertContractShape(result);
        Assert.Equal([Id(1), Id(2)], result.Items!.Select(item => item.ItemId));
        var revised = result.Items![1];
        Assert.Equal((T0.AddMinutes(40).ToUniversalTime(), Reviser, "/uretim/plan/" + Id(2).ToString("D"), Id(2).ToString("D")),
            (revised.WaitingSinceUtc, revised.PreparedBy, revised.Link, revised.DocumentLabel));
        Assert.Equal([new WorkApprovalRuleResultV1("manufacturing.plan-first-approval", null, null, null, null)], revised.Rules);
        Assert.Empty(revised.References);
        Assert.Equal(Maker, result.Items![0].PreparedBy);
    }

    [Fact]
    public async Task A_released_approved_plan_waits_for_reapproval_since_the_order_cancel_until_it_is_reapproved()
    {
        Plan(Id(1), status: 2, minutes: 0, version: 3);
        Order(Id(1), Id(91), status: 3, planVersionAfterCancel: 3, cancelledMinutes: 50);
        Plan(Id(2), status: 2, minutes: 0, version: 4);
        Order(Id(2), Id(92), status: 3, planVersionAfterCancel: 3, cancelledMinutes: 20);
        rows.ReapprovalRows.Add(new PlanReapprovalRow
        {
            CompanyId = CompanyA, PlanId = Id(2), ReapprovalNo = 1, PlanVersion = 4,
            ReapprovedByUserId = Guid.Parse(Approver), ReapprovedAtUtc = T0.AddMinutes(25), CorrelationId = "SENTETIK",
        });
        Plan(Id(3), status: 2, minutes: 0, version: 3);
        Order(Id(3), Id(93), status: 3, planVersionAfterCancel: 3, cancelledMinutes: 10);
        Order(Id(3), Id(94), status: 1);

        var result = await Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        AssertContractShape(result);
        var item = Assert.Single(result.Items!);
        Assert.Equal((Id(1), T0.AddMinutes(50).ToUniversalTime(), Maker), (item.ItemId, item.WaitingSinceUtc, item.PreparedBy));
        Assert.Equal([new WorkApprovalRuleResultV1("manufacturing.plan-reapproval-after-order-cancel", null, null, null, null)], item.Rules);
        var reference = Assert.Single(item.References);
        Assert.Equal(("manufacturing-order", Id(91), "/uretim/emir/" + Id(91).ToString("D")),
            (reference.Role, reference.RecordId, reference.Link));
    }

    [Fact]
    public async Task The_rule_codes_tell_first_approval_from_reapproval_and_name_an_unanswered_rejection_and_a_missing_termin()
    {
        Plan(Id(1), status: 1, minutes: 1, version: 2, dueDate: null);
        rows.RejectionRows.Add(new PlanRejectionRow
        {
            CompanyId = CompanyA, PlanId = Id(1), RejectionNo = 1, Reason = "SENTETIK", PlanVersion = 2,
            RejectedByUserId = Guid.Parse(Approver), RejectedAtUtc = T0.AddMinutes(90), CorrelationId = "SENTETIK",
        });
        Plan(Id(2), status: 2, minutes: 0, version: 3);
        Order(Id(2), Id(92), status: 3, planVersionAfterCancel: 3, cancelledMinutes: 5);

        var result = await Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        Assert.Equal([Id(1), Id(2)], result.Items!.Select(item => item.ItemId));
        Assert.Equal(
            ["manufacturing.plan-first-approval", "manufacturing.plan-rejected-not-revised", "manufacturing.plan-due-date-missing"],
            result.Items![0].Rules.Select(rule => rule.RuleCode));
        Assert.Equal(T0.AddMinutes(1).ToUniversalTime(), result.Items![0].WaitingSinceUtc);
        Assert.All(result.Items!.SelectMany(item => item.Rules), rule =>
            Assert.Equal((null, null, null, null), (rule.Threshold, rule.Actual, rule.Deviation, rule.Unit)));
        Assert.Equal("manufacturing.plan-reapproval-after-order-cancel", Assert.Single(result.Items![1].Rules).RuleCode);
    }

    [Fact]
    public async Task Another_companys_plans_are_never_listed()
    {
        Plan(Id(1), status: 1, minutes: 5);
        Plan(Id(2), status: 1, minutes: 1, companyId: CompanyB);
        Plan(Id(3), status: 2, minutes: 0, version: 3, companyId: CompanyB);
        Order(Id(3), Id(93), status: 3, planVersionAfterCancel: 3, cancelledMinutes: 1, companyId: CompanyB);

        var result = await Source().ListPendingAsync(Context(), 10, CancellationToken.None);

        Assert.Equal([Id(1)], result.Items!.Select(item => item.ItemId));
    }

    [Fact]
    public async Task Without_the_company_wide_approve_permission_or_as_another_user_the_answer_is_denied_without_a_read()
    {
        Plan(Id(1), status: 1, minutes: 5);
        var source = Source();

        authority.Answer = (_, _) => PermissionScope.Denied;
        var denied = await source.ListPendingAsync(Context(), 10, CancellationToken.None);
        authority.Answer = (_, _) => ManufacturingWorkInboxTestAuthority.WorkCenterOnly;
        var scoped = await source.ListPendingAsync(Context(), 10, CancellationToken.None);
        authority.Answer = (_, _) => ManufacturingWorkInboxTestAuthority.CompanyWide;
        var otherUser = await source.ListPendingAsync(new WorkInboxSourceContextV1(CompanyA, Maker, "SENTETIK", null), 10,
            CancellationToken.None);

        Assert.All(new[] { denied, scoped, otherUser }, result =>
            Assert.Equal((NativeApprovalQueueOutcomeV1.Denied, (IReadOnlyList<WorkInboxItemV1>?)null), (result.Outcome, result.Items)));
        Assert.Equal(0, rows.Reads);
        Assert.All(authority.Calls, call => Assert.Equal(ManufacturingPermissionManifestV1.PlansApprove, call.Permission));
    }

    [Fact]
    public async Task The_preparers_own_plan_is_returned_for_the_preparer_and_a_plan_without_a_recorded_preparer_names_no_maker()
    {
        user.UserId = Maker;
        Plan(Id(1), status: 1, minutes: 5);
        Plan(Id(2), status: 1, minutes: 6, createdBy: null);

        var result = await Source().ListPendingAsync(new WorkInboxSourceContextV1(CompanyA, Maker, "SENTETIK", null), 10,
            CancellationToken.None);

        // WP1 D4: the domain refuses no one as the preparer of a plan stored before preparers were kept, so the inbox lists it
        // with no maker (the domain's own definition) instead of hiding it; the preparer's own plan still names the preparer.
        Assert.Equal([(Id(1), (string?)Maker), (Id(2), null)],
            result.Items!.Select(item => (item.ItemId, item.PreparedBy)).ToArray());
        Assert.True(result.Items![0].Excludes(Maker.ToUpperInvariant()));
        Assert.False(result.Items[1].Excludes(Maker));
    }

    [Fact]
    public async Task More_plans_than_the_maximum_answer_the_oldest_of_both_states_and_has_more()
    {
        Plan(Id(1), status: 1, minutes: 30);
        Plan(Id(2), status: 1, minutes: 10);
        Plan(Id(3), status: 2, minutes: 0, version: 3);
        Order(Id(3), Id(93), status: 3, planVersionAfterCancel: 3, cancelledMinutes: 20);
        Plan(Id(4), status: 2, minutes: 0, version: 3);
        Order(Id(4), Id(94), status: 3, planVersionAfterCancel: 3, cancelledMinutes: 40);

        var result = await Source().ListPendingAsync(Context(), 3, CancellationToken.None);

        Assert.Equal([Id(2), Id(3), Id(1)], result.Items!.Select(item => item.ItemId));
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task A_failed_read_is_unavailable_and_lost_authority_after_the_read_is_denied()
    {
        Plan(Id(1), status: 1, minutes: 5);
        var source = Source();

        rows.FailReads = true;
        var failed = await source.ListPendingAsync(Context(), 10, CancellationToken.None);
        rows.FailReads = false;
        var calls = 0;
        authority.Answer = (_, _) => ++calls == 1 ? ManufacturingWorkInboxTestAuthority.CompanyWide : PermissionScope.Denied;
        var revoked = await source.ListPendingAsync(Context(), 10, CancellationToken.None);

        Assert.Equal((NativeApprovalQueueOutcomeV1.Unavailable, NativeApprovalQueueOutcomeV1.Denied), (failed.Outcome, revoked.Outcome));
    }

    [Fact]
    public async Task Both_queries_translate_for_sql_server_with_the_explicit_company_filter()
    {
        using var translating = new ManufacturingSqlTranslatingWorkInboxRows(CompanyA);
        var source = new ManufacturingPlanWorkInboxSourceV1(translating, Fence());

        var result = await source.ListPendingAsync(Context(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal(2, translating.Sql.Count);
        Assert.All(translating.Sql, sql => Assert.Contains("[Plans]", sql, StringComparison.Ordinal));
    }

    private ManufacturingPlanWorkInboxSourceV1 Source() => new(rows, Fence());

    private ManufacturingWorkInboxFence Fence() => new(company, user, authority);

    private static WorkInboxSourceContextV1 Context() => new(CompanyA, Approver, "SENTETIK-correlation", null);

    private static Guid Id(int n) => Guid.Parse($"{n:D8}-0000-4000-8000-000000000000");

    private void Plan(Guid id, int status, int minutes, long version = 1, string? createdBy = Maker, Guid? companyId = null) =>
        rows.PlanRows.Add(new PlanRow
        {
            Id = id, CompanyId = companyId ?? CompanyA, WorkCenterId = Id(8001), Process = 1, Status = status, Version = version,
            PayloadHash = "SENTETIK", CreatedBy = createdBy, CreatedAtUtc = T0.AddMinutes(minutes), UpdatedAtUtc = T0,
            RequestedDueDate = new DateOnly(2026, 12, 1),
            ApprovedByUserId = status == 2 ? Guid.Parse(Approver) : null, ApprovedAtUtc = status == 2 ? T0 : null,
        });

    private void Plan(Guid id, int status, int minutes, long version, DateOnly? dueDate) =>
        rows.PlanRows.Add(new PlanRow
        {
            Id = id, CompanyId = CompanyA, WorkCenterId = Id(8001), Process = 1, Status = status, Version = version,
            PayloadHash = "SENTETIK", CreatedBy = Maker, CreatedAtUtc = T0.AddMinutes(minutes), UpdatedAtUtc = T0,
            RequestedDueDate = dueDate,
        });

    private void Revision(Guid planId, int revisionNo, long planVersion, int minutes) =>
        rows.RevisionRows.Add(new PlanRevisionRow
        {
            CompanyId = CompanyA, PlanId = planId, RevisionNo = revisionNo, WorkCenterId = Id(8001), Reason = "SENTETIK",
            PlanVersion = planVersion, RevisedBy = Reviser, RevisedAtUtc = T0.AddMinutes(minutes), CorrelationId = "SENTETIK",
        });

    private void Order(Guid planId, Guid orderId, int status, long? planVersionAfterCancel = null, int cancelledMinutes = 0,
        Guid? companyId = null) =>
        rows.OrderRows.Add(new OrderRow
        {
            Id = orderId, CompanyId = companyId ?? CompanyA, PlanId = planId, WorkCenterId = Id(8001), Process = 1, Status = status,
            Version = 1, CreatedAtUtc = T0, UpdatedAtUtc = T0,
            CancelledByUserId = status == 3 ? Guid.Parse(Approver) : null,
            CancelledAtUtc = status == 3 ? T0.AddMinutes(cancelledMinutes) : null,
            CancellationReason = status == 3 ? "SENTETIK" : null,
            PlanVersionAfterCancel = status == 3 ? planVersionAfterCancel : null,
        });

    private static void AssertContractShape(WorkInboxSourceResultV1 result)
    {
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        foreach (var item in result.Items!)
        {
            Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link), item.Link);
            Assert.Equal(TimeSpan.Zero, item.WaitingSinceUtc.Offset);
            Assert.InRange(item.DocumentLabel.Length, 1, WorkInboxRulesV1.MaxDocumentLabelLength);
            Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("manufacturing", rule), rule.RuleCode));
            Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference), reference.Role));
        }
    }
}
