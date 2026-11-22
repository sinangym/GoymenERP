using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.SignedApprovals;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// WP13 (YOL-D11) service skeleton (K3): the record's own name is measured before anything is read; the replay comes before any
/// owner read and answers the stored state with the original correlation; another content under the same key is one 409; an order
/// that is not an open approved order, a stale order version and the same document again are terminal refusals recorded under the
/// key; a document the vault does not accept (another record's, unknown, not downloadable, another kind) is a 422 that writes
/// nothing; an unavailable vault (or no vault port) is 503; a vault refusal of the actor's own read is 403. The read needs only the
/// draft read. Withdrawal needs a reason and the record's version. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderSignedApprovalServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("0d0d0d0d-0d0d-4d0d-8d0d-0d0d0d0d0d0d");
    private static readonly Guid Document = Guid.Parse("d0c0d0c0-d0c0-4d0c-8d0c-d0c0d0c0d0c0");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Seller = "synthetic-seller";

    private static readonly SalesCommandMetadata Metadata = new("SENTETIK-key-1", "SENTETIK-correlation", null, Now);

    private static RecordSalesOrderSignedApprovalRequest Request(string version = "7") => new(Document, version);

    private static (SalesOrderSignedApprovalService Service, Store Store, Vault Vault, Authority Authority) Build(bool withVault = true)
    {
        var store = new Store();
        var vault = new Vault();
        var authority = new Authority();
        var service = new SalesOrderSignedApprovalService(new CompanyContext(), new User(Seller), authority, new Correlation(), store,
            new FixedClock(), withVault ? vault : null);
        return (service, store, vault, authority);
    }

    private static async Task<SalesDraftServiceException> Refused(Func<Task> act) =>
        await Assert.ThrowsAsync<SalesDraftServiceException>(act);

    [Fact]
    public async Task Record_without_its_own_permission_is_403_before_any_replay_or_vault_read()
    {
        var (service, store, vault, authority) = Build();
        authority.Denied.Add(SalesPermissionManifestV1.OrdersSignedApprovalRecord);

        var refused = await Refused(() => service.RecordAsync(Order, Request(), Metadata));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Empty(store.Calls);
        Assert.Equal(0, vault.Calls);
    }

    [Fact]
    public async Task Record_pins_the_vault_version_and_hash_for_the_current_approved_revision_and_answers_received()
    {
        var (service, store, vault, _) = Build();

        var result = await service.RecordAsync(Order, Request(), Metadata);

        Assert.False(result.IsReplay);
        var stored = Assert.Single(store.Records);
        Assert.Equal((Order, 3, 7L, Document, 2, Sha, Seller), (stored.OrderId, stored.OrderRevision, stored.OrderVersion,
            stored.Document.DocumentId, stored.Document.VersionNo, stored.Document.Sha256, stored.RecordedBy));
        Assert.True(result.Detail.SignedApprovalReceived);
        Assert.Equal(("Rev.3", "Active", "1"), (result.Detail.Active!.OrderRevision, result.Detail.Active.State, result.Detail.Active.Version));
        Assert.Equal((Company, Order, Document), vault.Last);
    }

    [Fact]
    public async Task The_same_key_again_answers_the_replay_with_the_original_correlation_and_reads_no_vault()
    {
        var (service, store, vault, _) = Build();
        await service.RecordAsync(Order, Request(), Metadata);
        var vaultCalls = vault.Calls;

        var replay = await service.RecordAsync(Order, Request(), Metadata with { CorrelationId = "SENTETIK-second" });

        Assert.True(replay.IsReplay);
        Assert.Equal("SENTETIK-correlation", replay.OriginalCorrelationId);
        Assert.Single(store.Records);
        Assert.Equal(vaultCalls, vault.Calls);
    }

    [Fact]
    public async Task The_same_key_with_another_document_is_one_409_and_writes_nothing()
    {
        var (service, store, _, _) = Build();
        await service.RecordAsync(Order, Request(), Metadata);

        var refused = await Refused(() => service.RecordAsync(Order, Request() with { DocumentId = Guid.NewGuid() }, Metadata));

        Assert.Equal((409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch), (refused.Status, refused.Code));
        Assert.Single(store.Records);
    }

    [Fact]
    public async Task A_stale_order_version_is_a_recorded_terminal_409_and_the_same_key_replays_the_refusal()
    {
        var (service, store, vault, _) = Build();

        var refused = await Refused(() => service.RecordAsync(Order, Request("6"), Metadata));
        var again = await Refused(() => service.RecordAsync(Order, Request("6"), Metadata));

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (refused.Status, refused.Code));
        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict, true), (again.Status, again.Code, again.IsReplay));
        Assert.Empty(store.Records);
        Assert.Equal(0, vault.Calls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task An_order_that_is_not_open_and_approved_is_a_recorded_terminal_409(bool open, bool approved)
    {
        var (service, store, vault, _) = Build();
        store.Order = store.Order with { IsOpen = open, IsApproved = approved };

        var refused = await Refused(() => service.RecordAsync(Order, Request(), Metadata));

        Assert.Equal((409, SalesOrderSignedApprovalProblemCodesV1.OrderNotApproved), (refused.Status, refused.Code));
        Assert.Single(store.Refusals);
        Assert.Empty(store.Records);
        Assert.Equal(0, vault.Calls);
    }

    [Theory]
    [InlineData(SalesSignedApprovalDocumentOutcomeV1.OwnerMismatch, "document_not_on_order")]
    [InlineData(SalesSignedApprovalDocumentOutcomeV1.NotFound, "document_not_found")]
    [InlineData(SalesSignedApprovalDocumentOutcomeV1.KindNotAllowed, "document_kind_not_allowed")]
    [InlineData(SalesSignedApprovalDocumentOutcomeV1.NotDownloadable, "document_not_downloadable")]
    public async Task A_document_the_vault_does_not_accept_is_422_and_writes_nothing(SalesSignedApprovalDocumentOutcomeV1 outcome,
        string field)
    {
        var (service, store, vault, _) = Build();
        vault.Answer = new SalesSignedApprovalDocumentCheckV1(outcome, null, null);

        var refused = await Refused(() => service.RecordAsync(Order, Request(), Metadata));

        Assert.Equal((422, SalesOrderSignedApprovalProblemCodesV1.DocumentInvalid), (refused.Status, refused.Code));
        Assert.Equal(("documentId", field), (refused.Errors.Single().Field, refused.Errors.Single().Code));
        Assert.Empty(store.Records);
        Assert.Empty(store.Refusals);
    }

    [Fact]
    public async Task A_vault_refusal_of_the_actors_own_read_is_403_and_an_unavailable_vault_is_503()
    {
        var (service, store, vault, _) = Build();
        vault.Answer = new SalesSignedApprovalDocumentCheckV1(SalesSignedApprovalDocumentOutcomeV1.Denied, null, null);
        var denied = await Refused(() => service.RecordAsync(Order, Request(), Metadata));
        vault.Answer = new SalesSignedApprovalDocumentCheckV1(SalesSignedApprovalDocumentOutcomeV1.Unavailable, null, null);
        var unavailable = await Refused(() => service.RecordAsync(Order, Request(), Metadata with { IdempotencyKey = "SENTETIK-key-2" }));

        Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        Assert.Equal((503, SalesOrderSignedApprovalProblemCodesV1.DocumentUnavailable), (unavailable.Status, unavailable.Code));
        Assert.Empty(store.Records);
    }

    [Fact]
    public async Task Without_a_vault_port_a_record_fails_closed_with_503()
    {
        var (service, store, _, _) = Build(withVault: false);

        var refused = await Refused(() => service.RecordAsync(Order, Request(), Metadata));

        Assert.Equal((503, SalesOrderSignedApprovalProblemCodesV1.DocumentUnavailable), (refused.Status, refused.Code));
        Assert.Empty(store.Records);
    }

    [Fact]
    public async Task The_same_document_version_again_is_a_recorded_409_and_a_new_one_supersedes_the_active_record()
    {
        var (service, store, vault, _) = Build();
        await service.RecordAsync(Order, Request(), Metadata);

        var again = await Refused(() => service.RecordAsync(Order, Request(), Metadata with { IdempotencyKey = "SENTETIK-key-2" }));
        vault.Answer = new SalesSignedApprovalDocumentCheckV1(SalesSignedApprovalDocumentOutcomeV1.Accepted, 3, Sha);
        var newer = await service.RecordAsync(Order, Request(), Metadata with { IdempotencyKey = "SENTETIK-key-3" });

        Assert.Equal((409, SalesOrderSignedApprovalProblemCodesV1.AlreadyCurrent), (again.Status, again.Code));
        Assert.Equal(2, store.Records.Count);
        Assert.Equal(SalesOrderSignedApprovalState.Passive, store.Records[0].State);
        Assert.Equal(SalesOrderSignedApprovalDeactivationKind.Superseded, store.Records[0].Deactivation!.Kind);
        Assert.Equal(store.Records[1].Id, store.Records[0].Deactivation!.SupersededById);
        Assert.Equal(3, newer.Detail.Active!.DocumentVersionNo);
        Assert.Equal(2, newer.Detail.Items.Count);
    }

    [Fact]
    public async Task After_a_commercial_revision_the_active_record_no_longer_covers_the_order()
    {
        var (service, store, _, _) = Build();
        await service.RecordAsync(Order, Request(), Metadata);
        store.Order = store.Order with { Revision = 4, Version = 9 };

        var status = await service.GetAsync(Order);

        Assert.False(status.SignedApprovalReceived);
        Assert.Equal(("Rev.4", "Rev.3"), (status.OrderRevision, status.Active!.OrderRevision));
    }

    [Fact]
    public async Task The_read_needs_only_the_draft_read_and_another_companys_order_is_404()
    {
        var (service, store, _, authority) = Build();
        authority.Denied.Add(SalesPermissionManifestV1.OrdersSignedApprovalRecord);

        var empty = await service.GetAsync(Order);
        store.OrderVisible = false;
        var missing = await Refused(() => service.GetAsync(Order));

        Assert.False(empty.SignedApprovalReceived);
        Assert.Empty(empty.Items);
        Assert.Equal((404, "not_found"), (missing.Status, missing.Code));
    }

    [Fact]
    public async Task The_read_without_the_draft_read_is_403()
    {
        var (service, _, _, authority) = Build();
        authority.Denied.Add(SalesPermissionManifestV1.DraftsRead);

        var refused = await Refused(() => service.GetAsync(Order));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task A_withdrawal_needs_a_reason_and_keeps_the_record_as_passive()
    {
        var (service, store, _, _) = Build();
        await service.RecordAsync(Order, Request(), Metadata);
        var approvalId = store.Records[0].Id;

        var blank = await Refused(() => service.DeactivateAsync(Order, approvalId,
            new DeactivateSalesOrderSignedApprovalRequest("1", " "), Metadata with { IdempotencyKey = "SENTETIK-key-2" }));
        var withdrawn = await service.DeactivateAsync(Order, approvalId,
            new DeactivateSalesOrderSignedApprovalRequest("1", "SENTETIK yanlış dosya"), Metadata with { IdempotencyKey = "SENTETIK-key-3" });

        Assert.Equal((422, SalesRuleCodes.SignedApprovalReasonRequired), (blank.Status, blank.Code));
        Assert.False(withdrawn.Detail.SignedApprovalReceived);
        Assert.Null(withdrawn.Detail.Active);
        var item = Assert.Single(withdrawn.Detail.Items);
        Assert.Equal(("Passive", "2", "Withdrawn", "SENTETIK yanlış dosya"), (item.State, item.Version, item.DeactivationKind, item.DeactivationReason));
    }

    [Fact]
    public async Task Withdrawing_a_passive_record_is_a_recorded_state_conflict_and_a_stale_version_a_version_conflict()
    {
        var (service, store, _, _) = Build();
        await service.RecordAsync(Order, Request(), Metadata);
        var approvalId = store.Records[0].Id;

        var stale = await Refused(() => service.DeactivateAsync(Order, approvalId,
            new DeactivateSalesOrderSignedApprovalRequest("2", "SENTETIK"), Metadata with { IdempotencyKey = "SENTETIK-key-2" }));
        await service.DeactivateAsync(Order, approvalId, new DeactivateSalesOrderSignedApprovalRequest("1", "SENTETIK"),
            Metadata with { IdempotencyKey = "SENTETIK-key-3" });
        var passive = await Refused(() => service.DeactivateAsync(Order, approvalId,
            new DeactivateSalesOrderSignedApprovalRequest("2", "SENTETIK"), Metadata with { IdempotencyKey = "SENTETIK-key-4" }));

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (stale.Status, stale.Code));
        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (passive.Status, passive.Code));
        Assert.Equal(2, store.Refusals.Count);
    }

    [Fact]
    public async Task A_withdrawal_of_an_unknown_record_is_404()
    {
        var (service, _, _, _) = Build();

        var refused = await Refused(() => service.DeactivateAsync(Order, Guid.NewGuid(),
            new DeactivateSalesOrderSignedApprovalRequest("1", "SENTETIK"), Metadata));

        Assert.Equal((404, "not_found"), (refused.Status, refused.Code));
    }

    [Fact]
    public void The_record_name_is_registered_in_the_central_catalogue_and_company_wide()
    {
        Assert.True(ErpPermissions.IsKnown(SalesPermissionManifestV1.OrdersSignedApprovalRecord));
        Assert.Equal(ErpPermissions.SalesOrdersSignedApprovalRecord, SalesPermissionManifestV1.OrdersSignedApprovalRecord);
        Assert.Contains(SalesPermissionManifestV1.OrdersSignedApprovalRecord, SalesPermissionManifestV1.Names);
    }

    [Fact]
    public void The_domain_refuses_a_malformed_document_hash_and_restores_only_consistent_state()
    {
        var malformed = Assert.Throws<SalesRuleException>(() => SalesOrderSignedApproval.Record(Guid.NewGuid(), Company, Order, 0, 1,
            new SalesOrderSignedApprovalDocument(Document, 1, Sha.ToUpperInvariant()), Seller, Now));
        Assert.Equal(SalesRuleCodes.SignedApprovalDocumentInvalid, malformed.Code);
        Assert.Throws<InvalidOperationException>(() => SalesOrderSignedApproval.Restore(Guid.NewGuid(), Company, Order, 0, 1,
            new SalesOrderSignedApprovalDocument(Document, 1, Sha), SalesOrderSignedApprovalState.Passive, 1, Seller, Now, null));

        var item = SalesOrderSignedApproval.Record(Guid.NewGuid(), Company, Order, 0, 1,
            new SalesOrderSignedApprovalDocument(Document, 1, Sha), Seller, Now);
        Assert.Throws<ArgumentException>(() => item.Supersede(item.Id, Seller, Now));
        item.Supersede(Guid.NewGuid(), Seller, Now);
        Assert.Equal((SalesOrderSignedApprovalState.Passive, 2L), (item.State, item.Version));
        Assert.Equal(SalesRuleCodes.StateConflict,
            Assert.Throws<SalesRuleException>(() => item.Withdraw(2, Seller, "SENTETIK", Now)).Code);
    }

    [Fact]
    public void The_replay_identity_covers_the_order_the_document_and_the_expected_version()
    {
        var baseline = SalesOrderSignedApprovalHash.Compute(Order, Request());
        Assert.Equal(baseline, SalesOrderSignedApprovalHash.Compute(Order, Request()));
        Assert.NotEqual(baseline, SalesOrderSignedApprovalHash.Compute(Guid.NewGuid(), Request()));
        Assert.NotEqual(baseline, SalesOrderSignedApprovalHash.Compute(Order, Request("8")));
        Assert.NotEqual(baseline, SalesOrderSignedApprovalHash.Compute(Order, Request() with { DocumentId = Guid.NewGuid() }));
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

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Denied.Contains(permission)
                ? PermissionScope.Denied
                : new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(),
                    new HashSet<Guid>()));
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

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Vault : ISalesOrderSignedApprovalDocumentPortV1
    {
        public int Calls { get; private set; }
        public (Guid, Guid, Guid) Last { get; private set; }
        public SalesSignedApprovalDocumentCheckV1 Answer { get; set; } = new(SalesSignedApprovalDocumentOutcomeV1.Accepted, 2, Sha);

        public Task<SalesSignedApprovalDocumentCheckV1> CheckAsync(Guid companyId, Guid orderId, Guid documentId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Last = (companyId, orderId, documentId);
            return Task.FromResult(Answer);
        }
    }

    /// <summary>An in-memory store with the attempt rules of the real one (key identity, same content and actor, recorded refusal).</summary>
    private sealed class Store : ISalesOrderSignedApprovalStore
    {
        private readonly Dictionary<(string, string), (string Actor, string Hash, Guid? OrderId, SalesDraftTerminalRefusal? Refusal, string Correlation)> attempts = [];

        public List<string> Calls { get; } = [];
        public List<SalesOrderSignedApproval> Records { get; } = [];
        public List<SalesDraftTerminalRefusal> Refusals { get; } = [];
        public SalesOrderSignedApprovalOrderFactsV1 Order { get; set; } = new(SalesOrderSignedApprovalServiceTests.Order, 7, 3, true, true);
        public bool OrderVisible { get; set; } = true;

        public Task<SalesOrderSignedApprovalReplay?> FindSignedApprovalReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            string commandName, CancellationToken cancellationToken = default)
        {
            Calls.Add("replay");
            if (!attempts.TryGetValue((commandName, envelope.IdempotencyKey), out var attempt))
            {
                return Task.FromResult<SalesOrderSignedApprovalReplay?>(null);
            }

            if (attempt.Actor != envelope.ActorId || attempt.Hash != requestHash)
            {
                throw new SalesDraftStoreException(SalesDraftStoreCodes.SameKeyPayloadMismatch, "SENTETIK");
            }

            return Task.FromResult<SalesOrderSignedApprovalReplay?>(new(attempt.OrderId, attempt.Refusal, attempt.Correlation));
        }

        public Task<SalesOrderSignedApprovalOrderFactsV1?> FindSignedApprovalOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            Calls.Add("order");
            return Task.FromResult(OrderVisible && orderId == Order.OrderId ? Order : null);
        }

        public Task<SalesOrderSignedApprovalStoreResult> StoreSignedApprovalAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            long expectedOrderVersion, SalesOrderSignedApproval approval, SalesDraftStateAudit audit, SalesDraftStateAudit supersededAudit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("store");
            if (expectedOrderVersion != Order.Version)
            {
                throw new SalesVersionConflictException(expectedOrderVersion, Order.Version);
            }

            foreach (var active in Records.Where(item => item.State == SalesOrderSignedApprovalState.Active))
            {
                active.Supersede(approval.Id, envelope.ActorId, Now);
            }

            Records.Add(approval);
            attempts[(SalesOrderSignedApprovalCommands.RecordV1, envelope.IdempotencyKey)] =
                (envelope.ActorId, requestHash, approval.OrderId, null, envelope.CorrelationId);
            return Task.FromResult(new SalesOrderSignedApprovalStoreResult(approval.OrderId, SalesDraftWriteOutcome.Recorded,
                envelope.CorrelationId));
        }

        public Task<SalesOrderSignedApprovalStoreResult?> DeactivateSignedApprovalAsync(SalesDraftWriteEnvelope envelope,
            string requestHash, Guid orderId, Guid approvalId, Action<SalesOrderSignedApproval, DateTimeOffset> mutate,
            SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            Calls.Add("deactivate");
            var item = Records.SingleOrDefault(candidate => candidate.Id == approvalId && candidate.OrderId == orderId);
            if (item is null)
            {
                return Task.FromResult<SalesOrderSignedApprovalStoreResult?>(null);
            }

            mutate(item, Now);
            attempts[(SalesOrderSignedApprovalCommands.DeactivateV1, envelope.IdempotencyKey)] =
                (envelope.ActorId, requestHash, orderId, null, envelope.CorrelationId);
            return Task.FromResult<SalesOrderSignedApprovalStoreResult?>(new(orderId, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task RecordSignedApprovalRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid orderId,
            SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
        {
            Calls.Add("refusal");
            Refusals.Add(refusal);
            attempts[(commandName, envelope.IdempotencyKey)] = (envelope.ActorId, requestHash, null, refusal, envelope.CorrelationId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SalesOrderSignedApproval>> ListSignedApprovalsAsync(Guid orderId, int limit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<SalesOrderSignedApproval>>([.. Records.Where(item => item.OrderId == orderId)]);
        }
    }
}
