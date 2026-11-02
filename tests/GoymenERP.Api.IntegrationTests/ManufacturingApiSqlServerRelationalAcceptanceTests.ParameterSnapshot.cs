using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Settings.Application;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Üretim U1-b against REAL SQL Server through the REAL host, on a business date after the reviewed version-0 catalog
/// starts (2026-11-01): plan approval captures the Settings parameter profile (DEC-PRDCT-008) and the plan read shows it;
/// the close reads the line-scoped tolerance, accepts the catalog version 0 (URT-D23), closes a roll whose unexplained
/// difference is within ± 2 % without approval (URT-D5/D6, the difference kept as its own category) and requires the
/// separately granted exception approval outside it; another company sees none of it.
/// </summary>
public sealed partial class ManufacturingApiSqlServerRelationalAcceptanceTests
{
    private static readonly DateTimeOffset CatalogDay = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Approval_captures_the_catalog_parameter_profile_and_the_close_applies_the_catalog_tolerance()
    {
        await using var run = await Run.StartAsync(useSqlSettings: true, clockAt: CatalogDay);
        var closer = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            [.. AllWrites, ErpPermissions.ManufacturingOrdersClose, ErpPermissions.ManufacturingPlansRead]);
        using var client = run.ClientFor(closer, CompanyA);

