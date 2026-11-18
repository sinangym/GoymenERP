using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingHttpAcceptanceTests
{
    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(503)]
    public async Task Incoming_invoice_failures_hide_owner_internals_and_keep_private_no_store(int status)
    {
        var owner = new IncomingInvoiceHttpOwner { FailureStatus = status };
        using var host = IncomingInvoiceHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"{PurchaseIncomingInvoiceWireV1.Path}/{owner.Id:D}/finalization", "SENTETIK-key", "{\"expectedVersion\":\"2\"}"));
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("invoice", out _));
        Assert.Equal("2", owner.Version);
        Assert.NotNull(owner.Metadata);
        AssertSafePurchasingOwnerFailure(body, status);
        AssertPrivateNoStore(response);
    }

    [Fact]
    public async Task Incoming_invoice_mutation_binds_version_key_and_exact_money_strings_and_replay_headers()
    {
        var owner = new IncomingInvoiceHttpOwner { Replay = true };
        using var host = IncomingInvoiceHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"{PurchaseIncomingInvoiceWireV1.Path}/{owner.Id:D}/finalization", "SENTETIK-key", "{\"expectedVersion\":\"2\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", owner.Version);
        Assert.Equal("SENTETIK-key", owner.Metadata!.IdempotencyKey);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("120.00", body.GetProperty("invoice").GetProperty("amounts").GetProperty("payable").GetString());
        Assert.Equal("10", body.GetProperty("invoice").GetProperty("lines")[0].GetProperty("quantity").GetString());
        Assert.Equal("unavailable", body.GetProperty("financeOutcome").GetString());
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal("SENTETIK-original", Assert.Single(response.Headers.GetValues("Idempotency-Original-Correlation-Id")));
    }

    [Theory]
    [InlineData("{\"expectedVersion\":\"2\",\"companyId\":\"b0d19f04-3b9d-4871-9ca6-c0ba77c12760\"}")]
    [InlineData("{\"expectedVersion\":2}")]
    [InlineData("{\"expectedVersion\":\"0\"}")]
    [InlineData("{\"expectedVersion\":\"02\"}")]
    [InlineData("{\"expectedVersion\":\"9223372036854775808\"}")]
    public async Task Incoming_invoice_rejects_client_company_or_malformed_versions_before_the_owner(string body)
    {
        var owner = new IncomingInvoiceHttpOwner();
        using var host = IncomingInvoiceHost(owner); using var client = CreateClient(host);
        using var response = await client.SendAsync(Post($"{PurchaseIncomingInvoiceWireV1.Path}/{owner.Id:D}/finalization", "SENTETIK-key", body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(owner.Version);
    }

    [Fact]
    public async Task Incoming_invoice_matching_accepts_only_receipt_identity_and_quantity_without_client_price_or_policy()
    {
        var owner = new IncomingInvoiceHttpOwner();
        using var host = IncomingInvoiceHost(owner); using var client = CreateClient(host);
        var line = Guid.NewGuid(); var order = Guid.NewGuid(); var receipt = Guid.NewGuid();
        var request = JsonSerializer.Serialize(new { expectedVersion = "1", receipts = new[] {
            new { invoiceLineId = line, purchaseOrderId = order, acceptanceId = receipt, quantity = "4" } } });
        using var response = await client.SendAsync(Post($"{PurchaseIncomingInvoiceWireV1.Path}/{owner.Id:D}/matching", "SENTETIK-match", request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new PurchaseInvoiceReceiptSelectionV1(line, order, receipt, "4"), Assert.Single(owner.Selection!.Receipts));
        AssertPrivateNoStore(response);
    }

    [Theory]
    [InlineData("expectedNet", "40")]
    [InlineData("controlRate", "35")]
    [InlineData("amountTolerance", "0.01")]
    [InlineData("companyId", "b0d19f04-3b9d-4871-9ca6-c0ba77c12760")]
    public async Task Incoming_invoice_matching_rejects_client_authoritative_facts_before_the_owner(string name, string value)
    {
        var owner = new IncomingInvoiceHttpOwner();
        using var host = IncomingInvoiceHost(owner); using var client = CreateClient(host);
        var selection = new Dictionary<string, string> { ["invoiceLineId"] = Guid.NewGuid().ToString(),
            ["purchaseOrderId"] = Guid.NewGuid().ToString(), ["acceptanceId"] = Guid.NewGuid().ToString(), ["quantity"] = "4", [name] = value };
        var request = JsonSerializer.Serialize(new { expectedVersion = "1", receipts = new[] { selection } });
        using var response = await client.SendAsync(Post($"{PurchaseIncomingInvoiceWireV1.Path}/{owner.Id:D}/matching", "SENTETIK-match", request));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(owner.Selection);
    }

    [Fact]
    public async Task Incoming_invoice_workspace_is_navigable_by_official_number_and_refuses_a_client_company_filter()
    {
        var owner = new IncomingInvoiceHttpOwner();
        using var host = IncomingInvoiceHost(owner); using var client = CreateClient(host);
        using var response = await client.GetAsync(PurchaseIncomingInvoiceWireV1.Path + "?officialNumber=SENTETIK-INVOICE&pageIndex=0");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SENTETIK-INVOICE", body.GetProperty("items")[0].GetProperty("officialNumber").GetString());
        AssertPrivateNoStore(response);
        using var denied = await client.GetAsync(PurchaseIncomingInvoiceWireV1.Path + "?companyId=" + CompanyA);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
    }

    private WebApplicationFactory<Program> IncomingInvoiceHost(IncomingInvoiceHttpOwner owner) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPurchaseIncomingInvoiceWorkflowV1>(); services.AddScoped<IPurchaseIncomingInvoiceWorkflowV1>(_ => owner);
            services.RemoveAll<IPurchaseIncomingInvoiceWorkspaceV1>(); services.AddScoped<IPurchaseIncomingInvoiceWorkspaceV1>(_ => new IncomingInvoiceWorkspaceStub(owner));
        }));

    private sealed class IncomingInvoiceWorkspaceStub(IncomingInvoiceHttpOwner owner) : IPurchaseIncomingInvoiceWorkspaceV1
    {
        public async Task<PurchaseIncomingInvoicePageV1> ListAsync(string? officialNumber, int pageIndex, CancellationToken cancellationToken = default) =>
            new(pageIndex, false, [PurchaseIncomingInvoiceWireV1.View(await owner.ReadAsync(owner.Id, cancellationToken))]);
        public Task<PurchaseInvoiceReceiptCandidatePageV1> CandidatesAsync(Guid invoiceId, Guid invoiceLineId, int pageIndex,
            CancellationToken cancellationToken = default) => Task.FromResult(new PurchaseInvoiceReceiptCandidatePageV1(invoiceId, invoiceLineId, "1", pageIndex, false, []));
    }

    private sealed class IncomingInvoiceHttpOwner : IPurchaseIncomingInvoiceWorkflowV1
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int? FailureStatus { get; init; }
        public bool Replay { get; init; }
        public string? Version { get; private set; }
        public PurchasingCommandMetadata? Metadata { get; private set; }
        public PurchaseIncomingInvoiceMatchRequestV1? Selection { get; private set; }
        public Task<PurchaseIncomingInvoiceSnapshot> ReadAsync(Guid invoiceId, CancellationToken cancellationToken = default) => Task.FromResult(Invoice());
        public Task<PurchaseIncomingInvoiceOperationResultV1> MatchAsync(Guid invoiceId, PurchaseIncomingInvoiceMatchRequestV1 request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
        { Selection = request; return Answer(request.ExpectedVersion, metadata); }
        public Task<PurchaseIncomingInvoiceOperationResultV1> ApproveVarianceAsync(Guid invoiceId, PurchaseIncomingInvoiceVarianceApprovalRequestV1 request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(request.ExpectedVersion, metadata);
        public Task<PurchaseIncomingInvoiceOperationResultV1> FinalizeAsync(Guid invoiceId, PurchaseIncomingInvoiceVersionRequestV1 request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(request.ExpectedVersion, metadata);
        public Task<PurchaseIncomingInvoiceOperationResultV1> RetryFinanceAsync(Guid invoiceId, PurchaseIncomingInvoiceVersionRequestV1 request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) => Answer(request.ExpectedVersion, metadata);
        private Task<PurchaseIncomingInvoiceOperationResultV1> Answer(string version, PurchasingCommandMetadata metadata)
        {
            Version = version; Metadata = metadata;
            return FailureStatus is { } status
                ? throw new PurchasingServiceException(status, status == 409 ? PurchaseIncomingInvoice.StateConflict : "SENTETIK-internal")
                : Task.FromResult(new PurchaseIncomingInvoiceOperationResultV1(Invoice(), Replay, "SENTETIK-original", "unavailable"));
        }
        private PurchaseIncomingInvoiceSnapshot Invoice()
        {
            var currency = Guid.NewGuid();
            var invoice = PurchaseIncomingInvoice.Record(Id, CompanyA, Guid.NewGuid(), Guid.NewGuid(), currency, currency, 2, 2, null,
                new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-INVOICE", Guid.NewGuid(), new(2026, 11, 17)), new(100m, 20m, 0m), null,
                [new(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 10m, "material", "resale", 20m, new(100m, 20m, 0m), null)],
                [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 12, 17), 120m, null)], "SENTETIK", DateTimeOffset.Parse("2026-11-17T10:00:00Z"));
            return PurchaseIncomingInvoiceSnapshot.Capture(invoice);
        }
    }
}
