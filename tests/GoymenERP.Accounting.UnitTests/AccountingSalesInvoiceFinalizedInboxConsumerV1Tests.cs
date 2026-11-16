using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Application.Inbox;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// FIN-S13 (B-04; KLN-D6..D19): the Accounting consumer of <c>sales.invoice.finalized.v1</c> maps one finalised invoice version to
/// ONE B-04b admission offer with the producer's frozen amounts (never recomputed, never rounded), fences the company three ways,
/// never guesses the invoice date or the source actor, and retries only what Accounting could not answer yet.
/// </summary>
public sealed class AccountingSalesInvoiceFinalizedInboxConsumerV1Tests
{
    private static readonly Guid CompanyA = Guid.Parse("a0000000-0000-0000-0000-0000000013a1");
    private static readonly Guid CompanyB = Guid.Parse("b0000000-0000-0000-0000-0000000013b1");
    private static readonly Guid InvoiceId = Guid.Parse("10000000-0000-0000-0000-0000000013c1");
    private static readonly Guid AdmissionId = Guid.Parse("20000000-0000-0000-0000-0000000013c2");
    private static readonly Guid PartyId = Guid.Parse("30000000-0000-0000-0000-0000000013c3");
    private static readonly Guid TryCurrencyId = Guid.Parse("40000000-0000-0000-0000-0000000013c4");
    private static readonly Guid EurCurrencyId = Guid.Parse("50000000-0000-0000-0000-0000000013c5");
    private static readonly Guid FinalizedBy = Guid.Parse("60000000-0000-0000-0000-0000000013c6");
    private static readonly Guid EventId = Guid.Parse("70000000-0000-0000-0000-0000000013c7");
    private static readonly DateOnly InvoiceDate = new(2026, 11, 13);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_TL_invoice_event_becomes_one_sales_invoice_offer_with_the_frozen_amounts_as_exact_text()
    {
        var request = AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload()), new FixedCompany(CompanyA));

        Assert.Equal(CompanyA, request.CompanyId);
        Assert.Equal(AccountingSalesInvoiceFinalizedInboxV1.ActorId, request.ActorId);
        Assert.Equal("synthetic-fin-s13", request.CorrelationId);
        Assert.Equal(EventId.ToString("D"), request.CausationId);
        Assert.Equal(AccountingSourceDocumentKindsV1.SalesInvoice, request.DocumentKind);
        Assert.Equal(AccountingSourceDocumentKindsV1.SalesInvoiceKey(InvoiceId, 4), request.IdempotencyKey);
        Assert.Equal(AdmissionId, request.SourceDocumentId);
        Assert.Equal(InvoiceId, request.InvoiceId);
        Assert.Equal(4, request.FinalizedVersion);
        Assert.Equal(FinalizedBy, request.SourceActorId);
        Assert.Equal(PartyId, request.PartyId);
        Assert.Equal(TryCurrencyId, request.CurrencyId);
        Assert.Null(request.ExchangeRate);
        Assert.Equal(InvoiceDate, request.DocumentDate);
        Assert.Null(request.DueDate);
        Assert.Equal(new AccountingSourceDocumentTotalsV1("150", "30", "6", "174"), request.DocumentTotals);
        Assert.Equal(request.DocumentTotals, request.FunctionalTotals);
        Assert.Collection(request.Lines,
            line => Assert.Equal(new AccountingSourceDocumentLineV1(1, "Goods", null, null, "20", "100", "20", "6"), line),
            line => Assert.Equal(new AccountingSourceDocumentLineV1(2, "Service", null, null, "20", "50", "10", "0"), line));
    }

    [Fact]
    public void A_foreign_currency_event_uses_the_TL_line_amounts_at_the_invoices_own_fixed_rate()
    {
        var request = AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(EurPayload()), new FixedCompany(CompanyA));

        Assert.Equal(EurCurrencyId, request.CurrencyId);
        Assert.Equal("35.125", request.ExchangeRate);
        Assert.Equal(new AccountingSourceDocumentTotalsV1("10", "2", "0", "12"), request.DocumentTotals);
        Assert.Equal(new AccountingSourceDocumentTotalsV1("351.25", "70.25", "0", "421.5"), request.FunctionalTotals);
        var line = Assert.Single(request.Lines);
        Assert.Equal(new AccountingSourceDocumentLineV1(1, "Goods", null, null, "20", "351.25", "70.25", "0"), line);
    }

    [Fact]
    public void An_export_registered_buyer_does_not_make_the_line_an_export_sale_and_the_purpose_is_not_invented()
    {
        var payload = TlPayload(buyerSituation: "ExportRegistered");

        var request = AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(payload), new FixedCompany(CompanyA));

        Assert.All(request.Lines, line =>
        {
            Assert.Null(line.IsExport);
            Assert.Null(line.LinePurpose);
        });
    }

    [Fact]
    public void An_event_without_the_official_invoice_date_or_the_finalizing_user_is_poison_and_nothing_is_guessed()
    {
        var company = new FixedCompany(CompanyA);

        var noDate = Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload(withInvoiceDate: false)), company));
        var noActor = Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload(finalizedBy: null)), company));
        var badActor = Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload(finalizedBy: "SENTETIK-not-a-user-id")), company));

        Assert.Equal("invoice_date_missing", noDate.Code);
        Assert.Equal("finalized_by_missing", noActor.Code);
        Assert.Equal("finalized_by_missing", badActor.Code);
    }

    [Fact]
    public void The_company_is_the_envelopes_the_ambient_scopes_and_the_payloads_at_once()
    {
        var otherAmbient = Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload()), new FixedCompany(CompanyB)));
        var unresolved = Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload()), new FixedCompany(CompanyA, resolved: false)));
        var otherPayload = Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload(companyId: CompanyB)), new FixedCompany(CompanyA)));

        Assert.Equal("company_context_mismatch", otherAmbient.Code);
        Assert.Equal("company_context_mismatch", unresolved.Code);
        Assert.Equal("invalid_payload", otherPayload.Code);
    }

    [Fact]
    public void Another_source_type_or_version_and_a_broken_envelope_or_payload_are_poison()
    {
        var company = new FixedCompany(CompanyA);

        Assert.Equal("unsupported_event", Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload()) with { SourceName = "sales-legacy" }, company)).Code);
        Assert.Equal("unsupported_event", Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload()) with { EventType = "sales.invoice.cancelled.v1" }, company)).Code);
        Assert.Equal("unsupported_event", Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload()) with { Version = 2 }, company)).Code);
        Assert.Equal("invalid_envelope", Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload()) with { CorrelationId = " " }, company)).Code);
        Assert.Equal("invalid_payload", Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload()) with { PayloadJson = "{" }, company)).Code);
    }

    [Fact]
    public void A_TL_invoice_carrying_functional_amounts_or_a_foreign_invoice_without_them_is_poison()
    {
        var company = new FixedCompany(CompanyA);

        var tlWithFunctional = Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(TlPayload(functionalNet: 150m)), company));
        var foreignWithout = Assert.Throws<OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceFinalizedInboxConsumer.Read(Event(EurPayload(withFunctionalLines: false)), company));

        Assert.Equal("invalid_payload", tlWithFunctional.Code);
        Assert.Equal("invalid_payload", foreignWithout.Code);
    }

    [Fact]
    public void An_amount_is_written_as_exact_text_and_is_never_rounded()
    {
        Assert.Equal("100", AccountingSalesInvoiceFinalizedInboxConsumer.Text(100.00m));
        Assert.Equal("1.23", AccountingSalesInvoiceFinalizedInboxConsumer.Text(1.230m));
        Assert.Equal("0", AccountingSalesInvoiceFinalizedInboxConsumer.Text(0.00m));
        Assert.Equal("0.000000001", AccountingSalesInvoiceFinalizedInboxConsumer.Text(0.000000001m));
    }

    [Fact]
    public async Task Admitted_completes_the_delivery_and_a_replay_of_the_same_key_completes_it_again()
    {
        var port = new SyntheticPort(AccountingSourceDocumentAdmissionAnswerV1.Admitted(Guid.Parse("80000000-0000-0000-0000-0000000013c8"), false),
            AccountingSourceDocumentAdmissionAnswerV1.Admitted(Guid.Parse("80000000-0000-0000-0000-0000000013c8"), true));
        var consumer = new AccountingSalesInvoiceFinalizedInboxConsumer(port, new FixedCompany(CompanyA));

        await consumer.HandleAsync(Event(TlPayload()), CancellationToken.None);
        await consumer.HandleAsync(Event(TlPayload()), CancellationToken.None);

        Assert.Equal(2, port.Requests.Count);
        Assert.All(port.Requests, request =>
            Assert.Equal(AccountingSourceDocumentKindsV1.SalesInvoiceKey(InvoiceId, 4), request.IdempotencyKey));
    }

    [Fact]
    public async Task Unavailable_a_waiting_mapping_and_a_missing_authority_are_retried_with_the_same_key()
    {
        var company = new FixedCompany(CompanyA);
        var unavailable = new AccountingSalesInvoiceFinalizedInboxConsumer(
            new SyntheticPort(AccountingSourceDocumentAdmissionAnswerV1.Unavailable), company);
        var pending = new AccountingSalesInvoiceFinalizedInboxConsumer(new SyntheticPort(
            AccountingSourceDocumentAdmissionAnswerV1.Refused(AccountingSourceDocumentAdmissionReasonsV1.MappingPending, retryable: true)), company);
        var denied = new AccountingSalesInvoiceFinalizedInboxConsumer(new SyntheticPort(
            AccountingSourceDocumentAdmissionAnswerV1.Refused(AccountingSourceDocumentAdmissionReasonsV1.AuthorityDenied)), company);

        var first = await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() =>
            unavailable.HandleAsync(Event(TlPayload()), CancellationToken.None));
        var second = await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() =>
            pending.HandleAsync(Event(TlPayload()), CancellationToken.None));
        var third = await Assert.ThrowsAsync<AccountingInvoiceJournalRetryException>(() =>
            denied.HandleAsync(Event(TlPayload()), CancellationToken.None));

        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.Unavailable, first.Reason);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.MappingPending, second.Reason);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.AuthorityDenied, third.Reason);
    }

    [Fact]
    public async Task A_content_refusal_or_an_identity_conflict_is_final_for_the_key_and_is_poison_with_the_reason()
    {
        var company = new FixedCompany(CompanyA);
        var mismatch = new AccountingSalesInvoiceFinalizedInboxConsumer(new SyntheticPort(
            AccountingSourceDocumentAdmissionAnswerV1.Refused(AccountingSourceDocumentAdmissionReasonsV1.TotalsMismatch)), company);
        var conflict = new AccountingSalesInvoiceFinalizedInboxConsumer(new SyntheticPort(
            AccountingSourceDocumentAdmissionAnswerV1.Refused(AccountingSourceDocumentAdmissionReasonsV1.IdempotencyConflict)), company);

        var first = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            mismatch.HandleAsync(Event(TlPayload()), CancellationToken.None));
        var second = await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            conflict.HandleAsync(Event(TlPayload()), CancellationToken.None));

        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.TotalsMismatch, first.Code);
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.IdempotencyConflict, second.Code);
    }

    [Fact]
    public async Task A_poison_event_never_reaches_the_admission_port()
    {
        var port = new SyntheticPort(AccountingSourceDocumentAdmissionAnswerV1.Unavailable);
        var consumer = new AccountingSalesInvoiceFinalizedInboxConsumer(port, new FixedCompany(CompanyB));

        await Assert.ThrowsAsync<OutboxPermanentFailureException>(() =>
            consumer.HandleAsync(Event(TlPayload()), CancellationToken.None));

        Assert.Empty(port.Requests);
    }

    [Fact]
    public void The_subscription_is_the_one_Sales_event_and_the_caller_is_the_consumer_outside_the_grantable_names()
    {
        var subscription = Assert.Single(AccountingSalesInvoiceFinalizedInboxV1.Subscriptions);

        Assert.Equal(new WorkInboxSubscriptionV1("sales", "sales.invoice.finalized.v1", 1), subscription);
        Assert.Equal("accounting.invoice-journal", AccountingSalesInvoiceFinalizedInboxV1.ConsumerName);
        Assert.StartsWith("inbox.", AccountingSalesInvoiceFinalizedInboxV1.ActorId, StringComparison.Ordinal);
        Assert.Equal(AccountingSalesInvoiceFinalizedInboxV1.ActorId, AccountingSalesInvoiceFinalizedInboxV1.AuditPermissionCode);
    }

    private static WorkInboxEventV1 Event(string payloadJson) =>
        new(AccountingSalesInvoiceFinalizedInboxV1.SourceName, EventId, CompanyA, AccountingSalesInvoiceFinalizedInboxV1.EventType,
            AccountingSalesInvoiceFinalizedInboxV1.Version, payloadJson, new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero),
            "synthetic-fin-s13", null);

    private static string TlPayload(
        bool withInvoiceDate = true,
        string? finalizedBy = "default",
        Guid? companyId = null,
        string buyerSituation = "Normal",
        decimal? functionalNet = null) =>
        JsonSerializer.Serialize(new
        {
            invoiceId = InvoiceId,
            documentNo = "SENTETIK-FIN-S13-1",
            version = 4,
            admissionId = AdmissionId,
            partyId = PartyId,
            companyAccountId = Guid.Parse("90000000-0000-0000-0000-0000000013c9"),
            currencyId = TryCurrencyId,
            rate = (decimal?)null,
            payable = 174.00m,
            functionalPayable = (decimal?)null,
            match = (object?)null,
            instalments = Array.Empty<object>(),
            companyId = companyId ?? CompanyA,
            businessDate = new DateOnly(2026, 11, 12),
            rateDay = (DateOnly?)null,
            functionalCurrencyId = (Guid?)null,
            net = 150.00m,
            vat = 30.00m,
            withholding = 6.00m,
            functionalNet,
            functionalVat = (decimal?)null,
            functionalWithholding = (decimal?)null,
            lines = new object[]
            {
                Line(1, "Goods", buyerSituation, 100.00m, 20.00m, 6.00m, null),
                Line(2, "Service", buyerSituation, 50.00m, 10.00m, 0.00m, null),
            },
            invoiceDate = withInvoiceDate ? InvoiceDate : (DateOnly?)null,
            finalizedBy = finalizedBy == "default" ? FinalizedBy.ToString("D") : finalizedBy,
        }, Json);

    private static string EurPayload(bool withFunctionalLines = true) =>
        JsonSerializer.Serialize(new
        {
            invoiceId = InvoiceId,
            documentNo = "SENTETIK-FIN-S13-2",
            version = 4,
            admissionId = AdmissionId,
            partyId = PartyId,
            companyAccountId = Guid.Parse("90000000-0000-0000-0000-0000000013c9"),
            currencyId = EurCurrencyId,
            rate = 35.1250m,
            payable = 12.00m,
            functionalPayable = 421.50m,
            match = (object?)null,
            instalments = Array.Empty<object>(),
            companyId = CompanyA,
            businessDate = new DateOnly(2026, 11, 12),
            rateDay = new DateOnly(2026, 11, 12),
            functionalCurrencyId = TryCurrencyId,
            net = 10.00m,
            vat = 2.00m,
            withholding = 0.00m,
            functionalNet = 351.25m,
            functionalVat = 70.25m,
            functionalWithholding = 0.00m,
            lines = new object[]
            {
                Line(1, "Goods", "Normal", 10.00m, 2.00m, 0.00m,
                    withFunctionalLines ? (351.25m, 70.25m, 0.00m) : null),
            },
            invoiceDate = InvoiceDate,
            finalizedBy = FinalizedBy.ToString("D"),
        }, Json);

    private static object Line(int lineNo, string itemType, string buyerSituation, decimal net, decimal vat, decimal withholding,
        (decimal Net, decimal Vat, decimal Withholding)? functional) => new
    {
        lineId = Guid.Parse(FormattableString.Invariant($"a1000000-0000-0000-0000-0000000013{lineNo:D2}")),
        lineNo,
        productId = Guid.Parse("b1000000-0000-0000-0000-0000000013d1"),
        unitId = Guid.Parse("c1000000-0000-0000-0000-0000000013d2"),
        quantity = 1.000m,
        itemType,
        buyerSituation,
        vatPercent = 20.00m,
        withholdingShare = withholding > 0m ? 0.3m : (decimal?)null,
        exemptionCode = (string?)null,
        withholdingCode = withholding > 0m ? "SENTETIK-601" : null,
        net,
        vat,
        withholding,
        payable = net + vat - withholding,
        functionalNet = functional?.Net,
        functionalVat = functional?.Vat,
        functionalWithholding = functional?.Withholding,
        functionalPayable = functional is { } amounts ? amounts.Net + amounts.Vat - amounts.Withholding : (decimal?)null,
    };

    private sealed class FixedCompany(Guid companyId, bool resolved = true) : ICompanyContext
    {
        public bool IsResolved => resolved;

        public Guid CompanyId => companyId;

        public void Set(Guid value) => throw new NotSupportedException();
    }

    private sealed class SyntheticPort(params AccountingSourceDocumentAdmissionAnswerV1[] answers) : IAccountingSourceDocumentAdmissionPortV1
    {
        public List<AccountingSourceDocumentAdmissionRequestV1> Requests { get; } = [];

        public Task<AccountingSourceDocumentAdmissionAnswerV1> AdmitAsync(
            AccountingSourceDocumentAdmissionRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(answers[Math.Min(Requests.Count, answers.Length) - 1]);
        }
    }
}
