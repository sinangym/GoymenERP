using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Application.Inbox;

namespace GoymenERP.Accounting.UnitTests;

public sealed class AccountingSourceVoucherTests
{
    private static readonly Guid Company = Guid.Parse("b0438000-0000-4000-8000-000000000001");
    private static readonly Guid Actor = Guid.Parse("b0438000-0000-4000-8000-000000000002");
    private static readonly Guid Maker = Guid.Parse("b0438000-0000-4000-8000-000000000003");
    private static readonly Guid Invoice = Guid.Parse("b0438000-0000-4000-8000-000000000004");

    [Fact]
    public void Invoice_finalizer_cannot_approve_the_source_voucher_even_with_an_approval_grant()
    {
        Assert.Equal("source_voucher_approval_refused", Assert.Throws<AccountingStoreException>(() =>
            AccountingSourceVoucherRulesV1.RequireApproval(1, Maker, Maker, false)).Code);
        AccountingSourceVoucherRulesV1.RequireApproval(1, Maker, Actor, false);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void Cancelled_source_or_decided_voucher_cannot_be_posted(int state, bool cancelled) =>
        Assert.Equal("source_voucher_not_approvable", Assert.Throws<AccountingStoreException>(() =>
            AccountingSourceVoucherRulesV1.RequireApproval(state, Maker, Actor, cancelled)).Code);

    [Fact]
    public async Task Denied_approval_never_reads_command_history_or_writes_a_journal()
    {
        var f = new Fixture(); f.Authority.Granted = false;
        var answer = await f.Service.ApproveAsync(Invoice, 1, "SENTETIK-key");
        Assert.Equal(AccountingApiStatus.Denied, answer.Status);
        Assert.Equal(0, f.Commands.Reads); Assert.Equal(0, f.Store.Writes);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("permission")]
    public async Task Fresh_fence_after_owner_read_refuses_changed_actor_company_or_authority(string change)
    {
        var f = new Fixture(); f.Store.BeforeFence = () =>
        {
            if (change == "actor") f.User.UserId = Maker.ToString("D");
            if (change == "company") f.Company.Set(Guid.NewGuid());
            if (change == "permission") f.Authority.Granted = false;
        };
        Assert.Equal(AccountingApiStatus.Denied, (await f.Service.ApproveAsync(Invoice, 1, "SENTETIK-key")).Status);
        Assert.Equal(0, f.Store.Writes);
    }

    [Fact]
    public async Task Same_key_replays_the_original_approval_and_other_payload_conflicts()
    {
        var f = new Fixture();
        var first = await f.Service.ApproveAsync(Invoice, 1, "SENTETIK-key");
        var replay = await f.Service.ApproveAsync(Invoice, 1, "SENTETIK-key");
        var conflict = await f.Service.ApproveAsync(Invoice, 2, "SENTETIK-key");
        Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.True(replay.IsReplay);
        Assert.Equal("SENTETIK-correlation", replay.OriginalCorrelationId);
        Assert.Equal(AccountingApiCodes.IdempotencyConflict, conflict.Code); Assert.Equal(1, f.Store.Writes);
    }

    [Fact]
    public async Task Stale_version_is_a_durable_terminal_refusal_and_no_journal_is_created()
    {
        var f = new Fixture(); f.Store.Error = new AccountingVersionConflictException(1, 2);
        var first = await f.Service.ApproveAsync(Invoice, 1, "SENTETIK-stale");
        var replay = await f.Service.ApproveAsync(Invoice, 1, "SENTETIK-stale");
        Assert.Equal(AccountingApiStatus.Conflict, first.Status); Assert.True(replay.IsReplay);
        Assert.Equal(0, f.Store.Writes); Assert.NotNull(f.Commands.Record);
    }

    [Fact]
    public async Task Missing_period_is_waiting_without_a_terminal_command_or_a_fabricated_journal()
    {
        var f = new Fixture(); f.Store.Error = new AccountingStoreException("source_voucher_period_waiting", "SENTETIK");
        var answer = await f.Service.ApproveAsync(Invoice, 1, "SENTETIK-period");
        Assert.Equal(AccountingApiStatus.Unprocessable, answer.Status); Assert.Null(f.Commands.Record); Assert.Equal(0, f.Store.Writes);
    }

    [Fact]
    public void Cancellation_decoder_binds_original_admission_and_envelope_event_identity()
    {
        var id = Guid.NewGuid(); var payload = new AccountingSalesInvoiceCancelledEventV1(Company, id, Invoice,
            Guid.NewGuid(), 4, Actor, DateTimeOffset.Parse("2026-11-17T12:00:00Z"), "SENTETIK resmî iptal");
        var envelope = new GoymenERP.BuildingBlocks.Messaging.WorkInboxEventV1(AccountingSalesInvoiceCancelledInboxV1.SourceName,
            id, Company, AccountingSalesInvoiceCancelledInboxV1.EventType, 1, JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            payload.CancelledAtUtc, "SENTETIK-correlation", null);
        var company = new CompanyContext(); company.Set(Company);
        var read = AccountingSalesInvoiceCancelledInboxConsumerV1.Read(envelope, company);
        Assert.Equal(payload.AdmissionId, read.SourceDocumentId); Assert.Equal(4, read.FinalizedVersion);
        Assert.Throws<GoymenERP.BuildingBlocks.Messaging.OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceCancelledInboxConsumerV1.Read(envelope with { EventId = Guid.NewGuid() }, company));
        company.Set(Guid.NewGuid());
        Assert.Throws<GoymenERP.BuildingBlocks.Messaging.OutboxPermanentFailureException>(() =>
            AccountingSalesInvoiceCancelledInboxConsumerV1.Read(envelope, company));
    }

