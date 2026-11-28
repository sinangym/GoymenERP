using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool CompanyTaxIdVersion(string? text, out long version) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0
            && text == version.ToString(CultureInfo.InvariantCulture);
    private static bool ValidCompanyTaxIdRequest(string kind, WorkDecisionRequestV1 r) => kind != OrganizationCompanyTaxId
        || CompanyTaxIdVersion(r.ExpectedVersion, out var version) && (!r.Approve || version < long.MaxValue)
            && OrganizationCompanyTaxIdRules.IsValidNote(r.Reason)
            && r.InventoryCostCancellation is null && r.PostingAccountMappingSelection is null && r.ApprovedSourceCatalogSelection is null
            && r.AccountingPeriodReopening is null && r.NormalJournalDraft is null && r.ProductPackageStandard is null
            && r.ManufacturingPlanReapproval is null && r.InventoryTransferLoss is null && r.InventoryTransferOverage is null
            && r.LogisticsPlanExcess is null && r.SupplierReturnCostException is null && r.LogisticsCustomerReturn is null;
    private static bool ValidCompanyTaxIdProof(string kind, WorkApprovalDecisionSnapshotV1 s) => kind != OrganizationCompanyTaxId
        ? s.OrganizationCompanyTaxIdProof is null
        : s.OrganizationCompanyTaxIdProof is { } p && p.CompanyId == s.CompanyId && p.RequestId == s.Item.ItemId
            && CompanyTaxIdVersion(s.SourceVersion, out var version) && version == p.CurrentCompanyVersion
            && s.Pending == (p.Status == "pending") && OrganizationCompanyTaxIdDecisionBindingV1.MatchesDecisionProof(s.Item, p);
    private async Task CompanyTaxIdFenceAsync(WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RequireDecisionAuthorityAsync(OrganizationCompanyTaxId, OrganizationCompanyTaxIdPermissionsV1.Approve, s.CompanyId, actor, token);
        token.ThrowIfCancellationRequested();
    }
    private async Task<WorkOrganizationCompanyTaxIdDecisionPreviewV1> CompanyTaxIdHumanPreviewAsync(
        WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (organizationCompanyTaxIds is not IOrganizationCompanyTaxIdDecisionReadV1 reader) throw new WorkDecisionException(503);
        OrganizationCompanyTaxIdDecisionReadResultV1 answer;
        try { answer = await reader.ReadDecisionAsync(s.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer is null) throw new WorkDecisionException(503);
        if (answer.Status == OrganizationCompanyTaxIdDecisionReadStatusV1.Denied && answer.Detail is null) throw new WorkDecisionException(403);
        var p = s.OrganizationCompanyTaxIdProof!;
        if (answer.Status != OrganizationCompanyTaxIdDecisionReadStatusV1.Found || answer.Detail is not { } d
            || !OrganizationCompanyTaxIdDecisionBindingV1.ValidDetail(d) || d.CompanyId != s.CompanyId || d.Request.Id != s.Item.ItemId
            || d.CompanyVersion != p.CurrentCompanyVersion || d.CompanyVersionAtRequest != p.CompanyVersionAtRequest
            || d.Request.Status != p.Status || d.IsCurrent != p.IsCurrent || d.SourceFactsDigest != p.SourceFactsDigest
            || d.PreviewDigest != p.PreviewDigest || !OrganizationCompanyTaxIdDecisionBindingV1.MatchesDecisionItem(s.Item, d))
            throw new WorkDecisionException(503);
        return WorkOrganizationCompanyTaxIdDecisionPreviewV1.FromNative(d);
        Task Fence(CancellationToken ct) => CompanyTaxIdFenceAsync(s, actor, ct);
    }
    private async Task<WorkDecisionResultV1> DecideCompanyTaxIdAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 s, string actor, CancellationToken token)
    {
        if (organizationCompanyTaxIds is not IOrganizationCompanyTaxIdDecisionReadV1 reader) throw new WorkDecisionException(503);
        _ = CompanyTaxIdVersion(request.ExpectedVersion, out var version);
        var command = new DecideCompanyTaxIdChange(version, request.Approve ? "approve" : "reject", request.Reason);
        var metadata = new CommandMetadata(s.CompanyId, actor, correlation.CorrelationId, correlation.CausationId,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, key);
        CommandExecutionResult answer;
        // Exactly one native owned command. A 204 alone is not a receipt of this specific frozen decision.
        try { answer = await organizationCompanyTaxIds.DecideCompanyTaxIdChangeAsync(id, command, metadata, token); }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch (ArgumentException) { await Fence(token); throw new WorkDecisionException(400, "work_decision_invalid"); }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (answer?.Response is not { } response || !Enum.IsDefined(response.Result)
            || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId) || answer.OriginalCorrelationId.Length > 128
            || answer.OriginalCorrelationId != answer.OriginalCorrelationId.Trim()
            || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId) throw new WorkDecisionException(503);
        if (response.Result != CommandResult.Succeeded) RefuseCompanyTaxId(answer, command.ExpectedVersion);
        if (response != CommandResponses.For(CommandResult.Succeeded)) throw new WorkDecisionException(503);
        OrganizationCompanyTaxIdDecisionReceiptV1? receipt;
        try { receipt = await reader.ReadDecisionReceiptAsync(id, command, key, answer, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { throw new WorkDecisionException(403); }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        var p = s.OrganizationCompanyTaxIdProof!;
        var note = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        // Missing proof is an unknown outcome, never a rollback assertion; retry uses the original native key/body.
        if (receipt is not { } r || !OrganizationCompanyTaxIdDecisionBindingV1.MatchesOriginalDecisionItem(s.Item, p, r)
            || r.CompanyId != s.CompanyId || r.RequestId != id || r.ExpectedCompanyVersion != version
            || r.Decision != command.Decision || r.Note != note || r.ActorId != actor || r.DecidedBy != actor
            || r.CommandName != OrganizationCompanyTaxIdDecisionBindingV1.DecisionCommand
            || r.IdempotencyKeyHash != OrganizationCompanyTaxIdDecisionBindingV1.DecisionKeyHash(key)
            || r.PayloadHash != OrganizationCompanyTaxIdDecisionBindingV1.DecisionPayloadHash(s.CompanyId, id, command)
            || r.OriginalResultDigest != OrganizationCompanyTaxIdDecisionBindingV1.OriginalResultDigest(response)
            || r.OriginalCorrelationId != answer.OriginalCorrelationId
            || !answer.IsReplay && (!s.Pending || s.SourceVersion != request.ExpectedVersion || request.Approve && !p.IsCurrent))
            throw new WorkDecisionException(503);
        return new(s.CompanyId, OrganizationCompanyTaxId, id, request.Approve ? "approved" : "rejected", answer.IsReplay, r.OriginalCorrelationId);
        Task Fence(CancellationToken ct) => CompanyTaxIdFenceAsync(s, actor, ct);
    }
    private static void RefuseCompanyTaxId(CommandExecutionResult answer, long expectedVersion)
    {
        var r = answer.Response;
        var code = r.Result switch
        {
            CommandResult.NotFound => null,
            CommandResult.VersionConflict => OrganizationProblemCodesV1.CompanyTaxIdVersionConflict,
            CommandResult.IdempotencyConflict => OrganizationProblemCodesV1.CompanyTaxIdIdempotencyConflict,
            CommandResult.TaxIdInUse => OrganizationProblemCodesV1.CompanyTaxIdInUse,
            CommandResult.TaxIdChangeNotPending => OrganizationProblemCodesV1.CompanyTaxIdChangeNotPending,
            CommandResult.TaxIdChangeStale => OrganizationProblemCodesV1.CompanyTaxIdChangeStale,
            CommandResult.TaxIdSelfDecision => OrganizationProblemCodesV1.CompanyTaxIdSelfDecision,
            _ => throw new WorkDecisionException(503),
        };
        var canonical = r.Result == CommandResult.VersionConflict
            ? r.Conflict is { ExpectedVersion: var e, CurrentVersion: > 0 } conflict && e == expectedVersion
                ? CommandResponses.VersionConflict(e, conflict.CurrentVersion) : null
            : CommandResponses.For(r.Result);
        if (canonical is null || r != canonical) throw new WorkDecisionException(503);
        throw new WorkDecisionException(r.StatusCode, code, answer.IsReplay, answer.OriginalCorrelationId);
    }
}
