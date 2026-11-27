using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinancePartyCreditLimitService
{
    private async Task<PartyCreditLimitDecisionReceiptV1> ReadDecisionReceiptAsync(FinanceContext context,
        CommandMetadata metadata, Guid proposalId, string command, PartyCreditLimitCommandResult result, CancellationToken token)
    {
        await Fence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var row = await dbContext.PartyCreditLimitCommands.AsNoTracking().SingleOrDefaultAsync(record =>
            record.CompanyId == context.CompanyId && record.CommandName == command && record.IdempotencyKeyHash == key
            && record.ActorId == context.ActorId && record.PartyCreditLimitId == proposalId, token);
        await Fence();
        return row is null ? throw new InvalidOperationException("Credit limit native receipt is missing.")
            : RestoreDecisionReceipt(row, context, metadata, proposalId, command, result);
        async Task Fence()
        {
            if (await FenceAsync(context, FinanceAction.ApproveCreditLimit, proposalId, token) is { } refusal)
                throw new DecisionRefusal(refusal);
        }
    }

    internal static PartyCreditLimitDecisionReceiptV1 RestoreDecisionReceipt(PartyCreditLimitCommandRow row,
        FinanceContext context, CommandMetadata metadata, Guid proposalId, string command, PartyCreditLimitCommandResult result)
    {
        using var json = JsonDocument.Parse(row.ResultJson);
        if (!Exact(json.RootElement, "outcome", "resource", "isReplay", "originalCorrelationId", "issues")
            || !json.RootElement.TryGetProperty("resource", out var resourceJson)
            || !Exact(resourceJson, "id", "companyId", "partyId", "companyAccountId", "kind",
                "currencyId", "amount", "status", "isDirect", "revision", "rejectionReason", "proposedAtUtc", "decidedAtUtc", "supersededAtUtc", "version"))
            throw new InvalidOperationException("Credit limit native receipt shape is invalid.");
        var stored = JsonSerializer.Deserialize<PartyCreditLimitCommandResult>(row.ResultJson, Json);
        var approved = command == ApproveCommand;
        if (command is not (ApproveCommand or RejectCommand) || stored is not { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: { } resource }
            || stored.Issues is not { Count: 0 } || result.Outcome != CommandOutcome.Succeeded || result.Issues is not { Count: 0 }
            || result.Resource != resource || result.OriginalCorrelationId != stored.OriginalCorrelationId
            || row.CompanyId != context.CompanyId || row.PartyCreditLimitId != proposalId || resource.Id != proposalId
            || resource.CompanyId != context.CompanyId || resource.PartyId == Guid.Empty || resource.CompanyAccountId == Guid.Empty
            || resource.IsDirect || resource.Status != (approved ? PartyCreditLimitStatus.Approved : PartyCreditLimitStatus.Rejected)
            || resource.SupersededAtUtc is not null || resource.DecidedAtUtc is not { } decided || decided.Offset != TimeSpan.Zero
            || resource.ProposedAtUtc == default || resource.ProposedAtUtc.Offset != TimeSpan.Zero || decided < resource.ProposedAtUtc
            || !long.TryParse(resource.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 2
            || VersionText(version) != resource.Version || row.CommandName != command || row.ActorId != context.ActorId
            || row.IdempotencyKeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(row.CorrelationId) || row.CorrelationId != stored.OriginalCorrelationId
            || row.RecordedAtUtc == default || row.RecordedAtUtc.Offset != TimeSpan.Zero
            || row.RecordedAtUtc != decided || !result.IsReplay && row.CorrelationId != context.CorrelationId)
            throw new InvalidOperationException("Credit limit native receipt binding is invalid.");
        var expected = VersionText(version - 1);
        var hash = approved ? PayloadHash(command, new PartyCreditLimitApproveRequest(proposalId, expected))
            : PayloadHash(command, new PartyCreditLimitRejectRequest(proposalId, expected, resource.RejectionReason!));
        if (row.PayloadHash != hash || approved && (resource.RejectionReason is not null || !long.TryParse(resource.Revision, out var revision) || revision < 1)
            || !approved && (!IsCanonical(resource.RejectionReason) || resource.RejectionReason!.Length > ReasonMaximumLength || resource.Revision is not null)
            || resource.Kind == PartyCreditLimitKind.Set && (resource.CurrencyId is null || resource.CurrencyId == Guid.Empty || !IsExactAmount(resource.Amount))
            || resource.Kind == PartyCreditLimitKind.Remove && (resource.CurrencyId is not null || resource.Amount is not null)
            || resource.Kind is not (PartyCreditLimitKind.Set or PartyCreditLimitKind.Remove))
            throw new InvalidOperationException("Credit limit native receipt payload is invalid.");
        return new(context.CompanyId, proposalId, command, row.IdempotencyKeyHash, row.PayloadHash, row.ActorId,
            expected, resource.Version, row.CorrelationId, row.RecordedAtUtc);

        static bool Exact(JsonElement value, params string[] names) => value.ValueKind == JsonValueKind.Object
            && value.EnumerateObject().Count() == names.Length && names.All(name => value.TryGetProperty(name, out _));
    }
}
