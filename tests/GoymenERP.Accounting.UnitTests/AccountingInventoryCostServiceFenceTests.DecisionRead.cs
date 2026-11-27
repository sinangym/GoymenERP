using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingInventoryCostServiceFenceTests
{
    private static readonly JsonSerializerOptions DecisionJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset DecisionAt = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(2L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue - 1)]
    public void Original_receipt_preserves_full_native_Int64_expected_and_result_versions(long version)
    {
        var source = DecisionSource(AccountingInventoryCostKindV1.PurchaseInvoiceDifference, "Posted", version);
        var answer = DecisionAnswer(source.Proposal); var command = DecisionCommand(answer, source.Proposal.Kind) with
        { RequestHash = AccountingInventoryCostServiceV1.InventoryCostApprovalPayloadHash(SafetyId, source.Proposal.Kind, version) };
        var receipt = AccountingInventoryCostServiceV1.BindInventoryCostApprovalReceipt(command, source, answer, SafetyId, source.Proposal.Kind, version, "receipt", Actor);
        Assert.NotNull(receipt); Assert.Equal(version, receipt.ExpectedVersion); Assert.Equal(version + 1, receipt.ResultVersion);
    }
    [Theory]
    [InlineData(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Posted")]
    [InlineData(AccountingInventoryCostKindV1.PurchaseInvoiceDifference, "Posted")]
    [InlineData(AccountingInventoryCostKindV1.SalesInvoicedShipment, "RoundedNoCost")]
    [InlineData(AccountingInventoryCostKindV1.PurchaseInvoiceDifference, "Stale")]
    public async Task Purpose_native_receipt_uses_original_Nplus1_and_exact_kind_wire_without_live_plan(AccountingInventoryCostKindV1 kind, string status)
    {
        var port = new DecisionProtocol(DecisionSource(kind, status)); var authority = new SafetyAuthority();
        var service = DecisionService(port, authority: authority); var source = port.Source;
        var before = await service.ReadDecisionAsync(SafetyId, kind, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, before.Status); Assert.Equal(source.Proposal, before.Resource?.Proposal);
        Assert.Equal(DecisionAt.AddSeconds(1), before.Resource?.PreparedRecordedAtUtc);
        Assert.Equal(kind == AccountingInventoryCostKindV1.SalesInvoicedShipment ? NormalJournalPermissionNamesV1.DraftApprove : AccountingJournalReversalPermissionNamesV1.Approve, authority.Permissions[0]);
        var answer = await service.ApproveAsync(SafetyId, kind, 7, "receipt"); var receipt = Assert.IsType<AccountingInventoryCostApprovalReceiptV1>(answer.Resource?.ApprovalReceipt);
        Assert.Equal(status == "Stale" ? AccountingApiStatus.Conflict : AccountingApiStatus.Ok, answer.Status);
        Assert.Equal(status == "Stale" ? AccountingCostProposalStaleEventV1.ReasonCode : null, answer.Code);
        Assert.Equal(7L, receipt.ExpectedVersion); Assert.Equal(8L, receipt.ResultVersion); Assert.Equal(kind, receipt.Kind);
        Assert.Equal(8L, port.SelectedVersion); Assert.Equal(InventoryCostDigest(port.Commands.Single().ResultJson), receipt.OriginalResultDigest);
        var native = JsonSerializer.Serialize(answer, DecisionJson); Assert.DoesNotContain("approvalReceipt", native, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preparedRecordedAtUtc", native, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, JsonSerializer.SerializeToElement(answer, DecisionJson).EnumerateObject().Count());
        // A new current view is never consulted for an original-key result or used to reconstruct its answer.
        port.Current = source with { Proposal = source.Prepared with { Version = 25 }, Prepared = source.Prepared with { Version = 25 } };
        var replay = await service.ApproveAsync(SafetyId, kind, 7, "receipt");
        Assert.True(replay.IsReplay); Assert.Equal(receipt, replay.Resource?.ApprovalReceipt); Assert.Equal(1, port.Effects);
        Assert.Equal(8L, replay.Resource?.Version); Assert.Equal(8L, port.SelectedVersion);
    }
    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("permission")]
    [InlineData("scope")] [InlineData("null-scope")] [InlineData("cancel")]
    public async Task Last_normally_completed_consumer_callback_cannot_release_revoked_or_cancelled_owner_facts(string fault)
    {
        var port = new DecisionProtocol(DecisionSource(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Posted"));
        var authority = new SafetyAuthority(); var company = new MutableCompany(); var user = new MutableUser(); using var cancellation = new CancellationTokenSource();
        var service = DecisionService(port, company, user, authority); var calls = 0;
        Task Consumer(CancellationToken _) { if (++calls == 4) Change(fault, company, user, authority, cancellation); return Task.CompletedTask; }
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(SafetyId, port.Source.Proposal.Kind, Consumer, cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied, (await service.ReadDecisionAsync(SafetyId, port.Source.Proposal.Kind, Consumer)).Status);
        Assert.Equal(0, port.Effects); Assert.Empty(port.Commands);
    }
    [Theory]
    [InlineData("null")] [InlineData("foreign")] [InlineData("kind")] [InlineData("amount")] [InlineData("source")]
    [InlineData("prepared-version")] [InlineData("journal")] [InlineData("private-json")]
    public async Task Malformed_owner_source_cannot_certify_a_human_preview(string fault)
    {
        var source = DecisionSource(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Posted");
        var altered = JsonNode.Parse(source.SourceJson)!.AsObject(); altered["unexpected"] = true;
        var alteredJson = altered.ToJsonString(DecisionJson);
        source = fault switch
        {
            "null" => source with { Proposal = null! },
            "foreign" => source with { Proposal = source.Proposal with { CompanyId = Guid.NewGuid() } },
            "kind" => source with { Proposal = source.Proposal with { Kind = AccountingInventoryCostKindV1.PurchaseInvoiceDifference } },
            "amount" => source with { Prepared = source.Prepared with { Preparation = source.Prepared.Preparation! with { SignedAmount = "601" } }, Proposal = source.Proposal with { Preparation = source.Proposal.Preparation! with { SignedAmount = "601" } } },
            "source" => source with { SourceHash = new string('A', 64) },
            "prepared-version" => source with { Prepared = source.Prepared with { Version = 6 } },
            "journal" => source with { JournalBound = false },
            _ => source with { SourceJson = alteredJson, SourceHash = InventoryCostDigest(alteredJson) },
        };
        var answer = await DecisionService(new(source)).ReadDecisionAsync(SafetyId, AccountingInventoryCostKindV1.SalesInvoicedShipment, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Null(answer.Resource);
    }
    [Theory]
    [InlineData("missing")] [InlineData("key")] [InlineData("actor")] [InlineData("payload")] [InlineData("unknown-root")]
    [InlineData("unknown-resource")] [InlineData("private-proof")] [InlineData("version")] [InlineData("correlation")]
    public void Genuine_original_json_and_exact_native_identity_are_required_for_private_receipt(string fault)
    {
        var source = DecisionSource(AccountingInventoryCostKindV1.PurchaseInvoiceDifference, "Posted"); var answer = DecisionAnswer(source.Proposal);
        var command = DecisionCommand(answer, source.Proposal.Kind); AccountingCommandRecord? selected = command;
        if (fault == "missing") selected = null;
        else if (fault == "key") selected = command with { IdempotencyKeyHash = new string('A', 64) };
        else if (fault == "actor") selected = command with { ActorId = Guid.NewGuid().ToString("D") };
        else if (fault == "payload") selected = command with { RequestHash = new string('A', 64) };
        else if (fault == "correlation") selected = command with { OriginalCorrelationId = "other" };
        else
        {
            var json = JsonNode.Parse(command.ResultJson)!.AsObject();
            if (fault == "unknown-root") json["unexpected"] = true;
            else if (fault == "version") json["resource"]!["version"] = 9;
            else json["resource"]![fault == "private-proof" ? "approvalReceipt" : "unexpected"] = true;
            selected = command with { ResultJson = json.ToJsonString(DecisionJson) };
        }
        Assert.Null(AccountingInventoryCostServiceV1.BindInventoryCostApprovalReceipt(selected, source, answer, SafetyId, source.Proposal.Kind, 7, "receipt", Actor));
        Assert.NotNull(AccountingInventoryCostServiceV1.BindInventoryCostApprovalReceipt(command, source, answer, SafetyId, source.Proposal.Kind, 7, "receipt", Actor));
    }
    [Fact]
    public async Task Maker_missing_reader_and_non_approval_grants_do_not_release_cost_human_facts()
    {
        var source = DecisionSource(AccountingInventoryCostKindV1.SalesInvoicedShipment, "Posted"); var user = new MutableUser { UserId = source.Prepared.Preparation!.PreparedBy.ToString("D") };
        Assert.Equal(AccountingApiStatus.Denied, (await DecisionService(new(source), user: user).ReadDecisionAsync(SafetyId, source.Proposal.Kind, _ => Task.CompletedTask)).Status);
        var legacy = new AccountingInventoryCostServiceV1(new FailingOptionsStore(), new UnusedCommands(), new SafetyAuthority(), new MutableCompany(), new MutableUser(), SafetyCorrelation());
        Assert.Equal(AccountingApiStatus.Unavailable, (await legacy.ReadDecisionAsync(SafetyId, source.Proposal.Kind, _ => Task.CompletedTask)).Status);
        var denied = new SafetyAuthority { Granted = false };
        Assert.Equal(AccountingApiStatus.Denied, (await DecisionService(new(source), authority: denied).ReadDecisionAsync(SafetyId, source.Proposal.Kind, _ => Task.CompletedTask)).Status);
    }
    private static AccountingInventoryCostServiceV1 DecisionService(DecisionProtocol port, MutableCompany? company = null, MutableUser? user = null, SafetyAuthority? authority = null) =>
        new(port, port, authority ?? new SafetyAuthority(), company ?? new MutableCompany(), user ?? new MutableUser(), SafetyCorrelation());
    internal static AccountingInventoryCostDecisionSourceV1 DecisionSource(AccountingInventoryCostKindV1 kind, string status, long version = 7)
    {
        var zero = status == "RoundedNoCost"; var currency = Guid.NewGuid(); var ratio = zero ? new AccountingInventoryCostRatioV1("1", "1000") : new("600", "1");
        var component = new AccountingInventoryCostComponentV1("SENTETIK component", Guid.NewGuid(), Guid.NewGuid(), 1, null, ratio, "{}", InventoryCostDigest("{}"));
        var profile = Guid.NewGuid(); AccountingInventoryCostRoundingV1? rounding = null;
        if (zero) { Assert.True(AccountingInventoryCostRoundingV1Rules.TryResolve(component, currency, profile, 1, 2, out _, out rounding)); }
        var prep = new AccountingInventoryCostPreparationV1(Guid.NewGuid(), new(2026, 11, 28), new(2026, 11, 27), currency, zero ? "0" : "600", profile, 1, 2,
            new(InvoiceLineCogsClassificationV1.TradeGoods, Guid.NewGuid(), "SENTETIK", null, null, null, 0), new(Guid.NewGuid(), "151", "SENTETIK counter"),
            Guid.NewGuid(), 9, null, "SENTETIK plan", new string('A', 64), Guid.NewGuid(), DecisionAt, "SENTETIK reason", rounding);
        var source = new AccountingInventoryCostSourceV1(Company, Guid.NewGuid(), kind, Guid.NewGuid(), 4, Guid.NewGuid(), new(2026, 11, 27), "SENTETIK invoice", null, new string('B', 64), [component]);
        var prepared = new AccountingInventoryCostProposalV1(SafetyId, Company, source.EventId, kind, source.InvoiceId, 4, source.AdmissionId, source.InvoiceDate, source.OfficialInvoiceNumber,
            component, version, "Prepared", DecisionAt.AddDays(-1), prep);
        var proposal = prepared with { Status = status, Version = version + 1, ApprovedBy = status == "Stale" ? null : Actor, ApprovedAtUtc = status == "Stale" ? null : DecisionAt.AddMinutes(1),
            JournalId = status == "Posted" ? Guid.NewGuid() : null, StaleClosure = status == "Stale" ? new(Guid.NewGuid(), version, prep.PreparedBy, DecisionAt.AddMinutes(1),
                AccountingCostProposalStaleEventV1.ReasonCode, AccountingCostProposalStaleEventV1.ReasonText, "SENTETIK-inventory-approval", null) : null };
        var raw = JsonSerializer.Serialize(source, DecisionJson);
        return new(prepared, proposal, DecisionAt.AddSeconds(1), raw, InventoryCostDigest(raw), false, status == "Posted", status == "Posted" ? "SENTETIK-inventory-approval" : null);
    }
    private static string InventoryCostDigest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static AccountingApiResult<AccountingInventoryCostProposalV1> DecisionAnswer(AccountingInventoryCostProposalV1 p) =>
        new(p.Status == "Stale" ? AccountingApiStatus.Conflict : AccountingApiStatus.Ok, p, false, p.Status == "Stale" ? AccountingCostProposalStaleEventV1.ReasonCode : null);
    private static AccountingCommandRecord DecisionCommand(AccountingApiResult<AccountingInventoryCostProposalV1> answer, AccountingInventoryCostKindV1 kind) =>
        new(AccountingInventoryCostServiceV1.InventoryCostApproveCommand, AccountingInventoryCostServiceV1.InventoryCostApprovalKeyHash("receipt"), Actor.ToString("D"),
            AccountingInventoryCostServiceV1.InventoryCostApprovalPayloadHash(SafetyId, kind, 7), JsonSerializer.Serialize(answer, DecisionJson), "SENTETIK-inventory-approval");
    // Explicit synthetic command/store protocol only; it proves neither SQL rollback nor durable Audit.
    private sealed class DecisionProtocol(AccountingInventoryCostDecisionSourceV1 source) : IAccountingInventoryCostStoreV1, IAccountingInventoryCostDecisionStoreV1, IAccountingCommandLog
    {
        internal readonly AccountingInventoryCostDecisionSourceV1 Source = source; internal AccountingInventoryCostDecisionSourceV1? Current;
        internal long? SelectedVersion; internal int Effects; internal List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<AccountingInventoryCostDecisionSourceV1?> ReadInventoryCostDecisionAsync(Guid id, AccountingInventoryCostKindV1 kind, long? version, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); SelectedVersion = version; await fence(token); return version is null ? Current ?? Source : version == Source.Proposal.Version ? Source : null; }
        public async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> RunApprovalAsync(Func<bool, Task<AccountingApiResult<AccountingInventoryCostProposalV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { try { await fence(token); var answer = await attempt(false); await fence(token); return answer; } catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, null); } }
        public async Task<AccountingInventoryCostProposalV1> ApproveAsync(Guid id, AccountingInventoryCostKindV1 kind, long version, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        { await fence(token); Effects++; Commands.Add(metadata.Completion!(Source.Proposal)); return Source.Proposal; }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(c => c.CommandName == command && c.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token = default) => throw new NotSupportedException();
        public Task RecordAttemptAsync(AccountingCommandRecord record, Func<CancellationToken, Task> fence, CancellationToken token = default) => throw new NotSupportedException();
        public Task AcceptAsync(WorkInboxEventV1 envelope, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountingInventoryCostProposalV1>> ListAsync(AccountingInventoryCostKindV1 kind, CancellationToken token = default) => throw new NotSupportedException();
        public Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> PrepareAsync(Guid id, AccountingInventoryCostKindV1 kind, PrepareAccountingInventoryCostV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ProposeReversalAsync(Guid id, ProposeAccountingInventoryCostReversalV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AccountingInventoryCostProposalV1> ApproveCancellationAsync(Guid id, ApproveInventoryCostCancellationV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
    }
}
