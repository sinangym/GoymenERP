using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Answers;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiAskServiceTests
{
    [Fact]
    public async Task General_chat_final_visible_producer_records_exactly_one_real_receipt_without_ERP_sources()
    {
        var rig = new Harness();
        var result = await rig.Service([new ChatModeComposer(AiModelPlacementV1.Cloud)]).AskAsync(GeneralQuestion());
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var receipt = Assert.Single(rig.ReceiptStore.Receipts); var identity = Assert.Single(rig.ReceiptStore.Identities);
        Assert.Equal(receipt.AnswerId, result.Response!.Answer!.AnswerId);
        Assert.Equal(AiAskModesV1.GeneralChat, result.Response.Mode); Assert.False(result.Response.Answer.Grounded);
        Assert.Empty(result.Response.Answer.Citations); Assert.Empty(result.Response.ToolResults);
        Assert.Equal((Company, Actor.ToString("D"), receipt.AnswerId), (identity.CompanyId, identity.ActorId, identity.AnswerId));
        Assert.True(AiAnswerReceiptRulesV1.IsValid(receipt));
        Assert.Single(rig.GatewayStore.Reservations); Assert.Empty(rig.Tool.Contexts); Assert.Equal(0, rig.Provider.Calls);
    }

    [Theory]
    [InlineData("missing")] [InlineData("refused")] [InlineData("exception")] [InlineData("legacy-writer")]
    public async Task General_chat_does_not_release_an_answer_without_its_final_receipt(string fault)
    {
        var rig = new Harness(); rig.ReceiptStore.Refuse = fault == "refused"; rig.ReceiptStore.Throw = fault == "exception";
        var result = await rig.Service([new ChatModeComposer(AiModelPlacementV1.Cloud)], receiptsAvailable: fault != "missing",
            receiptWriter: fault == "legacy-writer" ? new LegacyOnlyReceiptWriter() : null).AskAsync(GeneralQuestion());
        Assert.Equal(AiAskStatusV1.Unavailable, result.Status); Assert.Null(result.Response);
        Assert.Empty(rig.ReceiptStore.Receipts); Assert.Empty(rig.ReceiptStore.Identities);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task General_chat_receipt_rechecks_current_authority_before_commit_and_after_durable_commit(bool afterCommit)
    {
        var rig = new Harness(); var writer = new GeneralReleaseReceiptWriter(() => rig.Authority.Revoke(ErpPermissions.AiPanelUse));
        if (!afterCommit) rig.ReceiptStore.BeforeCommit = () => rig.Authority.Revoke(ErpPermissions.AiPanelUse);
        var result = await rig.Service([new ChatModeComposer(AiModelPlacementV1.Cloud)],
            receiptWriter: afterCommit ? writer : null).AskAsync(GeneralQuestion());
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Null(result.Response);
        Assert.Equal(afterCommit, writer.Committed); // Post-commit denial does not claim rollback.
        Assert.Empty(rig.ReceiptStore.Receipts); Assert.Empty(rig.ReceiptStore.Identities);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task General_chat_receipt_cancellation_before_or_after_commit_releases_no_visible_answer(bool afterCommit)
    {
        var rig = new Harness(); using var cancelled = new CancellationTokenSource();
        var writer = new GeneralReleaseReceiptWriter(cancelled.Cancel);
        if (!afterCommit) rig.ReceiptStore.BeforeCommit = cancelled.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service([new ChatModeComposer(AiModelPlacementV1.Cloud)],
            receiptWriter: afterCommit ? writer : null).AskAsync(GeneralQuestion(), cancelled.Token));
        Assert.Equal(afterCommit, writer.Committed); Assert.Empty(rig.ReceiptStore.Receipts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task General_chat_company_change_before_or_after_receipt_commit_releases_no_answer(bool afterCommit)
    {
        var rig = new Harness(); var company = new GeneralReceiptMutableCompany(Company); rig.CompanyScope = company;
        var writer = new GeneralReleaseReceiptWriter(() => company.Set(OtherCompany));
        if (!afterCommit) rig.ReceiptStore.BeforeCommit = () => company.Set(OtherCompany);
        var result = await rig.Service([new ChatModeComposer(AiModelPlacementV1.Cloud)],
            receiptWriter: afterCommit ? writer : null).AskAsync(GeneralQuestion());
        Assert.Equal(AiAskStatusV1.Refused, result.Status); Assert.Null(result.Response);
        Assert.Equal(afterCommit, writer.Committed); Assert.Empty(rig.ReceiptStore.Receipts);
    }

    private sealed class GeneralReceiptMutableCompany(Guid company) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = company;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class LegacyOnlyReceiptWriter : IAiAnswerReceiptWriterV1
    {
        public Task<Guid?> RecordAsync(Guid companyId, string actorId, string correlationId, AiGatewayAnswerV1 answer,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("General chat must never call the legacy grounded writer.");
    }

    // Controlled post-commit owner seam, not an assertion of SQL rollback or provider delivery.
    private sealed class GeneralReleaseReceiptWriter(Action afterCommit) : IAiAnswerReceiptWriterV1
    {
        public bool Committed { get; private set; }
        public Task<Guid?> RecordAsync(Guid companyId, string actorId, string correlationId, AiGatewayAnswerV1 answer,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("General chat must select its explicit writer profile.");
        public async Task<Guid?> RecordGeneralChatAsync(Guid companyId, string actorId, string correlationId, AiGatewayAnswerV1 answer,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
        {
            await freshConsumerFence(cancellationToken); Committed = true; afterCommit(); return Guid.NewGuid();
        }
    }
}
