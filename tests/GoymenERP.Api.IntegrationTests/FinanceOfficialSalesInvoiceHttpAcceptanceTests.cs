using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-005 D1 over HTTP (FNS-D17…FNS-D21, FNS-D26; ADR-011 §4/§7). The shipped runtime grants neither name. With both
/// names granted, the real service reserves the id (#66 6860586175), records, replays, refuses the maker as approver,
/// lets a second person approve and replays the recorded terminal refusals. FNS-D39: an approver other than the
/// recorder rejects a pending record with a reason, the recorder withdraws their own pending record, and the official
/// number and ETTN of a rejected or withdrawn record are free again. The idempotency header and the strict body answer
/// before the service.
/// </summary>
public sealed class FinanceOfficialSalesInvoiceHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    // The development-bootstrap subjects resolve to these Identity user ids before Finance is called.
    private const string Recorder = "20000000-0000-0000-0000-000000000003";
    private const string Checker = "20000000-0000-0000-0000-000000000001";
    private const string Replayed = "Idempotency-Replayed";
    private const string OriginalCorrelation = "Idempotency-Original-Correlation-Id";
    private static readonly Guid Customer = Guid.Parse("15000000-0000-0000-0000-000000000001");
    private static readonly Guid Currency = Guid.Parse("15000000-0000-0000-0000-000000000002");
    private static readonly Guid Pdf = Guid.Parse("15000000-0000-0000-0000-000000000003");
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    private static string Path => FinanceEndpoints.OfficialSalesInvoicesPath;
    private static string ReservationsPath => Path + "/reservations";

    [Fact]
    public async Task Shipped_runtime_grants_neither_name_so_every_route_is_forbidden_and_nothing_is_stored()
    {
        var invoiceId = Guid.NewGuid();
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        var reservationsBefore = await ReservationCountAsync();

        using var reserve = await client.PostAsJsonAsync(ReservationsPath,
            new FinanceOfficialSalesInvoiceReservationRequestV1(), Json);
        await AssertProblemAsync(reserve, HttpStatusCode.Forbidden, "forbidden");
        PrivateNoStore(reserve);
        using var record = await client.PostAsJsonAsync(Path, Request(invoiceId), Json);
        await AssertProblemAsync(record, HttpStatusCode.Forbidden, "forbidden");
        PrivateNoStore(record);
        using var approve = await client.PostAsJsonAsync(ApprovalPath(invoiceId), Approval(invoiceId), Json);
        await AssertProblemAsync(approve, HttpStatusCode.Forbidden, "forbidden");
        using var reject = await client.PostAsJsonAsync(RejectionPath(invoiceId), Rejection(invoiceId), Json);
        await AssertProblemAsync(reject, HttpStatusCode.Forbidden, "forbidden");
        PrivateNoStore(reject);
        using var withdraw = await client.PostAsJsonAsync(WithdrawalPath(invoiceId), Withdrawal(invoiceId), Json);
        await AssertProblemAsync(withdraw, HttpStatusCode.Forbidden, "forbidden");
        PrivateNoStore(withdraw);
        using var detail = await client.GetAsync(DetailPath(invoiceId));
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        using var list = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        using var anonymous = factory.CreateClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync(Path, Request(invoiceId), Json);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.False(await finance.OfficialSalesInvoices.IgnoreQueryFilters().AnyAsync(x => x.Id == invoiceId));
        Assert.False(await finance.OfficialSalesInvoiceCommands.IgnoreQueryFilters()
            .AnyAsync(x => x.InvoiceId == invoiceId));
        Assert.Equal(reservationsBefore, await ReservationCountAsync());
    }

    private async Task<int> ReservationCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().OfficialSalesInvoiceReservations
            .IgnoreQueryFilters().CountAsync();
    }

    [Fact]
    public async Task Granted_actors_record_replay_refuse_the_maker_and_approve_by_a_second_person()
    {
        using var host = RealHost();
        using var recorder = Client(host, TestUsers.GroupAdmin);
        using var checker = Client(host, TestUsers.CompanyA);
        var invoiceId = await ReserveAsync(recorder);
        var request = Request(invoiceId);

        var recordKey = Key();
        var recordCorrelation = Correlation();
        using var recorded = await SendAsync(recorder, Path, request, recordKey, recordCorrelation);
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        PrivateNoStore(recorded);
        Assert.False(recorded.Headers.Contains(Replayed));
        var first = await ResultAsync(recorded);
        Assert.Equal((CommandOutcome.Succeeded, false, recordCorrelation), (first.Outcome, first.IsReplay,
            first.OriginalCorrelationId));
        Assert.Empty(first.Issues);
        var pending = Assert.IsType<FinanceOfficialSalesInvoiceViewV1>(first.Resource);
        Assert.Equal((invoiceId, TestCompanyIds.CompanyA, request.OfficialNo, FinanceOfficialSalesInvoiceStatusV1.PendingApproval,
                "1", Recorder, (string?)null),
            (pending.InvoiceId, pending.CompanyId, pending.OfficialNo, pending.Status, pending.Version,
                pending.RecordedBy, pending.ApprovedBy));
        Assert.Equal((Pdf, 1, ShaFor(invoiceId)),
            (pending.PdfDocumentId, pending.PdfDocumentVersionNo, pending.PdfSha256));
        Assert.Null(first.ExistingOfficialNo);

        using var replayed = await SendAsync(recorder, Path, request, recordKey, Correlation());
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        AssertReplay(replayed, recordCorrelation);
        var replay = await ResultAsync(replayed);
        Assert.Equal((CommandOutcome.Succeeded, true), (replay.Outcome, replay.IsReplay));
        Assert.Equal(pending, replay.Resource);

        using var mismatch = await SendAsync(recorder, Path, request with { OfficialNo = request.OfficialNo + "X" },
            recordKey, Correlation());
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        var conflict = await ResultAsync(mismatch);
        Assert.Equal(CommandOutcome.IdempotencyConflict, conflict.Outcome);
        Assert.Null(conflict.Resource);
        Assert.Empty(conflict.Issues);

        // The maker as approver is a refusal on the merits: answered, never stored, so the same key answers it again.
        var selfKey = Key();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var self = await SendAsync(recorder, ApprovalPath(invoiceId), Approval(invoiceId), selfKey, Correlation());
            await AssertRefusalAsync(self, HttpStatusCode.UnprocessableEntity, CommandOutcome.ValidationFailed,
                "approver", "approver_must_differ_from_recorder");
            Assert.False(self.Headers.Contains(Replayed));
        }

        var approveKey = Key();
        var approveCorrelation = Correlation();
        using var approved = await SendAsync(checker, ApprovalPath(invoiceId), Approval(invoiceId), approveKey,
            approveCorrelation);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.False(approved.Headers.Contains(Replayed));
        var view = Assert.IsType<FinanceOfficialSalesInvoiceViewV1>((await ResultAsync(approved)).Resource);
        Assert.Equal((FinanceOfficialSalesInvoiceStatusV1.Approved, "2", Recorder, Checker),
            (view.Status, view.Version, view.RecordedBy, view.ApprovedBy));
        Assert.Equal(pending with
        {
            Status = view.Status, Version = view.Version, ApprovedBy = view.ApprovedBy, ApprovedAtUtc = view.ApprovedAtUtc,
        }, view);
        using var approvedAgain = await SendAsync(checker, ApprovalPath(invoiceId), Approval(invoiceId), approveKey,
            Correlation());
        Assert.Equal(HttpStatusCode.OK, approvedAgain.StatusCode);
        AssertReplay(approvedAgain, approveCorrelation);
        Assert.Equal(view, (await ResultAsync(approvedAgain)).Resource);

        using var detail = await checker.GetAsync(DetailPath(invoiceId));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        PrivateNoStore(detail);
        Assert.Equal(view, await detail.Content.ReadFromJsonAsync<FinanceOfficialSalesInvoiceViewV1>(Json));
        using var list = await checker.GetAsync(Path + "?pendingOnly=true");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = Assert.IsType<FinanceOfficialSalesInvoicePageV1>(
            await list.Content.ReadFromJsonAsync<FinanceOfficialSalesInvoicePageV1>(Json));
        Assert.Equal(TestCompanyIds.CompanyA, page.CompanyId);
        Assert.DoesNotContain(page.Items, item => item.InvoiceId == invoiceId);
    }

    [Fact]
    public async Task Granted_actors_reject_with_a_reason_or_withdraw_their_own_pending_record_and_reuse_its_number()
    {
        using var host = RealHost();
        using var recorder = Client(host, TestUsers.GroupAdmin);
        using var checker = Client(host, TestUsers.CompanyA);
        var rejectedId = await ReserveAsync(recorder);
        var original = Request(rejectedId);
        using var recorded = await SendAsync(recorder, Path, original, Key(), Correlation());
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        var pending = Assert.IsType<FinanceOfficialSalesInvoiceViewV1>((await ResultAsync(recorded)).Resource);

        // The maker as rejecter and a missing reason are refusals on the merits: answered, never stored.
        using var self = await SendAsync(recorder, RejectionPath(rejectedId), Rejection(rejectedId), Key(),
            Correlation());
        await AssertRefusalAsync(self, HttpStatusCode.UnprocessableEntity, CommandOutcome.ValidationFailed,
            "approver", "approver_must_differ_from_recorder");
        using var blank = await SendAsync(checker, RejectionPath(rejectedId),
            Rejection(rejectedId) with { Reason = " " }, Key(), Correlation());
        await AssertRefusalAsync(blank, HttpStatusCode.UnprocessableEntity, CommandOutcome.ValidationFailed,
            "reason", "reason_required");
        using var foreignWithdrawal = await SendAsync(checker, WithdrawalPath(rejectedId), Withdrawal(rejectedId),
            Key(), Correlation());
        await AssertRefusalAsync(foreignWithdrawal, HttpStatusCode.UnprocessableEntity,
            CommandOutcome.ValidationFailed, "withdrawer", "withdrawer_must_be_recorder");

        var rejectKey = Key();
        var rejectCorrelation = Correlation();
        using var rejected = await SendAsync(checker, RejectionPath(rejectedId), Rejection(rejectedId), rejectKey,
            rejectCorrelation);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        PrivateNoStore(rejected);
        Assert.False(rejected.Headers.Contains(Replayed));
        var rejectedView = Assert.IsType<FinanceOfficialSalesInvoiceViewV1>((await ResultAsync(rejected)).Resource);
        Assert.Equal((FinanceOfficialSalesInvoiceStatusV1.Rejected, "2", Checker, Rejection(rejectedId).Reason),
            (rejectedView.Status, rejectedView.Version, rejectedView.RejectedBy, rejectedView.RejectionReason));
        Assert.NotNull(rejectedView.RejectedAtUtc);
        Assert.Equal(pending with
        {
            Status = rejectedView.Status, Version = rejectedView.Version, RejectedBy = rejectedView.RejectedBy,
            RejectedAtUtc = rejectedView.RejectedAtUtc, RejectionReason = rejectedView.RejectionReason,
        }, rejectedView);
        using var rejectedAgain = await SendAsync(checker, RejectionPath(rejectedId), Rejection(rejectedId),
            rejectKey, Correlation());
        Assert.Equal(HttpStatusCode.OK, rejectedAgain.StatusCode);
        AssertReplay(rejectedAgain, rejectCorrelation);
        Assert.Equal(rejectedView, (await ResultAsync(rejectedAgain)).Resource);

        // A rejected record never changes again: a recorded terminal conflict for the recorder's withdrawal too.
        using var late = await SendAsync(recorder, WithdrawalPath(rejectedId),
            Withdrawal(rejectedId) with { ExpectedVersion = "2" }, Key(), Correlation());
        await AssertRefusalAsync(late, HttpStatusCode.Conflict, CommandOutcome.VersionConflict, "expectedVersion",
            "version_conflict");

        // FNS-D39: the rejected record's official number and ETTN are free for a new record on a new reservation.
        var withdrawnId = await ReserveAsync(recorder);
        var reused = Request(withdrawnId) with { OfficialNo = original.OfficialNo, Ettn = original.Ettn };
        using var again = await SendAsync(recorder, Path, reused, Key(), Correlation());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.PendingApproval,
            Assert.IsType<FinanceOfficialSalesInvoiceViewV1>((await ResultAsync(again)).Resource).Status);

        var withdrawKey = Key();
        var withdrawCorrelation = Correlation();
        using var withdrawn = await SendAsync(recorder, WithdrawalPath(withdrawnId), Withdrawal(withdrawnId),
            withdrawKey, withdrawCorrelation);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        PrivateNoStore(withdrawn);
        var withdrawnView = Assert.IsType<FinanceOfficialSalesInvoiceViewV1>((await ResultAsync(withdrawn)).Resource);
        Assert.Equal((FinanceOfficialSalesInvoiceStatusV1.Withdrawn, "2", Recorder, (string?)null, (string?)null),
            (withdrawnView.Status, withdrawnView.Version, withdrawnView.WithdrawnBy, withdrawnView.ApprovedBy,
                withdrawnView.RejectedBy));
        Assert.NotNull(withdrawnView.WithdrawnAtUtc);
        using var withdrawnAgain = await SendAsync(recorder, WithdrawalPath(withdrawnId), Withdrawal(withdrawnId),
            withdrawKey, Correlation());
        Assert.Equal(HttpStatusCode.OK, withdrawnAgain.StatusCode);
        AssertReplay(withdrawnAgain, withdrawCorrelation);
        Assert.Equal(withdrawnView, (await ResultAsync(withdrawnAgain)).Resource);

        // Both stay readable with their trail; the pending list holds neither.
        using var detail = await checker.GetAsync(DetailPath(rejectedId));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(rejectedView, await detail.Content.ReadFromJsonAsync<FinanceOfficialSalesInvoiceViewV1>(Json));
        using var list = await checker.GetAsync(Path + "?pendingOnly=true");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = Assert.IsType<FinanceOfficialSalesInvoicePageV1>(
            await list.Content.ReadFromJsonAsync<FinanceOfficialSalesInvoicePageV1>(Json));
        Assert.DoesNotContain(page.Items, item => item.InvoiceId == rejectedId || item.InvoiceId == withdrawnId);
    }

    [Fact]
    public async Task A_stale_version_and_an_unknown_record_are_stored_refusals_that_replay_under_their_key()
    {
        using var host = RealHost();
        using var recorder = Client(host, TestUsers.GroupAdmin);
        using var checker = Client(host, TestUsers.CompanyA);
        var invoiceId = await ReserveAsync(recorder);
        using var recorded = await SendAsync(recorder, Path, Request(invoiceId), Key(), Correlation());
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);

        var staleKey = Key();
        var staleCorrelation = Correlation();
        var stale = Approval(invoiceId) with { ExpectedVersion = "2" };
        using var refused = await SendAsync(checker, ApprovalPath(invoiceId), stale, staleKey, staleCorrelation);
        await AssertRefusalAsync(refused, HttpStatusCode.Conflict, CommandOutcome.VersionConflict, "expectedVersion",
            "version_conflict");
        Assert.False(refused.Headers.Contains(Replayed));
        using var refusedAgain = await SendAsync(checker, ApprovalPath(invoiceId), stale, staleKey, Correlation());
        await AssertRefusalAsync(refusedAgain, HttpStatusCode.Conflict, CommandOutcome.VersionConflict,
            "expectedVersion", "version_conflict");
        AssertReplay(refusedAgain, staleCorrelation);

        var unknownId = Guid.NewGuid();
        var unknownKey = Key();
        var unknownCorrelation = Correlation();
        using var missing = await SendAsync(checker, ApprovalPath(unknownId), Approval(unknownId), unknownKey,
            unknownCorrelation);
        await AssertRefusalAsync(missing, HttpStatusCode.UnprocessableEntity, CommandOutcome.ValidationFailed,
            "invoiceId", "invoice_not_found");
        using var missingAgain = await SendAsync(checker, ApprovalPath(unknownId), Approval(unknownId), unknownKey,
            Correlation());
        await AssertRefusalAsync(missingAgain, HttpStatusCode.UnprocessableEntity, CommandOutcome.ValidationFailed,
            "invoiceId", "invoice_not_found");
        AssertReplay(missingAgain, unknownCorrelation);

        // The stored refusal never touched the record: it is still pending at version 1.
        using var detail = await checker.GetAsync(DetailPath(invoiceId));
        var view = Assert.IsType<FinanceOfficialSalesInvoiceViewV1>(
            await detail.Content.ReadFromJsonAsync<FinanceOfficialSalesInvoiceViewV1>(Json));
        Assert.Equal((FinanceOfficialSalesInvoiceStatusV1.PendingApproval, "1"), (view.Status, view.Version));
    }

    [Fact]
    public async Task A_reservation_replays_under_its_key_and_another_actor_under_the_key_is_a_conflict()
    {
        using var host = RealHost();
        using var recorder = Client(host, TestUsers.GroupAdmin);
        using var checker = Client(host, TestUsers.CompanyA);
        var key = Key();
        var correlation = Correlation();

        using var reserved = await SendAsync(recorder, ReservationsPath,
            new FinanceOfficialSalesInvoiceReservationRequestV1(), key, correlation);
        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        PrivateNoStore(reserved);
        Assert.False(reserved.Headers.Contains(Replayed));
        var first = await ReservationAsync(reserved);
        Assert.Equal((CommandOutcome.Succeeded, false, correlation), (first.Outcome, first.IsReplay,
            first.OriginalCorrelationId));
        var reservation = Assert.IsType<FinanceOfficialSalesInvoiceReservationV1>(first.Resource);
        Assert.NotEqual(Guid.Empty, reservation.InvoiceId);
        Assert.Equal((TestCompanyIds.CompanyA, Recorder), (reservation.CompanyId, reservation.ReservedBy));

        using var replayed = await SendAsync(recorder, ReservationsPath,
            new FinanceOfficialSalesInvoiceReservationRequestV1(), key, Correlation());
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        AssertReplay(replayed, correlation);
        Assert.Equal(reservation, (await ReservationAsync(replayed)).Resource);

        // The approver holds the key's name but not its first actor: a conflict, never the recorder's id.
        using var other = await SendAsync(checker, ReservationsPath,
            new FinanceOfficialSalesInvoiceReservationRequestV1(), key, Correlation());
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        var conflict = await ReservationAsync(other);
        Assert.Equal(CommandOutcome.IdempotencyConflict, conflict.Outcome);
        Assert.Null(conflict.Resource);

        // A reservation is the recorder's own: another actor recording on it is refused before the PDF check.
        using var foreign = await SendAsync(checker, Path, Request(reservation.InvoiceId), Key(), Correlation());
        await AssertRefusalAsync(foreign, HttpStatusCode.UnprocessableEntity, CommandOutcome.ValidationFailed,
            "invoiceId", "reservation_not_found");
        using var unreserved = await SendAsync(recorder, Path, Request(Guid.NewGuid()), Key(), Correlation());
        await AssertRefusalAsync(unreserved, HttpStatusCode.UnprocessableEntity, CommandOutcome.ValidationFailed,
            "invoiceId", "reservation_not_found");
    }

    [Fact]
    public async Task The_same_pdf_content_in_a_second_record_answers_422_with_the_first_official_number()
    {
        var firstPdf = Guid.NewGuid();
        var secondPdf = Guid.NewGuid();
        var sameContent = ShaFor(Guid.NewGuid());
        using var host = RealHost(new CleanPdf(new Dictionary<Guid, string>
        {
            [firstPdf] = sameContent,
            [secondPdf] = sameContent,
        }));
        using var recorder = Client(host, TestUsers.GroupAdmin);
        var firstId = await ReserveAsync(recorder);
        var secondId = await ReserveAsync(recorder);
        var firstRequest = Request(firstId) with { PdfDocumentId = firstPdf };
        using var recorded = await SendAsync(recorder, Path, firstRequest, Key(), Correlation());
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);

        var key = Key();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var refused = await SendAsync(recorder, Path, Request(secondId) with { PdfDocumentId = secondPdf },
                key, Correlation());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            PrivateNoStore(refused);
            Assert.False(refused.Headers.Contains(Replayed));
            var result = await ResultAsync(refused);
            Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
            Assert.Null(result.Resource);
            Assert.Equal(new[] { new ValidationIssue("pdfDocumentId", "pdf_already_recorded") }, result.Issues);
            Assert.Equal(firstRequest.OfficialNo, result.ExistingOfficialNo);
            using var raw = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Equal(firstRequest.OfficialNo, raw.RootElement.GetProperty("existingOfficialNo").GetString());
        }

        await using var scope = host.Services.CreateAsyncScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.False(await finance.OfficialSalesInvoices.IgnoreQueryFilters().AnyAsync(x => x.Id == secondId));
        Assert.False(await finance.OfficialSalesInvoiceCommands.IgnoreQueryFilters()
            .AnyAsync(x => x.InvoiceId == secondId && x.CommandName == "finance.official-sales-invoices.record"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("padded")]
    [InlineData("two-values")]
    [InlineData("too-long")]
    [InlineData("comma")]
    public async Task A_non_canonical_idempotency_key_is_refused_before_the_service(string fault)
    {
        var service = new CapturingService();
        using var host = FakeHost(service);
        using var client = Client(host, TestUsers.GroupAdmin);
        var invoiceId = Guid.NewGuid();
        foreach (var (path, body) in Commands(invoiceId))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", Correlation());
            switch (fault)
            {
                case "missing":
                    break;
                case "two-values":
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", ["SENTETIK-a", "SENTETIK-b"]);
                    break;
                default:
                    request.Headers.TryAddWithoutValidation("Idempotency-Key", fault switch
                    {
                        "blank" => " ",
                        "padded" => " SENTETIK-k",
                        "too-long" => new string('k', 129),
                        "comma" => "SENTETIK-a,SENTETIK-b",
                        _ => throw new ArgumentOutOfRangeException(nameof(fault)),
                    });
                    break;
            }

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            PrivateNoStore(response);
        }

        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("record-unknown-member")]
    [InlineData("record-missing-member")]
    [InlineData("record-not-json")]
    [InlineData("approve-unknown-member")]
    [InlineData("approve-missing-member")]
    [InlineData("approve-other-route-id")]
    [InlineData("approve-not-json")]
    [InlineData("reserve-unknown-member")]
    [InlineData("reserve-not-json")]
    [InlineData("reject-unknown-member")]
    [InlineData("reject-missing-member")]
    [InlineData("reject-other-route-id")]
    [InlineData("reject-not-json")]
    [InlineData("withdraw-unknown-member")]
    [InlineData("withdraw-missing-member")]
    [InlineData("withdraw-other-route-id")]
    [InlineData("withdraw-not-json")]
    public async Task The_strict_body_and_the_route_identity_are_refused_before_the_service(string fault)
    {
        var service = new CapturingService();
        using var host = FakeHost(service);
        using var client = Client(host, TestUsers.GroupAdmin);
        var invoiceId = Guid.NewGuid();
        var (path, body) = Commands(invoiceId)[fault[..fault.IndexOf('-', StringComparison.Ordinal)] switch
        {
            "record" => 0,
            "approve" => 1,
            "reserve" => 2,
            "reject" => 3,
            "withdraw" => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        }];
        if (fault.EndsWith("unknown-member", StringComparison.Ordinal)) body["note"] = "SENTETIK";
        if (fault == "record-missing-member") body.Remove("taxBaseFunctionalAmount");
        if (fault == "approve-missing-member") body.Remove("expectedVersion");
        if (fault == "reject-missing-member") body.Remove("reason");
        if (fault == "withdraw-missing-member") body.Remove("expectedVersion");
        if (fault == "approve-other-route-id") path = ApprovalPath(Guid.NewGuid());
        if (fault == "reject-other-route-id") path = RejectionPath(Guid.NewGuid());
        if (fault == "withdraw-other-route-id") path = WithdrawalPath(Guid.NewGuid());
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8,
                fault.EndsWith("not-json", StringComparison.Ordinal) ? "text/plain" : "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Key());
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", Correlation());

        using var response = await client.SendAsync(request);

        if (fault.EndsWith("not-json", StringComparison.Ordinal))
        {
            // The route's JSON-only Accepts metadata answers 415 in routing, before the handler runs.
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            PrivateNoStore(response);
        }

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task The_server_context_and_the_canonical_key_reach_the_service_unchanged()
    {
        var service = new CapturingService();
        using var host = FakeHost(service);
        using var client = Client(host, TestUsers.GroupAdmin);
        var invoiceId = Guid.NewGuid();
        var key = new string('k', 128);
        var correlation = Correlation();
        var record = Request(invoiceId);

        using var reserved = await SendAsync(client, ReservationsPath,
            new FinanceOfficialSalesInvoiceReservationRequestV1(), key, correlation);
        using var recorded = await SendAsync(client, Path, record, key, correlation);
        using var approved = await SendAsync(client, ApprovalPath(invoiceId), Approval(invoiceId), key, correlation);
        using var rejected = await SendAsync(client, RejectionPath(invoiceId), Rejection(invoiceId), key,
            correlation);
        using var withdrawn = await SendAsync(client, WithdrawalPath(invoiceId), Withdrawal(invoiceId), key,
            correlation);

        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        Assert.Equal(5, service.Calls);
        Assert.Equal(record, service.Recorded);
        Assert.Equal(Approval(invoiceId), service.Approved);
        Assert.Equal(Rejection(invoiceId), service.Rejected);
        Assert.Equal(Withdrawal(invoiceId), service.Withdrawn);
        Assert.All(service.Contexts, context =>
            Assert.Equal((TestCompanyIds.CompanyA, Recorder, correlation), (context.CompanyId, context.ActorId,
                context.CorrelationId)));
        Assert.All(service.Keys, value => Assert.Equal(key, value));
    }

    [Theory]
    [InlineData(CommandOutcome.Denied, HttpStatusCode.Forbidden, "forbidden")]
    [InlineData(CommandOutcome.Unavailable, HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    public async Task Denied_and_unavailable_answer_the_platform_problem_without_the_service_detail(
        CommandOutcome outcome, HttpStatusCode status, string code)
    {
        var service = new CapturingService { Outcome = outcome };
        using var host = FakeHost(service);
        using var client = Client(host, TestUsers.GroupAdmin);
        var invoiceId = Guid.NewGuid();

        using var reserved = await SendAsync(client, ReservationsPath,
            new FinanceOfficialSalesInvoiceReservationRequestV1(), Key(), Correlation());
        using var recorded = await SendAsync(client, Path, Request(invoiceId), Key(), Correlation());
        using var approved = await SendAsync(client, ApprovalPath(invoiceId), Approval(invoiceId), Key(), Correlation());
        using var rejected = await SendAsync(client, RejectionPath(invoiceId), Rejection(invoiceId), Key(),
            Correlation());
        using var withdrawn = await SendAsync(client, WithdrawalPath(invoiceId), Withdrawal(invoiceId), Key(),
            Correlation());

        foreach (var response in new[] { reserved, recorded, approved, rejected, withdrawn })
        {
            await AssertProblemAsync(response, status, code);
            PrivateNoStore(response);
            Assert.False(response.Headers.Contains(Replayed));
            Assert.DoesNotContain(CapturingService.ServiceDetail, await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        Assert.Equal(5, service.Calls);
    }

    [Fact]
    public async Task A_command_without_a_selected_company_is_refused_before_the_service()
    {
        var service = new CapturingService();
        using var host = FakeHost(service);
        using var client = Client(host, TestUsers.GroupAdmin, includeCompany: false);
        var invoiceId = Guid.NewGuid();

        using var reserved = await SendAsync(client, ReservationsPath,
            new FinanceOfficialSalesInvoiceReservationRequestV1(), Key(), Correlation());
        using var recorded = await SendAsync(client, Path, Request(invoiceId), Key(), Correlation());
        using var approved = await SendAsync(client, ApprovalPath(invoiceId), Approval(invoiceId), Key(), Correlation());

        await AssertProblemAsync(reserved, HttpStatusCode.BadRequest, "company_context_invalid");
        await AssertProblemAsync(recorded, HttpStatusCode.BadRequest, "company_context_invalid");
        await AssertProblemAsync(approved, HttpStatusCode.BadRequest, "company_context_invalid");
        using var rejected = await SendAsync(client, RejectionPath(invoiceId), Rejection(invoiceId), Key(),
            Correlation());
        await AssertProblemAsync(rejected, HttpStatusCode.BadRequest, "company_context_invalid");
        using var withdrawn = await SendAsync(client, WithdrawalPath(invoiceId), Withdrawal(invoiceId), Key(),
            Correlation());
        await AssertProblemAsync(withdrawn, HttpStatusCode.BadRequest, "company_context_invalid");
        Assert.Equal(0, service.Calls);
    }

    private WebApplicationFactory<Program> RealHost(CleanPdf? pdf = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var authority = new GrantedAuthority();
            services.RemoveAll<IErpAuthorizationService>();
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorizationService>(authority);
            services.AddSingleton<IErpAuthorityRevalidationService>(authority);
            services.RemoveAll<IFinanceOfficialSalesInvoiceDocumentPortV1>();
            services.AddSingleton<IFinanceOfficialSalesInvoiceDocumentPortV1>(pdf ?? new CleanPdf());
            services.RemoveAll<ISettingsMoneyCandidateReader>();
            services.AddSingleton<ISettingsMoneyCandidateReader>(new OneCurrency());
        }));

    private WebApplicationFactory<Program> FakeHost(CapturingService service) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFinanceOfficialSalesInvoiceServiceV1>();
            services.AddSingleton<IFinanceOfficialSalesInvoiceServiceV1>(service);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host, string user, bool includeCompany = true)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", user);
        if (includeCompany) client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(HttpClient client, string path, T body, string key,
        string correlation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlation);
        return await client.SendAsync(request);
    }

    private static (string Path, JsonObject Body)[] Commands(Guid invoiceId) =>
    [
        (Path, JsonSerializer.SerializeToNode(Request(invoiceId), Json)!.AsObject()),
        (ApprovalPath(invoiceId), JsonSerializer.SerializeToNode(Approval(invoiceId), Json)!.AsObject()),
        (ReservationsPath, JsonSerializer.SerializeToNode(new FinanceOfficialSalesInvoiceReservationRequestV1(), Json)!
            .AsObject()),
        (RejectionPath(invoiceId), JsonSerializer.SerializeToNode(Rejection(invoiceId), Json)!.AsObject()),
        (WithdrawalPath(invoiceId), JsonSerializer.SerializeToNode(Withdrawal(invoiceId), Json)!.AsObject()),
    ];

    /// <summary>#66 6860586175: every record consumes the recorder's own server-generated reservation.</summary>
    private static async Task<Guid> ReserveAsync(HttpClient client)
    {
        using var response = await SendAsync(client, ReservationsPath, new FinanceOfficialSalesInvoiceReservationRequestV1(),
            Key(), Correlation());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<FinanceOfficialSalesInvoiceReservationV1>((await ReservationAsync(response)).Resource)
            .InvoiceId;
    }

    /// <summary>The vault's content hash of the PDF uploaded to that record id (distinct per record).</summary>
    private static string ShaFor(Guid invoiceId) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(invoiceId.ToByteArray()));

    private static FinanceOfficialSalesInvoiceRecordRequestV1 Request(Guid invoiceId) => new(
        invoiceId, "GYM" + invoiceId.ToString("N")[..13].ToUpperInvariant(), Guid.NewGuid().ToString("D"),
        Customer, Currency, "1250.50", "1250.50", "1042.08", "208.42", "0", Pdf);

    private static FinanceOfficialSalesInvoiceApproveRequestV1 Approval(Guid invoiceId) => new(invoiceId, "1");

    private static string ApprovalPath(Guid invoiceId) => $"{Path}/{invoiceId:D}/approval";

    private static FinanceOfficialSalesInvoiceRejectRequestV1 Rejection(Guid invoiceId) =>
        new(invoiceId, "1", "SENTETIK tutar faturayla uyuşmuyor");

    private static FinanceOfficialSalesInvoiceWithdrawRequestV1 Withdrawal(Guid invoiceId) => new(invoiceId, "1");

    private static string RejectionPath(Guid invoiceId) => $"{Path}/{invoiceId:D}/rejection";

    private static string WithdrawalPath(Guid invoiceId) => $"{Path}/{invoiceId:D}/withdrawal";

    private static string DetailPath(Guid invoiceId) => $"{Path}/{invoiceId:D}";

    private static string Key() => "SENTETIK-" + Guid.NewGuid().ToString("N");

    private static string Correlation() => Guid.NewGuid().ToString("N");

    private static async Task<FinanceOfficialSalesInvoiceResultV1> ResultAsync(HttpResponseMessage response) =>
        Assert.IsType<FinanceOfficialSalesInvoiceResultV1>(
            await response.Content.ReadFromJsonAsync<FinanceOfficialSalesInvoiceResultV1>(Json));

    private static async Task<FinanceOfficialSalesInvoiceReservationResultV1> ReservationAsync(
        HttpResponseMessage response) =>
        Assert.IsType<FinanceOfficialSalesInvoiceReservationResultV1>(
            await response.Content.ReadFromJsonAsync<FinanceOfficialSalesInvoiceReservationResultV1>(Json));

    private static async Task AssertRefusalAsync(HttpResponseMessage response, HttpStatusCode status,
        CommandOutcome outcome, string field, string code)
    {
        Assert.Equal(status, response.StatusCode);
        PrivateNoStore(response);
        var result = await ResultAsync(response);
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(new[] { new ValidationIssue(field, code) }, result.Issues);
    }

    private static void AssertReplay(HttpResponseMessage response, string originalCorrelation)
    {
        Assert.Equal("true", Assert.Single(response.Headers.GetValues(Replayed)));
        Assert.Equal(originalCorrelation, Assert.Single(response.Headers.GetValues(OriginalCorrelation)));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    private static void PrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class CapturingService : IFinanceOfficialSalesInvoiceServiceV1
    {
        public const string ServiceDetail = "finance_permission_mapping_unavailable";
        public CommandOutcome Outcome { get; init; } = CommandOutcome.Succeeded;
        public int Calls { get; private set; }
        public List<FinanceContext> Contexts { get; } = [];
        public List<string> Keys { get; } = [];
        public FinanceOfficialSalesInvoiceRecordRequestV1? Recorded { get; private set; }
        public FinanceOfficialSalesInvoiceApproveRequestV1? Approved { get; private set; }
        public FinanceOfficialSalesInvoiceRejectRequestV1? Rejected { get; private set; }
        public FinanceOfficialSalesInvoiceWithdrawRequestV1? Withdrawn { get; private set; }

        public async Task<FinanceOfficialSalesInvoiceReservationResultV1> ReserveAsync(FinanceContext context,
            CommandMetadata metadata, FinanceOfficialSalesInvoiceReservationRequestV1 request,
            CancellationToken cancellationToken)
        {
            var answer = await Answer(context, metadata);
            return new FinanceOfficialSalesInvoiceReservationResultV1(answer.Outcome,
                answer.Outcome == CommandOutcome.Succeeded
                    ? new FinanceOfficialSalesInvoiceReservationV1(Guid.NewGuid(), context.CompanyId, context.ActorId,
                        DateTimeOffset.UnixEpoch)
                    : null,
                false, context.CorrelationId, answer.Issues);
        }

        public Task<FinanceOfficialSalesInvoiceResultV1> RecordAsync(FinanceContext context, CommandMetadata metadata,
            FinanceOfficialSalesInvoiceRecordRequestV1 request, CancellationToken cancellationToken)
        {
            Recorded = request;
            return Answer(context, metadata);
        }

        public Task<FinanceOfficialSalesInvoiceResultV1> ApproveAsync(FinanceContext context, CommandMetadata metadata,
            FinanceOfficialSalesInvoiceApproveRequestV1 request, CancellationToken cancellationToken)
        {
            Approved = request;
            return Answer(context, metadata);
        }

        public Task<FinanceOfficialSalesInvoiceResultV1> RejectAsync(FinanceContext context, CommandMetadata metadata,
            FinanceOfficialSalesInvoiceRejectRequestV1 request, CancellationToken cancellationToken)
        {
            Rejected = request;
            return Answer(context, metadata);
        }

        public Task<FinanceOfficialSalesInvoiceResultV1> WithdrawAsync(FinanceContext context, CommandMetadata metadata,
            FinanceOfficialSalesInvoiceWithdrawRequestV1 request, CancellationToken cancellationToken)
        {
            Withdrawn = request;
            return Answer(context, metadata);
        }

        public Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> GetAsync(FinanceContext context, Guid invoiceId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReadResult<FinanceOfficialSalesInvoicePageV1>> ListAsync(FinanceContext context, bool pendingOnly,
            string? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();

        private Task<FinanceOfficialSalesInvoiceResultV1> Answer(FinanceContext context, CommandMetadata metadata)
        {
            Calls++;
            Contexts.Add(context);
            Keys.Add(metadata.IdempotencyKey);
            IReadOnlyList<ValidationIssue> issues = Outcome == CommandOutcome.Succeeded
                ? []
                : [new ValidationIssue("permission", ServiceDetail)];
            return Task.FromResult(new FinanceOfficialSalesInvoiceResultV1(Outcome, null, false, context.CorrelationId,
                issues, null));
        }
    }

    private sealed class GrantedAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public Task<bool> HasPermissionAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Granted(permission, companyId));

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("f005-http-only", new HashSet<Guid> { TestCompanyIds.CompanyA },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId),
                    StringComparer.Ordinal)));

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(permission, companyId));

        private static bool Granted(string permission, Guid companyId) =>
            companyId == TestCompanyIds.CompanyA
            && permission is ErpPermissions.FinanceOfficialSalesInvoicesRecord
                or ErpPermissions.FinanceOfficialSalesInvoicesApprove
                or ErpPermissions.FinanceOfficialSalesInvoicesWithdraw;

        private static PermissionScope Scope(string permission, Guid companyId) =>
            new(Granted(permission, companyId), true, new HashSet<string>(StringComparer.Ordinal) { "F005-HTTP" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    }

    /// <summary>
    /// The standard PDF answers the content hash of the record id it was uploaded to; <paramref name="sameContent"/>
    /// maps other documents to a fixed hash (two uploads of one PDF).
    /// </summary>
    private sealed class CleanPdf(IReadOnlyDictionary<Guid, string>? sameContent = null)
        : IFinanceOfficialSalesInvoiceDocumentPortV1
    {
        public Task<FinanceOfficialSalesInvoicePdfCheckV1> CheckAsync(FinanceContext context, Guid documentId,
            Guid invoiceId, int? versionNo, CancellationToken cancellationToken)
        {
            string? sha = sameContent is not null && sameContent.TryGetValue(documentId, out var fixedSha) ? fixedSha
                : documentId == Pdf ? ShaFor(invoiceId) : null;
            return Task.FromResult(sha is not null && versionNo is null or 1
                ? new FinanceOfficialSalesInvoicePdfCheckV1(FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, 1, sha)
                : new FinanceOfficialSalesInvoicePdfCheckV1(FinanceOfficialSalesInvoicePdfOutcomeV1.NotFound, null,
                    null));
        }
    }

    private sealed class OneCurrency : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>(
                [new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")]);
    }
}
