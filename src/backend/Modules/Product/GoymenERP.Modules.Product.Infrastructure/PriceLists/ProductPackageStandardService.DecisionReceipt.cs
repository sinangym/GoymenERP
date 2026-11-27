using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

internal sealed partial class ProductPackageStandardService
{
    // Exact native key, command, actor and company; no history scan and no current-head substitution.
    private async Task<ProductPackageStandardDecisionReceiptV3?> ReadDecisionReceiptAsync(string command,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken token)
    {
        await RequireOwnedDecisionFenceAsync(ownedDecision!, token);
        var key = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var decisionId = VersionIdOf(command, metadata.CompanyId, key);
        var attempt = await database.PackageStandardAttempts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == metadata.CompanyId && row.CommandName == command && row.IdempotencyKeyHash == key
            && row.ActorId == metadata.ActorId && row.StatusCode == 204, token);
        await RequireOwnedDecisionFenceAsync(ownedDecision!, token);
        if (attempt is null) return null;
        var decision = await database.PackageStandardDecisions.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == metadata.CompanyId && row.DecisionId == decisionId, token);
        await RequireOwnedDecisionFenceAsync(ownedDecision!, token);
        if (decision is null || decision.DecidedBy != attempt.ActorId || decision.CorrelationId != attempt.CorrelationId
            || decision.Decision != (command == ApproveCommandName ? "Approved" : "Rejected")) return null;
        var header = await database.PackageStandardVersions.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == metadata.CompanyId && row.Version == decision.Version && row.RequiresApproval, token);
        await RequireOwnedDecisionFenceAsync(ownedDecision!, token);
        if (header is null) return null;
        var effective = decision.EffectiveFrom?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var hash = command == ApproveCommandName
            ? IdempotencyHash.ComputePayload(new { Version = decision.Version, EffectiveFrom = effective, Reason = decision.Reason })
            : IdempotencyHash.ComputePayload(new { Version = decision.Version, Reason = decision.Reason });
        if (!string.Equals(hash, attempt.RequestHash, StringComparison.Ordinal)) return null;
        return new(metadata.CompanyId, header.VersionId, header.Version, command, attempt.ActorId, key, hash,
            decision.Decision, effective, decision.Reason, decision.DecidedAtUtc.ToUniversalTime(), header.CreatedBy,
            header.CreatedAtUtc.ToUniversalTime());
    }
}
