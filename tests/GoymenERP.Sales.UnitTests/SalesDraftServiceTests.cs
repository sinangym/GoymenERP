using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// The Sales API slice's draft service over SYNTHETIC ports: the fence order, the replay before any
/// owner, the two fail-closed contract deltas, every owner refusal's public answer, the terminal
/// refusal of ADR-011 §7, and the wire mapping. The permission names bound here are registered
/// platform names, so the fence itself is exercised; the real Sales names join the catalogue with
/// the central writer's step. Every identity is SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid MoneyProfile = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    // SALES-A2a: the Cari owner's operational address / contact of the account (versions "5" / "8"), plus an inactive address.
    private static readonly Guid AddressId = Guid.Parse("a5a5a5a5-a5a5-4a5a-8a5a-a5a5a5a5a5a5");
    private static readonly Guid InactiveAddressId = Guid.Parse("a6a6a6a6-a6a6-4a6a-8a6a-a6a6a6a6a6a6");
    private static readonly Guid ContactId = Guid.Parse("c8c8c8c8-c8c8-4c8c-8c8c-c8c8c8c8c8c8");
    private static readonly Guid InactiveContactId = Guid.Parse("c9c9c9c9-c9c9-4c9c-8c9c-c9c9c9c9c9c9");
    private const string AddressVersion = "5";
    private const string ContactVersion = "8";
    private static readonly DateOnly Day = new(2026, 9, 18);
    private const string Actor = "synthetic-actor";
    // Registered platform names stand in for the Sales names until the central catalogue knows them.
    private const string CreatePermission = ErpPermissions.CounterpartyPartiesCreate;
    private const string ReadPermission = ErpPermissions.CounterpartyPartiesRead;
    // K3: the separate price-write name, bound here to a central name the fence knows (the same stand-in pattern as the others).
    private const string PricePermission = ErpPermissions.CounterpartyAccountsCreate;
    /// <summary>K3 P2: the separate price READ name — a known stand-in, distinct from every other binding.</summary>
    private const string PriceReadPermission = ErpPermissions.CounterpartyAccountsStatusUpdate;
    /// <summary>DEC-SAL-003: the separate delivery-schedule write name — a known stand-in, distinct from every other binding.</summary>
    private const string SchedulePermission = ErpPermissions.CounterpartyPartiesUpdateBasic;
    [Fact]
    public async Task A_valid_request_is_stored_once_and_answered_as_the_wire_detail()
    {
        var world = new World();

        var response = await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"));

        Assert.False(response.IsReplay);
        Assert.Equal("synthetic-correlation", response.OriginalCorrelationId);
        var draft = response.Detail.Draft;
        Assert.Equal((Company, "SO-2026-000001", "Order", "Draft", "3", SalesDraftApiV1.Rev0, 2),
            (draft.CompanyId, draft.DocumentNo, draft.Kind, draft.State, draft.Version, draft.Revision, draft.LineCount));
        Assert.Equal((Party, Account, Currency, Day), (draft.PartyId, draft.CompanyAccountId, draft.CurrencyId, draft.BusinessDate));
        var line = response.Detail.Lines[0];
        Assert.Equal((1, Product, "7", "GDC-001", "Sentetik bant", KgUnit, "kg", "1.25"),
            (line.LineNo, line.ProductId, line.ProductVersion, line.ProductCode, line.ProductName, line.OrderUnitId,
                line.OrderUnitCode, line.Quantity));
        var product = Assert.Single(draft.ProductSnapshots!);
        Assert.Equal((Product, "7", "GDC-001", "Sentetik bant", KgUnit, "kg"),
            (product.ProductId, product.ProductVersion, product.ProductCode, product.ProductName,
                product.OrderUnitId, product.OrderUnitCode));
        Assert.Null(line.UnitPrice);
        Assert.Equal(2, line.RequestedDeliverySlices!.Count);
        Assert.Null(response.Detail.DeliveryAddress);
        // A draft naming no address or contact never asks the contact-information owner.
        Assert.Equal(0, world.ContactInformation.Calls);
        // One stored attempt, its envelope from the server context and the fresh measurement's roles.
        var envelope = Assert.Single(world.Store.Envelopes);
        Assert.Equal((Actor, CreatePermission, "synthetic-correlation", "k-1", "Web"),
            (envelope.ActorId, envelope.PermissionCode, envelope.CorrelationId, envelope.IdempotencyKey, envelope.SourceChannel));
        Assert.Equal(new[] { "ROLE-A", "ROLE-B" }, envelope.RoleCodes);
        // Authority measured before the replay, and after every owner read: never fewer than 1 + reads.
        Assert.True(world.Authority.Calls >= 6, $"measured {world.Authority.Calls} times");
    }

    [Fact]
    public async Task The_same_key_content_and_actor_replays_the_stored_draft_before_any_owner_is_asked()
    {
        var world = new World();
        var request = Request(); // the same bytes again: the client resends the request it sent
        var first = await world.Service().CreateDraftAsync(request, Metadata("k-1"));
        world.ResetOwnerCalls();

        var replay = await world.Service().CreateDraftAsync(request, Metadata("k-1", correlation: "second"));

        Assert.True(replay.IsReplay);
        Assert.Equal((first.Detail.Draft.Id, "synthetic-correlation"), (replay.Detail.Draft.Id, replay.OriginalCorrelationId));
        Assert.Equal((0, 0, 0), (world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
    }

    [Fact]
    public async Task A_recorded_terminal_refusal_replays_as_the_same_409_with_its_first_correlation()
    {
        var world = new World { Products = { VersionConflict = true } };
        var request = Request();

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Equal(("lines[0].productId", ProductApiV1.VersionConflict), (refusal.Errors[0].Field, refusal.Errors[0].Code));
        Assert.Single(world.Store.Refusals);
        Assert.Null(world.Store.Refusals[0].DraftId);
        world.ResetOwnerCalls();

        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1", correlation: "retry")));

        // Another content under the same key (new line ids) is the store's §3 refusal, mapped, never a raw store fault.
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch, false), (other.Status, other.Code, other.IsReplay));

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true, "synthetic-correlation"),
            (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));
        Assert.Equal(0, world.Products.Calls);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task Authority_drift_before_a_terminal_refusal_records_nothing(string drift)
    {
        var world = new World { Products = { VersionConflict = true } };
        world.Products.BeforeErrorsEnumerated = () => Drift(world, drift);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(Request(), Metadata("k-terminal-drift")));

        Assert.Equal((403, "forbidden", false, null),
            (refused.Status, refused.Code, refused.IsReplay, refused.OriginalCorrelationId));
        Assert.Empty(refused.Errors);
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(0, world.Store.Attempts);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task A_terminal_race_winner_is_not_disclosed_after_authority_drift(string drift)
    {
        var world = new World
        {
            Products = { VersionConflict = true },
            Store = { Race = "refusal-at-record" },
        };
        world.Store.OnRecordRefusal = () => Drift(world, drift);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(Request(), Metadata("k-terminal-race-drift")));

        Assert.Equal((403, "forbidden", false, null),
            (refused.Status, refused.Code, refused.IsReplay, refused.OriginalCorrelationId));
        Assert.Empty(refused.Errors);
        Assert.Empty(world.Store.Refusals);
        Assert.Equal(1, world.Store.Attempts);
        Assert.Equal(1, world.Store.Replays);
    }

    private static void Drift(World world, string drift)
    {
        switch (drift)
        {
            case "permission":
                world.Authority.Granted = false;
                break;
            case "actor":
                world.User.Id = "SENTETIK-other-actor";
                break;
            case "company":
                world.User.Company = Guid.NewGuid();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(drift), drift, null);
        }
    }

    [Fact]
    public async Task An_authority_lost_after_the_commit_does_not_hide_the_stored_draft()
    {
        // S03 (ORG #321 P3-1): the grant is revoked between the last fence and the commit. The draft and
        // its attempt are stored; the answer is that draft, not a 403 that would make the caller drop
        // its key and create a second one. The next request of the key meets the fence first (§5).
        var world = new World();
        world.Store.OnStore = () => world.Authority.Granted = false;
        var request = Request();

        var response = await world.Service().CreateDraftAsync(request, Metadata("k-1"));

        Assert.False(response.IsReplay);
        Assert.Single(world.Store.Envelopes);
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1")));
        Assert.Equal(403, refused.Status);
        Assert.Equal(1, world.Store.Replays);
    }

    [Fact]
    public async Task The_loser_of_a_same_key_race_answers_the_winners_draft_as_a_replay()
    {
        var world = new World { Store = { Race = "draft-at-store" } };

        var response = await world.Service().CreateDraftAsync(Request(), Metadata("k-1", correlation: "loser"));

        Assert.True(response.IsReplay);
        Assert.Equal("winner-correlation", response.OriginalCorrelationId);
        Assert.Equal(world.Store.Winner!.Id, response.Detail.Draft.Id);
        Assert.Equal(2, world.Store.Replays);
    }

    [Fact]
    public async Task A_race_that_leaves_nothing_under_the_key_is_the_callers_concurrent_change()
    {
        var world = new World { Store = { Race = "empty-at-store" } };

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));

        Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange, false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Equal(2, world.Store.Replays);
    }

    [Fact]
    public async Task A_terminal_record_that_loses_its_race_answers_the_winners_recorded_refusal_as_a_replay()
    {
        var world = new World { Products = { VersionConflict = true }, Store = { Race = "refusal-at-record" } };

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1", correlation: "loser")));

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true, "winner-correlation"),
            (refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId));
        Assert.Empty(world.Store.Refusals);
    }

    [Theory]
    [InlineData("unauthenticated", 401)]
    [InlineData("other-company", 403)]
    [InlineData("not-granted", 403)]
    [InlineData("site-scoped", 403)]
    [InlineData("unregistered-name", 503)]
    [InlineData("unbound-name", 503)]
    public async Task The_fence_refuses_before_anything_is_read(string fault, int status)
    {
        var world = new World();
        switch (fault)
        {
            case "unauthenticated": world.User.Authenticated = false; break;
            case "other-company": world.User.Company = Guid.NewGuid(); break;
            case "not-granted": world.Authority.Granted = false; break;
            case "site-scoped": world.Authority.CompanyWide = false; break;
            // A name outside the central catalogue (the Sales names themselves are registered since #321's central commit).
            case "unregistered-name": world.Bindings = new SalesDraftServiceBindings("sales.synthetic.unregistered", ReadPermission, ApprovePermission, PriceReadPermission: PriceReadPermission); break;
            default: world.Bindings = new SalesDraftServiceBindings(null, ReadPermission, ApprovePermission, PriceReadPermission: PriceReadPermission); break;
        }

        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));

        Assert.Equal(status, error.Status);
        if (status == 503) Assert.Equal(SalesDraftProblemCodesV1.PermissionUnregistered, error.Code);
        Assert.Equal((0, 0, 0, 0), (world.Store.Replays, world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
    }

    [Fact]
    public async Task Owner_references_without_a_port_and_commercial_fields_are_refused_before_any_owner_is_asked()
    {
        var world = new World();

        // SALES-A2a: the delivery address and the contact have their owner port now; the other four still have none.
        var references = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            Request() with
            {
                DeliveryAddress = new SalesDraftOwnerReferenceV1(AddressId, AddressVersion), Contact = new(ContactId, ContactVersion),
                Responsible = new(Guid.NewGuid(), "2"), PaymentTerms = new(Guid.NewGuid(), "1"),
                SourceStrategy = new(Guid.NewGuid(), "1"), Package = new(Guid.NewGuid(), "1"),
            },
            Metadata("k-1")));
        Assert.Equal((422, SalesDraftProblemCodesV1.OwnerReferenceUnavailable), (references.Status, references.Code));
        Assert.Equal(["responsible", "paymentTerms", "sourceStrategy", "package"], references.Errors.Select(error => error.Field));
        Assert.Equal(0, world.ContactInformation.Calls);

        // Without the Cari contact-information port (the host's registration is central) a named address or contact is
        // 503, never stored unvalidated; the account was asked first, nothing after it.
        var withoutPort = new World { NoContactInformation = true };
        var noPort = await Assert.ThrowsAsync<SalesDraftServiceException>(() => withoutPort.Service().CreateDraftAsync(
            Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(AddressId, AddressVersion) }, Metadata("k-3")));
        Assert.Equal((503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable), (noPort.Status, noPort.Code));
        Assert.Equal((1, 0, 0), (withoutPort.Accounts.Calls, withoutPort.Settings.Calls, withoutPort.Store.Envelopes.Count));

        Assert.Equal((0, 0, 0, 0), (world.Store.Replays, world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
    }

    // ---- K3 (Sinan 5748258299): manual price and discount under the SEPARATE price-write permission ----------

    private static CreateSalesOrderDraftRequest Priced(string? unitPrice = "12.50", Guid? priceUnit = null, string? discount = "5")
    {
        var request = Request();
        return request with { Lines = [request.Lines[0] with { UnitPrice = unitPrice, PriceUnitId = priceUnit ?? (unitPrice is null ? null : KgUnit), DiscountPercent = discount }] };
    }

    [Fact]
    public async Task A_commercial_field_without_the_separate_price_permission_is_403_before_anything_is_read_and_the_create_permission_does_not_confer_it()
    {
        var world = new World();
        world.Authority.Denied.Add(PricePermission);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Priced(), Metadata("k-p1")));

        Assert.Equal((403, SalesDraftProblemCodesV1.PriceWriteForbidden), (refused.Status, refused.Code));
        Assert.Equal(["lines[0].unitPrice", "lines[0].priceUnitId", "lines[0].discountPercent"], refused.Errors.Select(error => error.Field));
        Assert.All(refused.Errors, error => Assert.Equal(SalesDraftProblemCodesV1.PriceWriteForbidden, error.Code));
        // The create permission was granted and measured first; the price name was asked next and refused; nothing after it.
        Assert.Equal([CreatePermission, PricePermission], world.Authority.Measured);
        Assert.Equal((0, 0, 0, 0, 0), (world.Store.Replays, world.Store.Envelopes.Count, world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
        // A discount alone, or a price alone, asks the same fence: any commercial field is the price owner's.
        var discountOnly = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Priced(unitPrice: null, discount: "5"), Metadata("k-p2")));
        Assert.Equal(403, discountOnly.Status);
        Assert.Equal(["lines[0].discountPercent"], discountOnly.Errors.Select(error => error.Field));
        // Without any commercial field the price name is never asked (an un-priced draft needs only the create permission).
        await world.Service().CreateDraftAsync(Request(), Metadata("k-p3"));
        Assert.DoesNotContain(PricePermission, world.Authority.Measured.Skip(4));
    }

    [Fact]
    public async Task A_priced_line_is_stored_as_the_exact_texts_under_the_price_permission_measured_again_after_the_owner_reads()
    {
        var world = new World();

        var response = await world.Service().CreateDraftAsync(Priced(), Metadata("k-p4"));

        var line = response.Detail.Lines[0];
        Assert.Equal(("12.50", KgUnit, "5"), (line.UnitPrice, line.PriceUnitId, line.DiscountPercent));
        // The price fence: asked right after the create permission, and again after every owner read, right before the save.
        Assert.Equal(PricePermission, world.Authority.Measured[1]);
        Assert.True(world.Authority.Measured.Count(name => name == PricePermission) == 2, string.Join(",", world.Authority.Measured));
        // K3 P2: the price READ is asked LAST — after the save, at the moment of answering — so the response's
        // disclosure is the authority's freshest word; the write fence's second ask is the one before it.
        Assert.Equal(PriceReadPermission, world.Authority.LastPermission);
        Assert.Equal(PricePermission, world.Authority.Measured[^2]);
        Assert.Equal(SalesPriceDisclosure.Visible, line.PriceDisclosure);
        // The envelope of the create stays the create's (the price name is a fence, not the command's permission).
        Assert.Equal(CreatePermission, Assert.Single(world.Store.Envelopes).PermissionCode);
    }

    [Fact]
    public async Task A_price_permission_lost_while_the_owners_answered_refuses_the_write_and_stores_nothing()
    {
        var world = new World();
        // Granted at the first measurement (call 2), gone by the re-measurement after the reads.
        world.Authority.LostLater = (PricePermission, 2);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Priced(), Metadata("k-p5")));

        Assert.Equal((403, SalesDraftProblemCodesV1.PriceWriteForbidden), (refused.Status, refused.Code));
        Assert.True(world.Accounts.Calls > 0 && world.Products.Calls > 0, "the owners were read before the late fence");
        Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData("12.505", "kg", "5", "lines[0].unitPrice", SalesRuleCodes.PriceScaleExceeded)]
    [InlineData("12.50", "other", "5", "lines[0].priceUnitId", SalesRuleCodes.PriceUnitUnsupported)]
    [InlineData(null, null, "5", "lines[0].discountPercent", SalesRuleCodes.DiscountWithoutPrice)]
    [InlineData("12.50", "kg", "100.5", "lines[0].discountPercent", SalesRuleCodes.DiscountOutOfRange)]
    public async Task A_commercial_value_that_is_not_mathematically_valid_is_422_on_its_own_field_and_nothing_is_stored(
        string? unitPrice, string? unit, string? discount, string field, string code)
    {
        var world = new World();
        Guid? priceUnit = unit switch { "kg" => KgUnit, "other" => Guid.NewGuid(), _ => null };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Priced(unitPrice, priceUnit, discount), Metadata("k-p6")));

        Assert.Equal((422, "validation_failed"), (refused.Status, refused.Code));
        Assert.Equal((field, code), (Assert.Single(refused.Errors).Field, Assert.Single(refused.Errors).Code));
        Assert.Empty(world.Store.Envelopes);
    }

    // ---- K3 Head B: the commercial revision ---------------------------------------------------------------------

    private static SalesDraftCommercialRevisionRequest Revision(string version, params SalesDraftLineCommercialV1[] lines) => new(version, lines);

    [Fact]
    public async Task An_approved_price_change_archives_the_approved_revision_advances_the_draft_and_is_read_back_and_a_resent_key_replays()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-submit"));
        var approved = await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("k-approve"));
        Assert.Equal(("Approved", SalesDraftApiV1.Rev0), (approved.Detail.Draft.ApprovalState, approved.Detail.Draft.Revision));

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision("4", new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, "5")), Metadata("k-r1"));

        Assert.False(revised.IsReplay);
        Assert.Equal(("Rev.1", "Preparing", "5", "13.00", KgUnit, "5"), (revised.Detail.Draft.Revision, revised.Detail.Draft.ApprovalState,
            revised.Detail.Draft.Version, revised.Detail.Lines[0].UnitPrice, revised.Detail.Lines[0].PriceUnitId, revised.Detail.Lines[0].DiscountPercent));
        Assert.Equal(new SalesDraftApprovalV1(null, null, null, null, null), revised.Detail.Approval);
        var archived = Assert.Single(world.Store.Archived);
        Assert.Equal((0, "12.50", Actor, Actor), (archived.Revision, archived.Lines[0].Commercial.UnitPrice, archived.Approval.SubmittedBy, archived.Approval.DecidedBy));
        // The command ran under the price permission, as its own transition with its audit event.
        var transition = world.Store.Transitions.Single(item => item.Command == SalesDraftCommands.ReviseCommercialV1);
        Assert.Equal((PricePermission, "sales.order-draft.commercial-revised.v1", 4L, 5L), (transition.Envelope.PermissionCode, transition.Audit.EventType, transition.VersionBefore, transition.VersionAfter));
        // The history is read back under the draft read: Rev.0 with its facts and the OLD values.
        var history = await world.Service().GetDraftRevisionsAsync(created.Draft.Id);
        var item = Assert.Single(history.Items);
        Assert.Equal(("Rev.0", Actor, Actor, "12.50", "5", 1), (item.Revision, item.SubmittedBy, item.ApprovedBy, item.Lines[0].UnitPrice, item.Lines[0].DiscountPercent, item.Lines.Count));
        // The SAME key resent is the replay of the stored result — never the "no change" refusal (ADR-011 §4/§8).
        var replay = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision("4", new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, "5")), Metadata("k-r1"));
        Assert.True(replay.IsReplay);
        Assert.Equal("Rev.1", replay.Detail.Draft.Revision);
        Assert.Single(world.Store.Archived);
        // A DIFFERENT key with the values the draft already has is the refusal, on `lines`, and archives nothing more.
        var same = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision("5", new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, "5")), Metadata("k-r2")));
        Assert.Equal((422, "validation_failed", "lines", SalesRuleCodes.RevisionNoChange), (same.Status, same.Code, Assert.Single(same.Errors).Field, Assert.Single(same.Errors).Code));
        Assert.Single(world.Store.Archived);
    }

    // ---- DEC-SAL-003: the delivery-schedule update -----------------------------------------------------------------

    private static SalesDraftDeliveryScheduleRequest Schedule(string version, DateOnly? header, params SalesDraftLineScheduleV1[] lines) =>
        new(version, header, lines);

    private static SalesDraftLineScheduleV1 SameDates(SalesOrderDraftLineDetail line, int shiftDays = 0) =>
        new(line.LineId, line.RequestedDeliveryDate?.AddDays(shiftDays),
            [.. (line.RequestedDeliverySlices ?? []).Select(slice => new SalesDraftSliceScheduleV1(slice.SliceId, slice.RequestedDeliveryDate.AddDays(shiftDays)))]);

    [Fact]
    public async Task An_approved_orders_dates_move_under_the_schedule_name_without_re_approval_are_read_back_and_a_resent_key_replays()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("3"), Metadata("k-submit"));
        var approved = await world.Service().ApproveDraftAsync(created.Draft.Id, Command("4"), Metadata("k-approve"));
        Assert.Equal(("Approved", "5"), (approved.Detail.Draft.ApprovalState, approved.Detail.Draft.Version));
        world.Authority.Measured.Clear();

        var request = Schedule("5", Day.AddDays(30), SameDates(created.Lines[0], 7), SameDates(created.Lines[1]) with { RequestedDeliveryDate = Day.AddDays(40) });
        var moved = await world.Service().RescheduleDeliveryAsync(created.Draft.Id, request, Metadata("k-s1"));

        // DEC-SAL-003: no re-approval — still Approved at Rev.0 with the same facts; one version step.
        Assert.False(moved.IsReplay);
        Assert.Equal(("Approved", SalesDraftApiV1.Rev0, "6"), (moved.Detail.Draft.ApprovalState, moved.Detail.Draft.Revision, moved.Detail.Draft.Version));
        Assert.Equal(approved.Detail.Approval, moved.Detail.Approval);
        Assert.Equal((Day.AddDays(30), Day.AddDays(40)), (moved.Detail.RequestedDeliveryDate, moved.Detail.Lines[1].RequestedDeliveryDate));
        Assert.Equal([Day.AddDays(8), Day.AddDays(9)], moved.Detail.Lines[0].RequestedDeliverySlices!.Select(slice => slice.RequestedDeliveryDate));
        Assert.Equal(["0.5", "0.5"], moved.Detail.Lines[0].RequestedDeliverySlices!.Select(slice => slice.Quantity));
        // Measured under its OWN name (never the create, approve or price name), as its own transition with its audit event.
        Assert.Contains(SchedulePermission, world.Authority.Measured);
        Assert.DoesNotContain(ApprovePermission, world.Authority.Measured.Where(name => name != ReadPermission));
        var transition = world.Store.Transitions.Single(item => item.Command == SalesDraftCommands.RescheduleDeliveryV1);
        Assert.Equal((SchedulePermission, "sales.order-draft.delivery-rescheduled.v1", 5L, 6L),
            (transition.Envelope.PermissionCode, transition.Audit.EventType, transition.VersionBefore, transition.VersionAfter));
        // The history reads back schedule 0 (as created, nobody) and schedule 1 (the planner, the new dates).
        var history = await world.Service().GetDraftDeliverySchedulesAsync(created.Draft.Id);
        Assert.Equal([0, 1], history.Items.Select(item => item.ScheduleNo));
        Assert.Equal(((string?)null, (DateOnly?)null), (history.Items[0].ChangedBy, history.Items[0].RequestedDeliveryDate));
        Assert.Equal((Actor, (DateOnly?)Day.AddDays(30)), (history.Items[1].ChangedBy, history.Items[1].RequestedDeliveryDate));
        Assert.Equal([Day.AddDays(1), Day.AddDays(2)], history.Items[0].Lines[0].Slices.Select(slice => slice.RequestedDeliveryDate));
        // The SAME key resent is the replay of the stored result, never the "no change" refusal.
        var replay = await world.Service().RescheduleDeliveryAsync(created.Draft.Id, request, Metadata("k-s1"));
        Assert.True(replay.IsReplay);
        Assert.Single(world.Store.Schedules);
        // A DIFFERENT key with the dates the draft already has is the refusal on `lines`, and records nothing more.
        var same = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("6", Day.AddDays(30), SameDates(moved.Detail.Lines[0]), SameDates(moved.Detail.Lines[1])), Metadata("k-s2")));
        Assert.Equal((422, "validation_failed", "lines", SalesRuleCodes.ScheduleNoChange), (same.Status, same.Code, Assert.Single(same.Errors).Field, Assert.Single(same.Errors).Code));
        Assert.Single(world.Store.Schedules);
    }

    [Fact]
    public async Task Without_the_schedule_name_it_is_403_before_the_draft_is_read_and_the_other_names_do_not_confer_it()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        world.Authority.Denied.Add(SchedulePermission);

        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("3", Day.AddDays(30), SameDates(created.Lines[0]), SameDates(created.Lines[1])), Metadata("k-s1")));

        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.Empty(world.Store.Schedules);
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.RescheduleDeliveryV1);
    }

    [Fact]
    public async Task An_unregistered_delivery_schedule_name_fails_closed_503_and_the_production_name_is_registered()
    {
        // Central registration (ErpPermissions.SalesOrdersDeliveryScheduleWrite) made the production binding KNOWN; the
        // fail-closed path for a name the catalogue does not know is kept with a synthetic name (the price-read precedent).
        Assert.True(ErpPermissions.IsKnown(SalesPermissionManifestV1.OrdersDeliveryScheduleWrite));
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        world.Bindings = world.Bindings with { DeliverySchedulePermission = "sales.synthetic.unregistered" };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("3", Day.AddDays(30), SameDates(created.Lines[0]), SameDates(created.Lines[1])), Metadata("k-s1")));

        // A name the central catalogue does not know yet is a setup gap (503), never a grant.
        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Schedules);
    }

    [Fact]
    public async Task A_schedule_name_revoked_after_the_store_reads_is_refused_before_any_schedule_attempt_or_audit()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        var attemptsBefore = world.Store.Attempts;
        world.Store.OnStore = () => world.Authority.Denied.Add(SchedulePermission);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("3", Day.AddDays(30), SameDates(created.Lines[0]), SameDates(created.Lines[1])), Metadata("k-s1")));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Equal((attemptsBefore, 0), (world.Store.Attempts, world.Store.Schedules.Count));
    }

    [Fact]
    public async Task Line_and_slice_mismatches_are_422_on_their_field_and_a_withdrawn_or_stale_draft_is_the_keys_terminal_409()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        var (first, second) = (created.Lines[0], created.Lines[1]);

        var lines = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("3", Day.AddDays(30), SameDates(first)), Metadata("k-s1")));
        Assert.Equal((422, "lines", SalesRuleCodes.ScheduleLinesMismatch), (lines.Status, Assert.Single(lines.Errors).Field, Assert.Single(lines.Errors).Code));

        // Dates only: dropping one of the first line's two slices is a re-split, refused on that line's index in THIS request.
        var dropped = SameDates(first) with { Slices = [new SalesDraftSliceScheduleV1(first.RequestedDeliverySlices![0].SliceId, Day)] };
        var slices = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("3", Day.AddDays(30), SameDates(second), dropped), Metadata("k-s2")));
        Assert.Equal((422, "lines[1].slices", SalesRuleCodes.ScheduleSlicesMismatch), (slices.Status, Assert.Single(slices.Errors).Field, Assert.Single(slices.Errors).Code));

        // A stale version is terminal for the key and replays with the first correlation.
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("2", Day.AddDays(30), SameDates(first), SameDates(second)), Metadata("k-s3")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, false), (stale.Status, stale.Code, stale.IsReplay));
        var staleAgain = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("2", Day.AddDays(30), SameDates(first), SameDates(second)), Metadata("k-s3")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true), (staleAgain.Status, staleAgain.Code, staleAgain.IsReplay));

        // A withdrawn draft: terminal state conflict on `approvalState`.
        await world.Service().WithdrawDraftAsync(created.Draft.Id, Command("3"), Metadata("k-withdraw"));
        var withdrawn = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("4", Day.AddDays(30), SameDates(first), SameDates(second)), Metadata("k-s4")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, "approvalState"), (withdrawn.Status, withdrawn.Code, Assert.Single(withdrawn.Errors).Field));
        Assert.Empty(world.Store.Schedules);

        // Malformed version and an unknown draft answer as the other commands do; the history of an unknown draft is 404.
        Assert.Equal(400, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(created.Draft.Id,
            Schedule("x", null, SameDates(first), SameDates(second)), Metadata("k-s5")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().RescheduleDeliveryAsync(Guid.NewGuid(),
            Schedule("1", null, SameDates(first), SameDates(second)), Metadata("k-s6")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDeliverySchedulesAsync(Guid.NewGuid()))).Status);
    }

    [Fact]
    public async Task A_price_write_revoked_after_the_store_reads_is_refused_before_business_history_attempt_or_audit_changes()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-submit"));
        var approved = await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("k-approve"));
        var attemptsBefore = world.Store.Attempts;
        var transitionsBefore = world.Store.Transitions.Count;
        world.Store.OnStore = () => world.Authority.Denied.Add(PricePermission);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(
            created.Draft.Id, Revision("4", new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, "5")), Metadata("k-r1")));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Equal(("Approved", SalesDraftApiV1.Rev0, "4"),
            (approved.Detail.Draft.ApprovalState, approved.Detail.Draft.Revision, approved.Detail.Draft.Version));
        Assert.Equal((attemptsBefore, transitionsBefore, 0),
            (world.Store.Attempts, world.Store.Transitions.Count, world.Store.Archived.Count));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
    }

    /// <summary>
    /// K3 P2 (Sinan 5748258299): the price READ is its own narrow name. A reader holding the draft read but not it gets
    /// the WITHHELD marker with the three commercial members null — never the texts, never a bare null that could read
    /// as "unpriced" — on the detail and on the archived revisions; the same reader with the name gets the texts. The
    /// name is ASKED, not required: refusing it never turns a read into a 403.
    /// </summary>
    [Fact]
    public async Task A_reader_without_the_price_read_gets_the_withheld_marker_on_the_detail_and_the_history_and_with_it_the_texts()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Priced(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-submit"));
        await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("k-approve"));
        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision("4", new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, "5")), Metadata("k-r1"));

        world.Authority.Denied.Add(PriceReadPermission);
        world.Authority.Measured.Clear();
        var withheld = await world.Service().GetDraftDetailAsync(created.Draft.Id);
        var line = Assert.Single(withheld.Lines);
        Assert.Equal((SalesPriceDisclosure.Withheld, null, null, null), (line.PriceDisclosure, line.UnitPrice, line.PriceUnitId, line.DiscountPercent));
        // Not a 403, and the other facts of the line are untouched by the marker.
        Assert.Equal(("1.25", "Rev.1"), (line.Quantity, withheld.Draft.Revision));
        var history = await world.Service().GetDraftRevisionsAsync(created.Draft.Id);
        var archivedLine = Assert.Single(Assert.Single(history.Items).Lines);
        Assert.Equal((SalesPriceDisclosure.Withheld, null, null, null), (archivedLine.PriceDisclosure, archivedLine.UnitPrice, archivedLine.PriceUnitId, archivedLine.DiscountPercent));
        // The ask is pinned by its property: the read name was asked (each read once, after the draft read), for this company.
        Assert.Equal([ReadPermission, ReadPermission, PriceReadPermission, ReadPermission, ReadPermission, PriceReadPermission], world.Authority.Measured);

        world.Authority.Denied.Remove(PriceReadPermission);
        var visible = await world.Service().GetDraftDetailAsync(created.Draft.Id);
        Assert.Equal((SalesPriceDisclosure.Visible, "13.00", KgUnit, "5"), (visible.Lines[0].PriceDisclosure, visible.Lines[0].UnitPrice, visible.Lines[0].PriceUnitId, visible.Lines[0].DiscountPercent));
        var visibleHistory = await world.Service().GetDraftRevisionsAsync(created.Draft.Id);
        Assert.Equal((SalesPriceDisclosure.Visible, "12.50"), (visibleHistory.Items[0].Lines[0].PriceDisclosure, visibleHistory.Items[0].Lines[0].UnitPrice));
    }

    /// <summary>
    /// ASSUMPTION-SALESP-6 (reversible by a GRANT, never by code): the WRITE name does not confer the READ. A pricer
    /// holding only the write name stores the exact texts and gets the WITHHELD marker back on its own response; the
    /// texts are in the store, and a holder of the read name sees them. K3 names two separate names and says nothing
    /// about one implying the other; making the write imply the read would be choosing a policy here.
    /// </summary>
    [Fact]
    public async Task A_pricer_without_the_price_read_stores_the_texts_and_gets_the_withheld_marker_on_its_own_response()
    {
        var world = new World();
        world.Authority.Denied.Add(PriceReadPermission);
        // Built ONCE: Priced() mints a fresh line id per call, and a same-key resend with another payload is the 409 (ADR-011 §3).
        var priced = Priced();

        var response = await world.Service().CreateDraftAsync(priced, Metadata("k-p6"));

        var line = Assert.Single(response.Detail.Lines);
        Assert.Equal((SalesPriceDisclosure.Withheld, null, null, null), (line.PriceDisclosure, line.UnitPrice, line.PriceUnitId, line.DiscountPercent));
        Assert.Equal(CreatePermission, Assert.Single(world.Store.Envelopes).PermissionCode);
        // The store holds what the pricer wrote: the marker withheld the answer, not the write.
        world.Authority.Denied.Remove(PriceReadPermission);
        var detail = await world.Service().GetDraftDetailAsync(response.Detail.Draft.Id);
        Assert.Equal(("12.50", KgUnit, "5"), (detail.Lines[0].UnitPrice, detail.Lines[0].PriceUnitId, detail.Lines[0].DiscountPercent));

        // A REPLAY is disclosed for the REPLAYING caller, not carried over from the original answer: the same key resent
        // by a holder of the read name shows the texts the first response withheld; resent again without it, withheld.
        var replayVisible = await world.Service().CreateDraftAsync(priced, Metadata("k-p6"));
        Assert.Equal((true, SalesPriceDisclosure.Visible, "12.50"), (replayVisible.IsReplay, replayVisible.Detail.Lines[0].PriceDisclosure, replayVisible.Detail.Lines[0].UnitPrice));
        world.Authority.Denied.Add(PriceReadPermission);
        var replayWithheld = await world.Service().CreateDraftAsync(priced, Metadata("k-p6"));
        Assert.Equal((true, SalesPriceDisclosure.Withheld, null), (replayWithheld.IsReplay, replayWithheld.Detail.Lines[0].PriceDisclosure, replayWithheld.Detail.Lines[0].UnitPrice));
    }

    /// <summary>A price READ name the central catalogue does not know is a setup gap on every answer that would disclose: 503, never a silent "withheld".</summary>
    [Fact]
    public async Task An_unregistered_price_read_name_is_503_on_the_detail_never_a_silent_withheld()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        world.Bindings = world.Bindings with { PriceReadPermission = "sales.synthetic.unregistered" };

        var gap = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDetailAsync(created.Draft.Id));
        Assert.Equal((503, SalesDraftProblemCodesV1.PermissionUnregistered), (gap.Status, gap.Code));
    }

    /// <summary>D2: a SalesSupport creator, including a dual-role creator, cannot approve or reject; another actor and a non-SalesSupport creator retain main behavior.</summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task A_sales_support_creator_cannot_decide_and_other_personas_keep_main_behavior(string decision)
    {
        var world = new World();
        world.ActiveRoleCodes.Codes.UnionWith([ErpPersonaRoleCodes.SalesSupport, "SALES_MANAGER"]);
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-submit"));
        var transitionsBefore = world.Store.Transitions.Count;

        Task<SalesOrderDraftResponse> Decide() => decision == "approve"
            ? world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("k-self"))
            : world.Service().RejectDraftAsync(created.Draft.Id, Command("3", "SENTETIK"), Metadata("k-self"));

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(Decide);
        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Equal((transitionsBefore, 0), (world.Store.Transitions.Count, world.Store.Refusals.Count));
        var pending = await world.Service().GetDraftDetailAsync(created.Draft.Id);
        Assert.Equal(("PendingApproval", true, true),
            (pending.Draft.ApprovalState, pending.IsOwnDraft, pending.IsOwnDecisionRestricted));

        world.User.Id = "synthetic-manager";
        world.ActiveRoleCodes.Codes.Clear();
        var decided = await Decide();
        Assert.Equal(decision == "approve" ? "Approved" : "Rejected", decided.Detail.Draft.ApprovalState);
        Assert.Equal("synthetic-manager", decided.Detail.Approval.DecidedBy);

        world.User.Id = Actor;
        var second = (await world.Service().CreateDraftAsync(Request(), Metadata("k-2"))).Detail;
        await world.Service().SubmitDraftAsync(second.Draft.Id, Command("2"), Metadata("k-submit-2"));
        var own = await world.Service().ApproveDraftAsync(second.Draft.Id, Command("3"), Metadata("k-own"));
        Assert.Equal(("Approved", Actor, Actor, true, false),
            (own.Detail.Draft.ApprovalState, own.Detail.Approval.SubmittedBy, own.Detail.Approval.DecidedBy,
                own.Detail.IsOwnDraft, own.Detail.IsOwnDecisionRestricted));
    }

    /// <summary>
    /// D2md1: creator identity alone is not an own-decision ban. With no active SalesSupport persona,
    /// the creator keeps the existing approve/reject path under the ordinary approve authority.
    /// </summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task A_non_sales_support_creator_keeps_the_existing_decision_behavior(string decision)
    {
        var world = new World();
        Assert.Empty(world.ActiveRoleCodes.Codes);
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-md1-create"))).Detail;
        Assert.True(created.IsOwnDraft);
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-md1-submit"));
        world.Authority.Measured.Clear();

        var decided = decision == "approve"
            ? await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("k-md1-decide"))
            : await world.Service().RejectDraftAsync(created.Draft.Id, Command("3", "SENTETIK md1"), Metadata("k-md1-decide"));

        Assert.Equal((decision == "approve" ? "Approved" : "Rejected", Actor, Actor, true, false),
            (decided.Detail.Draft.ApprovalState, decided.Detail.Approval.SubmittedBy, decided.Detail.Approval.DecidedBy,
                decided.Detail.IsOwnDraft, decided.Detail.IsOwnDecisionRestricted));
        Assert.Contains(ApprovePermission, world.Authority.Measured);
        Assert.All(world.Authority.Measured, permission => Assert.Contains(permission, new[] { ApprovePermission, PriceReadPermission }));
    }

    /// <summary>
    /// The persona read is not a gap after the authority fence: an actor swap or permission loss during
    /// the late read is seen by the final permission/binding measurement before any state effect.
    /// </summary>
    [Theory]
    [InlineData("actor")]
    [InlineData("permission")]
    public async Task A_change_during_the_late_persona_read_is_refused_before_the_state_write(string change)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-persona-race-create"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-persona-race-submit"));
        var transitions = world.Store.Transitions.Count;
        var refusals = world.Store.Refusals.Count;
        world.ActiveRoleCodes.Calls = 0;
        world.ActiveRoleCodes.OnRead = call =>
        {
            if (call != 2) return;
            if (change == "actor") world.User.Id = "synthetic-swapped-during-persona-read";
            else world.Authority.Granted = false;
        };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("k-persona-race-decide")));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Equal((transitions, refusals), (world.Store.Transitions.Count, world.Store.Refusals.Count));
    }

    /// <summary>
    /// D2 (Sinan 5750987986 points 1–2): "own order" is the order the actor PREPARED (the stored creator), not the one
    /// they submitted. A creates, an authorized B submits: A holding the general approve name is still fenced (the own
    /// name is asked and refused); B — the submitter, not the preparer — decides it exactly as on main: only the approve
    /// name and the disclosure name are asked, nothing new enters, the transition is recorded. No persona D2 did not name
    /// gains a silent new self-approval policy.
    /// </summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Ownership_is_the_preparer_not_the_submitter_and_an_authorized_non_preparer_behaves_as_on_main(string decision)
    {
        var world = new World();
        world.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport);
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        world.User.Id = "synthetic-submitter";
        world.ActiveRoleCodes.Codes.Clear();
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-submit"));
        Assert.Equal("synthetic-submitter", (await world.Service().GetDraftDetailAsync(created.Draft.Id)).Approval.SubmittedBy);

        Task<SalesOrderDraftResponse> Decide() => decision == "approve"
            ? world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("k-" + world.User.Id))
            : world.Service().RejectDraftAsync(created.Draft.Id, Command("3", "SENTETIK"), Metadata("k-" + world.User.Id));

        // The preparer, holding the approve name, deciding the order they prepared but did not submit: fenced.
        world.User.Id = Actor;
        world.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport);
        var transitions = world.Store.Transitions.Count;
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(Decide);
        Assert.Equal((403, "forbidden", transitions, 0), (refused.Status, refused.Code, world.Store.Transitions.Count, world.Store.Refusals.Count));

        // The submitter — authorized, not the preparer — decides it as on main: the same names main's path asked, no other.
        world.User.Id = "synthetic-submitter";
        world.ActiveRoleCodes.Codes.Clear();
        world.Authority.Measured.Clear();
        var decided = await Decide();
        Assert.Equal(decision == "approve" ? "Approved" : "Rejected", decided.Detail.Draft.ApprovalState);
        Assert.Equal(("synthetic-submitter", "synthetic-submitter"), (decided.Detail.Approval.SubmittedBy, decided.Detail.Approval.DecidedBy));
        Assert.Equal(transitions + 1, world.Store.Transitions.Count);
        Assert.All(world.Authority.Measured, name => Assert.Contains(name, new[] { ApprovePermission, PriceReadPermission }));
    }

    /// <summary>
    /// D2: the CREATOR withdraws their own not-yet-approved draft under the create name — its own command, its own audit event,
    /// its own terminal state, nothing deleted; the same key replays. Anyone else is refused 403 with nothing recorded (no name
    /// lifts it); an approved draft is the key's terminal 409; an overlong reason is 422.
    /// </summary>
    [Fact]
    public async Task The_creator_withdraws_their_own_unapproved_draft_with_its_own_trail_and_nobody_else_can()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-submit"));

        // Someone else, even holding the create name: 403, nothing recorded.
        world.User.Id = "synthetic-other";
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithdrawDraftAsync(created.Draft.Id, Command("3"), Metadata("k-w-other")));
        Assert.Equal((403, "forbidden", 1, 0), (other.Status, other.Code, world.Store.Transitions.Count, world.Store.Refusals.Count));
        Assert.False((await world.Service().GetDraftDetailAsync(created.Draft.Id)).IsOwnDraft);

        // The creator: Withdrawn, the trail is its own command and event under the CREATE name, the reason as stored.
        world.User.Id = Actor;
        var withdrawn = await world.Service().WithdrawDraftAsync(created.Draft.Id, Command("3", "  SENTETIK vazgeçildi  "), Metadata("k-w"));
        Assert.Equal(("Withdrawn", "4", Actor, Actor, "SENTETIK vazgeçildi", "Rev.0"),
            (withdrawn.Detail.Draft.ApprovalState, withdrawn.Detail.Draft.Version, withdrawn.Detail.Approval.SubmittedBy,
                withdrawn.Detail.Approval.DecidedBy, withdrawn.Detail.Approval.RejectionReason, withdrawn.Detail.Draft.Revision));
        var transition = world.Store.Transitions.Single(item => item.Command == SalesDraftCommands.WithdrawV1);
        Assert.Equal((CreatePermission, "sales.order-draft.withdrawn.v1", "Withdraw", 3L, 4L),
            (transition.Envelope.PermissionCode, transition.Audit.EventType, transition.Audit.Operation, transition.VersionBefore, transition.VersionAfter));
        Assert.Equal(created.Lines.Count, withdrawn.Detail.Lines.Count);
        // The relationship travels, not the identity: the creator's own answers say so, another actor's detail says not.
        Assert.Equal((true, true), (created.IsOwnDraft, withdrawn.Detail.IsOwnDraft));

        // The same key again: the replay, not a second withdrawal.
        var replay = await world.Service().WithdrawDraftAsync(created.Draft.Id, Command("3", "  SENTETIK vazgeçildi  "), Metadata("k-w"));
        Assert.True(replay.IsReplay);
        Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.WithdrawV1);

        // Terminal: a submit after the withdrawal is the key's terminal 409.
        var after = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id, Command("4"), Metadata("k-submit-again")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (after.Status, after.Code));

        // An APPROVED draft is not withdrawable — the key's terminal 409, recorded; an overlong reason is 422 before anything.
        var approved = (await world.Service().CreateDraftAsync(Request(), Metadata("k-a"))).Detail;
        await world.Service().SubmitDraftAsync(approved.Draft.Id, Command("2"), Metadata("k-a-submit"));
        world.User.Id = "synthetic-manager";
        await world.Service().ApproveDraftAsync(approved.Draft.Id, Command("3"), Metadata("k-a-approve"));
        world.User.Id = Actor;
        var refusals = world.Store.Refusals.Count;
        var conflict = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithdrawDraftAsync(approved.Draft.Id, Command("4"), Metadata("k-a-w")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, refusals + 1), (conflict.Status, conflict.Code, world.Store.Refusals.Count));
        Assert.Equal(approved.Draft.Id, world.Store.Refusals[^1].DraftId);
        // An APPROVED HISTORY (Sinan 5750987986 point 3a): a commercial revision brings the approved draft back to Preparing
        // at Rev.1 — the easy path is still refused (the key's terminal 409, recorded); the archived snapshot stays.
        // (The refused withdrawal above did not advance the version: still 4 here.)
        var revised = await world.Service().ReviseCommercialAsync(approved.Draft.Id,
            Revision("4", new SalesDraftLineCommercialV1(approved.Lines[0].LineId, "13.00", KgUnit, "5")), Metadata("k-a-revise"));
        Assert.Equal(("Preparing", "Rev.1", "5"), (revised.Detail.Draft.ApprovalState, revised.Detail.Draft.Revision, revised.Detail.Draft.Version));
        var history = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithdrawDraftAsync(approved.Draft.Id, Command("5"), Metadata("k-a-w2")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, refusals + 2, 1), (history.Status, history.Code, world.Store.Refusals.Count, world.Store.Archived.Count));
        Assert.Equal(("Preparing", "Rev.1"), ((await world.Service().GetDraftDetailAsync(approved.Draft.Id)).Draft.ApprovalState, revised.Detail.Draft.Revision));
        var tooLong = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithdrawDraftAsync(created.Draft.Id, Command("4", new string('x', SalesOrderDraft.MaximumRejectionReasonLength + 1)), Metadata("k-w-long")));
        Assert.Equal((422, SalesDraftProblemCodesV1.ApprovalReasonTooLong), (tooLong.Status, tooLong.Code));
    }

    [Fact]
    public async Task Detail_uses_the_post_read_persona_when_sales_support_is_added_during_the_read()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-detail-add"))).Detail;
        world.ActiveRoleCodes.Codes.Remove(ErpPersonaRoleCodes.SalesSupport);
        world.ActiveRoleCodes.Calls = 0;
        world.ActiveRoleCodes.OnRead = null;
        world.Store.OnFindDraft = () => world.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport);

        var detail = await world.Service().GetDraftDetailAsync(created.Draft.Id);

        Assert.True(detail.IsOwnDraft);
        Assert.True(detail.IsOwnDecisionRestricted);
    }

    [Fact]
    public async Task Detail_uses_the_post_read_persona_when_sales_support_is_removed_during_the_read()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-detail-remove"))).Detail;
        world.ActiveRoleCodes.Codes.Add(ErpPersonaRoleCodes.SalesSupport);
        world.ActiveRoleCodes.Calls = 0;
        world.ActiveRoleCodes.OnRead = null;
        world.Store.OnFindDraft = () => world.ActiveRoleCodes.Codes.Remove(ErpPersonaRoleCodes.SalesSupport);

        var detail = await world.Service().GetDraftDetailAsync(created.Draft.Id);

        Assert.True(detail.IsOwnDraft);
        Assert.False(detail.IsOwnDecisionRestricted);
    }

    [Fact]
    public async Task A_revision_without_the_price_permission_is_403_before_the_draft_is_read_and_a_pending_draft_is_the_keys_terminal_409()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"))).Detail;
        var (a, b) = (created.Lines[0].LineId, created.Lines[1].LineId);

        world.Authority.Denied.Add(PricePermission);
        var forbidden = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision("3", new SalesDraftLineCommercialV1(a, "1", KgUnit, null), new SalesDraftLineCommercialV1(b)), Metadata("k-r1")));
        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
        world.Authority.Denied.Clear();

        // The 422 names the line by its index in THIS request (the second draft line sent first).
        var scale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision("3", new SalesDraftLineCommercialV1(b, "1.005", KgUnit, null), new SalesDraftLineCommercialV1(a)), Metadata("k-r2")));
        Assert.Equal((422, "lines[0].unitPrice", SalesRuleCodes.PriceScaleExceeded), (scale.Status, Assert.Single(scale.Errors).Field, Assert.Single(scale.Errors).Code));
        var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision("3", new SalesDraftLineCommercialV1(a, "1", KgUnit, null)), Metadata("k-r3")));
        Assert.Equal((422, "lines", SalesRuleCodes.RevisionLinesMismatch), (mismatch.Status, Assert.Single(mismatch.Errors).Field, Assert.Single(mismatch.Errors).Code));

        // Pending: decide first. The refusal is terminal for the key and replays with the first correlation.
        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("3"), Metadata("k-submit"));
        var pending = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision("4", new SalesDraftLineCommercialV1(a, "1", KgUnit, null), new SalesDraftLineCommercialV1(b)), Metadata("k-r4")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, false), (pending.Status, pending.Code, pending.IsReplay));
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision("4", new SalesDraftLineCommercialV1(a, "1", KgUnit, null), new SalesDraftLineCommercialV1(b)), Metadata("k-r4")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, true, "synthetic-correlation"), (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));
        // Malformed version and unknown draft answer as the state commands do.
        Assert.Equal(400, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id, Revision("x", new SalesDraftLineCommercialV1(a)), Metadata("k-r5")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(Guid.NewGuid(), Revision("1", new SalesDraftLineCommercialV1(a)), Metadata("k-r6")))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftRevisionsAsync(Guid.NewGuid()))).Status);
    }

    [Theory]
    [InlineData("not-found", 409, "account_state", "invalid_reference")]
    [InlineData("passive-account", 409, "account_state", "passive_company_account")]
    [InlineData("passive-party", 409, "account_state", "passive_company_account")]
    [InlineData("other-company", 409, "account_state", "company_mismatch")]
    [InlineData("denied", 403, "forbidden", null)]
    [InlineData("unavailable", 503, "counterparty_provider_unavailable", null)]
    public async Task The_customer_is_the_Cari_owners_answer(string fault, int status, string code, string? fieldCode)
    {
        var world = new World { Accounts = { Fault = fault } };

        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));

        Assert.Equal((status, code), (error.Status, error.Code));
        if (fieldCode is not null) Assert.Contains(error.Errors, item => item.Code == fieldCode);
        // Refused before the money profile or any product is read; nothing recorded.
        Assert.Equal((0, 0, 0), (world.Settings.Calls, world.Products.Calls, world.Store.Refusals.Count));
    }

    // ---- SALES-A2a: the delivery address and the contact are the Cari owner's -------------------

    [Fact]
    public async Task A_delivery_address_and_a_contact_the_Cari_owner_lists_are_stored_with_the_owners_versions()
    {
        var world = new World();
        var request = Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(AddressId, AddressVersion), Contact = new(ContactId, ContactVersion) };

        var response = await world.Service().CreateDraftAsync(request, Metadata("k-1"));

        Assert.Equal(new SalesDraftOwnerReferenceV1(AddressId, AddressVersion), response.Detail.DeliveryAddress);
        Assert.Equal(new SalesDraftOwnerReferenceV1(ContactId, ContactVersion), response.Detail.Contact);
        Assert.Null(response.Detail.Responsible);
        // Read once, for this party and account, after the account answered and before the money profile.
        Assert.Equal((1, 1), (world.Accounts.Calls, world.ContactInformation.Calls));
        Assert.Equal((Party, Account), world.ContactInformation.Asked!.Value);
        Assert.Single(world.Store.Envelopes);
        Assert.Empty(world.Store.Refusals);

        // The stored draft reopens with the same identities and versions; nothing of the address text is kept.
        var detail = await world.Service().GetDraftDetailAsync(response.Detail.Draft.Id);
        Assert.Equal((response.Detail.DeliveryAddress, response.Detail.Contact), (detail.DeliveryAddress, detail.Contact));

        // One of the two alone is fine as well: no reference is not an error, and the missing one stays null.
        var only = await world.Service().CreateDraftAsync(Request() with { Contact = new(ContactId, ContactVersion) }, Metadata("k-2"));
        Assert.Null(only.Detail.DeliveryAddress);
        Assert.Equal(new SalesDraftOwnerReferenceV1(ContactId, ContactVersion), only.Detail.Contact);
    }

    [Theory]
    [InlineData("unknown-address", "deliveryAddress")]
    [InlineData("inactive-address", "deliveryAddress")]
    [InlineData("unknown-contact", "contact")]
    [InlineData("inactive-contact", "contact")]
    public async Task A_reference_the_Cari_owner_does_not_list_or_lists_inactive_is_refused_and_nothing_is_recorded(string variant, string field)
    {
        var world = new World();
        var request = variant switch
        {
            "unknown-address" => Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(Guid.NewGuid(), "1"), Contact = new(ContactId, ContactVersion) },
            "inactive-address" => Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(InactiveAddressId, "2") },
            "unknown-contact" => Request() with { Contact = new SalesDraftOwnerReferenceV1(Guid.NewGuid(), "1") },
            _ => Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(AddressId, AddressVersion), Contact = new(InactiveContactId, "3") },
        };

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1")));

        Assert.Equal((422, SalesDraftProblemCodesV1.OwnerReferenceInvalid, false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Equal((field, SalesDraftProblemCodesV1.OwnerReferenceInvalid), (Assert.Single(refusal.Errors).Field, refusal.Errors[0].Code));
        // A non-terminal owner refusal: not recorded, the money profile and the products never asked, nothing stored.
        Assert.Equal((0, 0, 0, 0), (world.Settings.Calls, world.Products.Calls, world.Store.Refusals.Count, world.Store.Envelopes.Count));
    }

    [Fact]
    public async Task A_reference_at_another_version_than_the_owners_is_the_terminal_refusal_recorded_and_replayed()
    {
        var world = new World();
        var request = Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(AddressId, "4"), Contact = new(ContactId, ContactVersion) };

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, false), (refusal.Status, refusal.Code, refusal.IsReplay));
        Assert.Equal(("deliveryAddress", SalesDraftProblemCodesV1.VersionConflict), (Assert.Single(refusal.Errors).Field, refusal.Errors[0].Code));
        var recorded = Assert.Single(world.Store.Refusals);
        Assert.Equal(("k-1", 409), (recorded.Envelope.IdempotencyKey, recorded.Refusal.Status));
        Assert.Equal((0, 0), (world.Settings.Calls, world.Store.Envelopes.Count));
        world.ResetOwnerCalls();

        // ADR-011 §7: the same key, content and actor answers the recorded refusal again, without any owner.
        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true, "synthetic-correlation"),
            (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));
        Assert.Equal((0, 0), (world.Accounts.Calls, world.ContactInformation.Calls));

        // An unlisted reference beside a stale one is the 422 (the owner must know the item before its version matters).
        var mixed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(AddressId, "4"), Contact = new(Guid.NewGuid(), "1") }, Metadata("k-2")));
        Assert.Equal((422, SalesDraftProblemCodesV1.OwnerReferenceInvalid, "contact"), (mixed.Status, mixed.Code, Assert.Single(mixed.Errors).Field));
        Assert.Single(world.Store.Refusals);
    }

    [Fact]
    public async Task A_stale_reference_record_that_loses_its_race_answers_the_winners_recorded_refusal_as_a_replay()
    {
        // ADR-011 §8, the product race test's mirror: the winner recorded the same key's refusal first.
        var world = new World { Store = { Race = "refusal-at-record" } };
        var request = Request() with { Contact = new SalesDraftOwnerReferenceV1(ContactId, "7") };

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1", correlation: "loser")));

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true, "winner-correlation"),
            (refusal.Status, refusal.Code, refusal.IsReplay, refusal.OriginalCorrelationId));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData("port-missing", 503, "counterparty_provider_unavailable", null)]
    [InlineData("unavailable", 503, "counterparty_provider_unavailable", null)]
    [InlineData("denied", 403, "forbidden", null)]
    [InlineData("not-found", 409, "account_state", "invalid_reference")]
    [InlineData("other-company", 503, "reference_provider_invalid", null)]
    [InlineData("other-account", 503, "reference_provider_invalid", null)]
    [InlineData("malformed-version", 503, "reference_provider_invalid", null)]
    public async Task The_contact_information_is_the_Cari_owners_answer(string fault, int status, string code, string? fieldCode)
    {
        var world = new World { ContactInformation = { Fault = fault } };
        if (fault == "port-missing") world.NoContactInformation = true;
        var request = Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(AddressId, AddressVersion) };

        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1")));

        Assert.Equal((status, code), (error.Status, error.Code));
        if (fieldCode is not null) Assert.Contains(error.Errors, item => item.Code == fieldCode);
        // The account was asked first; nothing after the contact information; nothing recorded.
        Assert.Equal((1, 0, 0, 0), (world.Accounts.Calls, world.Settings.Calls, world.Products.Calls, world.Store.Refusals.Count));
    }

    [Theory]
    [InlineData("money-not-found", 422, "unsupported_currency")]
    [InlineData("money-access-denied", 403, "settings_access_denied")]
    [InlineData("money-unavailable", 503, "settings_provider_unavailable")]
    [InlineData("money-other-company", 503, "reference_provider_invalid")]
    [InlineData("money-not-in-effect", 422, "validation_failed")]
    public async Task The_money_profile_is_the_Settings_owners_answer_for_the_sales_operation(string fault, int status, string code)
    {
        var world = new World { Settings = { Fault = fault } };

        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));

        Assert.Equal((status, code), (error.Status, error.Code));
        Assert.Equal((SalesMoneyOperations.OrderDraftV1, Currency, Day), world.Settings.MoneyAsked!.Value);
        if (fault == "money-not-in-effect")
        {
            // The domain's rule, not the port's: the profile came back, but not in effect on the business date.
            Assert.Equal("sales_money_profile_not_in_effect", Assert.Single(error.Errors).Code);
        }
    }

    [Theory]
    [InlineData("port-missing", 503, "product_eligibility_unavailable")]
    [InlineData("unavailable", 503, "product_eligibility_unavailable")]
    [InlineData("field-errors", 422, "unsupported_product")]
    [InlineData("not-found", 422, "unsupported_product")]
    [InlineData("other-unit", 422, "unsupported_unit")]
    [InlineData("unit-not-found", 422, "unsupported_unit")]
    [InlineData("unit-other-company", 503, "reference_provider_invalid")]
    public async Task The_product_and_its_order_unit_are_the_owners_answers(string fault, int status, string code)
    {
        var world = new World();
        if (fault == "port-missing") world.NoProducts = true;
        else if (fault.StartsWith("unit", StringComparison.Ordinal)) world.Settings.Fault = fault;
        else world.Products.Fault = fault;

        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));

        Assert.Equal((status, code), (error.Status, error.Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task A_stale_product_version_is_recorded_as_the_terminal_refusal_and_nothing_else_is_stored()
    {
        var world = new World { Products = { Fault = "conflict-exception" } };

        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (error.Status, error.Code));
        var recorded = Assert.Single(world.Store.Refusals);
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (recorded.Refusal.Status, recorded.Refusal.Code));
        Assert.Equal("k-1", recorded.Envelope.IdempotencyKey);
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public async Task Store_refusals_keep_their_public_meaning()
    {
        var world = new World { Store = { Fault = SalesDraftStoreCodes.SameKeyPayloadMismatch } };
        var mismatch = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (mismatch.Status, mismatch.Code));

        world.Store.Fault = SalesDraftStoreCodes.ConcurrentChange;
        var race = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-2")));
        Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange), (race.Status, race.Code));

        world.Store.Fault = SalesDraftStoreCodes.InvalidCursor;
        var cursor = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftListAsync(new SalesOrderDraftListQuery(10, "x")));
        Assert.Equal((400, SalesDraftProblemCodesV1.InvalidCursor), (cursor.Status, cursor.Code));
    }

    [Fact]
    public async Task Detail_and_list_read_under_the_read_permission_and_another_companys_draft_is_404()
    {
        var world = new World();
        var created = await world.Service().CreateDraftAsync(Request(lines: 2), Metadata("k-1"));

        var detail = await world.Service().GetDraftDetailAsync(created.Detail.Draft.Id);
        Assert.Equal(created.Detail.Draft with { ProductSnapshots = null }, detail.Draft with { ProductSnapshots = null });
        Assert.Equal(created.Detail.Draft.ProductSnapshots, detail.Draft.ProductSnapshots);
        Assert.Equal(created.Detail.Lines.Select(line => line.LineId), detail.Lines.Select(line => line.LineId));
        // The read's own fence is re-measured AFTER the read (#276); K3 P2 then asks the price READ last, fresh, to decide the
        // disclosure — so the read name is the second-to-last measurement and the disclosure ask is the last.
        Assert.Equal(ReadPermission, world.Authority.Measured[^2]);
        Assert.Equal(PriceReadPermission, world.Authority.LastPermission);

        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDetailAsync(Guid.NewGuid()));
        Assert.Equal(404, missing.Status);

        var page = await world.Service().GetDraftListAsync(new SalesOrderDraftListQuery());
        var listedDraft = Assert.Single(page.Items);
        Assert.Equal(created.Detail.Draft with { ProductSnapshots = null }, listedDraft with { ProductSnapshots = null });
        Assert.Equal(created.Detail.Draft.ProductSnapshots, listedDraft.ProductSnapshots);
        var listedProduct = Assert.Single(Assert.Single(page.Items).ProductSnapshots!);
        Assert.Equal((Product, "7", "GDC-001", "Sentetik bant", KgUnit, "kg"),
            (listedProduct.ProductId, listedProduct.ProductVersion, listedProduct.ProductCode,
                listedProduct.ProductName, listedProduct.OrderUnitId, listedProduct.OrderUnitCode));
        Assert.Null(page.NextCursor);

        var tooBig = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftListAsync(new SalesOrderDraftListQuery(101)));
        Assert.Equal((400, "validation_failed"), (tooBig.Status, tooBig.Code));
    }

    // ---- SALES-A1: the internal approval commands --------------------------------------------

    private const string ApprovePermission = ErpPermissions.CounterpartyPartiesRead;

    private static SalesDraftStateCommandRequest Command(string version, string? reason = null) => new(version, reason);

    [Fact]
    public async Task A_draft_is_submitted_approved_and_answered_after_each_transition_with_its_audit_facts()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail.Draft;
        Assert.Equal(("Preparing", "2"), (created.ApprovalState, created.Version));

        var submitted = await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-submit"));
        Assert.False(submitted.IsReplay);
        Assert.Equal(("PendingApproval", "3", Actor), (submitted.Detail.Draft.ApprovalState, submitted.Detail.Draft.Version, submitted.Detail.Approval.SubmittedBy));
        Assert.NotNull(submitted.Detail.Approval.SubmittedAtUtc);
        Assert.Null(submitted.Detail.Approval.DecidedBy);
        // The submission runs under the create permission; the envelope and the audit facts say so.
        var submission = Assert.Single(world.Store.Transitions);
        Assert.Equal((SalesDraftCommands.SubmitV1, CreatePermission, "sales.order-draft.submitted.v1", "Submit", 2L, 3L),
            (submission.Command, submission.Envelope.PermissionCode, submission.Audit.EventType, submission.Audit.Operation, submission.VersionBefore, submission.VersionAfter));

        var approved = await world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-approve"));
        Assert.Equal(("Approved", "4", Actor, Actor), (approved.Detail.Draft.ApprovalState, approved.Detail.Draft.Version,
            approved.Detail.Approval.SubmittedBy, approved.Detail.Approval.DecidedBy));
        Assert.Null(approved.Detail.Approval.RejectionReason);
        Assert.Equal((SalesDraftCommands.ApproveV1, ApprovePermission, "sales.order-draft.approved.v1", "Approve"),
            (world.Store.Transitions[1].Command, world.Store.Transitions[1].Envelope.PermissionCode, world.Store.Transitions[1].Audit.EventType, world.Store.Transitions[1].Audit.Operation));
        // The approval's fence is re-measured after the transition; K3 P2 then asks the price READ last to disclose the response.
        Assert.Equal(ApprovePermission, world.Authority.Measured[^2]);
        Assert.Equal(PriceReadPermission, world.Authority.LastPermission);

        // The list shows the axis and narrows to it; the detail carries the facts.
        var pending = await world.Service().GetDraftListAsync(new SalesOrderDraftListQuery(ApprovalState: "PendingApproval"));
        Assert.Empty(pending.Items);
        var approvedList = await world.Service().GetDraftListAsync(new SalesOrderDraftListQuery(ApprovalState: "Approved"));
        Assert.Equal("Approved", Assert.Single(approvedList.Items).ApprovalState);
        var detail = await world.Service().GetDraftDetailAsync(created.Id);
        Assert.Equal(("Approved", Actor), (detail.Draft.ApprovalState, detail.Approval.DecidedBy));
        var badFilter = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftListAsync(new SalesOrderDraftListQuery(ApprovalState: "approved")));
        Assert.Equal((400, "validation_failed"), (badFilter.Status, badFilter.Code));
    }

    [Fact]
    public async Task Customer_acceptance_replays_once_and_a_commercial_revision_requires_a_fresh_acceptance()
    {
        var world = new World
        {
            // Central registers the production name in its own composition change. This service proof binds the
            // new operation to an already-known test permission without creating a role, grant or admin exception.
            Bindings = new SalesDraftServiceBindings(CreatePermission, ReadPermission, ApprovePermission, PricePermission,
                PriceReadPermission, SchedulePermission, CancelPermission, QuantityPermission, ApprovePermission),
        };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail;
        Assert.Equal((true, (string?)null, false), (created.CustomerAcceptance.Required,
            created.CustomerAcceptance.AcceptedRevision, created.CustomerAcceptance.IsCurrentRevisionAccepted));
        Assert.Equal(SalesCommercialStageV1.Quote, created.Draft.CommercialStage);

        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("2"), Metadata("k-submit"));
        await world.Service().ApproveDraftAsync(created.Draft.Id, Command("3"), Metadata("k-approve"));
        var accepted = await world.Service().RecordCustomerAcceptanceAsync(created.Draft.Id, Command("4"), Metadata("k-accept"));
        Assert.Equal(("5", SalesDraftApiV1.Rev0, Actor, true), (accepted.Detail.Draft.Version,
            accepted.Detail.CustomerAcceptance.AcceptedRevision, accepted.Detail.CustomerAcceptance.AcceptedBy,
            accepted.Detail.CustomerAcceptance.IsCurrentRevisionAccepted));
        Assert.Equal(SalesCommercialStageV1.Order, accepted.Detail.Draft.CommercialStage);
        Assert.Equal((SalesDraftCommands.RecordCustomerAcceptanceV1, ApprovePermission,
                "sales.order-draft.customer-acceptance-recorded.v1"),
            (world.Store.Transitions[^1].Command, world.Store.Transitions[^1].Envelope.PermissionCode,
                world.Store.Transitions[^1].Audit.EventType));

        var replay = await world.Service().RecordCustomerAcceptanceAsync(created.Draft.Id, Command("4"),
            Metadata("k-accept", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal(("synthetic-correlation", "5", true), (replay.OriginalCorrelationId,
            replay.Detail.Draft.Version, replay.Detail.CustomerAcceptance.IsCurrentRevisionAccepted));
        Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.RecordCustomerAcceptanceV1);

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision("5", new SalesDraftLineCommercialV1(created.Lines[0].LineId, "13.00", KgUnit, "5")), Metadata("k-r1"));
        Assert.Equal(("6", "Rev.1", SalesDraftApiV1.Rev0, false), (revised.Detail.Draft.Version, revised.Detail.Draft.Revision,
            revised.Detail.CustomerAcceptance.AcceptedRevision, revised.Detail.CustomerAcceptance.IsCurrentRevisionAccepted));
        Assert.Equal(SalesCommercialStageV1.Quote, revised.Detail.Draft.CommercialStage);

        await world.Service().SubmitDraftAsync(created.Draft.Id, Command("6"), Metadata("k-submit-r1"));
        await world.Service().ApproveDraftAsync(created.Draft.Id, Command("7"), Metadata("k-approve-r1"));
        var acceptedRevision = await world.Service().RecordCustomerAcceptanceAsync(created.Draft.Id, Command("8"), Metadata("k-accept-r1"));
        Assert.Equal(("9", "Rev.1", true), (acceptedRevision.Detail.Draft.Version,
            acceptedRevision.Detail.CustomerAcceptance.AcceptedRevision,
            acceptedRevision.Detail.CustomerAcceptance.IsCurrentRevisionAccepted));
        Assert.Equal(SalesCommercialStageV1.Order, acceptedRevision.Detail.Draft.CommercialStage);
        Assert.Equal(2, world.Store.Transitions.Count(item => item.Command == SalesDraftCommands.RecordCustomerAcceptanceV1));
    }

    [Fact]
    public async Task A_rejection_carries_its_reason_returns_the_draft_to_the_preparer_and_a_resubmission_is_allowed()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail.Draft;
        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-submit"));

        var rejected = await world.Service().RejectDraftAsync(created.Id, Command("3", "  SENTETIK neden  "), Metadata("k-reject"));
        Assert.Equal(("Rejected", "4", "SENTETIK neden"), (rejected.Detail.Draft.ApprovalState, rejected.Detail.Draft.Version, rejected.Detail.Approval.RejectionReason));
        Assert.Equal("sales.order-draft.rejected.v1", world.Store.Transitions[^1].Audit.EventType);

        // No reason is "no reason", never a default text; the same Rev.0 goes back for approval.
        var again = await world.Service().SubmitDraftAsync(created.Id, Command("4"), Metadata("k-submit-2"));
        Assert.Equal(("PendingApproval", "5", SalesDraftApiV1.Rev0), (again.Detail.Draft.ApprovalState, again.Detail.Draft.Version, again.Detail.Draft.Revision));
        Assert.Null(again.Detail.Approval.DecidedBy);
        Assert.Null(again.Detail.Approval.RejectionReason);

        var tooLong = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().RejectDraftAsync(created.Id, Command("5", new string('x', SalesOrderDraft.MaximumRejectionReasonLength + 1)), Metadata("k-reject-2")));
        Assert.Equal((422, SalesDraftProblemCodesV1.ApprovalReasonTooLong, "reason"), (tooLong.Status, tooLong.Code, tooLong.Errors[0].Field));
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task A_wrong_state_and_a_stale_version_are_terminal_for_the_key_and_replay_with_the_first_correlation()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail.Draft;

        // Approving a draft that was never submitted: 409 sales_state_conflict, recorded under the key.
        var wrongState = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDraftAsync(created.Id, Command("2"), Metadata("k-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, false, "approvalState"), (wrongState.Status, wrongState.Code, wrongState.IsReplay, wrongState.Errors[0].Field));
        var recorded = Assert.Single(world.Store.Refusals);
        Assert.Equal(("k-approve", 409, SalesDraftProblemCodesV1.StateConflict), (recorded.Envelope.IdempotencyKey, recorded.Refusal.Status, recorded.Refusal.Code));
        var replayed = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDraftAsync(created.Id, Command("2"), Metadata("k-approve", correlation: "retry")));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict, true, "synthetic-correlation"), (replayed.Status, replayed.Code, replayed.IsReplay, replayed.OriginalCorrelationId));
        // The same key with another version is another content: one 409, nothing disclosed.
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (other.Status, other.Code));

        // A stale version: 409 version_conflict, terminal too; the draft did not move.
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Id, Command("1"), Metadata("k-stale")));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, "expectedVersion"), (stale.Status, stale.Code, stale.Errors[0].Field));
        Assert.Equal(2, world.Store.Refusals.Count);
        Assert.Equal("Preparing", (await world.Service().GetDraftDetailAsync(created.Id)).Draft.ApprovalState);
        Assert.Empty(world.Store.Transitions);
    }

    [Fact]
    public async Task The_same_key_answers_the_stored_transition_again_and_a_lost_race_answers_the_winner()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail.Draft;
        var first = await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-submit"));
        var replay = await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-submit", correlation: "second"));
        Assert.True(replay.IsReplay);
        Assert.Equal((first.Detail.Draft.Id, "synthetic-correlation", "PendingApproval"), (replay.Detail.Draft.Id, replay.OriginalCorrelationId, replay.Detail.Draft.ApprovalState));
        Assert.Single(world.Store.Transitions);

        // Another key's transition took the draft's version first: the caller's concurrent_change, nothing recorded.
        world.Store.Race = "version-at-store";
        var raced = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-approve")));
        Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange), (raced.Status, raced.Code));
        Assert.Empty(world.Store.Refusals);
    }

    [Theory]
    [InlineData("not-granted", 403)]
    [InlineData("site-scoped", 403)]
    [InlineData("unbound-approve", 503)]
    [InlineData("unauthenticated", 401)]
    public async Task An_approval_without_the_approve_authority_is_refused_before_the_draft_is_read(string fault, int status)
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-create"))).Detail.Draft;
        await world.Service().SubmitDraftAsync(created.Id, Command("2"), Metadata("k-submit"));
        world.Store.OnStore = () => throw new InvalidOperationException("the store must not be reached");
        switch (fault)
        {
            case "not-granted": world.Authority.Granted = false; break;
            case "site-scoped": world.Authority.CompanyWide = false; break;
            case "unbound-approve": world.Bindings = new SalesDraftServiceBindings(CreatePermission, ReadPermission, null, PriceReadPermission: PriceReadPermission); break;
            default: world.User.Authenticated = false; break;
        }

        var error = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveDraftAsync(created.Id, Command("3"), Metadata("k-approve")));

        Assert.Equal(status, error.Status);
        if (status == 503) Assert.Equal(SalesDraftProblemCodesV1.PermissionUnregistered, error.Code);
        Assert.Single(world.Store.Transitions);
    }

    [Fact]
    public async Task A_state_command_on_an_unknown_or_another_companys_draft_is_404_and_a_malformed_version_is_400()
    {
        var world = new World();
        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(Guid.NewGuid(), Command("1"), Metadata("k-1")));
        Assert.Equal(404, missing.Status);
        var empty = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(Guid.Empty, Command("1"), Metadata("k-2")));
        Assert.Equal(404, empty.Status);
        var overflow = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(Guid.NewGuid(), Command("99999999999999999999"), Metadata("k-3")));
        Assert.Equal((400, "validation_failed"), (overflow.Status, overflow.Code));
        Assert.Empty(world.Store.Refusals);
        Assert.Empty(world.Store.Transitions);
    }

    // ---- request ----------------------------------------------------------------------------

    [Theory]
    [InlineData("2.9999999999999999999999999999999", "line")]
    [InlineData("1.00000000000000000000000000000001", "line")]
    [InlineData("2.9999999999999999999999999999999", "slice")]
    [InlineData("1.00000000000000000000000000000001", "slice")]
    public async Task A_quantity_text_longer_than_a_decimal_holds_is_refused_on_the_text_and_never_rounded(string text, string where)
    {
        // FİNANS #350 P2-1: decimal.Parse would round these past 28 digits (to 3 and 1) and the verbatim text would be stored beside
        // the rounded value; the text is refused before any conversion, on the line and on a delivery slice alike.
        var world = new World();
        var request = RequestWithQuantity(where == "line" ? text : "1", where == "slice" ? text : null);

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-long")));

        Assert.Equal((422, "validation_failed", "quantity", "sales_quantity_scale_exceeded"),
            (refusal.Status, refusal.Code, refusal.Errors[0].Field, refusal.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);
        Assert.Empty(world.Store.Refusals);
    }

    [Fact]
    public async Task A_quantity_with_more_than_fifteen_integer_digits_does_not_fit_the_column_and_leading_zeros_do_not_count()
    {
        var world = new World();
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateDraftAsync(RequestWithQuantity("1234567890123456", null), Metadata("k-wide")));
        Assert.Equal((422, "validation_failed", "quantity", "sales_value_does_not_fit"),
            (refused.Status, refused.Code, refused.Errors[0].Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Envelopes);

        var created = await world.Service().CreateDraftAsync(RequestWithQuantity("000000000000000001.250", null), Metadata("k-zeros"));
        Assert.Equal("000000000000000001.250", created.Detail.Lines[0].Quantity);
    }

    private static CreateSalesOrderDraftRequest RequestWithQuantity(string lineQuantity, string? sliceQuantity)
    {
        var line = new SalesOrderDraftLineRequest(Guid.NewGuid(), Product, "7", KgUnit, lineQuantity,
            RequestedDeliverySlices: sliceQuantity is null ? null : [new(Guid.NewGuid(), KgUnit, sliceQuantity, Day.AddDays(1))]);
        return new CreateSalesOrderDraftRequest("Order", Party, Account, Day, Currency, [line]);
    }

    private static CreateSalesOrderDraftRequest Request(int lines = 1)
    {
        var list = Enumerable.Range(0, lines).Select(index => new SalesOrderDraftLineRequest(Guid.NewGuid(), Product, "7", KgUnit,
            (1.25m + index).ToString(CultureInfo.InvariantCulture),
            RequestedDeliverySlices: index == 0
                ? [new(Guid.NewGuid(), KgUnit, "0.5", Day.AddDays(1)), new(Guid.NewGuid(), KgUnit, "0.5", Day.AddDays(2))]
                : null)).ToArray();
        return new CreateSalesOrderDraftRequest("Order", Party, Account, Day, Currency, list);
    }

    private static SalesCommandMetadata Metadata(string key, string? correlation = null) =>
        new(key, correlation ?? "synthetic-correlation", null, new DateTimeOffset(2026, 9, 18, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Enriched_list_and_detail_read_Cari_display_once_per_read_and_preserve_the_Sales_order()
    {
        var world = new World();
        var partyIds = Enumerable.Range(1, 100)
            .Select(index => Guid.Parse($"b7000000-0000-4000-8000-{index:D12}"))
            .ToArray();

        foreach (var partyId in partyIds)
        {
            await world.Service().CreateDraftAsync(Request() with { PartyId = partyId }, Metadata($"k-{partyId:N}"));
        }

        world.PartyDisplays.Reverse = true;
        var list = await world.ReadService().GetDraftListAsync(new SalesOrderDraftListQuery(100));

        Assert.Equal(100, list.Items.Count);
        Assert.Equal(1, world.PartyDisplays.Calls);
        Assert.Equal(100, world.PartyDisplays.Asked.Distinct().Count());
        Assert.Equal((Company, Actor), (world.PartyDisplays.CompanyId, world.PartyDisplays.ActorId));
        Assert.All(list.Items, item =>
        {
            Assert.Equal(item.PartyId, item.PartyDisplay.PartyId);
            Assert.Equal($"CAR-{item.PartyId:N}", item.PartyDisplay.Code);
            Assert.Equal($"Legal {item.PartyId:N}", item.PartyDisplay.LegalName);
            Assert.Equal($"Trade {item.PartyId:N}", item.PartyDisplay.TradeName);
        });

        var first = list.Items[0];
        var detail = await world.ReadService().GetDraftDetailAsync(first.Id);
        Assert.Equal(2, world.PartyDisplays.Calls);
        Assert.Equal(first.PartyId, detail.PartyDisplay.PartyId);
        Assert.Equal(first.PartyDisplay, detail.PartyDisplay);
    }

    [Theory]
    [InlineData("denied", 403, "forbidden")]
    [InlineData("unavailable", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    [InlineData("missing", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    [InlineData("duplicate", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    [InlineData("mismatch", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    [InlineData("malformed", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    [InlineData("null", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    [InlineData("throw", 503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable)]
    public async Task Enriched_Cari_display_failures_refuse_the_entire_read_without_a_partial_response(
        string fault, int status, string code)
    {
        var world = new World();
        var created = await world.Service().CreateDraftAsync(Request(), Metadata("k-display-failure"));
        world.PartyDisplays.Fault = fault;

        var detail = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.ReadService().GetDraftDetailAsync(created.Detail.Draft.Id));
        Assert.Equal((status, code), (detail.Status, detail.Code));

        var list = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.ReadService().GetDraftListAsync(new SalesOrderDraftListQuery()));
        Assert.Equal((status, code), (list.Status, list.Code));
    }

    [Fact]
    public async Task Enriched_read_refuses_data_when_Sales_authority_is_lost_during_the_Cari_read()
    {
        var world = new World();
        var created = await world.Service().CreateDraftAsync(Request(), Metadata("k-late-loss"));
        world.PartyDisplays.OnRead = () => world.Authority.Granted = false;

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.ReadService().GetDraftDetailAsync(created.Detail.Draft.Id));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Equal(1, world.PartyDisplays.Calls);
    }

    // ---- synthetic world --------------------------------------------------------------------

    private sealed class World
    {
        public FixedUser User { get; } = new();
        public FixedAuthority Authority { get; } = new();
        public FakeAccounts Accounts { get; init; } = new();
        public FakePartyDisplays PartyDisplays { get; init; } = new();
        public FakeContactInformation ContactInformation { get; init; } = new();
        public FakeSettings Settings { get; init; } = new();
        public FakeProducts Products { get; init; } = new();
        public FakeStore Store { get; init; } = new();
        public bool NoProducts { get; set; }
        public bool NoContactInformation { get; set; }
        /// <summary>AT-024: the delivery owner's read port; null = not composed.</summary>
        public ISalesOrderDeliveryReadPortV1? Deliveries { get; set; }
        /// <summary>DEC-SAL-005: the allocation owner's read port (Inventory); null = not composed.</summary>
        public ISalesOrderAllocationReadPortV1? Allocations { get; set; }
        /// <summary>DEC-SAL-005: the loading owner's read port (Logistics); null = not composed.</summary>
        public ISalesOrderLoadingReadPortV1? Loadings { get; set; }
        /// <summary>AT-018: the Product price-list reference read; null = not composed.</summary>
        public IProductQuantityPriceReferenceReaderV1? PriceReferences { get; set; }
        public FixedActiveRoleCodeReader ActiveRoleCodes { get; } = new();
        public SalesDraftServiceBindings Bindings { get; set; } = new(CreatePermission, ReadPermission, ApprovePermission, PricePermission, PriceReadPermission, SchedulePermission,
            CancelPermission, QuantityPermission);

        public SalesDraftService Service()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new SalesDraftService(company, User, Authority, ActiveRoleCodes, new FixedCorrelation(), Accounts, Settings,
                NoProducts ? null : Products, Store, Bindings, TimeProvider.System, NoContactInformation ? null : ContactInformation, Deliveries,
                Allocations, Loadings, PriceReferences);
        }

        public SalesDraftReadServiceV1 ReadService()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new SalesDraftReadServiceV1(Service(), PartyDisplays, company, User, Authority, Bindings);
        }

        public void ResetOwnerCalls()
        {
            Accounts.Calls = 0;
            ContactInformation.Calls = 0;
            Settings.Calls = 0;
            Products.Calls = 0;
        }
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool Authenticated { get; set; } = true;
        public Guid Company { get; set; } = SalesDraftServiceTests.Company;
        /// <summary>D2: the acting identity; another actor in the same world is the same user fake with another id.</summary>
        public string Id { get; set; } = Actor;
        public bool IsAuthenticated => Authenticated;
        public string? UserId => Authenticated ? Id : null;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedActiveRoleCodeReader : IActiveRoleCodeReader
    {
        public HashSet<string> Codes { get; } = new(StringComparer.Ordinal);
        public int Calls { get; set; }
        public Action<int>? OnRead { get; set; }

        public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            OnRead?.Invoke(Calls);
            return Task.FromResult<IReadOnlySet<string>>(companyId == Company ? Codes : new HashSet<string>(StringComparer.Ordinal));
        }
    }

    private sealed class FixedAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public bool CompanyWide { get; set; } = true;
        public int Calls { get; private set; }
        public string? LastPermission { get; private set; }
        /// <summary>Names never granted (K3: the separate price permission is measured on its own).</summary>
        public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);
        /// <summary>A name granted at first and lost from the given measurement on (the late-loss fence).</summary>
        public (string Permission, int AfterCall)? LostLater { get; set; }
        public Action<string, int>? OnRead { get; set; }
        public List<string> Measured { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPermission = permission;
            Measured.Add(permission);
            var granted = Granted && !Denied.Contains(permission)
                && !(LostLater is { } lost && lost.Permission == permission && Calls > lost.AfterCall);
            OnRead?.Invoke(permission, Calls);
            return Task.FromResult(new PermissionScope(granted, CompanyWide, new HashSet<string>(["ROLE-B", "ROLE-A"], StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "synthetic-correlation";
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    private sealed class FakeAccounts : ISalesPartyAccountPortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }

        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Fault switch
            {
                "not-found" => new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.NotFound, null),
                "denied" => new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.Denied, null),
                "unavailable" => new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.Unavailable, null),
                _ => new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.Found, new SalesPartyAccountV1(partyId, companyAccountId,
                    Fault == "other-company" ? Guid.NewGuid() : Company, Fault != "passive-party", Fault != "passive-account", "3", "2")),
            });
        }
    }

    private sealed class FakePartyDisplays : ISalesPartyDisplaySourceV1
    {
        public string Fault { get; set; } = "";
        public bool Reverse { get; set; }
        public Action? OnRead { get; set; }
        public int Calls { get; private set; }
        public Guid CompanyId { get; private set; }
        public string ActorId { get; private set; } = "";
        public IReadOnlyList<Guid> Asked { get; private set; } = [];

        public Task<SalesPartyDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
            SalesPartyDisplayBatchRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            CompanyId = request.CompanyId;
            ActorId = request.ActorId;
            Asked = [.. request.PartyIds];
            OnRead?.Invoke();
            if (Fault == "throw") throw new InvalidOperationException("synthetic provider failure");
            if (Fault == "null") return Task.FromResult<SalesPartyDisplayBatchAnswerV1>(null!);
            if (Fault == "denied")
                return Task.FromResult(new SalesPartyDisplayBatchAnswerV1(SalesPartyDisplayBatchOutcomeV1.Denied, []));
            if (Fault == "unavailable")
                return Task.FromResult(new SalesPartyDisplayBatchAnswerV1(SalesPartyDisplayBatchOutcomeV1.Unavailable, []));

            var items = request.PartyIds.Select(id => new SalesPartyDisplayV1(
                id,
                Fault == "malformed" ? " " : $"CAR-{id:N}",
                $"Legal {id:N}",
                $"Trade {id:N}"))
                .ToList();
            if (Reverse) items.Reverse();
            if (Fault == "missing" && items.Count != 0) items.RemoveAt(items.Count - 1);
            if (Fault == "duplicate" && items.Count != 0) items.Add(items[0]);
            if (Fault == "mismatch" && items.Count != 0) items[0] = items[0] with { PartyId = Guid.NewGuid() };
            return Task.FromResult(new SalesPartyDisplayBatchAnswerV1(SalesPartyDisplayBatchOutcomeV1.Success, items));
        }
    }

    /// <summary>SALES-A2a: the Cari owner's contact information of the account — one active and one inactive address and contact.</summary>
    private sealed class FakeContactInformation : ISalesPartyContactInformationPortV1
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }
        public (Guid Party, Guid Account)? Asked { get; private set; }

        public Task<SalesPartyContactInformationAnswerV1> GetContactInformationAsync(Guid partyId, Guid companyAccountId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Asked = (partyId, companyAccountId);
            return Task.FromResult(Fault switch
            {
                "not-found" => new SalesPartyContactInformationAnswerV1(SalesPartyContactInformationOutcomeV1.NotFound, null),
                "denied" => new SalesPartyContactInformationAnswerV1(SalesPartyContactInformationOutcomeV1.Denied, null),
                "unavailable" => new SalesPartyContactInformationAnswerV1(SalesPartyContactInformationOutcomeV1.Unavailable, null),
                _ => new SalesPartyContactInformationAnswerV1(SalesPartyContactInformationOutcomeV1.Found, new SalesPartyContactInformationV1(
                    partyId, Fault == "other-account" ? Guid.NewGuid() : companyAccountId, Fault == "other-company" ? Guid.NewGuid() : Company, "2",
                    [
                        new SalesPartyAddressV1(AddressId, Fault == "malformed-version" ? " " : AddressVersion, true, "delivery", "TR",
                            "Sentetik Sanayi Cad. 1", null, "Kocaeli", "Gebze", "41400"),
                        new SalesPartyAddressV1(InactiveAddressId, "2", false, "delivery", "TR", "Eski adres", null, null, null, null),
                    ],
                    [
                        new SalesPartyContactV1(ContactId, ContactVersion, true, "Sentetik Kişi", "Satın alma", "delivery"),
                        new SalesPartyContactV1(InactiveContactId, "3", false, "Ayrılan Kişi", null, "delivery"),
                    ])),
            });
        }
    }

    private sealed class FakeSettings : ISettingsPrerequisiteProvider
    {
        public string Fault { get; set; } = "";
        public int Calls { get; set; }
        public (string Operation, Guid Currency, DateOnly On)? MoneyAsked { get; private set; }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            Calls++;
            MoneyAsked = (operation, currencyId, effectiveOn);
            return Fault switch
            {
                "money-not-found" => Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.MoneyProfileNotFound)),
                "money-access-denied" => Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException("settings_access_denied")),
                "money-unavailable" => Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException("settings_provider_unavailable")),
                _ => Task.FromResult(new MoneyProfileV1(Fault == "money-other-company" ? Guid.NewGuid() : Company, currencyId, "TRY", operation,
                    new ProfileVersionV1(MoneyProfile, 3), 2, true, Fault == "money-not-in-effect" ? Day.AddDays(1) : new DateOnly(2026, 9, 1), null)),
            };
        }

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fault switch
            {
                "unit-not-found" => Task.FromException<UnitProfileV1>(new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.UnitProfileNotFound)),
                _ => Task.FromResult(new UnitProfileV1(Fault == "unit-other-company" ? Guid.NewGuid() : Company, unitId, "kg", "weight", true,
                    new ProfileVersionV1(KgProfile, 4), 3, true, new DateOnly(2026, 9, 1), null)),
            };
        }

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeProducts : IProductSalesEligibilityV1
    {
        public string Fault { get; set; } = "";
        public bool VersionConflict { get; set; }
        public int Calls { get; set; }
        public Action? BeforeErrorsEnumerated { get; set; }

        public Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fault == "unavailable") throw new ProductOperationException("product_family_policy_unavailable", 503);
            if (Fault == "conflict-exception") throw new ProductOperationException("product_selection_changed", 409);
            if (Fault == "not-found") throw new ProductOperationException("product_not_found", 404);
            if (VersionConflict)
            {
                IReadOnlyList<ProductFieldError> errors = [new("productId", ProductApiV1.VersionConflict)];
                if (BeforeErrorsEnumerated is not null)
                {
                    errors = new OnEnumeratedList<ProductFieldError>(errors, BeforeErrorsEnumerated);
                }

                return Task.FromResult(new ProductSalesEligibilityV1(null, errors));
            }
            if (Fault == "field-errors") return Task.FromResult(new ProductSalesEligibilityV1(null, [new("productId", ProductApiV1.LifecycleConflict)]));
            var unit = new ProductSalesUnitProfileV1(Company, Fault == "other-unit" ? Guid.NewGuid() : selection.OrderUnitId, KgProfile, "4");
            return Task.FromResult(new ProductSalesEligibilityV1(
                new ProductSalesSnapshotV1(selection.ProductId, "7", "GDC-001", "Sentetik bant", unit, unit, unit, selection.EffectiveOn), []));
        }
    }

    private sealed class OnEnumeratedList<T>(IReadOnlyList<T> items, Action beforeEnumeration) : IReadOnlyList<T>
    {
        public int Count => items.Count;
        public T this[int index] => items[index];

        public IEnumerator<T> GetEnumerator()
        {
            beforeEnumeration();
            return items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>An in-memory store with the contract's replay semantics; no database, no owner rule.</summary>
    private sealed class FakeStore : ISalesOrderDraftStore
    {
        private readonly Dictionary<(string Command, string Key), (string Actor, string Hash, SalesOrderDraft? Draft, SalesDraftTerminalRefusal? Refusal, string Correlation, SalesDraftReplayResult? Result)> _attempts = [];
        private readonly List<SalesOrderDraft> _drafts = [];
        public List<SalesDraftWriteEnvelope> Envelopes { get; } = [];
        public int Attempts => _attempts.Count;
        public List<(SalesDraftWriteEnvelope Envelope, SalesDraftTerminalRefusal Refusal, Guid? DraftId)> Refusals { get; } = [];
        /// <summary>SALES-A1: every state transition the store committed, with its audit facts.</summary>
        public List<(string Command, Guid DraftId, SalesDraftWriteEnvelope Envelope, SalesDraftStateAudit Audit, long VersionBefore, long VersionAfter)> Transitions { get; } = [];
        public int Replays { get; private set; }
        public string? Fault { get; set; }
        /// <summary>Runs when the store is about to commit: what changes in the world between the last fence and the commit.</summary>
        public Action? OnStore { get; set; }
        /// <summary>Runs after the terminal fence and before the refusal store arbitrates the idempotency key.</summary>
        public Action? OnRecordRefusal { get; set; }
        /// <summary>A same-key race lost once: "draft-at-store" (the winner stored a draft), "empty-at-store" (nothing under the key), "refusal-at-record" (the winner recorded the refusal), or "floor-result-at-refusal" (the winner's immutable floor result exists while the live draft has advanced).</summary>
        public string? Race { get; set; }
        public SalesOrderDraft? Winner { get; private set; }

        public Task<SalesDraftStateResult?> UpdateDraftStateAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid draftId, Action<SalesOrderDraft, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default) =>
            UpdateDraftStateAsyncCore(envelope, requestHash, commandName, draftId, mutate, audit, null, cancellationToken);

        private async Task<SalesDraftStateResult?> UpdateDraftStateAsyncCore(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid draftId, Action<SalesOrderDraft, DateTimeOffset> mutate, SalesDraftStateAudit audit,
            Func<CancellationToken, Task>? beforeWrite, CancellationToken cancellationToken,
            Func<SalesOrderDraft, SalesDraftReplayResult?>? replayResult = null)
        {
            if (Fault is not null) throw new SalesDraftStoreException(Fault, "synthetic");
            if (_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
                    throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "synthetic");
                if (attempt.Draft is null) throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic refusal under the key");
                return new SalesDraftStateResult(attempt.Draft, SalesDraftWriteOutcome.Replayed, attempt.Correlation, attempt.Result);
            }

            var draft = _drafts.SingleOrDefault(candidate => candidate.Id == draftId);
            if (draft is null) return null;
            OnStore?.Invoke();
            if (beforeWrite is not null) await beforeWrite(cancellationToken);
            if (Race == "version-at-store")
            {
                Race = null;
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic version race");
            }

            var before = draft.Version;
            mutate(draft, TimeProvider.System.GetUtcNow());
            var storedResult = replayResult?.Invoke(draft);
            _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, draft, null, envelope.CorrelationId, storedResult);
            Transitions.Add((commandName, draftId, envelope, audit, before, draft.Version));
            return new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId, storedResult);
        }

        public List<ApprovedRevisionSnapshot> Archived { get; } = [];

        public async Task<SalesDraftStateResult?> ReviseDraftCommercialAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid draftId, Func<SalesOrderDraft, DateTimeOffset, ApprovedRevisionSnapshot?> mutate, SalesDraftStateAudit audit,
            Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default,
            Func<SalesOrderDraft, SalesDraftReplayResult?>? replayResult = null)
        {
            if (commandName is not (SalesDraftCommands.ReviseCommercialV1 or SalesDraftCommands.ApprovePriceFloorExceptionsV1))
                throw new ArgumentException("wrong command", nameof(commandName));
            ApprovedRevisionSnapshot? archived = null;
            var result = await UpdateDraftStateAsyncCore(envelope, requestHash, commandName, draftId,
                (draft, now) => archived = mutate(draft, now), audit, beforeWrite, cancellationToken, replayResult);
            if (archived is not null) Archived.Add(archived);
            return result;
        }

        public async Task<SalesDraftStateResult?> ReviseDraftQuantityAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid draftId, Func<SalesOrderDraft, DateTimeOffset, ApprovedRevisionSnapshot?> mutate, SalesDraftStateAudit audit,
            Func<SalesOrderDraft, CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
        {
            if (commandName != SalesDraftCommands.ReviseQuantityV1) throw new ArgumentException("wrong command", nameof(commandName));
            ApprovedRevisionSnapshot? archived = null;
            var draft = _drafts.SingleOrDefault(candidate => candidate.Id == draftId);
            var result = await UpdateDraftStateAsyncCore(envelope, requestHash, commandName, draftId,
                (target, now) => archived = mutate(target, now), audit,
                token => beforeWrite(draft!, token), cancellationToken);
            if (archived is not null) Archived.Add(archived);
            return result;
        }

        public List<DeliveryScheduleSnapshot> Schedules { get; } = [];

        public async Task<SalesDraftStateResult?> RescheduleDeliveryAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid draftId, Func<SalesOrderDraft, DateTimeOffset, DeliveryScheduleSnapshot> mutate, SalesDraftStateAudit audit,
            Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
        {
            if (commandName != SalesDraftCommands.RescheduleDeliveryV1) throw new ArgumentException("wrong command", nameof(commandName));
            DeliveryScheduleSnapshot? recorded = null;
            var result = await UpdateDraftStateAsyncCore(envelope, requestHash, commandName, draftId,
                (draft, now) => recorded = mutate(draft, now), audit, beforeWrite, cancellationToken);
            if (recorded is not null) Schedules.Add(recorded);
            return result;
        }

        public List<CancellationFacts> Cancellations { get; } = [];

        public async Task<SalesDraftStateResult?> CancelDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid draftId, Func<SalesOrderDraft, CancellationImpactSnapshot, DateTimeOffset, CancellationFacts> mutate, SalesDraftStateAudit audit,
            Func<SalesOrderDraft, CancellationToken, Task<CancellationImpactSnapshot>> beforeWrite, CancellationToken cancellationToken = default)
        {
            if (commandName != SalesDraftCommands.CancelV1) throw new ArgumentException("wrong command", nameof(commandName));
            CancellationImpactSnapshot? impact = null;
            CancellationFacts? recorded = null;
            var draft = _drafts.SingleOrDefault(candidate => candidate.Id == draftId);
            var result = await UpdateDraftStateAsyncCore(envelope, requestHash, commandName, draftId,
                (target, now) => recorded = mutate(target, impact!, now), audit,
                async token => impact = await beforeWrite(draft!, token), cancellationToken);
            if (recorded is not null) Cancellations.Add(recorded);
            return result;
        }

        public List<RemainderChangeFacts> RemainderChanges { get; } = [];

        public async Task<SalesDraftStateResult?> ChangeRemainderAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid draftId, Func<SalesOrderDraft, DateTimeOffset, RemainderChangeFacts> mutate, SalesDraftStateAudit audit,
            Func<SalesOrderDraft, CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
        {
            if (!SalesDraftCommands.IsRemainderCommand(commandName)) throw new ArgumentException("wrong command", nameof(commandName));
            RemainderChangeFacts? recorded = null;
            var draft = _drafts.SingleOrDefault(candidate => candidate.Id == draftId);
            var result = await UpdateDraftStateAsyncCore(envelope, requestHash, commandName, draftId,
                (target, now) => recorded = mutate(target, now), audit,
                token => beforeWrite(draft!, token), cancellationToken);
            if (recorded is not null) RemainderChanges.Add(recorded);
            return result;
        }

        public Task<SalesDraftStoreResult> StoreDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash, Func<string, SalesOrderDraft> build,
            CancellationToken cancellationToken = default)
        {
            if (Fault is not null) throw new SalesDraftStoreException(Fault, "synthetic");
            OnStore?.Invoke();
            if (Race is "draft-at-store" or "empty-at-store")
            {
                var race = Race;
                Race = null;
                if (race == "draft-at-store")
                {
                    Winner = build("SO-2026-000099");
                    Winner.RequireStorable();
                    Winner.RestoreCreator(envelope.ActorId);
                    _drafts.Add(Winner);
                    _attempts[(SalesDraftCommands.CreateV1, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, Winner, null, "winner-correlation", null);
                }

                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic race");
            }

            var draft = build($"SO-2026-{_drafts.Count + 1:D6}");
            draft.RequireStorable();
            // D2: the real store restores the creator from the create envelope's actor on every read; the fake holds the
            // live aggregate, so the creator is fixed here, once, at the create.
            draft.RestoreCreator(envelope.ActorId);
            _drafts.Add(draft);
            _attempts[(SalesDraftCommands.CreateV1, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, draft, null, envelope.CorrelationId, null);
            Envelopes.Add(envelope);
            return Task.FromResult(new SalesDraftStoreResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesDraftReplay?> FindReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, CancellationToken cancellationToken = default,
            string commandName = SalesDraftCommands.CreateV1)
        {
            Replays++;
            if (Fault is not null) throw new SalesDraftStoreException(Fault, "synthetic");
            if (!_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt)) return Task.FromResult<SalesDraftReplay?>(null);
            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
                throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "synthetic");
            return Task.FromResult<SalesDraftReplay?>(new SalesDraftReplay(attempt.Draft, attempt.Refusal, attempt.Correlation, attempt.Result));
        }

        public Task RecordRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, SalesDraftTerminalRefusal refusal,
            CancellationToken cancellationToken = default, string commandName = SalesDraftCommands.CreateV1, Guid? draftId = null)
        {
            OnRecordRefusal?.Invoke();
            if (Race == "refusal-at-record")
            {
                Race = null;
                _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, "winner-correlation", null);
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic race");
            }

            if (Race == "floor-result-at-refusal")
            {
                Race = null;
                var winner = _attempts.Values.Single(attempt => attempt.Result is not null);
                _attempts[(commandName, envelope.IdempotencyKey)] =
                    (envelope.ActorId, requestHash, winner.Draft, null, "winner-floor-correlation", winner.Result);
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic floor-result race");
            }

            if (_attempts.ContainsKey((commandName, envelope.IdempotencyKey)))
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic: the key already holds an attempt");
            _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, envelope.CorrelationId, null);
            Refusals.Add((envelope, refusal, draftId));
            return Task.CompletedTask;
        }

        public Action? OnFindDraft { get; set; }

        public Task<SalesOrderDraft?> FindDraftAsync(Guid draftId, CancellationToken cancellationToken = default)
        {
            OnFindDraft?.Invoke();
            return Task.FromResult(_drafts.SingleOrDefault(draft => draft.Id == draftId));
        }

        public Task<SalesDraftPage> ListDraftsAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default,
            InternalApprovalState? approvalState = null)
        {
            if (Fault is not null) throw new SalesDraftStoreException(Fault, "synthetic");
            var drafts = _drafts.Where(draft => approvalState is null || draft.ApprovalState == approvalState);
            return Task.FromResult(new SalesDraftPage([.. drafts.OrderByDescending(draft => draft.CreatedAtUtc).Take(pageSize)], null));
        }
    }
}
