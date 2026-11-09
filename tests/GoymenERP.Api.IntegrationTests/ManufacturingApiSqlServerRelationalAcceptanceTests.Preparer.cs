using System.Net;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U2-d (URT-D2 "hazırlayan ≠ onaylayan") against REAL SQL Server through the REAL host: the user who saved a
/// Draft gets <c>422 manufacturing_plan_self_approval</c> on its approval and on its rejection; the refusal is a rule
/// refusal, so nothing is recorded under the key (a retry is decided again, never replayed) and the same key then
/// succeeds for another user.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_preparer_gets_422_on_approval_and_rejection_and_the_key_then_serves_another_user_on_real_sql()
    {
        await using var run = await Run.StartAsync(useSqlSettings: true, clockAt: CatalogDay);
        var user = await run.GrantAsync(TestUsers.CompanyA, CompanyA, [.. AllWrites, ErpPermissions.ManufacturingPlansRead]);
        using var preparer = run.ClientFor(user, CompanyA);
        var approver = await run.ApproverAsync(CompanyA);
        var tube = await run.WorkCenterOfKindAsync(CompanyA, "TubeProfile");

        async Task<Guid> DraftAsync(string label)
        {
            var roll = await run.StockedLabelAsync($"SYN-MFG-U2D-{label}-{Guid.NewGuid():N}");
            var created = await PlanAsync(preparer, HttpStatusCode.Created, $"{Base}/plans",
                new SavePlanRequestV1(Guid.NewGuid(), tube, [new PlannedInputRequestV1(roll.Id, "1000", roll.Version)],
                    null, LineSpeedMetresPerMinute: "30"));
            Assert.Equal(await run.UserIdOfAsync(TestUsers.CompanyA), Guid.Parse(created.PreparedBy!));
            return created.PlanId;
        }

        var toApprove = await DraftAsync("APPROVE");
        var toReject = await DraftAsync("REJECT");
        var commands = await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.Commands WHERE CompanyId = @c",
            ("@c", CompanyA));

        var approvalPath = $"{Base}/plans/{toApprove}/approval";
        var rejectionPath = $"{Base}/plans/{toReject}/rejection";
        var approval = new ApprovePlanRequestV1(1);
        var rejection = new RejectPlanRequestV1(1, "kendi planım değil");

        // The preparer is refused both decisions, twice under the same key: decided again, never replayed.
        foreach (var _ in new[] { 1, 2 })
        {
            using var refusedApproval = await preparer.PostWithKeyAsync(approvalPath, approval, "u2d-approve");
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanSelfApproval),
                (refusedApproval.StatusCode, (await Problem(refusedApproval)).Code));
            Assert.False(refusedApproval.Headers.Contains("Idempotency-Replayed"));

            using var refusedRejection = await preparer.PostWithKeyAsync(rejectionPath, rejection, "u2d-reject");
            Assert.Equal((HttpStatusCode.UnprocessableEntity, ManufacturingRuleCodes.PlanSelfApproval),
                (refusedRejection.StatusCode, (await Problem(refusedRejection)).Code));
            Assert.False(refusedRejection.Headers.Contains("Idempotency-Replayed"));
        }

        // Nothing was recorded: no command, no rejection, both plans still Draft version 1.
        Assert.Equal(commands, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Commands WHERE CompanyId = @c", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanRejections WHERE PlanId IN (@a, @r)", ("@a", toApprove), ("@r", toReject)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Plans WHERE Id IN (@a, @r) AND [Status] = 1 AND [Version] = 1",
            ("@a", toApprove), ("@r", toReject)));

        // The same keys then serve another user: the approval and the rejection both succeed.
        var approved = await PlanAsync(approver, HttpStatusCode.OK, approvalPath, approval, "u2d-approve");
        Assert.Equal(("Approved", 2L), (approved.Status, approved.Version));
        var rejected = await PlanAsync(approver, HttpStatusCode.OK, rejectionPath, rejection, "u2d-reject");
        Assert.Equal(("Draft", 2L, 1), (rejected.Status, rejected.Version, rejected.Rejections!.Count));
    }
}
