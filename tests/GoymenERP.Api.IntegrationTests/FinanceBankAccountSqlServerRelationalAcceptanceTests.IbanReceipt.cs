using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceBankAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Iban_native_private_receipts_retain_original_change_maker_time_versions_and_digest_after_pending_is_replaced_or_cleared()
    {
        await using var run = await Run.StartAsync(); var id = await IbanSafetyPending(run);
        await using var reader = new BankSafetyScope(run);
        var proposalCommand = await reader.Database.Commands.AsNoTracking().SingleAsync(row => row.BankAccountId == id
            && row.CommandName == FinanceBankAccountService.ProposeIbanChangeCommand);
        var originalProof = FinanceBankAccountService.ReadIbanChangeReceiptProof(proposalCommand);
        Assert.NotNull(originalProof); Assert.Equal(Proposer, originalProof.ChangeProposedBy);
        Assert.Equal(3, originalProof.ProposalVersion); Assert.Null(originalProof.ApprovalVersion);
        Assert.Equal(FinanceBankAccountService.IbanChangeFactsDigest(null, IbanSafetyValue, "SENTETIK immutable IBAN reason"), originalProof.FactsDigest);
        Assert.DoesNotContain(IbanSafetyValue, proposalCommand.ResultJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTETIK immutable IBAN reason", proposalCommand.ResultJson, StringComparison.Ordinal);

        const string replacementReason = "SENTETIK replacement proof reason";
        BankAccountCommandResult replacement;
        await using (var proposer = run.Open(CompanyA, Approver))
        {
            var request = new BankAccountIbanChangeProposeRequest(id, "3", IbanSafetyValue, replacementReason);
            replacement = await proposer.Accounts.ProposeIbanChangeAsync(proposer.Context, Key("iban-proof-replacement"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, replacement.Outcome);
            var command = await reader.Database.Commands.AsNoTracking().SingleAsync(row => row.BankAccountId == id
                && row.CommandName == FinanceBankAccountService.ProposeIbanChangeCommand && row.ActorId == Approver);
            Assert.Equal(FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ProposeIbanChangeCommand, request), command.PayloadHash);
            var proof = FinanceBankAccountService.ReadIbanChangeReceiptProof(command)!;
            Assert.Equal(Approver, proof.ChangeProposedBy); Assert.Equal(4, proof.ProposalVersion);
            Assert.Equal(command.RecordedAtUtc, proof.ChangeProposedAtUtc);
        }
        await using (var originalMaker = run.Open(CompanyA, Proposer))
        {
            var replay = await originalMaker.Accounts.ProposeIbanChangeAsync(originalMaker.Context, Key("iban-request-" + id),
                new(id, "2", IbanSafetyValue, "SENTETIK immutable IBAN reason"), default);
            Assert.True(replay.IsReplay); Assert.Equal("3", replay.Resource!.Version);
        }
        reader.User.Actor = Proposer; // Change maker is Approver; the original bank-card maker is not a substitute.
        var approved = await IbanSafetyApprove(reader, id, "iban-proof-approve", "4");
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        var approvedCommand = await reader.Database.Commands.AsNoTracking().SingleAsync(row => row.BankAccountId == id
            && row.CommandName == FinanceBankAccountService.ApproveIbanChangeCommand);
        Assert.Equal(FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ApproveIbanChangeCommand,
            new BankAccountIbanChangeApproveRequest(id, "4")), approvedCommand.PayloadHash);
        var approvedProof = FinanceBankAccountService.ReadIbanChangeReceiptProof(approvedCommand)!;
        Assert.Equal(Approver, approvedProof.ChangeProposedBy); Assert.Equal(4, approvedProof.ProposalVersion); Assert.Equal(5L, approvedProof.ApprovalVersion);
        Assert.Equal(FinanceBankAccountService.IbanChangeFactsDigest(null, IbanSafetyValue, replacementReason), approvedProof.FactsDigest);
        Assert.Equal(originalProof, FinanceBankAccountService.ReadIbanChangeReceiptProof(proposalCommand));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankAccounts WHERE Id=@id AND PendingIban IS NULL AND PendingIbanProposedBy IS NULL", ("@id", id)));
        Assert.DoesNotContain(IbanSafetyValue, approvedCommand.ResultJson, StringComparison.Ordinal);
        Assert.DoesNotContain(replacementReason, approvedCommand.ResultJson, StringComparison.Ordinal);
        var publicJson = JsonSerializer.Serialize(approved, FinanceJson.CreateOptions());
        Assert.DoesNotContain("ibanChangeProof", publicJson, StringComparison.Ordinal); Assert.DoesNotContain(IbanSafetyValue, publicJson, StringComparison.Ordinal);
        await using (var later = run.Open(CompanyA, Approver))
            Assert.Equal(CommandOutcome.Succeeded, (await later.Accounts.ProposeIbanChangeAsync(later.Context, Key("iban-proof-later"),
                new(id, "5", "TR330006100519786457841326", "SENTETIK later"), default)).Outcome);
        var originalReplay = await IbanSafetyApprove(reader, id, "iban-proof-approve", "4");
        Assert.True(originalReplay.IsReplay); Assert.Equal(approved.Resource, originalReplay.Resource);
        Assert.Equal(approved.OriginalCorrelationId, originalReplay.OriginalCorrelationId);
        Assert.Equal(approvedProof, FinanceBankAccountService.ReadIbanChangeReceiptProof(approvedCommand));
    }
}
