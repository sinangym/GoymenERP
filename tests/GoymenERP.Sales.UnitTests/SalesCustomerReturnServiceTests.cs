using System.Reflection;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Domain.Invoices;
using static GoymenERP.Sales.UnitTests.SalesCustomerReturnFixtures;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// API-40 service skeleton (K3): the operation's own name and the price read are measured before anything is read, the replay comes
/// before any read, a terminal refusal (self-approval, stale version) is recorded under the key, a rule refusal writes nothing, and
/// finalisation without the Finance reversal port fails closed before any read (SAT-D294). SAT-S2: the finalisation offers Finance
/// the actor, the key, both versions and the user's installment selection; a partial return without a selection is refused before
/// any offer (FNS-D81: the automatic nearest-due order is never applied).
/// </summary>
public sealed class SalesCustomerReturnServiceTests
{
    private static readonly Guid ReturnId = Guid.Parse("acacacac-acac-4cac-8cac-acacacacacac");

    private static readonly SalesCommandMetadata Metadata = new("SENTETIK-key-1", "SENTETIK-correlation", null, Now);

    private static SalesInvoice OneLineInvoice() => TlInvoice(new StoredLine(3m, 100.00m, 20.00m, 0m, NetUnitPrice: 33.3333m));

    private static CreateSalesCustomerReturnRequest CreateRequest(Guid? invoiceId = null) => new(
        invoiceId ?? OneLineInvoice().Id, "IADE-SENTETIK-1", Day, [new CreateSalesCustomerReturnLineRequest(LineId(1), "1")], ShipmentOf(1));

    private static SalesCustomerReturn Draft() => SalesCustomerReturn.Create(ReturnId, Company, OneLineInvoice(), ShipmentOf(1),
        "IADE-SENTETIK-1", Day, [new SalesCustomerReturnLineInput(LineId(1), 1m)],
        new Dictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>>(), Preparer, Now);

    private static SalesCustomerReturn ApprovedReturn()
    {
        var item = Draft();
        item.Submit(1, "SENTETIK-submitter", Now);
        item.Approve(2, Approver, Now);
        return item;
    }

    private static readonly Guid FirstOpenItem = Guid.Parse("a1a1a1a1-a1a1-4a1a-8a1a-a1a1a1a1a1a1");
    private static readonly Guid SecondOpenItem = Guid.Parse("a2a2a2a2-a2a2-4a2a-8a2a-a2a2a2a2a2a2");

    private static SalesCustomerReturnInstallmentAnswerV1 PartialPrefill() => new(SalesCustomerReturnFinanceOutcomeV1.Admitted, null, true,
        "40.00", "0",
        [
            new SalesCustomerReturnFinanceInstallmentV1(FirstOpenItem, Day.AddDays(30), "60.00", "30.00", "30.00"),
            new SalesCustomerReturnFinanceInstallmentV1(SecondOpenItem, Day.AddDays(60), "60.00", "60.00", "10.00"),
        ]);

    private static (SalesCustomerReturnService Service, Store Store, InvoiceProxy Invoices, Authority Authority) Build(
        string actor = Preparer, ISalesCustomerReturnFinancePortV1? finance = null)
    {
        var store = new Store();
        var invoices = InvoiceProxy.Create();
        invoices.Invoice = OneLineInvoice();
        var authority = new Authority();
        var service = new SalesCustomerReturnService(new CompanyContext(), new User(actor), authority, new Correlation(), store,
            invoices.Service, TimeProvider.System, finance);
        return (service, store, invoices, authority);
    }

    private static async Task<SalesDraftServiceException> Refused(Func<Task> act) =>
        await Assert.ThrowsAsync<SalesDraftServiceException>(act);

    [Fact]
    public void Only_the_rejection_and_the_cancellation_bodies_take_a_reason_and_every_step_needs_a_positive_expected_version()
    {
        Assert.Equal(new SalesDraftStateCommandRequest("2", "Belge eksik"),
            SalesCustomerReturnJson.ReadDecisionCommand("{\"expectedVersion\":\"2\",\"reason\":\"Belge eksik\"}"));
        Assert.Equal(new SalesDraftStateCommandRequest("1"), SalesCustomerReturnJson.ReadDecisionCommand("{\"expectedVersion\":\"1\"}"));
        Assert.Equal(new SalesDraftStateCommandRequest("1"), SalesCustomerReturnJson.ReadStateCommand("{\"expectedVersion\":\"1\"}"));
        Assert.Throws<JsonException>(() => SalesCustomerReturnJson.ReadStateCommand("{\"expectedVersion\":\"1\",\"reason\":\"x\"}"));
        foreach (var body in new[] { "{}", "{\"expectedVersion\":\"0\"}", "{\"expectedVersion\":1}", "null" })
        {
            Assert.Throws<JsonException>(() => SalesCustomerReturnJson.ReadDecisionCommand(body));
        }
    }

