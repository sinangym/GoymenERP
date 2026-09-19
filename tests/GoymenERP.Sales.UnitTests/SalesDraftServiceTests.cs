using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
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
public sealed class SalesDraftServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid MoneyProfile = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Product = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly DateOnly Day = new(2026, 9, 18);
    private const string Actor = "synthetic-actor";
    // Registered platform names stand in for the Sales names until the central catalogue knows them.
    private const string CreatePermission = ErpPermissions.CounterpartyPartiesCreate;
    private const string ReadPermission = ErpPermissions.CounterpartyPartiesRead;

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
        Assert.Equal((1, Product, "7", "GDC-001", "Sentetik bant", KgUnit, "1.25"),
            (line.LineNo, line.ProductId, line.ProductVersion, line.ProductCode, line.ProductName, line.OrderUnitId, line.Quantity));
        Assert.Null(line.UnitPrice);
        Assert.Equal(2, line.RequestedDeliverySlices!.Count);
        Assert.Null(response.Detail.DeliveryAddress);
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
        world.ResetOwnerCalls();

        var again = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(request, Metadata("k-1", correlation: "retry")));

        // Another content under the same key (new line ids) is the store's §3 refusal, mapped, never a raw store fault.
        var other = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Request(), Metadata("k-1")));
        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch, false), (other.Status, other.Code, other.IsReplay));

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true, "synthetic-correlation"),
            (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));
        Assert.Equal(0, world.Products.Calls);
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
            case "unregistered-name": world.Bindings = new SalesDraftServiceBindings("sales.synthetic.unregistered", ReadPermission, ApprovePermission); break;
            default: world.Bindings = new SalesDraftServiceBindings(null, ReadPermission, ApprovePermission); break;
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

        var references = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(
            Request() with { DeliveryAddress = new SalesDraftOwnerReferenceV1(Guid.NewGuid(), "2"), Contact = new(Guid.NewGuid(), "1") },
            Metadata("k-1")));
        Assert.Equal((422, SalesDraftProblemCodesV1.OwnerReferenceUnavailable), (references.Status, references.Code));
        Assert.Equal(["deliveryAddress", "contact"], references.Errors.Select(error => error.Field));

        var priced = Request();
        priced = priced with { Lines = [priced.Lines[0] with { UnitPrice = "12.50", PriceUnitId = KgUnit, DiscountPercent = "5" }] };
        var commercial = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(priced, Metadata("k-2")));
        Assert.Equal((422, SalesDraftProblemCodesV1.CommercialNotAvailable), (commercial.Status, commercial.Code));
        Assert.Equal(["lines[0].unitPrice", "lines[0].priceUnitId", "lines[0].discountPercent"], commercial.Errors.Select(error => error.Field));

        Assert.Equal((0, 0, 0, 0), (world.Store.Replays, world.Accounts.Calls, world.Settings.Calls, world.Products.Calls));
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
        Assert.Equal(created.Detail.Draft, detail.Draft);
        Assert.Equal(created.Detail.Lines.Select(line => line.LineId), detail.Lines.Select(line => line.LineId));
        Assert.Equal(ReadPermission, world.Authority.LastPermission);

        var missing = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDetailAsync(Guid.NewGuid()));
        Assert.Equal(404, missing.Status);

        var page = await world.Service().GetDraftListAsync(new SalesOrderDraftListQuery());
        Assert.Equal(new[] { created.Detail.Draft }, page.Items);
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
        Assert.Equal(ApprovePermission, world.Authority.LastPermission);

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
            case "unbound-approve": world.Bindings = new SalesDraftServiceBindings(CreatePermission, ReadPermission, null); break;
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

    // ---- synthetic world --------------------------------------------------------------------

    private sealed class World
    {
        public FixedUser User { get; } = new();
        public FixedAuthority Authority { get; } = new();
        public FakeAccounts Accounts { get; init; } = new();
        public FakeSettings Settings { get; init; } = new();
        public FakeProducts Products { get; init; } = new();
        public FakeStore Store { get; init; } = new();
        public bool NoProducts { get; set; }
        public SalesDraftServiceBindings Bindings { get; set; } = new(CreatePermission, ReadPermission, ApprovePermission);

        public SalesDraftService Service()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new SalesDraftService(company, User, Authority, new FixedCorrelation(), Accounts, Settings,
                NoProducts ? null : Products, Store, Bindings, TimeProvider.System);
        }

        public void ResetOwnerCalls()
        {
            Accounts.Calls = 0;
            Settings.Calls = 0;
            Products.Calls = 0;
        }
    }

    private sealed class FixedUser : ICurrentUser
    {
        public bool Authenticated { get; set; } = true;
        public Guid Company { get; set; } = SalesDraftServiceTests.Company;
        public bool IsAuthenticated => Authenticated;
        public string? UserId => Authenticated ? Actor : null;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class FixedAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public bool CompanyWide { get; set; } = true;
        public int Calls { get; private set; }
        public string? LastPermission { get; private set; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPermission = permission;
            return Task.FromResult(new PermissionScope(Granted, CompanyWide, new HashSet<string>(["ROLE-B", "ROLE-A"], StringComparer.Ordinal),
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

        public Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fault == "unavailable") throw new ProductOperationException("product_family_policy_unavailable", 503);
            if (Fault == "conflict-exception") throw new ProductOperationException("product_selection_changed", 409);
            if (Fault == "not-found") throw new ProductOperationException("product_not_found", 404);
            if (VersionConflict) return Task.FromResult(new ProductSalesEligibilityV1(null, [new("productId", ProductApiV1.VersionConflict)]));
            if (Fault == "field-errors") return Task.FromResult(new ProductSalesEligibilityV1(null, [new("productId", ProductApiV1.LifecycleConflict)]));
            var unit = new ProductSalesUnitProfileV1(Company, Fault == "other-unit" ? Guid.NewGuid() : selection.OrderUnitId, KgProfile, "4");
            return Task.FromResult(new ProductSalesEligibilityV1(
                new ProductSalesSnapshotV1(selection.ProductId, "7", "GDC-001", "Sentetik bant", unit, unit, unit, selection.EffectiveOn), []));
        }
    }

    /// <summary>An in-memory store with the contract's replay semantics; no database, no owner rule.</summary>
    private sealed class FakeStore : ISalesOrderDraftStore
    {
        private readonly Dictionary<(string Command, string Key), (string Actor, string Hash, SalesOrderDraft? Draft, SalesDraftTerminalRefusal? Refusal, string Correlation)> _attempts = [];
        private readonly List<SalesOrderDraft> _drafts = [];
        public List<SalesDraftWriteEnvelope> Envelopes { get; } = [];
        public List<(SalesDraftWriteEnvelope Envelope, SalesDraftTerminalRefusal Refusal)> Refusals { get; } = [];
        /// <summary>SALES-A1: every state transition the store committed, with its audit facts.</summary>
        public List<(string Command, Guid DraftId, SalesDraftWriteEnvelope Envelope, SalesDraftStateAudit Audit, long VersionBefore, long VersionAfter)> Transitions { get; } = [];
        public int Replays { get; private set; }
        public string? Fault { get; set; }
        /// <summary>Runs when the store is about to commit: what changes in the world between the last fence and the commit.</summary>
        public Action? OnStore { get; set; }
        /// <summary>A same-key race lost once: "draft-at-store" (the winner stored a draft), "empty-at-store" (nothing under the key), "refusal-at-record" (the winner recorded the refusal).</summary>
        public string? Race { get; set; }
        public SalesOrderDraft? Winner { get; private set; }

        public Task<SalesDraftStateResult?> UpdateDraftStateAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid draftId, Action<SalesOrderDraft, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            if (Fault is not null) throw new SalesDraftStoreException(Fault, "synthetic");
            if (_attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
                    throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "synthetic");
                if (attempt.Draft is null) throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic refusal under the key");
                return Task.FromResult<SalesDraftStateResult?>(new SalesDraftStateResult(attempt.Draft, SalesDraftWriteOutcome.Replayed, attempt.Correlation));
            }

            var draft = _drafts.SingleOrDefault(candidate => candidate.Id == draftId);
            if (draft is null) return Task.FromResult<SalesDraftStateResult?>(null);
            OnStore?.Invoke();
            if (Race == "version-at-store")
            {
                Race = null;
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic version race");
            }

            var before = draft.Version;
            mutate(draft, TimeProvider.System.GetUtcNow());
            _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, draft, null, envelope.CorrelationId);
            Transitions.Add((commandName, draftId, envelope, audit, before, draft.Version));
            return Task.FromResult<SalesDraftStateResult?>(new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
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
                    _drafts.Add(Winner);
                    _attempts[(SalesDraftCommands.CreateV1, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, Winner, null, "winner-correlation");
                }

                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic race");
            }

            var draft = build($"SO-2026-{_drafts.Count + 1:D6}");
            draft.RequireStorable();
            _drafts.Add(draft);
            _attempts[(SalesDraftCommands.CreateV1, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, draft, null, envelope.CorrelationId);
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
            return Task.FromResult<SalesDraftReplay?>(new SalesDraftReplay(attempt.Draft, attempt.Refusal, attempt.Correlation));
        }

        public Task RecordRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, SalesDraftTerminalRefusal refusal,
            CancellationToken cancellationToken = default, string commandName = SalesDraftCommands.CreateV1)
        {
            if (Race == "refusal-at-record")
            {
                Race = null;
                _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, "winner-correlation");
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic race");
            }

            if (_attempts.ContainsKey((commandName, envelope.IdempotencyKey)))
                throw new SalesDraftStoreException(SalesDraftStoreCodes.ConcurrentChange, "synthetic: the key already holds an attempt");
            _attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, envelope.CorrelationId);
            Refusals.Add((envelope, refusal));
            return Task.CompletedTask;
        }

        public Task<SalesOrderDraft?> FindDraftAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_drafts.SingleOrDefault(draft => draft.Id == draftId));

        public Task<SalesDraftPage> ListDraftsAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default,
            InternalApprovalState? approvalState = null)
        {
            if (Fault is not null) throw new SalesDraftStoreException(Fault, "synthetic");
            var drafts = _drafts.Where(draft => approvalState is null || draft.ApprovalState == approvalState);
            return Task.FromResult(new SalesDraftPage([.. drafts.OrderByDescending(draft => draft.CreatedAtUtc).Take(pageSize)], null));
        }
    }
}
