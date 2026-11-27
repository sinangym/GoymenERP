using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceBankAccountIbanReceiptTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Account = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-27T07:00:00Z");
    private const string OldIban = "TR330006100519786457841326", NewIban = "TR060006100519786457841327", Reason = "SENTETIK private reason";

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Private_receipt_preserves_the_native_public_wire_and_binds_original_proposal_or_approval(bool approval)
    {
        var command = Command(approval); var result = Result(approval ? "4" : "3");
        var proof = Proof(approval); command.ResultJson = FinanceBankAccountService.SerializeIbanChangeReceipt(result, proof);
        Assert.Equal(proof, FinanceBankAccountService.ReadIbanChangeReceiptProof(command));
        Assert.Equal(JsonSerializer.Serialize(result, FinanceJson.CreateOptions()),
            JsonSerializer.Serialize(FinanceBankAccountService.RestoreBankCommandResult(command), FinanceJson.CreateOptions()));
        foreach (var secret in new[] { OldIban, NewIban, Reason }) Assert.DoesNotContain(secret, command.ResultJson, StringComparison.Ordinal);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(result, FinanceJson.CreateOptions()));
        Assert.False(wire.RootElement.TryGetProperty("ibanChangeProof", out _));
        Assert.False(wire.RootElement.TryGetProperty("schemaVersion", out _));
    }

    [Theory]
    [InlineData(CommandOutcome.Succeeded)] [InlineData(CommandOutcome.VersionConflict)]
    public void Legacy_bare_success_and_terminal_results_replay_without_fabricating_new_proof(CommandOutcome outcome)
    {
        var original = Result("4") with { Outcome = outcome, Resource = outcome == CommandOutcome.Succeeded ? Result("4").Resource : null };
        var command = Command(true); command.ResultJson = JsonSerializer.Serialize(original, FinanceJson.CreateOptions());
        Assert.Null(FinanceBankAccountService.ReadIbanChangeReceiptProof(command));
        Assert.Equal(command.ResultJson, JsonSerializer.Serialize(FinanceBankAccountService.RestoreBankCommandResult(command), FinanceJson.CreateOptions()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Private_receipt_binding_does_not_add_an_active_account_precondition_to_the_native_iban_decision(bool approval)
    {
        var command = Command(approval); var original = Result(approval ? "4" : "3");
        var result = original with { Resource = original.Resource! with { IsActive = false } };
        var proof = Proof(approval);
        command.ResultJson = FinanceBankAccountService.SerializeIbanChangeReceipt(result, proof);
        Assert.Equal(proof, FinanceBankAccountService.ReadIbanChangeReceiptProof(command));
        var restored = FinanceBankAccountService.RestoreBankCommandResult(command);
        Assert.False(restored.Resource!.IsActive);
        Assert.Equal(JsonSerializer.Serialize(result, FinanceJson.CreateOptions()), JsonSerializer.Serialize(restored, FinanceJson.CreateOptions()));
    }

    [Fact]
    public void Facts_digest_normalizes_iban_only_and_binds_old_new_and_exact_reason_without_retaining_them()
    {
        var digest = FinanceBankAccountService.IbanChangeFactsDigest(OldIban, NewIban, Reason);
        Assert.Equal(digest, FinanceBankAccountService.IbanChangeFactsDigest(OldIban.ToLowerInvariant(), NewIban.ToLowerInvariant(), Reason));
        Assert.NotEqual(digest, FinanceBankAccountService.IbanChangeFactsDigest(null, NewIban, Reason));
        Assert.NotEqual(digest, FinanceBankAccountService.IbanChangeFactsDigest(NewIban, OldIban, Reason));
        Assert.NotEqual(digest, FinanceBankAccountService.IbanChangeFactsDigest(OldIban, NewIban, Reason + " changed"));
    }

    [Theory]
    [InlineData("schema")] [InlineData("missing-proof")] [InlineData("missing-time")] [InlineData("extra")]
    [InlineData("company")] [InlineData("account")] [InlineData("hash")] [InlineData("version")]
    [InlineData("maker")] [InlineData("correlation")]
    public void Malformed_or_unbound_private_approval_is_refused_instead_of_reconstructed_from_a_current_card(string fault)
    {
        var command = Command(true); var json = JsonNode.Parse(FinanceBankAccountService.SerializeIbanChangeReceipt(Result("4"), Proof(true)))!.AsObject();
        var proof = json["ibanChangeProof"]!.AsObject();
        switch (fault)
        {
            case "schema": json["schemaVersion"] = 99; break;
            case "missing-proof": json.Remove("ibanChangeProof"); break;
            case "missing-time": proof.Remove("changeProposedAtUtc"); break;
            case "extra": proof["iban"] = NewIban; break;
            case "company": command.CompanyId = Guid.NewGuid(); break;
            case "account": command.BankAccountId = Guid.NewGuid(); break;
            case "hash": command.PayloadHash = new string('b', 64); break;
            case "version": proof["approvalVersion"] = 5; break;
            case "maker": proof["changeProposedBy"] = command.ActorId; break;
            case "correlation": command.CorrelationId = "SENTETIK-other"; break;
        }
        command.ResultJson = json.ToJsonString();
        Assert.Throws<InvalidOperationException>(() => FinanceBankAccountService.RestoreBankCommandResult(command));
    }

    [Fact]
    public void Proposal_maker_and_time_must_match_the_immutable_command_and_native_minimum_time_is_not_a_new_policy()
    {
        var command = Command(false); var proof = Proof(false) with { ChangeProposedAtUtc = DateTimeOffset.MinValue };
        command.RecordedAtUtc = DateTimeOffset.MinValue;
        command.ResultJson = FinanceBankAccountService.SerializeIbanChangeReceipt(Result("3"), proof);
        Assert.Equal(DateTimeOffset.MinValue, FinanceBankAccountService.ReadIbanChangeReceiptProof(command)!.ChangeProposedAtUtc);
        command.ActorId = "SENTETIK-other-maker";
        Assert.Throws<InvalidOperationException>(() => FinanceBankAccountService.ReadIbanChangeReceiptProof(command));
    }

    private static BankAccountCommandResult Result(string version) => new(CommandOutcome.Succeeded,
        new(Account, Company, Guid.Parse("30000000-0000-0000-0000-000000000001"), "SENTETIK account", true, false, version), false, "SENTETIK-original", []);
    private static FinanceBankAccountService.IbanChangeReceiptProof Proof(bool approval) => new(
        approval ? FinanceBankAccountService.ApproveIbanChangeCommand : FinanceBankAccountService.ProposeIbanChangeCommand,
        Company, Account, "SENTETIK-change-maker", At, 3, approval ? 4L : null, new string('a', 64),
        FinanceBankAccountService.IbanChangeFactsDigest(OldIban, NewIban, Reason));
    private static FinanceCommandRow Command(bool approval) => new()
    {
        CompanyId = Company, BankAccountId = Account,
        CommandName = approval ? FinanceBankAccountService.ApproveIbanChangeCommand : FinanceBankAccountService.ProposeIbanChangeCommand,
        ActorId = approval ? "SENTETIK-checker" : "SENTETIK-change-maker", PayloadHash = new string('a', 64),
        CorrelationId = "SENTETIK-original", RecordedAtUtc = At,
    };
}