        // Within tolerance: 1000 issued, 10 scrap (1 % fire), 975 good → 15 kg (1.5 %) unexplained.
        var (withinOrder, withinRoll) = await run.OpenOrderAsync(closer, ManufacturingProcess.Slitting);
        var planId = await run.ScalarAsync<Guid>(
            "SELECT PlanId FROM manufacturing.Orders WHERE CompanyId = @c AND Id = @o", ("@c", CompanyA), ("@o", withinOrder));
        var catalogSlitting = ApprovedManufacturingProfileCatalog.ParametersFor(CompanyA)
            .Single(entry => entry.Process == nameof(ManufacturingProcess.Slitting));
        Assert.Equal((catalogSlitting.ProfileId, "0", "Catalog", 1), (
            await run.ScalarAsync<Guid>("SELECT ProfileId FROM manufacturing.PlanParameterSnapshots WHERE PlanId = @p",
                ("@p", planId)),
            await run.ScalarAsync<string>("SELECT ProfileVersion FROM manufacturing.PlanParameterSnapshots WHERE PlanId = @p",
                ("@p", planId)),
            await run.ScalarAsync<string>("SELECT Source FROM manufacturing.PlanParameterSnapshots WHERE PlanId = @p",
                ("@p", planId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM manufacturing.PlanParameterSnapshots WHERE PlanId = @p",
                ("@p", planId))));
        Assert.Equal(7850m, await run.ScalarAsync<decimal>(
            "SELECT NumericValue FROM manufacturing.PlanParameterSnapshotValues WHERE PlanId = @p "
            + "AND ParameterKey = 'densityKgPerCubicMetre'", ("@p", planId)));
        var plan = (await client.GetFromJsonAsync<PlanViewV1>($"{Base}/plans/{planId:D}"))!;
        Assert.Equal(("Catalog", "0", "7850"), (plan.ParameterSnapshot!.Source, plan.ParameterSnapshot.ProfileVersion,
            plan.ParameterSnapshot.Values.Single(value => value.Key == "densityKgPerCubicMetre").Number));
        Assert.DoesNotContain("densityKgPerCubicMetre", plan.Calculation!.MissingParameters);
        // The snapshot is append-only evidence: the database refuses nothing here, the write guard refuses any change.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'manufacturing.plan.approved.v1' AND EntityId = @p "
            + "AND AfterJson LIKE N'%parameterProfile%'", ("@p", planId.ToString("D"))));

        var issued = await run.OkAsync(client, $"{Base}/orders/{withinOrder}/input-issues",
            Run.Issue(1, withinRoll, "1000", CatalogDay.AddMinutes(-30)));
        var scrap = await run.OkAsync(client, $"{Base}/orders/{withinOrder}/outputs",
            await run.OutputAsync(client, issued.Version, withinRoll, "Scrap", "10", CatalogDay.AddMinutes(-20)));
        var good = await run.OkAsync(client, $"{Base}/orders/{withinOrder}/outputs",
            await run.OutputAsync(client, scrap.Version, withinRoll, "GoodProduct", "975", CatalogDay.AddMinutes(-10)));
        Assert.Equal("15", Assert.Single(good.Rolls).UnexplainedKg);
        using (var closed = await client.PostWithKeyAsync($"{Base}/orders/{withinOrder}/close",
                   new CloseOrderRequestV1(good.Version), "catalog-within"))
        {
            Assert.True(closed.StatusCode == HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
            var view = (await closed.Content.ReadFromJsonAsync<OrderViewV1>())!;
            // The difference stays its own category after the close: nothing was reclassified as fire.
            Assert.Equal(("Closed", "15"), (view.Status, Assert.Single(view.Rolls).UnexplainedKg));
        }

        var catalogTolerance = ApprovedManufacturingProfileCatalog.ToleranceFor(CompanyA)
            .Single(entry => entry.Process == nameof(ManufacturingProcess.Slitting));
        Assert.Equal((catalogTolerance.ProfileId, 0L, "Catalog", "2", (Guid?)null), await DecisionAsync(run, withinOrder));

        // Outside tolerance: 40 kg (4 %) unexplained needs the separately granted exception approval.
        var (outsideOrder, outsideRoll) = await run.OpenOrderAsync(closer, ManufacturingProcess.Slitting);
        var outsideIssued = await run.OkAsync(client, $"{Base}/orders/{outsideOrder}/input-issues",
            Run.Issue(1, outsideRoll, "1000", CatalogDay.AddMinutes(-30)));
        var outsideScrap = await run.OkAsync(client, $"{Base}/orders/{outsideOrder}/outputs",
            await run.OutputAsync(client, outsideIssued.Version, outsideRoll, "Scrap", "10", CatalogDay.AddMinutes(-20)));
        var outsideGood = await run.OkAsync(client, $"{Base}/orders/{outsideOrder}/outputs",
            await run.OutputAsync(client, outsideScrap.Version, outsideRoll, "GoodProduct", "950", CatalogDay.AddMinutes(-10)));
        using (var refused = await client.PostWithKeyAsync($"{Base}/orders/{outsideOrder}/close",
                   new CloseOrderRequestV1(outsideGood.Version), "catalog-outside"))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal(ClosureAssessmentCodesV1.ApprovalRequired, (await Problem(refused)).Code);
        }

        using (var withoutGrant = await client.PostWithKeyAsync($"{Base}/orders/{outsideOrder}/close",
                   new CloseOrderRequestV1(outsideGood.Version, "SENTETIK hat sapması"), "catalog-outside-no-grant"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, withoutGrant.StatusCode);
        }

        var approver = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ErpPermissions.ManufacturingOrdersCloseExceptionApprove);
        using var approverClient = run.ClientFor(approver, CompanyA);
        using (var approved = await approverClient.PostWithKeyAsync($"{Base}/orders/{outsideOrder}/close",
                   new CloseOrderRequestV1(outsideGood.Version, "SENTETIK hat sapması"), "catalog-outside-approved"))
        {
            Assert.True(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        }

        Assert.Equal((catalogTolerance.ProfileId, 0L, "Catalog", "2", (Guid?)null), await DecisionAsync(run, outsideOrder));
        Assert.Equal(0L, await run.ScalarAsync<long>(
            "SELECT ApprovalPolicyVersion FROM manufacturing.OrderClosureDecisions WHERE CompanyId = @c AND OrderId = @o",
            ("@c", CompanyA), ("@o", outsideOrder)));

        // Company isolation: B finds neither the plan nor its snapshot.
        var reader = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingPlansRead);
        using (var foreign = run.ClientFor(reader, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{Base}/plans/{planId:D}")).StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.PlanParameterSnapshots WHERE CompanyId = @c", ("@c", CompanyB)));
    }

    private static async Task<(Guid, long, string, string, Guid?)> DecisionAsync(Run run, Guid orderId) => (
        await run.ScalarAsync<Guid>("SELECT PolicyId FROM manufacturing.OrderClosureDecisions WHERE CompanyId = @c AND OrderId = @o",
            ("@c", CompanyA), ("@o", orderId)),
        await run.ScalarAsync<long>("SELECT PolicyVersion FROM manufacturing.OrderClosureDecisions WHERE CompanyId = @c AND OrderId = @o",
            ("@c", CompanyA), ("@o", orderId)),
        await run.ScalarAsync<string>("SELECT PolicySource FROM manufacturing.OrderClosureDecisions WHERE CompanyId = @c AND OrderId = @o",
            ("@c", CompanyA), ("@o", orderId)),
        await run.ScalarAsync<string>(
            "SELECT MassBalanceDifferenceTolerancePercent FROM manufacturing.OrderClosureDecisions WHERE CompanyId = @c AND OrderId = @o",
            ("@c", CompanyA), ("@o", orderId)),
        await run.ScalarAsync<object>(
            "SELECT PolicyWorkCenterId FROM manufacturing.OrderClosureDecisions WHERE CompanyId = @c AND OrderId = @o",
            ("@c", CompanyA), ("@o", orderId)) is Guid line ? line : null);
}
