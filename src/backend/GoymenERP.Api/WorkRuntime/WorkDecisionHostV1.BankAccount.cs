using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Authorized human GET only. The native safe view intentionally contains no bank card/IBAN/account number.</summary>
public sealed record WorkBankAccountPreviewV1(BankAccountView Account, string ProposedBy, DateTimeOffset ProposedAtUtc);

public sealed partial class WorkDecisionHostV1
{
    private static bool BankAccountVersion(string? version) => long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value > 0 && value < long.MaxValue && value.ToString(CultureInfo.InvariantCulture) == version;

    private static bool ValidBankAccountProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        var p = snapshot.BankAccountProof;
        if (kind != BankAccount) return p is null;
        return p is not null && p.CompanyId == snapshot.CompanyId && p.AccountId == snapshot.Item.ItemId && p.CurrencyId != Guid.Empty
            && !string.IsNullOrWhiteSpace(p.DisplayName) && p.DisplayName.Length <= 200 && p.ProposedBy == snapshot.Item.PreparedBy
            && p.ProposedAtUtc != default && p.ProposedAtUtc.Offset == TimeSpan.Zero && p.ProposedAtUtc == snapshot.Item.WaitingSinceUtc
            && BankAccountVersion(snapshot.SourceVersion) && p.IsActive == !snapshot.Pending && (!p.IsUsableForReceipt || p.IsActive)
            && snapshot.Item.References.Count(r => r.Role == "bank-account" && r.RecordId == p.AccountId) == 1
            && snapshot.Item.References.Count(r => r.Role == "currency" && r.RecordId == p.CurrencyId) == 1;
    }
    private static WorkBankAccountPreviewV1 BankAccountHumanPreview(WorkApprovalDecisionSnapshotV1 snapshot)
    {
        var p = snapshot.BankAccountProof!;
        return new(new(p.AccountId, p.CompanyId, p.CurrencyId, p.DisplayName, p.IsActive, p.IsUsableForReceipt, snapshot.SourceVersion), p.ProposedBy, p.ProposedAtUtc);
    }
    private async Task<WorkDecisionResultV1> DecideBankAccountAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (bankAccounts is null) throw new WorkDecisionException(503);
        var result = await bankAccounts.ApproveAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId),
            new(key), new(id, request.ExpectedVersion), token);
        // Non-success native outcomes retain their existing terminal/replay semantics; no Work success evidence is synthesized.
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var p = snapshot.BankAccountProof!;
        if (result.Resource is not { } r || result.ApprovalReceipt is not { } proof || r.CompanyId != snapshot.CompanyId || r.Id != id
            || r.CurrencyId != p.CurrencyId || r.DisplayName != p.DisplayName || !r.IsActive
            || r.Version != (long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture)
            || proof.CompanyId != r.CompanyId || proof.AccountId != id || proof.CommandName != "finance.bank-account.approve" || proof.ActorId != actor
            || proof.RecordedAtUtc == default || proof.RecordedAtUtc.Offset != TimeSpan.Zero || proof.RecordedAtUtc < p.ProposedAtUtc
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId)
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || result.OriginalCorrelationId != correlation.CorrelationId))
            throw new WorkDecisionException(503);
        // The stored version/eligibility belongs to the original command, not a later IBAN/card revision.
        return new(snapshot.CompanyId, BankAccount, id, "Approved", result.IsReplay, result.OriginalCorrelationId);
    }
}
