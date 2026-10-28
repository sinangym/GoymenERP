using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-30 / API-31 (plan §3.3, MOD-06; SAT-D7, DEC-SAL-006, AT-025, AT-055) over SYNTHETIC owners: the dispatch list and the
/// invoice candidates are composed from the Logistics owner's answer, the Sales store's own order references and the Cari display;
/// each read measures its OWN name before and after every owner; every absent, refusing, failing, foreign or malformed owner
/// answer fails closed (403 / 400 / 503, never an empty list or a guessed number); grouping uses only the decided key parts that
/// have an owner and lists the rest as unresolved. Every identity is SENTETIK.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private const string DispatchRead = SalesPermissionManifestV1.DraftsRead;
    private const string InvoiceRead = SalesPermissionManifestV1.InvoicesRead;
    private static readonly Guid DispatchPieceUnit = Guid.Parse("68686868-6868-4686-8686-686868686868");
    private static readonly Guid DispatchOtherCurrency = Guid.Parse("56565656-5656-4565-8565-565656565656");
    private static readonly Guid DispatchOtherParty = Guid.Parse("34343434-3434-4343-8343-343434343434");
    private static readonly Guid DispatchOtherAccount = Guid.Parse("45454545-4545-4454-8454-454545454545");
    private static readonly DateTimeOffset Shipped = new(2026, 10, 20, 7, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Approved = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_dispatch_list_is_the_owners_page_with_the_sales_order_and_customer_composed_and_measured_at_every_fence()
    {
        var dispatch = new DispatchWorld();
        var order = dispatch.AddOrder("SO-2026-000007", Party, Account, Currency, (KgUnit, true), (DispatchPieceUnit, false));
        var note = dispatch.Note(SalesDispatchNoteStateV1.Approved, "IRS-000001", order, (0, 1.250m), (1, 3m));
        dispatch.Notes.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company, [note], "next-page");

        var page = await dispatch.Service().GetDispatchesAsync(new SalesDispatchListQuery(50, null, null));

        var item = Assert.Single(page.Items);
        Assert.Equal("next-page", page.NextCursor);
        Assert.Equal((note.DispatchNoteId, Company, SalesDispatchNoteStateView.Approved, "IRS-000001", SalesInvoicedStateView.NoSource),
            (item.Id, item.CompanyId, item.State, item.SystemNumber, item.Invoicing));
        Assert.Equal((Party, Account, $"CAR-{Party:N}", $"Legal {Party:N}"),
            (item.Customer.PartyId, item.Customer.CompanyAccountId, item.Customer.Code, item.Customer.LegalName));
        Assert.Equal(
            [("SO-2026-000007", 1, "P-1", "1.25", KgUnit), ("SO-2026-000007", 2, "P-2", "3", DispatchPieceUnit)],
            item.Lines.Select(line => (line.SalesOrderDocumentNo, line.SalesOrderLineNo, line.ProductCode, line.Quantity, line.UnitId)));
        // The owner was asked once with the page bounds; the order reader once for the one order; Cari once for the one party.
        Assert.Equal([new SalesDispatchNotePageQueryV1(50, null, null)], dispatch.Notes.Asked);
        Assert.Equal([[order.OrderId]], dispatch.Orders.Asked);
        Assert.Equal([Party], dispatch.Displays.Asked);
        // The route's own name before anything, after the owner, after the order read, after Cari and before the answer.
        Assert.Equal([DispatchRead, DispatchRead, DispatchRead, DispatchRead, DispatchRead], dispatch.Authority.Measured);
        // No money on the wire: nothing named price, amount or total.
        Assert.DoesNotContain(typeof(SalesDispatchLineView).GetProperties(), property =>
            property.Name.Contains("Price", StringComparison.Ordinal) || property.Name.Contains("Amount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_state_filter_is_the_exact_wire_name_and_an_empty_page_asks_no_other_owner()
    {
        var dispatch = new DispatchWorld();
        dispatch.Notes.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company, [], null);

        var page = await dispatch.Service().GetDispatchesAsync(new SalesDispatchListQuery(10, "cursor-1", "Cancelled"));

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
        Assert.Equal([new SalesDispatchNotePageQueryV1(10, "cursor-1", SalesDispatchNoteStateV1.Cancelled)], dispatch.Notes.Asked);
        Assert.Empty(dispatch.Orders.Asked);
        Assert.Equal(0, dispatch.Displays.Calls);
        foreach (var invalid in new[] { "cancelled", "2", "Unknown", " Approved" })
        {
            Assert.Equal((400, "validation_failed"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                dispatch.Service().GetDispatchesAsync(new SalesDispatchListQuery(10, null, invalid)))));
        }

        foreach (var size in new[] { 0, 101 })
        {
            Assert.Equal(400, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                dispatch.Service().GetDispatchesAsync(new SalesDispatchListQuery(size, null, null)))).Status);
        }

        Assert.Single(dispatch.Notes.Asked);
    }

    [Fact]
    public async Task Without_the_order_read_the_dispatch_list_is_403_and_no_owner_is_asked()
    {
        var dispatch = new DispatchWorld();
        dispatch.Authority.Denied.Add(DispatchRead);

        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            dispatch.Service().GetDispatchesAsync(new SalesDispatchListQuery(50, null, null)))));
        Assert.Empty(dispatch.Notes.Asked);

        // The invoice read name does not stand in for the order read, and vice versa (each route measures only its own).
        dispatch.Authority.Denied.Clear();
        dispatch.Authority.Denied.Add(InvoiceRead);
        dispatch.Notes.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company, [], null);
        await dispatch.Service().GetDispatchesAsync(new SalesDispatchListQuery(50, null, null));
        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            dispatch.Service().GetInvoiceCandidatesAsync(new SalesInvoiceCandidateQuery(null)))));
        Assert.Empty(dispatch.Shipments.Asked);
    }

    [Fact]
    public async Task A_read_lost_while_an_owner_answered_is_403_and_nothing_is_released()
    {
        var dispatch = new DispatchWorld();
        var order = dispatch.AddOrder("SO-2026-000001", Party, Account, Currency, (KgUnit, true));
        dispatch.Notes.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company,
            [dispatch.Note(SalesDispatchNoteStateV1.Draft, null, order, (0, 1m))], null);
        // Granted for the first measurement only: lost while the Logistics owner answered.
        dispatch.Authority.LostLater = (DispatchRead, 1);

        Assert.Equal((403, "forbidden"), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            dispatch.Service().GetDispatchesAsync(new SalesDispatchListQuery(50, null, null)))));
        Assert.Single(dispatch.Notes.Asked);
        Assert.Empty(dispatch.Orders.Asked);
    }

    [Theory]
    [InlineData("absent", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("throw", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("null", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("unavailable", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("denied", 403, "forbidden")]
    [InlineData("invalid-cursor", 400, SalesDraftProblemCodesV1.InvalidCursor)]
    [InlineData("other-company", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("too-many", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("no-lines", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("approved-without-number", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("unknown-order", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("other-customer", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("unknown-line", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("other-unit", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("other-product", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("fractional-pieces", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("zero", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("display-unavailable", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    [InlineData("display-denied", 403, "forbidden")]
    public async Task Every_absent_refusing_or_malformed_dispatch_answer_fails_closed(string fault, int status, string code)
    {
        var dispatch = new DispatchWorld();
        var order = dispatch.AddOrder("SO-2026-000001", Party, Account, Currency, (KgUnit, true), (DispatchPieceUnit, false));
        var note = dispatch.Note(SalesDispatchNoteStateV1.Approved, "IRS-000001", order, (0, 1m), (1, 2m));
        var line = note.Lines[0];
        var pieceLine = note.Lines[1];
        note = fault switch
        {
            "no-lines" => note with { Lines = [] },
            "approved-without-number" => note with { SystemNumber = null },
            "unknown-order" => note with { Lines = [line with { SalesOrderId = Guid.NewGuid() }] },
            "other-customer" => note with { CompanyAccountId = DispatchOtherAccount },
            "unknown-line" => note with { Lines = [line with { SalesOrderLineId = Guid.NewGuid() }] },
            "other-unit" => note with { Lines = [line with { UnitId = DispatchPieceUnit }] },
            "other-product" => note with { Lines = [line with { ProductId = Guid.NewGuid() }] },
            "fractional-pieces" => note with { Lines = [pieceLine with { Quantity = 1.5m }] },
            "zero" => note with { Lines = [line with { Quantity = 0m }] },
            _ => note,
        };
        dispatch.Notes.Answer = fault switch
        {
            "throw" => _ => throw new InvalidOperationException("synthetic owner failure"),
            "null" => _ => null!,
            "unavailable" => _ => new(SalesDispatchReadOutcomeV1.Unavailable, Company, [], null),
            "denied" => _ => new(SalesDispatchReadOutcomeV1.Denied, Guid.Empty, [], null),
            "invalid-cursor" => _ => new(SalesDispatchReadOutcomeV1.InvalidCursor, Company, [], null),
            "other-company" => _ => new(SalesDispatchReadOutcomeV1.Found, Guid.NewGuid(), [note], null),
            "too-many" => _ => new(SalesDispatchReadOutcomeV1.Found, Company, [note, note], null),
            _ => _ => new(SalesDispatchReadOutcomeV1.Found, Company, [note], null),
        };
        dispatch.Displays.Fault = fault switch { "display-unavailable" => "unavailable", "display-denied" => "denied", _ => "" };
        dispatch.NoNotes = fault == "absent";

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            dispatch.Service().GetDispatchesAsync(new SalesDispatchListQuery(1, null, null)));

        Assert.Equal((status, code), Status(refusal));
    }

    [Fact]
    public async Task Candidates_are_grouped_by_the_SAT_D101_key_with_vade_eksik_and_the_rate_of_the_shipment_day()
    {
        var dispatch = new DispatchWorld();
        var lira = SettingsCurrencyIdentitiesV1.TurkishLira;
        // Two Turkish-lira orders of one customer: one group across orders and notes, shipped on different days (no rate in TL).
        var first = dispatch.AddOrder("SO-2026-000001", Party, Account, lira, (KgUnit, true), (DispatchPieceUnit, false));
        var second = dispatch.AddOrder("SO-2026-000002", Party, Account, lira, (KgUnit, true));
        // A foreign-currency order of the same customer shipped on two Istanbul days: the rate value has no record, so the two
        // shipment days never merge (SAT-D101: grouped by the rate day, its value unresolved).
        var foreign = dispatch.AddOrder("SO-2026-000003", Party, Account, DispatchOtherCurrency, (KgUnit, true));
        // Another customer: its own group.
        var otherCustomer = dispatch.AddOrder("SO-2026-000005", DispatchOtherParty, DispatchOtherAccount, lira, (KgUnit, true));
        var noteA = Guid.NewGuid();
        var noteB = Guid.NewGuid();
        var nextDay = Shipped.AddDays(1);
        // 22:30 UTC on 20 October is 21 October in Europe/Istanbul (SAT-D10): the shipment day is the Istanbul day.
        var lateEvening = new DateTimeOffset(2026, 10, 20, 22, 30, 0, TimeSpan.Zero);
        dispatch.Shipments.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company,
            [
                dispatch.Share(noteA, 1, first, 0, 1.250m),
                dispatch.Share(noteA, 2, first, 1, 4m),
                dispatch.Share(noteB, 1, second, 0, 0.750m, nextDay),
                dispatch.Share(noteB, 2, foreign, 0, 2m),
                dispatch.Share(noteB, 3, foreign, 0, 5m, lateEvening),
                dispatch.Share(noteB, 4, foreign, 0, 1m, nextDay),
                dispatch.Share(Guid.NewGuid(), 1, otherCustomer, 0, 1m),
            ],
            HasMore: true, SalesInvoicedStateSourceV1.NoSource);

        var answer = await dispatch.Service().GetInvoiceCandidatesAsync(new SalesInvoiceCandidateQuery(null));

        Assert.True(answer.Truncated);
        Assert.Equal(SalesInvoicedStateView.NoSource, answer.InvoicedState);
        Assert.Equal(4, answer.Groups.Count);
        var main = answer.Groups[0];
        Assert.Equal(new SalesInvoiceGroupKeyView(Company, Party, Account, lira,
            new SalesInvoiceGroupRateView(SalesInvoiceRateStateView.NotApplicable, null, null, null, null), null, null), main.Key);
        // No due-date source: "vade eksik", never invoiceable; the invoice type is the only unresolved part of a TL group.
        Assert.True(main.DueDateMissing);
        Assert.Equal([SalesInvoiceGroupKeyPart.InvoiceType], main.UnresolvedKeyParts);
        Assert.False(main.Invoiceable);
        Assert.Equal(2, main.DispatchNoteCount);
        Assert.Equal(["SO-2026-000001", "SO-2026-000001", "SO-2026-000002"], main.Shares.Select(share => share.SalesOrderDocumentNo));
        // Units are never added together: kilograms and pieces are separate exact totals.
        Assert.Equal([new SalesInvoiceQuantityTotalView(KgUnit, "kg", "2"), new SalesInvoiceQuantityTotalView(DispatchPieceUnit, "adet", "4")],
            main.QuantityTotals);
        Assert.All(main.Shares, share => Assert.Equal(SalesInvoicedStateView.NoSource, share.Invoicing));

        // The foreign-currency order: one group per shipment day (the late-evening shipment is 21 October in Istanbul and meets the
        // next-day one), each with its rate value unresolved by the SAT-D54/D55 resolver's own code.
        var day20 = answer.Groups[1];
        var day21 = answer.Groups[2];
        Assert.Equal((DispatchOtherCurrency, SalesInvoiceRateStateView.Unresolved, new DateOnly(2026, 10, 20)),
            (day20.Key.CurrencyId, day20.Key.ExchangeRate.State, day20.Key.ExchangeRate.ShipmentDate));
        Assert.Equal(PreviousBusinessDayExchangeRateCodes.DayUnconfirmed, day20.Key.ExchangeRate.ReasonCode);
        Assert.Equal((new DateOnly(2026, 10, 21), 2), (day21.Key.ExchangeRate.ShipmentDate, day21.Shares.Count));
        Assert.Equal([new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21)], day21.Shares.Select(share => share.ShipmentDate));
        Assert.All([day20, day21], group =>
        {
            Assert.Equal([SalesInvoiceGroupKeyPart.ExchangeRate, SalesInvoiceGroupKeyPart.InvoiceType], group.UnresolvedKeyParts);
            Assert.Null(group.Key.ExchangeRate.Rate);
            Assert.True(group.DueDateMissing);
            Assert.False(group.Invoiceable);
        });
        Assert.Equal((DispatchOtherParty, DispatchOtherAccount, $"CAR-{DispatchOtherParty:N}"),
            (answer.Groups[3].Key.PartyId, answer.Groups[3].Customer.CompanyAccountId, answer.Groups[3].Customer.Code));
        // The owner is asked with the technical bound; the invoice read name is measured at every fence.
        Assert.Equal([new SalesInvoiceableShipmentQueryV1(null, SalesDispatchApiV1.MaximumCandidateShares)], dispatch.Shipments.Asked);
        Assert.Equal([InvoiceRead, InvoiceRead, InvoiceRead, InvoiceRead, InvoiceRead], dispatch.Authority.Measured);
        Assert.DoesNotContain(typeof(SalesInvoiceCandidateGroupView).GetProperties().Concat(typeof(SalesInvoiceCandidateShareView).GetProperties()),
            property => property.Name.Contains("Price", StringComparison.Ordinal) || property.Name.Contains("Amount", StringComparison.Ordinal)
                || property.Name.Contains("Tax", StringComparison.Ordinal));
        // SAT-D102: the e-document status is the customer's (Cari card), never a key part of its own.
        Assert.Equal(["ExchangeRate", "InvoiceType"], Enum.GetNames<SalesInvoiceGroupKeyPart>());
    }

    [Fact]
    public async Task The_customer_filter_reaches_the_owner_and_a_share_of_another_customer_in_its_answer_fails_closed()
    {
        var dispatch = new DispatchWorld();
        var order = dispatch.AddOrder("SO-2026-000001", Party, Account, Currency, (KgUnit, true));
        var other = dispatch.AddOrder("SO-2026-000002", DispatchOtherParty, DispatchOtherAccount, Currency, (KgUnit, true));
        dispatch.Shipments.Answer = query => new(SalesDispatchReadOutcomeV1.Found, Company,
            [dispatch.Share(Guid.NewGuid(), 1, order, 0, 1m)], false, SalesInvoicedStateSourceV1.NoSource);

        var answer = await dispatch.Service().GetInvoiceCandidatesAsync(new SalesInvoiceCandidateQuery(Account));

        Assert.Equal(Account, Assert.Single(answer.Groups).Key.CompanyAccountId);
        Assert.Equal([new SalesInvoiceableShipmentQueryV1(Account, SalesDispatchApiV1.MaximumCandidateShares)], dispatch.Shipments.Asked);

        dispatch.Shipments.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company,
            [dispatch.Share(Guid.NewGuid(), 1, other, 0, 1m)], false, SalesInvoicedStateSourceV1.NoSource);
        Assert.Equal((503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable), Status(await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            dispatch.Service().GetInvoiceCandidatesAsync(new SalesInvoiceCandidateQuery(Account)))));
        Assert.Equal(400, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            dispatch.Service().GetInvoiceCandidatesAsync(new SalesInvoiceCandidateQuery(Guid.Empty)))).Status);
    }

    [Theory]
    [InlineData("absent", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("throw", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("unavailable", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("denied", 403, "forbidden")]
    [InlineData("other-company", 503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable)]
    [InlineData("same-shipment-twice", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("without-number", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("other-customer", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("orders-throw", 503, "service_unavailable")]
    [InlineData("orders-other-company", 503, SalesDraftProblemCodesV1.ReferenceProviderInvalid)]
    [InlineData("display-absent", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    public async Task Every_absent_refusing_or_malformed_candidate_answer_fails_closed(string fault, int status, string code)
    {
        var dispatch = new DispatchWorld();
        var order = dispatch.AddOrder("SO-2026-000001", Party, Account, Currency, (KgUnit, true));
        var share = dispatch.Share(Guid.NewGuid(), 1, order, 0, 1m);
        IReadOnlyList<SalesInvoiceableShipmentV1> shares = fault switch
        {
            "same-shipment-twice" => [share, share with { NoteLineNo = 2 }],
            "without-number" => [share with { SystemNumber = " " }],
            "other-customer" => [share with { CustomerPartyId = DispatchOtherParty }],
            _ => [share],
        };
        dispatch.Shipments.Answer = fault switch
        {
            "throw" => _ => throw new InvalidOperationException("synthetic owner failure"),
            "unavailable" => _ => new(SalesDispatchReadOutcomeV1.Unavailable, Company, [], false, SalesInvoicedStateSourceV1.NoSource),
            "denied" => _ => new(SalesDispatchReadOutcomeV1.Denied, Guid.Empty, [], false, SalesInvoicedStateSourceV1.NoSource),
            "other-company" => _ => new(SalesDispatchReadOutcomeV1.Found, Guid.NewGuid(), shares, false, SalesInvoicedStateSourceV1.NoSource),
            _ => _ => new(SalesDispatchReadOutcomeV1.Found, Company, shares, false, SalesInvoicedStateSourceV1.NoSource),
        };
        dispatch.NoShipments = fault == "absent";
        dispatch.NoDisplays = fault == "display-absent";
        dispatch.Orders.Fault = fault switch { "orders-throw" => "throw", "orders-other-company" => "other-company", _ => "" };

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            dispatch.Service().GetInvoiceCandidatesAsync(new SalesInvoiceCandidateQuery(null)));

        Assert.Equal((status, code), Status(refusal));
    }

    [Fact]
    public async Task No_posted_share_is_an_empty_answer_only_after_the_owner_answered()
    {
        var dispatch = new DispatchWorld();
        dispatch.Shipments.Answer = _ => new(SalesDispatchReadOutcomeV1.Found, Company, [], false, SalesInvoicedStateSourceV1.NoSource);

        var answer = await dispatch.Service().GetInvoiceCandidatesAsync(new SalesInvoiceCandidateQuery(null));

        Assert.Empty(answer.Groups);
        Assert.False(answer.Truncated);
        Assert.Empty(dispatch.Orders.Asked);
        Assert.Equal(0, dispatch.Displays.Calls);
    }

    private sealed class DispatchWorld
    {
        public FixedUser User { get; } = new();
        public FixedAuthority Authority { get; } = new();
        public FakePartyDisplays Displays { get; } = new();
        public FakeDispatchNotes Notes { get; } = new();
        public FakeInvoiceableShipments Shipments { get; } = new();
        public FakeOrderReferences Orders { get; } = new();
        public bool NoNotes { get; set; }
        public bool NoShipments { get; set; }
        public bool NoDisplays { get; set; }

        public SalesDispatchReadService Service()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new SalesDispatchReadService(company, User, Authority, new SalesDispatchReadBindings(DispatchRead, InvoiceRead), Orders,
                NoDisplays ? null : Displays, NoNotes ? null : Notes, NoShipments ? null : Shipments);
        }

        public SalesOrderDispatchReference AddOrder(string documentNo, Guid party, Guid account, Guid currency,
            params (Guid Unit, bool Kilogram)[] lines)
        {
            var order = new SalesOrderDispatchReference(Guid.NewGuid(), Company, documentNo, party, account, currency,
                [.. lines.Select((line, index) => new SalesOrderDispatchLineReference(Guid.NewGuid(), index + 1, Guid.NewGuid(),
                    $"P-{index + 1}", $"Ürün {index + 1}", line.Unit, line.Kilogram ? "kg" : "adet", line.Kilogram))]);
            Orders.Replace(order);
            return order;
        }

        public SalesDispatchNoteV1 Note(SalesDispatchNoteStateV1 state, string? number, SalesOrderDispatchReference order,
            params (int Line, decimal Quantity)[] lines) =>
            new(Guid.NewGuid(), Guid.NewGuid(), state, order.PartyId, order.CompanyAccountId, number, null, Shipped,
                state == SalesDispatchNoteStateV1.Approved ? Approved : null, null,
                [.. lines.Select((line, index) => new SalesDispatchShareV1(index + 1, order.OrderId, order.Lines[line.Line].LineId,
                    Guid.NewGuid(), Shipped, order.Lines[line.Line].ProductId, order.Lines[line.Line].OrderUnitId, line.Quantity))]);

        public SalesInvoiceableShipmentV1 Share(Guid noteId, int lineNo, SalesOrderDispatchReference order, int line, decimal quantity,
            DateTimeOffset? shippedAt = null) =>
            new(noteId, "IRS-000001", null, Approved, order.PartyId, order.CompanyAccountId, lineNo, Guid.NewGuid(), shippedAt ?? Shipped,
                order.OrderId,
                order.Lines[line].LineId, order.Lines[line].ProductId, order.Lines[line].OrderUnitId, quantity);
    }

    private sealed class FakeDispatchNotes : ISalesDispatchNoteReadPortV1
    {
        public Func<SalesDispatchNotePageQueryV1, SalesDispatchNotePageAnswerV1> Answer { get; set; } =
            _ => new(SalesDispatchReadOutcomeV1.Found, Company, [], null);
        public List<SalesDispatchNotePageQueryV1> Asked { get; } = [];

        public Task<SalesDispatchNotePageAnswerV1> ListSalesDispatchNotesAsync(SalesDispatchNotePageQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(query);
            return Task.FromResult(Answer(query));
        }
    }

    private sealed class FakeInvoiceableShipments : ISalesInvoiceableShipmentReadPortV1
    {
        public Func<SalesInvoiceableShipmentQueryV1, SalesInvoiceableShipmentAnswerV1> Answer { get; set; } =
            _ => new(SalesDispatchReadOutcomeV1.Found, Company, [], false, SalesInvoicedStateSourceV1.NoSource);
        public List<SalesInvoiceableShipmentQueryV1> Asked { get; } = [];

        public Task<SalesInvoiceableShipmentAnswerV1> ReadInvoiceableShipmentsAsync(SalesInvoiceableShipmentQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(query);
            return Task.FromResult(Answer(query));
        }
    }

    private sealed class FakeOrderReferences : ISalesOrderDispatchReferenceReader
    {
        private readonly Dictionary<Guid, SalesOrderDispatchReference> orders = [];
        public string Fault { get; set; } = "";
        public List<Guid[]> Asked { get; } = [];

        public void Replace(SalesOrderDispatchReference order) => orders[order.OrderId] = order;

        public Task<IReadOnlyList<SalesOrderDispatchReference>> ReadOrderReferencesAsync(IReadOnlyCollection<Guid> orderIds,
            CancellationToken cancellationToken = default)
        {
            Asked.Add([.. orderIds]);
            if (Fault == "throw")
            {
                throw new InvalidOperationException("synthetic store failure");
            }

            IReadOnlyList<SalesOrderDispatchReference> found = [.. orderIds.Where(orders.ContainsKey)
                .Select(id => Fault == "other-company" ? orders[id] with { CompanyId = Guid.NewGuid() } : orders[id])];
            return Task.FromResult(found);
        }
    }
}
