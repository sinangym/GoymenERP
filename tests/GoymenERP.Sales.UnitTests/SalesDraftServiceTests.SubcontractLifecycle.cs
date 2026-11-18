using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Subcontract_match_reads_the_official_date_rate_freezes_it_once_and_replays_before_owner_reads()
    {
        var fixture = new SubcontractLifecycleWorld();
        var matched = await fixture.Service().MatchSubcontractInvoiceAsync(fixture.Invoice.Id, fixture.Request, Metadata("sc34-match"));
        Assert.Equal("4", matched.Invoice.Version);
        Assert.Equal("40", matched.Invoice.Lifecycle!.ExchangeRate!.Rate);
        Assert.Equal(Day.AddDays(3), Assert.Single(fixture.Rates.Asked).Day);
        Assert.Equal("1200.00", matched.Invoice.Lifecycle.FunctionalTotals!.PayableAmount);
        fixture.Official.Throws = true;
        var replay = await fixture.Service().MatchSubcontractInvoiceAsync(fixture.Invoice.Id, fixture.Request, Metadata("sc34-match"));
        Assert.True(replay.IsReplay);
        Assert.Equal(1, fixture.Official.Calls);
        Assert.Single(fixture.Rates.Asked);
        Assert.Equal(1, fixture.Lifecycle.Writes);
    }

    [Theory]
    [InlineData("denied", 403)]
    [InlineData("lost-during-read", 403)]
    [InlineData("other-company", 503)]
    [InlineData("missing-rate", 409)]
    public async Task Subcontract_official_match_without_authority_or_actual_source_proof_writes_nothing(string fault, int status)
    {
        var fixture = new SubcontractLifecycleWorld();
        if (fault == "denied") fixture.World.Authority.Denied.Add(SalesPermissionManifestV1.InvoicesMatch);
        if (fault == "lost-during-read") fixture.Official.OnRead = () => fixture.World.Authority.Denied.Add(SalesPermissionManifestV1.InvoicesMatch);
        if (fault == "other-company") fixture.Official.Facts = fixture.Official.Facts with
        { SourceProof = fixture.Official.Facts.SourceProof! with { CompanyId = Guid.NewGuid() } };
        if (fault == "missing-rate") fixture.Rates.State = "Unresolved";
        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            fixture.Service().MatchSubcontractInvoiceAsync(fixture.Invoice.Id, fixture.Request, Metadata("sc34-refused")));
        Assert.Equal(status, refusal.Status);
        Assert.Equal(0, fixture.Lifecycle.Writes);
        Assert.Null(fixture.Invoice.Match);
        Assert.Null(fixture.Invoice.Rate);
        if (fault == "denied") Assert.Equal(0, fixture.Official.Calls);
    }

    [Fact]
    public async Task Subcontract_finalization_with_no_finance_port_stays_waiting_and_preserves_official_match_evidence()
    {
        var fixture = new SubcontractLifecycleWorld();
        await fixture.Service().MatchSubcontractInvoiceAsync(fixture.Invoice.Id, fixture.Request, Metadata("sc34-match"));
        var finalized = await fixture.Service().FinalizeSubcontractInvoiceAsync(fixture.Invoice.Id, new("4"), Metadata("sc34-final"));
        Assert.Equal("Finalized", finalized.Invoice.State);
        Assert.Equal("waiting", finalized.Invoice.Lifecycle!.Finalization!.FinanceStatus);
        Assert.Equal(0, finalized.Invoice.Lifecycle.Finalization.AttemptCount);
        Assert.Equal(Day.AddDays(3), finalized.Invoice.Lifecycle.Match!.InvoiceDate);
    }

    [Fact]
    public async Task Subcontract_first_finalization_keeps_its_own_command_receipt_when_the_automatic_finance_offer_replays()
    {
        var fixture = new SubcontractLifecycleWorld();
        await fixture.Service().MatchSubcontractInvoiceAsync(fixture.Invoice.Id, fixture.Request, Metadata("sc34-match"));
        fixture.Lifecycle.ReplayConcurrentFinanceOffer = true;
        var metadata = Metadata("sc34-final");
        var result = await fixture.Service(new FakeFinanceAdmission()).FinalizeSubcontractInvoiceAsync(fixture.Invoice.Id, new("4"), metadata);
        Assert.False(result.IsReplay);
        Assert.False(result.Invoice.IsReplay);
        Assert.Equal(metadata.CorrelationId, result.OriginalCorrelationId);
        Assert.Equal("admitted", result.Invoice.Lifecycle!.Finalization!.FinanceStatus);
    }

    private sealed class SubcontractLifecycleWorld
    {
        public World World { get; } = new();
        public FakeSubcontractInvoiceStore Reader { get; } = new();
        public FakeExchangeRates Rates { get; } = new(ExchangeRateResolutionStatesV1.Resolved, "40");
        public SalesSubcontractServiceInvoice Invoice { get; }
        public SubcontractOfficialReader Official { get; }
        public SubcontractLifecycleStore Lifecycle { get; }
        public MatchSalesInvoiceRequest Request => new("3", Official.Facts.OfficialNo, Official.Facts.Ettn);
        public SubcontractLifecycleWorld()
        {
            Invoice = SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), Company, "SFH-SENTETIK",
                new(Party, Account, Currency, Guid.NewGuid(), "1", 2, Day), new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, "10"),
                [new(Guid.NewGuid(), Guid.NewGuid(), 0, Product, KgUnit, 10m, "10", new("2.5", KgUnit, null), SubcontractTax())],
                10m, "SENTETIK-preparer", World.Clock.GetUtcNow());
            var terms = SalesDueTerms.Create([new(1, SalesDueBasis.OrderDate, 30, "100")]);
            Invoice.SetDuePlan(terms, SalesDueDateCalculator.Compute(terms, Day, null).DueDates);
            Invoice.Submit(1, "SENTETIK-submitter", World.Clock.GetUtcNow());
            Invoice.Approve(2, "SENTETIK-checker", World.Clock.GetUtcNow());
            Reader.Stored[Invoice.Id] = Invoice;
            Official = new(new("SENTETIK-SFH-1", Guid.NewGuid().ToString("D"), Party, Currency, 30m, 1200m, 200m, 0m, Day.AddDays(3),
                new(Company, Guid.NewGuid(), "approved-import", "1", World.Clock.GetUtcNow(), null, null, null, Guid.NewGuid(), new string('a', 64), false)));
            Lifecycle = new(Reader, World.Clock);
        }
        public SalesInvoiceService Service(ISalesFinanceAdmissionPortV1? finance = null)
        {
            var company = new CompanyContext(); company.Set(Company);
            return new(company, World.User, World.Authority, new FixedCorrelation(), new FakeInvoiceStore(), World.Store, World.Settings,
                World.Clock, exchangeRates: Rates, officialInvoices: Official, financeAdmission: finance,
                subcontractInvoices: Reader, subcontractLifecycle: Lifecycle);
        }
    }

    private sealed class SubcontractOfficialReader(SalesOfficialInvoiceFacts facts) : ISalesOfficialInvoiceReadPortV1
    {
        public SalesOfficialInvoiceFacts Facts { get; set; } = facts;
        public int Calls { get; private set; }
        public bool Throws { get; set; }
        public Action? OnRead { get; set; }
        public Task<SalesOfficialInvoiceAnswerV1> LookupAsync(Guid companyId, string officialNo, string ettn, CancellationToken cancellationToken = default)
        {
            Calls++; OnRead?.Invoke();
            if (Throws) throw new InvalidOperationException("SENTETIK provider unavailable");
            return Task.FromResult(new SalesOfficialInvoiceAnswerV1(SalesOfficialInvoiceOutcomeV1.Found, Facts));
        }
    }

    private sealed class SubcontractLifecycleStore(FakeSubcontractInvoiceStore reader, TimeProvider clock) : ISalesSubcontractInvoiceLifecycleStoreV1
    {
        private readonly Dictionary<(string Command, string Key), (string Hash, string Actor, SalesSubcontractInvoiceReplayV1 Result)> attempts = [];
        public int Writes { get; private set; }
        public bool ReplayConcurrentFinanceOffer { get; set; }
        public Task<SalesSubcontractInvoiceReplayV1?> FindLifecycleReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            CancellationToken cancellationToken = default)
        {
            if (ReplayConcurrentFinanceOffer && commandName == SalesSubcontractInvoiceLifecycleCommandsV1.ResendFinance)
            {
                var invoice = Assert.Single(reader.Stored.Values);
                invoice.RecordFinanceAttempt(true, "admitted", clock.GetUtcNow());
                return Task.FromResult<SalesSubcontractInvoiceReplayV1?>(new(invoice, null, "SENTETIK-concurrent-offer"));
            }
            if (!attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var found)) return Task.FromResult<SalesSubcontractInvoiceReplayV1?>(null);
            if (found.Hash != requestHash || found.Actor != envelope.ActorId) throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "SENTETIK");
            return Task.FromResult<SalesSubcontractInvoiceReplayV1?>(found.Result);
        }
        public Task<SalesSubcontractServiceInvoiceStoreResult?> UpdateLifecycleAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            string commandName, Guid invoiceId, Action<SalesSubcontractServiceInvoice, DateTimeOffset> mutate, SalesDraftStateAudit audit,
            CancellationToken cancellationToken = default)
        {
            var invoice = reader.Stored[invoiceId]; mutate(invoice, clock.GetUtcNow()); Writes++;
            attempts[(commandName, envelope.IdempotencyKey)] = (requestHash, envelope.ActorId, new(invoice, null, envelope.CorrelationId));
            return Task.FromResult<SalesSubcontractServiceInvoiceStoreResult?>(new(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }
        public Task<SalesSubcontractInvoiceReplayV1> RecordLifecycleRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            string commandName, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
        {
            var result = new SalesSubcontractInvoiceReplayV1(null, refusal, envelope.CorrelationId);
            attempts[(commandName, envelope.IdempotencyKey)] = (requestHash, envelope.ActorId, result);
            return Task.FromResult(result);
        }
        public Task<Guid?> FindOfficialClaimAsync(string officialNo, string ettn, CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(null);
    }
}
