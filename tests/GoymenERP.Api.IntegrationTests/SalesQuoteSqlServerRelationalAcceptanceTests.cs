using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Domain.Requests;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-22…24 against a REAL SQL Server: the quote's one-save create, revision and loss with their attempts and audit events;
/// No 163 (several requests of one customer under one quote, a request under at most one live quote); No 160 (no order while
/// a covered request is not approved); No 162 (the conversion's price/quantity snapshot into ONE order through the draft's own
/// creation path, exactly once: same-key replay, a second key refused, a two-key race storing one order, the unique source
/// index); No 161 (a request with an approved order is not rejected); company isolation; the routes through the REAL host with
/// fresh synthetic grants (the conversion needs BOTH the quote write and the order create name); the guarded rollback. The
/// service runs over the real store with SYNTHETIC owner ports: it proves the quote slice and the store, not any owner's rule.
/// Every identity is SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesQuoteSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 27);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly Guid KgUnit = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c93");
    private static readonly Guid KgProfile = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c94");
    private static readonly Guid Product = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c95");
    private static readonly Guid Currency = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c96");
    private static readonly Guid MoneyProfile = Guid.Parse("6b1d2c3b-4a59-4687-9a0b-1c2d3e4f5c97");
    private const string Approver = "SENTETIK-approver";

    [SqlServerFact]
    public async Task A_quote_covers_approved_requests_is_revised_and_converted_into_exactly_one_order_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();
        var (first, second) = (await run.RequestAsync(party, account), await run.RequestAsync(party, account));
        await run.ApproveRequestAsync(first, "q-approve-first");

        // No 163: one quote covers two requests of the same customer.
        SalesQuoteResponse created;
        await using (var scope = run.Store(CompanyA))
        {
            created = await run.Service(scope, run.Actor).CreateQuoteAsync(CreateQuote(party, account, [first, second], "12.50"), run.Metadata("q-create"));
        }

        var quoteId = created.Detail.Quote.Id;
        Assert.Equal(("Open", "1", "Rev.0"), (created.Detail.Quote.State, created.Detail.Quote.Version, created.Detail.Quote.Revision));
        Assert.Matches("^SQ-[0-9]{4}-000001$", created.Detail.Quote.DocumentNo);
        Assert.Equal(new[] { first, second }.Order(), created.Detail.RequestIds);
        Assert.Equal(("12.50", SalesPriceDisclosure.Visible), (created.Detail.Lines[0].UnitPrice, created.Detail.Lines[0].PriceDisclosure));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteRequests WHERE QuoteId = @q AND IsActive = 1", ("@q", quoteId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.QuoteAttempts WHERE CommandName = N'sales.quote.create.v1' AND QuoteId = @q AND Outcome = 1 AND ResultJson IS NULL",
            ("@q", quoteId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.Quote' AND EventType = N'sales.quote.created.v1' AND EntityId = @q "
            + "AND EntityVersionAfter = 1 AND PermissionCode = N'sales.quotes.write'", ("@q", quoteId.ToString("D"))));

        // A request is under at most one live quote: a second quote over it is refused and records nothing.
        await using (var scope = run.Store(CompanyA))
        {
            var taken = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, run.Actor).CreateQuoteAsync(CreateQuote(party, account, [first], "1"), run.Metadata("q-create-taken")));
            Assert.Equal((422, SalesQuoteProblemCodesV1.RequestUnavailable), (taken.Status, taken.Code));
            var replay = await run.Service(scope, run.Actor).CreateQuoteAsync(CreateQuote(party, account, [first, second], "12.50") with
            {
                Lines = created.Detail.Lines.Select(line => new SalesQuoteLineRequest(line.LineId, Product, "7", KgUnit, "1.25", "12.50", KgUnit)).ToList(),
            }, run.Metadata("q-create"));
            Assert.Equal((true, quoteId), (replay.IsReplay, replay.Detail.Quote.Id));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Quotes"));
        // The revision replaces lines and terms as Rev.1; Rev.0 stays as it was recorded.
        await using (var scope = run.Store(CompanyA))
        {
            var revised = await run.Service(scope, run.Actor).ReviseQuoteAsync(quoteId,
                new SalesQuoteRevisionRequest("1", [Line("13.00"), Line("13.00", "2.5")], Day.AddDays(20)), run.Metadata("q-revise"));
            Assert.Equal(("2", "Rev.1", 2), (revised.Detail.Quote.Version, revised.Detail.Quote.Revision, revised.Detail.Lines.Count));
        }

        Assert.Equal((2, 1, 2), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteRevisions WHERE QuoteId = @q", ("@q", quoteId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteLines WHERE QuoteId = @q AND Revision = 0 AND UnitPrice = N'12.50'", ("@q", quoteId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteLines WHERE QuoteId = @q AND Revision = 1 AND UnitPrice = N'13.00'", ("@q", quoteId))));

        // No 160: no order while a covered request is not approved — refused, NOT recorded (the same key may succeed later).
        await using (var scope = run.Store(CompanyA))
        {
            var unapproved = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, run.Actor).ConvertQuoteAsync(quoteId, new SalesQuoteConversionRequest("2", Day), run.Metadata("q-convert")));
            Assert.Equal((409, SalesQuoteProblemCodesV1.RequestNotApproved, "requestIds"),
                (unapproved.Status, unapproved.Code, Assert.Single(unapproved.Errors).Field));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("q-convert"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));

        // No 162: with every request approved, the conversion stores ONE order with the quote's Rev.1 snapshot and its reference.
        await run.ApproveRequestAsync(second, "q-approve-second");
        SalesOrderDraftResponse converted;
        await using (var scope = run.Store(CompanyA))
        {
            converted = await run.Service(scope, run.Actor).ConvertQuoteAsync(quoteId, new SalesQuoteConversionRequest("2", Day), run.Metadata("q-convert"));
        }

        var draftId = converted.Detail.Draft.Id;
        Assert.Equal((false, "Order", 2), (converted.IsReplay, converted.Detail.Draft.Kind, converted.Detail.Lines.Count));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Drafts WHERE Id = @d AND SourceQuoteId = @q AND SourceQuoteRevision = 1 AND CreatedBy = @a",
            ("@d", draftId), ("@q", quoteId), ("@a", run.Actor)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftLines WHERE DraftId = @d AND UnitPrice = N'13.00' AND QuantityText = N'1.25'", ("@d", draftId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftLines WHERE DraftId = @d AND DiscountPercent = N'2.5'", ("@d", draftId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Quotes WHERE Id = @q AND State = 2 AND Version = 3 AND ConvertedDraftId = @d AND ConvertedBy = @a",
            ("@q", quoteId), ("@d", draftId), ("@a", run.Actor)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftAttempts WHERE CommandName = N'sales.quote.convert.v1' AND DraftId = @d AND Outcome = 1",
            ("@d", draftId)));
        // ADR-SAL-001 §3: the conversion's ONE attempt is the order's; no quote attempt is written for it.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("q-convert"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.Quote' AND EventType = N'sales.quote.converted.v1' AND EntityId = @q "
            + "AND EntityVersionBefore = 2 AND EntityVersionAfter = 3", ("@q", quoteId.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft' AND EntityId = @d", ("@d", draftId.ToString("D"))));

        // Exactly once: the same key answers the same order; another key meets the converted quote and is a recorded 409.
        await using (var scope = run.Store(CompanyA))
        {
            var replay = await run.Service(scope, run.Actor).ConvertQuoteAsync(quoteId, new SalesQuoteConversionRequest("2", Day), run.Metadata("q-convert", "SENTETIK-again"));
            Assert.Equal((true, draftId, run.Correlation), (replay.IsReplay, replay.Detail.Draft.Id, replay.OriginalCorrelationId));
            var second409 = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, run.Actor).ConvertQuoteAsync(quoteId, new SalesQuoteConversionRequest("3", Day), run.Metadata("q-convert-2")));
            Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (second409.Status, second409.Code));
            var lost = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, run.Actor).RecordQuoteLossAsync(quoteId, new SalesQuoteLossRequest("3", "Fiyat"), run.Metadata("q-lose-converted")));
            Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (lost.Status, lost.Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h AND Outcome = 2 AND ISJSON(RefusalJson) = 1",
            ("@h", SalesStore.IdempotencyKeyHash("q-convert-2"))));

        // No 161: with the order approved, a request it came from is not rejected (refused, recorded). The fail-closed cases
        // (a Preparing, revised-back, force-closed or cancelled order) are the dedicated fact below.
        await using (var scope = run.Store(CompanyA))
        {
            var submitted = await run.Service(scope, run.Actor).SubmitDraftAsync(draftId,
                new SalesDraftStateCommandRequest(converted.Detail.Draft.Version), run.Metadata("q-submit"));
            var approved = await run.Service(scope, Approver).ApproveDraftAsync(draftId,
                new SalesDraftStateCommandRequest(submitted.Detail.Draft.Version), run.Metadata("q-approve-order"));
            Assert.Equal("Approved", approved.Detail.Draft.ApprovalState);
        }

        await using (var scope = run.Store(CompanyA))
        {
            var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                run.Service(scope, Approver).RejectRequestAsync(first, new("2", "SENTETIK vazgeçildi"), run.Metadata("q-reject-first")));
            Assert.Equal((409, SalesRequestProblemCodesV1.HasOrder), (blocked.Status, blocked.Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests WHERE Id = @r AND State = 2", ("@r", first)));

        // The schema itself: one order per quote (unique source), source facts together, the quote's lifecycle facts by state.
        Guid direct;
        await using (var scope = run.Store(CompanyA))
        {
            direct = (await run.Service(scope, run.Actor).CreateDraftAsync(new CreateSalesOrderDraftRequest("Order", party, account, Day, Currency,
                [new SalesOrderDraftLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "1")]), run.Metadata("q-direct"))).Detail.Draft.Id;
        }

        var unique = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.Drafts SET SourceQuoteId = @q, SourceQuoteRevision = 1 WHERE Id = @d", ("@q", quoteId), ("@d", direct)));
        Assert.Equal(2601, unique.Number);
        Assert.Contains("UX_Drafts_SourceQuoteId", unique.Message, StringComparison.Ordinal);
        var pair = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.Drafts SET SourceQuoteRevision = NULL WHERE Id = @d", ("@d", draftId)));
        Assert.Contains("CK_Drafts_SourceQuote", pair.Message, StringComparison.Ordinal);
        var lifecycle = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.Quotes SET LossReason = N'x', LostBy = N'x', LostAtUtc = SYSUTCDATETIME() WHERE Id = @q", ("@q", quoteId)));
        Assert.Contains("CK_Quotes_Lifecycle", lifecycle.Message, StringComparison.Ordinal);
        // ADR-SAL-001 §2/§6: the quote's own attempt table takes only quote commands (never the conversion) and is append-only.
        var target = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO sales.QuoteAttempts (CompanyId, CommandName, IdempotencyKeyHash, RequestHash, ActorId, Outcome, QuoteId, CorrelationId, RecordedAtUtc) "
            + "VALUES (@c, N'sales.quote.convert.v1', REPLICATE('C', 64), REPLICATE('0', 64), N'SENTETIK', 1, @q, N'SENTETIK', SYSUTCDATETIME())",
            ("@c", CompanyA), ("@q", quoteId)));
        Assert.Equal(547, target.Number);
        Assert.Contains("CK_QuoteAttempts_Command", target.Message, StringComparison.Ordinal);
        var attempts = await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteAttempts WHERE QuoteId = @q", ("@q", quoteId));
        var update = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.QuoteAttempts SET ActorId = N'SENTETIK-other' WHERE QuoteId = @q", ("@q", quoteId)));
        Assert.Equal(51068, update.Number);
        var delete = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "DELETE FROM sales.QuoteAttempts WHERE QuoteId = @q", ("@q", quoteId)));
        Assert.Equal(51068, delete.Number);
        Assert.Equal(attempts, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.QuoteAttempts WHERE QuoteId = @q AND ActorId <> N'SENTETIK-other'", ("@q", quoteId)));
    }

    [SqlServerFact]
    public async Task A_lost_quote_frees_its_requests_and_two_conversions_under_different_keys_store_one_order()
    {
        await using var run = await Run.StartAsync();
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();
        var request = await run.RequestAsync(party, account);
        await run.ApproveRequestAsync(request, "l-approve");

        Guid lostId;
        await using (var scope = run.Store(CompanyA))
        {
            var service = run.Service(scope, run.Actor);
            lostId = (await service.CreateQuoteAsync(CreateQuote(party, account, [request], "10"), run.Metadata("l-create"))).Detail.Quote.Id;
            var blank = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                service.RecordQuoteLossAsync(lostId, new SalesQuoteLossRequest("1", " "), run.Metadata("l-lose-blank")));
            Assert.Equal((422, SalesRuleCodes.QuoteLossReasonRequired), (blank.Status, blank.Code));
            var lost = await service.RecordQuoteLossAsync(lostId, new SalesQuoteLossRequest("1", "SENTETIK fiyat yüksek"), run.Metadata("l-lose"));
            Assert.Equal(("Lost", "SENTETIK fiyat yüksek"), (lost.Detail.Quote.State, lost.Detail.Loss!.Reason));
            var notConvertible = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                service.ConvertQuoteAsync(lostId, new SalesQuoteConversionRequest("2", Day), run.Metadata("l-convert-lost")));
            Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (notConvertible.Status, notConvertible.Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Quotes WHERE Id = @q AND State = 3 AND LossReason = N'SENTETIK fiyat yüksek' AND LostBy = @a",
            ("@q", lostId), ("@a", run.Actor)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("l-lose-blank"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteRequests WHERE QuoteId = @q AND IsActive = 1", ("@q", lostId)));

        // The lost quote's request is free again: a new quote takes it.
        Guid quoteId;
        await using (var scope = run.Store(CompanyA))
        {
            quoteId = (await run.Service(scope, run.Actor).CreateQuoteAsync(CreateQuote(party, account, [request], "11"), run.Metadata("l-create-2"))).Detail.Quote.Id;
        }

        // Two conversions of the same quote under DIFFERENT keys, both past every read: the quote's version token and the unique
        // source index let exactly one order through.
        var barrier = new Barrier(2);
        async Task<object> ConvertAsync(string key)
        {
            await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return (await run.Service(scope, run.Actor).ConvertQuoteAsync(quoteId, new SalesQuoteConversionRequest("1", Day), run.Metadata(key))).Detail.Draft.Id;
            }
            catch (SalesDraftServiceException exception)
            {
                return (exception.Status, exception.Code);
            }
        }

        var results = await Task.WhenAll(Task.Run(() => ConvertAsync("l-race-a")), Task.Run(() => ConvertAsync("l-race-b")));
        var winner = Assert.IsType<Guid>(Assert.Single(results, result => result is Guid));
        var loser = Assert.IsType<(int, string)>(Assert.Single(results, result => result is ValueTuple<int, string>));
        Assert.Equal(409, loser.Item1);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE SourceQuoteId = @q", ("@q", quoteId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Quotes WHERE Id = @q AND State = 2 AND ConvertedDraftId = @d",
            ("@q", quoteId), ("@d", winner)));
    }

    [SqlServerFact]
    public async Task A_request_with_any_order_that_is_not_cancelled_is_not_rejected_and_only_a_cancelled_order_releases_it_on_real_sql()
    {
        // No 161, fail-closed (NEEDS-SINAN #6 open): only a CANCELLED order releases the request it came from.
        await using var run = await Run.StartAsync();
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();

        async Task<(Guid Request, Guid Order, SalesOrderDraftDetailView Detail)> OrderFromApprovedRequestAsync(string tag)
        {
            var request = await run.RequestAsync(party, account);
            await run.ApproveRequestAsync(request, tag + "-approve");
            await using var scope = run.Store(CompanyA);
            var service = run.Service(scope, run.Actor);
            var quote = await service.CreateQuoteAsync(CreateQuote(party, account, [request], "12.50"), run.Metadata(tag + "-quote"));
            var order = await service.ConvertQuoteAsync(quote.Detail.Quote.Id, new SalesQuoteConversionRequest("1", Day), run.Metadata(tag + "-convert"));
            return (request, order.Detail.Draft.Id, order.Detail);
        }

        async Task<SalesOrderDraftDetailView> SubmitAndApproveAsync(Guid order, string version, string tag)
        {
            await using var scope = run.Store(CompanyA);
            var submitted = await run.Service(scope, run.Actor).SubmitDraftAsync(order, new SalesDraftStateCommandRequest(version), run.Metadata(tag + "-submit"));
            var approved = await run.Service(scope, Approver).ApproveDraftAsync(order,
                new SalesDraftStateCommandRequest(submitted.Detail.Draft.Version), run.Metadata(tag + "-approve-order"));
            Assert.Equal("Approved", approved.Detail.Draft.ApprovalState);
            return approved.Detail;
        }

        async Task RefusedAsync(Guid request, string tag)
        {
            await using (var scope = run.Store(CompanyA))
            {
                var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                    run.Service(scope, Approver).RejectRequestAsync(request, new("2", "SENTETIK vazgeçildi"), run.Metadata(tag)));
                Assert.Equal((409, SalesRequestProblemCodesV1.HasOrder), (refused.Status, refused.Code));
            }

            // The refusal is terminal for its key (recorded in the request's own attempt table); the request did not move.
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM sales.RequestAttempts WHERE IdempotencyKeyHash = @h AND RequestId = @r AND Outcome = 2 AND ISJSON(RefusalJson) = 1",
                ("@h", SalesStore.IdempotencyKeyHash(tag)), ("@r", request)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests WHERE Id = @r AND State = 2 AND Version = 2", ("@r", request)));
        }

        // 1. The converted order is still Preparing (never submitted, never approved).
        var preparing = await OrderFromApprovedRequestAsync("n-preparing");
        Assert.Equal("Preparing", preparing.Detail.Draft.ApprovalState);
        await RefusedAsync(preparing.Request, "n-preparing-reject");

        // 2. Approved, then revised back to Preparing (DEC-SAL-002 quantity revision): the order is live, the request stays.
        var revised = await OrderFromApprovedRequestAsync("n-revised");
        var approvedRevised = await SubmitAndApproveAsync(revised.Order, revised.Detail.Draft.Version, "n-revised");
        await using (var scope = run.Store(CompanyA))
        {
            var back = await run.Service(scope, run.Actor).ReviseQuantityAsync(revised.Order, new SalesDraftQuantityRevisionRequest(
                approvedRevised.Draft.Version, "SENTETIK miktar değişti", [new SalesDraftLineQuantityV1(approvedRevised.Lines[0].LineId, "2")]),
                run.Metadata("n-revised-quantity"));
            Assert.Equal("Preparing", back.Detail.Draft.ApprovalState);
        }

        await RefusedAsync(revised.Request, "n-revised-reject");

        // 3. Approved, then force-closed (SalesDraftState.Closed, the state No 30's closure records; set here directly because
        //    the force-close route needs a shipment's evidence): closed is not cancelled, the request stays.
        var closed = await OrderFromApprovedRequestAsync("n-closed");
        await SubmitAndApproveAsync(closed.Order, closed.Detail.Draft.Version, "n-closed");
        await run.ExecuteAsync("UPDATE sales.Drafts SET State = 3 WHERE Id = @d", ("@d", closed.Order));
        await RefusedAsync(closed.Request, "n-closed-reject");

        // 4. Approved, then CANCELLED through its own route: the request is free to be rejected.
        var cancelled = await OrderFromApprovedRequestAsync("n-cancelled");
        var approvedCancelled = await SubmitAndApproveAsync(cancelled.Order, cancelled.Detail.Draft.Version, "n-cancelled");
        await using (var scope = run.Store(CompanyA))
        {
            var service = run.Service(scope, run.Actor);
            var impact = await service.GetCancellationImpactAsync(cancelled.Order);
            Assert.True(impact.Cancellable);
            await service.CancelDraftAsync(cancelled.Order, new SalesDraftCancelRequest(approvedCancelled.Draft.Version, "SENTETIK iptal", impact.ImpactToken),
                run.Metadata("n-cancel"));
            var rejected = await run.Service(scope, Approver).RejectRequestAsync(cancelled.Request, new("2", "SENTETIK vazgeçildi"), run.Metadata("n-cancelled-reject"));
            Assert.Equal("Rejected", rejected.Detail.Request.State);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Id = @d AND State = 2", ("@d", cancelled.Order)));

        // No 160, belt and braces: an order whose source quote covers a request that is no longer approved (here forced by a
        // direct edit, since the rejection itself is refused above) neither submits nor approves — refused, not recorded.
        var stale = await OrderFromApprovedRequestAsync("n-stale");
        var pending = await OrderFromApprovedRequestAsync("n-pending");
        string pendingVersion;
        await using (var scope = run.Store(CompanyA))
        {
            pendingVersion = (await run.Service(scope, run.Actor).SubmitDraftAsync(pending.Order,
                new SalesDraftStateCommandRequest(pending.Detail.Draft.Version), run.Metadata("n-pending-submit"))).Detail.Draft.Version;
        }

        foreach (var request in new[] { stale.Request, pending.Request })
        {
            await run.ExecuteAsync(
                "UPDATE sales.Requests SET State = 3, RejectionReason = N'SENTETIK', DecidedBy = N'SENTETIK', DecidedAtUtc = SYSUTCDATETIME() WHERE Id = @r",
                ("@r", request));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var submit = await Assert.ThrowsAsync<SalesDraftServiceException>(() => run.Service(scope, run.Actor).SubmitDraftAsync(stale.Order,
                new SalesDraftStateCommandRequest(stale.Detail.Draft.Version), run.Metadata("n-stale-submit")));
            var approve = await Assert.ThrowsAsync<SalesDraftServiceException>(() => run.Service(scope, Approver).ApproveDraftAsync(pending.Order,
                new SalesDraftStateCommandRequest(pendingVersion), run.Metadata("n-pending-approve")));
            foreach (var refused in new[] { submit, approve })
            {
                Assert.Equal((409, SalesQuoteProblemCodesV1.RequestNotApproved, "requestIds"),
                    (refused.Status, refused.Code, Assert.Single(refused.Errors).Field));
            }
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash IN (@s, @a)",
            ("@s", SalesStore.IdempotencyKeyHash("n-stale-submit")), ("@a", SalesStore.IdempotencyKeyHash("n-pending-approve"))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Id = @d AND ApprovalState = 1", ("@d", stale.Order)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE Id = @d AND ApprovalState = 2", ("@d", pending.Order)));
    }

    [SqlServerFact]
    public async Task The_conversion_rechecks_its_requests_inside_the_save_and_serializes_with_a_rejection_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();

        async Task<(Guid Request, Guid Quote)> QuotedApprovedRequestAsync(string tag)
        {
            var request = await run.RequestAsync(party, account);
            await run.ApproveRequestAsync(request, tag + "-approve");
            await using var scope = run.Store(CompanyA);
            var quote = await run.Service(scope, run.Actor).CreateQuoteAsync(CreateQuote(party, account, [request], "12.50"), run.Metadata(tag + "-quote"));
            return (request, quote.Detail.Quote.Id);
        }

        // 1. The request is rejected AFTER the service's own No 160 check (while an owner is being asked) and before the save:
        //    only the store's in-save re-check sees it — removing that check would store an order from a rejected request.
        var seam = await QuotedApprovedRequestAsync("s-seam");
        await using (var scope = run.Store(CompanyA))
        {
            var service = run.Service(scope, run.Actor, async () =>
            {
                await using var other = run.Store(CompanyA);
                await run.Service(other, Approver).RejectRequestAsync(seam.Request, new("2", "SENTETIK araya giren ret"), run.Metadata("s-seam-reject"));
            });
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
                service.ConvertQuoteAsync(seam.Quote, new SalesQuoteConversionRequest("1", Day), run.Metadata("s-seam-convert")));
            Assert.Equal((409, SalesQuoteProblemCodesV1.RequestNotApproved, "requestIds"),
                (refused.Status, refused.Code, Assert.Single(refused.Errors).Field));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests WHERE Id = @r AND State = 3", ("@r", seam.Request)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE SourceQuoteId = @q", ("@q", seam.Quote)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Quotes WHERE Id = @q AND State = 1 AND Version = 1", ("@q", seam.Quote)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("s-seam-convert"))));

        // 2. A conversion and a rejection of the same request, both past every read and held until both reach their save: the
        //    request row is a concurrency token of BOTH saves, so exactly one commits — never a rejected request with a live order.
        var race = await QuotedApprovedRequestAsync("s-race");
        var barrier = new Barrier(2);
        async Task<object> ConvertAsync()
        {
            await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return (await run.Service(scope, run.Actor).ConvertQuoteAsync(race.Quote, new SalesQuoteConversionRequest("1", Day),
                    run.Metadata("s-race-convert"))).Detail.Draft.Id;
            }
            catch (SalesDraftServiceException exception)
            {
                return (exception.Status, exception.Code);
            }
        }

        async Task<object> RejectAsync()
        {
            await using var scope = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                return (await run.Service(scope, Approver).RejectRequestAsync(race.Request, new("2", "SENTETIK yarış"),
                    run.Metadata("s-race-reject"))).Detail.Request.State;
            }
            catch (SalesDraftServiceException exception)
            {
                return (exception.Status, exception.Code);
            }
        }

        var results = await Task.WhenAll(Task.Run(ConvertAsync), Task.Run(RejectAsync));
        var loser = Assert.IsType<(int, string)>(Assert.Single(results, result => result is ValueTuple<int, string>));
        Assert.Equal((409, SalesDraftProblemCodesV1.ConcurrentChange), loser);
        var orders = await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts WHERE SourceQuoteId = @q AND State <> 2", ("@q", race.Quote));
        var rejected = await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Requests WHERE Id = @r AND State = 3", ("@r", race.Request));
        Assert.Equal(1, orders + rejected);
    }

    [SqlServerFact]
    public async Task Another_companys_quote_is_invisible_the_routes_need_fresh_grants_and_the_rollback_refuses_while_a_quote_exists()
    {
        await using var run = await Run.StartAsync();
        var party = Guid.NewGuid();
        var account = Guid.NewGuid();
        Guid quoteId;
        await using (var scope = run.Store(CompanyA))
        {
            quoteId = (await run.Service(scope, run.Actor).CreateQuoteAsync(CreateQuote(party, account, [], "9.90"), run.Metadata("i-create"))).Detail.Quote.Id;
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Quotes.FindQuoteAsync(quoteId));
            Assert.Empty((await b.Quotes.ListQuotesAsync(50, null, null)).Items);
            Assert.Null(await b.Quotes.UpdateQuoteAsync(run.Envelope("i-b-lose"), Hash("lose-b"), SalesQuoteCommands.LoseV1, quoteId,
                (quote, now) => quote.RecordLoss(1, run.Actor, "x", now), new SalesDraftStateAudit("sales.quote.lost.v1", "Lose", "SENTETIK")));
            var foreign = await Assert.ThrowsAsync<SalesDraftStoreException>(() => b.Quotes.StoreConvertedDraftAsync(run.Envelope("i-b-convert"),
                Hash("convert-b"), quoteId, 1, _ => throw new InvalidOperationException("a foreign quote never builds an order")));
            Assert.Equal(SalesDraftStoreCodes.ConcurrentChange, foreign.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Quotes WHERE Id = @q AND State = 1 AND Version = 1", ("@q", quoteId)));

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.QuotesRead, SalesPermissionManifestV1.QuotesWrite);
        var creator = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.DraftsCreate);
        using var readerClient = run.Factory.CreateClientFor(reader, CompanyA);
        using var creatorClient = run.Factory.CreateClientFor(creator, CompanyA);
        foreach (var client in new[] { readerClient, creatorClient })
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        }

        using (var list = await readerClient.GetAsync(SalesEndpoints.QuotesPath))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var body = await list.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(quoteId, Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var detail = await readerClient.GetAsync($"{SalesEndpoints.QuotesPath}/{quoteId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
            // The reader holds no price read: the quoted price is withheld on the wire, as on an order.
            var line = body.GetProperty("lines")[0];
            Assert.Equal(("Open", "Withheld", JsonValueKind.Null), (body.GetProperty("quote").GetProperty("state").GetString(),
                line.GetProperty("priceDisclosure").GetString(), line.GetProperty("unitPrice").ValueKind));
        }

        // The quote writer holds no price read: a revision (which replaces every line, prices included) is refused through the
        // real host with a code-less 403 — the quoted price the writer cannot see is not erased, and nothing is recorded.
        var withheldRevision = "{\"expectedVersion\":\"1\",\"lines\":[{\"lineId\":\"" + Guid.NewGuid().ToString("D")
            + "\",\"productId\":\"" + Product.ToString("D") + "\",\"expectedProductVersion\":\"7\",\"orderUnitId\":\""
            + KgUnit.ToString("D") + "\",\"quantity\":\"1.25\"}]}";
        using (var revised = await readerClient.SendAsync(Command(quoteId, "revision", "SENTETIK-withheld-revise", withheldRevision)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, revised.StatusCode);
            Assert.DoesNotContain("price_read_required", await revised.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.Quotes q JOIN sales.QuoteLines l ON l.CompanyId = q.CompanyId AND l.QuoteId = q.Id AND l.Revision = q.Revision "
            + "WHERE q.Id = @q AND q.Version = 1 AND q.Revision = 0 AND l.UnitPrice = N'9.90'", ("@q", quoteId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.QuoteAttempts WHERE IdempotencyKeyHash = @h",
            ("@h", SalesStore.IdempotencyKeyHash("SENTETIK-withheld-revise"))));

        // The conversion needs BOTH names: the quote writer without the order create name and the order creator without the
        // quote write name are each refused, and nothing is recorded.
        foreach (var (client, key) in new[] { (readerClient, "SENTETIK-reader-converts"), (creatorClient, "SENTETIK-creator-converts") })
        {
            using var refused = await client.SendAsync(Command(quoteId, "conversion", key, "{\"expectedVersion\":\"1\",\"businessDate\":\"2026-10-27\"}"));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash = @h",
                ("@h", SalesStore.IdempotencyKeyHash(key))));
        }

        var correlation = Guid.NewGuid().ToString("N");
        using (var lost = await readerClient.SendAsync(Command(quoteId, "loss", "SENTETIK-lose", "{\"expectedVersion\":\"1\",\"reason\":\"SENTETIK teslim süresi\"}", correlation)))
        {
            Assert.Equal(HttpStatusCode.OK, lost.StatusCode);
            var body = await lost.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Lost", body.GetProperty("detail").GetProperty("quote").GetProperty("state").GetString());
        }

        using (var replay = await readerClient.SendAsync(Command(quoteId, "loss", "SENTETIK-lose", "{\"expectedVersion\":\"1\",\"reason\":\"SENTETIK teslim süresi\"}")))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(correlation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        var other = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.QuotesRead);
        using var otherClient = run.Factory.CreateClientFor(other, CompanyB);
        using (var foreign = await otherClient.GetAsync($"{SalesEndpoints.QuotesPath}/{quoteId:D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        // Rollback refuses while a quote exists (forward fix); nothing is dropped.
        await using var context = run.Context(CompanyA);
        var refusal = await Assert.ThrowsAsync<SqlException>(() =>
            context.GetService<IMigrator>().MigrateAsync("20261027063346_AddSalesRequests"));
        Assert.Equal(51066, refusal.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Quotes"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.__EFMigrationsHistory WHERE MigrationId LIKE N'%_AddSalesQuotes'"));
    }

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private static SalesQuoteLineRequest Line(string price, string? discount = null) =>
        new(Guid.NewGuid(), Product, "7", KgUnit, "1.25", price, KgUnit, discount);

    private static CreateSalesQuoteRequest CreateQuote(Guid party, Guid account, IReadOnlyList<Guid> requests, string price) =>
        new(party, account, Currency, Day, [Line(price)], requests, Day.AddDays(15));

    private static HttpRequestMessage Command(Guid quoteId, string segment, string idempotencyKey, string json, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{SalesEndpoints.QuotesPath}/{quoteId:D}/{segment}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", correlationId ?? Guid.NewGuid().ToString("N"));
        return request;
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-seller-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-api22-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        public SalesDraftWriteEnvelope Envelope(string key) =>
            new(Actor, SalesPermissionManifestV1.QuotesWrite, ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesCommandMetadata Metadata(string key, string? correlation = null) => new(key, correlation ?? Correlation, null, DateTimeOffset.UtcNow);

        /// <summary>A submitted request of the customer, through the real service.</summary>
        public async Task<Guid> RequestAsync(Guid party, Guid account)
        {
            await using var scope = Store(CompanyA);
            var created = await Service(scope, Actor).CreateRequestAsync(new CreateSalesRequestRequest(party, account, Day,
                [new SalesRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "1.25")]), Metadata("req-" + Guid.NewGuid().ToString("N")));
            return created.Detail.Request.Id;
        }

        public async Task ApproveRequestAsync(Guid requestId, string key)
        {
            await using var scope = Store(CompanyA);
            await Service(scope, Approver).ApproveRequestAsync(requestId, new("1"), Metadata(key));
        }

        /// <summary>The REAL draft service over this scope's real store, with SYNTHETIC owner ports and every Sales name granted.</summary>
        public SalesDraftService Service(StoreScope scope, string actor, Func<Task>? onProductRead = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            var owners = new SyntheticOwners(onProductRead);
            var links = new NoOwnerLinks();
            return new SalesDraftService(companyContext, new FixedUser(actor, CompanyA), new GrantedAuthority(), new NoRoles(),
                new FixedCorrelation(Correlation), owners, owners, owners, scope.Drafts, new SalesDraftServiceBindings(), TimeProvider.System,
                allocations: links, loadings: links, requests: scope.Requests, quotes: scope.Quotes);
        }

        public SalesDbContext Context(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

        public StoreScope Store(Guid company, IInterceptor? extra = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var builder = new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            if (extra is not null)
            {
                builder.AddInterceptors(extra);
            }

            var dbContext = new SalesDbContext(builder.Options, companyContext);
            return new StoreScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        /// <summary>A synthetic role with exactly these permissions in one company, assigned to a configured development user.</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales quote user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SALES-API22-{roleId:N}", "Synthetic Sales quote test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class StoreScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesRequestStore Requests => store;

        public ISalesQuoteStore Quotes => store;

        public ISalesOrderDraftStore Drafts => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class NoRoles : IActiveRoleCodeReader
    {
        public Task<IReadOnlySet<string>> GetActiveRoleCodesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));
    }

    private sealed class GrantedAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string>(["SENTETIK-SALES"], StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => correlationId;
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    /// <summary>SYNTHETIC Cari / Settings / Product answers for company A: an active account, the kg profile, the order money profile, a sellable product.</summary>
    private sealed class SyntheticOwners(Func<Task>? onProductRead = null) : ISalesPartyAccountPortV1, ISettingsPrerequisiteProvider, IProductSalesEligibilityV1
    {
        private Func<Task>? onProductRead = onProductRead;

        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.Found,
                new SalesPartyAccountV1(partyId, companyAccountId, CompanyA, true, true, "1", "1")));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UnitProfileV1(CompanyA, unitId, "kg", "weight", true, new ProfileVersionV1(KgProfile, 4), 3, true, From, null));

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(CompanyA, currencyId, "TRY", operation, new ProfileVersionV1(MoneyProfile, 3), 2, true, From, null));

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<ProductSalesEligibilityV1> EvaluateAsync(ProductSalesSelectionV1 selection, CancellationToken cancellationToken)
        {
            // A test seam: something happens (once) while an owner is being asked — after the service's own checks, before the save.
            if (Interlocked.Exchange(ref onProductRead, null) is { } hook)
            {
                await hook();
            }

            var unit = new ProductSalesUnitProfileV1(CompanyA, selection.OrderUnitId, KgProfile, "4");
            return new ProductSalesEligibilityV1(
                new ProductSalesSnapshotV1(selection.ProductId, "7", "GDC-001", "Sentetik bant", unit, unit, unit, selection.EffectiveOn), []);
        }
    }

    /// <summary>SYNTHETIC allocation / loading owners that hold nothing for any order (a revision or a cancellation is unblocked).</summary>
    private sealed class NoOwnerLinks : ISalesOrderAllocationReadPortV1, ISalesOrderLoadingReadPortV1
    {
        public Task<SalesOrderAllocationReadAnswerV1> ReadOrderAllocationsAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderAllocationReadAnswerV1(CompanyA, orderId, []));

        public Task<SalesOrderLoadingReadAnswerV1> ReadOrderLoadingAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderLoadingReadAnswerV1(CompanyA, orderId, []));
    }

    /// <summary>Holds each save until both writers have reached it, so both pass every read first.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }
}
