using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore.Storage;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal sealed partial class IdentityAccessAssignmentService
{
    public async Task<IdentityAccessGrantDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid requestId, bool approve,
        string expectedVersion, string? reasonCode, IdentityAccessCommandMetadataV1 context,
        Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumerFence);
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        if (!TryParseVersion(expectedVersion, MinimumExpectedVersion, out var expected) || expected == long.MaxValue
            || (approve ? reasonCode is not null : !AccessGrantRequest.IsCanonicalReasonCode(reasonCode!))
            || string.IsNullOrWhiteSpace(context.IdempotencyKey)) return null;
        var name = approve ? IdentityAccessCommandsV1.Approve : IdentityAccessCommandsV1.Reject;
        object payload = approve ? new ApproveAccessGrantRequestV1(expectedVersion) : new RejectAccessGrantRequestV1(expectedVersion, reasonCode!);
        var key = IdempotencyHash.ComputeKey(context.IdempotencyKey);
        var hash = IdempotencyHash.ComputePayload(new { Command = name, Target = TargetText(requestId), Request = payload });
        var command = await database.CommandExecutions.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == context.CompanyId && x.CommandName == name && x.IdempotencyKeyHash == key, cancellationToken);
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        if (command is null || command.ActorId != context.ActorId || command.RequestHash != hash || command.HttpStatusCode != 200) return null;
        var row = await database.AccessGrantRequests.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == context.CompanyId && x.Id == requestId, cancellationToken);
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        if (row is null || row.RequiresSecondApproval && row.IsRequesterOrTarget(Guid.Parse(context.ActorId))) return null;
        IdentityAccessCommandAnswerV1? answer;
        try
        {
            answer = JsonSerializer.Deserialize<IdentityAccessCommandAnswerV1>(command.ResponseJson, Json);
            if (answer is null || !JsonNode.DeepEquals(JsonNode.Parse(command.ResponseJson), JsonSerializer.SerializeToNode(answer, Json))) return null;
        }
        catch (JsonException) { return null; }
        var status = approve ? AccessGrantRequestStatus.Approved : AccessGrantRequestStatus.Rejected;
        if (answer.StatusCode != 200 || answer.Code is not null || answer.Result is not { } result
            || result.Id != requestId || result.Version != Text(expected + 1) || result.Status != status.ToString()
            || row.Version != expected + 1 || row.Status != status || row.DecidedByUserId != Guid.Parse(context.ActorId)
            || row.DecidedAtUtc is null || row.DecidedAtUtc != command.CompletedAtUtc || result.RoleAssignmentId != row.ResultingRoleAssignmentId
            || (!approve && row.DecisionReasonCode != reasonCode) || command.Outcome != Outcome(answer)) return null;
        if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(answer, Json), JsonSerializer.SerializeToNode(
            Answers.Ok(row.Id, row.Version, row.Status.ToString(), row.ResultingRoleAssignmentId), Json))) return null;
        if (approve)
        {
            if (row.ResultingRoleAssignmentId is null || row.ResultingRoleAssignmentId == Guid.Empty) return null;
            var assignment = await database.RoleAssignments.AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == context.CompanyId && x.Id == row.ResultingRoleAssignmentId, cancellationToken);
            await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
            if (assignment is null || assignment.UserId != row.TargetUserId || assignment.RoleId != row.RoleId
                || assignment.ApprovedByUserId != row.DecidedByUserId) return null;
            var window = EffectiveWindow(row.ValidFromUtc, row.ValidUntilUtc, row.DecidedAtUtc.Value);
            if (assignment.ValidFromUtc != window.ValidFrom || assignment.ValidUntilUtc != window.ValidUntil) return null;
        }
        var receipt = new IdentityAccessGrantDecisionReceiptV1(row.CompanyId, row.Id, row.TargetUserId, row.RoleId,
            row.RequestedByUserId, row.RequestedAtUtc, expectedVersion, result.Version, name, key, hash, command.ActorId,
            command.OriginalCorrelationId, command.CompletedAtUtc, AccessDecisionSource(row), answer, IdempotencyHash.ComputePayload(answer));
        if (approve && decisionAudit is not null)
        {
            var artifact = await decisionAudit.ReadAsync(new(context.CompanyId, requestId, context.ActorId,
                command.ActorId, command.OriginalCorrelationId, key), token => DecisionReadFenceAsync(context, consumerFence, token),
                database.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken);
            await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
            if (artifact is not null && artifact.VersionBefore == expected && artifact.VersionAfter == expected + 1)
            {
                try
                {
                    var image = JsonNode.Parse(artifact.AfterJson) as JsonObject;
                    if (image?["appliedPermissionDigest"]?.GetValue<string>() is { Length: 64 } applied
                        && applied.All(x => x is >= '0' and <= '9' or >= 'A' and <= 'F')
                        && image["appliedPermissionCount"]?.GetValue<int>() is int count && count >= 0
                        && image["appliedRoleCode"]?.GetValue<string>() is { Length: > 0 } roleCode)
                    {
                        var actual = JsonSerializer.SerializeToNode(RequestImage(row), Json)!.AsObject();
                        actual["appliedPermissionDigest"] = applied; actual["appliedPermissionCount"] = count;
                        actual["appliedRoleCode"] = roleCode;
                        if (JsonNode.DeepEquals(image, actual)) receipt = receipt with { AppliedImpactDigest = applied, AppliedPermissionCount = count };
                    }
                }
                catch (JsonException) { }
                catch (InvalidOperationException) { }
            }
        }
        await DecisionReadFenceAsync(context, consumerFence, cancellationToken);
        return receipt;
    }
}
