using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class CashExchangeBookValueProofTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Original_decision_receipt_reuses_the_native_five_field_result_hash_and_retained_transfer_proposal_decision(bool approve)
    {
        var f = new BookReceiptFixture(approve); var proof = f.Restore(); Assert.NotNull(proof);
        Assert.Equal(f.Proposal.TransferSourceHash, f.Result.Resource!.TransferSourceHash);
        Assert.Equal((Company, f.Proposal.Id, f.Proposal.TransferId, "1", "2", f.Context.ActorId),
            (proof.CompanyId, proof.ProposalId, proof.TransferId, proof.ExpectedVersion, proof.ResultVersion, proof.ActorId));
        Assert.Equal(f.Context.CorrelationId, proof.OriginalCorrelationId); Assert.Equal(f.Decision.DecidedAtUtc, proof.RecordedAtUtc);
        var receiptResult = f.Result with { DecisionReceipt = proof };
        var (outcome, resource, replay, correlation, code) = receiptResult; // Original positional contract remains five fields.
        Assert.Equal((f.Result.Outcome, f.Result.Resource, false, f.Context.CorrelationId, (string?)null), (outcome, resource, replay, correlation, code));
        Assert.Equal(f.Command.ResultJson, JsonSerializer.Serialize(receiptResult, Json));
        Assert.Equal(proof, f.Restore(f.Result with { IsReplay = true }));
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(receiptResult, Json));
        Assert.Equal(5, native.RootElement.EnumerateObject().Count()); Assert.False(native.RootElement.TryGetProperty("decisionReceipt", out _));
        var detail = new CashExchangeBookValueDecisionDetailV1(Source(), f.Result.Resource);
        var p = new CashExchangeBookValueDecisionProofV1(Company, f.Proposal.Id, f.Proposal.TransferId, f.Proposal.PreparedBy, f.Proposal.PreparedAtUtc,
            f.Result.Resource.Status.ToString(), "2", f.Proposal.TransferSourceHash, proof.ProposalFactsDigest, CashExchangeBookValueServiceV1.DecisionPreviewDigest(detail));
        using var human = JsonDocument.Parse(JsonSerializer.Serialize(detail with { DecisionProof = p }, Json));
        Assert.Equal(2, human.RootElement.EnumerateObject().Count()); Assert.False(human.RootElement.TryGetProperty("decisionProof", out _));
    }

    [Theory]
    [InlineData("company")] [InlineData("actor")] [InlineData("key")] [InlineData("hash")] [InlineData("command")] [InlineData("extra-json")]
    [InlineData("result-version")] [InlineData("correlation")] [InlineData("source")] [InlineData("proposal")] [InlineData("maker")]
    [InlineData("decision-reason")] [InlineData("decision-time")]
    public void No_current_projection_or_forged_native_provenance_can_supply_an_original_decision_receipt(string fault)
    {
        var f = new BookReceiptFixture(true);
        switch (fault)
        {
            case "company": f.Command.CompanyId = Guid.NewGuid(); break; case "actor": f.Command.ActorId = "other"; break;
            case "key": f.Command.KeyHash = "wrong"; break; case "hash": f.Command.PayloadHash = "wrong"; break;
            case "command": f.Command.Command = CashExchangeBookValueServiceV1.ProposeCommand; break;
            case "extra-json": f.Command.ResultJson = f.Command.ResultJson[..^1] + ",\"extra\":true}"; break;
            case "result-version": f.Result = f.Result with { Resource = f.Result.Resource! with { Version = "3" } }; break;
            case "correlation": f.Result = f.Result with { OriginalCorrelationId = "other" }; break;
            case "source": f.Transfer = f.Transfer with { RecordedBy = "other" }; break;
            case "proposal": f.Proposal.PreparationJson = "{}"; break; case "maker": f.Proposal.PreparedBy = f.Context.ActorId; break;
            case "decision-reason": f.Decision.Reason = "other"; break; case "decision-time": f.Decision.DecidedAtUtc = At.AddDays(1); break;
        }
        Assert.Null(f.Restore());
    }

    private sealed class BookReceiptFixture
    {
        public readonly FinanceContext Context = new(Company, "SENTETIK-accountant-checker", "SENTETIK-original", null);
        public readonly CommandMetadata Metadata = new("SENTETIK-decision-key"); public readonly DecideCashExchangeBookValueV1 Request;
        public readonly CashExchangeBookValueProposalRow Proposal = CashExchangeBookValueProofTests.Proposal();
        public readonly CashExchangeBookValueDecisionRow Decision; public readonly CashExchangeBookValueCommandRow Command;
        public CashTransferView Transfer = Source(); public CashExchangeBookValueCommandResultV1 Result;
        public BookReceiptFixture(bool approve)
        {
            Decision = CashExchangeBookValueProofTests.Decision(Proposal); Decision.Approved = approve; Decision.Reason = approve ? null : "SENTETIK ret";
            Request = new(Proposal.Id, "1", approve, Decision.Reason);
            Result = new(CommandOutcome.Succeeded, CashExchangeBookValueServiceV1.View(Proposal, Decision), false, Context.CorrelationId, null);
            Command = new() { CompanyId = Company, Command = CashExchangeBookValueServiceV1.DecideCommand, KeyHash = FinanceCommandKeys.Hash(Metadata.IdempotencyKey),
                ActorId = Context.ActorId, PayloadHash = CashExchangeBookValueServiceV1.DecidePayloadHash(Request), ResultJson = JsonSerializer.Serialize(Result, Json) };
        }
        public CashExchangeBookValueDecisionReceiptV1? Restore(CashExchangeBookValueCommandResultV1? result = null) => CashExchangeBookValueServiceV1.RestoreDecisionReceipt(Command, Proposal, Decision, Transfer, Context, Metadata, Request, result ?? Result);
    }
}
