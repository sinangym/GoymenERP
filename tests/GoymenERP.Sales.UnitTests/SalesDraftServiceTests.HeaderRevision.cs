using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-04 (DEC-SAL-002, DEC-SAL-008, SAT-D27, SAT-D103) over SYNTHETIC owners: the header revision of an approved order archives
/// Rev.0 WITH its header and re-enters approval at Rev.1 under its own name <c>sales.orders.header.write</c>; the existing
/// approve flow approves it. Due terms, sales owner and delivery references keep shipping on the last approved revision
/// (SAT-D103); a customer or currency change stops shipping until it is approved and is refused while any owner record names
/// the order or while any line is priced (fail-closed). Owners are asked only for what changes; the due terms' own rules ask
/// none; a stale version and a wrong state are the key's terminal 409s; the same key replays with no second effect.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid NewAccount = Guid.Parse("4b4b4b4b-4b4b-44b4-84b4-4b4b4b4b4b4b");
    private static readonly Guid NewCurrency = Guid.Parse("5c5c5c5c-5c5c-45c5-85c5-5c5c5c5c5c5c");
    private static readonly Guid Owner = Guid.Parse("0e0e0e0e-0e0e-40e0-80e0-0e0e0e0e0e0e");

    private static SalesDueTermsV1 NetDays(int days) => new([new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, days, "100")]);

    /// <summary>The whole header; the due terms default to the order's own (the create's <see cref="TestDueTerms.PointDue30Wire"/>).</summary>
    private static SalesDraftHeaderRevisionRequest HeaderRevision(string version, SalesDueTermsV1? terms = null, Guid? owner = null,
        Guid? account = null, Guid? currency = null, SalesDraftOwnerReferenceV1? address = null, string reason = "SENTETIK vade değişti",
        bool clearTerms = false) =>
        new(version, reason, Party, account ?? Account, currency ?? Currency, address, null, owner,
            clearTerms ? null : terms ?? TestDueTerms.PointDue30Wire);

    [Fact]
    public async Task An_approved_orders_due_terms_and_sales_owner_revision_archives_rev0_with_its_header_and_ships_on_rev0_until_approved()
    {
        var world = new World();
        world.SalesOwners!.Eligible.Add(Owner);
        Owners(world);
        var (approved, line) = await AcceptedApprovedAsync(world);
        var port = LinePort(world);
        world.Authority.Measured.Clear();
        world.ResetOwnerCalls();
        var terms = new SalesDueTermsV1([
            new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, 30, "40"),
            new SalesDueInstalmentV1(2, SalesDueBasisNamesV1.ShipmentDate, 60, "60.00"),
        ]);
        var address = new SalesDraftOwnerReferenceV1(AddressId, AddressVersion);

        var revised = await world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", terms, Owner, address: address, reason: "  SENTETIK müşteri iki taksit istedi  "), Metadata("k-header"));

        Assert.False(revised.IsReplay);
        Assert.Equal(("Draft", "Preparing", "6", "Rev.1"), (revised.Detail.Draft.State, revised.Detail.Draft.ApprovalState,
            revised.Detail.Draft.Version, revised.Detail.Draft.Revision));
        Assert.Equal(new SalesDraftOwnerReferenceV1(AddressId, AddressVersion), revised.Detail.DeliveryAddress);
        // Only the owners of what changed: the contact information (the new address), the directory twice (display + eligibility);
        // neither the customer account, the money profile nor the impact owners.
        Assert.Equal((0, 1, 0), (world.Accounts.Calls, world.ContactInformation.Calls, world.Settings.Calls));
        Assert.Equal((1, Owner), (world.UserDisplays.Calls, world.UserDisplays.Asked.Single()));
        var search = Assert.Single(world.SalesOwners.Asked);
        Assert.Equal(($"Kisi {Owner:N}", SalesOwnerPickerLimitsV1.MaximumLimit, SalesPermissionManifestV1.DraftsCreate),
            (search.Query, search.Limit, search.EligibilityPermission));
        // The header name before the replay, after each owner and last; the price read first (the due terms) and for the answer.
        Assert.Equal((SalesPermissionManifestV1.OrdersHeaderWrite, PriceReadPermission, PriceReadPermission),
            (world.Authority.Measured[0], world.Authority.Measured[1], world.Authority.Measured[^1]));
        Assert.All(world.Authority.Measured.Skip(2).SkipLast(1),
            permission => Assert.Equal(SalesPermissionManifestV1.OrdersHeaderWrite, permission));
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.ReviseHeaderV1, SalesPermissionManifestV1.OrdersHeaderWrite, "sales.order-draft.header-revised.v1",
                "ReviseHeader", 5L, 6L),
            (transition.Command, transition.Envelope.PermissionCode, transition.Audit.EventType, transition.Audit.Operation,
                transition.VersionBefore, transition.VersionAfter));

        // Rev.0 is archived with the header it was approved with (its point due, no owner, no address) and the reason.
        var archived = Assert.Single(world.Store.Archived);
        Assert.Equal((0, "SENTETIK müşteri iki taksit istedi"), (archived.Revision, archived.Reason));
        Assert.Equal((Party, Account, Currency, (OwnerReference?)null, (Guid?)null), (archived.Header!.PartyId, archived.Header.CompanyAccountId,
            archived.Header.CurrencyId, archived.Header.DeliveryAddress, archived.Header.SalesOwnerUserId));
        Assert.True(SalesDueTerms.SameTerms(TestDueTerms.PointDue30, archived.Header.DueTerms));

        // The revisable header read: the new values; the due terms because the price read is held.
        var header = await world.Service().GetDraftHeaderAsync(approved.Id);
        Assert.Equal(("6", "Rev.1", Owner, $"Kisi {Owner:N}", "Visible", false),
            (header.DraftVersion, header.Revision, header.SalesOwnerUserId, header.SalesOwnerDisplayName, header.DueTermsDisclosure,
                header.CustomerOrCurrencyRevisionPending));
        Assert.Equal([(1, "OrderDate", 30, "40"), (2, "ShipmentDate", 60, "60.00")],
            header.DueTerms!.Instalments.Select(item => (item.SequenceNo, item.Basis, item.DaysAfter, item.SharePercent)));

        // SAT-D103: shipping goes on under Rev.0 (the customer is unchanged); invoicing waits for the decision.
        await AssertLastApprovedAsync(port, approved.Id, line, "Preparing", "1.25", "1.25", "6");
        Assert.Equal((SalesDraftInvoicingGatesV1.AwaitingReapproval, (string?)null), await GateAsync(world, approved.Id));

        // The same key and content: replayed, nobody asked again; another content under the key is one 409.
        var calls = (world.ContactInformation.Calls, world.UserDisplays.Calls, world.SalesOwners.Asked.Count);
        var replay = await world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", terms, Owner, address: address, reason: "  SENTETIK müşteri iki taksit istedi  "),
            Metadata("k-header", correlation: "retry"));
        Assert.Equal((true, "synthetic-correlation", "6"), (replay.IsReplay, replay.OriginalCorrelationId, replay.Detail.Draft.Version));
        Assert.Equal(calls, (world.ContactInformation.Calls, world.UserDisplays.Calls, world.SalesOwners.Asked.Count));
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", NetDays(10)), Metadata("k-header")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), Status(other));

        // The existing flow approves Rev.1; then every consumer moves to it.
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-header-submit"));
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-header-approve"));
        await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("k-header-accept"));
        var answer = await port.GetApprovedLineAsync(approved.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 1), (answer.Outcome, answer.Line!.Revision));
        Assert.Equal((SalesDraftInvoicingGatesV1.Open, "Rev.1"), await GateAsync(world, approved.Id));
    }

    [Fact]
    public async Task A_customer_and_currency_change_stops_shipping_until_approved_and_the_approval_reads_the_owner_records_again()
    {
        var world = new World();
        var (allocations, loadings) = Owners(world);
        var (approved, line) = await AcceptedApprovedAsync(world);
        var port = LinePort(world);
        world.ResetOwnerCalls();

        var revised = await world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", account: NewAccount, currency: NewCurrency, reason: "SENTETIK cari hesap ve para birimi"), Metadata("k-customer"));

        Assert.Equal(("Preparing", "Rev.1", NewAccount, NewCurrency), (revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Revision,
            revised.Detail.Draft.CompanyAccountId, revised.Detail.Draft.CurrencyId));
        // The customer account, the new currency's money profile on the order's business date and every impact owner were asked.
        Assert.Equal((1, 0, 1), (world.Accounts.Calls, world.ContactInformation.Calls, world.Settings.Calls));
        Assert.Equal((SalesMoneyOperations.OrderDraftV1, NewCurrency, Day), world.Settings.MoneyAsked);
        Assert.Equal([approved.Id], allocations.Asked);
        Assert.Equal([approved.Id], loadings.CancellationImpactAsked);
        var draft = (await world.Store.FindDraftAsync(approved.Id))!;
        Assert.Equal((NewCurrency, Day), (draft.Money.CurrencyId, draft.Money.EffectiveOn));
        Assert.Equal(Currency, world.Store.Archived.Single().Header!.CurrencyId);
        Assert.True((await world.Service().GetDraftHeaderAsync(approved.Id)).CustomerOrCurrencyRevisionPending);

        // Fail-closed: nothing of the order ships while another customer or currency waits for approval.
        var pending = await port.GetApprovedLineAsync(approved.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.NotApproved, "HeaderRevisionPending"), (pending.Outcome, pending.ApprovalState));
        Assert.Empty((await port.ListApprovedLinesAsync(10, null)).Items);

        // A record appearing before the approval (the residual race) blocks the approval — 409, not recorded.
        await world.Service().SubmitDraftAsync(approved.Id, Command("6"), Metadata("k-customer-submit"));
        allocations.Answer = order => new(Company, order, [new SalesOrderLineAllocationV1(line, 1, 0.5m)]);
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-customer-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.RevisionBlocked), Status(blocked));
        Assert.Equal(SalesCancellationImpactSourcesV1.ActiveAllocations, Assert.Single(blocked.Errors).Field);
        Assert.Empty(world.Store.Refusals);

        allocations.Answer = order => new(Company, order, []);
        await world.Service().ApproveDraftAsync(approved.Id, Command("7"), Metadata("k-customer-approve"));
        await world.Service().RecordCustomerAcceptanceAsync(approved.Id, Command("8"), Metadata("k-customer-accept"));
        var answer = await port.GetApprovedLineAsync(approved.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, NewAccount, 1), (answer.Outcome, answer.Line!.CompanyAccountId, answer.Line.Revision));
    }

    [Theory]
    [InlineData("loading", 409)]
    [InlineData("dispatch-note", 409)]
    [InlineData("unreadable", 503)]
    public async Task A_customer_change_of_an_order_with_owner_records_or_an_unreadable_owner_is_refused_unrecorded(string fault, int status)
    {
        var world = new World();
        var (allocations, loadings) = Owners(world);
        var (approved, line) = await AcceptedApprovedAsync(world);
        switch (fault)
        {
            case "loading":
                loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(line, KgUnit, 1, 0.5m, 0m, 0m, 0m)]);
                break;
            case "dispatch-note":
                loadings.CancellationImpactAnswer = order => new(Company, order, [], ActiveApprovedDispatchNoteCount: 1);
                break;
            default:
                world.Allocations = null;
                break;
        }

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", account: NewAccount), Metadata("k-blocked")));

        Assert.Equal(status, refused.Status);
        Assert.Equal(status == 409 ? SalesDraftProblemCodesV1.RevisionBlocked : SalesDraftProblemCodesV1.RevisionImpactUnavailable, refused.Code);
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Archived);
        Assert.Equal(("Approved", "Rev.0", Account), ((await world.Service().GetDraftDetailAsync(approved.Id)).Draft.ApprovalState,
            (await world.Service().GetDraftDetailAsync(approved.Id)).Draft.Revision,
            (await world.Service().GetDraftDetailAsync(approved.Id)).Draft.CompanyAccountId));
    }

    [Fact]
    public async Task A_priced_line_refuses_a_customer_or_currency_change_before_any_owner_and_a_due_terms_change_still_goes()
    {
        var world = new World().AllowDiscountsUpTo(10m);
        Owners(world);
        var request = Request() with { Lines = [Request().Lines[0] with { UnitPrice = "10.00", PriceUnitId = KgUnit }] };
        var created = (await world.Service().CreateDraftAsync(request, Metadata("k-priced"))).Detail.Draft;
        world.ResetOwnerCalls();

        var currency = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(created.Id,
            HeaderRevision(created.Version, currency: NewCurrency), Metadata("k-priced-currency")));
        Assert.Equal((422, "validation_failed"), Status(currency));
        Assert.Equal(("currencyId", SalesRuleCodes.HeaderRevisionPricedLines), (Assert.Single(currency.Errors).Field, currency.Errors[0].Code));
        var customer = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(created.Id,
            HeaderRevision(created.Version, account: NewAccount, currency: NewCurrency), Metadata("k-priced-customer")));
        Assert.Equal(["companyAccountId", "currencyId"], customer.Errors.Select(error => error.Field));
        Assert.Equal((0, 0), (world.Accounts.Calls, world.Settings.Calls));
        Assert.Empty(world.Store.Refusals);

        // A Preparing order's due terms change in place: no archive, the revision number stays.
        var revised = await world.Service().ReviseHeaderAsync(created.Id, HeaderRevision(created.Version, NetDays(45)), Metadata("k-priced-terms"));
        Assert.Equal(("Preparing", "Rev.0"), (revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Revision));
        Assert.Empty(world.Store.Archived);
    }

    [Theory]
    [InlineData("shares", "dueTerms.instalments", SalesDueTermsCodes.SharesNotWhole)]
    [InlineData("sequence", "dueTerms.instalments[1].sequenceNo", SalesDueTermsCodes.SequenceInvalid)]
    [InlineData("days", "dueTerms.instalments[0].daysAfter", SalesDueTermsCodes.DaysOutOfRange)]
    [InlineData("share-zero", "dueTerms.instalments[0].sharePercent", SalesDueTermsCodes.ShareInvalid)]
    public async Task The_due_terms_rules_are_a_422_on_the_instalment_asked_before_the_replay_and_any_owner(string fault, string field, string code)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        world.ResetOwnerCalls();
        SalesDueTermsV1 terms = fault switch
        {
            "shares" => new([new(1, "OrderDate", 30, "50"), new(2, "ShipmentDate", 60, "40")]),
            "sequence" => new([new(1, "OrderDate", 30, "50"), new(3, "ShipmentDate", 60, "50")]),
            "days" => new([new(1, "OrderDate", -1, "100")]),
            _ => new([new(1, "OrderDate", 10, "0"), new(2, "OrderDate", 20, "100")]),
        };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("4", terms), Metadata("k-terms")));

        Assert.Equal((422, "validation_failed", field, code), (refused.Status, refused.Code, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(0, world.Store.Transitions.Count(item => item.Command == SalesDraftCommands.ReviseHeaderV1));
    }

    [Theory]
    [InlineData("not-eligible", 422)]
    [InlineData("inactive", 422)]
    [InlineData("unknown", 422)]
    [InlineData("more-than-a-page", 503)]
    [InlineData("search-unavailable", 503)]
    [InlineData("search-denied", 403)]
    [InlineData("not-composed", 503)]
    public async Task A_sales_owner_must_be_active_and_hold_the_eligibility_name_or_the_change_is_refused_unrecorded(string fault, int status)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        switch (fault)
        {
            case "inactive": world.UserDisplays.Fault = "inactive"; world.SalesOwners!.Eligible.Add(Owner); break;
            case "unknown": world.UserDisplays.Fault = "missing"; break;
            case "more-than-a-page": world.SalesOwners!.HasMore = true; break;
            case "search-unavailable": world.SalesOwners!.Fault = "unavailable"; break;
            case "search-denied": world.SalesOwners!.Fault = "denied"; break;
            case "not-composed": world.SalesOwners = null; break;
        }

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("4", owner: Owner), Metadata("k-owner")));

        Assert.Equal(status, refused.Status);
        if (status == 422)
        {
            Assert.Equal(("salesOwnerUserId", SalesDraftProblemCodesV1.OwnerReferenceInvalid), (Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        }

        Assert.Empty(world.Store.Refusals);
        Assert.Null((await world.Service().GetDraftHeaderAsync(approved.Draft.Id)).SalesOwnerUserId);
    }

    [Fact]
    public async Task A_stale_version_a_pending_order_and_no_change_are_refused_and_only_the_409s_are_recorded()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        world.ResetOwnerCalls();

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("3", NetDays(30)), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, Assert.Single(stale.Errors).Field));
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("3", NetDays(30)), Metadata("k-stale")));
        Assert.True(again.IsReplay);

        var unchanged = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("4"), Metadata("k-nothing")));
        Assert.Equal((422, "header", SalesRuleCodes.RevisionNoChange), (unchanged.Status, Assert.Single(unchanged.Errors).Field, unchanged.Errors[0].Code));
        var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("4", NetDays(30), reason: "  "), Metadata("k-blank")));
        Assert.Equal((422, "reason", SalesRuleCodes.RevisionReasonRequired), (blank.Status, Assert.Single(blank.Errors).Field, blank.Errors[0].Code));

        // No owner was asked for a stale, unchanged or reasonless revision.
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.ContactInformation.Calls, world.Settings.Calls));
        var pending = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create-2"))).Detail;
        await world.Service().SubmitDraftAsync(pending.Draft.Id, Command("2"), Metadata("k-submit-2"));
        world.ResetOwnerCalls();
        var wrongState = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(pending.Draft.Id,
            HeaderRevision("3", NetDays(30)), Metadata("k-pending")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, "approvalState"), (wrongState.Status, wrongState.Code, Assert.Single(wrongState.Errors).Field));

        Assert.Equal(2, world.Store.Refusals.Count);
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.ContactInformation.Calls, world.Settings.Calls));
        Assert.Empty(world.Store.Archived);
    }

    [Theory]
    [InlineData("denied", 403)]
    [InlineData("unbound", 503)]
    [InlineData("no-price-read", 403)]
    public async Task Without_the_header_name_or_the_price_read_nothing_is_read_or_recorded(string fault, int status)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        if (fault == "denied") world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersHeaderWrite);
        else if (fault == "no-price-read") world.Authority.Denied.Add(PriceReadPermission);
        else world.Bindings = world.Bindings with { HeaderPermission = null };
        var attempts = world.Store.Attempts;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("4", NetDays(30)), Metadata("k-denied")));

        Assert.Equal(status, refused.Status);
        Assert.Equal(attempts, world.Store.Attempts);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task The_header_read_withholds_the_due_terms_without_the_price_read_and_another_companys_order_is_404()
    {
        var world = new World();
        world.SalesOwners!.Eligible.Add(Owner);
        var approved = await ApprovedAsync(world);
        await world.Service().ReviseHeaderAsync(approved.Draft.Id, HeaderRevision("4", NetDays(30), Owner), Metadata("k-terms"));

        world.Authority.Denied.Add(PriceReadPermission);
        var withheld = await world.Service().GetDraftHeaderAsync(approved.Draft.Id);
        Assert.Equal(("Withheld", (SalesDueTermsV1?)null, Owner), (withheld.DueTermsDisclosure, withheld.DueTerms, withheld.SalesOwnerUserId));

        // The directory not answering leaves the name empty; the id still shows.
        world.UserDisplays.Fault = "unavailable";
        Assert.Null((await world.Service().GetDraftHeaderAsync(approved.Draft.Id)).SalesOwnerDisplayName);

        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftHeaderAsync(Guid.NewGuid()));
        Assert.Equal(404, missing.Status);
        world.Authority.Denied.Add(ReadPermission);
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftHeaderAsync(approved.Draft.Id));
        Assert.Equal(403, denied.Status);
    }

    [Fact]
    public async Task A_stale_delivery_address_is_the_keys_terminal_409_and_an_inactive_one_is_422()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);

        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("4", address: new SalesDraftOwnerReferenceV1(AddressId, "4")), Metadata("k-address-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, "deliveryAddress"), (stale.Status, stale.Code, Assert.Single(stale.Errors).Field));
        Assert.Single(world.Store.Refusals);

        var inactive = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(approved.Draft.Id,
            HeaderRevision("4", address: new SalesDraftOwnerReferenceV1(InactiveAddressId, "2")), Metadata("k-address-inactive")));
        Assert.Equal((422, SalesDraftProblemCodesV1.OwnerReferenceInvalid), Status(inactive));
        Assert.Single(world.Store.Refusals);
    }

    [Fact]
    public async Task SAT_D126_a_change_of_only_the_sales_owner_and_delivery_references_of_an_approved_order_keeps_it_approved_at_its_revision()
    {
        var world = new World();
        world.SalesOwners!.Eligible.Add(Owner);
        Owners(world);
        var (approved, line) = await AcceptedApprovedAsync(world);
        var port = LinePort(world);

        var revised = await world.Service().ReviseHeaderAsync(approved.Id,
            HeaderRevision("5", owner: Owner, address: new SalesDraftOwnerReferenceV1(AddressId, AddressVersion), reason: "  SENTETIK sorumlu değişti  "),
            Metadata("k-simple"));

        // No re-approval and no commercial revision: still Approved at Rev.0, one version step, nothing archived.
        Assert.Equal(("Approved", "Rev.0", "6"), (revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Revision, revised.Detail.Draft.Version));
        Assert.Empty(world.Store.Archived);
        Assert.Equal(new SalesDraftOwnerReferenceV1(AddressId, AddressVersion), revised.Detail.DeliveryAddress);
        Assert.Equal(Owner, (await world.Service().GetDraftHeaderAsync(approved.Id)).SalesOwnerUserId);
        // The reason and the audit record it.
        var transition = world.Store.Transitions[^1];
        Assert.Equal((SalesDraftCommands.ReviseHeaderV1, "sales.order-draft.header-revised.v1", 5L, 6L),
            (transition.Command, transition.Audit.EventType, transition.VersionBefore, transition.VersionAfter));
        // SAT-D126: the user's own (trimmed) reason is the audit's reason, not a fixed service text.
        Assert.Equal("SENTETIK sorumlu değişti", transition.Audit.Reason);
        // Shipping and invoicing are untouched: the current approved revision ships, invoicing is open at Rev.0.
        var answer = await port.GetApprovedLineAsync(approved.Id, line);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, SalesOrderLineRevisionBasisV1.CurrentRevision),
            (answer.Outcome, answer.Line!.Revision, answer.Line.RevisionBasis));
        Assert.Equal((SalesDraftInvoicingGatesV1.Open, SalesDraftApiV1.Rev0), await GateAsync(world, approved.Id));

        // A due-terms change on the same order re-enters approval (DEC-SAL-002, SAT-D123).
        var commercial = await world.Service().ReviseHeaderAsync(approved.Id, HeaderRevision("6", NetDays(60), Owner,
            address: new SalesDraftOwnerReferenceV1(AddressId, AddressVersion)), Metadata("k-terms-after"));
        Assert.Equal(("Preparing", "Rev.1"), (commercial.Detail.Draft.ApprovalState, commercial.Detail.Draft.Revision));
        Assert.Single(world.Store.Archived);
    }

    [Fact]
    public async Task SAT_D123_an_order_without_due_terms_is_not_approved_until_a_header_revision_enters_them()
    {
        var world = new World();
        Owners(world);
        var created = (await world.Service().CreateDraftAsync(Request() with { DueTerms = null }, Metadata("k-no-terms"))).Detail.Draft;
        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-no-terms-submit"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-no-terms-approve")));

        Assert.Equal((422, "validation_failed", "dueTerms", SalesRuleCodes.DueTermsRequired),
            (refused.Status, refused.Code, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Equal("PendingApproval", (await world.Service().GetDraftDetailAsync(created.Id)).Draft.ApprovalState);

        // Rejected, given its due terms by a header revision (in place: never approved), resubmitted and approved.
        await world.Service().RejectDraftAsync(created.Id, Command("3", "SENTETIK vade yok"), Metadata("k-no-terms-reject"));
        var entered = await world.Service().ReviseHeaderAsync(created.Id, HeaderRevision("4"), Metadata("k-no-terms-header"));
        Assert.Equal(("Rejected", "Rev.0", "5"), (entered.Detail.Draft.ApprovalState, entered.Detail.Draft.Revision, entered.Detail.Draft.Version));
        await world.Service().SubmitDraftAsync(created.Id, Command("5"), Metadata("k-terms-submit"));
        var approved = await world.Service().ApproveDraftAsync(created.Id, Command("6"), Metadata("k-terms-approve"));
        Assert.Equal("Approved", approved.Detail.Draft.ApprovalState);

        // Clearing the due terms of the approved order re-enters approval, and that approval is refused again.
        var cleared = await world.Service().ReviseHeaderAsync(created.Id, HeaderRevision("7", clearTerms: true, reason: "SENTETIK vade kaldırıldı"),
            Metadata("k-clear"));
        Assert.Equal(("Preparing", "Rev.1"), (cleared.Detail.Draft.ApprovalState, cleared.Detail.Draft.Revision));
        await world.Service().SubmitDraftAsync(created.Id, Command("8"), Metadata("k-clear-submit"));
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(created.Id, Command("9"), Metadata("k-clear-approve")));
        Assert.Equal(SalesRuleCodes.DueTermsRequired, Assert.Single(again.Errors).Code);
    }

    [Fact]
    public async Task SAT_D127_the_create_keeps_the_due_terms_the_salesperson_entered_and_never_defaults_them()
    {
        var world = new World();
        var none = (await world.Service().CreateDraftAsync(Request() with { DueTerms = null }, Metadata("k-none"))).Detail.Draft;
        Assert.Null((await world.Service().GetDraftHeaderAsync(none.Id)).DueTerms);
        var entered = (await world.Service().CreateDraftAsync(Request() with { DueTerms = NetDays(45) }, Metadata("k-entered"))).Detail.Draft;
        Assert.Equal(45, (await world.Service().GetDraftHeaderAsync(entered.Id)).DueTerms!.Instalments[0].DaysAfter);

        var invalid = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            Request() with { DueTerms = new([new(1, "OrderDate", 30, "60"), new(2, "ShipmentDate", 30, "30")]) }, Metadata("k-invalid")));
        Assert.Equal((422, "dueTerms.instalments", SalesDueTermsCodes.SharesNotWhole),
            (invalid.Status, Assert.Single(invalid.Errors).Field, invalid.Errors[0].Code));
    }
}
