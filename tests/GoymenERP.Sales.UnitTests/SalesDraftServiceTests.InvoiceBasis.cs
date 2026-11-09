using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// S5 API-32 (Fatura cekirdegi 1), review round: the price basis the invoicing gate opened (current approved revision, the
/// customer-accepted archive after a customer rejection, the last approved archive after an internal rejection — SAT-D103 /
/// SAT-D114 / SAT-D115), every gate wait and the customer mismatch as named reasons, the SAT-D101 one-invoice key (currency, money
/// profile, due date, rate), and the functional-currency target of the rate answer. SENTETIK owners.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData("accepted-archive", "Rev.0")]
    [InlineData("current", "Rev.1")]
    [InlineData("last-approved-archive", "Rev.0")]
    public async Task The_invoice_takes_the_price_basis_revision_the_gate_opened(string scenario, string expectedBasis)
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var world = invoicing.World;
        Owners(world);
        var draftId = invoicing.Order.Draft.Id;
        var line = invoicing.Order.Lines[0].LineId;
        async Task<string> VersionAsync() => (await world.Service().GetDraftDetailAsync(draftId)).Draft.Version;

        await world.Service().ReviseQuantityAsync(draftId,
            new SalesDraftQuantityRevisionRequest(await VersionAsync(), "SENTETIK revizyon", [new SalesDraftLineQuantityV1(line, "1")]),
            Metadata("k-basis-revise"));
        await world.Service().SubmitDraftAsync(draftId, Command(await VersionAsync()), Metadata("k-basis-submit"));
        switch (scenario)
        {
            case "accepted-archive":
                await world.Service().ApproveDraftAsync(draftId, Command(await VersionAsync()), Metadata("k-basis-approve"));
                await world.Service().RecordCustomerRejectionAsync(draftId, Command(await VersionAsync(), "SENTETIK musteri istemedi"),
                    Metadata("k-basis-customer-reject"));
                break;
            case "current":
                await world.Service().ApproveDraftAsync(draftId, Command(await VersionAsync()), Metadata("k-basis-approve"));
                await world.Service().RecordCustomerAcceptanceAsync(draftId, Command(await VersionAsync()), Metadata("k-basis-accept"));
                break;
            default:
                await world.Service().RejectDraftAsync(draftId, Command(await VersionAsync(), "SENTETIK ic ret"), Metadata("k-basis-reject"));
                break;
        }

        var created = await invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-basis-invoice"));

        Assert.Equal(expectedBasis, Assert.Single(created.Invoice.Lines).PriceBasisRevision);
    }

    [Theory]
    [InlineData("reapproval", SalesInvoiceReasonCodesV1.OrderAwaitingReapproval)]
    [InlineData("customer-acceptance", SalesInvoiceReasonCodesV1.OrderAwaitingCustomerAcceptance)]
    [InlineData("not-approved", SalesInvoiceReasonCodesV1.OrderNotApproved)]
    public async Task An_order_the_gate_has_not_opened_waits_with_its_own_named_reason_and_nothing_is_written(string scenario, string expected)
    {
        var world = new World();
        SalesOrderDraftDetailView order;
        if (scenario == "not-approved")
        {
            order = (await world.Service().CreateDraftAsync(Priced(discount: null), Metadata("k-gate-create"))).Detail;
        }
        else
        {
            order = await PricedApprovedAsync(world, "k-gate-order");
            if (scenario == "reapproval")
            {
                await world.Service().RecordCustomerAcceptanceAsync(order.Draft.Id, Command(order.Draft.Version), Metadata("k-gate-accept"));
                Owners(world);
                var version = (await world.Service().GetDraftDetailAsync(order.Draft.Id)).Draft.Version;
                await world.Service().ReviseQuantityAsync(order.Draft.Id,
                    new SalesDraftQuantityRevisionRequest(version, "SENTETIK revizyon", [new SalesDraftLineQuantityV1(order.Lines[0].LineId, "1")]),
                    Metadata("k-gate-revise"));
            }
        }

        var invoicing = InvoiceWorld.Around(world, order);
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-gate-invoice")));

        Assert.Equal(409, refused.Status);
        Assert.Equal([new SalesDraftFieldError("shares[0]", expected)], refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
        Assert.Empty(invoicing.Invoices.Attempts);
    }

    [Fact]
    public async Task A_share_of_another_customer_than_the_order_basis_is_a_named_customer_mismatch()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var original = invoicing.Shipments.Answer;
        invoicing.Shipments.Answer = query =>
        {
            var answer = original(query);
            return answer with { Shares = [.. answer.Shares.Select(share => share with { CustomerPartyId = Guid.NewGuid() })] };
        };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-customer-mismatch")));

        Assert.Equal([new SalesDraftFieldError("shares[0]", SalesInvoiceReasonCodesV1.CustomerMismatch)], refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Theory]
    [InlineData("currency", SalesInvoiceReasonCodesV1.CurrencyMismatch)]
    [InlineData("money-profile", SalesInvoiceReasonCodesV1.MoneyProfileMismatch)]
    [InlineData("due-date", SalesInvoiceReasonCodesV1.DueDateMismatch)]
    public async Task SAT_D101_shares_that_differ_in_currency_money_profile_or_due_date_cannot_share_one_invoice(string difference, string expected)
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var world = invoicing.World;
        if (difference == "money-profile")
        {
            world.Settings.MoneyProfileOverride = Guid.Parse("77777777-7777-4777-8777-777777777771");
        }

        var second = await ApprovedAcceptedAsync(world, "k-d101-second", request => difference switch
        {
            "currency" => request with { CurrencyId = Guid.Parse("77777777-7777-4777-8777-777777777772") },
            "due-date" => request with { DueTerms = new SalesDueTermsV1([new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, 45, "100")]) },
            _ => request,
        });
        var secondShipment = Guid.NewGuid();
        var original = invoicing.Shipments.Answer;
        invoicing.Shipments.Answer = query =>
        {
            var answer = original(query);
            return answer with
            {
                Shares =
                [
                    .. answer.Shares,
                    new SalesInvoiceableShipmentV1(Guid.NewGuid(), "IRS-2026-000002", null, Approved, Party, Account, 1, secondShipment, Shipped,
                        second.Draft.Id, second.Lines[0].LineId, Product, KgUnit, 1.25m),
                ],
            };
        };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().CreateInvoiceAsync(
            invoicing.Request() with { Shares = [new(invoicing.ShipmentId), new(secondShipment)] }, Metadata("k-d101")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.Contains(new SalesDraftFieldError("shares[1]", expected), refused.Errors);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task Shipments_whose_rate_days_resolve_to_different_bulletins_cannot_share_one_invoice_rate()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        var secondShipment = Guid.NewGuid();
        var original = invoicing.Shipments.Answer;
        invoicing.Shipments.Answer = query =>
        {
            var answer = original(query);
            var first = answer.Shares[0];
            return answer with { Shares = [first, first with { ShipmentId = secondShipment, ShippedAtUtc = Shipped.AddDays(1) }] };
        };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => invoicing.Service().CreateInvoiceAsync(
            invoicing.Request() with { Shares = [new(invoicing.ShipmentId), new(secondShipment)] }, Metadata("k-rate-mismatch")));

        Assert.Equal((409, SalesInvoiceProblemCodesV1.InvoiceNotReady), Status(refused));
        Assert.Equal([new SalesDraftFieldError("shares[1]", SalesInvoiceReasonCodesV1.RateMismatch)], refused.Errors);
        Assert.Equal(2, invoicing.Rates.Asked.Count);
        Assert.Empty(invoicing.Invoices.Stored);
    }

    [Fact]
    public async Task A_resolved_rate_for_another_target_than_the_functional_currency_is_a_fail_closed_provider_error_not_a_wait()
    {
        var invoicing = await InvoiceWorld.CreateAsync();
        invoicing.Rates.AnswerTarget = Guid.NewGuid();

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            invoicing.Service().CreateInvoiceAsync(invoicing.Request(), Metadata("k-rate-target")));

        Assert.Equal((503, SalesDraftProblemCodesV1.ReferenceProviderInvalid), Status(refused));
        Assert.Empty(invoicing.Invoices.Stored);
        Assert.Empty(invoicing.Invoices.Attempts);
    }

    /// <summary>A priced one-line order approved by another person and accepted by the customer; <paramref name="tweak"/> shapes its create.</summary>
    private static async Task<SalesOrderDraftDetailView> ApprovedAcceptedAsync(World world, string key,
        Func<CreateSalesOrderDraftRequest, CreateSalesOrderDraftRequest> tweak)
    {
        var created = (await world.Service().CreateDraftAsync(tweak(Priced(discount: null)), Metadata($"{key}-create"))).Detail;
        var version = await ApproveDiscountChecksAsync(world, created.Draft.Id, created.Draft.Version, created.Lines[0].LineId);
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(version), Metadata($"{key}-submit"));
        var approved = (await world.Service().ApproveDraftAsync(created.Draft.Id, Command(submitted.Detail.Draft.Version), Metadata($"{key}-approve"))).Detail;
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id, Command(approved.Draft.Version), Metadata($"{key}-accept"));
        return approved;
    }
}
