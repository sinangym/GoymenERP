using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed class ReversalProposalDecisionReceiptTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SourceId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Actual_native_receipt_binds_original_command_and_preserves_five_field_wire_including_raw_reject_hash(bool approve)
    {
        var (row, result, context, metadata, reason) = Fixture(approve);
        var proof = Restore(row, result, context, metadata, reason);
        Assert.NotNull(proof); Assert.Equal(row.PayloadHash, proof.PayloadHash); Assert.Equal("1", proof.ExpectedVersion);
        var wire = JsonSerializer.Serialize(result with { DecisionReceipt = proof }, Json);
        Assert.Equal(row.ResultJson, wire);
        using var document = JsonDocument.Parse(wire); Assert.Equal(5, document.RootElement.EnumerateObject().Count());
        Assert.False(document.RootElement.TryGetProperty("decisionReceipt", out _));
        var replay = Restore(row, result with { IsReplay = true }, context with { CorrelationId = "new-request-correlation" }, metadata, reason);
        Assert.Equal(proof, replay);
        if (!approve) Assert.Null(Restore(row, result, context, metadata, reason!.Trim())); // the retained hash used the original untrimmed body
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Native_clock_rollback_keeps_original_decision_timestamp_identity_without_an_added_chronology_rule(bool approve)
    {
        var (row, result, context, metadata, reason) = Fixture(approve);
        result = result with { Resource = result.Resource! with { ProposedAtUtc = At.AddMinutes(1) } };
        row.ResultJson = JsonSerializer.Serialize(result, Json);
        var proof = Restore(row, result, context, metadata, reason);
        Assert.NotNull(proof); Assert.Equal(At, proof.RecordedAtUtc); Assert.Equal("2", proof.ResultVersion);
        Assert.Equal(At, result.Resource!.DecidedAtUtc); Assert.True(result.Resource.ProposedAtUtc > proof.RecordedAtUtc);
        row.RecordedAtUtc = At.AddSeconds(1);
        Assert.Null(Restore(row, result, context, metadata, reason));
    }

    [Theory]
    [InlineData("company")] [InlineData("proposal")] [InlineData("command")] [InlineData("actor")]
    [InlineData("key")] [InlineData("hash")] [InlineData("correlation")] [InlineData("time")]
    [InlineData("version")] [InlineData("result-resource")] [InlineData("maker")] [InlineData("extra-json")]
    [InlineData("missing-json")] [InlineData("replayed-json")]
    public void Missing_forged_or_misbound_native_command_is_not_decision_proof(string failure)
    {
        var (row, result, context, metadata, reason) = Fixture(true);
        switch (failure)
        {
            case "company": row.CompanyId = Guid.NewGuid(); break;
            case "proposal": row.ReversalProposalId = Guid.NewGuid(); break;
            case "command": row.CommandName = ReversalProposalService.RejectCommand; break;
            case "actor": row.ActorId = "other"; break;
            case "key": row.IdempotencyKeyHash = new string('a', 64); break;
            case "hash": row.PayloadHash = new string('b', 64); break;
            case "correlation": row.CorrelationId = "other"; break;
            case "time": row.RecordedAtUtc = At.AddMinutes(1); break;
            case "version": result = result with { Resource = result.Resource! with { Version = "3" } }; row.ResultJson = JsonSerializer.Serialize(result, Json); break;
            case "result-resource": result = result with { Resource = result.Resource! with { OriginalSourceId = Guid.NewGuid() } }; break;
            case "maker": result = result with { Resource = result.Resource! with { ProposedBy = "checker" } }; row.ResultJson = JsonSerializer.Serialize(result, Json); break;
            case "extra-json": var extra = JsonNode.Parse(row.ResultJson)!.AsObject(); extra["decisionReceipt"] = new JsonObject(); row.ResultJson = extra.ToJsonString(); break;
            case "missing-json": var missing = JsonNode.Parse(row.ResultJson)!.AsObject(); missing.Remove("issues"); row.ResultJson = missing.ToJsonString(); break;
            case "replayed-json": row.ResultJson = JsonSerializer.Serialize(result with { IsReplay = true }, Json); break;
        }
        Assert.Null(Restore(row, result, context, metadata, reason));
    }

    [Fact]
    public void Native_current_result_cannot_replace_an_old_key_immutable_receipt()
    {
        var (row, old, context, metadata, reason) = Fixture(true);
        var different = old with { Resource = old.Resource! with { Id = Guid.NewGuid(), Reason = "later request" } };
        Assert.Null(Restore(row, different, context, metadata, reason));
        Assert.NotNull(Restore(row, old with { IsReplay = true }, context with { CorrelationId = "later" }, metadata, reason));
    }

    [Fact]
    public void Missing_retained_original_facts_cannot_be_replaced_with_current_proposal_data()
    {
        var (row, result, context, metadata, reason) = Fixture(true);
        Assert.Null(ReversalProposalService.RestoreDecisionReceipt(row, context, metadata, result.Resource!.Id,
            "1", reason, ReversalProposalService.ApproveCommand, result, null));
    }

    private static ReceiptReversalDecisionReceiptV1? Restore(FinanceCommandRow row, ReversalProposalCommandResult result,
        FinanceContext context, CommandMetadata metadata, string? reason) => ReversalProposalService.RestoreDecisionReceipt(row, context,
            metadata, result.Resource!.Id, "1", reason, reason is null ? ReversalProposalService.ApproveCommand : ReversalProposalService.RejectCommand, result, Original());
    private static ReceiptReversalOriginalReceiptV1 Original() => new(SourceId, "2", null, Company, Company, Company, Company,
        "1250.50", new(2026, 11, 26), null, null, null, null, Company, "recorder", At.AddDays(-1));
    private static (FinanceCommandRow Row, ReversalProposalCommandResult Result, FinanceContext Context, CommandMetadata Metadata, string? Reason) Fixture(bool approve)
    {
        var id = Guid.NewGuid(); var context = new FinanceContext(Company, "checker", "original-correlation", null); var metadata = new CommandMetadata("original-key");
        var reason = approve ? null : "  original reason  "; var command = approve ? ReversalProposalService.ApproveCommand : ReversalProposalService.RejectCommand;
        var view = new ReversalProposalView(id, SourceId, new DateOnly(2026, 11, 27), "proposal reason",
            approve ? ReversalProposalStatus.Approved : ReversalProposalStatus.Rejected, "maker", At.AddDays(-1), "checker", At,
            reason?.Trim(), approve ? Guid.NewGuid() : null, "2");
        var result = new ReversalProposalCommandResult(CommandOutcome.Succeeded, view, false, context.CorrelationId, []);
        var row = new FinanceCommandRow { CompanyId = Company, ReversalProposalId = id, CommandName = command,
            ActorId = context.ActorId, IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            PayloadHash = approve ? ReversalProposalService.PayloadHash(command, new ApproveReversalRequest(id, "1"))
                : ReversalProposalService.PayloadHash(command, new RejectReversalRequest(id, "1", reason!)),
            CorrelationId = context.CorrelationId, RecordedAtUtc = At, ResultJson = JsonSerializer.Serialize(result, Json) };
        return (row, result, context, metadata, reason);
    }
}
