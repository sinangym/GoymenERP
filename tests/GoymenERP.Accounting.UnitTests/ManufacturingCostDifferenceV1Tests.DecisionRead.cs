using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class ManufacturingCostDifferenceV1Tests
{
    [Fact]
    public async Task Purpose_read_returns_all_retained_outputs_and_selected_plan_without_general_prepare_permission_or_writes()
    {
        var store = new SyntheticCostDecisionProtocolStore(); var source = store.Source!;
        var service = DecisionService(store);
        var answer = await service.ReadDecisionAsync(EventId, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, answer.Status); var detail = Assert.IsType<ManufacturingCostDifferenceDecisionDetailV1>(answer.Resource);
        Assert.Equal(source.Proposal, detail.Proposal); Assert.Equal(source.PreparedAtUtc, detail.PreparedAtUtc);
        Assert.Equal(ManufacturingCostDifferenceServiceV1.CostDifferenceSourceFactsDigest(source), detail.SourceFactsDigest);
        Assert.Equal(ManufacturingCostDifferenceServiceV1.CostDifferencePreviewDigest(source), detail.PreviewDigest);
        var output = Assert.Single(detail.Proposal.Outputs); Assert.Equal("600", output.ConsumedTlDifference);
        Assert.Equal("2000", output.OriginalTlAmount); Assert.Equal("4", output.UsdUnitCostPerKg);
        Assert.Equal(source.Proposal.AccountPlan, detail.Proposal.AccountPlan); Assert.Equal(0, store.Effects); Assert.Empty(store.Commands);
    }

    [Theory]
    [InlineData("maker")] [InlineData("permission")] [InlineData("scope")] [InlineData("null-scope")]
    [InlineData("member")] [InlineData("actor")] [InlineData("company")] [InlineData("cancel")]
    public async Task Purpose_read_never_releases_after_normally_completed_final_consumer_authority_or_context_loss(string fault)
    {
        var store = new SyntheticCostDecisionProtocolStore(); var authority = new CostAuthority(); var user = new CostUser(); var company = new CostCompany();
        if (fault == "maker") user.Actor = Maker;
        var service = DecisionService(store, authority, company, user); using var cancel = new CancellationTokenSource(); var callbacks = 0;
        Task Consumer(CancellationToken _)
        {
            if (++callbacks == 4)
            {
                if (fault == "permission") authority.Granted = false; if (fault == "scope") authority.CompanyWide = false;
                if (fault == "null-scope") authority.Null = true; if (fault == "member") user.Member = false;
                if (fault == "actor") user.Actor = Maker; if (fault == "company") company.Set(Guid.NewGuid()); if (fault == "cancel") cancel.Cancel();
            }
            return Task.CompletedTask;
        }
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(EventId, Consumer, cancel.Token));
        else { var answer = await service.ReadDecisionAsync(EventId, Consumer); Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource); }
        Assert.Equal(4, callbacks); Assert.Empty(store.Commands); Assert.Equal(0, store.Effects);
    }

    [Theory]
    [InlineData("null-view")] [InlineData("company")] [InlineData("proposal")] [InlineData("source-hash")]
    [InlineData("source-json")] [InlineData("source-identity")] [InlineData("output-amount")]
    [InlineData("prepared-time")] [InlineData("currency")] [InlineData("plan")] [InlineData("journal")]
    [InlineData("signed-total")] [InlineData("signed-format")]
    public async Task Purpose_read_malformed_or_foreign_retained_facts_are_unavailable_not_human_success(string fault)
    {
        var store = new SyntheticCostDecisionProtocolStore(); var source = store.Source!;
        store.Source = fault switch
        {
            "null-view" => source with { Proposal = null! }, "company" => source with { Proposal = source.Proposal with { CompanyId = Guid.NewGuid() } },
            "proposal" => source with { Proposal = source.Proposal with { ProposalId = Guid.NewGuid() } }, "source-hash" => source with { SourceHash = new('F', 64) },
            "source-json" => source with { SourceJson = "{", SourceHash = Hash("{") }, "source-identity" => source with { Proposal = source.Proposal with { GenerationId = Guid.NewGuid() } },
            "output-amount" => source with { Proposal = source.Proposal with { Outputs = [source.Proposal.Outputs[0] with { ConsumedTlDifference = "601" }] } },
            "signed-total" => source with { Proposal = source.Proposal with { SignedTlDifference = "601" } },
            "signed-format" => source with { Proposal = source.Proposal with { SignedTlDifference = "600.0" } },
            "prepared-time" => source with { PreparedAtUtc = null }, "currency" => source with { CurrencyId = null },
            "plan" => source with { Proposal = source.Proposal with { AccountPlan = null } },
            _ => source with { Proposal = source.Proposal with { Status = "Posted", ApprovedBy = Checker, JournalId = Guid.NewGuid() }, ApprovedAtUtc = DecisionAt, JournalBound = false },
        };
        var answer = await DecisionService(store).ReadDecisionAsync(EventId, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, answer.Status); Assert.Null(answer.Resource); Assert.Empty(store.Commands);
    }

    [Fact]
    public async Task Purpose_read_distinguishes_missing_identity_unsupported_port_and_cancelled_entry()
    {
        var store = new SyntheticCostDecisionProtocolStore { Source = null }; var service = DecisionService(store);
        Assert.Equal(AccountingApiStatus.NotFound, (await service.ReadDecisionAsync(EventId, _ => Task.CompletedTask)).Status);
        Assert.Equal(AccountingApiStatus.Invalid, (await service.ReadDecisionAsync(Guid.Empty, _ => Task.CompletedTask)).Status);
        var legacy = new SyntheticCostApprovalProtocolStore();
        var old = new ManufacturingCostDifferenceServiceV1(legacy, legacy, new CostAuthority(), new CostCompany(), new CostUser(), new Correlation());
        Assert.Equal(AccountingApiStatus.Unavailable, (await old.ReadDecisionAsync(EventId, _ => Task.CompletedTask)).Status);
        using var cts = new CancellationTokenSource(); cts.Cancel(); var called = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(EventId, _ => { called = true; return Task.CompletedTask; }, cts.Token));
        Assert.False(called);
    }

    [Fact]
    public async Task Genuine_synthetic_protocol_receipt_retains_original_json_key_versions_actor_and_public_wire_after_plan_reader_is_absent()
    {
        var store = new SyntheticCostDecisionProtocolStore(); var service = DecisionService(store);
        var before = ManufacturingCostDifferenceServiceV1.CostDifferenceSourceFactsDigest(store.Source!);
        var first = await service.ApproveAsync(EventId, 2, "owner-key"); var replay = await service.ApproveAsync(EventId, 2, "owner-key");
        var receipt = Assert.IsType<ManufacturingCostDifferenceApprovalReceiptV1>(first.Resource?.ApprovalReceipt);
        Assert.Equal(2, receipt.ExpectedVersion); Assert.Equal(3, receipt.ResultVersion); Assert.Equal(before, receipt.SourceFactsDigest);
        Assert.Equal(Checker, receipt.ActorId); Assert.Equal(Maker, receipt.PreparedBy); Assert.Equal(DecisionAt, receipt.ApprovedAtUtc);
        Assert.Equal(receipt, replay.Resource?.ApprovalReceipt); Assert.True(replay.IsReplay); Assert.Equal("synthetic-cost", replay.OriginalCorrelationId);
        var command = Assert.Single(store.Commands); Assert.Equal(Hash(command.ResultJson), receipt.OriginalResultDigest);
        Assert.Equal(ManufacturingCostDifferenceServiceV1.CostDifferenceOriginalResultDigest(first), receipt.OriginalResultDigest);
        Assert.Equal(8, JsonSerializer.SerializeToElement(first, Json).EnumerateObject().Count());
        Assert.Equal(20, JsonSerializer.SerializeToElement(first.Resource, Json).EnumerateObject().Count());
        Assert.DoesNotContain("approvalReceipt", command.ResultJson); Assert.DoesNotContain("approvalReceipt", JsonSerializer.Serialize(first, Json));
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }

    [Theory]
    [InlineData("unknown-root")] [InlineData("unknown-resource")] [InlineData("private-proof")]
    [InlineData("actor")] [InlineData("key")] [InlineData("payload")] [InlineData("correlation")]
    [InlineData("amount")] [InlineData("version")] [InlineData("prepared")] [InlineData("journal")]
    public async Task Original_receipt_binder_refuses_closed_world_json_or_exact_command_source_mismatch(string fault)
    {
        var store = new SyntheticCostDecisionProtocolStore(); var answer = await DecisionService(store).ApproveAsync(EventId, 2, "original");
        var command = Assert.Single(store.Commands); var source = store.Source!;
        if (fault is "unknown-root" or "unknown-resource" or "private-proof")
        {
            var json = JsonNode.Parse(command.ResultJson)!.AsObject();
            if (fault == "unknown-root") json["unexpected"] = true;
            else json["resource"]!.AsObject()[fault == "private-proof" ? "approvalReceipt" : "unexpected"] = true;
            command = command with { ResultJson = json.ToJsonString() };
        }
        if (fault == "actor") command = command with { ActorId = Maker.ToString("D") };
        if (fault == "key") command = command with { IdempotencyKeyHash = Hash("other") };
        if (fault == "payload") command = command with { RequestHash = Hash("other") };
        if (fault == "correlation") command = command with { OriginalCorrelationId = "other" };
        if (fault == "amount") source = source with { Proposal = source.Proposal with { SignedTlDifference = "601" } };
        if (fault == "version") source = source with { Proposal = source.Proposal with { Version = 4 } };
        if (fault == "prepared") source = source with { Proposal = source.Proposal with { PreparedBy = Checker } };
        if (fault == "journal") source = source with { JournalBound = false };
        Assert.Null(ManufacturingCostDifferenceServiceV1.BindCostDifferenceApprovalReceipt(command, source, answer, 2, "original", Checker));
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }

    [Fact]
    public void Native_output_formatter_keeps_exact_scalar_and_ratio_text_without_recalculation()
    {
        var source = Source(); var scalar = Assert.Single(ManufacturingCostDifferenceServiceV1.CostDifferenceOutputs(source));
        Assert.Equal(JsonSerializer.Serialize(new ManufacturingCostDifferenceOutputViewV1(source.Outputs[0].OutputId,
            source.Outputs[0].ReceiptMovementId, source.Outputs[0].LabelId, "100", "70", "30", "20", "1400", "600", "4", "2000"), Json), JsonSerializer.Serialize(scalar, Json));
        source = source with { Outputs = [source.Outputs[0] with { RemainingQuantityKg = null, ConsumedQuantityKg = null,
            RemainingQuantityKgRatio = new("2", "3"), ConsumedQuantityKgRatio = new("1", "3") }] };
        var ratio = Assert.Single(ManufacturingCostDifferenceServiceV1.CostDifferenceOutputs(source));
        Assert.Equal("2 / 3", ratio.RemainingQuantityKg); Assert.Equal("1 / 3", ratio.ConsumedQuantityKg); Assert.Equal("600", ratio.ConsumedTlDifference);
    }

    [Fact]
    public void Native_admission_total_helper_keeps_signed_exact_aggregate_and_canonical_text()
    {
        var source = Source();
        foreach (var value in new[] { 600m, -600m, 33.3400m, 0m })
        {
            var frozen = source with { Outputs = [source.Outputs[0] with { ConsumedTlDifference = value }] };
            Assert.True(ManufacturingCostDifferenceServiceV1.TryCostDifferenceSignedTotal(frozen, out var total, out var text));
            Assert.Equal(value, total); Assert.Equal(value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture), text);
        }
        var overflow = source with { Outputs = [source.Outputs[0] with { ConsumedTlDifference = decimal.MaxValue }, source.Outputs[0] with { ConsumedTlDifference = 1m }] };
        Assert.False(ManufacturingCostDifferenceServiceV1.TryCostDifferenceSignedTotal(overflow, out _, out _));
    }

    [Fact]
    public async Task Invalid_owner_total_after_native_success_keeps_native_answer_without_forging_receipt_or_claiming_rollback()
    {
        var store = new SyntheticCostDecisionProtocolStore(); store.Source = store.Source! with { Proposal = store.Source!.Proposal with { SignedTlDifference = "601" } };
        var service = DecisionService(store); var first = await service.ApproveAsync(EventId, 2, "invalid-source-total");
        Assert.Equal(AccountingApiStatus.Ok, first.Status); Assert.Null(first.Resource?.ApprovalReceipt);
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands); // Explicit protocol effects remain; this is no rollback proof.
        var replay = await service.ApproveAsync(EventId, 2, "invalid-source-total");
        Assert.True(replay.IsReplay); Assert.Equal("601", replay.Resource?.SignedTlDifference); Assert.Null(replay.Resource?.ApprovalReceipt);
        Assert.Equal(1, store.Effects); Assert.Single(store.Commands);
    }

    private static readonly DateTimeOffset DecisionAt = DateTimeOffset.Parse("2026-11-18T10:00:00Z");
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static ManufacturingCostDifferenceServiceV1 DecisionService(SyntheticCostDecisionProtocolStore store, CostAuthority? authority = null,
        CostCompany? company = null, CostUser? user = null) => new(store, store, authority ?? new(), company ?? new(), user ?? new(), new Correlation());
    private static ManufacturingCostDifferenceDecisionSourceV1 DecisionSource()
    {
        var source = Source(); var json = JsonSerializer.Serialize(source, Json); var p = View() with
        {
            ManufacturingOrderId = source.ManufacturingOrderId, GenerationId = source.GenerationId,
            Outputs = ManufacturingCostDifferenceServiceV1.CostDifferenceOutputs(source),
            AccountPlan = new(Guid.NewGuid(), 2, 1, "SENTETIK-plan/2", new('a', 64),
                new(Preparation().DebitAccountId, "620", "SENTETIK debit"), new(Preparation().CreditAccountId, "151", "SENTETIK credit")),
        };
        return new(p, json, Hash(json), "synthetic-cost", Guid.NewGuid(), DecisionAt.AddDays(-1), null, false, null);
    }
    // Explicit owner protocol fixture only; no relational transaction, durable Audit or SQL acceptance is claimed.
    private sealed class SyntheticCostDecisionProtocolStore : IManufacturingCostDifferenceStoreV1, IManufacturingCostDifferenceDecisionStoreV1, IAccountingCommandLog
    {
        public ManufacturingCostDifferenceDecisionSourceV1? Source = DecisionSource(); public int Effects;
        public List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<ManufacturingCostDifferenceDecisionSourceV1?> ReadCostDifferenceDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); var answer = Source; await fence(token); return answer; }
        public async Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> RunApprovalAsync(Func<bool, Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { try { await fence(token); var answer = await attempt(false); await fence(token); return answer; } catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); } }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(x => x.CommandName == command && x.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public async Task<ManufacturingCostDifferenceViewV1> ApproveAsync(Guid id, long version, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            await fence(token); var p = Source!.Proposal with { Version = checked(version + 1), Status = "Posted", ApprovedBy = actor, JournalId = Guid.NewGuid() };
            Source = Source with { Proposal = p, ApprovedAtUtc = DecisionAt, JournalBound = true, JournalCorrelationId = metadata.CorrelationId };
            Effects++; Commands.Add(metadata.Completion!(p)); return p;
        }
        public Task AcceptAsync(GoymenERP.BuildingBlocks.Messaging.WorkInboxEventV1 envelope, ManufacturingCostDifferenceEventV1 source, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ManufacturingCostDifferenceViewV1>> ListAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<ManufacturingCostDifferenceViewV1?> FindAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ManufacturingCostDifferenceViewV1> PrepareAsync(PrepareManufacturingCostDifferenceV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
    }
}
