using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-37 (Kontrol Merkezi) over SYNTHETIC owners: a read-only list of the sales follow-ups. An approved shipment on no invoice is
/// overdue only after the grace days (SAT-D95); an approved invoice waits for its official match until API-35 records it; a recorded
/// TL difference is classed against its tolerance (SAT-D229); every absent or failing owner is stated, never a silent empty list;
/// the read name is measured before, between and after the reads. SENTETIK.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static SalesControlCenterService ControlCenter(InvoiceWorld invoicing, DateOnly today, bool withShipments = true,
        SalesControlCenterPolicy? policy = null)
    {
        var company = new CompanyContext();
        company.Set(Company);
        return new SalesControlCenterService(company, invoicing.World.User, invoicing.World.Authority, invoicing.Invoices,
            policy ?? new SalesControlCenterPolicy(), new IstanbulDayClock(today), withShipments ? invoicing.Shipments : null);
    }

    [Fact]
    public async Task An_uninvoiced_shipment_is_listed_only_after_the_grace_days_and_the_read_name_is_measured_around_the_reads()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.World.Authority.Measured.Clear();

        var onTheLastGraceDay = await ControlCenter(invoicing, new DateOnly(2026, 10, 27)).GetControlCenterAsync();
        Assert.Empty(onTheLastGraceDay.Items);
        Assert.Equal(7, onTheLastGraceDay.UninvoicedDispatchGraceDays);

        var overdue = await ControlCenter(invoicing, new DateOnly(2026, 10, 28)).GetControlCenterAsync();

        var item = Assert.Single(overdue.Items);
        Assert.Equal((SalesControlCenterCheckKindView.UninvoicedDispatchOverdue, SalesControlCenterSeverityView.Critical, 8, 1,
            "IRS-2026-000001", new DateOnly(2026, 10, 20)), (item.Kind, item.Severity, item.AgeDays, item.Count, item.Reference, item.ReferenceDate));
        Assert.Equal((invoicing.ShipmentId, invoicing.Order.Draft.Id, Party, Account), (item.ShipmentId, item.SalesOrderId, item.PartyId, item.CompanyAccountId));
        Assert.Null(item.InvoiceId);
        Assert.Equal(SalesControlCenterCheckStateView.Measured, overdue.Checks.Single(check => check.Kind == SalesControlCenterCheckKindView.UninvoicedDispatchOverdue).State);
        Assert.All(invoicing.World.Authority.Measured, name => Assert.Equal(InvoiceRead, name));
        Assert.True(invoicing.World.Authority.Measured.Count >= 4);
        Assert.Equal([new SalesInvoiceableShipmentQueryV1(null, SalesDispatchApiV1.MaximumCandidateShares)], invoicing.Shipments.Asked.Distinct().ToArray());
    }

    [Fact]
    public async Task A_shipment_on_an_active_invoice_is_not_overdue_and_a_flagged_line_and_an_unmatched_approval_are_listed()
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();

        var answer = await ControlCenter(invoicing, new DateOnly(2026, 11, 20)).GetControlCenterAsync();

        Assert.DoesNotContain(answer.Items, item => item.Kind == SalesControlCenterCheckKindView.UninvoicedDispatchOverdue);
        var waiting = Assert.Single(answer.Items, item => item.Kind == SalesControlCenterCheckKindView.InvoiceAwaitingOfficialMatch);
        Assert.Equal((id, SalesControlCenterSeverityView.Info, 1), (waiting.InvoiceId, waiting.Severity, waiting.Count));
        Assert.All(answer.Checks.Where(check => check.Kind != SalesControlCenterCheckKindView.UninvoicedDispatchOverdue),
            check => Assert.Equal(SalesControlCenterCheckStateView.Measured, check.State));
    }

    [Theory]
    [InlineData("648.49", false, null)]
    [InlineData("648.54", true, "WithinTolerance")]
    [InlineData("648.60", true, "Info")]
    public async Task A_matched_invoice_leaves_the_waiting_list_and_its_recorded_difference_is_classed_by_the_tolerance(string official,
        bool listed, string? severity)
    {
        var (invoicing, id, _) = await ApprovedInvoiceAsync();
        invoicing.Official.Has(decimal.Parse(official, System.Globalization.CultureInfo.InvariantCulture));
        var over = decimal.Parse(official, System.Globalization.CultureInfo.InvariantCulture) - 648.49m > 0.05m;
        await invoicing.Service().MatchInvoiceAsync(id, MatchRequest(approve: over, reason: over ? "Kur farki" : null), Metadata("k-inv-match"));

        var answer = await ControlCenter(invoicing, new DateOnly(2026, 11, 20)).GetControlCenterAsync();

        Assert.DoesNotContain(answer.Items, item => item.Kind == SalesControlCenterCheckKindView.InvoiceAwaitingOfficialMatch);
        var deviations = answer.Items.Where(item => item.Kind == SalesControlCenterCheckKindView.OfficialDocumentDeviation).ToArray();
        Assert.Equal(listed, deviations.Length == 1);
        if (listed)
        {
            Assert.Equal(severity, deviations[0].Severity.ToString());
            Assert.Equal(id, deviations[0].InvoiceId);
        }
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("throw")]
    [InlineData("unavailable")]
    [InlineData("other-company")]
    [InlineData("blank-number")]
    public async Task An_absent_failing_or_malformed_shipment_owner_leaves_only_that_check_unavailable(string fault)
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var share = invoicing.Shipments.Answer(new SalesInvoiceableShipmentQueryV1(null, 1)).Shares[0];
        invoicing.Shipments.Answer = fault switch
        {
            "throw" => _ => throw new InvalidOperationException("synthetic owner failure"),
            "unavailable" => _ => new(SalesDispatchReadOutcomeV1.Unavailable, Company, [], false, SalesInvoicedStateSourceV1.NoSource),
            "other-company" => _ => new(SalesDispatchReadOutcomeV1.Found, Guid.NewGuid(), [share], false, SalesInvoicedStateSourceV1.NoSource),
            "blank-number" => _ => new(SalesDispatchReadOutcomeV1.Found, Company, [share with { SystemNumber = " " }], false, SalesInvoicedStateSourceV1.NoSource),
            _ => invoicing.Shipments.Answer,
        };

        var answer = await ControlCenter(invoicing, new DateOnly(2026, 11, 20), withShipments: fault != "absent").GetControlCenterAsync();

        Assert.Empty(answer.Items);
        Assert.Equal(SalesControlCenterCheckStateView.Unavailable, answer.Checks.Single(check => check.Kind == SalesControlCenterCheckKindView.UninvoicedDispatchOverdue).State);
        Assert.Equal(SalesControlCenterCheckStateView.Measured, answer.Checks.Single(check => check.Kind == SalesControlCenterCheckKindView.InvoiceNeedsReview).State);
    }

    [Fact]
    public async Task A_denied_read_name_or_one_lost_after_the_reads_answers_forbidden_and_a_refusing_owner_is_forbidden()
    {
        var invoicing = await InvoiceWorld.CreateAsync();

        invoicing.World.Authority.Denied.Add(InvoiceRead);
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => ControlCenter(invoicing, new DateOnly(2026, 11, 20)).GetControlCenterAsync());
        Assert.Equal(403, denied.Status);
        Assert.Empty(invoicing.Shipments.Asked);
        invoicing.World.Authority.Denied.Clear();

        invoicing.World.Authority.LostLater = (InvoiceRead, invoicing.World.Authority.Calls + 2);
        var lost = await Assert.ThrowsAsync<SalesDraftServiceException>(() => ControlCenter(invoicing, new DateOnly(2026, 11, 20)).GetControlCenterAsync());
        Assert.Equal(403, lost.Status);
        invoicing.World.Authority.LostLater = null;

        invoicing.Shipments.Answer = _ => new(SalesDispatchReadOutcomeV1.Denied, Guid.Empty, [], false, SalesInvoicedStateSourceV1.NoSource);
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => ControlCenter(invoicing, new DateOnly(2026, 11, 20)).GetControlCenterAsync());
        Assert.Equal(403, refused.Status);
    }

    [Fact]
    public void The_grace_days_are_configurable_and_a_value_below_one_is_a_setup_error()
    {
        Assert.Equal(7, new SalesControlCenterPolicy().UninvoicedDispatchGraceDays);
        Assert.Equal(3, new SalesControlCenterPolicy(3).UninvoicedDispatchGraceDays);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalesControlCenterPolicy(0));
    }
}
