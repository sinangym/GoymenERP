using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseIncomingInvoiceWorkflowTests
{
    private static readonly Guid Company = Guid.Parse("b0d19f04-3b9d-4871-9ca6-c0ba77c12760");
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Matching_reads_owner_facts_under_the_selected_order_locks_and_replays_without_reading_them_again()
    {
        var world = new World(recorded: true); var orderId = Guid.NewGuid(); var acceptance = Guid.NewGuid();
        var source = new MatchSource(world.Store); world.Matching = source;
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1", [new(world.Store.Current.Lines[0].Id, orderId, acceptance, "10")]);
        var result = await world.Service().MatchAsync(world.Id, request, Metadata("match"));
        Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, result.Invoice.Status);
        Assert.Equal(new[] { orderId }, world.Store.LastOrdersToLock);
        Assert.Equal(acceptance, Assert.Single(result.Invoice.Matches).AcceptanceId);
        Assert.True((await world.Service().MatchAsync(world.Id, request, Metadata("match"))).IsReplay);
        Assert.Equal(1, source.Calls);
        Assert.Empty(world.Finance.Offers);
    }

    [Fact]
    public async Task Matching_without_its_real_source_cannot_substitute_client_prices_or_zero_tolerance()
    {
        var world = new World(recorded: true);
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1", [new(world.Store.Current.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), "10")]);
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().MatchAsync(world.Id, request, Metadata("no-source")));
        Assert.Equal(503, failure.Status);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, world.Store.Current.Status);
        Assert.Empty(world.Store.Committed);
    }

    [Fact]
    public async Task A_busy_order_line_returns_retryable_conflict_and_the_same_key_can_match_then_replay()
    {
        var world = new World(recorded: true);
        var source = new MatchSource(world.Store); world.Matching = source;
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1",
            [new(world.Store.Current.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), "10")]);
        var metadata = Metadata("busy-line");
        world.Store.BeforeMutation = () => throw new PurchasingStoreException(PurchasingStoreCodes.LineBusy, "SENTETIK-busy");

        var busy = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().MatchAsync(world.Id, request, metadata));
        Assert.Equal((409, PurchasingProblemCodesV1.LineBusy), (busy.Status, busy.Code));
        Assert.False(busy.IsReplay);
        Assert.Equal(1, world.Store.Current.Version);
        Assert.Equal(0, source.Calls);
        Assert.Empty(world.Store.Committed);
        Assert.Empty(world.Finance.Offers);

        world.Store.BeforeMutation = null;
        var matched = await world.Service().MatchAsync(world.Id, request, metadata);
        Assert.False(matched.IsReplay);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, matched.Invoice.Status);
        Assert.True((await world.Service().MatchAsync(world.Id, request, metadata)).IsReplay);
        Assert.Single(world.Store.Committed);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Authority_revoked_while_waiting_for_an_invoice_order_lock_hides_the_busy_response()
    {
        var world = new World(recorded: true); world.Matching = new MatchSource(world.Store);
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1",
            [new(world.Store.Current.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), "10")]);
        world.Store.BeforeMutation = () =>
        {
            world.Authority.Denied = PurchaseIncomingInvoicePermissionNamesV1.Match;
            throw new PurchasingStoreException(PurchasingStoreCodes.LineBusy, "SENTETIK-busy");
        };
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().MatchAsync(world.Id, request, Metadata("busy-revoked")));
        Assert.Equal(403, denied.Status);
        Assert.Equal(1, world.Store.Current.Version);
        Assert.Empty(world.Store.Committed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Matching_rechecks_permission_after_both_a_successful_and_a_failed_source_read(bool fail)
    {
        var world = new World(recorded: true);
        world.Matching = new MatchSource(world.Store) { BeforeRead = () =>
        {
            world.Authority.Denied = PurchaseIncomingInvoicePermissionNamesV1.Match;
            if (fail) throw new InvalidOperationException("SENTETIK-provider-failure");
        } };
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1",
            [new(world.Store.Current.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), "10")]);
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().MatchAsync(world.Id, request, Metadata("revoked")));
        Assert.Equal(403, denied.Status);
        Assert.Empty(world.Store.Committed);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, world.Store.Current.Status);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("1.0000001")]
    [InlineData("1,5")]
    public async Task Matching_rejects_noncanonical_or_unstorable_quantity_before_the_source(string quantity)
    {
        var world = new World(recorded: true); var source = new MatchSource(world.Store); world.Matching = source;
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1",
            [new(world.Store.Current.Lines[0].Id, Guid.NewGuid(), Guid.NewGuid(), quantity)]);
        var invalid = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().MatchAsync(world.Id, request, Metadata("invalid-quantity")));
        Assert.Equal(400, invalid.Status);
        Assert.Equal(0, source.Calls);
        Assert.Empty(world.Store.Committed);
    }

    [Fact]
    public async Task Finalization_commits_before_Finance_and_replaying_the_key_does_not_offer_twice()
    {
        var world = new World();
        world.Finance.OnOffer = _ =>
        {
            Assert.False(world.Store.InMutation);
            Assert.Equal(PurchaseIncomingInvoiceStatus.FinancePending, world.Store.Current.Status);
        };
        var result = await world.Service().FinalizeAsync(world.Id, new("2"), Metadata("finalize"));
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinanceAccepted, result.Invoice.Status);
        Assert.Equal(4, result.Invoice.Version);
        Assert.Equal("admitted", result.FinanceOutcome);
        Assert.Equal(new[] { PurchaseIncomingInvoiceCommandsV1.Finalize, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance }, world.Store.Committed);
        var again = await world.Service().FinalizeAsync(world.Id, new("2"), Metadata("finalize") with { CorrelationId = "second" });
        Assert.True(again.IsReplay);
        Assert.Equal("SENTETIK-correlation", again.OriginalCorrelationId);
        Assert.Equal(result.Invoice.AdmissionId, again.Invoice.AdmissionId);
        Assert.Single(world.Finance.Offers);
    }

    [Theory]
    [InlineData(PurchaseInvoiceFinanceAdmissionOutcomeV1.Refused, "refused")]
    [InlineData(PurchaseInvoiceFinanceAdmissionOutcomeV1.Retry, "retry")]
    [InlineData(PurchaseInvoiceFinanceAdmissionOutcomeV1.Unavailable, "unavailable")]
    [InlineData(PurchaseInvoiceFinanceAdmissionOutcomeV1.KeyConflict, "key_conflict")]
    public async Task A_non_admitted_Finance_answer_preserves_the_finalized_pending_invoice(
        PurchaseInvoiceFinanceAdmissionOutcomeV1 outcome, string text)
    {
        var world = new World(); world.Finance.Outcome = outcome;
        var result = await world.Service().FinalizeAsync(world.Id, new("2"), Metadata("finalize"));
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinancePending, result.Invoice.Status);
        Assert.Equal(3, result.Invoice.FinalizedVersion);
        Assert.Equal(text, result.FinanceOutcome);
        Assert.Equal(new[] { PurchaseIncomingInvoiceCommandsV1.Finalize }, world.Store.Committed);
    }

    [Fact]
    public async Task Another_authorized_actor_retries_the_frozen_admission_without_changing_the_official_payload()
    {
        var world = new World(); world.Finance.Throw = true;
        var pending = await world.Service().FinalizeAsync(world.Id, new("2"), Metadata("finalize"));
        Assert.Equal("unavailable", pending.FinanceOutcome);
        Assert.Single(world.Diagnostics.Errors);
        world.Finance.Throw = false; world.User.Actor = "SENTETIK-other";
        var accepted = await world.Service().RetryFinanceAsync(world.Id, new("3"), Metadata("retry"));
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinanceAccepted, accepted.Invoice.Status);
        Assert.Equal("SENTETIK-actor", accepted.Invoice.FinalizedBy);
        Assert.Equal(world.Finance.Offers[0].AdmissionId, world.Finance.Offers[1].AdmissionId);
        Assert.Equal(world.Finance.Offers[0].Installments, world.Finance.Offers[1].Installments);
        Assert.NotEqual(world.Finance.Offers[0].IdempotencyKey, world.Finance.Offers[1].IdempotencyKey);
        Assert.Equal("3", world.Finance.Offers[1].FinalizedVersion);
        var replay = await world.Service().RetryFinanceAsync(world.Id, new("3"), Metadata("retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal(2, world.Finance.Offers.Count);
    }

    [Fact]
    public async Task A_stale_version_is_terminal_and_replayed_without_an_admission()
    {
        var world = new World();
        var first = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().FinalizeAsync(world.Id, new("1"), Metadata("stale")));
        var replay = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().FinalizeAsync(world.Id, new("1"), Metadata("stale")));
        Assert.Equal(PurchasingProblemCodesV1.VersionConflict, first.Code);
        Assert.True(replay.IsReplay);
        Assert.Equal(2, world.Store.Current.Version);
        Assert.Empty(world.Finance.Offers);
    }

    [Fact]
    public async Task Revocation_during_Finance_leaves_the_committed_invoice_pending_and_discloses_no_success_response()
    {
        var world = new World();
        world.Finance.OnOffer = _ => world.Authority.Denied = PurchaseIncomingInvoicePermissionNamesV1.Finalize;
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().FinalizeAsync(world.Id, new("2"), Metadata("revoked")));
        Assert.Equal(403, failure.Status);
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinancePending, world.Store.Current.Status);
        Assert.Equal(new[] { PurchaseIncomingInvoiceCommandsV1.Finalize }, world.Store.Committed);
    }

    [Theory]
    [InlineData(PurchaseIncomingInvoicePermissionNamesV1.Finalize)]
    [InlineData(PurchasingPermissionManifestV1.OrdersPriceRead)]
    public async Task Permission_or_price_denial_happens_before_the_store_or_Finance_is_read(string denied)
    {
        var world = new World(); world.Authority.Denied = denied;
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().FinalizeAsync(world.Id, new("2"), Metadata("denied")));
        Assert.Equal(403, failure.Status);
        Assert.Equal(0, world.Store.Reads);
        Assert.Empty(world.Finance.Offers);
    }

    [Fact]
    public async Task The_read_rechecks_authority_and_rejects_company_drift_after_the_store_returns()
    {
        var world = new World(); world.Store.AfterRead = () => world.Company.Set(Guid.NewGuid());
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReadAsync(world.Id));
        Assert.Equal(403, failure.Status);
        Assert.Empty(world.Store.Committed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_owner_history_is_unavailable_and_never_a_ready_invoice(bool throws)
    {
        var world = new World();
        world.Store.ReadFailure = throws ? new InvalidOperationException("SENTETIK-invalid-history") : null;
        world.Store.ReadAnswer = world.Store.Current with { Version = 99 };
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReadAsync(world.Id));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Empty(world.Store.Committed);
        Assert.Empty(world.Finance.Offers);
    }

    [Fact]
    public async Task The_import_preparer_cannot_approve_its_own_invoice_variance()
    {
        var world = new World(withVariance: true); world.User.Actor = "SENTETIK-preparer";
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ApproveVarianceAsync(world.Id, new("2", "SENTETIK-reason"), Metadata("approve")));
        Assert.Equal(PurchaseIncomingInvoice.MakerChecker, failure.Code);
        Assert.Equal(2, world.Store.Current.Version);
        Assert.Null(world.Store.Current.VarianceDecision);
    }

    private static PurchasingCommandMetadata Metadata(string key) => new("SENTETIK-" + key, "SENTETIK-correlation", "SENTETIK-cause", Now);
    private sealed class World
    {
        public World(bool withVariance = false, bool recorded = false) { Company.Set(PurchaseIncomingInvoiceWorkflowTests.Company); Store = new(Invoice(withVariance, !recorded)); }
        public MutableCompany Company { get; } = new();
        public UserStub User { get; } = new();
        public Authority Authority { get; } = new();
        public Store Store { get; }
        public Finance Finance { get; } = new();
        public Diagnostics Diagnostics { get; } = new();
        public IPurchaseIncomingInvoiceMatchSourceV1? Matching { get; set; }
        public Guid Id => Store.Current.Id;
        public PurchaseIncomingInvoiceWorkflow Service() => new(Company, User, Authority, new Correlation(), Store, new Clock(), Finance, Diagnostics, Matching);
    }
    private static PurchaseIncomingInvoice Invoice(bool variance, bool matched = true)
    {
        var currency = Guid.NewGuid(); var unit = Guid.NewGuid(); var line = Guid.NewGuid();
        var invoice = PurchaseIncomingInvoice.Record(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(), currency, currency, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-invoice", Guid.NewGuid(), new(2026, 11, 17)), new(100m, 20m, 0m), null,
            [new(line, 1, Guid.NewGuid(), unit, 10m, "material", "resale", 20m, new(100m, 20m, 0m), null)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 12, 17), 120m, null)], "SENTETIK-preparer", Now);
        if (matched) invoice.Match(1, [new(Company, line, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, invoice.SupplierPartyId,
            invoice.CompanyAccountId, currency, unit, 10m, variance ? 99m : 100m, null, new(2026, 11, 16))],
            new(Guid.NewGuid(), 1, 0m, 0m), "SENTETIK-matcher");
        return invoice;
    }
    private sealed class MutableCompany : ICompanyContext
    {
        public Guid CompanyId { get; private set; }
        public bool IsResolved => CompanyId != Guid.Empty;
        public void Set(Guid value) => CompanyId = value;
    }
    private sealed class UserStub : ICurrentUser
    {
        public string Actor { get; set; } = "SENTETIK-actor";
        public bool IsAuthenticated => true;
        public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(permission != Denied, true, new HashSet<string> { "SENTETIK-role" },
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "SENTETIK-correlation";
        public string? CausationId => "SENTETIK-cause";
        public string SourceChannel => "Test";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Diagnostics : IPurchaseIncomingInvoiceDiagnosticsV1
    {
        public List<string> Errors { get; } = [];
        public void FinanceHandoffFailed(Guid companyId, Guid invoiceId, string phase, Exception exception) => Errors.Add(phase);
    }
    private sealed class Finance : IPurchaseInvoiceFinanceAdmissionPortV1
    {
        public PurchaseInvoiceFinanceAdmissionOutcomeV1 Outcome { get; set; } = PurchaseInvoiceFinanceAdmissionOutcomeV1.Admitted;
        public bool Throw { get; set; }
        public Action<PurchaseInvoiceFinanceAdmissionRequestV1>? OnOffer { get; set; }
        public List<PurchaseInvoiceFinanceAdmissionRequestV1> Offers { get; } = [];
        public Task<PurchaseInvoiceFinanceAdmissionAnswerV1> AdmitAsync(PurchaseInvoiceFinanceAdmissionRequestV1 request, CancellationToken cancellationToken = default)
        {
            Offers.Add(request); OnOffer?.Invoke(request);
            return Throw ? throw new InvalidOperationException("SENTETIK-unavailable") : Task.FromResult(new PurchaseInvoiceFinanceAdmissionAnswerV1(Outcome, "SENTETIK"));
        }
    }
    private sealed class Store(PurchaseIncomingInvoice initial) : IPurchasingIncomingInvoiceStoreV1
    {
        public PurchaseIncomingInvoiceSnapshot Current { get; private set; } = PurchaseIncomingInvoiceSnapshot.Capture(initial);
        public int Reads { get; private set; }
        public bool InMutation { get; private set; }
        public Action? AfterRead { get; set; }
        public Action? BeforeMutation { get; set; }
        public Exception? ReadFailure { get; set; }
        public PurchaseIncomingInvoiceSnapshot? ReadAnswer { get; set; }
        public IReadOnlyList<Guid> LastOrdersToLock { get; private set; } = [];
        public List<string> Committed { get; } = [];
        private readonly Dictionary<(string Command, string Key), (string Hash, string Actor, PurchasingIncomingInvoiceWriteResult Result)> attempts = [];
        public Task<PurchaseIncomingInvoiceSnapshot?> FindIncomingInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
        {
            Reads++; AfterRead?.Invoke();
            if (ReadFailure is { } failure) throw failure;
            return Task.FromResult<PurchaseIncomingInvoiceSnapshot?>(invoiceId == Current.Id ? ReadAnswer ?? Current : null);
        }
        public Task<PurchasingIncomingInvoiceWriteResult?> FindIncomingInvoiceAttemptAsync(string command, PurchasingWriteEnvelope envelope,
            string requestHash, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (!attempts.TryGetValue((command, envelope.IdempotencyKey), out var result)) return Task.FromResult<PurchasingIncomingInvoiceWriteResult?>(null);
            if (result.Hash != requestHash || result.Actor != envelope.ActorId) throw new PurchasingStoreException(PurchasingStoreCodes.SameKeyPayloadMismatch, "SENTETIK");
            return Task.FromResult<PurchasingIncomingInvoiceWriteResult?>(result.Result with { IsReplay = true });
        }
        public async Task<PurchasingIncomingInvoiceWriteResult?> MutateIncomingInvoiceAsync(Guid invoiceId, string command,
            PurchasingWriteEnvelope envelope, string requestHash, IReadOnlyList<Guid> orderIdsToLock,
            Func<PurchaseIncomingInvoice, CancellationToken, Task> mutate, Func<CancellationToken, Task> requireStillAuthorized,
            CancellationToken cancellationToken = default)
        {
            await requireStillAuthorized(cancellationToken);
            LastOrdersToLock = orderIdsToLock;
            BeforeMutation?.Invoke();
            if (invoiceId != Current.Id) return null;
            InMutation = true;
            try
            {
                var invoice = Current.Restore(); await mutate(invoice, cancellationToken); await requireStillAuthorized(cancellationToken);
                Current = PurchaseIncomingInvoiceSnapshot.Capture(invoice); Committed.Add(command);
                var result = new PurchasingIncomingInvoiceWriteResult(Current, null, false, envelope.CorrelationId);
                attempts.Add((command, envelope.IdempotencyKey), (requestHash, envelope.ActorId, result));
                return result;
            }
            finally { InMutation = false; }
        }
        public async Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceRefusalAsync(string command,
            PurchasingWriteEnvelope envelope, string requestHash, PurchasingTerminalRefusal refusal,
            Func<CancellationToken, Task> requireStillAuthorized, CancellationToken cancellationToken = default)
        {
            await requireStillAuthorized(cancellationToken);
            var result = new PurchasingIncomingInvoiceWriteResult(null, refusal, false, envelope.CorrelationId);
            attempts.Add((command, envelope.IdempotencyKey), (requestHash, envelope.ActorId, result)); return result;
        }
        public Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceAsync(PurchaseIncomingInvoice invoice,
            PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MatchSource(Store owner) : IPurchaseIncomingInvoiceMatchSourceV1
    {
        public int Calls { get; private set; }
        public Action? BeforeRead { get; init; }
        public Task<PurchaseIncomingInvoiceMatchSourceV1> ReadAsync(PurchaseIncomingInvoice invoice,
            IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> selected, CancellationToken cancellationToken = default)
        {
            Assert.True(owner.InMutation); Calls++;
            BeforeRead?.Invoke();
            var item = Assert.Single(selected); var line = Assert.Single(invoice.Lines);
            return Task.FromResult(new PurchaseIncomingInvoiceMatchSourceV1([new(Company, line.Id, item.AcceptanceId,
                item.PurchaseOrderId, Guid.NewGuid(), 2, invoice.SupplierPartyId, invoice.CompanyAccountId, invoice.CurrencyId,
                line.UnitId, 10m, 100m, null, new(2026, 11, 17))], new(Guid.NewGuid(), 1, 0m, 0m)));
        }
    }
}
