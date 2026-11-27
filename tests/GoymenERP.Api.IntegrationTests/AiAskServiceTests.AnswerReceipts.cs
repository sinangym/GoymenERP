using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Answers;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiAskServiceTests
{
    [Fact]
    public async Task A_successful_grounded_answer_has_a_real_content_free_receipt_and_separate_identity_with_record_time_retention()
    {
        var rig = new Harness();
        var result = await rig.Service().AskAsync(Ask());
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var receipt = Assert.Single(rig.ReceiptStore.Receipts);
        var identity = Assert.Single(rig.ReceiptStore.Identities);
        Assert.Equal(receipt.AnswerId, result.Response!.Answer!.AnswerId);
        Assert.True(AiAnswerReceiptRulesV1.IsValid(receipt));
        Assert.Equal(receipt.CreatedAtUtc.AddDays(90), receipt.RetainUntilUtc);
        Assert.Equal((Company, Actor.ToString("D"), receipt.AnswerId), (identity.CompanyId, identity.ActorId, identity.AnswerId));
        var stored = JsonSerializer.Serialize(receipt);
        Assert.DoesNotContain(result.Response.Answer.Text, stored);
        Assert.DoesNotContain(Actor.ToString("D"), stored); Assert.DoesNotContain("corr-ai-sentetik", stored);
        Assert.DoesNotContain(Target.ToString("D"), stored); Assert.DoesNotContain("SENTETIK kalem", stored);
    }

    [Theory]
    [InlineData("missing")] [InlineData("refused")] [InlineData("exception")]
    public async Task An_unavailable_or_failed_receipt_never_releases_a_successful_answer_or_fake_id(string failure)
    {
        var rig = new Harness(); rig.ReceiptStore.Refuse = failure == "refused"; rig.ReceiptStore.Throw = failure == "exception";
        var result = await rig.Service(receiptsAvailable: failure != "missing").AskAsync(Ask());
        Assert.Equal(AiAskStatusV1.Unavailable, result.Status); Assert.Null(result.Response);
        Assert.Empty(rig.ReceiptStore.Receipts); Assert.Empty(rig.ReceiptStore.Identities);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Authority_loss_at_the_receipt_boundary_never_releases_or_records_the_answer(bool panel)
    {
        var rig = new Harness(); rig.ReceiptStore.BeforeCommit = () => rig.Authority.Revoke(panel ? GoymenERP.BuildingBlocks.Security.ErpPermissions.AiPanelUse : ToolPermission);
        var result = await rig.Service().AskAsync(Ask());
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Null(result.Response);
        Assert.Equal(GoymenERP.BuildingBlocks.Security.Events.SecurityEventTypesV1.AiAuthorizationDenied, rig.Writer.Events[^1].EventType);
        Assert.Empty(rig.ReceiptStore.Receipts); Assert.Empty(rig.ReceiptStore.Identities);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Authority_loss_after_the_receipt_writer_commit_refuses_response_without_claiming_the_receipt_was_rolled_back(bool panel)
    {
        var rig = new Harness();
        var writer = new ReleaseRacingReceiptWriter(() => rig.Authority.Revoke(panel ? GoymenERP.BuildingBlocks.Security.ErpPermissions.AiPanelUse : ToolPermission));
        var result = await rig.Service(receiptWriter: writer).AskAsync(Ask());
        Assert.True(writer.Committed); Assert.NotEqual(Guid.Empty, writer.AnswerId);
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Null(result.Response);
        Assert.Equal(GoymenERP.BuildingBlocks.Security.Events.SecurityEventTypesV1.AiAuthorizationDenied, rig.Writer.Events[^1].EventType);
    }

    [Fact]
    public async Task No_supported_source_creates_neither_a_receipt_nor_an_answer_id()
    {
        var rig = new Harness(); rig.Tool.NextResult = new(GoymenERP.BuildingBlocks.Ai.AiToolOutcomeV1.NotFound);
        var result = await rig.Service().AskAsync(Ask());
        Assert.Equal(AiAskStatusV1.Answered, result.Status); Assert.Null(result.Response!.Answer);
        Assert.Empty(rig.ReceiptStore.Receipts); Assert.Empty(rig.ReceiptStore.Identities);
    }

    [Fact]
    public async Task Ask_has_no_idempotency_key_and_two_successful_requests_get_distinct_receipts_for_the_same_answer_digest()
    {
        var rig = new Harness(); var first = await rig.Service().AskAsync(Ask()); var second = await rig.Service().AskAsync(Ask());
        Assert.NotEqual(first.Response!.Answer!.AnswerId, second.Response!.Answer!.AnswerId);
        Assert.Equal(2, rig.ReceiptStore.Receipts.Count);
        Assert.Equal(rig.ReceiptStore.Receipts[0].AnswerSha256, rig.ReceiptStore.Receipts[1].AnswerSha256);
    }

    private sealed class ReleaseRacingReceiptWriter(Action afterCommit) : IAiAnswerReceiptWriterV1
    {
        public Guid AnswerId { get; } = Guid.NewGuid();
        public bool Committed { get; private set; }
        public async Task<Guid?> RecordAsync(Guid companyId, string actorId, string correlationId, AiGatewayAnswerV1 answer,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
        {
            await freshConsumerFence(cancellationToken);
            Committed = true;
            afterCommit();
            return AnswerId;
        }
    }

    // Real API receipt adapter, controlled atomic owner seam. Real SQL tests separately prove persistence/rollback.
    private sealed class AnswerReceiptTestStore : IAiAnswerReceiptPersistenceV1
    {
        public List<AiAnswerReceiptV1> Receipts { get; } = [];
        public List<AiAnswerReceiptIdentityV1> Identities { get; } = [];
        public bool Refuse; public bool Throw; public Action? BeforeCommit;
        public async Task<bool> TryAddAsync(AiAnswerReceiptV1 receipt, AiAnswerReceiptIdentityV1 identity,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
        {
            await freshConsumerFence(cancellationToken);
            if (Throw) throw new InvalidOperationException("SENTETIK store failure");
            if (Refuse) return false;
            BeforeCommit?.Invoke(); await freshConsumerFence(cancellationToken);
            Receipts.Add(receipt); Identities.Add(identity); return true;
        }
        public Task<AiAnswerReceiptV1?> GetOwnAsync(Guid companyId, Guid answerId, string actorId,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