    private sealed class Fixture
    {
        public CompanyContext Company { get; } = new(); public User User { get; } = new(); public Authority Authority { get; } = new();
        public Commands Commands { get; } = new(); public Store Store { get; } public AccountingSourceVoucherServiceV1 Service { get; }
        public Fixture() { Company.Set(AccountingSourceVoucherTests.Company); Store = new(Commands); Service = new(Store, Commands, Authority, Company, User, new Correlation()); }
    }
    private sealed class Store(Commands commands) : IAccountingSourceVoucherStoreV1
    {
        public int Writes; public Action? BeforeFence; public Exception? Error;
        public Task<IReadOnlyList<AccountingSourceVoucherViewV1>> ListAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AccountingSourceVoucherViewV1>>([]);
        public Task<AccountingSourceVoucherViewV1?> FindAsync(Guid id, CancellationToken token) => Task.FromResult<AccountingSourceVoucherViewV1?>(null);
        public async Task<AccountingSourceVoucherViewV1> ApproveAsync(Guid id, long version, Guid actor,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token)
        {
            BeforeFence?.Invoke(); await fence(token); if (Error is not null) throw Error;
            Writes++; var view = new AccountingSourceVoucherViewV1(id, Company, "sales-invoice", Guid.NewGuid(), Invoice, 4, Maker,
                Guid.NewGuid(), Guid.NewGuid(), null, new(2026, 11, 17), null, "Posted", 2, 1, Guid.NewGuid(), id, actor,
                DateTimeOffset.UtcNow, null, [], "SENTETIK-1", "120");
            commands.Record = metadata.Completion!(view); return view;
        }
    }
    private sealed class Commands : IAccountingCommandLog
    {
        public AccountingCommandRecord? Record; public int Reads;
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token)
        { Reads++; return Task.FromResult(Record); }
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token) { Record = record; return Task.CompletedTask; }
    }
    private sealed class User : ICurrentUser
    {
        public string? UserId { get; set; } = Actor.ToString("D"); public bool IsAuthenticated => true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]); public bool CanAccessCompany(Guid id) => id == Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        { Assert.Equal(NormalJournalPermissionNamesV1.DraftApprove, permission); return Task.FromResult(Granted
            ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied); }
    }
    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "SENTETIK-correlation"; public string? CausationId => null;
        public string SourceChannel => SourceChannels.Web; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress = null, string? userAgent = null) => throw new NotSupportedException();
    }
}
