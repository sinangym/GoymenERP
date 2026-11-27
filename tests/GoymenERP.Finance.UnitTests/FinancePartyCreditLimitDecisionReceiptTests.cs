using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinancePartyCreditLimitDecisionReceiptTests
{
    private static readonly Guid Company = Guid.NewGuid(), Id = Guid.NewGuid(), Party = Guid.NewGuid(), Account = Guid.NewGuid(), Currency = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly FinanceContext Context = new(Company, "SENTETIK-checker", "SENTETIK-original", null);
    private static readonly CommandMetadata Key = new("SENTETIK-key");
    private static (PartyCreditLimitCommandRow Row, PartyCreditLimitCommandResult Result) Native(bool approve)
    {
        var command = approve ? FinancePartyCreditLimitService.ApproveCommand : FinancePartyCreditLimitService.RejectCommand;
        var result = new PartyCreditLimitCommandResult(CommandOutcome.Succeeded,
            new(Id, Company, Party, Account, PartyCreditLimitKind.Set, Currency, "00123.4500",
                approve ? PartyCreditLimitStatus.Approved : PartyCreditLimitStatus.Rejected, false, approve ? "1" : null,
                approve ? null : "SENTETIK reason", At, At.AddHours(1), null, "2"), false, Context.CorrelationId, []);
        var hash = approve ? FinancePartyCreditLimitService.PayloadHash(command, new PartyCreditLimitApproveRequest(Id, "1"))
            : FinancePartyCreditLimitService.PayloadHash(command, new PartyCreditLimitRejectRequest(Id, "1", "SENTETIK reason"));
        return (new() { CompanyId = Company, CommandName = command, IdempotencyKeyHash = FinanceCommandKeys.Hash(Key.IdempotencyKey),
            ActorId = Context.ActorId, PayloadHash = hash, PartyCreditLimitId = Id, ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()),
            CorrelationId = Context.CorrelationId, RecordedAtUtc = At.AddHours(1) }, result);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Exact_native_result_json_binds_the_original_version_and_has_no_new_wire_or_hash_fields(bool approve)
    {
        var (row, result) = Native(approve); var original = row.ResultJson;
        var receipt = FinancePartyCreditLimitService.RestoreDecisionReceipt(row, Context, Key, Id, row.CommandName, result);
        Assert.Equal((Company, Id, "1", "2", Context.ActorId, Context.CorrelationId),
            (receipt.CompanyId, receipt.ProposalId, receipt.ExpectedVersion, receipt.ResultVersion, receipt.ActorId, receipt.OriginalCorrelationId));
        Assert.Equal(original, JsonSerializer.Serialize(result with { DecisionReceipt = receipt }, FinanceJson.CreateOptions()));
        var replay = FinancePartyCreditLimitService.RestoreDecisionReceipt(row, Context with { CorrelationId = "SENTETIK-new" }, Key,
            Id, row.CommandName, result with { IsReplay = true });
        Assert.Equal(receipt, replay); Assert.Equal(original, row.ResultJson);
    }

    [Theory]
    [InlineData("company")] [InlineData("proposal")] [InlineData("command")] [InlineData("key")] [InlineData("actor")]
    [InlineData("hash")] [InlineData("json")] [InlineData("resource")] [InlineData("correlation")] [InlineData("replay")]
    public void No_current_record_or_forged_stored_result_is_an_original_receipt(string fault)
    {
        var (row, result) = Native(true);
        switch (fault)
        {
            case "company": row.CompanyId = Guid.NewGuid(); break;
            case "proposal": row.PartyCreditLimitId = Guid.NewGuid(); break;
            case "command": row.CommandName = FinancePartyCreditLimitService.SetCommand; break;
            case "key": row.IdempotencyKeyHash = "forged"; break;
            case "actor": row.ActorId = "SENTETIK-other"; break;
            case "hash": row.PayloadHash = "forged"; break;
            case "json": row.ResultJson = "{}"; break;
            case "resource": result = result with { Resource = result.Resource! with { Version = "3", Status = PartyCreditLimitStatus.Superseded } }; break;
            case "correlation": row.CorrelationId = "forged"; break;
            case "replay": row.ResultJson = JsonSerializer.Serialize(result with { IsReplay = true }, FinanceJson.CreateOptions()); break;
        }
        Assert.Throws<InvalidOperationException>(() => FinancePartyCreditLimitService.RestoreDecisionReceipt(row, Context, Key, Id,
            FinancePartyCreditLimitService.ApproveCommand, result));
    }
}
