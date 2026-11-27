using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>FNS-D128 authorized decision GET only; not an inbox/journal/AI/export or a general bank-account read.</summary>
public sealed record WorkBankAccountIbanChangePreviewV1(Guid AccountId, Guid CompanyId, string CompanyLegalName,
    string? CurrentIban, string ProposedIban, string Reason, string ProposedBy, DateTimeOffset ProposedAtUtc);

public sealed partial class WorkDecisionHostV1
{
    private static bool ValidBankIbanProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        var proof = snapshot.BankAccountIbanChangeProof;
        if (kind != BankAccountIbanChange) return proof is null;
        if (proof is null || proof.CompanyId != snapshot.CompanyId || proof.AccountId != snapshot.Item.ItemId
            || proof.ProposedBy != snapshot.Item.PreparedBy || proof.ProposedAtUtc != snapshot.Item.WaitingSinceUtc
            || proof.ProposedAtUtc.Offset != TimeSpan.Zero || !BankAccountVersion(proof.ProposalVersion)
            || proof.ProposalVersion != snapshot.SourceVersion || proof.FactsDigest is not { Length: 64 }
            || !proof.FactsDigest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            || snapshot.Item.References.Count(reference => reference.Role == "bank-account" && reference.RecordId == proof.AccountId) != 1)
            return false;
        if (proof.HumanFacts is not { } facts) return !snapshot.Pending;
        return snapshot.Pending && facts.ProposedIban == FinanceIban.Normalize(facts.ProposedIban)
            && FinanceIban.IsStructurallyValid(facts.ProposedIban) && !string.IsNullOrWhiteSpace(facts.Reason)
            && facts.Reason.Trim() == facts.Reason && facts.Reason.Length <= 500;
    }

    private async Task<WorkBankAccountIbanChangePreviewV1> BankIbanHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (bankAccountCompanies is null || snapshot.BankAccountIbanChangeProof?.HumanFacts is not { } facts)
            throw new WorkDecisionException(503);
        await RequireDecisionAuthorityAsync(BankAccountIbanChange, ErpPermissions.FinanceBankAccountApprove, snapshot.CompanyId, actor, token);
        GoymenERP.Modules.Organization.Application.Contracts.CompanySummary[] matching;
        try { matching = (await bankAccountCompanies.GetAuthorizedCompaniesAsync(token)).Where(value => value.Id == snapshot.CompanyId).Take(2).ToArray(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await RequireDecisionAuthorityAsync(BankAccountIbanChange, ErpPermissions.FinanceBankAccountApprove, snapshot.CompanyId, actor, token); throw new WorkDecisionException(503); }
        await RequireDecisionAuthorityAsync(BankAccountIbanChange, ErpPermissions.FinanceBankAccountApprove, snapshot.CompanyId, actor, token);
        if (matching.Length != 1 || matching[0].IsPlaceholder || string.IsNullOrWhiteSpace(matching[0].LegalName)) throw new WorkDecisionException(503);
        var proof = snapshot.BankAccountIbanChangeProof!;
        return new(proof.AccountId, proof.CompanyId, matching[0].LegalName, facts.CurrentIban, facts.ProposedIban,
            facts.Reason, proof.ProposedBy, proof.ProposedAtUtc);
    }

    private async Task<WorkDecisionResultV1> DecideBankIbanAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (bankAccounts is null) throw new WorkDecisionException(503);
        var result = await bankAccounts.ApproveIbanChangeAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId),
            new(key), new(id, request.ExpectedVersion), token);
        await RequireDecisionAuthorityAsync(BankAccountIbanChange, ErpPermissions.FinanceBankAccountApprove, snapshot.CompanyId, actor, token);
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Issues is null) throw new WorkDecisionException(503);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var source = snapshot.BankAccountIbanChangeProof!;
        var next = (long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
        if (result.Resource is not { } resource || result.IbanApprovalReceipt is not { } receipt
            || resource.Id != id || resource.CompanyId != snapshot.CompanyId || resource.Version != next
            || receipt.CompanyId != resource.CompanyId || receipt.AccountId != id || receipt.CommandName != "finance.bank-account.iban-change.approve"
            || receipt.ActorId != actor || receipt.RecordedAtUtc.Offset != TimeSpan.Zero || receipt.ProposalVersion != request.ExpectedVersion
            || receipt.ApprovalVersion != next || receipt.ChangeProposedBy != source.ProposedBy || receipt.ChangeProposedAtUtc != source.ProposedAtUtc
            || receipt.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key)
            || receipt.FactsDigest != source.FactsDigest || string.IsNullOrWhiteSpace(result.OriginalCorrelationId)
            || !result.IsReplay && (!snapshot.Pending || result.OriginalCorrelationId != correlation.CorrelationId))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, BankAccountIbanChange, id, "Approved", result.IsReplay, result.OriginalCorrelationId);
    }
}
