using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP4 (SAT-D475 / SAT-D476) against a REAL SQL Server and the REAL host composition: the Sales over-delivery gate the Logistics
/// shipment calls judges the shipped total with GDC's approved tolerance (1 / 2 / 3 %) and the one calculator — within passes, the
/// approval band opens ONE pending request in the shipper's name (a second attempt answers the same request), the block band is
/// blocked; the request is approved through the real route by another actor in one save (the order's over-delivery approval row,
/// the order version, the request decision, the attempt and the audit), after which the gate passes. The database keeps one
/// pending request per order line and per quote revision and refuses the shipper as decider; the migration refuses to roll back
/// over a recorded request (THROW 52130, forward fix). SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesOrderOverDeliveryApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_over_delivery_gate_opens_one_request_in_the_approval_band_which_another_actor_approves_through_the_route_and_then_the_gate_passes()
    {
        await using var run = await Run.StartAsync(owner: null);
        var draft = await run.ApprovedDraftAsync(CompanyA, "k-gate", "100");
        var line = Assert.Single(draft.Lines);

        // Within (100 + 0.5 = 0.5 % over) passes; the block band (103.5 = 3.5 %) is blocked; neither writes a request.
        Assert.Equal(SalesOverDeliveryGateResultV1.Pass, (await GateAsync(run, draft.Id, line.Id, 100m, 0.5m)).Result);
        Assert.Equal(SalesOverDeliveryGateResultV1.Blocked, (await GateAsync(run, draft.Id, line.Id, 100m, 3.5m)).Result);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ApprovalRequests] WHERE [SubjectId] = @id", ("@id", draft.Id)));

        // The approval band (102.5 = 2.5 %): Held with request 1, recorded in the shipper's name with who, when, how much, which line.
        var held = await GateAsync(run, draft.Id, line.Id, 100m, 2.5m);
        Assert.Equal((SalesOverDeliveryGateResultV1.Held, (int?)1), (held.Result, held.RequestNo));
        Assert.Equal((2, 1, "100", "2.5", "102.5", Shipper), (
            await run.ScalarAsync<int>("SELECT [Kind] FROM [sales].[ApprovalRequests] WHERE [SubjectId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[ApprovalRequests] WHERE [SubjectId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Ordered] FROM [sales].[ApprovalRequests] WHERE [SubjectId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Quantity] FROM [sales].[ApprovalRequests] WHERE [SubjectId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [RequestedTotal] FROM [sales].[ApprovalRequests] WHERE [SubjectId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [RequestedBy] FROM [sales].[ApprovalRequests] WHERE [SubjectId] = @id", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order.over-delivery-requested.v1' AND [ActorId] = @actor",
            ("@actor", Shipper)));

        // A second attempt answers the line's pending request; nothing new is written.
        Assert.Equal((SalesOverDeliveryGateResultV1.Held, (int?)1), ((await GateAsync(run, draft.Id, line.Id, 100m, 2.5m)) is var again
            ? (again.Result, again.RequestNo) : default));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ApprovalRequests] WHERE [SubjectId] = @id", ("@id", draft.Id)));

        // The database itself refuses the shipper as decider (CK_ApprovalRequests_MakerChecker, binary comparison).
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[ApprovalRequests] SET [State] = 2, [DecidedBy] = [RequestedBy], [DecidedAtUtc] = SYSDATETIMEOFFSET(), "
            + "[Version] = 2 WHERE [SubjectId] = @id; SELECT 1", ("@id", draft.Id)))).Number);

        // Another actor approves through the real route: the order's approval row, the version step, the decision, attempt, audit.
        var (subject, userId) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersOverDeliveryApprove);
        using var client = run.Client(subject, CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/over-delivery-requests/1/approval";
        using (var approved = await client.SendAsync(Command(path, "SENTETIK-over-request-1", new { expectedVersion = "1" })))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var body = await approved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("Approved", "2", userId.ToString("D")), (body.GetProperty("status").GetString(),
                body.GetProperty("version").GetString(), body.GetProperty("decidedBy").GetString()));
        }

        Assert.Equal(("102.5", "100", 5L, 1), (
            await run.ScalarAsync<string>("SELECT [ApprovedShipped] FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Ordered] FROM [sales].[DraftOverDeliveryApprovals] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ApprovalRequestAttempts] WHERE [SubjectId] = @id AND [Outcome] = 1",
                ("@id", draft.Id))));

        // The same key replays; the approved total now passes the gate, a larger one awaits a new request (number 2).
        using (var replayed = await client.SendAsync(Command(path, "SENTETIK-over-request-1", new { expectedVersion = "1" })))
        {
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(SalesOverDeliveryGateResultV1.Pass, (await GateAsync(run, draft.Id, line.Id, 100m, 2.5m)).Result);
        var larger = await GateAsync(run, draft.Id, line.Id, 102.5m, 0.3m);
        Assert.Equal((SalesOverDeliveryGateResultV1.Held, (int?)2), (larger.Result, larger.RequestNo));

        // The list read names both requests, newest first.
        var list = await run.JsonAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/over-delivery-requests");
        Assert.Equal(new[] { "Pending", "Approved" },
            list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString()).ToArray());
    }

    [SqlServerFact]
    public async Task The_database_keeps_one_pending_request_per_quote_revision_and_another_company_never_sees_it()
    {
        await using var run = await Run.StartAsync(owner: null);
        var quoteId = Guid.NewGuid();
        await using (var scope = run.SalesStore(CompanyA))
        {
            var store = (ISalesApprovalRequestStore)scope.Store;
            var first = await store.StoreQuoteConversionRequestAsync(run.SalesEnvelope("k-quote-request-1"), Hash("k-quote-request-1"), quoteId,
                (no, now) => SalesApprovalRequest.RequestQuoteConversion(Guid.NewGuid(), CompanyA, quoteId, no, 0, "SENTETIK not", run.Actor, now),
                new SalesDraftStateAudit("sales.quote.expired-conversion-requested.v1", "RequestQuoteConversion", "SENTETIK"));
            Assert.Equal((1, SalesApprovalRequestState.Requested), (first.Request.RequestNo, first.Request.State));

            // UX_ApprovalRequests_PendingQuoteRevision: a second pending request of the same revision is the save's concurrent change.
            var second = await Assert.ThrowsAsync<SalesDraftStoreException>(() => store.StoreQuoteConversionRequestAsync(
                run.SalesEnvelope("k-quote-request-2"), Hash("k-quote-request-2"), quoteId,
                (no, now) => SalesApprovalRequest.RequestQuoteConversion(Guid.NewGuid(), CompanyA, quoteId, no, 0, null, run.Actor, now),
                new SalesDraftStateAudit("sales.quote.expired-conversion-requested.v1", "RequestQuoteConversion", "SENTETIK")));
            Assert.Equal(SalesDraftStoreCodes.ConcurrentChange, second.Code);
        }

        await using (var other = run.SalesStore(CompanyB))
        {
            Assert.Empty(await ((ISalesApprovalRequestStore)other.Store).ListApprovalRequestsAsync(
                SalesApprovalRequestKind.QuoteExpiredConversion, quoteId, 10));
        }

        // CK_ApprovalRequests_Facts: a quote request never carries over-delivery facts.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[ApprovalRequests] SET [Ordered] = N'1' WHERE [SubjectId] = @id; SELECT 1", ("@id", quoteId)))).Number);
    }

    [SqlServerFact]
    public async Task The_approval_request_migrations_Down_passes_with_no_request_and_refuses_52130_while_one_exists()
    {
        await using var run = await Run.StartAsync(owner: null);
        var guard = Assert.IsType<SqlOperation>(new AddSalesApprovalRequests().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + " SELECT 1");

        var draft = await run.ApprovedDraftAsync(CompanyA, "k-down", "100");
        Assert.Equal(SalesOverDeliveryGateResultV1.Held, (await GateAsync(run, draft.Id, Assert.Single(draft.Lines).Id, 100m, 2.5m)).Result);

        Assert.Equal(52130, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ApprovalRequests]"));
    }

    /// <summary>The real host's Sales over-delivery gate port, as the Logistics shipment calls it, in the shipper's name.</summary>
    private static async Task<SalesOverDeliveryGateReadV1> GateAsync(Run run, Guid orderId, Guid lineId, decimal shippedBefore, decimal quantity)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var port = scope.ServiceProvider.GetRequiredService<ISalesOverDeliveryGatePortV1>();
        return await port.EvaluateAsync(new SalesOverDeliveryGateRequestV1(orderId, lineId, Guid.NewGuid(), Guid.NewGuid(), shippedBefore,
            quantity, run.SalesEnvelope("k-gate-" + Guid.NewGuid().ToString("N"), Shipper)));
    }

    private static HttpRequestMessage Command(string path, string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }
}
