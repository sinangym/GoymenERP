using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool PackageVersion(string text, out long version) => long.TryParse(text, NumberStyles.None,
        CultureInfo.InvariantCulture, out version) && version > 0 && version.ToString(CultureInfo.InvariantCulture) == text;

    private static bool ValidProductPackageRequest(WorkDecisionRequestV1 request) =>
        PackageVersion(request.ExpectedVersion, out _) && !string.IsNullOrWhiteSpace(request.Reason)
        && request.Reason.Trim().Length <= ProductPackageStandardInputLimitsV2.ReasonMaximumLength
        && (request.Approve
            ? request.ProductPackageStandard is { } selection
                && selection.EffectiveFrom is { Length: 10 }
                && DateOnly.TryParseExact(selection.EffectiveFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)
            : request.ProductPackageStandard is null);

    private static bool ValidProductPackageProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != ProductPackageStandard) return snapshot.ProductPackageStandardProof is null;
        if (snapshot.ProductPackageStandardProof is not { Table: { } table } proof
            || proof.CompanyId != snapshot.CompanyId || proof.VersionId != snapshot.Item.ItemId
            || proof.Version != snapshot.SourceVersion || !PackageVersion(proof.Version, out _)
            || snapshot.Item.DocumentLabel != "v" + proof.Version
            || table.PreparedBy != snapshot.Item.PreparedBy || string.IsNullOrWhiteSpace(table.PreparedBy)
            || table.PreparedAtUtc != snapshot.Item.WaitingSinceUtc || table.PreparedAtUtc.Offset != TimeSpan.Zero
            || table.EntryCount < 0 || table.CustomerEntryCount < 0 || table.Entries is null
            || table.Entries.Count(entry => entry.CustomerId is null) != table.EntryCount
            || table.Entries.Count(entry => entry.CustomerId is not null) != table.CustomerEntryCount
            || table.Entries.Any(entry => entry.ProductId == Guid.Empty || entry.CustomerId == Guid.Empty || entry.PiecesPerPackage <= 0)
            || table.Entries.Select(entry => (entry.ProductId, entry.CustomerId)).Distinct().Count() != table.Entries.Count
            || snapshot.Item.Scope is not null || snapshot.Item.ExcludedDeciders is { Count: > 0 }) return false;
        return true;
    }

    private async Task<WorkDecisionResultV1> DecideProductPackageStandardAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (productPackageStandards is null || snapshot.ProductPackageStandardProof is not { } proof)
            throw new WorkDecisionException(503);
        if (request.ExpectedVersion != proof.Version) throw new WorkDecisionException(409, "work_decision_version_conflict");
        var version = long.Parse(proof.Version, CultureInfo.InvariantCulture);
        var reason = request.Reason!.Trim();
        var date = request.Approve ? request.ProductPackageStandard!.EffectiveFrom : null;
        var command = request.Approve ? "product.package-standards.approve.v3" : "product.package-standards.reject.v3";
        var metadata = new ProductPackageStandardCommandMetadataV2(snapshot.CompanyId, actor, correlation.CorrelationId,
            correlation.CausationId, "Web", null, null, key);
        try
        {
            var answer = request.Approve
                ? await productPackageStandards.ApproveAsync(version, new(date, request.Reason), metadata, token)
                : await productPackageStandards.RejectAsync(version, new(request.Reason), metadata, token);
            await RequireDecisionAuthorityAsync(ProductPackageStandard, ProductPackageStandardPermissionsV3.Approve,
                snapshot.CompanyId, actor, token);
            if (answer.Answer.StatusCode != 204)
            {
                if (answer.Answer.StatusCode is < 400 or > 599) throw new WorkDecisionException(503);
                throw new WorkDecisionException(answer.Answer.StatusCode, answer.Answer.Code, answer.IsReplay, answer.OriginalCorrelationId);
            }
            var hash = request.Approve
                ? IdempotencyHash.ComputePayload(new { Version = version, EffectiveFrom = date, Reason = reason })
                : IdempotencyHash.ComputePayload(new { Version = version, Reason = reason });
            if (answer.DecisionReceipt is not { } receipt || receipt.CompanyId != snapshot.CompanyId || receipt.VersionId != id
                || receipt.Version != version || receipt.CommandName != command || receipt.ActorId != actor
                || receipt.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key) || receipt.RequestHash != hash
                || receipt.Decision != (request.Approve ? "Approved" : "Rejected") || receipt.EffectiveFrom != date
                || receipt.Reason != reason || receipt.PreparedBy != proof.Table.PreparedBy
                || receipt.PreparedAtUtc != proof.Table.PreparedAtUtc || receipt.DecidedAtUtc.Offset != TimeSpan.Zero
                || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) || !snapshot.Pending && !answer.IsReplay
                || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId)
                throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, ProductPackageStandard, id, receipt.Decision, answer.IsReplay, answer.OriginalCorrelationId);
        }
        catch (ProductPackageStandardException error) { throw new WorkDecisionException(error.StatusCode, error.Code); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
    }
}
