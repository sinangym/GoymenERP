using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class ReversalProposalService
{
    private async Task<ReceiptReversalDecisionReceiptV1?> ReadDecisionReceiptAsync(FinanceContext context,
        CommandMetadata metadata, Guid proposalId, string expectedVersion, string? reason, string command,
        ReversalProposalCommandResult result, CancellationToken token)
    {
        await Fence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var row = await dbContext.Commands.AsNoTracking().SingleOrDefaultAsync(record => record.CompanyId == context.CompanyId
            && record.ReversalProposalId == proposalId && record.CommandName == command && record.IdempotencyKeyHash == key
            && record.ActorId == context.ActorId, token);
        await Fence();
        // Both original tables are retained immutable owner facts. Never use the current proposal or bank metadata.
        var sourceId = row is null ? Guid.Empty : JsonSerializer.Deserialize<ReversalProposalCommandResult>(row.ResultJson, Json)?.Resource?.OriginalSourceId ?? Guid.Empty;
        var original = sourceId == Guid.Empty ? null : await ReadOriginalReceiptAsync(context.CompanyId, sourceId, token);
        await Fence();
        // Native legacy replay stays compatible. Missing or malformed provenance is never Work success proof.
        return row is null ? null : RestoreDecisionReceipt(row, context, metadata, proposalId, expectedVersion, reason, command, result, original);
        async Task Fence()
        {
            if (await FenceAsync(context, FinanceAction.ApproveReversal, proposalId, token) is { } refusal)
                throw new ReceiptDecisionRefusal(refusal);
        }
    }

    internal static ReceiptReversalDecisionReceiptV1? RestoreDecisionReceipt(FinanceCommandRow row, FinanceContext context,
        CommandMetadata metadata, Guid proposalId, string expectedVersion, string? reason, string command,
        ReversalProposalCommandResult result, ReceiptReversalOriginalReceiptV1? original)
    {
        try
        {
            using var json = JsonDocument.Parse(row.ResultJson);
            if (!Exact(json.RootElement, "outcome", "resource", "isReplay", "originalCorrelationId", "issues")
                || !json.RootElement.TryGetProperty("resource", out var resource)
                || !Exact(resource, "id", "originalSourceId", "occurredOn", "reason", "status", "proposedBy", "proposedAtUtc",
                    "decidedBy", "decidedAtUtc", "decisionReason", "reversalSourceId", "version")) return null;
            var stored = JsonSerializer.Deserialize<ReversalProposalCommandResult>(row.ResultJson, Json);
            if (stored is not { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: { } view }
                || stored.Issues is not { Count: 0 } || result.Outcome != CommandOutcome.Succeeded || result.Issues is not { Count: 0 }
                || !JsonNode.DeepEquals(JsonNode.Parse(row.ResultJson), JsonSerializer.SerializeToNode(stored, Json))
                || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(stored, Json), JsonSerializer.SerializeToNode(result with { IsReplay = false, DecisionReceipt = null }, Json))
                || row.CompanyId == Guid.Empty || row.CompanyId != context.CompanyId || proposalId == Guid.Empty
                || row.ReversalProposalId != proposalId || view.Id != proposalId || view.OriginalSourceId == Guid.Empty
                || original is null || original.SourceId != view.OriginalSourceId
                || row.CommandName != command || command is not (ApproveCommand or RejectCommand)
                || row.ActorId != context.ActorId || !ReceiptIdentity(row.ActorId) || view.DecidedBy != row.ActorId
                || !ReceiptIdentity(view.ProposedBy) || view.ProposedBy == row.ActorId
                || row.IdempotencyKeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey)
                || !ReceiptIdentity(row.CorrelationId) || row.CorrelationId != stored.OriginalCorrelationId
                || !result.IsReplay && row.CorrelationId != context.CorrelationId
                || row.RecordedAtUtc.Offset != TimeSpan.Zero || view.DecidedAtUtc != row.RecordedAtUtc
                || view.ProposedAtUtc.Offset != TimeSpan.Zero
                || !CanonicalReason(view.Reason) || !Version(expectedVersion, out var expected) || expected == long.MaxValue
                || view.Version != (expected + 1).ToString(CultureInfo.InvariantCulture)) return null;
            var approve = command == ApproveCommand;
            var hash = approve ? PayloadHash(command, new ApproveReversalRequest(proposalId, expectedVersion))
                : PayloadHash(command, new RejectReversalRequest(proposalId, expectedVersion, reason!));
            if (row.PayloadHash != hash || approve && (reason is not null || view.Status != ReversalProposalStatus.Approved
                    || view.DecisionReason is not null || view.ReversalSourceId is null || view.ReversalSourceId == Guid.Empty
                    || view.ReversalSourceId == view.OriginalSourceId)
                || !approve && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000
                    || view.Status != ReversalProposalStatus.Rejected || view.DecisionReason != reason.Trim()
                    || view.ReversalSourceId is not null)) return null;
            return new(context.CompanyId, proposalId, view.OriginalSourceId, command, row.IdempotencyKeyHash, row.PayloadHash,
                row.ActorId, expectedVersion, view.Version, row.CorrelationId, row.RecordedAtUtc, RecordedFactsDigest(context.CompanyId, original));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        { return null; }
    }

    private static bool Exact(JsonElement value, params string[] names) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().Count() == names.Length && names.All(name => value.TryGetProperty(name, out _));
    private static bool ReceiptIdentity(string? value) => Canonical(value) && value!.Length <= IdentityMaximumLength;
    private static bool CanonicalReason(string? value) => Canonical(value) && value!.Length <= 1000;
    private static bool Version(string? text, out long value) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value > 0 && value.ToString(CultureInfo.InvariantCulture) == text;
}