    [Fact]
    public async Task Finalize_without_the_finance_port_is_503_before_any_replay_or_read_and_nothing_is_written_SAT_D294()
    {
        var (service, store, invoices, authority) = Build(Approver);

        var refused = await Refused(() => service.FinalizeCustomerReturnAsync(ReturnId, new FinalizeSalesCustomerReturnRequest("3"), Metadata));

        Assert.Equal((503, SalesCustomerReturnProblemCodesV1.FinancePortUnavailable), (refused.Status, refused.Code));
        Assert.Empty(store.Calls);
        Assert.Equal(0, invoices.Calls);
        Assert.Equal([SalesPermissionManifestV1.ReturnsFinalize, SalesPermissionManifestV1.OrdersPriceRead], authority.Asked);
    }

    [Fact]
    public async Task Finalize_with_a_refusing_finance_port_is_422_and_neither_the_finalisation_nor_a_refusal_is_recorded()
    {
        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Refused);
        var (service, store, _, _) = Build("SENTETIK-finalizer", port);
        var item = Draft();
        item.Submit(1, "SENTETIK-submitter", Now);
        item.Approve(2, Approver, Now);
        store.Stored = item;

        var refused = await Refused(() => service.FinalizeCustomerReturnAsync(ReturnId, new FinalizeSalesCustomerReturnRequest("3"), Metadata));

