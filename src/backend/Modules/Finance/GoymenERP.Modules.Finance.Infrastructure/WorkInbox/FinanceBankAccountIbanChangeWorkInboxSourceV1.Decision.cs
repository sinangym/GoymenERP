using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

internal sealed partial class FinanceBankAccountIbanChangeWorkInboxSourceV1
{
    private const int AmbiguousReceiptProbeCount = 2;

    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token) =>
        ReadPurposeAsync(context, itemId, null, token);

    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid itemId,
        string expectedProposalVersion, CancellationToken token) => ReadPurposeAsync(context, itemId, expectedProposalVersion, token);

    private async Task<WorkApprovalDecisionSnapshotV1?> ReadPurposeAsync(WorkInboxSourceContextV1 context, Guid itemId,
        string? selectedVersion, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (itemId == Guid.Empty || await fence.CheckAsync(context, ApproverPermission, token) is not null) return null;
        if (selectedVersion is not null && (!long.TryParse(selectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1 || parsed == long.MaxValue || parsed.ToString(CultureInfo.InvariantCulture) != selectedVersion))
            throw new InvalidOperationException("Invalid original IBAN proposal version.");
        var row = await dbContext.BankAccounts.AsNoTracking().SingleOrDefaultAsync(value => value.CompanyId == context.CompanyId && value.Id == itemId, token);
        await RequirePurposeFence(context, token);
        if (row is null) return null;
        var currentVersion = row.Version.ToString(CultureInfo.InvariantCulture);
        if ((selectedVersion is null || selectedVersion == currentVersion) && row.PendingIban is not null)
        {
            if (row.Version < 1 || Item(row, false) is not { } item || row.PendingIbanProposedAtUtc is not { } at
                || row.PendingIbanProposedBy is not { } maker || row.PendingIbanReason is not { } reason)
                throw new InvalidOperationException("Incomplete native IBAN proposal.");
            return new(context.CompanyId, item, currentVersion, true, BankAccountIbanChangeProof:
                new(context.CompanyId, row.Id, maker, at.ToUniversalTime(), currentVersion,
                    FinanceBankAccountService.IbanChangeFactsDigest(row.Iban, row.PendingIban, reason), new(row.Iban, row.PendingIban, reason)));
        }
        // Human terminal fields cannot be reversed from a digest. Only an explicit original POST version can select history.
        if (selectedVersion is null) throw new InvalidOperationException("Historical IBAN human facts are unavailable.");
        var payload = FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ApproveIbanChangeCommand,
            new BankAccountIbanChangeApproveRequest(itemId, selectedVersion));
        // Filter the exact original command identity before probing ambiguity; unrelated later versions or bare terminal
        // refusals cannot age out an old successful receipt. New genuine IBAN successes use the owner's explicit envelope.
        var commands = await dbContext.Commands.AsNoTracking().Where(value => value.CompanyId == context.CompanyId && value.BankAccountId == itemId
                && value.CommandName == FinanceBankAccountService.ApproveIbanChangeCommand && value.PayloadHash == payload && value.ActorId == context.UserId
                && value.ResultJson.StartsWith(FinanceBankAccountService.IbanReceiptEnvelopePrefix))
            .Take(AmbiguousReceiptProbeCount).ToListAsync(token);
        await RequirePurposeFence(context, token);
        if (commands.Any(command => command.CompanyId != context.CompanyId || command.BankAccountId != itemId
            || command.ActorId != context.UserId || command.PayloadHash != payload
            || command.CommandName != FinanceBankAccountService.ApproveIbanChangeCommand))
            throw new InvalidOperationException("Original IBAN command identity is not exact.");
        var selected = commands.Select(command => (Command: command, Proof: FinanceBankAccountService.ReadIbanChangeReceiptProof(command)))
            .Where(value => value.Proof?.ProposalVersion.ToString(CultureInfo.InvariantCulture) == selectedVersion).ToArray();
        if (commands.Count != 1 || selected.Length != 1)
            throw new InvalidOperationException("Original IBAN proposal proof is missing or ambiguous.");
        var proof = selected[0].Proof!;
        var historical = new WorkInboxItemV1(ModuleName, KindName, itemId, FinanceWorkInboxFence.ShortReference(itemId), proof.ChangeProposedAtUtc,
            FinanceWorkInboxFence.Link(WorkDecisionPath, itemId), proof.ChangeProposedBy, [new(ApprovalRequiredRule, null, null, null, null)],
            [FinanceWorkInboxRead.Reference("bank-account", itemId)]);
        await RequirePurposeFence(context, token);
        return new(context.CompanyId, historical, selectedVersion, false, BankAccountIbanChangeProof:
            new(context.CompanyId, itemId, proof.ChangeProposedBy, proof.ChangeProposedAtUtc, selectedVersion, proof.FactsDigest, null));
    }

    private async Task RequirePurposeFence(WorkInboxSourceContextV1 context, CancellationToken token)
    {
        if (await fence.CheckAsync(context, ApproverPermission, token) is not null)
            throw new UnauthorizedAccessException("IBAN decision source authority changed.");
    }
}