        Assert.Equal((422, SalesCustomerReturnProblemCodesV1.FinanceRefused), (refused.Status, refused.Code));
        Assert.Equal(1, port.Calls);
        Assert.DoesNotContain("finalize", store.Calls);
        Assert.DoesNotContain("refusal", store.Calls);
        Assert.Equal(SalesCustomerReturnState.Approved, store.Stored.State);
    }

    [Fact]
    public async Task An_admitted_whole_return_offers_the_actor_the_key_both_versions_and_the_amount_without_a_selection_and_commits()
    {
        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Admitted);
        var (service, store, invoices, _) = Build("SENTETIK-finalizer", port);
        store.Stored = ApprovedReturn();
        var invoice = invoices.Invoice!;
        var invoiceVersion = (invoice.Finalization?.FinalizedVersion ?? invoice.Version).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await service.FinalizeCustomerReturnAsync(ReturnId, new FinalizeSalesCustomerReturnRequest("3"), Metadata);

        Assert.Equal("Finalized", result.CustomerReturn.State);
        Assert.Equal((1, 1), (port.Reads, port.Calls));
        var offer = port.Offer!;
        Assert.Equal((Company, "SENTETIK-finalizer", "SENTETIK-key-1", "SENTETIK-correlation"),
            (offer.CompanyId, offer.ActorId, offer.IdempotencyKey, offer.CorrelationId));
        Assert.Equal((ReturnId, "3", invoice.Id, invoiceVersion, "40.00"),
            (offer.CustomerReturnId, offer.DocumentVersion, offer.OriginalInvoiceId, offer.OriginalInvoiceVersion, offer.Amount));
        Assert.Equal((invoice.Header.PartyId, invoice.Header.CompanyAccountId, invoice.Header.CurrencyId),
            (offer.PartyId, offer.CompanyAccountId, offer.CurrencyId));
        Assert.Null(offer.Allocations);
        Assert.Null(offer.FunctionalAmount);
        var query = port.Query!;
        Assert.Equal((Company, "SENTETIK-finalizer", ReturnId, invoice.Id, invoiceVersion, "40.00"),
            (query.CompanyId, query.ActorId, query.CustomerReturnId, query.OriginalInvoiceId, query.OriginalInvoiceVersion, query.Amount));
        Assert.Equal(1, store.Calls.Count(call => call == "finalize"));
    }

    [Fact]
    public async Task A_partial_return_without_the_users_selection_is_422_installments_required_and_nothing_is_offered_FNS_D81()
    {
        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Admitted) { Installments = PartialPrefill() };
        var (service, store, _, _) = Build("SENTETIK-finalizer", port);
        store.Stored = ApprovedReturn();

        var refused = await Refused(() => service.FinalizeCustomerReturnAsync(ReturnId, new FinalizeSalesCustomerReturnRequest("3"), Metadata));

        Assert.Equal((422, SalesCustomerReturnProblemCodesV1.InstallmentsRequired), (refused.Status, refused.Code));
        Assert.Equal(new SalesDraftFieldError("allocations", SalesCustomerReturnProblemCodesV1.InstallmentsRequired), Assert.Single(refused.Errors));
        Assert.Equal((1, 0), (port.Reads, port.Calls));
        Assert.DoesNotContain("finalize", store.Calls);
        Assert.DoesNotContain("refusal", store.Calls);
        Assert.Equal(SalesCustomerReturnState.Approved, store.Stored.State);
    }

    [Fact]
    public async Task The_users_selection_is_offered_as_it_was_chosen_and_the_prefill_is_never_read_in_its_place_FNS_D81()
    {
        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Admitted) { Installments = PartialPrefill() };
        var (service, store, _, _) = Build("SENTETIK-finalizer", port);
        store.Stored = ApprovedReturn();
        var request = new FinalizeSalesCustomerReturnRequest("3",
            [new SalesCustomerReturnAllocationRequest(SecondOpenItem, "40.00")]);

        var result = await service.FinalizeCustomerReturnAsync(ReturnId, request, Metadata);

        Assert.Equal("Finalized", result.CustomerReturn.State);
        Assert.Equal((0, 1), (port.Reads, port.Calls));
        Assert.Equal([new SalesCustomerReturnFinanceAllocationV1(SecondOpenItem, "40.00")], port.Offer!.Allocations!);
    }

    [Fact]
    public async Task A_finance_refusal_of_the_selection_names_the_selection_with_finances_code_and_nothing_is_recorded()
    {
        const string reason = "finance_customer_return_allocation_exceeds_open_amount";
        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Refused, reason);
        var (service, store, _, _) = Build("SENTETIK-finalizer", port);
        store.Stored = ApprovedReturn();
        var request = new FinalizeSalesCustomerReturnRequest("3", [new SalesCustomerReturnAllocationRequest(FirstOpenItem, "40.00")]);

        var refused = await Refused(() => service.FinalizeCustomerReturnAsync(ReturnId, request, Metadata));

        Assert.Equal((422, SalesCustomerReturnProblemCodesV1.FinanceRefused), (refused.Status, refused.Code));
        Assert.Equal(new SalesDraftFieldError("allocations", reason), Assert.Single(refused.Errors));
        Assert.DoesNotContain("finalize", store.Calls);
        Assert.DoesNotContain("refusal", store.Calls);
    }

    [Theory]
    [InlineData(SalesCustomerReturnFinanceOutcomeV1.KeyConflict, 409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch)]
    [InlineData(SalesCustomerReturnFinanceOutcomeV1.Retry, 409, SalesDraftProblemCodesV1.ConcurrentChange)]
    [InlineData(SalesCustomerReturnFinanceOutcomeV1.Denied, 403, "forbidden")]
    [InlineData(SalesCustomerReturnFinanceOutcomeV1.Unavailable, 503, "service_unavailable")]
    public async Task A_finance_answer_other_than_admitted_or_refused_is_mapped_and_nothing_is_committed(
        SalesCustomerReturnFinanceOutcomeV1 outcome, int status, string code)
    {
        var port = new FinancePort(outcome);
        var (service, store, _, _) = Build("SENTETIK-finalizer", port);
        store.Stored = ApprovedReturn();

        var refused = await Refused(() => service.FinalizeCustomerReturnAsync(ReturnId, new FinalizeSalesCustomerReturnRequest("3"), Metadata));

        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.DoesNotContain("finalize", store.Calls);
        Assert.DoesNotContain("refusal", store.Calls);
        Assert.Equal(SalesCustomerReturnState.Approved, store.Stored.State);
    }

    [Fact]
    public async Task A_repeated_open_item_in_the_selection_is_400_before_finance_or_the_store_is_asked()
    {
        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Admitted);
        var (service, store, _, _) = Build("SENTETIK-finalizer", port);
        var request = new FinalizeSalesCustomerReturnRequest("3",
        [
            new SalesCustomerReturnAllocationRequest(FirstOpenItem, "20.00"),
            new SalesCustomerReturnAllocationRequest(FirstOpenItem, "20.00"),
        ]);

        var refused = await Refused(() => service.FinalizeCustomerReturnAsync(ReturnId, request, Metadata));

        Assert.Equal((400, "validation_failed"), (refused.Status, refused.Code));
        Assert.Equal((0, 0), (port.Reads, port.Calls));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task The_installment_read_answers_finances_prefill_for_an_approved_return_under_the_finalize_permission()
    {
        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Admitted) { Installments = PartialPrefill() };
        var (service, store, _, authority) = Build("SENTETIK-finalizer", port);
        store.Stored = ApprovedReturn();

        var response = await service.GetCustomerReturnInstallmentsAsync(ReturnId);

        Assert.Equal(SalesPermissionManifestV1.ReturnsFinalize, authority.Asked[0]);
        Assert.Equal((ReturnId, "3", "40.00", true, "40.00", "0"), (response.CustomerReturnId, response.Version, response.Amount,
            response.SelectionAllowed, response.AppliedAmount, response.AdvanceAmount));
        Assert.Equal([FirstOpenItem, SecondOpenItem], response.Installments.Select(item => item.OpenItemId));
        Assert.Equal(["30.00", "10.00"], response.Installments.Select(item => item.PrefillAmount));
        Assert.Equal(0, port.Calls);
        Assert.DoesNotContain("finalize", store.Calls);
    }

    [Fact]
    public async Task The_installment_read_of_a_return_that_is_not_approved_is_409_and_finance_is_not_asked()
    {
        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Admitted);
        var (service, store, _, _) = Build("SENTETIK-finalizer", port);
        store.Stored = Draft();

        var refused = await Refused(() => service.GetCustomerReturnInstallmentsAsync(ReturnId));

        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (refused.Status, refused.Code));
        Assert.Equal(0, port.Reads);
    }

    [Fact]
    public async Task The_installment_read_without_the_finance_port_is_the_coded_503_and_without_the_permission_403()
    {
        var (service, store, _, _) = Build("SENTETIK-finalizer");
        store.Stored = ApprovedReturn();
        var unwired = await Refused(() => service.GetCustomerReturnInstallmentsAsync(ReturnId));
        Assert.Equal((503, SalesCustomerReturnProblemCodesV1.FinancePortUnavailable), (unwired.Status, unwired.Code));
        Assert.Empty(store.Calls);

        var port = new FinancePort(SalesCustomerReturnFinanceOutcomeV1.Admitted);
        var (deniedService, deniedStore, _, authority) = Build("SENTETIK-finalizer", port);
        authority.Denied.Add(SalesPermissionManifestV1.ReturnsFinalize);
        var denied = await Refused(() => deniedService.GetCustomerReturnInstallmentsAsync(ReturnId));
        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.Empty(deniedStore.Calls);
        Assert.Equal(0, port.Reads);
    }

    [Fact]
    public void The_finalisation_body_is_strict_and_its_hash_without_a_selection_is_the_earlier_state_command_hash()
    {
        Assert.Equal(new FinalizeSalesCustomerReturnRequest("3"), SalesCustomerReturnJson.ReadFinalize("{\"expectedVersion\":\"3\"}"));
        var selected = SalesCustomerReturnJson.ReadFinalize(
            "{\"expectedVersion\":\"3\",\"allocations\":[{\"openItemId\":\"a1a1a1a1-a1a1-4a1a-8a1a-a1a1a1a1a1a1\",\"amount\":\"40.00\"}]}");
        Assert.Equal((FirstOpenItem, "40.00"), (Assert.Single(selected.Allocations!).OpenItemId, selected.Allocations![0].Amount));
        foreach (var body in new[]
                 {
                     "{}", "null", "{\"expectedVersion\":\"0\"}", "{\"expectedVersion\":3}", "{\"expectedVersion\":\"3\",\"reason\":\"x\"}",
                     "{\"expectedVersion\":\"3\",\"allocations\":[]}",
                     "{\"expectedVersion\":\"3\",\"allocations\":[null]}",
                     "{\"expectedVersion\":\"3\",\"allocations\":[{\"openItemId\":\"00000000-0000-0000-0000-000000000000\",\"amount\":\"1\"}]}",
                     "{\"expectedVersion\":\"3\",\"allocations\":[{\"openItemId\":\"a1a1a1a1-a1a1-4a1a-8a1a-a1a1a1a1a1a1\",\"amount\":\"0\"}]}",
                     "{\"expectedVersion\":\"3\",\"allocations\":[{\"openItemId\":\"a1a1a1a1-a1a1-4a1a-8a1a-a1a1a1a1a1a1\",\"amount\":\"-1\"}]}",
                     "{\"expectedVersion\":\"3\",\"allocations\":[{\"openItemId\":\"a1a1a1a1-a1a1-4a1a-8a1a-a1a1a1a1a1a1\"}]}",
                 })
        {
            Assert.Throws<JsonException>(() => SalesCustomerReturnJson.ReadFinalize(body));
        }

        Assert.Equal(
            SalesCustomerReturnHash.Compute(SalesCustomerReturnCommands.FinalizeV1, ReturnId, new SalesDraftStateCommandRequest("3")),
            SalesCustomerReturnHash.ComputeFinalize(ReturnId, new FinalizeSalesCustomerReturnRequest("3")));
        Assert.NotEqual(
            SalesCustomerReturnHash.ComputeFinalize(ReturnId, new FinalizeSalesCustomerReturnRequest("3")),
            SalesCustomerReturnHash.ComputeFinalize(ReturnId,
                new FinalizeSalesCustomerReturnRequest("3", [new SalesCustomerReturnAllocationRequest(FirstOpenItem, "40.00")])));
    }

    [Fact]
    public async Task The_preparer_approving_is_a_recorded_409_self_approval_and_the_return_stays_pending_SAT_D290()
    {
        var (service, store, _, _) = Build(Preparer);
        var item = Draft();
        item.Submit(1, Preparer, Now);
        store.Stored = item;

        var refused = await Refused(() => service.ApproveCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("2"), Metadata));

        Assert.Equal((409, SalesCustomerReturnProblemCodesV1.SelfApproval), (refused.Status, refused.Code));
        Assert.Equal((SalesCustomerReturnState.PendingApproval, 2L), (store.Stored.State, store.Stored.Version));
        var recorded = Assert.Single(store.Refusals);
        Assert.Equal((SalesCustomerReturnCommands.ApproveV1, 409, SalesCustomerReturnProblemCodesV1.SelfApproval),
            (recorded.Command, recorded.Refusal.Status, recorded.Refusal.Code));
    }

    [Fact]
    public async Task Rejecting_needs_the_approvers_authority_stores_the_reason_and_the_submitter_rejecting_is_a_recorded_409()
    {
        var (service, store, _, authority) = Build(Approver);
        var item = Draft();
        item.Submit(1, "SENTETIK-submitter", Now);
        store.Stored = item;

        var result = await service.RejectCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("2", " Belge eksik "), Metadata);

        Assert.Equal(("Rejected", "3", Approver, "Belge eksik"), (result.CustomerReturn.State, result.CustomerReturn.Version,
            result.CustomerReturn.RejectedBy, result.CustomerReturn.RejectionReason));
        Assert.Equal(SalesPermissionManifestV1.ReturnsApprove, authority.Asked[0]);

        var (submitterService, submitterStore, _, _) = Build("SENTETIK-submitter");
        var pending = Draft();
        pending.Submit(1, "SENTETIK-submitter", Now);
        submitterStore.Stored = pending;
        var refused = await Refused(() =>
            submitterService.RejectCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("2"), Metadata));
        Assert.Equal((409, SalesCustomerReturnProblemCodesV1.SelfApproval), (refused.Status, refused.Code));
        Assert.Equal(SalesCustomerReturnCommands.RejectV1, Assert.Single(submitterStore.Refusals).Command);
        Assert.Equal(SalesCustomerReturnState.PendingApproval, submitterStore.Stored.State);
    }

    [Fact]
    public async Task Rejecting_without_the_approve_permission_is_403_before_any_read()
    {
        var (service, store, _, authority) = Build(Approver);
        authority.Denied.Add(SalesPermissionManifestV1.ReturnsApprove);

        var refused = await Refused(() => service.RejectCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("2"), Metadata));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task Cancelling_needs_the_write_permission_and_an_approved_return_is_a_recorded_409_state_conflict()
    {
        var (service, store, _, authority) = Build(Preparer);
        store.Stored = Draft();

        var result = await service.CancelCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("1", "Müşteri vazgeçti"), Metadata);

        Assert.Equal(("Cancelled", "2", Preparer, "Müşteri vazgeçti"), (result.CustomerReturn.State, result.CustomerReturn.Version,
            result.CustomerReturn.CancelledBy, result.CustomerReturn.CancellationReason));
        Assert.Equal(SalesPermissionManifestV1.ReturnsWrite, authority.Asked[0]);

        var (approvedService, approvedStore, _, _) = Build(Preparer);
        var approved = Draft();
        approved.Submit(1, "SENTETIK-submitter", Now);
        approved.Approve(2, Approver, Now);
        approvedStore.Stored = approved;
        var refused = await Refused(() =>
            approvedService.CancelCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("3"), Metadata));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (refused.Status, refused.Code));
        Assert.Equal(SalesCustomerReturnCommands.CancelV1, Assert.Single(approvedStore.Refusals).Command);
        Assert.Equal(SalesCustomerReturnState.Approved, approvedStore.Stored.State);
    }

    [Fact]
    public async Task A_too_long_reason_is_422_and_neither_the_decision_nor_a_refusal_is_recorded()
    {
        var (service, store, _, _) = Build(Preparer);
        store.Stored = Draft();
        var reason = new string('x', SalesCustomerReturn.MaximumReasonLength + 1);

        var refused = await Refused(() => service.CancelCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("1", reason), Metadata));

        Assert.Equal((422, SalesDraftProblemCodesV1.ApprovalReasonTooLong), (refused.Status, refused.Code));
        Assert.Empty(store.Refusals);
        Assert.Equal((SalesCustomerReturnState.Draft, 1L), (store.Stored.State, store.Stored.Version));
    }

    [Fact]
    public async Task Rejecting_or_cancelling_a_missing_return_is_404()
    {
        var (service, _, _, _) = Build(Approver);

        var rejected = await Refused(() => service.RejectCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("2"), Metadata));
        var cancelled = await Refused(() => service.CancelCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("1"), Metadata));

        Assert.Equal((404, 404), (rejected.Status, cancelled.Status));
    }

    [Fact]
    public void The_reason_is_part_of_a_decisions_request_hash_so_another_reason_under_the_same_key_is_a_different_request()
    {
        var first = SalesCustomerReturnHash.ComputeDecision(SalesCustomerReturnCommands.RejectV1, ReturnId,
            new SalesDraftStateCommandRequest("2", "A"));
        var second = SalesCustomerReturnHash.ComputeDecision(SalesCustomerReturnCommands.RejectV1, ReturnId,
            new SalesDraftStateCommandRequest("2", "B"));
        var cancel = SalesCustomerReturnHash.ComputeDecision(SalesCustomerReturnCommands.CancelV1, ReturnId,
            new SalesDraftStateCommandRequest("2", "A"));

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, cancel);
        Assert.Equal(first, SalesCustomerReturnHash.ComputeDecision(SalesCustomerReturnCommands.RejectV1, ReturnId,
            new SalesDraftStateCommandRequest("2", "A")));
    }

    [Fact]
    public async Task A_stored_answer_under_the_key_is_replayed_before_the_invoice_is_read()
    {
        var (service, store, invoices, _) = Build();
        store.Replay = new SalesCustomerReturnReplay(Draft(), null, "SENTETIK-original-correlation");

        var result = await service.CreateCustomerReturnAsync(CreateRequest(), Metadata);

        Assert.True(result.IsReplay);
        Assert.Equal(("SENTETIK-original-correlation", ReturnId), (result.OriginalCorrelationId, result.CustomerReturn.Id));
        Assert.Equal(0, invoices.Calls);
        Assert.Equal(["replay"], store.Calls);
    }

    [Fact]
    public async Task A_recorded_refusal_under_the_key_is_answered_again_as_a_replayed_refusal()
    {
        var (service, store, _, _) = Build(Preparer);
        store.Replay = new SalesCustomerReturnReplay(null,
            new SalesDraftTerminalRefusal(409, SalesCustomerReturnProblemCodesV1.SelfApproval, []), "SENTETIK-original-correlation");

        var refused = await Refused(() => service.ApproveCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("2"), Metadata));

        Assert.Equal((409, SalesCustomerReturnProblemCodesV1.SelfApproval, true), (refused.Status, refused.Code, refused.IsReplay));
        Assert.Equal(["replay"], store.Calls);
    }

    [Fact]
    public async Task The_submitters_recorded_self_rejection_is_answered_again_under_the_same_key_as_the_same_replayed_409()
    {
        var (service, store, _, _) = Build("SENTETIK-submitter");
        var pending = Draft();
        pending.Submit(1, "SENTETIK-submitter", Now);
        store.Stored = pending;
        var first = await Refused(() => service.RejectCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("2"), Metadata));
        var (command, recorded) = Assert.Single(store.Refusals);
        Assert.Equal((SalesCustomerReturnCommands.RejectV1, 409, SalesCustomerReturnProblemCodesV1.SelfApproval, false),
            (command, recorded.Status, recorded.Code, first.IsReplay));

        // The store answers the same key with the recorded refusal: nothing is read, decided or recorded a second time.
        store.Calls.Clear();
        store.Replay = new SalesCustomerReturnReplay(null, recorded, "SENTETIK-original-correlation");
        var again = await Refused(() => service.RejectCustomerReturnAsync(ReturnId, new SalesDraftStateCommandRequest("2"), Metadata));

        Assert.Equal((409, SalesCustomerReturnProblemCodesV1.SelfApproval, true, "SENTETIK-original-correlation"),
            (again.Status, again.Code, again.IsReplay, again.OriginalCorrelationId));
        Assert.Equal(["replay"], store.Calls);
        Assert.Single(store.Refusals);
        Assert.Equal(SalesCustomerReturnState.PendingApproval, store.Stored.State);
    }

    [Fact]
    public async Task Create_on_a_missing_or_other_companys_invoice_is_422_and_nothing_is_stored()
    {
        var (service, store, invoices, _) = Build();
        invoices.Invoice = null;

        var refused = await Refused(() => service.CreateCustomerReturnAsync(CreateRequest(), Metadata));

        Assert.Equal((422, SalesCustomerReturnProblemCodesV1.InvoiceNotFound), (refused.Status, refused.Code));
        Assert.DoesNotContain("store", store.Calls);
    }

    [Fact]
    public async Task Create_with_the_customers_document_already_recorded_is_409_duplicate_and_nothing_is_stored()
    {
        var (service, store, _, _) = Build();
        store.DocumentExists = true;

        var refused = await Refused(() => service.CreateCustomerReturnAsync(CreateRequest(), Metadata));

        Assert.Equal((409, SalesCustomerReturnProblemCodesV1.DocumentDuplicate), (refused.Status, refused.Code));
        Assert.DoesNotContain("store", store.Calls);
    }

    [Fact]
    public async Task Create_stores_one_draft_with_the_preparer_and_the_amounts_copied_from_the_original_line()
    {
        var (service, store, _, _) = Build();

        var result = await service.CreateCustomerReturnAsync(CreateRequest(), Metadata);

        Assert.False(result.IsReplay);
        Assert.Equal(("Draft", "1", Preparer, "33.33", "40.00"), (result.CustomerReturn.State, result.CustomerReturn.Version,
            result.CustomerReturn.CreatedBy, result.CustomerReturn.Totals.NetAmount, result.CustomerReturn.Totals.LineTotalAmount));
        Assert.Equal(1, store.Calls.Count(call => call == "store"));
    }

    [Fact]
    public async Task Create_without_the_price_read_is_403_and_nothing_is_read()
    {
        var (service, store, invoices, authority) = Build();
        authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);

        var refused = await Refused(() => service.CreateCustomerReturnAsync(CreateRequest(), Metadata));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Empty(store.Calls);
        Assert.Equal(0, invoices.Calls);
    }

    [Fact]
    public async Task An_unlinked_return_of_shipped_lines_is_rejected_with_its_own_code_and_nothing_is_stored_SAT_D293()
    {
        var (service, store, _, _) = Build();

        var refused = await Refused(() => service.CreateCustomerReturnAsync(CreateRequest() with { ShipmentId = null }, Metadata));

        Assert.Equal((422, SalesCustomerReturnProblemCodesV1.UnshippedBalanceExceeded), (refused.Status, refused.Code));
        Assert.DoesNotContain("store", store.Calls);
    }

    private sealed class CompanyContext : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId => Company;
        public void Set(Guid companyId) => throw new NotSupportedException();
    }

    private sealed class User(string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(Denied.Contains(permission)
                ? PermissionScope.Denied
                : new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(),
                    new HashSet<Guid>()));
        }
    }

    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "SENTETIK-correlation";
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    private sealed class FinancePort(SalesCustomerReturnFinanceOutcomeV1 outcome, string? reason = "SENTETIK-reason")
        : ISalesCustomerReturnFinancePortV1
    {
        public int Calls { get; private set; }
        public int Reads { get; private set; }
        public SalesCustomerReturnFinanceOfferV1? Offer { get; private set; }
        public SalesCustomerReturnInstallmentQueryV1? Query { get; private set; }

        /// <summary>The default prefill is a whole return: no selection applies, the whole amount reduces the open items.</summary>
        public SalesCustomerReturnInstallmentAnswerV1 Installments { get; set; } = new(SalesCustomerReturnFinanceOutcomeV1.Admitted, null,
            false, "40.00", "0", [new SalesCustomerReturnFinanceInstallmentV1(FirstOpenItem, Day.AddDays(30), "40.00", "40.00", "40.00")]);

        public Task<SalesCustomerReturnFinanceAnswerV1> OfferAsync(SalesCustomerReturnFinanceOfferV1 offer,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Offer = offer;
            return Task.FromResult(new SalesCustomerReturnFinanceAnswerV1(offer.CustomerReturnId, outcome, reason));
        }

        public Task<SalesCustomerReturnInstallmentAnswerV1> ReadInstallmentsAsync(SalesCustomerReturnInstallmentQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            Query = query;
            return Task.FromResult(Installments);
        }
    }

    private sealed class Store : ISalesCustomerReturnStore
    {
        public List<string> Calls { get; } = [];
        public SalesCustomerReturnReplay? Replay { get; set; }
        public SalesCustomerReturn? Stored { get; set; }
        public bool DocumentExists { get; set; }
        public List<(string Command, SalesDraftTerminalRefusal Refusal)> Refusals { get; } = [];

        public Task<SalesCustomerReturnReplay?> FindCustomerReturnReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            string commandName, CancellationToken cancellationToken = default)
        {
            Calls.Add("replay");
            return Task.FromResult(Replay);
        }

        public Task<SalesCustomerReturnStoreResult> StoreCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            Func<DateTimeOffset, SalesCustomerReturn> build, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            Calls.Add("store");
            Stored = build(Now);
            return Task.FromResult(new SalesCustomerReturnStoreResult(Stored, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task RecordCustomerReturnRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid customerReturnId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
        {
            Calls.Add("refusal");
            Refusals.Add((commandName, refusal));
            return Task.CompletedTask;
        }

        public Task<SalesCustomerReturnStoreResult?> DecideCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            string commandName, Guid customerReturnId, Action<SalesCustomerReturn, DateTimeOffset> mutate, SalesDraftStateAudit audit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("decide");
            if (Stored is null || Stored.Id != customerReturnId)
            {
                return Task.FromResult<SalesCustomerReturnStoreResult?>(null);
            }

            mutate(Stored, Now);
            return Task.FromResult<SalesCustomerReturnStoreResult?>(
                new SalesCustomerReturnStoreResult(Stored, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesCustomerReturnStoreResult?> FinalizeCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            Guid customerReturnId, int finalizedBefore, Action<SalesCustomerReturn, DateTimeOffset> finalize, SalesDraftStateAudit audit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("finalize");
            if (Stored is null || Stored.Id != customerReturnId)
            {
                return Task.FromResult<SalesCustomerReturnStoreResult?>(null);
            }

            finalize(Stored, Now);
            return Task.FromResult<SalesCustomerReturnStoreResult?>(
                new SalesCustomerReturnStoreResult(Stored, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesCustomerReturn?> FindCustomerReturnAsync(Guid customerReturnId, CancellationToken cancellationToken = default)
        {
            Calls.Add("find");
            return Task.FromResult(Stored is not null && Stored.Id == customerReturnId ? Stored : null);
        }

        public Task<IReadOnlyList<SalesCustomerReturn>> ListCustomerReturnsAsync(Guid? originalInvoiceId, int limit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<SalesCustomerReturn>>(Stored is null ? [] : [Stored]);
        }

        public Task<IReadOnlyList<SalesCustomerReturn>> ListFinalizedCustomerReturnsAsync(Guid originalInvoiceId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("finalized");
            return Task.FromResult<IReadOnlyList<SalesCustomerReturn>>([]);
        }

        public Task<bool> CustomerDocumentExistsAsync(Guid partyId, string customerDocumentNo, CancellationToken cancellationToken = default)
        {
            Calls.Add("document");
            return Task.FromResult(DocumentExists);
        }
    }

    /// <summary>The invoice store answers only <c>FindInvoiceAsync</c>; any other call is a test failure.</summary>
    public class InvoiceProxy : DispatchProxy
    {
        public ISalesInvoiceStore Service => (ISalesInvoiceStore)(object)this;
        public SalesInvoice? Invoice { get; set; }
        public int Calls { get; private set; }

        public static InvoiceProxy Create() => (InvoiceProxy)(object)DispatchProxy.Create<ISalesInvoiceStore, InvoiceProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ISalesInvoiceStore.FindInvoiceAsync))
            {
                Calls++;
                return Task.FromResult(Invoice is not null && Invoice.Id == (Guid)args![0]! ? Invoice : null);
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
